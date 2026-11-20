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
            var loop = new LoopStream(stream);
            var gain = new GainProvider(loop.ToSampleProvider());
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

    sealed class LoopStream : WaveStream
    {
        readonly WaveStream _source;

        public LoopStream(WaveStream source) => _source = source;

        public override WaveFormat WaveFormat => _source.WaveFormat;
        public override long Length => _source.Length;
        public override long Position
        {
            get => _source.Position;
            set => _source.Position = value;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int total = 0;
            while (total < count)
            {
                int read = _source.Read(buffer, offset + total, count - total);
                if (read > 0)
                {
                    total += read;
                    continue;
                }

                if (_source.Position == 0 || _source.Length == 0)
                    break;
                _source.Position = 0;
            }

            return total;
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
