using System.Reflection;

namespace SnowDesk;

/// <summary>
/// New-year line drawn above the plow. The glyphs are pre-rendered so Cyrillic
/// does not depend on the fonts installed on the machine.
/// </summary>
public static class SpeechBubble
{
    public const string Text = "Скоро Новый Год, я свои Дедлайны как видишь закрываю, а ты?";
    public const int PadX = 16;
    public const int PadY = 12;

    public static int TextWidth { get; private set; }
    public static int TextHeight { get; private set; }
    public static byte[] TextPixels { get; private set; } = Array.Empty<byte>();
    public static bool Ready { get; private set; }
    public static int BoxWidth => TextWidth + PadX * 2;
    public static int BoxHeight => TextHeight + PadY * 2;

    public readonly record struct Placement(int X, int Y, int TailX, bool TailDown, float Opacity);

    static SpeechBubble()
    {
        using Stream? stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("SnowDesk.SpeechBubble.bin");
        if (stream == null || stream.Length < 8)
            return;

        var header = new byte[8];
        if (stream.Read(header, 0, 8) != 8)
            return;
        int width = BitConverter.ToInt32(header, 0);
        int height = BitConverter.ToInt32(header, 4);
        int bytes = width * height * 4;
        if (width <= 0 || height <= 0 || stream.Length < 8 + bytes)
            return;

        var pixels = new byte[bytes];
        int read = 0;
        while (read < bytes)
        {
            int n = stream.Read(pixels, read, bytes - read);
            if (n <= 0)
                return;
            read += n;
        }

        TextWidth = width;
        TextHeight = height;
        TextPixels = pixels;
        Ready = true;
    }

    public static Placement Place(
        int anchorX,
        int anchorY,
        int screenWidth,
        int screenHeight,
        bool preferAbove,
        float travel,
        float length)
    {
        const int tail = 14;
        int boxW = Math.Max(1, BoxWidth);
        int boxH = Math.Max(1, BoxHeight);
        int x = anchorX - boxW / 2;
        int y = preferAbove ? anchorY - boxH - tail - 8 : anchorY + tail + 10;
        int maxX = Math.Max(4, screenWidth - boxW - 4);
        int maxY = Math.Max(4, screenHeight - boxH - tail - 4);
        x = Math.Clamp(x, 4, maxX);
        y = Math.Clamp(y, 4, maxY);
        int tailX = Math.Clamp(anchorX, x + 18, Math.Max(x + 18, x + boxW - 18));
        float enter = Math.Clamp((travel + 24f) / 110f, 0f, 1f);
        float leave = Math.Clamp((length + 16f - travel) / 110f, 0f, 1f);
        return new Placement(x, y, tailX, preferAbove, MathF.Min(enter, leave));
    }
}
