namespace Moniswitch;

internal sealed class SettingsForm : Form
{
    private readonly MonitorService _monitorService;
    private readonly SettingsStore _settingsStore;
    private readonly LanCanvasController _lanCanvasController;
    private readonly FlowLayoutPanel _monitorList;
    private readonly FlowLayoutPanel _profileList;
    private readonly Label _monitorSummary;
    private readonly Label _status;
    private readonly Panel _statusLamp;
    private readonly ComboBox _quickMonitor;
    private readonly ComboBox _quickInputA;
    private readonly ComboBox _quickInputB;
    private readonly Button _hotkeyButton;
    private readonly Button _applyButton;
    private Button _inputButton = null!;
    private Button _privacyButton = null!;
    private readonly Dictionary<string, MonitorCard> _cards = [];
    private InputSharingForm? _inputSharingForm;
    private LanCanvasForm? _lanCanvasForm;
    private string _inputStatusText = "Input sharing off";
    private bool _inputStatusError;
    private bool _loadingQuickSwitch;
    private bool _capturingHotkey;
    private bool _busy;

    public SettingsForm(
        MonitorService monitorService,
        SettingsStore settingsStore,
        LanCanvasController lanCanvasController)
    {
        _monitorService = monitorService;
        _settingsStore = settingsStore;
        _lanCanvasController = lanCanvasController;

        Text = "Moniswitch";
        Icon = AppIcon.Create();
        BackColor = UiTheme.Background;
        ForeColor = UiTheme.Text;
        Font = UiTheme.Font();
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(980, 680);
        ClientSize = new Size(1060, 720);
        AutoScaleMode = AutoScaleMode.Dpi;
        KeyPreview = true;
        DoubleBuffered = true;
        HandleCreated += (_, _) => UiTheme.UseDarkTitleBar(this);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.Background,
            ColumnCount = 1,
            RowCount = 3,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 84));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 66));
        Controls.Add(root);

        root.Controls.Add(BuildHeader(), 0, 0);

        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(22, 14, 22, 14),
            BackColor = UiTheme.Background,
            Margin = Padding.Empty
        };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 69));
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 31));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.Controls.Add(content, 0, 1);

        var monitorDeck = UiTheme.SurfacePanel();
        monitorDeck.Dock = DockStyle.Fill;
        monitorDeck.Margin = new Padding(0, 0, 8, 0);
        content.Controls.Add(monitorDeck, 0, 0);

        var monitorLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.Surface,
            ColumnCount = 1,
            RowCount = 2,
            Margin = Padding.Empty,
            Padding = new Padding(1)
        };
        monitorLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
        monitorLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        monitorDeck.Controls.Add(monitorLayout);

        var monitorHeader = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.Surface,
            Margin = Padding.Empty,
            Padding = new Padding(18, 12, 14, 10)
        };
        monitorLayout.Controls.Add(monitorHeader, 0, 0);

        var monitorTitle = UiTheme.ControlLabel("Displays");
        monitorTitle.Location = new Point(18, 11);
        _monitorSummary = UiTheme.SignalLabel("SCANNING", UiTheme.Faint);
        _monitorSummary.Location = new Point(18, 34);

        var refreshButton = UiTheme.Button("SCAN", ButtonTone.Ghost);
        refreshButton.Size = new Size(82, 36);
        refreshButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        refreshButton.Click += async (_, _) => await RefreshMonitorsAsync();
        monitorHeader.Resize += (_, _) =>
        {
            refreshButton.Location = new Point(monitorHeader.ClientSize.Width - refreshButton.Width - 14, 12);
        };
        monitorHeader.Controls.AddRange([monitorTitle, _monitorSummary, refreshButton]);

        _monitorList = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            BackColor = UiTheme.Surface,
            Padding = Padding.Empty,
            Margin = Padding.Empty
        };
        _monitorList.SizeChanged += (_, _) => ResizeMonitorCards();
        monitorLayout.Controls.Add(_monitorList, 0, 1);

        var routeDeck = UiTheme.SurfacePanel();
        routeDeck.Dock = DockStyle.Fill;
        routeDeck.Margin = new Padding(8, 0, 0, 0);
        routeDeck.Padding = new Padding(18, 15, 18, 16);
        content.Controls.Add(routeDeck, 1, 0);

        var routeLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.Surface,
            ColumnCount = 1,
            RowCount = 13,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        routeLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        routeLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 19));
        routeLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        routeLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 14));
        routeLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 19));
        routeLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        routeLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 14));
        routeLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 61));
        routeLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));
        routeLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        routeLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        routeLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 10));
        routeLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        routeDeck.Controls.Add(routeLayout);

        var routeTitle = UiTheme.Label("QUICK ROUTE", 13, bold: true, display: true);
        routeTitle.Anchor = AnchorStyles.Left;
        routeLayout.Controls.Add(routeTitle, 0, 0);

        routeLayout.Controls.Add(UiTheme.ControlLabel("Shortcut"), 0, 1);
        _hotkeyButton = UiTheme.Button(_settingsStore.Current.Hotkey.ToBinding().DisplayText);
        _hotkeyButton.Dock = DockStyle.Fill;
        _hotkeyButton.Margin = new Padding(0, 0, 0, 4);
        _hotkeyButton.TextAlign = ContentAlignment.MiddleLeft;
        _hotkeyButton.Font = UiTheme.MonoFont(9.5f, FontStyle.Bold);
        _hotkeyButton.Click += (_, _) => BeginHotkeyCapture();
        routeLayout.Controls.Add(_hotkeyButton, 0, 2);

        routeLayout.Controls.Add(UiTheme.ControlLabel("Hotkey display"), 0, 4);
        _quickMonitor = UiTheme.ComboBox();
        _quickMonitor.Dock = DockStyle.Fill;
        _quickMonitor.Margin = new Padding(0, 0, 0, 4);
        _quickMonitor.SelectedIndexChanged += (_, _) => QuickMonitorChanged();
        routeLayout.Controls.Add(_quickMonitor, 0, 5);

        _quickInputA = UiTheme.ComboBox();
        _quickInputB = UiTheme.ComboBox();
        _quickInputA.Dock = DockStyle.Fill;
        _quickInputB.Dock = DockStyle.Fill;
        _quickInputA.SelectedIndexChanged += (_, _) => SaveQuickSwitch();
        _quickInputB.SelectedIndexChanged += (_, _) => SaveQuickSwitch();
        routeLayout.Controls.Add(BuildSourcePair(), 0, 7);

        var profilesTitle = UiTheme.Label("SAVED ROUTES", 11, bold: true, display: true);
        profilesTitle.Anchor = AnchorStyles.Left;
        routeLayout.Controls.Add(profilesTitle, 0, 9);

        _profileList = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            BackColor = UiTheme.Surface,
            Padding = Padding.Empty,
            Margin = Padding.Empty
        };
        _profileList.SizeChanged += (_, _) => ResizeProfileButtons();
        routeLayout.Controls.Add(_profileList, 0, 10);

        var saveProfileButton = UiTheme.Button("SAVE ROUTE", ButtonTone.Ghost);
        saveProfileButton.Dock = DockStyle.Fill;
        saveProfileButton.Click += (_, _) => SaveProfile();
        routeLayout.Controls.Add(saveProfileButton, 0, 12);

        var footer = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.Surface,
            Padding = new Padding(22, 12, 22, 12),
            Margin = Padding.Empty
        };
        footer.Paint += (_, eventArgs) =>
        {
            using var pen = new Pen(UiTheme.Border);
            eventArgs.Graphics.DrawLine(pen, 0, 0, footer.Width, 0);
        };
        root.Controls.Add(footer, 0, 2);

        _statusLamp = new Panel
        {
            BackColor = UiTheme.Faint,
            Size = new Size(7, 7),
            Location = new Point(22, 29)
        };
        footer.Controls.Add(_statusLamp);

        _status = UiTheme.SignalLabel("READY");
        _status.Location = new Point(38, 24);
        footer.Controls.Add(_status);

        var copyright = UiTheme.Label("© 2026 Galactrex", 8.25f, color: UiTheme.Faint, display: true);
        footer.Controls.Add(copyright);

        _applyButton = UiTheme.Button("ROUTE SELECTED", ButtonTone.Accent);
        _applyButton.Size = new Size(170, 42);
        _applyButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _applyButton.Click += async (_, _) => await ApplySelectionAsync();
        void PositionFooterControls()
        {
            _applyButton.Location = new Point(footer.ClientSize.Width - _applyButton.Width - 22, 12);
            copyright.Location = new Point(
                Math.Max(200, footer.ClientSize.Width / 2 - copyright.PreferredWidth / 2),
                24);
        }

        footer.Resize += (_, _) => PositionFooterControls();
        footer.Controls.AddRange([copyright, _applyButton]);
        PositionFooterControls();

        LoadMonitors();
        LoadProfiles();
        FormClosed += (_, _) =>
        {
            _inputSharingForm?.Close();
            _lanCanvasForm?.Close();
        };
    }

    public event EventHandler? ConfigurationChanged;
    public event EventHandler? HotkeyChanged;
    public event EventHandler? InputSharingChanged;

    protected override bool ProcessCmdKey(ref Message message, Keys keyData)
    {
        if (!_capturingHotkey)
        {
            return base.ProcessCmdKey(ref message, keyData);
        }

        var key = keyData & Keys.KeyCode;
        if (key == Keys.Escape)
        {
            EndHotkeyCapture();
            SetStatus("SHORTCUT UNCHANGED");
            return true;
        }

        if (HotkeyFormatter.IsModifierKey(key))
        {
            return true;
        }

        if (!HotkeyFormatter.TryCreate(keyData, out var binding, out var error))
        {
            SetStatus(error, error: true);
            return true;
        }

        _settingsStore.Current.Hotkey.Set(binding);
        _settingsStore.Save();
        EndHotkeyCapture();
        SetStatus($"SHORTCUT / {binding.DisplayText.ToUpperInvariant()}", success: true);
        HotkeyChanged?.Invoke(this, EventArgs.Empty);
        ConfigurationChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public void ShowAndActivate()
    {
        if (!Visible)
        {
            Show();
        }

        WindowState = FormWindowState.Normal;
        Activate();
        BringToFront();
    }

    public void SyncFromService() => SyncCards();

    public void ShowLanCanvas()
    {
        ShowAndActivate();
        OpenLanCanvas();
    }

    public void SetInputSharingStatus(string status, bool error = false)
    {
        _inputStatusText = status;
        _inputStatusError = error;
        RefreshInputButton(error);
        _inputSharingForm?.SetStatus(
            status,
            success: _settingsStore.Current.InputSharing.Enabled && !error,
            error: error);
    }

    public void ReloadFromService()
    {
        LoadMonitors();
        LoadProfiles();
        EndHotkeyCapture();
    }

    private Control BuildHeader()
    {
        var header = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.Background,
            Padding = Padding.Empty,
            Margin = Padding.Empty
        };
        header.Paint += (_, eventArgs) =>
        {
            using var border = new Pen(UiTheme.Border);
            eventArgs.Graphics.DrawLine(border, 22, header.Height - 1, header.Width - 22, header.Height - 1);
        };

        var mark = new BrandMark
        {
            Location = new Point(22, 17),
            Size = new Size(46, 46)
        };
        header.Controls.Add(mark);

        var title = UiTheme.Label("MONISWITCH", 20, bold: true, display: true);
        title.Location = new Point(80, 16);
        header.Controls.Add(title);

        var subtitle = UiTheme.ControlLabel("Display router / software KVM");
        subtitle.ForeColor = UiTheme.Faint;
        subtitle.Location = new Point(82, 49);
        header.Controls.Add(subtitle);

        _inputButton = UiTheme.Button("INPUT / OFF", ButtonTone.Ghost);
        _inputButton.Size = new Size(126, 38);
        _inputButton.Click += (_, _) => OpenInputSharing();
        header.Controls.Add(_inputButton);
        RefreshInputButton();

        var canvasButton = UiTheme.Button("LAN CANVAS", ButtonTone.Ghost);
        canvasButton.Size = new Size(126, 38);
        canvasButton.ForeColor = UiTheme.Accent;
        canvasButton.FlatAppearance.BorderColor = UiTheme.Accent;
        canvasButton.Click += (_, _) => OpenLanCanvas();
        header.Controls.Add(canvasButton);

        _privacyButton = UiTheme.Button("PRIVATE / HIDDEN", ButtonTone.Ghost);
        _privacyButton.Size = new Size(148, 38);
        _privacyButton.Click += (_, _) => TogglePrivacyView();
        header.Controls.Add(_privacyButton);
        RefreshPrivacyButton();
        header.Resize += (_, _) =>
        {
            canvasButton.Location = new Point(header.ClientSize.Width - canvasButton.Width - 24, 23);
            _inputButton.Location = new Point(canvasButton.Left - _inputButton.Width - 12, 23);
            _privacyButton.Location = new Point(_inputButton.Left - _privacyButton.Width - 12, 23);
        };
        return header;
    }

    private void TogglePrivacyView()
    {
        var settings = _settingsStore.Current;
        settings.PrivacyView = !settings.PrivacyView;
        _settingsStore.Save();
        RefreshPrivacyButton();
        LoadMonitors();
        LoadProfiles();
        _inputSharingForm?.SetPrivacyMode(settings.PrivacyView);
        _lanCanvasForm?.SetPrivacyMode(settings.PrivacyView);
        ConfigurationChanged?.Invoke(this, EventArgs.Empty);
        SetStatus(settings.PrivacyView ? "PRIVATE DETAILS HIDDEN" : "PRIVATE DETAILS SHOWN");
    }

    private void RefreshPrivacyButton()
    {
        if (_privacyButton is null)
        {
            return;
        }

        var hidden = _settingsStore.Current.PrivacyView;
        var color = hidden ? UiTheme.Success : UiTheme.Faint;
        _privacyButton.Text = hidden ? "PRIVATE / HIDDEN" : "PRIVATE / SHOWN";
        _privacyButton.ForeColor = color;
        _privacyButton.FlatAppearance.BorderColor = color;
    }

    private void RefreshInputButton(bool error = false)
    {
        if (_inputButton is null)
        {
            return;
        }

        var enabled = _settingsStore.Current.InputSharing.Enabled;
        var color = error ? UiTheme.Danger : enabled ? UiTheme.Success : UiTheme.Faint;
        _inputButton.Text = enabled ? "INPUT / ON" : "INPUT / OFF";
        _inputButton.ForeColor = color;
        _inputButton.FlatAppearance.BorderColor = color;
    }

    private void OpenInputSharing()
    {
        if (_inputSharingForm is null || _inputSharingForm.IsDisposed)
        {
            var form = new InputSharingForm(_settingsStore);
            form.ConfigurationChanged += (_, _) => InputSharingChanged?.Invoke(this, EventArgs.Empty);
            form.FormClosed += (_, _) =>
            {
                if (ReferenceEquals(_inputSharingForm, form))
                {
                    _inputSharingForm = null;
                }
            };
            _inputSharingForm = form;
            form.SetStatus(
                _inputStatusText,
                success: _settingsStore.Current.InputSharing.Enabled && !_inputStatusError,
                error: _inputStatusError);
        }

        if (!_inputSharingForm.Visible)
        {
            _inputSharingForm.Show(this);
        }

        _inputSharingForm.Activate();
        _inputSharingForm.BringToFront();
    }

    private void OpenLanCanvas()
    {
        if (_lanCanvasForm is null || _lanCanvasForm.IsDisposed)
        {
            var form = new LanCanvasForm(_lanCanvasController, _settingsStore);
            form.FormClosed += (_, _) =>
            {
                if (ReferenceEquals(_lanCanvasForm, form))
                {
                    _lanCanvasForm = null;
                }
            };
            _lanCanvasForm = form;
        }

        if (!_lanCanvasForm.Visible)
        {
            _lanCanvasForm.Show(this);
        }

        _lanCanvasForm.Activate();
        _lanCanvasForm.BringToFront();
    }

    private Control BuildSourcePair()
    {
        var pair = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.Surface,
            ColumnCount = 2,
            RowCount = 2,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        pair.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        pair.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        pair.RowStyles.Add(new RowStyle(SizeType.Absolute, 19));
        pair.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var sourceA = UiTheme.ControlLabel("Source A");
        var sourceB = UiTheme.ControlLabel("Source B");
        sourceB.Margin = new Padding(5, 0, 0, 0);
        _quickInputA.Margin = new Padding(0, 0, 5, 0);
        _quickInputB.Margin = new Padding(5, 0, 0, 0);
        pair.Controls.Add(sourceA, 0, 0);
        pair.Controls.Add(sourceB, 1, 0);
        pair.Controls.Add(_quickInputA, 0, 1);
        pair.Controls.Add(_quickInputB, 1, 1);
        return pair;
    }

    private void BeginHotkeyCapture()
    {
        _capturingHotkey = true;
        _hotkeyButton.Text = "TYPE SHORTCUT / ESC CANCEL";
        _hotkeyButton.BackColor = UiTheme.Accent;
        _hotkeyButton.ForeColor = UiTheme.Background;
        _hotkeyButton.FlatAppearance.BorderColor = UiTheme.Accent;
        _hotkeyButton.Focus();
        SetStatus("WAITING FOR SHORTCUT");
    }

    private void EndHotkeyCapture()
    {
        _capturingHotkey = false;
        _hotkeyButton.Text = _settingsStore.Current.Hotkey.ToBinding().DisplayText;
        _hotkeyButton.BackColor = UiTheme.SurfaceRaised;
        _hotkeyButton.ForeColor = UiTheme.Text;
        _hotkeyButton.FlatAppearance.BorderColor = UiTheme.BorderStrong;
    }

    private void LoadMonitors()
    {
        _cards.Clear();
        _monitorList.SuspendLayout();
        try
        {
            _monitorList.Controls.Clear();
            var monitors = _monitorService.Monitors
                .OrderBy(monitor => monitor.DisplayNumber)
                .ThenBy(monitor => monitor.Bounds.Left)
                .ThenBy(monitor => monitor.Bounds.Top)
                .ToArray();

            for (var index = 0; index < monitors.Length; index++)
            {
                var monitor = monitors[index];
                var displayNumber = DisplayIdentity.NumberLabel(monitor.DisplayNumber, index + 1);
                var card = new MonitorCard(monitor, displayNumber, _settingsStore.Current.PrivacyView);
                card.SwitchRequested += async (_, request) => await SwitchOneAsync(request.MonitorId, request.Input);
                _cards[monitor.Id] = card;
                _monitorList.Controls.Add(card);
            }

            var controllable = monitors.Count(monitor => monitor.DdcAvailable && monitor.Inputs.Count > 0);
            _monitorSummary.Text = $"{monitors.Length:00} FOUND / {controllable:00} ROUTABLE";
            ResizeMonitorCards();
            LoadQuickSwitch(monitors);
        }
        finally
        {
            _monitorList.ResumeLayout();
        }
    }

    private void LoadQuickSwitch(IReadOnlyList<MonitorSnapshot> monitors)
    {
        _loadingQuickSwitch = true;
        try
        {
            _quickMonitor.Items.Clear();
            for (var index = 0; index < monitors.Count; index++)
            {
                var monitor = monitors[index];
                if (!monitor.DdcAvailable || monitor.Inputs.Count < 2)
                {
                    continue;
                }

                _quickMonitor.Items.Add(new MonitorChoice(
                    monitor.Id,
                    $"{DisplayIdentity.NumberLabel(monitor.DisplayNumber, index + 1)} / {monitor.Name}"));
            }

            var targetIndex = Enumerable.Range(0, _quickMonitor.Items.Count)
                .FirstOrDefault(index =>
                    _quickMonitor.Items[index] is MonitorChoice choice &&
                    choice.Id == _settingsStore.Current.QuickToggleMonitorId,
                    -1);
            if (targetIndex < 0 && _quickMonitor.Items.Count > 0)
            {
                targetIndex = 0;
            }

            _quickMonitor.SelectedIndex = targetIndex;
            PopulateQuickInputs();
        }
        finally
        {
            _loadingQuickSwitch = false;
        }

        SaveQuickSwitch();
    }

    private void QuickMonitorChanged()
    {
        if (_loadingQuickSwitch)
        {
            return;
        }

        _loadingQuickSwitch = true;
        try
        {
            PopulateQuickInputs();
        }
        finally
        {
            _loadingQuickSwitch = false;
        }

        SaveQuickSwitch();
        if (_quickMonitor.SelectedItem is MonitorChoice choice)
        {
            SetStatus($"HOTKEY DISPLAY / {choice.Label.ToUpperInvariant()}", success: true);
        }
    }

    private void PopulateQuickInputs()
    {
        _quickInputA.Items.Clear();
        _quickInputB.Items.Clear();
        if (_quickMonitor.SelectedItem is not MonitorChoice choice)
        {
            return;
        }

        var monitor = _monitorService.Monitors.First(item => item.Id == choice.Id);
        foreach (var input in monitor.Inputs)
        {
            _quickInputA.Items.Add(input);
            _quickInputB.Items.Add(input);
        }

        var preferredA = _settingsStore.Current.QuickToggleMonitorId == monitor.Id
            ? _settingsStore.Current.QuickToggleInputA
            : monitor.CurrentInput;
        var preferredB = _settingsStore.Current.QuickToggleMonitorId == monitor.Id
            ? _settingsStore.Current.QuickToggleInputB
            : monitor.Inputs.FirstOrDefault(input =>
                input.Code != preferredA && input.Name.StartsWith("HDMI", StringComparison.OrdinalIgnoreCase))?.Code;

        _quickInputA.SelectedIndex = FindInputIndex(_quickInputA, preferredA) is var aIndex && aIndex >= 0
            ? aIndex
            : 0;
        _quickInputB.SelectedIndex = FindInputIndex(_quickInputB, preferredB) is var bIndex && bIndex >= 0
            ? bIndex
            : Math.Min(1, _quickInputB.Items.Count - 1);
    }

    private void SaveQuickSwitch()
    {
        if (_loadingQuickSwitch ||
            _quickMonitor.SelectedItem is not MonitorChoice monitor ||
            _quickInputA.SelectedItem is not InputSource inputA ||
            _quickInputB.SelectedItem is not InputSource inputB)
        {
            return;
        }

        _settingsStore.Current.QuickToggleMonitorId = monitor.Id;
        _settingsStore.Current.QuickToggleInputA = inputA.Code;
        _settingsStore.Current.QuickToggleInputB = inputB.Code;
        _settingsStore.Save();
        ConfigurationChanged?.Invoke(this, EventArgs.Empty);
    }

    private void LoadProfiles()
    {
        _profileList.SuspendLayout();
        try
        {
            _profileList.Controls.Clear();
            var profiles = _settingsStore.Current.Profiles.OrderBy(item => item.Name).ToArray();
            for (var index = 0; index < profiles.Length; index++)
            {
                var profile = profiles[index];
                var label = _settingsStore.Current.PrivacyView
                    ? $"SAVED ROUTE {index + 1:00}"
                    : profile.Name.ToUpperInvariant();
                var button = UiTheme.Button(label, ButtonTone.Ghost);
                button.Height = 40;
                button.Margin = new Padding(0, 0, 0, 6);
                button.TextAlign = ContentAlignment.MiddleLeft;
                button.Tag = profile;
                button.Click += async (_, _) => await ApplyProfileAsync(profile);

                var menu = new ContextMenuStrip();
                UiTheme.StyleMenu(menu);
                menu.Items.Add("Rename", null, (_, _) => RenameProfile(profile));
                menu.Items.Add("Delete", null, (_, _) => DeleteProfile(profile));
                button.ContextMenuStrip = menu;
                _profileList.Controls.Add(button);
            }

            if (_settingsStore.Current.Profiles.Count == 0)
            {
                var empty = UiTheme.SignalLabel("NO SAVED ROUTES / SET SOURCES, THEN SAVE", UiTheme.Faint);
                empty.Margin = new Padding(2, 8, 0, 0);
                _profileList.Controls.Add(empty);
            }

            ResizeProfileButtons();
        }
        finally
        {
            _profileList.ResumeLayout();
        }
    }

    private async Task SwitchOneAsync(string monitorId, byte input)
    {
        if (_busy)
        {
            return;
        }

        SetBusy(true, $"ROUTING / {InputSourceCatalog.NameOf(input).ToUpperInvariant()}");
        try
        {
            await _monitorService.SwitchAsync(monitorId, input);
            SyncCards();
            SetStatus("ROUTE COMPLETE", success: true);
        }
        catch (Exception exception)
        {
            SetStatus(exception.Message, error: true);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task ApplySelectionAsync()
    {
        var assignments = _cards.Values
            .Where(card => card.SelectedInput.HasValue)
            .ToDictionary(card => card.MonitorId, card => card.SelectedInput!.Value);
        await ApplyAssignmentsAsync(assignments, "ROUTE COMPLETE");
    }

    private async Task ApplyProfileAsync(SwitchProfile profile)
    {
        foreach (var assignment in profile.Assignments)
        {
            if (_cards.TryGetValue(assignment.Key, out var card))
            {
                card.SelectInput(assignment.Value);
            }
        }

        var message = _settingsStore.Current.PrivacyView
            ? "SAVED ROUTE APPLIED"
            : $"ROUTE / {profile.Name.ToUpperInvariant()}";
        await ApplyAssignmentsAsync(profile.Assignments, message);
    }

    private async Task ApplyAssignmentsAsync(
        IReadOnlyDictionary<string, byte> assignments,
        string successMessage)
    {
        if (_busy || assignments.Count == 0)
        {
            return;
        }

        SetBusy(true, "ROUTING DISPLAYS");
        try
        {
            await _monitorService.SwitchManyAsync(assignments);
            SyncCards();
            SetStatus(successMessage, success: true);
        }
        catch (Exception exception)
        {
            SyncCards();
            SetStatus(exception.Message, error: true);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SaveProfile()
    {
        var assignments = _cards.Values
            .Where(card => card.SelectedInput.HasValue)
            .ToDictionary(card => card.MonitorId, card => card.SelectedInput!.Value);
        if (assignments.Count == 0)
        {
            SetStatus("SELECT A ROUTABLE DISPLAY", error: true);
            return;
        }

        using var dialog = new ProfileNameDialog();
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        _settingsStore.Current.Profiles.Add(new SwitchProfile
        {
            Name = dialog.ProfileName,
            Assignments = assignments
        });
        _settingsStore.Save();
        LoadProfiles();
        ConfigurationChanged?.Invoke(this, EventArgs.Empty);
        SetStatus(
            _settingsStore.Current.PrivacyView
                ? "ROUTE SAVED ON THIS PC"
                : $"SAVED / {dialog.ProfileName.ToUpperInvariant()}",
            success: true);
    }

    private void RenameProfile(SwitchProfile profile)
    {
        using var dialog = new ProfileNameDialog(profile.Name, rename: true);
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        profile.Name = dialog.ProfileName;
        _settingsStore.Save();
        LoadProfiles();
        ConfigurationChanged?.Invoke(this, EventArgs.Empty);
        SetStatus("ROUTE RENAMED", success: true);
    }

    private void DeleteProfile(SwitchProfile profile)
    {
        var result = MessageBox.Show(
            this,
            _settingsStore.Current.PrivacyView
                ? "Delete this saved route?"
                : $"Delete {profile.Name}?",
            "Moniswitch",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);
        if (result != DialogResult.Yes)
        {
            return;
        }

        _settingsStore.Current.Profiles.RemoveAll(item => item.Id == profile.Id);
        _settingsStore.Save();
        LoadProfiles();
        ConfigurationChanged?.Invoke(this, EventArgs.Empty);
        SetStatus("PROFILE DELETED");
    }

    private async Task RefreshMonitorsAsync()
    {
        if (_busy)
        {
            return;
        }

        SetBusy(true, "SCANNING DISPLAYS");
        try
        {
            await _monitorService.RefreshAsync();
            LoadMonitors();
            SetStatus("SCAN COMPLETE", success: true);
            ConfigurationChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception)
        {
            SetStatus(exception.Message, error: true);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SyncCards()
    {
        foreach (var monitor in _monitorService.Monitors)
        {
            if (_cards.TryGetValue(monitor.Id, out var card))
            {
                card.SetCurrentInput(monitor.CurrentInput);
            }
        }
    }

    private void SetBusy(bool busy, string? message = null)
    {
        _busy = busy;
        _applyButton.Enabled = !busy;
        foreach (var card in _cards.Values)
        {
            card.Enabled = !busy;
        }

        if (message is not null)
        {
            SetStatus(message);
        }
    }

    private void SetStatus(string message, bool success = false, bool error = false)
    {
        _status.Text = message.ToUpperInvariant();
        var color = error ? UiTheme.Danger : success ? UiTheme.Success : UiTheme.Muted;
        _status.ForeColor = color;
        _statusLamp.BackColor = color;
    }

    private void ResizeMonitorCards()
    {
        var scrollbar = _monitorList.VerticalScroll.Visible ? SystemInformation.VerticalScrollBarWidth : 0;
        var width = Math.Max(560, _monitorList.ClientSize.Width - scrollbar);
        foreach (Control control in _monitorList.Controls)
        {
            control.Width = width;
        }
    }

    private void ResizeProfileButtons()
    {
        var scrollbar = _profileList.VerticalScroll.Visible ? SystemInformation.VerticalScrollBarWidth : 0;
        var width = Math.Max(150, _profileList.ClientSize.Width - scrollbar - 4);
        foreach (Control control in _profileList.Controls)
        {
            control.Width = width;
        }
    }

    private static int FindInputIndex(ComboBox comboBox, byte? code)
    {
        if (!code.HasValue)
        {
            return -1;
        }

        for (var index = 0; index < comboBox.Items.Count; index++)
        {
            if (comboBox.Items[index] is InputSource input && input.Code == code)
            {
                return index;
            }
        }

        return -1;
    }
}

internal sealed record MonitorChoice(string Id, string Label)
{
    public override string ToString() => Label;
}

internal sealed class MonitorSwitchRequestedEventArgs(string monitorId, byte input) : EventArgs
{
    public string MonitorId { get; } = monitorId;
    public byte Input { get; } = input;
}

internal sealed class MonitorCard : UserControl
{
    private readonly ComboBox _input;
    private readonly Label _current;
    private readonly bool _routable;

    public MonitorCard(MonitorSnapshot monitor, string displayNumber, bool privacyView)
    {
        MonitorId = monitor.Id;
        _routable = monitor.DdcAvailable && monitor.Inputs.Count > 0;
        Height = 116;
        BackColor = UiTheme.Surface;
        Margin = new Padding(0, 0, 0, 1);
        Padding = new Padding(4, 0, 0, 0);
        DoubleBuffered = true;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.Surface,
            ColumnCount = 5,
            RowCount = 2,
            Margin = Padding.Empty,
            Padding = new Padding(14, 14, 14, 14)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 42));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 116));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 176));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 82));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        Controls.Add(layout);

        var number = UiTheme.SignalLabel(displayNumber, _routable ? UiTheme.Accent : UiTheme.Faint);
        number.Font = UiTheme.MonoFont(11, FontStyle.Bold);
        number.Anchor = AnchorStyles.Left;
        layout.Controls.Add(number, 0, 0);
        layout.SetRowSpan(number, 2);

        var visibleName = monitor.Name.ToUpperInvariant();
        var name = UiTheme.Label(visibleName, 12, bold: true, display: true);
        name.Anchor = AnchorStyles.Left;
        layout.Controls.Add(name, 1, 0);

        var displayDevice = monitor.DisplayName
            .Split('\\', StringSplitOptions.RemoveEmptyEntries)
            .LastOrDefault() ?? monitor.DisplayName;
        var orientation = monitor.Bounds.Height > monitor.Bounds.Width ? "PORTRAIT" : "LANDSCAPE";
        var visibleDetail = privacyView
            ? $"{monitor.Bounds.Width}×{monitor.Bounds.Height} · {orientation}"
            : $"{displayDevice} · {monitor.ProductCode} · {monitor.Bounds.Width}×{monitor.Bounds.Height}";
        var detail = UiTheme.SignalLabel(visibleDetail, UiTheme.Muted);
        detail.Anchor = AnchorStyles.Left;
        layout.Controls.Add(detail, 1, 1);

        var live = UiTheme.ControlLabel("Live");
        live.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
        live.Margin = new Padding(8, 0, 0, 1);
        layout.Controls.Add(live, 2, 0);

        _current = UiTheme.SignalLabel(CurrentLabel(monitor.CurrentInput),
            monitor.CurrentInput.HasValue ? UiTheme.Success : UiTheme.Danger);
        _current.Anchor = AnchorStyles.Left | AnchorStyles.Top;
        _current.Margin = new Padding(8, 1, 0, 0);
        layout.Controls.Add(_current, 2, 1);

        _input = UiTheme.ComboBox();
        _input.Dock = DockStyle.Fill;
        _input.Margin = new Padding(8, 7, 10, 7);
        foreach (var source in monitor.Inputs)
        {
            _input.Items.Add(source);
        }

        SelectInput(monitor.CurrentInput);
        _input.Enabled = _routable;
        layout.Controls.Add(_input, 3, 0);
        layout.SetRowSpan(_input, 2);

        var switchButton = UiTheme.Button("ROUTE", ButtonTone.Ghost);
        switchButton.Dock = DockStyle.Fill;
        switchButton.Margin = new Padding(0, 7, 0, 7);
        switchButton.ForeColor = _routable ? UiTheme.Accent : UiTheme.Faint;
        switchButton.FlatAppearance.BorderColor = _routable ? UiTheme.Accent : UiTheme.Border;
        switchButton.Enabled = _routable;
        switchButton.Click += (_, _) =>
        {
            if (_input.SelectedItem is InputSource source)
            {
                SwitchRequested?.Invoke(
                    this,
                    new MonitorSwitchRequestedEventArgs(MonitorId, source.Code));
            }
        };
        layout.Controls.Add(switchButton, 4, 0);
        layout.SetRowSpan(switchButton, 2);
    }

    public string MonitorId { get; }
    public byte? SelectedInput => (_input.SelectedItem as InputSource)?.Code;
    public event EventHandler<MonitorSwitchRequestedEventArgs>? SwitchRequested;

    public void SelectInput(byte? input)
    {
        for (var index = 0; index < _input.Items.Count; index++)
        {
            if (_input.Items[index] is InputSource source && source.Code == input)
            {
                _input.SelectedIndex = index;
                return;
            }
        }

        if (_input.Items.Count > 0 && _input.SelectedIndex < 0)
        {
            _input.SelectedIndex = 0;
        }
    }

    public void SetCurrentInput(byte? input)
    {
        SelectInput(input);
        _current.Text = CurrentLabel(input);
        _current.ForeColor = input.HasValue ? UiTheme.Success : UiTheme.Danger;
    }

    protected override void OnPaint(PaintEventArgs eventArgs)
    {
        base.OnPaint(eventArgs);
        using var rail = new SolidBrush(_routable ? UiTheme.Accent : UiTheme.Faint);
        eventArgs.Graphics.FillRectangle(rail, 0, 0, 3, Height);
    }

    private static string CurrentLabel(byte? input) => input.HasValue
        ? InputSourceCatalog.NameOf(input.Value).ToUpperInvariant()
        : "UNAVAILABLE";
}

