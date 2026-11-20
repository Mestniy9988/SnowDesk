namespace SnowDesk;

public sealed class FrameBuffer
{
    public int Width { get; }
    public int Height { get; }
    public byte[] Pixels { get; }

    public FrameBuffer(int width, int height)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width));
        Width = width;
        Height = height;
        Pixels = new byte[width * height * 4];
    }

    public void Clear() => Array.Clear(Pixels);
}

public readonly ref struct Surface
{
    public readonly Span<byte> Pixels;
    public readonly int Width;
    public readonly int Height;

    public Surface(Span<byte> pixels, int width, int height)
    {
        Pixels = pixels;
        Width = width;
        Height = height;
    }
}

public enum SpriteTransform
{
    None,
    FlipVertical,
    RotateClockwise,
    RotateCounterClockwise,
}

public static class SnowRasterizer
{
    public static void Render(FrameBuffer buffer, SnowSimulation simulation, float opacity)
        => Render(buffer.Pixels, buffer.Width, buffer.Height, simulation, opacity);

    public static void Render(Span<byte> pixels, int width, int height, SnowSimulation simulation, float opacity)
    {
        long bytes = (long)width * height * 4;
        if (opacity <= 0.001f || width <= 0 || height <= 0 || bytes > pixels.Length)
            return;

        Draw(new Surface(pixels, width, height), simulation, opacity);
    }

    static void Draw(Surface surface, SnowSimulation simulation, float opacity)
    {
        int fade = (int)Math.Clamp(opacity * 255f, 0f, 255f);
        IReadOnlyList<Flake> flakes = simulation.Flakes;
        for (int i = 0; i < flakes.Count; i++)
            DrawFlake(surface, flakes[i], fade);

        if (!simulation.TaskbarSnow)
            return;

        IReadOnlyList<DriftBand> bands = simulation.Bands;
        for (int i = 0; i < bands.Count; i++)
        {
            DriftBand band = bands[i];
            DrawDrift(surface, band, fade);
            DrawPuffs(surface, band, fade);
            if (band.Plowing)
                DrawTruck(surface, band, fade);
        }
    }

    static void DrawFlake(Surface surface, Flake flake, int fade)
    {
        int sprite = flake.Sprite;
        if ((uint)sprite >= (uint)FlakeSprites.Masks.Length)
            return;

        int diameter = FlakeSprites.Diameters[sprite];
        int alpha = (int)(flake.Alpha * fade);
        int x = (int)flake.X - diameter / 2;
        int y = (int)flake.Y - diameter / 2;
        BlitMask(surface, FlakeSprites.Masks[sprite], diameter, x, y, alpha, 248, 250, 255);

        if (flake.Speed > 170f)
        {
            int ghost = alpha * 2 / 5;
            int lift = Math.Max(2, (int)(flake.Size * 0.85f));
            BlitMask(surface, FlakeSprites.Masks[sprite], diameter, x, y - lift, ghost, 248, 250, 255);
        }
    }

    static void DrawPuffs(Surface surface, DriftBand band, int fade)
    {
        List<SnowPuff> puffs = band.Puffs;
        for (int i = 0; i < puffs.Count; i++)
        {
            SnowPuff puff = puffs[i];
            int sprite = FlakeSprites.Nearest(puff.Size);
            int diameter = FlakeSprites.Diameters[sprite];
            float life = puff.MaxLife <= 0f ? 0f : puff.Life / puff.MaxLife;
            int alpha = (int)(fade * life);
            BlitMask(surface, FlakeSprites.Masks[sprite], diameter,
                (int)puff.X - diameter / 2, (int)puff.Y - diameter / 2, alpha, 255, 255, 255);
        }
    }

