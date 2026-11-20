namespace SnowDesk.Tests;

public class FaderTests
{
    [Fact]
    public void Fade_reaches_the_target_and_can_reverse()
    {
        var fader = new OpacityFader();
        fader.FadeTo(1f, 1.8f);
        for (int i = 0; i < 70; i++)
            fader.Update(1.8f / 60f);
        Assert.Equal(1f, fader.Opacity, 3);
        Assert.False(fader.Animating);

        fader.FadeTo(0f, 1.75f);
        fader.Update(1.75f);
        Assert.Equal(0f, fader.Opacity, 3);
        Assert.False(fader.Animating);
    }

    [Fact]
    public void Snap_stops_animation()
    {
        var fader = new OpacityFader();
        fader.FadeTo(1f, 2f);
        fader.Update(0.2f);
        fader.Snap(0f);
        Assert.Equal(0f, fader.Opacity);
        Assert.False(fader.Animating);
    }
}

public class SettingsTests
{
    [Fact]
    public void Roundtrip_clamps_and_survives_a_corrupt_file()
    {
        string path = Path.Combine(Path.GetTempPath(), "snowdesk-tests", Guid.NewGuid().ToString("n"), "settings.json");
        var store = new SettingsStore(path);
        var settings = new AppSettings
        {
            Enabled = false,
            Intensity = 250,
            Volume = -4,
            Muted = true,
            MusicPath = "  ",
            TaskbarSnow = false,
            PlowIntervalMinutes = 0,
            StartWithWindows = true,
            PauseOnBatterySaver = false,
        };
        store.Save(settings);

        AppSettings loaded = store.Load();
        Assert.False(loaded.Enabled);
        Assert.Equal(100, loaded.Intensity);
        Assert.Equal(0, loaded.Volume);
        Assert.True(loaded.Muted);
        Assert.Null(loaded.MusicPath);
        Assert.False(loaded.TaskbarSnow);
        Assert.Equal(1, loaded.PlowIntervalMinutes);
        Assert.True(loaded.StartWithWindows);
        Assert.False(loaded.PauseOnBatterySaver);

        File.WriteAllText(path, "{ this is not json");
        AppSettings fallback = store.Load();
        Assert.Equal(40, fallback.Intensity);
        Assert.True(fallback.TaskbarSnow);
    }
}

public class SynthTests
{
    [Fact]
    public void Builtin_loop_is_audible_and_joins_itself()
    {
        byte[] wav = AmbientSynth.BuildWav();
        Assert.Equal((byte)'R', wav[0]);
        Assert.Equal((byte)'I', wav[1]);
        Assert.Equal((byte)'F', wav[2]);
        Assert.Equal((byte)'F', wav[3]);

        int samples = AmbientSynth.SampleRate * AmbientSynth.DurationSeconds;
        Assert.Equal(44 + samples * 2, wav.Length);

        short first = BitConverter.ToInt16(wav, 44);
        short last = BitConverter.ToInt16(wav, 44 + (samples - 1) * 2);
        Assert.InRange(Math.Abs(first - last), 0, 1200);

        double sum = 0;
        int peak = 0;
        for (int i = 0; i < samples; i++)
        {
            short sample = BitConverter.ToInt16(wav, 44 + i * 2);
            sum += sample * (double)sample;
            peak = Math.Max(peak, Math.Abs(sample));
        }

        double rms = Math.Sqrt(sum / samples);
        Assert.InRange(rms, 40, 2500);
        Assert.InRange(peak, 800, 16000);
        int quiet = 0;
        for (int i = 0; i < samples; i++)
        {
            if (Math.Abs(BitConverter.ToInt16(wav, 44 + i * 2)) < 250)
                quiet++;
        }

        Assert.True(quiet > samples * 0.45, $"quiet fraction {quiet / (double)samples:0.00}");
    }
}

public class SpeechBubbleTests
{
    [Fact]
    public void Line_is_exact_and_stays_on_screen()
    {
        Assert.Equal("Скоро Новый Год, я свои Дедлайны как видишь закрываю, а ты?", SpeechBubble.Text);
        Assert.True(SpeechBubble.Ready);
        Assert.True(SpeechBubble.TextWidth > 200);

        SpeechBubble.Placement left = SpeechBubble.Place(8, 80, 1400, 800, preferAbove: true, travel: 200, length: 1200);
        Assert.Equal(4, left.X);
        Assert.True(left.Opacity > 0.2f);
        SpeechBubble.Placement right = SpeechBubble.Place(1390, 80, 1400, 800, preferAbove: true, travel: 200, length: 1200);
        Assert.True(right.X >= 4);
        Assert.True(right.X + SpeechBubble.BoxWidth <= 1396);

        SpeechBubble.Placement hidden = SpeechBubble.Place(10, 40, 800, 200, preferAbove: true, travel: -30, length: 800);
        Assert.True(hidden.Opacity < 0.15f);
    }
}

public class RasterizerTests
{
    [Fact]
    public void Snow_and_the_truck_leave_pixels()
    {
        var sim = new SnowSimulation(seed: 9) { Intensity = 80, PlowIntervalMinutes = 120 };
        var scene = new Scene
        {
            Width = 320,
            Height = 180,
            Taskbars =
            [
                new TaskbarBand
                {
                    Id = "bar",
                    Edge = TaskbarEdge.Bottom,
                    X = 0,
                    Y = 150,
                    Width = 320,
                    Height = 30,
                },
            ],
        };
        sim.Update(1f / 30f, scene);
        sim.StampSnow(18f);
        sim.CallPlow();
        for (int i = 0; i < 140; i++)
            sim.Update(1f / 30f, scene);

        var buffer = new FrameBuffer(scene.Width, scene.Height);
        SnowRasterizer.Render(buffer, sim, 0f);
        Assert.Equal(0, Count(buffer));

        SnowRasterizer.Render(buffer, sim, 1f);
        Assert.True(Count(buffer) > 50);
        Assert.True(CountOrange(buffer) > 30, "the KAMAZ sprite should be visible");
    }

    [Fact]
    public void Sprite_has_an_orange_cab_and_a_blade()
    {
        int orange = 0;
        int pale = 0;
        byte[] pixels = KamazSprite.Pixels;
        for (int i = 0; i < pixels.Length; i += 4)
        {
            byte b = pixels[i];
            byte g = pixels[i + 1];
            byte r = pixels[i + 2];
            byte a = pixels[i + 3];
            if (a < 200)
                continue;
            if (r > 180 && g > 70 && g < 190 && b < 80)
                orange++;
            if (r > 190 && g > 190 && b > 180)
                pale++;
        }

        Assert.True(orange > 200, $"orange pixels {orange}");
        Assert.True(pale > 20, $"blade pixels {pale}");
    }

    static int Count(FrameBuffer buffer)
    {
        int n = 0;
        for (int i = 3; i < buffer.Pixels.Length; i += 4)
        {
            if (buffer.Pixels[i] != 0)
                n++;
        }
        return n;
    }

    static int CountOrange(FrameBuffer buffer)
    {
        int n = 0;
        byte[] pixels = buffer.Pixels;
        for (int i = 0; i < pixels.Length; i += 4)
        {
            int b = pixels[i];
            int g = pixels[i + 1];
            int r = pixels[i + 2];
            if (r > 140 && r > g && g > b + 20)
                n++;
        }
        return n;
    }
}
