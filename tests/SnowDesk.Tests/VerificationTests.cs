namespace SnowDesk.Tests;

public class DirectorTests
{
    static SnowDirector Running(int volume = 40)
    {
        return new SnowDirector
        {
            Enabled = true,
            DesktopFree = true,
            Volume = volume,
            PauseOnBatterySaver = true,
        };
    }

    static void Settle(SnowDirector director, float seconds, float step = 1f / 30f)
    {
        int steps = Math.Max(1, (int)MathF.Round(seconds / step));
        for (int i = 0; i < steps; i++)
            director.Tick(step);
    }

    [Fact]
    public void Fade_in_then_out_stops_audio_instead_of_leaving_it_muted()
    {
        SnowDirector director = Running();
        Settle(director, 2f);
        Assert.False(director.Animating);
        Assert.Equal(1f, director.Opacity, 2);
        Assert.True(director.Render);
        Assert.True(director.AudioRunning);
        Assert.Equal(0.40f, director.AudioLevel, 2);

        director.DesktopFree = false;
        Settle(director, 2f);
        Assert.False(director.Render);
        Assert.False(director.AudioRunning);
        Assert.Equal(0f, director.AudioLevel);
        Assert.Equal(0f, director.Opacity);
    }

    [Fact]
    public void Rapid_toggles_reverse_the_fade_and_can_restart()
    {
        SnowDirector director = Running();
        Settle(director, 0.4f);
        float rising = director.Opacity;
        Assert.InRange(rising, 0.15f, 0.35f);
        Assert.True(director.AudioRunning);

        director.DesktopFree = false;
        Settle(director, 0.2f);
        float falling = director.Opacity;
        Assert.True(falling < rising - 0.05f);
        Assert.True(director.Render);

        director.DesktopFree = true;
        Settle(director, 0.2f);
        Assert.True(director.Opacity > falling);
        Assert.True(director.Render);
        Assert.True(director.AudioRunning);
        Assert.InRange(director.Opacity, 0.01f, 0.99f);

        director.DesktopFree = false;
        Settle(director, 2f);
        Assert.False(director.Render);
        Assert.False(director.AudioRunning);

        director.DesktopFree = true;
        director.Tick(1f / 30f);
        Assert.True(director.Render);
        Assert.True(director.AudioRunning);
        Assert.True(director.Opacity > 0f);
    }

    [Fact]
    public void Fullscreen_cuts_snow_and_audio_immediately()
    {
        SnowDirector director = Running();
        Settle(director, 2f);
        director.DesktopFree = false;
        director.FullscreenBusy = true;
        director.Tick(0f);
        Assert.False(director.Render);
        Assert.False(director.AudioRunning);
        Assert.Equal(0f, director.Opacity);
    }

    [Fact]
    public void Lock_battery_saver_and_mute_keep_the_device_stopped()
    {
        SnowDirector director = Running();
        Settle(director, 2f);

        director.Muted = true;
        director.Tick(0f);
        Assert.True(director.Render);
        Assert.False(director.AudioRunning);

        director.Muted = false;
        director.Volume = 0;
        director.Tick(0f);
        Assert.False(director.AudioRunning);

        director.Volume = 40;
        director.Locked = true;
        Settle(director, 2f);
        Assert.False(director.Render);
        Assert.False(director.AudioRunning);

        director.Locked = false;
        director.DesktopFree = true;
        director.BatterySaver = true;
        director.Tick(0.2f);
        Assert.False(director.Render);
    }

    [Fact]
    public void Audio_level_tracks_opacity()
    {
        SnowDirector director = Running(volume: 80);
        Settle(director, 0.9f);
        Assert.Equal(0.5f, director.Opacity, 1);
        Assert.Equal(0.40f, director.AudioLevel, 1);
    }
}

public class TaskbarLayoutTests
{
    static readonly PixelRect Monitor = new(0, 0, 1920, 1080);

    [Theory]
    [InlineData(0, 1040, 1920, 1080, TaskbarEdge.Bottom)]
    [InlineData(0, 0, 1920, 48, TaskbarEdge.Top)]
    [InlineData(0, 0, 48, 1080, TaskbarEdge.Left)]
    [InlineData(1872, 0, 1920, 1080, TaskbarEdge.Right)]
    public void Edge_follows_the_nearest_side(int left, int top, int right, int bottom, TaskbarEdge edge)
    {
        Assert.Equal(edge, TaskbarLayout.GuessEdge(new PixelRect(left, top, right, bottom), Monitor));
    }

    [Fact]
    public void Auto_hide_collapses_onto_the_screen_edge()
    {
        PixelRect bottom = TaskbarLayout.PinToEdge(Monitor, TaskbarEdge.Bottom);
        TaskbarBand band = TaskbarLayout.ToBand(bottom, TaskbarEdge.Bottom, autoHide: true, 0, 0, "primary");
        Assert.Equal(0, band.Height);
        Assert.Equal(1080, band.Y);
        Assert.Equal(1920, band.Width);
        Assert.True(band.AutoHide);
        Assert.Equal(1920, band.Length);

        PixelRect left = TaskbarLayout.PinToEdge(Monitor, TaskbarEdge.Left);
        TaskbarBand side = TaskbarLayout.ToBand(left, TaskbarEdge.Left, autoHide: true, 0, 0, "primary");
        Assert.Equal(0, side.Width);
        Assert.Equal(0, side.X);
        Assert.Equal(1080, side.Height);
    }