    static void DrawDrift(Surface surface, DriftBand band, int fade)
    {
        TaskbarBand g = band.Geometry;
        float[] heights = band.Heights;
        switch (g.Edge)
        {
            case TaskbarEdge.Bottom:
                for (int column = 0; column < heights.Length; column++)
                {
                    float height = heights[column];
                    if (height < 0.4f)
                        continue;
                    int x = g.X + column;
                    int y0 = (int)MathF.Floor(g.Y - height);
                    FillVertical(surface, x, y0, g.Y, fade, crestAtStart: true);
                }
                break;
            case TaskbarEdge.Top:
                for (int column = 0; column < heights.Length; column++)
                {
                    float height = heights[column];
                    if (height < 0.4f)
                        continue;
                    int x = g.X + column;
                    int y0 = g.Y + g.Height;
                    FillVertical(surface, x, y0, y0 + (int)MathF.Ceiling(height), fade, crestAtStart: false);
                }
                break;
            case TaskbarEdge.Left:
                for (int column = 0; column < heights.Length; column++)
                {
                    float height = heights[column];
                    if (height < 0.4f)
                        continue;
                    int y = g.Y + column;
                    int x0 = g.X + g.Width;
                    FillHorizontal(surface, y, x0, x0 + (int)MathF.Ceiling(height), fade, crestAtStart: false);
                }
                break;
            default:
                for (int column = 0; column < heights.Length; column++)
                {
                    float height = heights[column];
                    if (height < 0.4f)
                        continue;
                    int y = g.Y + column;
                    int x1 = g.X;
                    FillHorizontal(surface, y, x1 - (int)MathF.Ceiling(height), x1, fade, crestAtStart: true);
                }
                break;
        }
    }

    static void FillVertical(Surface surface, int x, int y0, int y1, int fade, bool crestAtStart)
    {
        if ((uint)x >= (uint)surface.Width)
            return;
        if (y0 > y1)
            (y0, y1) = (y1, y0);
        int span = Math.Max(1, y1 - y0);
        int start = Math.Max(0, y0);
        int end = Math.Min(surface.Height, y1);
        for (int y = start; y < end; y++)
        {
            float along = (y - y0) / (float)span;
            float crest = crestAtStart ? along : 1f - along;
            int alpha = CrestAlpha(crest, fade);
            Blend(surface.Pixels, (y * surface.Width + x) * 4, 255, 250, 248, alpha);
        }
    }

    static void FillHorizontal(Surface surface, int y, int x0, int x1, int fade, bool crestAtStart)
    {
        if ((uint)y >= (uint)surface.Height)
            return;
        if (x0 > x1)
            (x0, x1) = (x1, x0);
        int span = Math.Max(1, x1 - x0);
        int start = Math.Max(0, x0);
        int end = Math.Min(surface.Width, x1);
        int row = y * surface.Width;
        for (int x = start; x < end; x++)
        {
            float along = (x - x0) / (float)span;
            float crest = crestAtStart ? along : 1f - along;
            int alpha = CrestAlpha(crest, fade);
            Blend(surface.Pixels, (row + x) * 4, 255, 250, 248, alpha);
        }
    }

    static int CrestAlpha(float crest, int fade)
    {
        float edge = crest < 0.18f ? 0.45f + crest / 0.18f * 0.55f : 1f;
        return (int)(230f * edge * fade / 255f);
    }

    static void DrawTruck(Surface surface, DriftBand band, int fade)
    {
        TaskbarBand g = band.Geometry;
        SpriteTransform transform = g.Edge switch
        {
            TaskbarEdge.Top => SpriteTransform.FlipVertical,
            TaskbarEdge.Left => SpriteTransform.RotateClockwise,
            TaskbarEdge.Right => SpriteTransform.RotateCounterClockwise,
            _ => SpriteTransform.None,
        };

        (int bladeX, int bladeY) = Map(KamazSprite.BladeX, KamazSprite.BladeY, transform);
        (int wheelX, int wheelY) = Map(KamazSprite.WheelX, KamazSprite.WheelY, transform);
        (int beaconX, int beaconY) = Map(KamazSprite.BeaconX, KamazSprite.BeaconY, transform);

        int travel = (int)band.Travel;
        int drawX;
        int drawY;
        switch (g.Edge)
        {
            case TaskbarEdge.Bottom:
                drawX = g.X + travel - bladeX;
                drawY = (g.Y - 1) - wheelY;
                break;
            case TaskbarEdge.Top:
                drawX = g.X + travel - bladeX;
                drawY = (g.Y + g.Height) - wheelY;
                break;
            case TaskbarEdge.Left:
                drawX = (g.X + g.Width) - wheelX;
                drawY = g.Y + travel - bladeY;
                break;
            default:
                drawX = (g.X - 1) - wheelX;
                drawY = g.Y + travel - bladeY;
                break;
        }

        BlitSprite(surface, KamazSprite.Pixels, KamazSprite.Width, KamazSprite.Height, drawX, drawY, fade, transform);

        bool lampOn = (band.Blink % 0.62f) < 0.16f;
        int lampX = drawX + beaconX;
        int lampY = drawY + beaconY;
        if (lampOn)
            DrawGlow(surface, lampX, lampY, 10, fade, 60, 210, 255);
        else
            DrawGlow(surface, lampX, lampY, 4, fade / 2, 40, 120, 160);

        DrawSpeechBubble(surface, band, g, drawX + beaconX, drawY + beaconY, fade);
    }

