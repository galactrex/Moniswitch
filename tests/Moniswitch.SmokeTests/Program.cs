using Moniswitch;
using System.Windows.Forms;

if (args.Contains("--route-status"))
{
    var store = new SettingsStore();
    using var monitors = new MonitorService();
    if (monitors.TryGetQuickToggle(store.Current.QuickToggleMonitorId, out var monitor))
    {
        Console.WriteLine($"Current source: {monitor.CurrentInput}; Windows mapping: {store.Current.QuickToggleInputA}; Linux mapping: {store.Current.QuickToggleInputB}");
    }
    return;
}

if (args.Contains("--probe-input"))
{
    var store = new SettingsStore();
    using var bridge = DeskflowBridge.StartServerIfEnabled(
        store.Current.InputSharing, store.DirectoryPath, store.Current.Hotkey.ToBinding());
    var connected = bridge is not null &&
                    await bridge.WaitForConnectedClientAsync(TimeSpan.FromSeconds(20));
    Console.WriteLine(connected ? "Input receiver connection confirmed (no input sent)." : "Input receiver connection not confirmed.");
    Environment.ExitCode = connected ? 0 : 1;
    return;
}

var root = Path.Combine(Path.GetTempPath(), $"Moniswitch-smoke-{Guid.NewGuid():N}");
Directory.CreateDirectory(root);

