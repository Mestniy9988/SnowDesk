namespace SnowDesk;

sealed class SettingsForm : Form
{
    readonly AppSettings _settings;
    readonly Action _changed;
    readonly TrackBar _intensity;
    readonly Label _intensityValue;
    readonly Label _intensityHint;
    readonly TrackBar _volume;
    readonly Label _volumeValue;
    readonly CheckBox _mute;
    readonly Label _music;
    readonly CheckBox _taskbar;
    readonly NumericUpDown _plow;
    readonly CheckBox _autostart;
    readonly CheckBox _battery;
    readonly Label _status;
    bool _binding;

    public SettingsForm(AppSettings settings, Action changed, Action callPlow, Action chooseMusic, Action resetMusic)
    {
        _settings = settings;
        _changed = changed;

        Text = "SnowDesk — настройки";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        AutoScaleMode = AutoScaleMode.Dpi;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterScreen;
        TopMost = true;
        Font = new Font("Segoe UI", 9.5f);
        BackColor = Color.FromArgb(246, 244, 240);
        ForeColor = Color.FromArgb(28, 40, 48);
        ClientSize = new Size(460, 640);
        Padding = new Padding(16);

        _status = new Label
        {
            AutoSize = false,
            Height = 28,
            Dock = DockStyle.Top,
            ForeColor = Color.FromArgb(47, 99, 130),
            TextAlign = ContentAlignment.MiddleLeft,
        };
        _intensity = Slider(1, 100);
        _intensityValue = ValueLabel();
        _intensityHint = Hint(" ");
        _volume = Slider(0, 100);
        _volumeValue = ValueLabel();
        _mute = new CheckBox { Text = "Без звука", AutoSize = true };
        _music = new Label { AutoSize = false, Height = 36, AutoEllipsis = true };
        _taskbar = new CheckBox { Text = "Копить сугроб на панели задач", AutoSize = true };
        _plow = new NumericUpDown { Minimum = 1, Maximum = 120, Width = 70 };
        _autostart = new CheckBox { Text = "Автозапуск с Windows", AutoSize = true };
        _battery = new CheckBox { Text = "Пауза при полноэкранных приложениях", AutoSize = true };

        _intensity.Scroll += (_, _) =>
        {
            if (_binding) return;
            _settings.Intensity = _intensity.Value;
            ShowIntensity();
            _changed();
        };
        _volume.Scroll += (_, _) =>
        {
            if (_binding) return;
            _settings.Volume = _volume.Value;
            _volumeValue.Text = _volume.Value.ToString();
            _changed();
        };
        _mute.CheckedChanged += (_, _) => Change(() => _settings.Muted = _mute.Checked);
        _taskbar.CheckedChanged += (_, _) => Change(() => _settings.TaskbarSnow = _taskbar.Checked);
        _plow.ValueChanged += (_, _) => Change(() => _settings.PlowIntervalMinutes = (int)_plow.Value);
        _autostart.CheckedChanged += (_, _) => Change(() => _settings.StartWithWindows = _autostart.Checked);
        _battery.CheckedChanged += (_, _) => Change(() => _settings.PauseOnFullscreen = _battery.Checked);

        var presets = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Dock = DockStyle.Top };
        foreach ((string name, int level) in Presets.Intensity)
        {
            int captured = level;
            presets.Controls.Add(GhostButton(name, () =>
            {
                _settings.Intensity = captured;
                Bind();
                _changed();
            }));
        }

