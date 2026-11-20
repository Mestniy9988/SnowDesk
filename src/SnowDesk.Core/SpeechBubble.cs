namespace SnowDesk;

/// <summary>
/// Where the plow's message sits. The glyphs themselves are drawn later with a font.
/// </summary>
public static class SpeechBubble
{
    public const string DefaultText = "Скоро Новый Год, я свои Дедлайны как видишь закрываю, а ты?";
    public const int MaxLength = 200;
    public const int PadX = 16;
    public const int PadY = 12;
    public const int Tail = 14;

    public readonly record struct Placement(int X, int Y, int TailX, bool TailDown, float Opacity);

    public static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return DefaultText;

        string cleaned = text.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
        if (cleaned.Length > MaxLength)
            cleaned = cleaned[..MaxLength];
        return cleaned.Length == 0 ? DefaultText : cleaned;
    }

    public static Placement Place(
        int anchorX,
        int anchorY,
        int screenWidth,
        int screenHeight,
        int boxWidth,
        int boxHeight,
        bool preferAbove,
        float travel,
        float length)
    {
        int boxW = Math.Max(1, boxWidth);
        int boxH = Math.Max(1, boxHeight);
        int x = anchorX - boxW / 2;
        int y = preferAbove ? anchorY - boxH - Tail - 8 : anchorY + Tail + 10;
        int maxX = Math.Max(4, screenWidth - boxW - 4);
        int maxY = Math.Max(4, screenHeight - boxH - Tail - 4);
        x = Math.Clamp(x, 4, maxX);
        y = Math.Clamp(y, 4, maxY);
        int tailX = Math.Clamp(anchorX, x + 18, Math.Max(x + 18, x + boxW - 18));
        float enter = Math.Clamp((travel + 24f) / 110f, 0f, 1f);
        float leave = Math.Clamp((length + 16f - travel) / 110f, 0f, 1f);
        return new Placement(x, y, tailX, preferAbove, MathF.Min(enter, leave));
    }
}
