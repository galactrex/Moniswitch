using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace Moniswitch;

internal sealed partial class LanCanvasController : IDisposable
{
    public const string AllDisplaysTarget = "all";
    private const int SunshineHttpPort = 47989;
    private const int GwlStyle = -16;
    private const int GwlExStyle = -20;
    private const long WsCaption = 0x00C00000L;
    private const long WsThickFrame = 0x00040000L;
    private const long WsMinimizeBox = 0x00020000L;
    private const long WsMaximizeBox = 0x00010000L;
    private const long WsSysMenu = 0x00080000L;
    private const long WsPopup = 0x80000000L;
    private const long WsExAppWindow = 0x00040000L;
    private const long WsExToolWindow = 0x00000080L;
    private const long WsExNoActivate = 0x08000000L;
    private const uint SwpFrameChanged = 0x0020;
    private const uint SwpShowWindow = 0x0040;
    private const uint SwpNoActivate = 0x0010;

    private readonly SettingsStore _settingsStore;
    private readonly MonitorService _monitorService;
    private readonly SemaphoreSlim _operationLock = new(1, 1);
    private Process? _moonlight;
    private bool _disposed;

    public LanCanvasController(SettingsStore settingsStore, MonitorService monitorService)
    {
        _settingsStore = settingsStore;
        _monitorService = monitorService;
    }

    public event EventHandler<LanCanvasStatusEventArgs>? StatusChanged;

    public bool IsRunning => _moonlight is { HasExited: false };