internal sealed class ProfileNameDialog : Form
{
    private readonly TextBox _name;

    public ProfileNameDialog(string? currentName = null, bool rename = false)
    {
        Text = "Save profile";
        Icon = AppIcon.Create();
        BackColor = UiTheme.Background;
        ForeColor = UiTheme.Text;
        Font = UiTheme.Font();
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(430, 180);
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        HandleCreated += (_, _) => UiTheme.UseDarkTitleBar(this);

        var title = UiTheme.Label(rename ? "RENAME ROUTE" : "SAVE ROUTE", 13, bold: true, display: true);
        title.Location = new Point(22, 18);
        Controls.Add(title);

        var fieldLabel = UiTheme.ControlLabel("Route name / stored on this PC only");
        fieldLabel.Location = new Point(23, 49);
        Controls.Add(fieldLabel);

        _name = new TextBox
        {
            Location = new Point(24, 70),
            Size = new Size(382, 32),
            BackColor = UiTheme.SurfaceRaised,
            ForeColor = UiTheme.Text,
            BorderStyle = BorderStyle.FixedSingle,
            Font = UiTheme.Font(10.25f)
        };
        _name.Text = currentName ?? string.Empty;
        _name.SelectAll();
        Controls.Add(_name);

        var cancel = UiTheme.Button("CANCEL", ButtonTone.Ghost);
        cancel.Size = new Size(92, 40);
        cancel.Location = new Point(214, 122);
        cancel.DialogResult = DialogResult.Cancel;
        Controls.Add(cancel);

        var save = UiTheme.Button("SAVE", ButtonTone.Accent);
        save.Size = new Size(92, 40);
        save.Location = new Point(314, 122);
        save.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(_name.Text))
            {
                _name.Focus();
                return;
            }

            DialogResult = DialogResult.OK;
            Close();
        };
        Controls.Add(save);

        AcceptButton = save;
        CancelButton = cancel;
    }

    public string ProfileName => _name.Text.Trim();
}
