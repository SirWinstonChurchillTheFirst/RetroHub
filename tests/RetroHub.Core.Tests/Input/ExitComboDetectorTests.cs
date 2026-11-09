using RetroHub.Input;
using Xunit;

namespace RetroHub.Core.Tests.Input;

public class ExitComboDetectorTests
{
    private const PadButtons Combo = PadButtons.Back | PadButtons.Start;

    private static PadInput P(int id, PadButtons b = PadButtons.None) => new(id, new PadSnapshot(b));

    private static ExitComboDetector Fresh(double hold = 1.5)
    {
        var d = new ExitComboDetector(hold);
        d.Update([P(0)], 0);
        return d;
    }

    /// <summary>Holds <paramref name="b"/> from <paramref name="from"/> to <paramref name="to"/> at 15 Hz-ish steps
    /// and returns the timestamps at which the detector fired.</summary>
    private static List<long> Hold(ExitComboDetector d, PadButtons b, long from, long to, long step = 10)
    {
        var fired = new List<long>();
        for (long t = from; t <= to; t += step)
            if (d.Update([P(0, b)], t)) fired.Add(t);
        return fired;
    }

    [Fact]
    public void Back_and_start_held_for_hold_time_fires_once()
    {
        var d = Fresh();
        var fired = Hold(d, Combo, 1000, 6000);
        Assert.Equal([2500L], fired); // exactly HoldSeconds after the combo started, once per hold
    }

    [Fact]
    public void Releasing_early_does_not_fire_and_restarts_the_timer()
    {
        var d = Fresh();
        Assert.Empty(Hold(d, Combo, 1000, 2400));
        Assert.False(d.Update([P(0, PadButtons.Back)], 2410)); // Start released
        Assert.Equal([3920L], Hold(d, Combo, 2420, 4000));     // full 1.5 s again
    }

    [Fact]
    public void Re_holding_fires_again()
    {
        var d = Fresh();
        Assert.Single(Hold(d, Combo, 0, 2000));
        Assert.False(d.Update([P(0)], 2010));
        Assert.Equal([3520L], Hold(d, Combo, 2020, 5000));
    }

    [Fact]
    public void Single_buttons_never_fire()
    {
        var d = Fresh();
        Assert.Empty(Hold(d, PadButtons.Back, 0, 5000));
        Assert.Empty(Hold(d, PadButtons.Start, 5010, 10000));
        Assert.Empty(Hold(d, PadButtons.South | PadButtons.East | PadButtons.Back, 10010, 15000));
    }

    [Fact]
    public void Guide_fires_immediately_once_per_press()
    {
        var d = Fresh();
        Assert.Equal([100L], Hold(d, PadButtons.Guide, 100, 3000));
        Assert.False(d.Update([P(0)], 3010));
        Assert.True(d.Update([P(0, PadButtons.Guide)], 3020));
    }

    [Fact]
    public void Inputs_held_when_watching_starts_are_ignored_until_released()
    {
        var d = new ExitComboDetector();
        Assert.Empty(Hold(d, Combo | PadButtons.Guide, 0, 5000));
        Assert.False(d.Update([P(0, PadButtons.Back)], 5010)); // Start + Guide released, Back still blocked
        Assert.Empty(Hold(d, Combo, 5020, 9000));              // Back was never released → still blocked
        Assert.False(d.Update([P(0)], 9010));
        Assert.Equal([10520L], Hold(d, Combo, 9020, 12000));
    }

    [Fact]
    public void Combo_must_be_on_one_pad()
    {
        var d = new ExitComboDetector();
        d.Update([P(0), P(1)], 0);
        for (long t = 10; t <= 5000; t += 10)
            Assert.False(d.Update([P(0, PadButtons.Back), P(1, PadButtons.Start)], t));
    }

    [Fact]
    public void Second_pad_can_trigger()
    {
        var d = new ExitComboDetector();
        d.Update([P(0), P(1)], 0);
        bool fired = false;
        for (long t = 10; t <= 2000; t += 10) fired |= d.Update([P(0), P(1, Combo)], t);
        Assert.True(fired);
    }

    [Fact]
    public void Hold_seconds_is_configurable_and_clamped()
    {
        var d = Fresh(0.5);
        Assert.Equal([600L], Hold(d, Combo, 100, 2000));
        d.HoldSeconds = 100;
        Assert.Equal(10, d.HoldSeconds);
        d.HoldSeconds = double.NaN;
        Assert.Equal(1.5, d.HoldSeconds);
        d.HoldSeconds = 0;
        Assert.Equal(0.1, d.HoldSeconds);
    }

    [Fact]
    public void Disconnected_pad_is_forgotten_and_guarded_on_return()
    {
        var d = Fresh();
        Assert.Empty(Hold(d, Combo, 0, 1000));
        Assert.False(d.Update([], 1010));                       // pad gone mid-hold
        Assert.Empty(Hold(d, Combo, 1020, 5000));               // back with combo held → guarded
        Assert.False(d.Update([P(0)], 5010));
        Assert.Single(Hold(d, Combo, 5020, 7000));
    }
}
