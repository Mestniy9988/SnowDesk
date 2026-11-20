namespace SnowDesk;

/// <summary>
/// Plays interleaved PCM once, or loops it with a short crossfade at the join.
/// </summary>
public sealed class FadeLoop
{
    readonly int _channels;
    readonly int _fadeSamples;
    readonly float[] _head;
    readonly Func<float[], int, int, int> _pull;
    readonly Func<int> _samplesLeft;
    readonly Action<int> _seek;
    bool _primed;

    public bool Loop { get; set; } = true;
    public bool Completed { get; private set; }

    public FadeLoop(float[] interleaved, int channels, int sampleRate, double fadeSeconds = 0.06)
        : this(channels, sampleRate, interleaved.Length / Math.Max(1, channels), fadeSeconds, new ArraySource(interleaved))
    {
    }

    FadeLoop(int channels, int sampleRate, int frameCount, double fadeSeconds, ArraySource source)
        : this(channels, sampleRate, frameCount, source.Read, () => source.Left, source.Seek, fadeSeconds)
    {
    }

    public FadeLoop(
        int channels,
        int sampleRate,
        int frameCount,
        Func<float[], int, int, int> pull,
        Func<int> samplesRemaining,
        Action<int> seekSample,
        double fadeSeconds = 0.06)
    {
        _channels = Math.Max(1, channels);
        _pull = pull;
        _samplesLeft = samplesRemaining;
        _seek = seekSample;
        int frames = FadeFrames(sampleRate, fadeSeconds, Math.Max(0, frameCount));
        _fadeSamples = frames * _channels;
        _head = new float[Math.Max(1, _fadeSamples)];
    }

    public static bool ShouldLoop(bool builtin, bool repeat) => builtin || repeat;

    public void Rewind()
    {
        _seek(0);
        _primed = false;
        Completed = false;
    }

    public int Read(float[] buffer, int offset, int count)
    {
        if (count <= 0 || Completed)
            return 0;

        if (!Loop)
            return ReadOnce(buffer, offset, count);

        if (!_primed)
            Prime();

        int written = 0;
        int guard = 0;
        while (written < count && guard++ < 8)
        {
            int samplesLeft = Math.Max(0, _samplesLeft());
            if (samplesLeft <= 0)
            {
                _seek(0);
                continue;
            }

            if (_fadeSamples > 0 && samplesLeft <= _fadeSamples)
            {
                int take = Math.Min(count - written, samplesLeft);
                var tail = new float[take];
                int got = _pull(tail, 0, take);
                if (got <= 0)
                {
                    _seek(0);
                    continue;
                }

                int fadePos = _fadeSamples - samplesLeft;
                for (int i = 0; i < got; i++)
                {
                    float weight = Math.Clamp((fadePos + i + 1) / (float)_fadeSamples, 0f, 1f);
                    int headIndex = Math.Clamp(fadePos + i, 0, _head.Length - 1);
                    buffer[offset + written + i] = tail[i] * (1f - weight) + _head[headIndex] * weight;
                }

                written += got;
                if (_samplesLeft() <= _channels)
                    _seek(_fadeSamples);
                continue;
            }

            int middle = Math.Min(count - written, Math.Max(1, samplesLeft - _fadeSamples));
            int read = _pull(buffer, offset + written, middle);
            if (read <= 0)
            {
                _seek(0);
                continue;
            }

            written += read;
        }

        return written;
    }

    int ReadOnce(float[] buffer, int offset, int count)
    {
        int left = Math.Max(0, _samplesLeft());
        if (left <= 0)
        {
            Completed = true;
            return 0;
        }

        int got = _pull(buffer, offset, Math.Min(count, left));
        if (got <= 0 || _samplesLeft() <= 0)
            Completed = true;
        return Math.Max(0, got);
    }

    void Prime()
    {
        _primed = true;
        if (_fadeSamples <= 0)
            return;

        int got = _pull(_head, 0, _head.Length);
        if (got < _head.Length)
            Array.Clear(_head, got, _head.Length - got);
        _seek(0);
    }

    static int FadeFrames(int sampleRate, double fadeSeconds, int frameCount)
    {
        int frames = (int)(Math.Max(1, sampleRate) * fadeSeconds);
        if (frameCount < frames * 3)
            frames = Math.Max(0, frameCount / 4);
        return frames;
    }

    sealed class ArraySource
    {
        readonly float[] _data;
        int _pos;

        public ArraySource(float[] data) => _data = data;
        public int Left => _data.Length - _pos;

        public int Read(float[] buffer, int offset, int count)
        {
            int n = Math.Min(count, Math.Max(0, Left));
            if (n > 0)
                Array.Copy(_data, _pos, buffer, offset, n);
            _pos += n;
            return n;
        }

        public void Seek(int sample) => _pos = Math.Clamp(sample, 0, _data.Length);
    }
}

/// <summary>
/// After a custom track finishes with repeat off, further starts are refused
/// until the snow is toggled or the file changes. Built-in bells always pass.
/// </summary>
public sealed class PlaybackGate
{
    string _key = "\0";
    bool _finished;

    public bool Allow(string? path, bool repeat)
    {
        bool builtin = string.IsNullOrWhiteSpace(path);
        string key = builtin ? "" : path!;
        if (!string.Equals(key, _key, StringComparison.OrdinalIgnoreCase))
        {
            _key = key;
            _finished = false;
        }

        if (builtin || repeat)
        {
            _finished = false;
            return true;
        }

        return !_finished;
    }

    public void MarkFinished() => _finished = true;

    public void Reset()
    {
        _key = "\0";
        _finished = false;
    }
}