    [Fact]
    public void Second_monitor_uses_virtual_screen_origin()
    {
        var monitor = new PixelRect(-1920, 0, 0, 1080);
        var taskbar = new PixelRect(-1920, 1040, 0, 1080);
        Assert.Equal(TaskbarEdge.Bottom, TaskbarLayout.GuessEdge(taskbar, monitor));

        TaskbarBand band = TaskbarLayout.ToBand(taskbar, TaskbarEdge.Bottom, autoHide: false, originX: -1920, originY: 0, "left");
        Assert.Equal(0, band.X);
        Assert.Equal(1040, band.Y);
        Assert.Equal(1920, band.Width);
        Assert.Equal(40, band.Height);
        Assert.False(band.AutoHide);
    }
}

public class MusicResolverTests
{
    [Fact]
    public void Missing_and_invalid_files_fall_back_without_throwing()
    {
        string root = Path.Combine(Path.GetTempPath(), "snowdesk-music", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(root);

        Assert.True(MusicResolver.Resolve(null).UseBuiltin);
        Assert.True(MusicResolver.Resolve("  ").UseBuiltin);
        Assert.NotNull(MusicResolver.Resolve(Path.Combine(root, "gone.mp3")).Error);

        string text = Path.Combine(root, "notes.mp3");
        File.WriteAllText(text, "this is not audio");
        MusicResolution invalid = MusicResolver.Resolve(text);
        Assert.True(invalid.UseBuiltin);
        Assert.NotNull(invalid.Error);

        string empty = Path.Combine(root, "empty.wav");
        File.WriteAllBytes(empty, new byte[8]);
        Assert.NotNull(MusicResolver.Resolve(empty).Error);

        string wav = Path.Combine(root, "wind.wav");
        byte[] header = "RIFF\0\0\0\0WAVEfmt "u8.ToArray();
        File.WriteAllBytes(wav, header);
        MusicResolution good = MusicResolver.Resolve(wav);
        Assert.False(good.UseBuiltin);
        Assert.Equal(wav, good.Path);
        Assert.Null(good.Error);

        string ogg = Path.Combine(root, "pad.ogg");
        File.WriteAllBytes(ogg, "OggS"u8.ToArray().Concat(new byte[20]).ToArray());
        Assert.False(MusicResolver.Resolve(ogg).UseBuiltin);

        string mp3 = Path.Combine(root, "song.mp3");
        var frame = new byte[16];
        frame[0] = 0xFF;
        frame[1] = 0xFB;
        File.WriteAllBytes(mp3, frame);
        Assert.False(MusicResolver.Resolve(mp3).UseBuiltin);

        string other = Path.Combine(root, "song.flac");
        File.WriteAllBytes(other, new byte[32]);
        Assert.NotNull(MusicResolver.Resolve(other).Error);
    }
}

public class SettingsCorruptionTests
{
    [Theory]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("{ this is not json")]
    [InlineData("[]")]
    [InlineData("{\"intensity\":\"lots\"}")]
    public void Corrupt_files_load_as_defaults(string json)
    {
        string path = NewPath();
        File.WriteAllText(path, json);
        AppSettings settings = new SettingsStore(path).Load();
        Assert.Equal(40, settings.Intensity);
        Assert.True(settings.Enabled);
        Assert.True(settings.TaskbarSnow);
        Assert.Equal(12, settings.PlowIntervalMinutes);
    }

    [Fact]
    public void Missing_file_is_defaults_and_unknown_fields_are_ignored()
    {
        string path = NewPath();
        AppSettings missing = new SettingsStore(path).Load();
        Assert.Equal(35, missing.Volume);

        File.WriteAllText(path, """{"intensity":1,"volume":9,"mystery":true}""");
        AppSettings loaded = new SettingsStore(path).Load();
        Assert.Equal(1, loaded.Intensity);
        Assert.Equal(9, loaded.Volume);
        Assert.True(loaded.Enabled);
    }

    static string NewPath()
    {
        string path = Path.Combine(Path.GetTempPath(), "snowdesk-tests", Guid.NewGuid().ToString("n"), "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        return path;
    }
}

public class ExtremeAndPlowTests
{
    static Scene Desktop(int width = 960, int height = 540) => new()
    {
        Width = width,
        Height = height,
        Taskbars =
        [
            new TaskbarBand
            {
                Id = "primary",
                Edge = TaskbarEdge.Bottom,
                X = 0,
                Y = height - 40,
                Width = width,
                Height = 40,
            },
        ],
    };