    public IReadOnlyList<CanvasTarget> Targets
    {
        get
        {
            var monitors = _monitorService.Monitors;
            var displayNumbers = monitors
                .GroupBy(monitor => monitor.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group.First().DisplayNumber,
                    StringComparer.OrdinalIgnoreCase);
            var screens = Screen.AllScreens
                .OrderBy(screen => displayNumbers.TryGetValue(screen.DeviceName, out var number)
                    ? number
                    : DisplayIdentity.NumberOf(screen.DeviceName))
                .ThenBy(screen => screen.Bounds.Left)
                .ThenBy(screen => screen.Bounds.Top)
                .ToArray();
            var left = screens.Min(screen => screen.Bounds.Left);
            var top = screens.Min(screen => screen.Bounds.Top);
            var right = screens.Max(screen => screen.Bounds.Right);
            var bottom = screens.Max(screen => screen.Bounds.Bottom);
            var targets = new List<CanvasTarget>
            {
                new(
                    AllDisplaysTarget,
                    "ALL DISPLAYS / SPAN",
                    screens.Length,
                    new Rectangle(left, top, right - left, bottom - top))
            };

            for (var index = 0; index < screens.Length; index++)
            {
                var screen = screens[index];
                var monitor = monitors.FirstOrDefault(item => item.Bounds == screen.Bounds);
                var name = monitor?.Name;
                if (string.IsNullOrWhiteSpace(name))
                {
                    name = screen.DeviceName.Replace(@"\\.\", string.Empty, StringComparison.Ordinal);
                }

                var orientation = screen.Bounds.Height > screen.Bounds.Width
                    ? "PORTRAIT"
                    : "LANDSCAPE";
                targets.Add(new CanvasTarget(
                    screen.DeviceName,
                    $"{DisplayIdentity.NumberLabel(monitor?.DisplayNumber ?? DisplayIdentity.NumberOf(screen.DeviceName), index + 1)} / {name.ToUpperInvariant()} / {screen.Bounds.Width}×{screen.Bounds.Height} / {orientation}",
                    1,
                    screen.Bounds));
            }

            return targets;
        }
    }

    public CanvasGeometry Geometry
    {
        get
        {
            var selected = Targets.FirstOrDefault(target =>
                string.Equals(
                    target.Key,
                    _settingsStore.Current.LanCanvas.DisplayTarget,
                    StringComparison.OrdinalIgnoreCase));
            selected ??= Targets[0];
            return new CanvasGeometry(selected.ScreenCount, selected.Bounds);
        }
    }

    public CanvasGeometry ResolveGeometry(string? targetKey)
    {
        var key = string.IsNullOrWhiteSpace(targetKey) ? AllDisplaysTarget : targetKey;
        var target = Targets.FirstOrDefault(item =>
            string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("CANVAS TARGET IS NO LONGER AVAILABLE");
        return new CanvasGeometry(target.ScreenCount, target.Bounds);
    }

    public static string? FindMoonlight()
    {
        var candidates = new[]
        {
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Moniswitch",
                "tools",
                "Moonlight",
                "Moonlight.exe"),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "Moonlight Game Streaming",
                "Moonlight.exe"),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                "Moonlight Game Streaming",
                "Moonlight.exe")
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    public async Task PairAsync()
    {
        await _operationLock.WaitAsync();
        try
        {
            ThrowIfDisposed();
            var settings = ValidateSettings();
            var geometry = ResolveGeometry(settings.DisplayTarget);
            SetStatus("STARTING SENDER");
            await StartSenderAsync(settings, geometry);
            try
            {
                await EnsureSunshineReachableAsync(settings.LinuxHost);
                var pin = RandomNumberGenerator.GetInt32(1000, 10000).ToString("0000");
                SetStatus("PAIRING RECEIVER");

                using var pairProcess = StartMoonlight(
                    settings,
                    ["pair", "--pin", pin, settings.LinuxHost],
                    redirectOutput: true);
                await SubmitPairingPinAsync(settings, pairProcess, pin);

                // Moonlight 6.1 keeps its success/already-paired dialog open
                // until somebody clicks OK. The authenticated Sunshine API
                // response above is the hand-off point, so don't turn that
                // harmless modal into a 20-second timeout.
                var pairResult = await WaitForProcessOrCloseAsync(
                    pairProcess,
                    TimeSpan.FromSeconds(4));
                if (pairResult is { ExitCode: not 0 })
                {
                    throw new InvalidOperationException(CleanError(pairResult, "Moonlight pairing failed."));
                }

                SetStatus("PAIRED", success: true);
            }
            finally
            {
                await StopSenderAsync(settings);
            }
        }
        finally
        {
            _operationLock.Release();
        }
    }

    public async Task<string> GetFirewallRulesAsync()
    {
        await _operationLock.WaitAsync();
        try
        {
            ThrowIfDisposed();
            var settings = ValidateConnectionSettings();
            var result = await RunSshAsync(settings, "printf '%s\\n' \"$SSH_CONNECTION\"");
            if (result.ExitCode != 0)
            {
                throw new InvalidOperationException(CleanError(result));
            }

            var peerAddress = result.Output
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault();
            return BuildFirewallRules(peerAddress ?? string.Empty);
        }
        finally
        {
            _operationLock.Release();
        }
    }

    internal static string BuildFirewallRules(string peerAddress)
    {
        if (!IPAddress.TryParse(peerAddress, out var address))
        {
            throw new InvalidOperationException("WINDOWS LAN ADDRESS WAS NOT DETECTED");
        }

        var source = address.ToString();
        return string.Join(
            Environment.NewLine,
            $"sudo ufw allow from {source} to any port 47984:47990 proto tcp",
            $"sudo ufw allow from {source} to any port 48010 proto tcp",
            $"sudo ufw allow from {source} to any port 47998:48000 proto udp");
    }

    public async Task StartAsync()
    {
        await _operationLock.WaitAsync();
        try
        {
            ThrowIfDisposed();
            if (IsRunning)
            {
                SetStatus("CANVAS LIVE", success: true);
                return;
            }

            var settings = ValidateSettings();
            var geometry = ResolveGeometry(settings.DisplayTarget);
            SetStatus("STARTING SENDER");
            await StartSenderAsync(settings, geometry);
            try
            {
                await EnsureSunshineReachableAsync(settings.LinuxHost);

                SetStatus("OPENING CANVAS");
                _moonlight = StartMoonlight(
                    settings,
                    [
                        "stream",
                        settings.LinuxHost,
                        "Desktop",
                        "--resolution",
                        $"{geometry.Bounds.Width}x{geometry.Bounds.Height}",
                        "--fps",
                        settings.FramesPerSecond.ToString(),
                        "--bitrate",
                        settings.BitrateKbps.ToString(),
                        "--display-mode",
                        "windowed",
                        "--video-codec",
                        "HEVC",
                        "--video-decoder",
                        "hardware",
                        "--capture-system-keys",
                        "never",
                        "--no-multi-controller",
                        "--no-background-gamepad",
                        "--no-vsync",
                        "--no-game-optimization"
                    ],
                    redirectOutput: false);

                await SpanAcrossDisplaysAsync(
                    _moonlight,
                    geometry.Bounds,
                    TimeSpan.FromSeconds(25));
                _moonlight.EnableRaisingEvents = true;
                _moonlight.Exited += MoonlightExited;
                var displayWord = geometry.ScreenCount == 1 ? "DISPLAY" : "DISPLAYS";
                SetStatus($"CANVAS LIVE / {geometry.ScreenCount} {displayWord}", success: true);
            }
            catch
            {
                if (_moonlight is not null)
                {
                    TryStopProcess(_moonlight);
                    _moonlight.Dispose();
                    _moonlight = null;
                }

                await StopSenderAsync(settings);
                throw;
            }
        }
        finally
        {
            _operationLock.Release();
        }
    }

    public async Task StopAsync()
    {
        await _operationLock.WaitAsync();
        try
        {
            if (_disposed)
            {
                return;
            }

            SetStatus("STOPPING CANVAS");
            if (_moonlight is not null)
            {
                _moonlight.Exited -= MoonlightExited;
                TryStopProcess(_moonlight);
                _moonlight.Dispose();
                _moonlight = null;
            }

            var settings = _settingsStore.Current.LanCanvas;
            if (HasConnectionSettings(settings))
            {
                await StopSenderAsync(settings);
            }

            SetStatus("CANVAS OFF");
        }
        finally
        {
            _operationLock.Release();
        }
    }

    private async Task StartSenderAsync(LanCanvasSettings settings, CanvasGeometry geometry)
    {
        var result = await RunSshAsync(
            settings,
            $"~/.local/bin/moniswitch-canvas start {geometry.Bounds.Width} {geometry.Bounds.Height} {settings.FramesPerSecond}");
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(CleanError(result));
        }
    }

