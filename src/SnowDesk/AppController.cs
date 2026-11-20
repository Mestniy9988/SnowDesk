using System.Diagnostics;
using Microsoft.Win32;

namespace SnowDesk;

sealed class AppController : ApplicationContext
{
    readonly SettingsStore _store = new();
    readonly AppSettings _settings;
    readonly TrayHost _tray = new();
    readonly DesktopMonitor _monitor = new();
    readonly SnowOverlay _overlay = new();
    readonly AudioEngine _audio = new();
    readonly SnowSimulation _sim = new();
    readonly SnowDirector _director = new();
    readonly System.Windows.Forms.Timer _frame = new() { Interval = 33 };
    readonly System.Windows.Forms.Timer _poll = new() { Interval = 1000 };
    readonly System.Windows.Forms.Timer _debounce = new() { Interval = 70 };
    readonly Stopwatch _clock = new();

    DesktopObservation _observation = new(true, false, null, null, new Scene());
    Scene _scene = new();
    SettingsForm? _form;
    bool _active;
    bool _locked;
    bool _suspended;
    bool _batterySaver;
    bool _exit;
    string? _announcedAudioError;
    readonly bool _showSettingsOnStart;

    public AppController(bool showSettingsOnStart = false)
    {
        _showSettingsOnStart = showSettingsOnStart;
        _settings = _store.Load();
        try
        {
            Autostart.Apply(_settings.StartWithWindows);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            Debug.WriteLine(ex);
        }

        _tray.EnableToggled += () => Toggle(s => s.Enabled = !s.Enabled);
        _tray.MuteToggled += () => Toggle(s => s.Muted = !s.Muted);
        _tray.TaskbarToggled += () => Toggle(s => s.TaskbarSnow = !s.TaskbarSnow);
        _tray.AutostartToggled += () => Toggle(s => s.StartWithWindows = !s.StartWithWindows);
        _tray.IntensitySelected += level => Assign(s => s.Intensity = level);
        _tray.VolumeSelected += volume => Assign(s =>
        {
            s.Volume = volume;
            s.Muted = false;
        });
        _tray.PlowIntervalSelected += minutes => Assign(s => s.PlowIntervalMinutes = minutes);
        _tray.ChooseMusic += ChooseMusic;
        _tray.ResetMusic += ResetMusic;
        _tray.CallPlow += CallPlow;
        _tray.OpenSettings += OpenSettings;
        _tray.ExitRequested += ExitApplication;

        _monitor.Activity += OnActivity;
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            Recheck();
        };
        _poll.Tick += (_, _) => Recheck();
        _frame.Tick += (_, _) => OnFrame();

        var boot = new System.Windows.Forms.Timer { Interval = 40 };
        boot.Tick += (_, _) =>
        {
            boot.Stop();
            boot.Dispose();
            SystemEvents.SessionSwitch += OnSession;
            SystemEvents.DisplaySettingsChanged += OnDisplayChanged;
            SystemEvents.PowerModeChanged += OnPower;
            try
            {
                _monitor.Start();
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
            {
                Debug.WriteLine(ex);
            }

            _poll.Start();
            Recheck();
            if (_showSettingsOnStart)
                OpenSettings();
        };
        boot.Start();
        Publish();
    }

    void OnActivity()
    {
        if (!_debounce.Enabled)
            _debounce.Start();
    }

    void Recheck()
    {
        if (_exit)
            return;
        try
        {
            _batterySaver = NativeMethods.IsBatterySaverOn();
            _observation = _monitor.Query();
            if (_active)
                _scene = _observation.Scene;
            ApplyDesired();
            Publish();
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
        }
    }

    void OnFrame()
    {
        if (_exit)
            return;

        float dt = (float)_clock.Elapsed.TotalSeconds;
        _clock.Restart();
        if (dt <= 0f)
            dt = 0.016f;

        PushDirector();
        _director.Tick(dt);
        if (!_director.Render)
        {
            EnterDormant();
            Publish();
            return;
        }

        try
        {
            _scene = _observation.Scene;
            _sim.Intensity = _settings.Intensity;
            _sim.TaskbarSnow = _settings.TaskbarSnow;
            _sim.PlowIntervalMinutes = _settings.PlowIntervalMinutes;
            _sim.Update(dt, _scene);
            _overlay.EnsureSize(_scene);
            if (!_overlay.IsShown)
                _overlay.ShowOverlay();
            _overlay.Render(_sim, _director.Opacity);
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
        }

        SyncAudio();
        Publish();
    }

    void PushDirector()
    {
        _director.Enabled = _settings.Enabled;
        _director.DesktopFree = _observation.Free;
        _director.FullscreenBusy = _observation.FullscreenBusy;
        _director.Locked = _locked;
        _director.Suspended = _suspended;
        _director.BatterySaver = _batterySaver;
        _director.PauseOnBatterySaver = _settings.PauseOnBatterySaver;
        _director.Volume = _settings.Volume;
        _director.Muted = _settings.Muted;
    }