    static void DrawSpeechBubble(Surface surface, DriftBand band, TaskbarBand bar, int anchorX, int anchorY, int fade)
    {
        if (!SpeechBubble.Ready)
            return;

        bool above = bar.Edge != TaskbarEdge.Top;
        SpeechBubble.Placement place = SpeechBubble.Place(
            anchorX,
            anchorY,
            surface.Width,
            surface.Height,
            above,
            band.Travel,
            bar.Length);
        if (place.Opacity < 0.04f)
            return;

        int localFade = (int)(fade * place.Opacity);
        int x = place.X;
        int y = place.Y;
        int w = SpeechBubble.BoxWidth;
        int h = SpeechBubble.BoxHeight;
        FillRound(surface, x + 2, y + 3, w, h, 16, localFade / 5, 40, 48, 60);
        FillRound(surface, x, y, w, h, 16, localFade, 252, 250, 255);
        if (place.TailDown)
            FillTail(surface, place.TailX, y + h - 1, 1, localFade);
        else
            FillTail(surface, place.TailX, y, -1, localFade);

        BlitSprite(
            surface,
            SpeechBubble.TextPixels,
            SpeechBubble.TextWidth,
            SpeechBubble.TextHeight,
            x + SpeechBubble.PadX,
            y + SpeechBubble.PadY,
            localFade,
            SpriteTransform.None);
    }

    static void FillTail(Surface surface, int tipX, int edgeY, int direction, int fade)
    {
        int length = 14;
        for (int i = 0; i < length; i++)
        {
            int half = 10 - i * 10 / length;
            int y = edgeY + direction * i;
            for (int x = tipX - half; x <= tipX + half; x++)
            {
                if ((uint)x >= (uint)surface.Width || (uint)y >= (uint)surface.Height)
                    continue;
                Blend(surface.Pixels, (y * surface.Width + x) * 4, 252, 250, 255, fade);
            }
        }
    }

    static void FillRound(Surface surface, int x, int y, int w, int h, int radius, int fade, byte b, byte g, byte r)
    {
        if (fade <= 0 || w <= 0 || h <= 0)
            return;
        radius = Math.Min(radius, Math.Min(w, h) / 2);
        int r2 = radius * radius;
        for (int yy = 0; yy < h; yy++)
        {
            int py = y + yy;
            if ((uint)py >= (uint)surface.Height)
                continue;
            for (int xx = 0; xx < w; xx++)
            {
                int px = x + xx;
                if ((uint)px >= (uint)surface.Width)
                    continue;
                if (!InsideRound(xx, yy, w, h, radius, r2))
                    continue;
                Blend(surface.Pixels, (py * surface.Width + px) * 4, b, g, r, fade);
            }
        }
    }

    static bool InsideRound(int x, int y, int w, int h, int radius, int r2)
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

    static (int x, int y) Map(int sx, int sy, SpriteTransform transform)
    {
        return transform switch
        {
            SpriteTransform.FlipVertical => (sx, KamazSprite.Height - 1 - sy),
            SpriteTransform.RotateClockwise => (KamazSprite.Height - 1 - sy, sx),
            SpriteTransform.RotateCounterClockwise => (sy, KamazSprite.Width - 1 - sx),
            _ => (sx, sy),
        };
    }

