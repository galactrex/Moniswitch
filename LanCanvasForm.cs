namespace Moniswitch;

internal sealed class LanCanvasForm : Form
{
    private readonly LanCanvasController _controller;
    private readonly SettingsStore _settingsStore;
    private readonly ComboBox _target;
    private readonly TextBox _host;
    private readonly TextBox _user;
    private readonly TextBox _sshKey;
    private readonly TextBox _moonlight;
    private readonly NumericUpDown _fps;
    private readonly NumericUpDown _bitrate;
    private readonly Label _topology;
    private readonly Label _status;
    private readonly Panel _statusLamp;
    private readonly Button _pairButton;
    private readonly Button _startButton;
    private readonly Button _stopButton;
    private Button _detailsButton = null!;
    private bool _detailsVisible;
    private bool _busy;

    public LanCanvasForm(LanCanvasController controller, SettingsStore settingsStore)
    {
        _controller = controller;
        _settingsStore = settingsStore;

        Text = "LAN Canvas — Moniswitch";
        Icon = AppIcon.Create();
        BackColor = UiTheme.Background;
        ForeColor = UiTheme.Text;
        Font = UiTheme.Font();
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(700, 674);
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
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        body.Controls.Add(grid);

        var geometry = _controller.Geometry;
        _topology = UiTheme.SignalLabel(
            $"{geometry.ScreenCount:00} DISPLAYS / {geometry.Bounds.Width}×{geometry.Bounds.Height} / HEVC",
            UiTheme.Success);
        _topology.Font = UiTheme.MonoFont(10, FontStyle.Bold);
        _topology.Anchor = AnchorStyles.Left;
        grid.Controls.Add(_topology, 0, 0);
        grid.SetColumnSpan(_topology, 2);

        _target = UiTheme.ComboBox();
        _target.Dock = DockStyle.Fill;
        _target.Margin = new Padding(0, 5, 0, 5);
        _target.SelectedIndexChanged += (_, _) => UpdateTopology();
        _host = TextBox();
        _user = TextBox();
        _sshKey = TextBox();
        _moonlight = TextBox();
        _fps = NumberBox(30, 120, 60, 1);
        _bitrate = NumberBox(10000, 150000, 60000, 5000);

        AddField(grid, 1, "Canvas target", _target);
        AddField(grid, 2, "Linux host", _host);
        AddField(grid, 3, "SSH user", _user);
        AddField(grid, 4, "SSH key", BuildPathField(_sshKey, "SSH private key", "All files|*.*"));
        AddField(grid, 5, "Moonlight", BuildPathField(_moonlight, "Moonlight executable", "Executable|*.exe"));

        var streamPair = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.Surface,
            ColumnCount = 4,
            RowCount = 1,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        streamPair.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 30));
        streamPair.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
        streamPair.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 64));
        streamPair.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
        var fpsLabel = UiTheme.ControlLabel("FPS");
        fpsLabel.Anchor = AnchorStyles.Left;
        var bitrateLabel = UiTheme.ControlLabel("Kbps");
        bitrateLabel.Anchor = AnchorStyles.Left;
        _fps.Dock = DockStyle.Fill;
        _fps.Margin = new Padding(0, 5, 12, 5);
        _bitrate.Dock = DockStyle.Fill;
        _bitrate.Margin = new Padding(0, 5, 0, 5);
        streamPair.Controls.Add(fpsLabel, 0, 0);
        streamPair.Controls.Add(_fps, 1, 0);
        streamPair.Controls.Add(bitrateLabel, 2, 0);
        streamPair.Controls.Add(_bitrate, 3, 0);
        AddField(grid, 6, "Stream", streamPair);

        var firewall = UiTheme.Button("COPY UFW RULES", ButtonTone.Ghost);
        firewall.Dock = DockStyle.Left;
        firewall.Size = new Size(150, 38);
        firewall.Margin = new Padding(0, 5, 0, 5);
        firewall.Click += async (_, _) => await CopyFirewallRulesAsync();
        AddField(grid, 7, "Linux firewall", firewall);

        var note = UiTheme.SignalLabel(
            "DETAILS STAY ON THIS PC / ZERO SENDERS WHEN OFF / DESKFLOW INPUT",
            UiTheme.Faint);
        note.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
        note.Margin = new Padding(0, 0, 0, 10);
        grid.Controls.Add(note, 1, 8);

        var actions = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.Surface,
            ColumnCount = 3,
            RowCount = 1,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));
        _pairButton = UiTheme.Button("PAIR", ButtonTone.Ghost);
        _startButton = UiTheme.Button("START CANVAS", ButtonTone.Accent);
        _stopButton = UiTheme.Button("STOP", ButtonTone.Ghost);
        _pairButton.Dock = DockStyle.Fill;
        _startButton.Dock = DockStyle.Fill;
        _stopButton.Dock = DockStyle.Fill;
        _pairButton.Margin = new Padding(0, 0, 6, 0);
        _startButton.Margin = new Padding(6, 0, 6, 0);
        _stopButton.Margin = new Padding(6, 0, 0, 0);
        _pairButton.Click += async (_, _) => await PairAsync();
        _startButton.Click += async (_, _) => await StartAsync();
        _stopButton.Click += async (_, _) => await StopAsync();
        actions.Controls.Add(_pairButton, 0, 0);
        actions.Controls.Add(_startButton, 1, 0);
        actions.Controls.Add(_stopButton, 2, 0);
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
        _status = UiTheme.SignalLabel("CANVAS OFF");
        _status.Location = new Point(38, 24);
        footer.Controls.AddRange([_statusLamp, _status]);

        LoadSettings();
        SetPrivacyMode(_settingsStore.Current.PrivacyView);
        _controller.StatusChanged += ControllerStatusChanged;
        FormClosed += (_, _) => _controller.StatusChanged -= ControllerStatusChanged;
    }

    public void SetPrivacyMode(bool privacyView)
    {
        _detailsVisible = !privacyView;
        ApplyDetailVisibility();
        RefreshTargets();
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
        var title = UiTheme.Label("LAN CANVAS", 20, bold: true, display: true);
        title.Location = new Point(80, 16);
        var subtitle = UiTheme.ControlLabel("Linux desktop / span or one display");
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
        if (_detailsButton is null || _host is null || _user is null || _sshKey is null || _moonlight is null)
        {
            return;
        }

        foreach (var field in new[] { _host, _user, _sshKey, _moonlight })
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
        Margin = new Padding(0, 5, 0, 5)
    };

    private static NumericUpDown NumberBox(decimal min, decimal max, decimal value, decimal increment) => new()
    {
        Minimum = min,
        Maximum = max,
        Value = value,
        Increment = increment,
        BackColor = UiTheme.SurfaceRaised,
        ForeColor = UiTheme.Text,
        BorderStyle = BorderStyle.FixedSingle,
        Font = UiTheme.MonoFont(9.25f),
        ThousandsSeparator = true
    };

    private static void AddField(TableLayoutPanel grid, int row, string label, Control control)
    {
        var fieldLabel = UiTheme.ControlLabel(label);
        fieldLabel.Anchor = AnchorStyles.Left;
        grid.Controls.Add(fieldLabel, 0, row);
        grid.Controls.Add(control, 1, row);
    }

    private Control BuildPathField(TextBox textBox, string title, string filter)
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
        textBox.Margin = new Padding(0, 5, 8, 5);
        var browse = UiTheme.Button("BROWSE", ButtonTone.Ghost);
        browse.Dock = DockStyle.Fill;
        browse.Margin = new Padding(0, 5, 0, 5);
        browse.Click += (_, _) =>
        {
            using var picker = new OpenFileDialog
            {
                Title = title,
                Filter = filter,
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
        var settings = _settingsStore.Current.LanCanvas;
        RefreshTargets(settings.DisplayTarget);

        _host.Text = settings.LinuxHost;
        _user.Text = settings.LinuxUser;
        _sshKey.Text = settings.SshKeyPath ?? string.Empty;
        _moonlight.Text = settings.MoonlightExecutablePath ?? LanCanvasController.FindMoonlight() ?? string.Empty;
        _fps.Value = Math.Clamp(settings.FramesPerSecond, (int)_fps.Minimum, (int)_fps.Maximum);
        _bitrate.Value = Math.Clamp(settings.BitrateKbps, (int)_bitrate.Minimum, (int)_bitrate.Maximum);
        UpdateTopology();
    }

    private void RefreshTargets(string? selectedKey = null)
    {
        selectedKey ??= (_target.SelectedItem as CanvasTarget)?.Key
            ?? _settingsStore.Current.LanCanvas.DisplayTarget;
        _target.BeginUpdate();
        try
        {
            _target.Items.Clear();
            foreach (var target in _controller.Targets)
            {
                _target.Items.Add(target);
            }

            var selectedIndex = Enumerable.Range(0, _target.Items.Count)
                .FirstOrDefault(
                    index => _target.Items[index] is CanvasTarget target &&
                        string.Equals(
                            target.Key,
                            selectedKey,
                            StringComparison.OrdinalIgnoreCase),
                    -1);
            _target.SelectedIndex = selectedIndex >= 0 ? selectedIndex : 0;
        }
        finally
        {
            _target.EndUpdate();
        }
        UpdateTopology();
    }

    private void SaveSettings()
    {
        var settings = _settingsStore.Current.LanCanvas;
        settings.DisplayTarget = _target.SelectedItem is CanvasTarget target
            ? target.Key
            : LanCanvasController.AllDisplaysTarget;
        settings.LinuxHost = _host.Text.Trim();
        settings.LinuxUser = _user.Text.Trim();
        settings.SshKeyPath = NullIfWhiteSpace(_sshKey.Text);
        settings.MoonlightExecutablePath = NullIfWhiteSpace(_moonlight.Text);
        settings.FramesPerSecond = (int)_fps.Value;
        settings.BitrateKbps = (int)_bitrate.Value;
        settings.Enabled = !string.IsNullOrWhiteSpace(settings.LinuxHost) &&
                           !string.IsNullOrWhiteSpace(settings.LinuxUser);
        _settingsStore.Save();
    }

    private async Task PairAsync() => await RunAsync(async () =>
    {
        SaveSettings();
        await _controller.PairAsync();
    });

    private async Task StartAsync() => await RunAsync(async () =>
    {
        SaveSettings();
        await _controller.StartAsync();
    });

    private async Task StopAsync() => await RunAsync(async () => await _controller.StopAsync());

    private async Task RunAsync(Func<Task> operation)
    {
        if (_busy)
        {
            return;
        }

        SetBusy(true);
        try
        {
            await operation();
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

    private void SetBusy(bool busy)
    {
        _busy = busy;
        _target.Enabled = !busy;
        _pairButton.Enabled = !busy;
        _startButton.Enabled = !busy;
        _stopButton.Enabled = !busy;
    }

    private void ControllerStatusChanged(object? sender, LanCanvasStatusEventArgs eventArgs)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => SetStatus(eventArgs.Message, eventArgs.Success, eventArgs.Error));
            return;
        }

        SetStatus(eventArgs.Message, eventArgs.Success, eventArgs.Error);
    }

    private void SetStatus(string message, bool success = false, bool error = false)
    {
        var color = error ? UiTheme.Danger : success ? UiTheme.Success : UiTheme.Muted;
        _status.Text = message.ToUpperInvariant();
        _status.ForeColor = color;
        _statusLamp.BackColor = color;
    }

    private async Task CopyFirewallRulesAsync() => await RunAsync(async () =>
    {
        SaveSettings();
        Clipboard.SetText(await _controller.GetFirewallRulesAsync());
        SetStatus("UFW RULES COPIED / THIS PC ONLY", success: true);
    });

    private void UpdateTopology()
    {
        if (_target.SelectedItem is not CanvasTarget target)
        {
            return;
        }

        var displayWord = target.ScreenCount == 1 ? "DISPLAY" : "DISPLAYS";
        _topology.Text =
            $"{target.ScreenCount:00} {displayWord} / {target.Bounds.Width}×{target.Bounds.Height} / HEVC";
    }

    private static string? NullIfWhiteSpace(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