    void ApplyDesired()
    {
        if (_exit)
            return;

        PushDirector();
        _director.Tick(0f);
        _active = _director.Render;
        if (_director.Render)
        {
            _scene = _observation.Scene;
            try
            {
                _overlay.EnsureSize(_scene);
                if (!_overlay.IsShown)
                {
                    _overlay.ShowOverlay();
                    _form?.RaiseAbove();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
            }

            if (!_frame.Enabled)
            {
                _clock.Restart();
                _frame.Start();
            }

            SyncAudio();
            return;
        }

        EnterDormant();
    }

    void EnterDormant()
    {
        _frame.Stop();
        _audio.Stop();
        _overlay.HideOverlay();
        _overlay.ReleaseBuffer();
        _active = false;
    }

    void SyncAudio()
    {
        if (!_director.AudioRunning)
        {
            _audio.Stop();
            return;
        }

        _audio.Play(_settings.MusicPath, _director.AudioLevel);
        if (_audio.LastError != null && _audio.LastError != _announcedAudioError)
        {
            _announcedAudioError = _audio.LastError;
            string notice = string.IsNullOrWhiteSpace(_settings.MusicPath)
                ? "Не удалось открыть звуковое устройство. Снег идёт без музыки."
                : "Файл музыки не открылся. Играет встроенный ветер.";
            _tray.Balloon("SnowDesk", notice);
        }
    }

    void Publish()
    {
        string status = StatusText();
        _tray.Sync(_settings, status);
        if (_form != null && !_form.IsDisposed)
            _form.SetStatus(status);
    }

    string StatusText()
    {
        if (!_settings.Enabled)
            return "снег выключен";
        if (_locked)
            return "пауза: сеанс заблокирован";
        if (_suspended)
            return "пауза: система спит";
        if (_batterySaver && _settings.PauseOnBatterySaver)
            return "пауза: энергосбережение";
        if (!_observation.Free)
        {
            string who = string.IsNullOrWhiteSpace(_observation.BlockerTitle)
                ? (_observation.BlockerClass ?? "окно")
                : _observation.BlockerTitle;
            if (who.Length > 40)
                who = who[..40] + "…";
            return _observation.FullscreenBusy ? "пауза: полный экран «" + who + "»" : "пауза: «" + who + "»";
        }

        if (_director.Animating && _director.Target <= 0f)
            return "снег затихает";
        if (_director.Render && _director.Opacity < 0.98f)
            return "снег начинается";
        if (!_director.Render)
            return "ожидание свободного стола";

        foreach (DriftBand band in _sim.Bands)
        {
            if (band.Plowing)
                return "идёт снег, КАМАЗ в пути";
        }

        return "идёт снег";
    }

    void Toggle(Action<AppSettings> change) => Assign(change);

    void Assign(Action<AppSettings> change)
    {
        change(_settings);
        Commit(refreshForm: true);
    }

    void Commit(bool refreshForm)
    {
        _settings.Clamp();
        try
        {
            _store.Save(_settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine(ex);
        }

        try
        {
            Autostart.Apply(_settings.StartWithWindows);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException)
        {
            Debug.WriteLine(ex);
        }

        if (refreshForm)
            _form?.Bind();
        ApplyDesired();
        Publish();
    }

    void OpenSettings()
    {
        if (_form != null && !_form.IsDisposed)
        {
            _form.Bind();
            _form.SetStatus(StatusText());
            _form.RaiseAbove();
            return;
        }

        _form = new SettingsForm(_settings, () => Commit(refreshForm: false), CallPlow, ChooseMusic, ResetMusic);
        _form.FormClosed += (_, _) => _form = null;
        _form.SetStatus(StatusText());
        _form.Show();
        _form.RaiseAbove();
    }

    void ChooseMusic()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Музыка для SnowDesk",
            Filter = "Музыка (*.mp3;*.wav;*.ogg)|*.mp3;*.wav;*.ogg",
            CheckFileExists = true,
        };
        if (!string.IsNullOrWhiteSpace(_settings.MusicPath))
        {
            try
            {
                dialog.InitialDirectory = Path.GetDirectoryName(_settings.MusicPath);
                dialog.FileName = Path.GetFileName(_settings.MusicPath);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
            {
                Debug.WriteLine(ex);
            }
        }

        if (dialog.ShowDialog(_form) != DialogResult.OK)
            return;

        _settings.MusicPath = dialog.FileName;
        _announcedAudioError = null;
        _audio.Stop();
        Commit(refreshForm: true);
    }

    void ResetMusic()
    {
        _settings.MusicPath = null;
        _announcedAudioError = null;
        _audio.Stop();
        Commit(refreshForm: true);
    }

    void CallPlow()
    {
        if (!_settings.TaskbarSnow)
        {
            _tray.Balloon("SnowDesk", "Включите «Снег на панели задач», чтобы вызвать КАМАЗ.");
            return;
        }

        _sim.CallPlow();
        PushDirector();
        if (!_director.WantSnow)
            _tray.Balloon("SnowDesk", "КАМАЗ выедет, когда рабочий стол снова освободится.");
    }

    void OnSession(object? sender, SessionSwitchEventArgs e)
    {
        if (e.Reason == SessionSwitchReason.SessionLock)
            _locked = true;
        else if (e.Reason == SessionSwitchReason.SessionUnlock)
            _locked = false;
        else
            return;
        ApplyDesired();
        Publish();
    }

    void OnPower(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Suspend)
            _suspended = true;
        else if (e.Mode == PowerModes.Resume)
            _suspended = false;
        else
            return;
        ApplyDesired();
        Publish();
    }

    void OnDisplayChanged(object? sender, EventArgs e)
    {
        try
        {
            _observation = _monitor.Query();
            _scene = _observation.Scene;
            if (_active)
                _overlay.EnsureSize(_scene);
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
        }
    }

    void ExitApplication()
    {
        if (_exit)
            return;
        _exit = true;
        SystemEvents.SessionSwitch -= OnSession;
        SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
        SystemEvents.PowerModeChanged -= OnPower;
        _frame.Stop();
        _poll.Stop();
        _debounce.Stop();
        _audio.Dispose();
        _overlay.Dispose();
        _monitor.Dispose();
        if (_form != null && !_form.IsDisposed)
            _form.Close();
        _tray.Dispose();
        ExitThread();
    }
}
