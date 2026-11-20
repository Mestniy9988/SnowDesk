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
    readonly OpacityFader _fader = new();
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

    public AppController()
    {
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
            _monitor.Start();
            _poll.Start();
            Recheck();
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
        else if (dt > 0.1f)
            dt = 0.1f;

        _fader.Update(dt);
        if (_fader.Opacity <= 0.001f && !_fader.Animating)
        {
            EnterDormant();
            Publish();
            return;
        }

        try
        {
            _sim.Intensity = _settings.Intensity;
            _sim.TaskbarSnow = _settings.TaskbarSnow;
            _sim.PlowIntervalMinutes = _settings.PlowIntervalMinutes;
            _sim.Update(dt, _scene);
            _overlay.EnsureSize(_scene);
            _overlay.Render(_sim, _fader.Opacity);
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
        }

        SyncAudio();
        Publish();
    }

    bool WantSnow() =>
        _settings.Enabled
        && !_locked
        && !_suspended
        && !(_batterySaver && _settings.PauseOnBatterySaver)
        && _observation.Free;

    void ApplyDesired()
    {
        if (WantSnow())
        {
            BeginFadeIn();
            return;
        }

        if (_observation.FullscreenBusy && (_active || _overlay.IsShown || _fader.Opacity > 0f))
        {
            EnterDormant();
            return;
        }

        if (_active || _fader.Opacity > 0.001f || _fader.Animating)
            BeginFadeOut();
    }

    void BeginFadeIn()
    {
        _scene = _observation.Scene;
        bool opening = !_overlay.IsShown || !_active || _fader.Target < 0.5f;
        _overlay.EnsureSize(_scene);
        if (opening)
        {
            _overlay.ShowOverlay();
            _form?.RaiseAbove();
        }

        bool starting = !_frame.Enabled;
        _active = true;
        if (starting)
        {
            _clock.Restart();
            _frame.Start();
        }

        if (_fader.Opacity < 0.999f || _fader.Target < 0.999f)
            _fader.FadeTo(1f, 1.8f);
        SyncAudio();
    }

    void BeginFadeOut()
    {
        _fader.FadeTo(0f, 1.75f);
        if (!_frame.Enabled)
        {
            _active = true;
            _clock.Restart();
            _frame.Start();
        }
    }

    void EnterDormant()
    {
        _fader.Snap(0f);
        _frame.Stop();
        _audio.Stop();
        _overlay.HideOverlay();
        _overlay.ReleaseBuffer();
        _active = false;
    }

    void SyncAudio()
    {
        bool fadingIn = _fader.Animating && _fader.Target > 0.5f;
        bool audible = _active && !_settings.Muted && _settings.Volume > 0 && (_fader.Opacity > 0.01f || fadingIn);
        if (!audible)
        {
            _audio.Stop();
            return;
        }

        _audio.Play(_settings.MusicPath, _settings.Volume / 100f * Math.Max(_fader.Opacity, fadingIn ? 0.02f : 0f));
        if (_audio.LastError != null && _audio.LastError != _announcedAudioError)
        {
            _announcedAudioError = _audio.LastError;
            _tray.Balloon("SnowDesk", "Файл музыки не открылся. Играет встроенный ветер.");
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

        if (_fader.Animating && _fader.Target <= 0f)
            return "снег затихает";
        if (_active && _fader.Opacity < 0.98f)
            return "снег начинается";
        if (!_active)
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
        if (!WantSnow())
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
