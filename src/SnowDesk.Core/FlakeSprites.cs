namespace SnowDesk;

public static class FlakeSprites
{
    public static readonly int[] Diameters = { 2, 3, 4, 5, 6, 8, 10, 13, 16, 20, 24 };
    public static readonly byte[][] Masks;

    static FlakeSprites()
    {
        Masks = new byte[Diameters.Length][];
        for (int i = 0; i < Diameters.Length; i++)
            Masks[i] = Make(Diameters[i]);
    }

    public static int Nearest(float size)
    {
        int best = 0;
        float bestDistance = float.MaxValue;
        for (int i = 0; i < Diameters.Length; i++)
        {
            float distance = MathF.Abs(Diameters[i] - size);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = i;
            }
        }

        return best;
    }

    static byte[] Make(int diameter)
    {
        var mask = new byte[diameter * diameter];
        float center = (diameter - 1) * 0.5f;
        float sigma = MathF.Max(0.45f, diameter * 0.27f);
        float inv = 1f / (2f * sigma * sigma);
        for (int y = 0; y < diameter; y++)
        {
            for (int x = 0; x < diameter; x++)
            {
                float dx = x - center;
                float dy = y - center;
                float alpha = MathF.Exp(-(dx * dx + dy * dy) * inv);
                if (alpha < 0.035f)
                    continue;
                mask[y * diameter + x] = (byte)Math.Clamp(alpha * 255f, 0f, 255f);
            }
        }

        return mask;
    }
}
