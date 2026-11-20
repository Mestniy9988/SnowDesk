using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace SnowDesk;

/// <summary>
/// Draws the plow's message with a Cyrillic-capable font. Segoe UI on Windows,
/// then any installed face that actually contains the requested family.
/// </summary>
sealed class BubblePainter
{
    const int MaxTextWidth = 440;
    string _cachedText = "";
    Bitmap? _text;
    Font? _font;

    public int TextWidth { get; private set; }
    public int TextHeight { get; private set; }
    public int BoxWidth => TextWidth + SpeechBubble.PadX * 2;
    public int BoxHeight => TextHeight + SpeechBubble.PadY * 2;

    public void Draw(Span<byte> pixels, int width, int height, SnowSimulation simulation, float opacity, string? message, bool show)
    {
        if (!show || opacity < 0.02f)
            return;

        string text = SpeechBubble.Normalize(message);
        EnsureText(text);
        if (_text == null)
            return;

        foreach (DriftBand band in simulation.Bands)
        {
            if (!SnowRasterizer.TryTruckAnchor(band, simulation.TruckScalePercent, out int anchorX, out int anchorY, out bool above))
                continue;

            SpeechBubble.Placement place = SpeechBubble.Place(
                anchorX,
                anchorY,
                width,
                height,
                BoxWidth,
                BoxHeight,
                above,
                band.Travel,
                band.Geometry.Length);
            if (place.Opacity < 0.04f)
                continue;

            BlitBubble(pixels, width, height, place, (int)(opacity * place.Opacity * 255f));
        }
    }

    void EnsureText(string text)
    {
        if (_text != null && _cachedText == text)
            return;

        _text?.Dispose();
        _font ??= PickFont();
        using var measure = new Bitmap(8, 8, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using Graphics measureGraphics = Graphics.FromImage(measure);
        measureGraphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        using var format = new StringFormat(StringFormat.GenericTypographic) { FormatFlags = StringFormatFlags.LineLimit };
        SizeF size = measureGraphics.MeasureString(text, _font, MaxTextWidth, format);
        TextWidth = Math.Clamp((int)Math.Ceiling(size.Width) + 4, 8, MaxTextWidth + 8);
        TextHeight = Math.Max(8, (int)Math.Ceiling(size.Height) + 4);

        _text = new Bitmap(TextWidth, TextHeight, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using Graphics graphics = Graphics.FromImage(_text);
        graphics.Clear(Color.Transparent);
        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        using var brush = new SolidBrush(Color.FromArgb(255, 28, 40, 52));
        graphics.DrawString(text, _font, brush, new RectangleF(0, 0, TextWidth, TextHeight), format);
        _cachedText = text;
    }

    static Font PickFont()
    {
        foreach (string name in new[] { "Segoe UI", "Arial", "Tahoma", "DejaVu Sans", "Liberation Sans" })
        {
            try
            {
                var font = new Font(name, 16f, FontStyle.Regular, GraphicsUnit.Pixel);
                if (font.FontFamily.Name.Contains(name, StringComparison.OrdinalIgnoreCase)
                    || font.Name.Contains(name, StringComparison.OrdinalIgnoreCase))
                    return font;
                font.Dispose();
            }
            catch (ArgumentException)
            {
            }
        }

        return new Font(FontFamily.GenericSansSerif, 16f, FontStyle.Regular, GraphicsUnit.Pixel);
    }

    void BlitBubble(Span<byte> pixels, int width, int height, SpeechBubble.Placement place, int fade)
    {
        if (_text == null || fade <= 0)
            return;

        FillRound(pixels, width, height, place.X + 2, place.Y + 3, BoxWidth, BoxHeight, 16, Math.Max(1, fade / 5), 40, 48, 60);
        FillRound(pixels, width, height, place.X, place.Y, BoxWidth, BoxHeight, 16, fade, 252, 250, 255);
        FillTail(pixels, width, height, place.TailX, place.TailDown ? place.Y + BoxHeight - 1 : place.Y, place.TailDown ? 1 : -1, fade);

        var data = _text.LockBits(
            new Rectangle(0, 0, _text.Width, _text.Height),
            System.Drawing.Imaging.ImageLockMode.ReadOnly,
            System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        try
        {
            int stride = data.Stride;
            var row = new byte[Math.Abs(stride)];
            for (int y = 0; y < _text.Height; y++)
            {
                int destY = place.Y + SpeechBubble.PadY + y;
                if ((uint)destY >= (uint)height)
                    continue;
                System.Runtime.InteropServices.Marshal.Copy(data.Scan0 + y * stride, row, 0, row.Length);
                for (int x = 0; x < _text.Width; x++)
                {
                    int destX = place.X + SpeechBubble.PadX + x;
                    if ((uint)destX >= (uint)width)
                        continue;
                    int src = x * 4;
                    int alpha = (row[src + 3] * fade) >> 8;
                    if (alpha == 0)
                        continue;
                    Blend(pixels, (destY * width + destX) * 4, row[src], row[src + 1], row[src + 2], alpha);
                }
            }
        }
        finally
        {
            _text.UnlockBits(data);
        }
    }

    static void FillTail(Span<byte> pixels, int width, int height, int tipX, int edgeY, int direction, int fade)
    {
        for (int i = 0; i < SpeechBubble.Tail; i++)
        {
            int half = 10 - i * 10 / SpeechBubble.Tail;
            int y = edgeY + direction * i;
            if ((uint)y >= (uint)height)
                continue;
            for (int x = tipX - half; x <= tipX + half; x++)
            {
                if ((uint)x >= (uint)width)
                    continue;
                Blend(pixels, (y * width + x) * 4, 252, 250, 255, fade);
            }
        }
    }

    static void FillRound(Span<byte> pixels, int width, int height, int x, int y, int w, int h, int radius, int fade, byte b, byte g, byte r)
    {
        if (fade <= 0 || w <= 0 || h <= 0)
            return;
        radius = Math.Min(radius, Math.Min(w, h) / 2);
        int r2 = radius * radius;
        for (int yy = 0; yy < h; yy++)
        {
            int py = y + yy;
            if ((uint)py >= (uint)height)
                continue;
            for (int xx = 0; xx < w; xx++)
            {
                int px = x + xx;
                if ((uint)px >= (uint)width)
                    continue;
                if (!Inside(xx, yy, w, h, radius, r2))
                    continue;
                Blend(pixels, (py * width + px) * 4, b, g, r, fade);
            }
        }
    }

    static bool Inside(int x, int y, int w, int h, int radius, int r2)
    {
        if (x >= radius && x < w - radius)
            return true;
        if (y >= radius && y < h - radius)
            return true;
        int cx = x < radius ? radius : w - radius - 1;
        int cy = y < radius ? radius : h - radius - 1;
        int dx = x - cx;
        int dy = y - cy;
        return dx * dx + dy * dy <= r2;
    }

    static void Blend(Span<byte> pixels, int index, byte b, byte g, byte r, int alpha)
    {
        int inv = 255 - alpha;
        pixels[index] = (byte)(((b * alpha) >> 8) + ((pixels[index] * inv) >> 8));
        pixels[index + 1] = (byte)(((g * alpha) >> 8) + ((pixels[index + 1] * inv) >> 8));
        pixels[index + 2] = (byte)(((r * alpha) >> 8) + ((pixels[index + 2] * inv) >> 8));
        pixels[index + 3] = (byte)(alpha + ((pixels[index + 3] * inv) >> 8));
    }
}
