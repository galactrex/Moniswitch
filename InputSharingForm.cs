namespace Moniswitch;

internal sealed class InputSharingForm : Form
{
    private readonly SettingsStore _settingsStore;
    private readonly TextBox _windowsName;
    private readonly TextBox _linuxName;
    private readonly TextBox _deskflow;
    private readonly CheckBox _startWithWindows;
    private readonly CheckBox _macPcShortcuts;
    private readonly Button _linuxPlatform;
    private readonly Button _macPlatform;
    private readonly Label _route;
    private readonly Label _remoteLabel;
    private RemotePlatform _platform;
    private readonly Label _status;
    private readonly Panel _statusLamp;
    private readonly Button _saveButton;
    private readonly Button _stopButton;
    private Button _detailsButton = null!;
    private bool _detailsVisible;
    private bool _busy;

    public InputSharingForm(SettingsStore settingsStore)
    {
        _settingsStore = settingsStore;

        Text = "Input Link — Moniswitch";
        Icon = AppIcon.Create();
        BackColor = UiTheme.Background;
        ForeColor = UiTheme.Text;
        Font = UiTheme.Font();
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(680, 700);
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
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
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 86));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
        Controls.Add(root);
        root.Controls.Add(BuildHeader(), 0, 0);

        var body = UiTheme.SurfacePanel();
        body.Dock = DockStyle.Fill;
        body.Margin = new Padding(22, 12, 22, 12);
        body.Padding = new Padding(20, 18, 20, 18);
        root.Controls.Add(body, 0, 1);

        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.Surface,
            ColumnCount = 2,
            RowCount = 10,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 134));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        body.Controls.Add(grid);

        _route = UiTheme.SignalLabel("KEYBOARD / MOUSE / TLS", UiTheme.Success);
        _route.Font = UiTheme.MonoFont(10, FontStyle.Bold);
        _route.Anchor = AnchorStyles.Left;
        grid.Controls.Add(_route, 0, 0);
        grid.SetColumnSpan(_route, 2);

        _linuxPlatform = UiTheme.Button("LINUX", ButtonTone.Ghost);
        _macPlatform = UiTheme.Button("MAC", ButtonTone.Ghost);
        _linuxPlatform.Click += (_, _) => SelectPlatform(RemotePlatform.Linux);
        _macPlatform.Click += (_, _) => SelectPlatform(RemotePlatform.MacOS);
        AddField(grid, 1, "Other computer", BuildPlatformField());

        _windowsName = TextBox();
        _linuxName = TextBox();
        _deskflow = TextBox();
        AddField(grid, 2, "Windows name", _windowsName);
        _remoteLabel = AddField(grid, 3, "Linux name", _linuxName);
        AddField(grid, 4, "Deskflow", BuildPathField(_deskflow));

        var copyFingerprint = UiTheme.Button("COPY SERVER PIN", ButtonTone.Ghost);
        copyFingerprint.Dock = DockStyle.Left;
        copyFingerprint.Size = new Size(176, 38);
        copyFingerprint.Margin = new Padding(0, 5, 0, 5);
        copyFingerprint.Click += (_, _) => CopyServerFingerprint();
        AddField(grid, 5, "TLS pin", copyFingerprint);

        _startWithWindows = new CheckBox
        {
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            BackColor = UiTheme.Surface,
            ForeColor = UiTheme.Text,
            FlatStyle = FlatStyle.Flat,
            Font = UiTheme.MonoFont(9.5f, FontStyle.Bold),
            Text = "START MONISWITCH WITH WINDOWS"
        };
        AddField(grid, 6, "Startup", _startWithWindows);

        _macPcShortcuts = new CheckBox
        {
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            BackColor = UiTheme.Surface,
            ForeColor = UiTheme.Text,
            FlatStyle = FlatStyle.Flat,
            Font = UiTheme.MonoFont(9.5f, FontStyle.Bold),
            Text = "CTRL ACTS AS COMMAND ON THE MAC"
        };
        AddField(grid, 7, "Mac keys", _macPcShortcuts);

        var note = UiTheme.SignalLabel(
            "PRIVATE VALUES STAY ON THIS PC / ONE BRIDGE / NO INPUT POLLING",
            UiTheme.Faint);
        note.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
        note.Margin = new Padding(0, 0, 0, 12);
        grid.Controls.Add(note, 1, 8);

        var actions = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.Surface,
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));
        _saveButton = UiTheme.Button("SAVE + START", ButtonTone.Accent);
        _stopButton = UiTheme.Button("STOP LINK", ButtonTone.Ghost);
        _saveButton.Dock = DockStyle.Fill;
        _stopButton.Dock = DockStyle.Fill;
        _saveButton.Margin = new Padding(0, 0, 6, 0);
        _stopButton.Margin = new Padding(6, 0, 0, 0);
        _saveButton.Click += (_, _) => SaveAndStart();
        _stopButton.Click += (_, _) => StopLink();
        actions.Controls.Add(_saveButton, 0, 0);
        actions.Controls.Add(_stopButton, 1, 0);
        grid.Controls.Add(actions, 0, 9);
        grid.SetColumnSpan(actions, 2);

        var footer = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.Surface,
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
        _status = UiTheme.SignalLabel("LINK OFF");
        _status.Location = new Point(38, 24);
        footer.Controls.AddRange([_statusLamp, _status]);

        LoadSettings();
        SetPrivacyMode(_settingsStore.Current.PrivacyView);
    }

    public event EventHandler? ConfigurationChanged;

    public void SetPrivacyMode(bool privacyView)
    {
        _detailsVisible = !privacyView;
        ApplyDetailVisibility();
    }

    public void SetStatus(string message, bool success = false, bool error = false)
    {
        var color = error ? UiTheme.Danger : success ? UiTheme.Success : UiTheme.Muted;
        _status.Text = message.ToUpperInvariant();
        _status.ForeColor = color;
        _statusLamp.BackColor = color;
    }

    private Control BuildHeader()
    {
        var header = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.Background,
            Margin = Padding.Empty
        };
        header.Paint += (_, eventArgs) =>
        {
            using var pen = new Pen(UiTheme.Border);
            eventArgs.Graphics.DrawLine(pen, 22, header.Height - 1, header.Width - 22, header.Height - 1);
        };

        var mark = new BrandMark { Location = new Point(22, 18), Size = new Size(46, 46) };
        var title = UiTheme.Label("INPUT LINK", 20, bold: true, display: true);
        title.Location = new Point(80, 16);
        var subtitle = UiTheme.ControlLabel("One keyboard / both computers");
        subtitle.ForeColor = UiTheme.Faint;
        subtitle.Location = new Point(82, 49);
        _detailsButton = UiTheme.Button("SHOW DETAILS", ButtonTone.Ghost);
        _detailsButton.Size = new Size(132, 38);
        _detailsButton.Click += (_, _) =>
        {
            _detailsVisible = !_detailsVisible;
            ApplyDetailVisibility();
        };
        header.Resize += (_, _) =>
        {
            _detailsButton.Location = new Point(
                header.ClientSize.Width - _detailsButton.Width - 22,
                23);
        };
        header.Controls.AddRange([mark, title, subtitle, _detailsButton]);
        return header;
    }

    private void ApplyDetailVisibility()
    {
        if (_detailsButton is null || _windowsName is null || _linuxName is null || _deskflow is null)
        {
            return;
        }

        foreach (var field in new[] { _windowsName, _linuxName, _deskflow })
        {
            field.UseSystemPasswordChar = !_detailsVisible;
        }

        _detailsButton.Text = _detailsVisible ? "HIDE DETAILS" : "SHOW DETAILS";
        var color = _detailsVisible ? UiTheme.Faint : UiTheme.Success;
        _detailsButton.ForeColor = color;
        _detailsButton.FlatAppearance.BorderColor = color;
    }

    private static TextBox TextBox() => new()
    {
        Dock = DockStyle.Fill,
        BackColor = UiTheme.SurfaceRaised,
        ForeColor = UiTheme.Text,
        BorderStyle = BorderStyle.FixedSingle,
        Font = UiTheme.Font(9.5f),
        Margin = new Padding(0, 6, 0, 6)
    };

    private static Label AddField(TableLayoutPanel grid, int row, string label, Control control)
    {
        var fieldLabel = UiTheme.ControlLabel(label);
        fieldLabel.Anchor = AnchorStyles.Left;
        grid.Controls.Add(fieldLabel, 0, row);
        grid.Controls.Add(control, 1, row);
        return fieldLabel;
    }

    private Control BuildPlatformField()
    {
        var field = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.Surface,
            ColumnCount = 3,
            RowCount = 1,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        field.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112));
        field.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112));
        field.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _linuxPlatform.Dock = DockStyle.Fill;
        _macPlatform.Dock = DockStyle.Fill;
        _linuxPlatform.Margin = new Padding(0, 6, 6, 6);
        _macPlatform.Margin = new Padding(0, 6, 0, 6);
        field.Controls.Add(_linuxPlatform, 0, 0);
        field.Controls.Add(_macPlatform, 1, 0);
        return field;
    }

    private void SelectPlatform(RemotePlatform platform)
    {
        // Carry an untouched default name across so a Mac is not labelled
        // linux-pc. A name the person typed is left exactly as written.
        var previousDefault = RemotePlatformText.DefaultScreenName(_platform);
        if (platform != _platform &&
            (string.IsNullOrWhiteSpace(_linuxName.Text) ||
             string.Equals(_linuxName.Text.Trim(), previousDefault, StringComparison.Ordinal)))
        {
            _linuxName.Text = RemotePlatformText.DefaultScreenName(platform);
        }

        _platform = platform;
        var mac = platform == RemotePlatform.MacOS;
        foreach (var (button, selected) in new[] { (_linuxPlatform, !mac), (_macPlatform, mac) })
        {
            button.ForeColor = selected ? UiTheme.Text : UiTheme.Faint;
            button.FlatAppearance.BorderColor = selected ? UiTheme.Accent : UiTheme.Border;
        }

        _remoteLabel.Text = $"{RemotePlatformText.Name(platform)} name".ToUpperInvariant();
        _macPcShortcuts.Enabled = mac;
        _route.Text = mac ? "KEYBOARD / MOUSE / CLIPBOARD / TLS" : "KEYBOARD / MOUSE / TLS";
    }

    private Control BuildPathField(TextBox textBox)
    {
        var field = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.Surface,
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        field.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        field.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 88));
        textBox.Margin = new Padding(0, 6, 8, 6);
        var browse = UiTheme.Button("BROWSE", ButtonTone.Ghost);
        browse.Dock = DockStyle.Fill;
        browse.Margin = new Padding(0, 6, 0, 6);
        browse.Click += (_, _) =>
        {
            using var picker = new OpenFileDialog
            {
                Title = "Deskflow executable",
                Filter = "Deskflow core|deskflow-core.exe|Executable|*.exe",
                CheckFileExists = true
            };
            if (picker.ShowDialog(this) == DialogResult.OK)
            {
                textBox.Text = picker.FileName;
            }
        };
        field.Controls.Add(textBox, 0, 0);
        field.Controls.Add(browse, 1, 0);
        return field;
    }

    private void LoadSettings()
    {
        var settings = _settingsStore.Current.InputSharing;
        _windowsName.Text = settings.WindowsScreenName;
        _linuxName.Text = settings.LinuxScreenName;
        _macPcShortcuts.Checked = settings.MacPcShortcuts;
        _platform = settings.RemotePlatform;
        SelectPlatform(settings.RemotePlatform);
        _deskflow.Text = settings.DeskflowExecutablePath ?? DeskflowBridge.FindExecutable() ?? string.Empty;
        _startWithWindows.Checked = settings.StartWithWindows;
        SetStatus(settings.Enabled ? "LINK CONFIGURED" : "LINK OFF", success: settings.Enabled);
    }

    private void SaveAndStart()
    {
        if (_busy)
        {
            return;
        }

        var windows = _windowsName.Text.Trim();
        var linux = _linuxName.Text.Trim();
        var executable = _deskflow.Text.Trim();
        if (string.IsNullOrWhiteSpace(windows) || string.IsNullOrWhiteSpace(linux))
        {
            SetStatus("BOTH SCREEN NAMES ARE REQUIRED", error: true);
            return;
        }

        if (string.IsNullOrWhiteSpace(executable) || !File.Exists(executable))
        {
            SetStatus("DESKFLOW CORE NOT FOUND", error: true);
            return;
        }

        SetBusy(true);
        try
        {
            var settings = _settingsStore.Current.InputSharing;
            settings.WindowsScreenName = windows;
            settings.LinuxScreenName = linux;
            settings.RemotePlatform = _platform;
            settings.MacPcShortcuts = _macPcShortcuts.Checked;
            settings.DeskflowExecutablePath = executable;
            settings.StartWithWindows = _startWithWindows.Checked;
            settings.Enabled = true;
            _settingsStore.Save();
            ConfigurationChanged?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void StopLink()
    {
        if (_busy)
        {
            return;
        }

        _settingsStore.Current.InputSharing.Enabled = false;
        _settingsStore.Current.InputSharing.StartWithWindows = _startWithWindows.Checked;
        _settingsStore.Save();
        ConfigurationChanged?.Invoke(this, EventArgs.Empty);
    }

    private void CopyServerFingerprint()
    {
        var fingerprint = DeskflowBridge.TryGetServerFingerprint(_settingsStore.DirectoryPath);
        if (string.IsNullOrWhiteSpace(fingerprint))
        {
            SetStatus("START THE LINK TO CREATE ITS TLS PIN", error: true);
            return;
        }

        Clipboard.SetText($"SHA256:{fingerprint}");
        SetStatus("SERVER PIN COPIED", success: true);
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        _saveButton.Enabled = !busy;
        _stopButton.Enabled = !busy;
    }
}
