namespace SnowDesk.Tests;

public class SimulationTests
{
    static Scene Desktop(int width = 960, int height = 540)
    {
        return new Scene
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
    }

    static void Run(SnowSimulation sim, Scene scene, float seconds, float step = 1f / 30f)
    {
        int steps = (int)(seconds / step);
        for (int i = 0; i < steps; i++)
            sim.Update(step, scene);
    }

    [Fact]
    public void Light_snow_stays_small_and_slow()
    {
        var sim = new SnowSimulation(seed: 3) { Intensity = 1, PlowIntervalMinutes = 120 };
        Scene scene = Desktop();
        Run(sim, scene, 2f);
        Assert.InRange(sim.Flakes.Count, 1, 12);
        Assert.All(sim.Flakes, flake => Assert.True(flake.Size <= 4f && flake.Speed < 40f));
        Assert.True(sim.Bands[0].Fullness < 0.08f, $"light drift grew to {sim.Bands[0].Fullness:0.00}");
    }

    [Fact]
    public void Blizzard_fills_the_drift_in_about_a_minute_then_calls_the_plow()
    {
        var sim = new SnowSimulation(seed: 7)
        {
            Intensity = 100,
            PlowIntervalMinutes = 120,
            TaskbarSnow = true,
        };
        Scene scene = Desktop();
        Run(sim, scene, 20f);
        float early = sim.Bands[0].Fullness;
        Assert.True(early > 0.08f && early < 0.85f, $"fullness at 20s was {early:0.00}");

        Run(sim, scene, 80f);
        Assert.True(sim.PlowsStarted >= 1, $"plows {sim.PlowsStarted}, fullness {sim.Bands[0].Fullness:0.00}, peak {sim.Bands[0].Peak:0.0}");
    }

    [Fact]
    public void Plow_clears_snow_behind_the_blade()
    {
        var sim = new SnowSimulation(seed: 1) { Intensity = 40, PlowIntervalMinutes = 120 };
        Scene scene = Desktop(width: 480, height: 320);
        sim.Update(1f / 30f, scene);
        sim.StampSnow(20f);
        float before = sim.Bands[0].Mean;
        sim.CallPlow();
        Run(sim, scene, 12f);
        Assert.True(sim.PlowsStarted >= 1);
        Assert.True(sim.Bands[0].Mean < before * 0.25f, $"mean {sim.Bands[0].Mean} before {before}");
        Assert.False(sim.Bands[0].Plowing);
    }

    [Fact]
    public void Drift_is_kept_when_updates_stop()
    {
        var sim = new SnowSimulation(seed: 2) { Intensity = 80, PlowIntervalMinutes = 120 };
        Scene scene = Desktop();
        Run(sim, scene, 8f);
        float mean = sim.Bands[0].Mean;
        Assert.True(mean > 0.5f);
        float later = sim.Bands[0].Mean;
        Assert.Equal(mean, later);
    }

    [Fact]
    public void Taskbar_snow_can_be_disabled_without_losing_the_map()
    {
        var sim = new SnowSimulation(seed: 4) { Intensity = 70, PlowIntervalMinutes = 120 };
        Scene scene = Desktop();
        Run(sim, scene, 6f);
        float mean = sim.Bands[0].Mean;
        sim.TaskbarSnow = false;
        Run(sim, scene, 4f);
        Assert.Equal(mean, sim.Bands[0].Mean, 1);
        Assert.Equal(0, sim.PlowsStarted);
    }

    [Fact]
    public void Manual_call_drives_even_a_thin_drift()
    {
        var sim = new SnowSimulation(seed: 5) { Intensity = 20, PlowIntervalMinutes = 120 };
        Scene scene = Desktop();
        sim.Update(1f / 30f, scene);
        sim.StampSnow(2f);
        sim.CallPlow();
        sim.Update(1f / 30f, scene);
        Assert.True(sim.Bands[0].Plowing);
        Assert.Equal(1, sim.PlowsStarted);
    }
}
