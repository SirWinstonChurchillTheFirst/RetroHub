using RetroHub.Core.Emulation.Windows;
using Xunit;

namespace RetroHub.Core.Tests.Emulation;

public sealed class MonitorPlacementTests
{
    // the user's real layout: 2560x1440 primary, 1920x1080 left of it (taskbar 48 px)
    private static readonly MonitorArea Primary = new(1, new ScreenRect(0, 0, 2560, 1440), new ScreenRect(0, 0, 2560, 1392));
    private static readonly MonitorArea Left = new(2, new ScreenRect(-1920, 468, 0, 1548), new ScreenRect(-1920, 468, 0, 1500));

    [Fact]
    public void Fullscreen_window_on_the_other_monitor_gets_the_whole_target_monitor()
    {
        var to = MonitorPlacement.PlanMove(new ScreenRect(-1920, 468, 0, 1548), Left, Primary);
        Assert.Equal(Primary.Bounds, to);
    }

    [Fact]
    public void Nearly_fullscreen_window_counts_as_fullscreen()
    {
        var to = MonitorPlacement.PlanMove(new ScreenRect(-1919, 469, -1, 1547), Left, Primary);
        Assert.Equal(Primary.Bounds, to);
    }

    [Fact]
    public void Normal_window_keeps_its_size_and_is_centred_in_the_work_area()
    {
        var to = MonitorPlacement.PlanMove(new ScreenRect(-1800, 500, -1160, 980), Left, Primary);
        Assert.Equal(new ScreenRect(960, 456, 1600, 936), to);
    }

    [Fact]
    public void Window_larger_than_the_target_is_shrunk_to_its_work_area()
    {
        var big = new MonitorArea(3, new ScreenRect(0, 0, 3840, 2160), new ScreenRect(0, 0, 3840, 2112));
        var small = new MonitorArea(4, new ScreenRect(3840, 0, 5120, 720), new ScreenRect(3840, 0, 5120, 680));
        var to = MonitorPlacement.PlanMove(new ScreenRect(100, 100, 2100, 1300), big, small);
        Assert.Equal(new ScreenRect(3840, 0, 5120, 680), to);
    }

    [Fact]
    public void Window_already_on_the_target_monitor_is_left_alone()
    {
        Assert.Null(MonitorPlacement.PlanMove(new ScreenRect(0, 0, 2560, 1440), Primary, Primary));
        Assert.Null(MonitorPlacement.PlanMove(new ScreenRect(100, 100, 900, 700), Primary, Primary));
    }
}
