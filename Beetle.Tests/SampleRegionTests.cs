using Beetle.Core;
using Xunit;

namespace Beetle.Tests;

public class SampleRegionTests
{
    [Fact]
    public void Full_maps_to_the_whole_screen()
    {
        Assert.Equal((0, 0, 3840, 2160), SampleRegion.Full.ToPixels(3840, 2160));
    }

    [Fact]
    public void Center_preset_maps_to_the_middle_half()
    {
        var center = SampleRegion.Presets.Single(p => p.Name == "Center").Region;
        Assert.Equal((960, 540, 1920, 1080), center.ToPixels(3840, 2160));
    }

    [Fact]
    public void FromCorners_accepts_a_drag_in_any_direction()
    {
        var forward = SampleRegion.FromCorners(0.1, 0.2, 0.5, 0.6);
        var backward = SampleRegion.FromCorners(0.5, 0.6, 0.1, 0.2);
        Assert.Equal(forward, backward);
        Assert.Equal(0.1, forward.X, 6);
        Assert.Equal(0.4, forward.Width, 6);
    }

    [Fact]
    public void Normalize_keeps_the_region_on_screen()
    {
        var r = new SampleRegion(0.9, 0.9, 0.5, 0.5).Normalize();
        Assert.True(r.X + r.Width <= 1 + 1e-9);
        Assert.True(r.Y + r.Height <= 1 + 1e-9);
    }

    [Fact]
    public void A_click_without_a_drag_still_gives_a_usable_area()
    {
        var r = SampleRegion.FromCorners(0.5, 0.5, 0.5, 0.5);
        Assert.True(r.Width >= SampleRegion.MinimumSize);
        var (_, _, w, h) = r.ToPixels(1920, 1080);
        Assert.True(w >= 1 && h >= 1);
    }

    [Fact]
    public void ToPixels_never_runs_past_the_screen_edge()
    {
        var (x, y, w, h) = new SampleRegion(0.75, 0.75, 0.25, 0.25).ToPixels(1001, 777);
        Assert.True(x + w <= 1001);
        Assert.True(y + h <= 777);
    }

    [Fact]
    public void PresetName_recognises_presets_and_not_custom_areas()
    {
        Assert.Equal("Top", SampleRegion.Presets.Single(p => p.Name == "Top").Region.PresetName);
        Assert.Null(new SampleRegion(0.1, 0.1, 0.3, 0.3).PresetName);
    }

    [Fact]
    public void Settings_round_trip_the_region()
    {
        var settings = new AppSettings { Region = new SampleRegion(0.1, 0.2, 0.3, 0.4) };
        Assert.Equal(new SampleRegion(0.1, 0.2, 0.3, 0.4), settings.Region);
        Assert.Equal(SampleRegion.Full, new AppSettings().Region);
    }
}