try
{
    Require(InputRouteTransaction.NeedsRemoteRecovery(true, 17, 17, true, false),
        "replacement desktop receiver did not recover the Linux route");
    Require(!InputRouteTransaction.NeedsRemoteRecovery(true, 15, 17, true, false),
        "reconnecting Linux stole input while Windows was selected");
    Require(!InputRouteTransaction.NeedsRemoteRecovery(true, 17, 17, false, false),
        "recovery ran before the receiver connected");
    Require(!InputRouteTransaction.NeedsRemoteRecovery(true, 17, 17, true, true),
        "healthy remote input was unnecessarily switched");
    Require(!InputRouteTransaction.NeedsRemoteRecovery(false, 17, 17, true, false),
        "disabled sharing recovered a route");
    Require(!InputRouteTransaction.NeedsRemoteRecovery(true, null, null, true, false),
        "missing route was treated as Linux");
    var routeEvents = new List<string>();
    await InputRouteTransaction.RunAsync(
        () => { routeEvents.Add("input"); return Task.CompletedTask; },
        () => { routeEvents.Add("display"); return Task.CompletedTask; },
        () => { routeEvents.Add("restore"); return Task.CompletedTask; });
    Require(string.Join(",", routeEvents) == "input,display", "route order is incorrect");
    foreach (var failure in new[] { "input", "display" })
    {
        routeEvents.Clear();
        try
        {
            await InputRouteTransaction.RunAsync(
                () => { routeEvents.Add("input"); return failure == "input"
                    ? Task.FromException(new InvalidOperationException("handoff failed")) : Task.CompletedTask; },
                () => { routeEvents.Add("display"); return Task.FromException(new InvalidOperationException("display failed")); },
                () => { routeEvents.Add("restore"); return Task.CompletedTask; });
            throw new Exception("route failure was swallowed");
        }
        catch (InvalidOperationException) { }
        Require(string.Join(",", routeEvents) == (failure == "input" ? "input,restore" : "input,display,restore"),
            "failed route did not restore input or moved the display without a handoff");
    }
    var freshStore = new SettingsStore(Path.Combine(root, "fresh-settings"));
    Require(freshStore.Current.PrivacyView, "privacy view is not enabled on first launch");
    Require(freshStore.Current.Version == 6, "fresh settings version is not current");
    Require(freshStore.Current.Profiles.Count == 0, "fresh settings contain a saved route");
    Require(
        freshStore.Current.InputSharing.WindowsScreenName == "windows-pc" &&
        freshStore.Current.InputSharing.LinuxScreenName == "linux-pc",
        "fresh input names are machine-specific");
    Require(
        !freshStore.Current.InputSharing.StartWithWindows,
        "fresh settings do not preserve the opt-in startup default");
    Require(
        StartupRegistration.BuildCommand(@"C:\Apps\Moniswitch.exe") == "\"C:\\Apps\\Moniswitch.exe\"",
        "Windows startup command is not quoted");
    Require(DisplayIdentity.NumberOf(@"\\.\DISPLAY1") == 1, "Windows display 1 was not parsed");
    Require(DisplayIdentity.NumberOf(@"\\.\DISPLAY12") == 12, "multi-digit display number was not parsed");
    Require(DisplayIdentity.NumberOf("unknown") == int.MaxValue, "invalid display name received a number");
    var rankedDisplays = DisplayIdentity.RankTargets([
        new DisplayPathIdentity(@"\\.\DISPLAY1", 0, 1, 0xA100),
        new DisplayPathIdentity(@"\\.\DISPLAY2", 0, 1, 0xA105),
        new DisplayPathIdentity(@"\\.\DISPLAY3", 0, 1, 0xA102)
    ]);
    Require(rankedDisplays[@"\\.\DISPLAY1"] == 1, "first Windows target rank was wrong");
    Require(rankedDisplays[@"\\.\DISPLAY3"] == 2, "second Windows target rank was wrong");
    Require(rankedDisplays[@"\\.\DISPLAY2"] == 3, "third Windows target rank was wrong");
    var routeInputs = new[]
    {
        new InputSource(0x0F, "DisplayPort 1"),
        new InputSource(0x11, "HDMI 1")
    };
    Require(
        InputSourceCatalog.AlternativeTo(routeInputs, 0x11) == 0x0F,
        "live HDMI did not fall back to DisplayPort");
    Require(
        InputSourceCatalog.AlternativeTo(routeInputs, 0x0F) == 0x11,
        "live DisplayPort did not prefer HDMI");
    Require(
        string.IsNullOrWhiteSpace(freshStore.Current.LanCanvas.LinuxHost) &&
        string.IsNullOrWhiteSpace(freshStore.Current.LanCanvas.LinuxUser) &&
        string.IsNullOrWhiteSpace(freshStore.Current.LanCanvas.SshKeyPath),
        "fresh LAN Canvas settings contain connection data");
    freshStore.Save();
    var freshText = File.ReadAllText(freshStore.FilePath);
    Require(
        !freshText.Contains(Environment.MachineName, StringComparison.OrdinalIgnoreCase),
        "fresh settings captured the Windows machine name");

    var serverConfig = DeskflowBridge.WriteServerConfiguration(
        root,
        "windows probe",
        "linux probe",
        new HotkeyBinding(Keys.M, true, true, false));
    var coreSettings = Path.Combine(root, "Deskflow.conf");
    DeskflowBridge.EnsureCoreSettings(coreSettings, root, "windows probe", serverConfig);

    Require(File.Exists(coreSettings), "core settings were not created");
    Require(File.Exists(Path.Combine(root, "tls", "deskflow.pem")), "TLS identity was not created");

    var coreText = File.ReadAllText(coreSettings);
    Require(coreText.Contains("tlsEnabled=true"), "TLS is not enabled");
    Require(coreText.Contains("toFile=false"), "file logging is not disabled");
    Require(coreText.Contains("externalConfig=true"), "external routing config is not enabled");
    Require(coreText.Contains("protocol=barrier"), "Waynergy-compatible protocol is not enabled");

    var legacyCoreSettings = Path.Combine(root, "legacy-deskflow.conf");
    File.WriteAllText(
        legacyCoreSettings,
        "[log]\nlevel=DEBUG\ntoFile=true\n\n[security]\ncheckPeerFingerprints=true\ntlsEnabled=false\n\n[server]\nexternalConfig=true\n");
    DeskflowBridge.OptimizeCoreSettings(legacyCoreSettings);
    var optimizedCoreText = File.ReadAllText(legacyCoreSettings);
    Require(optimizedCoreText.Contains("level=INFO"), "handoff status logging is not enabled");
    Require(optimizedCoreText.Contains("toFile=false"), "legacy file logging was not disabled");
    Require(
        optimizedCoreText.Contains("checkPeerFingerprints=false"),
        "legacy Deskflow settings still require a Waynergy client certificate");
    Require(optimizedCoreText.Contains("tlsEnabled=true"), "legacy Deskflow TLS was not restored");
    Require(
        optimizedCoreText.Contains("protocol=barrier"),
        "legacy Deskflow settings were not upgraded for Waynergy");

    var serverText = File.ReadAllText(serverConfig);
    Require(
        serverText.Contains("clipboardSharing = false"),
        "Waynergy clipboard traffic can still poison the input channel");
    Require(serverText.Contains("heartbeat = 3000"), "Waynergy heartbeat is not enabled");
    Require(
        serverText.Contains("keystroke(F23) = switchToScreen(windows-probe)"),
        "private Windows handoff signal was not written");
    Require(
        serverText.Contains("keystroke(F24) = switchToScreen(linux-probe)"),
        "private Linux handoff signal was not written");
    Require(
        !serverText.Contains("keystroke(Control+Alt+M)") &&
        !serverText.Contains("keystroke(Control+Alt+F24)"),
        "Deskflow still races Moniswitch for the physical shortcut");
    Require(
        !serverText.Contains("section: links"),
        "pointer-edge links can undo the explicit input target");

    var firewallRules = LanCanvasController.BuildFirewallRules("192.0.2.10");
    Require(
        firewallRules.Contains("allow from 192.0.2.10 to any port 47984:47990 proto tcp"),
        "Sunshine TCP range is missing");
    Require(
        firewallRules.Contains("allow from 192.0.2.10 to any port 48010 proto tcp"),
        "Sunshine RTSP port is missing");
    Require(
        firewallRules.Contains("allow from 192.0.2.10 to any port 47998:48000 proto udp"),
        "Sunshine UDP range is missing");

    var waynergyUnitPath = Path.Combine(
        Directory.GetCurrentDirectory(),
        "integration",
        "waynergy",
        "moniswitch-waynergy.service");
    var waynergyUnit = File.ReadAllText(waynergyUnitPath);
    Require(
        waynergyUnit.Contains("WAYLAND_DISPLAY=\"$${candidate##*/}\"", StringComparison.Ordinal),
        "systemd will consume the Wayland socket shell expansion");
    Require(
        waynergyUnit.Contains("--backend wlr --no-clip", StringComparison.Ordinal),
        "Waynergy desktop receiver still accepts unsafe clipboard payloads");
    Require(
        !waynergyUnit.Contains("WAYLAND_DISPLAY=\"${candidate##*/}\"", StringComparison.Ordinal),
        "unescaped Wayland socket shell expansion returned");

    var waynergyHandshakePatchPath = Path.Combine(
        Directory.GetCurrentDirectory(),
        "integration",
        "waynergy",
        "patch-waynergy-handshake.sh");
    var waynergyHandshakePatch = File.ReadAllText(waynergyHandshakePatchPath);
    Require(
        waynergyHandshakePatch.Contains("!context->m_hasReceivedHello", StringComparison.Ordinal),
        "Waynergy can send clipboard packets before HelloBack");
    Require(
        waynergyHandshakePatch.Contains("keep the update queued until then", StringComparison.Ordinal),
        "Waynergy handshake compatibility guard is missing");
    Require(
        waynergyHandshakePatch.Contains("buf.pos += flen", StringComparison.Ordinal),
        "Waynergy multi-format clipboard compatibility guard is missing");

    // TryGetServerFingerprint expects <settings>/deskflow/tls. Verify the
    // generated identity directly by placing it in that production layout.
    var productionRoot = Path.Combine(root, "production");
    var productionDeskflow = Path.Combine(productionRoot, "deskflow");
    Directory.CreateDirectory(productionDeskflow);
    var productionConfig = DeskflowBridge.WriteServerConfiguration(
        productionDeskflow,
        "windows-probe",
        "linux-probe",
        new HotkeyBinding(Keys.M, true, true, false));
    DeskflowBridge.EnsureCoreSettings(
        Path.Combine(productionDeskflow, "Deskflow.conf"),
        productionDeskflow,
        "windows-probe",
        productionConfig);
    var fingerprint = DeskflowBridge.TryGetServerFingerprint(productionRoot);
    Require(fingerprint is { Length: 64 }, "SHA-256 fingerprint was not produced");

    var deskflow = DeskflowBridge.FindExecutable();
    if (!string.IsNullOrWhiteSpace(deskflow))
    {
        var probeSettings = Path.Combine(productionDeskflow, "Deskflow.conf");
        var probeText = File.ReadAllText(probeSettings)
            .Replace("port=24800", "port=24802", StringComparison.Ordinal)
            .Replace("useHooks=true", "useHooks=false", StringComparison.Ordinal);
        File.WriteAllText(probeSettings, probeText);

        var startInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = deskflow,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };
        startInfo.ArgumentList.Add("server");
        startInfo.ArgumentList.Add("--new-instance");
        startInfo.ArgumentList.Add("--settings");
        startInfo.ArgumentList.Add(probeSettings);
        using var process = System.Diagnostics.Process.Start(startInfo)
            ?? throw new InvalidOperationException("Deskflow probe did not start");
        await Task.Delay(1200);
        if (process.HasExited)
        {
            throw new InvalidOperationException(
                $"Deskflow rejected the generated settings: {await process.StandardError.ReadToEndAsync()}");
        }

        process.Kill(entireProcessTree: true);
        await process.WaitForExitAsync();
    }

    Console.WriteLine("Moniswitch smoke tests passed.");
}
finally
{
    Directory.Delete(root, recursive: true);
}

static void Require(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}
