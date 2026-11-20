namespace SnowDesk.Tests;

public class IntensityTests
{
    [Fact]
    public void Level_one_is_sparse_and_slow()
    {
        IntensityProfile profile = IntensityProfile.FromLevel(1);
        Assert.Equal(1, profile.Level);
        Assert.InRange(profile.TargetFlakes, 1, 12);
        Assert.InRange(profile.MaxSize, 1f, 4f);
        Assert.InRange(profile.MaxFall, 1f, 30f);
        Assert.True(profile.FillSeconds > 20 * 60f);
    }

    [Fact]
    public void Default_medium_is_between_the_extremes()
    {
        IntensityProfile low = IntensityProfile.FromLevel(1);
        IntensityProfile mid = IntensityProfile.FromLevel(40);
        IntensityProfile high = IntensityProfile.FromLevel(100);

        Assert.InRange(mid.TargetFlakes, 120, 260);
        Assert.True(mid.TargetFlakes > low.TargetFlakes);
        Assert.True(mid.TargetFlakes < high.TargetFlakes);
        Assert.InRange(mid.MaxDrift, 22f, 28f);
        Assert.InRange(mid.AverageFall, 40f, 120f);
        Assert.Contains("снегопад", mid.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Blizzard_fills_in_about_a_minute_and_caps_flakes()
    {
        IntensityProfile profile = IntensityProfile.FromLevel(100);
        Assert.InRange(profile.FillSeconds, 20f, 40f);
        Assert.InRange(profile.TargetFlakes, 880, 960);
        Assert.True(profile.MaxSize > 24f);
        Assert.True(profile.MaxFall >= 400f);
        Assert.InRange(profile.MaxDrift, 30f, 36f);
        Assert.Equal("Метель", profile.Description);
    }

    [Theory]
    [InlineData(1, 20)]
    [InlineData(20, 40)]
    [InlineData(40, 70)]
    [InlineData(70, 100)]
    public void Higher_level_means_more_snow(int left, int right)
    {
        IntensityProfile a = IntensityProfile.FromLevel(left);
        IntensityProfile b = IntensityProfile.FromLevel(right);
        Assert.True(b.TargetFlakes >= a.TargetFlakes);
        Assert.True(b.MaxFall >= a.MaxFall);
        Assert.True(b.MaxSize >= a.MaxSize);
        Assert.True(b.MaxDrift >= a.MaxDrift);
        Assert.True(b.FillSeconds <= a.FillSeconds);
    }

    [Fact]
    public void Out_of_range_levels_clamp()
    {
        Assert.Equal(1, IntensityProfile.FromLevel(-5).Level);
        Assert.Equal(100, IntensityProfile.FromLevel(500).Level);
    }
}
