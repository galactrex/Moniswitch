using Moniswitch;
using System.Windows.Forms;

var root = Path.Combine(Path.GetTempPath(), $"Moniswitch-smoke-{Guid.NewGuid():N}");
Directory.CreateDirectory(root);

try
{
    var freshStore = new SettingsStore(Path.Combine(root, "fresh-settings"));
    Require(freshStore.Current.PrivacyView, "privacy view is not enabled on first launch");
    Require(freshStore.Current.Version == 6, "fresh settings version is not current");
    Require(freshStore.Current.Profiles.Count == 0, "fresh settings contain a saved route");
    Require(
        freshStore.Current.InputSharing.WindowsScreenName == "windows-pc" &&
        freshStore.Current.InputSharing.LinuxScreenName == "linux-pc",
        "fresh input names are machine-specific");
    Require(
        freshStore.Current.InputSharing.StartWithWindows,
        "fresh settings do not preserve the restart-safe tray utility default");
    Require(
        StartupRegistration.BuildCommand(@"C:\Apps\Moniswitch.exe") == "\"C:\\Apps\\Moniswitch.exe\"",
        "Windows startup command is not quoted");
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

    var serverText = File.ReadAllText(serverConfig);
    Require(serverText.Contains("clipboardSharing = true"), "clipboard sharing is not enabled");
    Require(serverText.Contains("keystroke(Control+Alt+M)"), "shortcut was not written");

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