    static void Run(SnowSimulation sim, Scene scene, float seconds)
    {
        int steps = (int)(seconds * 30f);
        for (int i = 0; i < steps; i++)
            sim.Update(1f / 30f, scene);
    }

    [Fact]
    public void Intensity_one_and_one_hundred_drive_the_simulation()
    {
        var light = new SnowSimulation(3) { Intensity = 1, PlowIntervalMinutes = 120 };
        Scene scene = Desktop();
        light.Update(1f / 30f, scene);
        IntensityProfile low = IntensityProfile.FromLevel(1);
        Assert.Equal(low.TargetFlakes, light.Flakes.Count);
        Assert.All(light.Flakes, flake =>
        {
            Assert.InRange(flake.Size, 0.5f, low.MaxSize + 0.01f);
            Assert.True(flake.Speed < 40f);
        });

        var storm = new SnowSimulation(3) { Intensity = 100, PlowIntervalMinutes = 120 };
        storm.Update(1f / 30f, scene);
        IntensityProfile high = IntensityProfile.FromLevel(100);
        Assert.Equal(high.TargetFlakes, storm.Flakes.Count);
        Assert.Equal(460, storm.Flakes.Count);
        Assert.Contains(storm.Flakes, flake => flake.Speed > 150f);
        Assert.Contains(storm.Flakes, flake => flake.Size > 8f);
    }

    [Fact]
    public void Plow_waits_for_its_interval_then_auto_starts_when_full()
    {
        var sim = new SnowSimulation(2) { Intensity = 1, PlowIntervalMinutes = 1 };
        Scene scene = Desktop();
        sim.Update(1f / 30f, scene);
        sim.StampSnow(4f);
        Run(sim, scene, 50f);
        Assert.Equal(0, sim.PlowsStarted);

        Run(sim, scene, 15f);
        Assert.True(sim.PlowsStarted >= 1);

        var full = new SnowSimulation(2) { Intensity = 40, PlowIntervalMinutes = 120 };
        full.Update(1f / 30f, scene);
        full.StampSnow(1000f);
        Assert.Equal(full.Bands[0].MaxHeight, full.Bands[0].Peak, 1);
        full.Update(1f / 30f, scene);
        Assert.True(full.Bands[0].Plowing);
        Assert.Equal(1, full.PlowsStarted);
    }

    [Fact]
    public void Drift_does_not_advance_while_updates_are_paused()
    {
        var sim = new SnowSimulation(5) { Intensity = 80, PlowIntervalMinutes = 120 };
        Scene scene = Desktop();
        Run(sim, scene, 4f);
        float mean = sim.Bands[0].Mean;
        int plows = sim.PlowsStarted;
        Assert.Equal(mean, sim.Bands[0].Mean);
        Assert.Equal(plows, sim.PlowsStarted);
    }

    [Fact]
    public void Two_monitors_each_collect_a_drift()
    {
        var scene = new Scene
        {
            OriginX = -1920,
            OriginY = 0,
            Width = 3840,
            Height = 1080,
            Taskbars =
            [
                new TaskbarBand
                {
                    Id = "left",
                    Edge = TaskbarEdge.Bottom,
                    X = 0,
                    Y = 1040,
                    Width = 1920,
                    Height = 40,
                },
                new TaskbarBand
                {
                    Id = "right",
                    Edge = TaskbarEdge.Bottom,
                    X = 1920,
                    Y = 1040,
                    Width = 1920,
                    Height = 40,
                },
            ],
        };

        var sim = new SnowSimulation(8) { Intensity = 100, PlowIntervalMinutes = 120 };
        Run(sim, scene, 8f);
        Assert.Equal(2, sim.Bands.Count);
        Assert.True(sim.Bands[0].Mean > 0.2f, $"left {sim.Bands[0].Mean}");
        Assert.True(sim.Bands[1].Mean > 0.2f, $"right {sim.Bands[1].Mean}");
        Assert.All(sim.Flakes, flake => Assert.InRange(flake.X, 0f, 3840f));
    }

    [Fact]
    public void Left_edge_and_top_edge_can_hold_snow()
    {
        var scene = new Scene
        {
            Width = 800,
            Height = 600,
            Taskbars =
            [
                new TaskbarBand
                {
                    Id = "left",
                    Edge = TaskbarEdge.Left,
                    X = 0,
                    Y = 0,
                    Width = 48,
                    Height = 600,
                },
                new TaskbarBand
                {
                    Id = "top",
                    Edge = TaskbarEdge.Top,
                    X = 0,
                    Y = 0,
                    Width = 800,
                    Height = 32,
                },
            ],
        };

        var sim = new SnowSimulation(4) { Intensity = 100, PlowIntervalMinutes = 120 };
        Run(sim, scene, 12f);
        DriftBand top = sim.Bands.Single(band => band.Geometry.Edge == TaskbarEdge.Top);
        DriftBand left = sim.Bands.Single(band => band.Geometry.Edge == TaskbarEdge.Left);
        Assert.True(top.Mean > 0.2f, $"top {top.Mean}");
        Assert.True(left.Peak > 0f, "left edge caught no flakes");
    }
}