    private static async Task SubmitPairingPinAsync(
        LanCanvasSettings settings,
        Process pairProcess,
        string pin)
    {
        ProcessResult? lastResult = null;
        for (var attempt = 0; attempt < 12; attempt++)
        {
            await Task.Delay(400);
            if (pairProcess.HasExited)
            {
                break;
            }

            lastResult = await RunSshAsync(
                settings,
                $"~/.local/bin/moniswitch-canvas pin {pin}");
            if (lastResult.ExitCode == 0)
            {
                return;
            }
        }

        TryStopProcess(pairProcess);
        throw new InvalidOperationException(
            lastResult is null
                ? "MOONLIGHT CLOSED BEFORE PAIRING"
                : CleanError(lastResult, "PAIRING PIN WAS NOT ACCEPTED"));
    }

    private async Task StopSenderAsync(LanCanvasSettings settings)
    {
        try
        {
            _ = await RunSshAsync(settings, "~/.local/bin/moniswitch-canvas stop");
        }
        catch
        {
            // Shutdown is best effort. The on-demand service is not enabled.
        }
    }

    private static async Task EnsureSunshineReachableAsync(string host)
    {
        using var client = new TcpClient();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        try
        {
            await client.ConnectAsync(host, SunshineHttpPort, timeout.Token);
        }
        catch
        {
            throw new InvalidOperationException(
                "SUNSHINE BLOCKED / COPY UFW RULES, THEN RETRY");
        }
    }

