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
    FadeLoop? _fade;
    readonly PlaybackGate _played = new();
    string _key = "";
    string? _rejected;

    public string? LastError { get; private set; }

    public void Play(string? path, float level, bool repeat)
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
            bool loop = FadeLoop.ShouldLoop(key.Length == 0, repeat);
            string? gatePath = key.Length == 0 ? null : key;
            if (_output != null && _fade != null && _key == key)
            {
                _fade.Loop = loop;
                if (_gain != null)
                    _gain.Volume = level;
                if (_output.PlaybackState == PlaybackState.Playing)
                    return;
                if (_fade.Completed)
                    _played.MarkFinished();
                if (!_played.Allow(gatePath, repeat))
                    return;
                if (_fade.Completed)
                    _fade.Rewind();
                _output.Play();
                return;
            }

            if (!_played.Allow(gatePath, repeat))
                return;

            StopLocked(resetGate: false);
            try
            {
                LoadLocked(key, loop);
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
                StopLocked(resetGate: false);
                if (key.Length == 0)
                    return;
                try
                {
                    LoadLocked("", loop: true);
                    if (_gain != null)
                        _gain.Volume = level;
                    _output?.Play();
                }
                catch (Exception fallback) when (fallback is not OutOfMemoryException)
                {
                    LastError = fallback.Message;
                    StopLocked(resetGate: false);
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
            StopLocked(resetGate: true);
    }

    public void Dispose() => Stop();

    void LoadLocked(string key, bool loop)
    {
        WaveStream? stream = null;
        WaveOutEvent? output = null;
        try
        {
            stream = Open(key);
            WaveStream source = stream;
            ISampleProvider samples = source.ToSampleProvider();
            int channels = Math.Max(1, source.WaveFormat.Channels);
            int block = Math.Max(1, source.WaveFormat.BlockAlign);
            int frames = (int)Math.Min(int.MaxValue, Math.Max(0, source.Length / block));
            var fade = new FadeLoop(
                channels,
                source.WaveFormat.SampleRate,
                frames,
                samples.Read,
                () =>
                {
                    long framesLeft = Math.Max(0, (source.Length - source.Position) / block);
                    long count = framesLeft * channels;
                    return count > int.MaxValue ? int.MaxValue : (int)count;
                },
                sample =>
                {
                    long position = (long)(sample / channels) * block;
                    source.Position = position < 0 ? 0 : position > source.Length ? source.Length : position;
                })
            { Loop = loop };
            var gain = new GainProvider(new FadeSource(fade, samples.WaveFormat));
            output = new WaveOutEvent { DesiredLatency = 180 };
            output.Init(gain);
            _source = stream;
            _gain = gain;
            _fade = fade;
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

    void StopLocked(bool resetGate)
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
        _fade = null;
        _key = "";
        if (resetGate)
            _played.Reset();
    }

    sealed class FadeSource : ISampleProvider
    {
        readonly FadeLoop _loop;
        public FadeSource(FadeLoop loop, WaveFormat format)
        {
            _loop = loop;
            WaveFormat = format;
        }

        public WaveFormat WaveFormat { get; }
        public int Read(float[] buffer, int offset, int count) => _loop.Read(buffer, offset, count);
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
