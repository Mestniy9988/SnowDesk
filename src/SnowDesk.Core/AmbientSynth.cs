namespace SnowDesk;

/// <summary>
/// Long procedural loop: sparse sleigh bells and quiet celesta-like chimes
/// on a major pentatonic scale, with long gaps and a barely-there warm bed.
/// </summary>
public static class AmbientSynth
{
    public const int SampleRate = 22050;
    public const int DurationSeconds = 36;

    static readonly float[] Scale =
    {
        523.25f, 587.33f, 659.25f, 783.99f, 880f, 1046.5f, 1174.66f, 1318.51f, 1567.98f,
    };

    public static byte[] BuildWav()
    {
        int count = SampleRate * DurationSeconds;
        var mix = new float[count];
        float time = 1.4f;
        int phrase = 0;
        while (time < DurationSeconds - 3.2f)
        {
            int notes = 2 + (HashInt(phrase * 13 + 1) % 4);
            float cursor = time;
            for (int note = 0; note < notes; note++)
            {
                int pick = HashInt(phrase * 29 + note * 7);
                float freq = Scale[pick % Scale.Length];
                float decay = 0.45f + (HashInt(pick + 3) % 80) / 100f;
                float amp = 0.11f + (HashInt(pick + 9) % 50) / 1000f;
                AddChime(mix, cursor, freq, decay, amp);
                if (HashInt(pick + 11) % 3 == 0)
                    AddSleigh(mix, cursor, 0.07f + (HashInt(pick) % 30) / 1000f);
                cursor += 0.28f + (HashInt(phrase * 3 + note) % 40) / 100f;
            }

            phrase++;
            time += 3.4f + (HashInt(phrase * 19) % 280) / 100f;
        }

        for (int i = 0; i < count; i++)
        {
            float t = i / (float)SampleRate;
            mix[i] += 0.012f * MathF.Sin(2f * MathF.PI * 196f * t);
            mix[i] += 0.008f * MathF.Sin(2f * MathF.PI * 294f * t);
            mix[i] = MathF.Tanh(mix[i] * 1.4f);
        }

        int fade = SampleRate / 3;
        var copy = (float[])mix.Clone();
        for (int i = 0; i < fade; i++)
        {
            float weight = i / (float)(fade - 1);
            int end = count - fade + i;
            mix[end] = copy[end] * (1f - weight) + copy[i] * weight;
        }

        var samples = new short[count];
        for (int i = 0; i < count; i++)
            samples[i] = (short)Math.Clamp(mix[i] * 7000f, short.MinValue, short.MaxValue);
        return WriteWav(samples, SampleRate);
    }

    static void AddChime(float[] mix, float start, float freq, float decay, float amp)
    {
        int origin = (int)(start * SampleRate);
        int length = (int)(decay * 4.2f * SampleRate);
        for (int i = 0; i < length && origin + i < mix.Length; i++)
        {
            float time = i / (float)SampleRate;
            float env = MathF.Exp(-time / decay) * MathF.Min(1f, time / 0.006f);
            float tone =
                0.72f * MathF.Sin(2f * MathF.PI * freq * time) +
                0.18f * MathF.Sin(2f * MathF.PI * freq * 2.003f * time) +
                0.07f * MathF.Sin(2f * MathF.PI * freq * 3.01f * time);
            mix[origin + i] += tone * env * amp;
        }
    }

    static void AddSleigh(float[] mix, float start, float amp)
    {
        int origin = (int)(start * SampleRate);
        int length = (int)(0.22f * SampleRate);
        for (int i = 0; i < length && origin + i < mix.Length; i++)
        {
            float time = i / (float)SampleRate;
            float env = MathF.Exp(-time / 0.055f) * MathF.Min(1f, time / 0.002f);
            float metal =
                0.45f * MathF.Sin(2f * MathF.PI * 2480f * time) +
                0.28f * MathF.Sin(2f * MathF.PI * 3320f * time) +
                0.22f * Hash(origin + i);
            mix[origin + i] += metal * env * amp;
        }
    }

    static int HashInt(int index)
    {
        uint x = (uint)index * 747796405u + 2891336453u;
        x = ((x >> 16) ^ x) * 0x7feb352du;
        x = ((x >> 15) ^ x) * 0x846ca68bu;
        return (int)(((x >> 16) ^ x) & 0x7fffffff);
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