        var volumePresets = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Dock = DockStyle.Top };
        foreach ((string name, int volume) in Presets.Volume)
        {
            int captured = volume;
            volumePresets.Controls.Add(GhostButton(name, () =>
            {
                _settings.Volume = captured;
                _settings.Muted = false;
                Bind();
                _changed();
            }));
        }

        var musicButtons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top };
        musicButtons.Controls.Add(GhostButton("Выбрать файл…", chooseMusic));
        musicButtons.Controls.Add(GhostButton("Встроенная", resetMusic));

        var plowRow = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, WrapContents = false };
        plowRow.Controls.Add(new Label { Text = "Интервал, минут", AutoSize = true, Margin = new Padding(0, 8, 8, 0) });
        plowRow.Controls.Add(_plow);
        var call = AccentButton("Вызвать КАМАЗ", callPlow);
        plowRow.Controls.Add(call);

        var root = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            Padding = new Padding(4, 0, 4, 8),
        };
        void Add(Control control, int height = 0)
        {
            control.Width = 412;
            control.Margin = new Padding(0, 3, 0, 3);
            if (height > 0)
                control.Height = height;
            root.Controls.Add(control);
        }

        Add(Title("SnowDesk"), 36);
        Add(_status, 24);
        Add(Section("Снег"), 24);
        Add(Pair("Интенсивность", _intensityValue), 26);
        Add(_intensity, 36);
        Add(_intensityHint);
        Add(presets);
        Add(Section("Звук"), 24);
        Add(Pair("Громкость", _volumeValue), 26);
        Add(_volume, 36);
        Add(volumePresets);
        Add(_mute);
        Add(_music, 36);
        Add(musicButtons);
        Add(Section("Панель задач"), 24);
        Add(_taskbar);
        Add(plowRow);
        Add(Hint("Сугроб копится, пока стол свободен. КАМАЗ выезжает по таймеру или когда сугроб почти полный — на метели это около минуты."));
        Add(Section("Система"), 24);
        Add(_autostart);
        Add(_battery);
        Add(Hint("Снег и музыка появляются, только когда все обычные окна свёрнуты. Клики проходят сквозь снег."));
        var close = GhostButton("Закрыть", Close);
        close.Margin = new Padding(0, 12, 0, 8);
        Add(close);

        Controls.Add(root);
        Bind();
    }

    public void Bind()
    {
        _binding = true;
        _intensity.Value = Math.Clamp(_settings.Intensity, 1, 100);
        _volume.Value = Math.Clamp(_settings.Volume, 0, 100);
        _mute.Checked = _settings.Muted;
        _taskbar.Checked = _settings.TaskbarSnow;
        _plow.Value = Math.Clamp(_settings.PlowIntervalMinutes, 1, 120);
        _autostart.Checked = _settings.StartWithWindows;
        _battery.Checked = _settings.PauseOnFullscreen;
        ShowIntensity();
        _volumeValue.Text = _settings.Volume.ToString();
        _music.Text = string.IsNullOrWhiteSpace(_settings.MusicPath)
            ? "Сейчас играет встроенный ветер и тихий пэд."
            : "Файл: " + _settings.MusicPath;
        _binding = false;
    }

    public void SetStatus(string text) => _status.Text = text;

    public void RaiseAbove()
    {
        if (IsDisposed)
            return;
        TopMost = true;
        Show();
        Activate();
    }

    void Change(Action apply)
    {
        if (_binding)
            return;
        apply();
        _changed();
    }

    void ShowIntensity()
    {
        _intensityValue.Text = _intensity.Value.ToString();
        _intensityHint.Text = IntensityProfile.FromLevel(_intensity.Value).Description
            + "  ·  1 — редкие снежинки, 100 — метель";
    }

    static TrackBar Slider(int min, int max) => new()
    {
        Minimum = min,
        Maximum = max,
        TickFrequency = 10,
        AutoSize = false,
        Height = 36,
        Dock = DockStyle.Top,
    };

    static Label ValueLabel() => new()
    {
        AutoSize = true,
        Font = new Font("Segoe UI", 11f, FontStyle.Bold),
        ForeColor = Color.FromArgb(24, 78, 112),
        TextAlign = ContentAlignment.MiddleRight,
    };

    static Label Title(string text) => new()
    {
        Text = text,
        AutoSize = false,
        Height = 34,
        Font = new Font("Segoe UI", 16f, FontStyle.Bold),
        ForeColor = Color.FromArgb(24, 52, 72),
        Dock = DockStyle.Top,
    };

    static Label Section(string text) => new()
    {
        Text = text,
        AutoSize = false,
        Height = 26,
        Margin = new Padding(0, 10, 0, 0),
        Font = new Font("Segoe UI", 10f, FontStyle.Bold),
        ForeColor = Color.FromArgb(180, 92, 18),
        Dock = DockStyle.Top,
    };

    static Label Hint(string text) => new()
    {
        Text = text,
        AutoSize = true,
        MaximumSize = new Size(412, 0),
        ForeColor = Color.FromArgb(90, 102, 112),
    };

    static Control Pair(string caption, Label value)
    {
        var row = new TableLayoutPanel { ColumnCount = 2, Dock = DockStyle.Top, Height = 26, Margin = new Padding(0) };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        row.Controls.Add(new Label { Text = caption, AutoSize = true, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        row.Controls.Add(value, 1, 0);
        return row;
    }

    static Button GhostButton(string text, Action click)
    {
        var button = new Button
        {
            Text = text,
            AutoSize = true,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(232, 236, 240),
            ForeColor = Color.FromArgb(28, 40, 48),
            Padding = new Padding(8, 3, 8, 3),
            Margin = new Padding(0, 4, 8, 4),
        };
        button.FlatAppearance.BorderColor = Color.FromArgb(196, 204, 212);
        button.Click += (_, _) => click();
        return button;
    }

    static Button AccentButton(string text, Action click)
    {
        var button = GhostButton(text, click);
        button.BackColor = Color.FromArgb(224, 112, 16);
        button.ForeColor = Color.White;
        button.FlatAppearance.BorderSize = 0;
        return button;
    }
}
