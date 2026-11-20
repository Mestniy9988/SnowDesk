namespace SnowDesk;

/// <summary>Linear opacity ramp used to fade snow and music together.</summary>
public sealed class OpacityFader
{
    public float Opacity { get; private set; }
    public bool Animating { get; private set; }
    public float Target => _target;

    float _target;
    float _rate;

    public void Snap(float value)
    {
        Opacity = Math.Clamp(value, 0f, 1f);
        _target = Opacity;
        Animating = false;
    }

    public void FadeTo(float target, float seconds)
    {
        target = Math.Clamp(target, 0f, 1f);
        _target = target;
        float distance = MathF.Abs(target - Opacity);
        if (distance < 0.001f)
        {
            Opacity = target;
            Animating = false;
            return;
        }

        // Constant speed across the whole 0..1 range, so a fade that is reversed
        // halfway finishes in half the time instead of restarting the full duration.
        _rate = 1f / MathF.Max(0.05f, seconds);
        Animating = true;
    }

    public void Update(float dt)
    {
        if (!Animating || dt <= 0f)
            return;

        float direction = MathF.Sign(_target - Opacity);
        Opacity += direction * _rate * dt;
        bool arrived = direction > 0f ? Opacity >= _target : Opacity <= _target;
        if (arrived || direction == 0f)
        {
            Opacity = _target;
            Animating = false;
        }
    }
}
