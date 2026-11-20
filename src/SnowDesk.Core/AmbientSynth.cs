namespace SnowDesk;

/// <summary>
/// Short seamless loop: a low pad plus heavily filtered wind.
/// Generated in-process, so the app ships without a third-party recording.
/// </summary>
public static class AmbientSynth
{
    public const int SampleRate = 22050;
    public const int DurationSeconds = 12;

    public static byte[] BuildWav()
    {
        int count = SampleRate * DurationSeconds;
        var samples = new short[count];
        float low = 0f;
        float lower = 0f;
        for (int i = 0; i < count; i++)
        {
            float time = i / (float)SampleRate;
            float pad =
                0.22f * MathF.Sin(2f * MathF.PI * 55f * time) +
                0.16f * MathF.Sin(2f * MathF.PI * 110f * time) +
                0.07f * MathF.Sin(2f * MathF.PI * 164f * time + 0.4f) +
                0.055f * MathF.Sin(2f * MathF.PI * 220f * time + 1.2f) +
                0.030f * MathF.Sin(2f * MathF.PI * 329f * time + 0.6f) +
                0.012f * MathF.Sin(2f * MathF.PI * 660f * time);
            float swell = 0.86f + 0.14f * MathF.Sin(2f * MathF.PI * time / DurationSeconds);

            float white = Hash(i);
            low += (white - low) * 0.012f;
            lower += (low - lower) * 0.0035f;
            float wind = (low - lower) * (0.65f + 0.35f * MathF.Sin(2f * MathF.PI * time / 6f));

            float mixed = MathF.Tanh((pad * swell * 0.62f + wind * 0.55f) * 1.15f);
            samples[i] = (short)Math.Clamp(mixed * 7000f, short.MinValue, short.MaxValue);
        }

        int fade = SampleRate / 2;
        var copy = (short[])samples.Clone();
        for (int i = 0; i < fade; i++)
        {
            float weight = i / (float)(fade - 1);
            int end = count - fade + i;
            samples[end] = (short)(copy[end] * (1f - weight) + copy[i] * weight);
        }

        return WriteWav(samples, SampleRate);
    }

    public static float Hash(int index)
    {
        uint x = (uint)index * 747796405u + 2891336453u;
        x = ((x >> 16) ^ x) * 0x7feb352du;
        x = ((x >> 15) ^ x) * 0x846ca68bu;
        x = (x >> 16) ^ x;
        return (x & 0xFFFFFF) / 8388608f - 1f;
    }

    static byte[] WriteWav(short[] samples, int sampleRate)
    {
        int dataBytes = samples.Length * 2;
        var wav = new byte[44 + dataBytes];
        WriteAscii(wav, 0, "RIFF");
        WriteInt(wav, 4, 36 + dataBytes);
        WriteAscii(wav, 8, "WAVE");
        WriteAscii(wav, 12, "fmt ");
        WriteInt(wav, 16, 16);
        WriteShort(wav, 20, 1);
        WriteShort(wav, 22, 1);
        WriteInt(wav, 24, sampleRate);
        WriteInt(wav, 28, sampleRate * 2);
        WriteShort(wav, 32, 2);
        WriteShort(wav, 34, 16);
        WriteAscii(wav, 36, "data");
        WriteInt(wav, 40, dataBytes);
        Buffer.BlockCopy(samples, 0, wav, 44, dataBytes);
        return wav;
    }

    static void WriteAscii(byte[] buffer, int offset, string text)
    {
        for (int i = 0; i < text.Length; i++)
            buffer[offset + i] = (byte)text[i];
    }

    static void WriteInt(byte[] buffer, int offset, int value)
    {
        buffer[offset] = (byte)value;
        buffer[offset + 1] = (byte)(value >> 8);
        buffer[offset + 2] = (byte)(value >> 16);
        buffer[offset + 3] = (byte)(value >> 24);
    }

    static void WriteShort(byte[] buffer, int offset, short value)
    {
        buffer[offset] = (byte)value;
        buffer[offset + 1] = (byte)(value >> 8);
    }
}
