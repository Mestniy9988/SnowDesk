namespace SnowDesk;

/// <summary>
/// Maps the 1–100 intensity slider onto flake count, size, speed and drift.
/// Level 1 is a few tiny slow flakes. Level 100 is a heavy blizzard, about
/// twice the previous cap, and it fills the taskbar drift in about half a minute.
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
        // Steep curves keep the middle of the slider calm and spend the extra
        // flakes, size and speed on the top of the range.
        float countT = MathF.Pow(t, 1.75f);
        float speedT = MathF.Pow(t, 1.45f);
        float sizeT = MathF.Pow(t, 1.35f);
        float fillEase = MathF.Pow(t, 2f);

        return new IntensityProfile(
            Level: level,
            TargetFlakes: (int)MathF.Round(Lerp(8f, 920f, countT)),
            MinSize: Lerp(1.3f, 3.2f, t),
            MaxSize: Lerp(2.8f, 32f, sizeT),
            MinFall: Lerp(8f, 90f, speedT),
            MaxFall: Lerp(18f, 520f, speedT),
            Sway: Lerp(5f, 55f, t),
            MaxDrift: Lerp(18f, 32f, MathF.Pow(t, 0.75f)),
            FillSeconds: 60f * MathF.Pow(25f, 1f - t) * Lerp(1f, 0.45f, fillEase));
    }

    public static float Lerp(float a, float b, float t) => a + (b - a) * t;
}