    private static async Task<ProcessResult> RunSshAsync(LanCanvasSettings settings, string command)
    {
        var ssh = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "OpenSSH",
            "ssh.exe");
        if (!File.Exists(ssh))
        {
            throw new FileNotFoundException("WINDOWS OPENSSH CLIENT NOT FOUND", ssh);
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = ssh,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        if (!string.IsNullOrWhiteSpace(settings.SshKeyPath))
        {
            startInfo.ArgumentList.Add("-i");
            startInfo.ArgumentList.Add(Environment.ExpandEnvironmentVariables(settings.SshKeyPath));
        }

        startInfo.ArgumentList.Add("-o");
        startInfo.ArgumentList.Add("BatchMode=yes");
        startInfo.ArgumentList.Add("-o");
        startInfo.ArgumentList.Add("ConnectTimeout=5");
        startInfo.ArgumentList.Add("-o");
        startInfo.ArgumentList.Add("StrictHostKeyChecking=accept-new");
        startInfo.ArgumentList.Add($"{settings.LinuxUser}@{settings.LinuxHost}");
        startInfo.ArgumentList.Add(command);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("SSH DID NOT START");
        return await WaitForProcessAsync(process, TimeSpan.FromSeconds(20));
    }

    private static Process StartMoonlight(
        LanCanvasSettings settings,
        IReadOnlyList<string> arguments,
        bool redirectOutput)
    {
        var executable = settings.MoonlightExecutablePath;
        if (string.IsNullOrWhiteSpace(executable) || !File.Exists(executable))
        {
            executable = FindMoonlight();
        }

        if (string.IsNullOrWhiteSpace(executable))
        {
            throw new FileNotFoundException("MOONLIGHT RECEIVER NOT FOUND");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = Path.GetDirectoryName(executable) ?? Environment.CurrentDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = redirectOutput,
            RedirectStandardError = redirectOutput
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return Process.Start(startInfo)
            ?? throw new InvalidOperationException("MOONLIGHT DID NOT START");
    }

    private static async Task<ProcessResult> WaitForProcessAsync(Process process, TimeSpan timeout)
    {
        Task<string>? outputTask = process.StartInfo.RedirectStandardOutput
            ? process.StandardOutput.ReadToEndAsync()
            : null;
        Task<string>? errorTask = process.StartInfo.RedirectStandardError
            ? process.StandardError.ReadToEndAsync()
            : null;

        using var cancellation = new CancellationTokenSource(timeout);
        try
        {
            await process.WaitForExitAsync(cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            TryStopProcess(process);
            throw new TimeoutException("REMOTE OPERATION TIMED OUT");
        }

        return new ProcessResult(
            process.ExitCode,
            outputTask is null ? string.Empty : await outputTask,
            errorTask is null ? string.Empty : await errorTask);
    }

    private static async Task<ProcessResult?> WaitForProcessOrCloseAsync(
        Process process,
        TimeSpan timeout)
    {
        Task<string>? outputTask = process.StartInfo.RedirectStandardOutput
            ? process.StandardOutput.ReadToEndAsync()
            : null;
        Task<string>? errorTask = process.StartInfo.RedirectStandardError
            ? process.StandardError.ReadToEndAsync()
            : null;

        using var cancellation = new CancellationTokenSource(timeout);
        try
        {
            await process.WaitForExitAsync(cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            TryStopProcess(process);
            return null;
        }

        return new ProcessResult(
            process.ExitCode,
            outputTask is null ? string.Empty : await outputTask,
            errorTask is null ? string.Empty : await errorTask);
    }

    private static async Task<IntPtr> WaitForStreamWindowAsync(Process process, TimeSpan timeout)
    {
        var started = Environment.TickCount64;
        while (Environment.TickCount64 - started < timeout.TotalMilliseconds)
        {
            if (process.HasExited)
            {
                throw new InvalidOperationException("MOONLIGHT CLOSED BEFORE THE STREAM STARTED");
            }

            process.Refresh();
            if (process.MainWindowHandle != IntPtr.Zero &&
                process.MainWindowTitle.EndsWith(" - Moonlight", StringComparison.OrdinalIgnoreCase))
            {
                return process.MainWindowHandle;
            }

            await Task.Delay(100);
        }

        throw new TimeoutException("MOONLIGHT STREAM WINDOW TIMED OUT");
    }

    private static async Task SpanAcrossDisplaysAsync(
        Process process,
        Rectangle bounds,
        TimeSpan timeout)
    {
        _ = await WaitForStreamWindowAsync(process, timeout);

        // Moonlight applies its saved window geometry while the stream changes
        // from the connection view to the decoder view. Reapply the canvas
        // bounds until they remain exact, then expose CANVAS LIVE.
        var started = Environment.TickCount64;
        var stableSamples = 0;
        while (Environment.TickCount64 - started < TimeSpan.FromSeconds(5).TotalMilliseconds)
        {
            if (process.HasExited)
            {
                throw new InvalidOperationException("MOONLIGHT CLOSED BEFORE THE STREAM STARTED");
            }

            process.Refresh();
            var window = process.MainWindowHandle;
            if (window != IntPtr.Zero)
            {
                SpanAcrossDisplays(window, bounds);
                await Task.Delay(250);
                stableSamples = GetWindowRect(window, out var actual) &&
                    actual.Left == bounds.Left &&
                    actual.Top == bounds.Top &&
                    actual.Right == bounds.Right &&
                    actual.Bottom == bounds.Bottom
                        ? stableSamples + 1
                        : 0;
                if (stableSamples >= 3)
                {
                    return;
                }
            }
            else
            {
                stableSamples = 0;
                await Task.Delay(100);
            }
        }

        throw new InvalidOperationException("CANVAS WINDOW DID NOT HOLD THE DISPLAY BOUNDS");
    }

    private static void SpanAcrossDisplays(IntPtr window, Rectangle bounds)
    {
        var style = GetWindowLongPtr(window, GwlStyle).ToInt64();
        style &= ~(WsCaption | WsThickFrame | WsMinimizeBox | WsMaximizeBox | WsSysMenu);
        style |= WsPopup;
        _ = SetWindowLongPtr(window, GwlStyle, new IntPtr(style));

        var exStyle = GetWindowLongPtr(window, GwlExStyle).ToInt64();
        exStyle &= ~WsExAppWindow;
        exStyle |= WsExToolWindow | WsExNoActivate;
        _ = SetWindowLongPtr(window, GwlExStyle, new IntPtr(exStyle));

        if (!SetWindowPos(
                window,
                IntPtr.Zero,
                bounds.Left,
                bounds.Top,
                bounds.Width,
                bounds.Height,
                SwpFrameChanged | SwpShowWindow | SwpNoActivate))
        {
            throw new InvalidOperationException("CANVAS WINDOW COULD NOT SPAN THE DISPLAYS");
        }
    }

    private LanCanvasSettings ValidateSettings()
    {
        var settings = ValidateConnectionSettings();

        settings.FramesPerSecond = Math.Clamp(settings.FramesPerSecond, 30, 120);
        settings.BitrateKbps = Math.Clamp(settings.BitrateKbps, 10000, 150000);
        var moonlight = settings.MoonlightExecutablePath;
        if (string.IsNullOrWhiteSpace(moonlight) || !File.Exists(moonlight))
        {
            settings.MoonlightExecutablePath = FindMoonlight();
        }

        if (string.IsNullOrWhiteSpace(settings.MoonlightExecutablePath))
        {
            throw new InvalidOperationException("MOONLIGHT RECEIVER NOT FOUND");
        }

        return settings;
    }

    private LanCanvasSettings ValidateConnectionSettings()
    {
        var settings = _settingsStore.Current.LanCanvas;
        if (!HasConnectionSettings(settings) ||
            !SafeHost().IsMatch(settings.LinuxHost) ||
            !SafeUser().IsMatch(settings.LinuxUser))
        {
            throw new InvalidOperationException("LAN CANVAS CONNECTION IS INCOMPLETE");
        }

        if (!string.IsNullOrWhiteSpace(settings.SshKeyPath) &&
            !File.Exists(Environment.ExpandEnvironmentVariables(settings.SshKeyPath)))
        {
            throw new InvalidOperationException("SSH KEY NOT FOUND");
        }

        return settings;
    }

    private static bool HasConnectionSettings(LanCanvasSettings settings) =>
        !string.IsNullOrWhiteSpace(settings.LinuxHost) &&
        !string.IsNullOrWhiteSpace(settings.LinuxUser);

    private static string CleanError(ProcessResult result, string fallback = "REMOTE OPERATION FAILED")
    {
        var text = string.IsNullOrWhiteSpace(result.Error) ? result.Output : result.Error;
        var lastLine = text
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .LastOrDefault();
        return string.IsNullOrWhiteSpace(lastLine) ? fallback : lastLine;
    }

    private void MoonlightExited(object? sender, EventArgs eventArgs)
    {
        SetStatus("CANVAS OFF");
        _ = StopAfterMoonlightExitAsync();
    }

    private async Task StopAfterMoonlightExitAsync()
    {
        try
        {
            await StopAsync();
        }
        catch
        {
            // The next explicit start rechecks and repairs the remote state.
        }
    }

    private void SetStatus(string message, bool success = false, bool error = false) =>
        StatusChanged?.Invoke(this, new LanCanvasStatusEventArgs(message, success, error));

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private static void TryStopProcess(Process process)
    {
        try
        {
            if (process.HasExited)
            {
                return;
            }

            if (process.CloseMainWindow() && process.WaitForExit(1500))
            {
                return;
            }

            process.Kill(entireProcessTree: true);
            process.WaitForExit(1500);
        }
        catch
        {
            // The process may exit between each check.
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            Task.Run(StopAsync).GetAwaiter().GetResult();
        }
        catch
        {
            // App shutdown cannot wait indefinitely for a remote machine.
        }
        finally
        {
            _disposed = true;
            _operationLock.Dispose();
        }
    }

    [GeneratedRegex(@"^[A-Za-z0-9._:-]+$")]
    private static partial Regex SafeHost();

    [GeneratedRegex(@"^[A-Za-z0-9._-]+$")]
    private static partial Regex SafeUser();

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr64(IntPtr window, int index);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern IntPtr GetWindowLongPtr32(IntPtr window, int index);

    private static IntPtr GetWindowLongPtr(IntPtr window, int index) => IntPtr.Size == 8
        ? GetWindowLongPtr64(window, index)
        : GetWindowLongPtr32(window, index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr64(IntPtr window, int index, IntPtr newValue);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern IntPtr SetWindowLongPtr32(IntPtr window, int index, IntPtr newValue);

    private static IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr newValue) => IntPtr.Size == 8
        ? SetWindowLongPtr64(window, index, newValue)
        : SetWindowLongPtr32(window, index, newValue);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr window,
        IntPtr insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr window, out NativeRectangle rectangle);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRectangle
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}

internal sealed record CanvasGeometry(int ScreenCount, Rectangle Bounds);
internal sealed record CanvasTarget(string Key, string Name, int ScreenCount, Rectangle Bounds)
{
    public override string ToString() => Name;
}
internal sealed record ProcessResult(int ExitCode, string Output, string Error);

internal sealed class LanCanvasStatusEventArgs(string message, bool success, bool error) : EventArgs
{
    public string Message { get; } = message;
    public bool Success { get; } = success;
    public bool Error { get; } = error;
}
