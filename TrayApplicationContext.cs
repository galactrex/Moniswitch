using System.Drawing;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Moniswitch;

internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly MonitorService _monitorService;
    private readonly SettingsStore _settingsStore;
    private readonly LanCanvasController _lanCanvasController;
    private readonly ContextMenuStrip _trayMenu;
    private readonly NotifyIcon _trayIcon;
    private readonly Icon _trayAppIcon;
    private SettingsForm? _settingsForm;
    private HotkeyWindow _hotkeyWindow;
    private DeskflowServerController? _deskflowServer;
    private bool _switching;

    public TrayApplicationContext()
    {
        _monitorService = new MonitorService(refreshImmediately: false);
        _settingsStore = new SettingsStore();
        StartupRegistration.Apply(_settingsStore.Current.InputSharing.StartWithWindows);
        _lanCanvasController = new LanCanvasController(_settingsStore, _monitorService);
        _lanCanvasController.StatusChanged += (_, _) =>
        {
            if (_trayMenu is not null && _trayMenu.IsHandleCreated)
            {
                _trayMenu.BeginInvoke(BuildTrayMenu);
            }
        };
        _deskflowServer = DeskflowBridge.StartServerIfEnabled(
            _settingsStore.Current.InputSharing,
            _settingsStore.DirectoryPath,
            _settingsStore.Current.Hotkey.ToBinding());

        _trayMenu = new ContextMenuStrip
        {
            BackColor = UiTheme.SurfaceRaised
        };
        UiTheme.StyleMenu(_trayMenu);
        _trayMenu.Opening += (_, _) => BuildTrayMenu();
        _ = _trayMenu.Handle;

        _trayAppIcon = AppIcon.Create();
        _trayIcon = new NotifyIcon
        {
            Icon = _trayAppIcon,
            Text = "Moniswitch",
            ContextMenuStrip = _trayMenu,
            Visible = true
        };
        _trayIcon.MouseClick += (_, eventArgs) =>
        {
            if (eventArgs.Button == MouseButtons.Left)
            {
                OpenSettings();
            }
        };

        _hotkeyWindow = CreateHotkeyWindow();

        BuildTrayMenu();
        var hotkeyText = _settingsStore.Current.Hotkey.ToBinding().DisplayText;
        if (!_hotkeyWindow.IsRegistered)
        {
            _trayIcon.ShowBalloonTip(
                3000,
                "Shortcut unavailable",
                $"{hotkeyText} could not be monitored. Display routing still works from Moniswitch.",
                ToolTipIcon.Warning);
        }

        OpenSettings();
        Application.Idle += InitializeMonitorsOnIdle;
    }

    private async void InitializeMonitorsOnIdle(object? sender, EventArgs eventArgs)
    {
        Application.Idle -= InitializeMonitorsOnIdle;
        _switching = true;
        try
        {
            await _monitorService.RefreshAsync();
            EnsureQuickSwitchDefaults();
            _settingsForm?.ReloadFromService();
            BuildTrayMenu();
        }
        catch (Exception exception)
        {
            _trayIcon.ShowBalloonTip(3500, "Display scan failed", exception.Message, ToolTipIcon.Error);
        }
        finally
        {
            _switching = false;
        }
    }

    private void OpenSettings()
    {
        if (_settingsForm is null || _settingsForm.IsDisposed)
        {
            var form = new SettingsForm(_monitorService, _settingsStore, _lanCanvasController);
            form.ConfigurationChanged += (_, _) => BuildTrayMenu();
            form.HotkeyChanged += (_, _) => ApplyHotkeyConfiguration();
            form.InputSharingChanged += (_, _) => ApplyInputSharingConfiguration();
            form.FormClosed += (_, _) =>
            {
                if (ReferenceEquals(_settingsForm, form))
                {
                    _settingsForm = null;
                    Application.Idle += ReleaseClosedUiOnIdle;
                }
            };
            _settingsForm = form;
            SyncInputSharingStatus();
        }

        _settingsForm.ShowAndActivate();
    }

    public void ActivateMainWindow()
    {
        try
        {
            if (_trayMenu.IsHandleCreated && !_trayMenu.IsDisposed)
            {
                _trayMenu.BeginInvoke((Action)OpenSettings);
            }
        }
        catch (InvalidOperationException)
        {
            // Shutdown can dispose the hidden message window after the signal.
        }
    }

    private static void ReleaseClosedUiOnIdle(object? sender, EventArgs eventArgs)
    {
        Application.Idle -= ReleaseClosedUiOnIdle;
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: false);
        GC.WaitForPendingFinalizers();
        _ = EmptyWorkingSet(Process.GetCurrentProcess().Handle);
    }

    [DllImport("psapi.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EmptyWorkingSet(IntPtr process);

    private HotkeyWindow CreateHotkeyWindow()
    {
        var window = new HotkeyWindow(_settingsStore.Current.Hotkey.ToBinding());
        window.HotkeyPressed += async (_, _) => await QuickToggleAsync();
        return window;
    }

    private void ApplyHotkeyConfiguration()
    {
        // Deskflow installs its own keyboard hook. Restart it first, then place
        // Moniswitch's non-consuming observer ahead of it for both directions.
        _hotkeyWindow.Dispose();
        RestartInputSharing();
        _hotkeyWindow = CreateHotkeyWindow();
        BuildTrayMenu();
    }

    private void ApplyInputSharingConfiguration()
    {
        StartupRegistration.Apply(_settingsStore.Current.InputSharing.StartWithWindows);
        RestartInputSharing();
        BuildTrayMenu();
    }

    private void RestartInputSharing()
    {
        _deskflowServer?.Dispose();
        _deskflowServer = DeskflowBridge.StartServerIfEnabled(
            _settingsStore.Current.InputSharing,
            _settingsStore.DirectoryPath,
            _settingsStore.Current.Hotkey.ToBinding());
        SyncInputSharingStatus();
    }

    private void SyncInputSharingStatus()
    {
        var status = _deskflowServer?.StatusText ?? "Input sharing off";
        var error = _settingsStore.Current.InputSharing.Enabled &&
                    _deskflowServer is { IsAvailable: false };
        _settingsForm?.SetInputSharingStatus(status, error);
    }

    private void EnsureQuickSwitchDefaults()
    {
        var settings = _settingsStore.Current;
        if (!_monitorService.TryGetQuickToggle(settings.QuickToggleMonitorId, out var monitor))
        {
            return;
        }

        var validA = settings.QuickToggleMonitorId == monitor.Id &&
                     settings.QuickToggleInputA.HasValue &&
                     monitor.Inputs.Any(input => input.Code == settings.QuickToggleInputA.Value);
        var inputA = validA
            ? settings.QuickToggleInputA!.Value
            : monitor.CurrentInput ?? monitor.Inputs[0].Code;
        var validB = settings.QuickToggleMonitorId == monitor.Id &&
                     settings.QuickToggleInputB.HasValue &&
                     settings.QuickToggleInputB.Value != inputA &&
                     monitor.Inputs.Any(input => input.Code == settings.QuickToggleInputB.Value);

        settings.QuickToggleMonitorId = monitor.Id;
        settings.QuickToggleInputA = inputA;
        settings.QuickToggleInputB = validB
            ? settings.QuickToggleInputB
            : InputSourceCatalog.AlternativeTo(monitor.Inputs, inputA)
              ?? throw new InvalidOperationException("Quick Route needs two different monitor inputs.");
        _settingsStore.Save();
    }

    private void BuildTrayMenu()
    {
        if (_trayMenu is null)
        {
            return;
        }

        _trayMenu.Items.Clear();
        _trayMenu.Items.Add("Open Moniswitch", null, (_, _) => OpenSettings());

        var quickItem = new ToolStripMenuItem("Quick switch now")
        {
            ShortcutKeyDisplayString = _settingsStore.Current.Hotkey.ToBinding().DisplayText
        };
        quickItem.Click += async (_, _) => await QuickToggleAsync();
        _trayMenu.Items.Add(quickItem);

        var orderedMonitors = _monitorService.Monitors
            .OrderBy(item => item.DisplayNumber)
            .ThenBy(item => item.Bounds.Left)
            .ThenBy(item => item.Bounds.Top)
            .ToArray();
        var hotkeyDisplay = new ToolStripMenuItem("Hotkey display");
        for (var index = 0; index < orderedMonitors.Length; index++)
        {
            var monitor = orderedMonitors[index];
            if (!monitor.DdcAvailable || monitor.Inputs.Count < 2)
            {
                continue;
            }

            var capturedMonitor = monitor;
            var targetItem = new ToolStripMenuItem(
                $"{DisplayIdentity.NumberLabel(monitor.DisplayNumber, index + 1)} / {monitor.Name}")
            {
                Checked = monitor.Id == _settingsStore.Current.QuickToggleMonitorId
            };
            targetItem.Click += (_, _) => SelectQuickSwitchMonitor(capturedMonitor);
            hotkeyDisplay.DropDownItems.Add(targetItem);
        }

        hotkeyDisplay.Enabled = hotkeyDisplay.DropDownItems.Count > 0;
        _trayMenu.Items.Add(hotkeyDisplay);

        if (_settingsStore.Current.InputSharing.Enabled)
        {
            _trayMenu.Items.Add(new ToolStripMenuItem(
                _deskflowServer?.StatusText ?? "Input sharing enabled")
            {
                Enabled = false
            });
        }

        var canvasItem = new ToolStripMenuItem("LAN Canvas");
        canvasItem.DropDownItems.Add(
            _lanCanvasController.IsRunning ? "Canvas live" : "Canvas off",
            null,
            (_, _) => OpenLanCanvas());
        canvasItem.DropDownItems.Add("Start", null, async (_, _) => await StartCanvasAsync());
        canvasItem.DropDownItems.Add("Stop", null, async (_, _) => await StopCanvasAsync());
        canvasItem.DropDownItems.Add("Settings", null, (_, _) => OpenLanCanvas());
        _trayMenu.Items.Add(canvasItem);

        if (_settingsStore.Current.Profiles.Count > 0)
        {
            var profiles = new ToolStripMenuItem("Saved routes");
            var savedRoutes = _settingsStore.Current.Profiles.OrderBy(item => item.Name).ToArray();
            for (var index = 0; index < savedRoutes.Length; index++)
            {
                var profile = savedRoutes[index];
                var capturedProfile = profile;
                var label = _settingsStore.Current.PrivacyView
                    ? $"Saved route {index + 1:00}"
                    : profile.Name;
                profiles.DropDownItems.Add(label, null, async (_, _) =>
                    await ApplyProfileAsync(capturedProfile));
            }

            _trayMenu.Items.Add(profiles);
        }

        var displays = new ToolStripMenuItem("Displays");
        for (var index = 0; index < orderedMonitors.Length; index++)
        {
            var monitor = orderedMonitors[index];
            var monitorLabel =
                $"{DisplayIdentity.NumberLabel(monitor.DisplayNumber, index + 1)} / {monitor.Name}";
            var monitorItem = new ToolStripMenuItem(monitorLabel)
            {
                Enabled = monitor.DdcAvailable && monitor.Inputs.Count > 0
            };
            foreach (var input in monitor.Inputs)
            {
                var monitorId = monitor.Id;
                var inputCode = input.Code;
                var inputItem = new ToolStripMenuItem(input.Name)
                {
                    Checked = monitor.CurrentInput == input.Code
                };
                inputItem.Click += async (_, _) => await SwitchOneAsync(monitorId, inputCode);
                monitorItem.DropDownItems.Add(inputItem);
            }

            displays.DropDownItems.Add(monitorItem);
        }

        _trayMenu.Items.Add(displays);
        _trayMenu.Items.Add(new ToolStripSeparator());
        _trayMenu.Items.Add("Refresh displays", null, async (_, _) => await RefreshAsync());
        _trayMenu.Items.Add("Exit Moniswitch", null, (_, _) => ExitThread());
    }

    private void SelectQuickSwitchMonitor(MonitorSnapshot monitor)
    {
        var settings = _settingsStore.Current;
        var keepSources = settings.QuickToggleMonitorId == monitor.Id &&
                          settings.QuickToggleInputA.HasValue &&
                          settings.QuickToggleInputB.HasValue &&
                          settings.QuickToggleInputA != settings.QuickToggleInputB &&
                          monitor.Inputs.Any(input => input.Code == settings.QuickToggleInputA.Value) &&
                          monitor.Inputs.Any(input => input.Code == settings.QuickToggleInputB.Value);

        settings.QuickToggleMonitorId = monitor.Id;
        if (!keepSources)
        {
            settings.QuickToggleInputA = monitor.CurrentInput ?? monitor.Inputs[0].Code;
            settings.QuickToggleInputB = monitor.Inputs.FirstOrDefault(input =>
                    input.Code != settings.QuickToggleInputA &&
                    input.Name.StartsWith("HDMI", StringComparison.OrdinalIgnoreCase))?.Code
                ?? monitor.Inputs.First(input => input.Code != settings.QuickToggleInputA).Code;
        }

        _settingsStore.Save();
        _settingsForm?.ReloadFromService();
        BuildTrayMenu();
        _trayIcon.ShowBalloonTip(
            1200,
            "Hotkey display selected",
            monitor.Name,
            ToolTipIcon.None);
    }

    private void OpenLanCanvas()
    {
        OpenSettings();
        _settingsForm?.ShowLanCanvas();
    }

    private async Task StartCanvasAsync()
    {
        try
        {
            await _lanCanvasController.StartAsync();
        }
        catch (Exception exception)
        {
            _trayIcon.ShowBalloonTip(4000, "LAN Canvas", exception.Message, ToolTipIcon.Error);
        }
        finally
        {
            BuildTrayMenu();
        }
    }

    private async Task StopCanvasAsync()
    {
        try
        {
            await _lanCanvasController.StopAsync();
        }
        catch (Exception exception)
        {
            _trayIcon.ShowBalloonTip(4000, "LAN Canvas", exception.Message, ToolTipIcon.Error);
        }
        finally
        {
            BuildTrayMenu();
        }
    }

    private async Task QuickToggleAsync()
    {
        var settings = _settingsStore.Current;
        if (_switching ||
            !settings.QuickToggleInputA.HasValue ||
            !settings.QuickToggleInputB.HasValue ||
            !_monitorService.TryGetQuickToggle(settings.QuickToggleMonitorId, out var monitor))
        {
            return;
        }

        var next = monitor.CurrentInput == settings.QuickToggleInputA
            ? settings.QuickToggleInputB.Value
            : settings.QuickToggleInputA.Value;
        await SwitchOneAsync(monitor.Id, next);
    }

    private async Task SwitchOneAsync(string monitorId, byte input)
    {
        if (_switching)
        {
            return;
        }

        _switching = true;
        try
        {
            await _monitorService.SwitchAsync(monitorId, input);
            var monitor = _monitorService.Monitors.First(item => item.Id == monitorId);
            _trayIcon.Text = $"Moniswitch - {monitor.Name}: {InputSourceCatalog.NameOf(input)}";
            _trayIcon.ShowBalloonTip(
                1000,
                "Moniswitch",
                $"{monitor.Name} -> {InputSourceCatalog.NameOf(input)}",
                ToolTipIcon.None);
            _settingsForm?.SyncFromService();
            BuildTrayMenu();
        }
        catch (Exception exception)
        {
            _trayIcon.ShowBalloonTip(
                3500,
                "Moniswitch could not switch the display",
                exception.Message,
                ToolTipIcon.Error);
        }
        finally
        {
            _switching = false;
        }
    }

    private async Task ApplyProfileAsync(SwitchProfile profile)
    {
        if (_switching)
        {
            return;
        }

        _switching = true;
        try
        {
            await _monitorService.SwitchManyAsync(profile.Assignments);
            _settingsForm?.SyncFromService();
            BuildTrayMenu();
            _trayIcon.ShowBalloonTip(1200, "Moniswitch", $"{profile.Name} applied", ToolTipIcon.None);
        }
        catch (Exception exception)
        {
            _trayIcon.ShowBalloonTip(3500, "Profile incomplete", exception.Message, ToolTipIcon.Warning);
        }
        finally
        {
            _switching = false;
        }
    }

    private async Task RefreshAsync()
    {
        if (_switching)
        {
            return;
        }

        _switching = true;
        try
        {
            await _monitorService.RefreshAsync();
            EnsureQuickSwitchDefaults();
            _settingsForm?.ReloadFromService();
            BuildTrayMenu();
        }
        catch (Exception exception)
        {
            _trayIcon.ShowBalloonTip(3500, "Display refresh failed", exception.Message, ToolTipIcon.Error);
        }
        finally
        {
            _switching = false;
        }
    }

    protected override void ExitThreadCore()
    {
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        _trayAppIcon.Dispose();
        _trayMenu.Dispose();
        _hotkeyWindow.Dispose();
        _deskflowServer?.Dispose();
        _lanCanvasController.Dispose();
        _settingsForm?.Dispose();
        _monitorService.Dispose();
        base.ExitThreadCore();
    }
}

internal sealed class HotkeyWindow : IDisposable
{
    private const int WhKeyboardLl = 13;
    private const int WmKeyDown = 0x0100;
    private const int WmKeyUp = 0x0101;
    private const int WmSysKeyDown = 0x0104;
    private const int WmSysKeyUp = 0x0105;
    private readonly LowLevelKeyboardProc _keyboardProc;
    private readonly IntPtr _keyboardHook;
    private readonly HotkeyBinding _binding;
    private bool _controlDown;
    private bool _altDown;
    private bool _shiftDown;
    private long _lastHotkeyTick;

    public HotkeyWindow(HotkeyBinding binding)
    {
        _binding = binding;
        _keyboardProc = KeyboardHookCallback;
        _keyboardHook = SetWindowsHookEx(
            WhKeyboardLl,
            _keyboardProc,
            GetModuleHandle(null),
            0);
    }

    public bool IsRegistered => _keyboardHook != IntPtr.Zero;
    public event EventHandler? HotkeyPressed;

    private IntPtr KeyboardHookCallback(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0)
        {
            var message = wParam.ToInt32();
            var key = Marshal.ReadInt32(lParam);
            var isDown = message is WmKeyDown or WmSysKeyDown;
            var isUp = message is WmKeyUp or WmSysKeyUp;

            if (key is (int)Keys.ControlKey or (int)Keys.LControlKey or (int)Keys.RControlKey)
            {
                _controlDown = isDown || !isUp && _controlDown;
            }
            else if (key is (int)Keys.Menu or (int)Keys.LMenu or (int)Keys.RMenu)
            {
                _altDown = isDown || !isUp && _altDown;
            }
            else if (key is (int)Keys.ShiftKey or (int)Keys.LShiftKey or (int)Keys.RShiftKey)
            {
                _shiftDown = isDown || !isUp && _shiftDown;
            }
            else if (key == (int)_binding.Key &&
                     isDown &&
                     _controlDown == _binding.Control &&
                     _altDown == _binding.Alt &&
                     _shiftDown == _binding.Shift)
            {
                var now = Environment.TickCount64;
                if (now - _lastHotkeyTick >= 600)
                {
                    _lastHotkeyTick = now;
                    HotkeyPressed?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        // Deskflow must receive the same physical keypress after Moniswitch
        // observes it, so the hook never consumes keyboard input.
        return CallNextHookEx(_keyboardHook, code, wParam, lParam);
    }

    public void Dispose()
    {
        if (_keyboardHook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_keyboardHook);
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(
        int hookId,
        LowLevelKeyboardProc callback,
        IntPtr module,
        uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(
        IntPtr hook,
        int code,
        IntPtr wParam,
        IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? moduleName);

    private delegate IntPtr LowLevelKeyboardProc(
        int code,
        IntPtr wParam,
        IntPtr lParam);
}