    static void DrawGlow(Surface surface, int cx, int cy, int radius, int fade, byte b, byte g, byte r)
    {
        int r2 = radius * radius;
        for (int y = cy - radius; y <= cy + radius; y++)
        {
            if ((uint)y >= (uint)surface.Height)
                continue;
            for (int x = cx - radius; x <= cx + radius; x++)
            {
                if ((uint)x >= (uint)surface.Width)
                    continue;
                int dx = x - cx;
                int dy = y - cy;
                int d2 = dx * dx + dy * dy;
                if (d2 > r2)
                    continue;
                float t = 1f - d2 / (float)(r2 + 1);
                int alpha = (int)(fade * t);
                Blend(surface.Pixels, (y * surface.Width + x) * 4, b, g, r, alpha);
            }
        }
    }

    static void BlitMask(Surface surface, byte[] mask, int diameter, int dx, int dy, int alpha, byte b, byte g, byte r)
    {
        if (alpha <= 0)
            return;

        int x0 = Math.Max(0, -dx);
        int y0 = Math.Max(0, -dy);
        int x1 = Math.Min(diameter, surface.Width - dx);
        int y1 = Math.Min(diameter, surface.Height - dy);
        if (x0 >= x1 || y0 >= y1)
            return;

        Span<byte> dest = surface.Pixels;
        int stride = surface.Width;
        for (int y = y0; y < y1; y++)
        {
            int row = (dy + y) * stride + dx;
            int src = y * diameter;
            for (int x = x0; x < x1; x++)
            {
                int coverage = mask[src + x];
                if (coverage == 0)
                    continue;
                int a = (coverage * alpha) >> 8;
                if (a == 0)
                    continue;
                Blend(dest, (row + x) * 4, b, g, r, a);
            }
        }
    }

    static void BlitSprite(Surface surface, byte[] src, int sw, int sh, int dx, int dy, int fade, SpriteTransform transform)
    {
        int dw = transform is SpriteTransform.RotateClockwise or SpriteTransform.RotateCounterClockwise ? sh : sw;
        int dh = transform is SpriteTransform.RotateClockwise or SpriteTransform.RotateCounterClockwise ? sw : sh;
        Span<byte> dest = surface.Pixels;
        for (int y = 0; y < dh; y++)
        {
            int destY = dy + y;
            if ((uint)destY >= (uint)surface.Height)
                continue;
            for (int x = 0; x < dw; x++)
            {
                int destX = dx + x;
                if ((uint)destX >= (uint)surface.Width)
                    continue;

                int sx, sy;
                switch (transform)
                {
                    case SpriteTransform.FlipVertical:
                        sx = x;
                        sy = sh - 1 - y;
                        break;
                    case SpriteTransform.RotateClockwise:
                        sx = y;
                        sy = sh - 1 - x;
                        break;
                    case SpriteTransform.RotateCounterClockwise:
                        sx = sw - 1 - y;
                        sy = x;
                        break;
                    default:
                        sx = x;
                        sy = y;
                        break;
                }

                int si = (sy * sw + sx) * 4;
                int sa = src[si + 3];
                if (sa == 0)
                    continue;
                int a = (sa * fade) >> 8;
                if (a == 0)
                    continue;
                Blend(dest, (destY * surface.Width + destX) * 4, src[si], src[si + 1], src[si + 2], a);
            }
        }
    }

    static void Blend(Span<byte> pixels, int index, byte b, byte g, byte r, int alpha)
    {
        if ((uint)alpha > 255)
            alpha = alpha < 0 ? 0 : 255;
        int inv = 255 - alpha;
        int pb = (b * alpha) >> 8;
        int pg = (g * alpha) >> 8;
        int pr = (r * alpha) >> 8;
        pixels[index] = (byte)(pb + ((pixels[index] * inv) >> 8));
        pixels[index + 1] = (byte)(pg + ((pixels[index + 1] * inv) >> 8));
        pixels[index + 2] = (byte)(pr + ((pixels[index + 2] * inv) >> 8));
        pixels[index + 3] = (byte)(alpha + ((pixels[index + 3] * inv) >> 8));
    }
}
