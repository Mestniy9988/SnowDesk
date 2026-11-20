namespace SnowDesk;

/// <summary>
/// Decides when snow is drawing and when audio is actually running.
/// A fullscreen window snaps both off. Any other busy desktop fades them out
/// together, and a quick return reverses the fade instead of restarting it.
/// </summary>
public sealed class SnowDirector
{
    readonly OpacityFader _fader = new();

    public bool Enabled { get; set; } = true;
    public bool DesktopFree { get; set; }
    public bool FullscreenBusy { get; set; }
    public bool Locked { get; set; }
    public bool Suspended { get; set; }
    public bool BatterySaver { get; set; }
    public bool PauseOnBatterySaver { get; set; } = true;
    public int Volume { get; set; } = 35;
    public bool Muted { get; set; }

    public float Opacity => _fader.Opacity;
    public float Target => _fader.Target;
    public bool Animating => _fader.Animating;
    public bool Render { get; private set; }
    public bool AudioRunning { get; private set; }
    public float AudioLevel { get; private set; }

    public bool WantSnow =>
        Enabled
        && DesktopFree
        && !Locked
        && !Suspended
        && !(BatterySaver && PauseOnBatterySaver);

    public void Tick(float dt)
    {
        if (dt < 0f)
            dt = 0f;
        if (dt > 0.1f)
            dt = 0.1f;

        if (WantSnow)
        {
            FadeToward(1f, 1.8f);
            Render = true;
        }
        else if (FullscreenBusy && (Render || _fader.Opacity > 0f || _fader.Animating))
        {
            _fader.Snap(0f);
            Render = false;
        }
        else if (Render || _fader.Opacity > 0.001f || _fader.Animating)
        {
            FadeToward(0f, 1.75f);
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
