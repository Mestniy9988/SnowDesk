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
                DrawTruck(surface, band, fade, simulation.TruckScalePercent);
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

    static void DrawTruck(Surface surface, DriftBand band, int fade, int scalePercent)
    {
        TaskbarBand g = band.Geometry;
        KamazSprite.Frame frame = KamazSprite.Measure(scalePercent);
        SpriteTransform transform = TransformFor(g.Edge);
        (int drawX, int drawY) = PlaceTruck(g, band.Travel, frame, transform);
        BlitSprite(surface, frame, drawX, drawY, fade, transform);

        (int beaconX, int beaconY) = Map(frame.BeaconX, frame.BeaconY, transform, frame);
        bool lampOn = (band.Blink % 0.62f) < 0.16f;
        int lampX = drawX + beaconX;
        int lampY = drawY + beaconY;
        if (lampOn)
            DrawGlow(surface, lampX, lampY, frame.GlowNear, fade, 60, 210, 255);
        else
            DrawGlow(surface, lampX, lampY, frame.GlowFar, fade / 2, 40, 120, 160);
    }

    public static bool TryTruckAnchor(DriftBand band, int scalePercent, out int anchorX, out int anchorY, out bool bubbleAbove)
    {
        anchorX = 0;
        anchorY = 0;
        bubbleAbove = true;
        TaskbarBand g = band.Geometry;
        if (!band.Plowing)
            return false;

        KamazSprite.Frame frame = KamazSprite.Measure(scalePercent);
        SpriteTransform transform = TransformFor(g.Edge);
        (int drawX, int drawY) = PlaceTruck(g, band.Travel, frame, transform);
        (int beaconX, int beaconY) = Map(frame.BeaconX, frame.BeaconY, transform, frame);
        anchorX = drawX + beaconX;
        anchorY = drawY + beaconY;
        bubbleAbove = g.Edge != TaskbarEdge.Top;
        return true;
    }

    static SpriteTransform TransformFor(TaskbarEdge edge) => edge switch
    {
        TaskbarEdge.Top => SpriteTransform.FlipVertical,
        TaskbarEdge.Left => SpriteTransform.RotateClockwise,
        TaskbarEdge.Right => SpriteTransform.RotateCounterClockwise,
        _ => SpriteTransform.None,
    };

    static (int x, int y) PlaceTruck(TaskbarBand g, float travel, KamazSprite.Frame frame, SpriteTransform transform)
    {
        (int bladeX, int bladeY) = Map(frame.BladeX, frame.BladeY, transform, frame);
        (int wheelX, int wheelY) = Map(frame.WheelX, frame.WheelY, transform, frame);
        int along = (int)travel;
        return g.Edge switch
        {
            TaskbarEdge.Bottom => (g.X + along - bladeX, (g.Y - 1) - wheelY),
            TaskbarEdge.Top => (g.X + along - bladeX, (g.Y + g.Height) - wheelY),
            TaskbarEdge.Left => ((g.X + g.Width) - wheelX, g.Y + along - bladeY),
            _ => ((g.X - 1) - wheelX, g.Y + along - bladeY),
        };
    }

    static (int x, int y) Map(int sx, int sy, SpriteTransform transform, KamazSprite.Frame frame)
    {
        return transform switch
        {
            SpriteTransform.FlipVertical => (sx, frame.Height - 1 - sy),
            SpriteTransform.RotateClockwise => (frame.Height - 1 - sy, sx),
            SpriteTransform.RotateCounterClockwise => (sy, frame.Width - 1 - sx),
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

    static void BlitSprite(Surface surface, KamazSprite.Frame frame, int dx, int dy, int fade, SpriteTransform transform)
    {
        byte[] src = KamazSprite.Pixels;
        int sw = KamazSprite.Width;
        int sh = KamazSprite.Height;
        bool rotated = transform is SpriteTransform.RotateClockwise or SpriteTransform.RotateCounterClockwise;
        int dw = rotated ? frame.Height : frame.Width;
        int dh = rotated ? frame.Width : frame.Height;
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

                int localX, localY;
                switch (transform)
                {
                    case SpriteTransform.FlipVertical:
                        localX = x;
                        localY = frame.Height - 1 - y;
                        break;
                    case SpriteTransform.RotateClockwise:
                        localX = y;
                        localY = frame.Height - 1 - x;
                        break;
                    case SpriteTransform.RotateCounterClockwise:
                        localX = frame.Width - 1 - y;
                        localY = x;
                        break;
                    default:
                        localX = x;
                        localY = y;
                        break;
                }

                int sx = frame.Width == sw ? localX : localX * sw / frame.Width;
                int sy = frame.Height == sh ? localY : localY * sh / frame.Height;
                if ((uint)sx >= (uint)sw || (uint)sy >= (uint)sh)
                    continue;

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
