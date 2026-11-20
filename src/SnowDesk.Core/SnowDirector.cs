namespace SnowDesk;

/// <summary>
/// Snow and audio follow the manual on/off switch. They fade together in about 1.5 seconds.
/// A fullscreen window pauses them only when that option is enabled.
/// </summary>
public sealed class SnowDirector
{
    public const float FadeSeconds = 1.5f;

    readonly OpacityFader _fader = new();

    public bool Enabled { get; set; } = true;
    public bool FullscreenBusy { get; set; }
    public bool PauseOnFullscreen { get; set; }
    public int Volume { get; set; } = 25;
    public bool Muted { get; set; }

    public float Opacity => _fader.Opacity;
    public float Target => _fader.Target;
    public bool Animating => _fader.Animating;
    public bool Render { get; private set; }
    public bool AudioRunning { get; private set; }
    public float AudioLevel { get; private set; }

    public bool WantSnow => Enabled && !(PauseOnFullscreen && FullscreenBusy);

    public void Tick(float dt)
    {
        if (dt < 0f)
            dt = 0f;
        if (dt > 0.1f)
            dt = 0.1f;

        if (WantSnow)
        {
            FadeToward(1f, FadeSeconds);
            Render = true;
        }
        else if (Render || _fader.Opacity > 0.001f || _fader.Animating)
        {
            FadeToward(0f, FadeSeconds);
            Render = true;
        }

        if (Render)
            _fader.Update(dt);

        if (_fader.Opacity <= 0.001f && !_fader.Animating)
            Render = false;

        bool fadingIn = _fader.Animating && _fader.Target > 0.5f;
        AudioRunning = Render && !Muted && Volume > 0 && (_fader.Opacity > 0.01f || fadingIn);
        AudioLevel = AudioRunning
            ? Volume / 100f * MathF.Max(_fader.Opacity, fadingIn ? 0.02f : 0f)
            : 0f;
    }

    void FadeToward(float target, float seconds)
    {
        if (!_fader.Animating && MathF.Abs(_fader.Opacity - target) < 0.001f)
            return;
        if (_fader.Animating && MathF.Abs(_fader.Target - target) < 0.001f)
            return;
        _fader.FadeTo(target, seconds);
    }
}
