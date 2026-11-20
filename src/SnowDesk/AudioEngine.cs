using NAudio.Vorbis;
using NAudio.Wave;

namespace SnowDesk;

sealed class AudioEngine : IDisposable
{
    readonly object _gate = new();
    byte[]? _builtin;
    WaveOutEvent? _output;
    WaveStream? _source;
    GainProvider? _gain;
    string _key = "";
    string? _rejected;

    public string? LastError { get; private set; }

    public void Play(string? path, float level)
    {
        lock (_gate)
        {
            level = Math.Clamp(level, 0f, 1f);
            MusicResolution choice = MusicResolver.Resolve(path);
            string key = choice.UseBuiltin ? "" : choice.Path ?? "";
            if (choice.Error != null)
                LastError = choice.Error;
            if (_rejected != null && _rejected != (path ?? ""))
                _rejected = null;
            if (_rejected != null && _rejected == path)
                key = "";
            if (_output != null && _key == key)
            {
                if (_gain != null)
                    _gain.Volume = level;
                if (_output.PlaybackState != PlaybackState.Playing)
                    _output.Play();
                return;
            }

            StopLocked();
            try
            {
                LoadLocked(key);
                if (_gain != null)
                    _gain.Volume = level;
                _output?.Play();
                if (choice.Error == null)
                    LastError = null;
                if (key.Length > 0)
                    _rejected = null;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                LastError = ex.Message;
                if (key.Length > 0)
                    _rejected = key;
                StopLocked();
                if (key.Length == 0)
                    return;
                try
                {
                    LoadLocked("");
                    if (_gain != null)
                        _gain.Volume = level;
                    _output?.Play();
                }
                catch (Exception fallback) when (fallback is not OutOfMemoryException)
                {
                    LastError = fallback.Message;
                    StopLocked();
                }
            }
        }
    }

    public void SetLevel(float level)
    {
        if (_gain != null)
            _gain.Volume = Math.Clamp(level, 0f, 1f);
    }

    public void Stop()
    {
        lock (_gate)
            StopLocked();
    }

    public void Dispose() => Stop();

    void LoadLocked(string key)
    {
        WaveStream? stream = null;
        WaveOutEvent? output = null;
        try
        {
            stream = Open(key);
            var loop = new CrossfadeLoop(stream);
            var gain = new GainProvider(loop);
            output = new WaveOutEvent { DesiredLatency = 180 };
            output.Init(gain);
            _source = stream;
            _gain = gain;
            _output = output;
            _key = key;
            stream = null;
            output = null;
        }
        finally
        {
            output?.Dispose();
            stream?.Dispose();
        }
    }

    WaveStream Open(string key)
    {
        if (key.Length == 0)
        {
            _builtin ??= AmbientSynth.BuildWav();
            var memory = new MemoryStream(_builtin, writable: false);
            return new WaveFileReader(memory);
        }

        if (key.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase))
            return new VorbisWaveReader(key);
        return new AudioFileReader(key);
    }

    void StopLocked()
    {
        try
        {
            _output?.Stop();
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
        }

        _output?.Dispose();
        _source?.Dispose();
        _output = null;
        _source = null;
        _gain = null;
        _key = "";
    }

    /// <summary>
    /// Loops a file and mixes about 60 ms of the ending into the beginning
    /// so an MP3 (including 320 kbps stereo 44.1 kHz) does not click at the join.
    /// </summary>
    sealed class CrossfadeLoop : ISampleProvider
    {
        readonly WaveStream _source;
        readonly ISampleProvider _samples;
        readonly float[] _head;
        readonly int _channels;
        readonly int _fadeSamples;
        bool _primed;

        public CrossfadeLoop(WaveStream source)
        {
            _source = source;
            _samples = source.ToSampleProvider();
            _channels = Math.Max(1, source.WaveFormat.Channels);
            int frames = (int)(source.WaveFormat.SampleRate * 0.06);
            long totalFrames = source.Length / Math.Max(1, source.WaveFormat.BlockAlign);
            if (totalFrames < frames * 3)
                frames = (int)Math.Max(0, totalFrames / 4);
            _fadeSamples = frames * _channels;
            _head = new float[Math.Max(1, _fadeSamples)];
        }

        public WaveFormat WaveFormat => _samples.WaveFormat;

        public int Read(float[] buffer, int offset, int count)
        {
            if (!_primed)
                Prime();

            int written = 0;
            int guard = 0;
            while (written < count && guard++ < 8)
            {
                int block = Math.Max(1, _source.WaveFormat.BlockAlign);
                long framesLeft = Math.Max(0, (_source.Length - _source.Position) / block);
                int samplesLeft = (int)Math.Min(int.MaxValue, framesLeft * _channels);
                if (samplesLeft <= 0)
                {
                    _source.Position = 0;
                    continue;
                }

                if (_fadeSamples > 0 && samplesLeft <= _fadeSamples)
                {
                    int take = Math.Min(count - written, samplesLeft);
                    var tail = new float[take];
                    int got = _samples.Read(tail, 0, take);
                    if (got <= 0)
                    {
                        _source.Position = 0;
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
                    if (_source.Position >= _source.Length - block)
                        _source.Position = (long)(_fadeSamples / _channels) * block;
                    continue;
                }

                int before = Math.Min(count - written, samplesLeft - _fadeSamples);
                int read = _samples.Read(buffer, offset + written, before);
                if (read <= 0)
                {
                    _source.Position = 0;
                    continue;
                }

                written += read;
            }

            return written;
        }

        void Prime()
        {
            _primed = true;
            if (_fadeSamples <= 0)
                return;
            int got = _samples.Read(_head, 0, _head.Length);
            if (got < _head.Length)
                Array.Clear(_head, got, _head.Length - got);
            _source.Position = 0;
        }
    }

    sealed class GainProvider : ISampleProvider
    {
        readonly ISampleProvider _source;
        public volatile float Volume = 1f;

        public GainProvider(ISampleProvider source) => _source = source;
        public WaveFormat WaveFormat => _source.WaveFormat;

        public int Read(float[] buffer, int offset, int count)
        {
            int read = _source.Read(buffer, offset, count);
            float volume = Volume;
            if (volume >= 0.999f)
                return read;
            for (int i = 0; i < read; i++)
                buffer[offset + i] *= volume;
            return read;
        }
    }
}
