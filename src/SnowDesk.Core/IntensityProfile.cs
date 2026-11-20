namespace SnowDesk;

/// <summary>
/// Maps the 1–100 intensity slider onto flake count, size, speed and drift.
/// Level 1 is a few tiny slow flakes. Level 100 is a capped blizzard that
/// fills the taskbar drift in about a minute.
/// </summary>
public readonly record struct IntensityProfile(
    int Level,
    int TargetFlakes,
    float MinSize,
    float MaxSize,
    float MinFall,
    float MaxFall,
    float Sway,
    float MaxDrift,
    float FillSeconds)
{
    public float AverageFall => (MinFall + MaxFall) * 0.5f;

    public string Description => Level switch
    {
        <= 8 => "Редкие медленные снежинки",
        <= 20 => "Лёгкий снег",
        <= 45 => "Спокойный снегопад",
        <= 70 => "Густой снег",
        <= 90 => "Сильный снегопад",
        _ => "Метель",
    };

    public static IntensityProfile FromLevel(int level)
    {
        level = Math.Clamp(level, 1, 100);
        float t = (level - 1) / 99f;
        float countT = MathF.Pow(t, 1.05f);
        float speedT = MathF.Pow(t, 1.15f);
        float sizeT = MathF.Pow(t, 1.25f);

        return new IntensityProfile(
            Level: level,
            TargetFlakes: (int)MathF.Round(Lerp(8f, 460f, countT)),
            MinSize: Lerp(1.3f, 2.6f, t),
            MaxSize: Lerp(2.8f, 18f, sizeT),
            MinFall: Lerp(8f, 60f, speedT),
            MaxFall: Lerp(18f, 300f, speedT),
            Sway: Lerp(5f, 42f, t),
            MaxDrift: Lerp(18f, 32f, MathF.Pow(t, 0.75f)),
            FillSeconds: 60f * MathF.Pow(25f, 1f - t));
    }

    public static float Lerp(float a, float b, float t) => a + (b - a) * t;
}
