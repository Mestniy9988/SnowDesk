namespace SnowDesk;

/// <summary>
/// Side-view municipal KAMAZ snowplow, drawn once into a straight-alpha BGRA buffer.
/// The truck faces right: the blade is the front, wheels sit on <see cref="WheelY"/>.
/// </summary>
public static class KamazSprite
{
    public const int Width = 176;
    public const int Height = 96;
    public const int BladeX = 172;
    public const int BladeY = 60;
    public const int WheelX = 116;
    public const int WheelY = 88;
    public const int BeaconX = 104;
    public const int BeaconY = 14;

    public static byte[] Pixels { get; } = Scale2(DrawSmall(), 88, 48);

    static byte[] Scale2(byte[] source, int width, int height)
    {
        var scaled = new byte[width * 2 * height * 2 * 4];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int from = (y * width + x) * 4;
                for (int dy = 0; dy < 2; dy++)
                {
                    for (int dx = 0; dx < 2; dx++)
                    {
                        int to = ((y * 2 + dy) * width * 2 + (x * 2 + dx)) * 4;
                        scaled[to] = source[from];
                        scaled[to + 1] = source[from + 1];
                        scaled[to + 2] = source[from + 2];
                        scaled[to + 3] = source[from + 3];
                    }
                }
            }
        }

        return scaled;
    }

    static byte[] DrawSmall()
    {
        var pix = new Pix(88, 48);

        // Angled municipal blade, low in front of the bumper.
        pix.FillPolygon(
            new (int x, int y)[]
            {
                (64, 30), (78, 20), (87, 22), (86, 28), (82, 43), (64, 39),
            },
            198, 208, 216);
        pix.FillPolygon(
            new (int x, int y)[]
            {
                (66, 32), (78, 24), (82, 26), (79, 41), (66, 37),
            },
            150, 162, 172);
        pix.Line(78, 21, 87, 23, 244, 248, 252);
        pix.Line(64, 38, 82, 42, 96, 104, 112);

        // Rear body and chassis.
        pix.Rect(12, 26, 30, 14, 196, 92, 12);
        pix.Rect(14, 28, 26, 4, 232, 236, 240);
        pix.Rect(10, 38, 64, 4, 62, 52, 46);

        // Tall orange cab — the readable KAMAZ silhouette.
        pix.Rect(38, 14, 30, 26, 232, 120, 16);
        pix.Rect(40, 16, 26, 3, 244, 150, 48);
        pix.Rect(36, 24, 4, 8, 210, 104, 14); // mirror

        // Windshield and side window.
        pix.Rect(44, 18, 18, 10, 22, 40, 58);
        pix.Rect(46, 19, 6, 3, 150, 190, 210, 180);
        pix.Rect(58, 22, 3, 5, 16, 28, 40); // driver

        // Municipal stripe.
        pix.Rect(14, 32, 52, 3, 28, 104, 190);
        pix.Rect(14, 35, 52, 1, 240, 244, 248);

        // Hood front and grille.
        pix.Rect(66, 26, 8, 14, 214, 102, 10);
        pix.Rect(70, 28, 2, 6, 48, 36, 32);
        pix.Rect(73, 28, 2, 6, 48, 36, 32);
        pix.Rect(68, 29, 3, 3, 255, 226, 120); // headlight

        // Door seam and handle.
        pix.Line(50, 18, 50, 39, 176, 86, 10);
        pix.Rect(52, 30, 4, 2, 255, 236, 210);

        // Wheels.
        DrawWheel(pix, 28, 40);
        DrawWheel(pix, 58, 40);

        // Beacon housing. The lamp itself is painted by the renderer so it can blink.
        pix.Rect(48, 11, 10, 4, 54, 58, 64);
        pix.FillCircle(52, 7, 3, 120, 78, 16);

        return pix.Buffer;
    }

    static void DrawWheel(Pix pix, int cx, int cy)
    {
        pix.FillCircle(cx, cy, 7, 28, 30, 34);
        pix.FillCircle(cx, cy, 4, 70, 74, 80);
        pix.FillCircle(cx, cy, 2, 210, 214, 220);
    }

    sealed class Pix
    {
        public readonly byte[] Buffer;
        public readonly int W;
        public readonly int H;

        public Pix(int w, int h)
        {
            W = w;
            H = h;
            Buffer = new byte[w * h * 4];
        }

        public void Rect(int x, int y, int w, int h, byte r, byte g, byte b, byte a = 255)
        {
            for (int yy = y; yy < y + h; yy++)
            {
                for (int xx = x; xx < x + w; xx++)
                    Plot(xx, yy, r, g, b, a);
            }
        }

        public void FillCircle(int cx, int cy, int radius, byte r, byte g, byte b, byte a = 255)
        {
            int r2 = radius * radius;
            for (int yy = cy - radius; yy <= cy + radius; yy++)
            {
                for (int xx = cx - radius; xx <= cx + radius; xx++)
                {
                    int dx = xx - cx;
                    int dy = yy - cy;
                    if (dx * dx + dy * dy <= r2)
                        Plot(xx, yy, r, g, b, a);
                }
            }
        }

        public void Line(int x0, int y0, int x1, int y1, byte r, byte g, byte b)
        {
            int steps = Math.Max(Math.Abs(x1 - x0), Math.Abs(y1 - y0));
            if (steps == 0)
            {
                Plot(x0, y0, r, g, b, 255);
                return;
            }

            for (int i = 0; i <= steps; i++)
            {
                float t = i / (float)steps;
                int x = (int)MathF.Round(x0 + (x1 - x0) * t);
                int y = (int)MathF.Round(y0 + (y1 - y0) * t);
                Plot(x, y, r, g, b, 255);
            }
        }

        public void FillPolygon((int x, int y)[] poly, byte r, byte g, byte b)
        {
            int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
            foreach ((int x, int y) in poly)
            {
                if (x < minX) minX = x;
                if (y < minY) minY = y;
                if (x > maxX) maxX = x;
                if (y > maxY) maxY = y;
            }

            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    if (Inside(poly, x, y))
                        Plot(x, y, r, g, b, 255);
                }
            }
        }

        public void Plot(int x, int y, byte r, byte g, byte b, byte a)
        {
            if ((uint)x >= (uint)W || (uint)y >= (uint)H || a == 0)
                return;

            int i = (y * W + x) * 4;
            int inv = 255 - a;
            Buffer[i] = (byte)((b * a + Buffer[i] * inv) / 255);
            Buffer[i + 1] = (byte)((g * a + Buffer[i + 1] * inv) / 255);
            Buffer[i + 2] = (byte)((r * a + Buffer[i + 2] * inv) / 255);
            Buffer[i + 3] = (byte)(a + Buffer[i + 3] * inv / 255);
        }

        static bool Inside((int x, int y)[] poly, int x, int y)
        {
            bool inside = false;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
            {
                (int xi, int yi) = poly[i];
                (int xj, int yj) = poly[j];
                if (((yi > y) != (yj > y)) &&
                    (x < (xj - xi) * (y - yi) / (double)(yj - yi) + xi))
                    inside = !inside;
            }

            return inside;
        }
    }
}
