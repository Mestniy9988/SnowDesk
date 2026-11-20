namespace SnowDesk;

sealed class TrayHost : IDisposable
{
    readonly NotifyIcon _icon;
    readonly ToolStripMenuItem _enable;
    readonly ToolStripMenuItem _mute;
    readonly ToolStripMenuItem _taskbar;
    readonly ToolStripMenuItem _autostart;
    readonly ToolStripMenuItem _intensityMenu;
    readonly ToolStripMenuItem _volumeMenu;
    readonly ToolStripMenuItem _plowMenu;
    Icon? _activeIcon;
    Icon? _idleIcon;

    public TrayHost()
    {
        _activeIcon = PaintIcon(true);
        _idleIcon = PaintIcon(false);
        _enable = Item("Включить снег", () => EnableToggled?.Invoke());
        _mute = Item("Без звука", () => MuteToggled?.Invoke());
        _taskbar = Item("Снег на панели задач", () => TaskbarToggled?.Invoke());
        _autostart = Item("Автозапуск с Windows", () => AutostartToggled?.Invoke());
        _intensityMenu = new ToolStripMenuItem("Интенсивность");
        _volumeMenu = new ToolStripMenuItem("Громкость");
        _plowMenu = new ToolStripMenuItem("Интервал КАМАЗа");

        foreach ((string name, int level) in Presets.Intensity)
        {
            int captured = level;
            var item = Item(name, () => IntensitySelected?.Invoke(captured));
            item.Tag = level;
            _intensityMenu.DropDownItems.Add(item);
        }

        foreach ((string name, int volume) in Presets.Volume)
        {
            int captured = volume;
            var item = Item(name, () => VolumeSelected?.Invoke(captured));
            item.Tag = volume;
            _volumeMenu.DropDownItems.Add(item);
        }

        foreach (int minutes in Presets.PlowMinutes)
        {
            int captured = minutes;
            var item = Item($"{minutes} мин", () => PlowIntervalSelected?.Invoke(captured));
            item.Tag = minutes;
            _plowMenu.DropDownItems.Add(item);
        }

        var menu = new ContextMenuStrip { Font = new Font("Segoe UI", 9f) };
        menu.Items.Add(_enable);
        menu.Items.Add(Item("Настройки…", () => OpenSettings?.Invoke()));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_intensityMenu);
        menu.Items.Add(_volumeMenu);
        menu.Items.Add(Item("Выбрать музыку…", () => ChooseMusic?.Invoke()));
        menu.Items.Add(Item("Встроенная музыка", () => ResetMusic?.Invoke()));
        menu.Items.Add(_mute);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_taskbar);
        menu.Items.Add(Item("Вызвать КАМАЗ", () => CallPlow?.Invoke()));
        menu.Items.Add(_plowMenu);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_autostart);
        menu.Items.Add(Item("Выход", () => ExitRequested?.Invoke()));

        _icon = new NotifyIcon
        {
            Icon = _activeIcon,
            Visible = true,
            Text = "SnowDesk",
            ContextMenuStrip = menu,
        };
        _icon.DoubleClick += (_, _) => OpenSettings?.Invoke();
    }

    public event Action? EnableToggled;
    public event Action? OpenSettings;
    public event Action<int>? IntensitySelected;
    public event Action<int>? VolumeSelected;
    public event Action? ChooseMusic;
    public event Action? ResetMusic;
    public event Action? MuteToggled;
    public event Action? TaskbarToggled;
    public event Action? CallPlow;
    public event Action<int>? PlowIntervalSelected;
    public event Action? AutostartToggled;
    public event Action? ExitRequested;

    public void Sync(AppSettings settings, string status)
    {
        _enable.Checked = settings.Enabled;
        _mute.Checked = settings.Muted;
        _taskbar.Checked = settings.TaskbarSnow;
        _autostart.Checked = settings.StartWithWindows;
        CheckTag(_intensityMenu, settings.Intensity);
        CheckTag(_volumeMenu, settings.Volume);
        CheckTag(_plowMenu, settings.PlowIntervalMinutes);
        _icon.Icon = settings.Enabled ? _activeIcon : _idleIcon;
        _icon.Text = TrimTooltip(status);
    }

    public void Balloon(string title, string text)
    {
        _icon.BalloonTipTitle = title;
        _icon.BalloonTipText = text;
        _icon.ShowBalloonTip(4000);
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _activeIcon?.Dispose();
        _idleIcon?.Dispose();
    }

    static void CheckTag(ToolStripMenuItem menu, int value)
    {
        foreach (ToolStripItem entry in menu.DropDownItems)
        {
            if (entry is ToolStripMenuItem item)
                item.Checked = item.Tag is int tag && tag == value;
        }
    }

    static ToolStripMenuItem Item(string text, Action click)
    {
        var item = new ToolStripMenuItem(text);
        item.Click += (_, _) => click();
        return item;
    }

    static string TrimTooltip(string status)
    {
        string text = "SnowDesk — " + status;
        return text.Length <= 63 ? text : text[..60] + "…";
    }

    static Icon PaintIcon(bool enabled)
    {
        using var bitmap = new Bitmap(32, 32, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        graphics.Clear(Color.Transparent);
        Color background = enabled ? Color.FromArgb(255, 28, 72, 104) : Color.FromArgb(255, 96, 102, 108);
        using var brush = new SolidBrush(background);
        graphics.FillEllipse(brush, 1, 1, 30, 30);
        using var pen = new Pen(Color.FromArgb(255, 245, 250, 255), 1.7f)
        {
            StartCap = System.Drawing.Drawing2D.LineCap.Round,
            EndCap = System.Drawing.Drawing2D.LineCap.Round,
        };
        float cx = 16f;
        float cy = 16f;
        for (int i = 0; i < 3; i++)
        {
            double angle = i * Math.PI / 3d - Math.PI / 2d;
            float x2 = cx + (float)Math.Cos(angle) * 9f;
            float y2 = cy + (float)Math.Sin(angle) * 9f;
            float x1 = cx - (float)Math.Cos(angle) * 9f;
            float y1 = cy - (float)Math.Sin(angle) * 9f;
            graphics.DrawLine(pen, x1, y1, x2, y2);
        }

        IntPtr handle = bitmap.GetHicon();
        return Icon.FromHandle(handle);
    }
}
