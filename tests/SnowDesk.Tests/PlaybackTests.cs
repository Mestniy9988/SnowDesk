namespace SnowDesk.Tests;

public class PlaybackTests
{
    [Fact]
    public void Custom_track_plays_once_then_stays_silent_until_restarted()
    {
        float[] song = { 1f, 2f, 3f, 4f, 5f, 6f, 7f, 8f };
        var once = new FadeLoop(song, channels: 1, sampleRate: 8000);
        once.Loop = false;

        var buffer = new float[8];
        Assert.Equal(8, once.Read(buffer, 0, 8));
        Assert.Equal(song, buffer);
        Assert.Equal(0, once.Read(buffer, 0, 8));
        Assert.True(once.Completed);
        Assert.Equal(0, once.Read(buffer, 0, 4));

        once.Rewind();
        Assert.False(once.Completed);
        Assert.Equal(8, once.Read(buffer, 0, 8));
        Assert.Equal(song, buffer);
    }

    [Fact]
    public void Repeat_loops_with_a_crossfade_and_bells_ignore_the_checkbox()
    {
        Assert.True(FadeLoop.ShouldLoop(builtin: true, repeat: false));
        Assert.True(FadeLoop.ShouldLoop(builtin: true, repeat: true));
        Assert.True(FadeLoop.ShouldLoop(builtin: false, repeat: true));
        Assert.False(FadeLoop.ShouldLoop(builtin: false, repeat: false));

        float[] song = { 8f, 0f, 0f, 0f, 0f, 0f, 0f, 0f };
        var loop = new FadeLoop(song, channels: 1, sampleRate: 100, fadeSeconds: 0.02);
        Assert.True(loop.Loop);
        var buffer = new float[16];
        int read = loop.Read(buffer, 0, 16);
        Assert.Equal(16, read);
        Assert.False(loop.Completed);
        Assert.Contains(buffer, sample => sample > 0.5f && sample < 7.5f);

        var bells = new FadeLoop(song, channels: 1, sampleRate: 100, fadeSeconds: 0.02);
        bells.Loop = FadeLoop.ShouldLoop(builtin: true, repeat: false);
        Assert.Equal(16, bells.Read(buffer, 0, 16));
        Assert.False(bells.Completed);
    }

    [Fact]
    public void Finished_file_stays_quiet_until_toggle_or_a_new_track()
    {
        var gate = new PlaybackGate();
        Assert.True(gate.Allow(null, repeat: false));
        gate.MarkFinished();
        Assert.True(gate.Allow(null, repeat: false));

        Assert.True(gate.Allow(@"C:\song.mp3", repeat: false));
        gate.MarkFinished();
        Assert.False(gate.Allow(@"C:\song.mp3", repeat: false));
        Assert.True(gate.Allow(@"C:\song.mp3", repeat: true));

        gate.MarkFinished();
        Assert.False(gate.Allow(@"C:\song.mp3", repeat: false));
        Assert.True(gate.Allow(@"C:\other.ogg", repeat: false));
        gate.MarkFinished();
        Assert.False(gate.Allow(@"C:\other.ogg", repeat: false));
        gate.Reset();
        Assert.True(gate.Allow(@"C:\other.ogg", repeat: false));
    }
}
