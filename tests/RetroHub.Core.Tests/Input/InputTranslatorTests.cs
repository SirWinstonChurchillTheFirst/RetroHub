using RetroHub.Input;
using Xunit;

namespace RetroHub.Core.Tests.Input;

public class InputTranslatorTests
{
    private static PadInput P(int id, PadButtons b = PadButtons.None, float x = 0, float y = 0, float lt = 0, float rt = 0)
        => new(id, new PadSnapshot(b, x, y, lt, rt));

    private static IReadOnlyList<UiAction> U(InputTranslator t, long ms, params PadInput[] pads) => t.Update(pads, ms);

    /// <summary>A translator that has already seen pad 0 idle (so the held-at-start guard is not involved).</summary>
    private static InputTranslator Fresh(int pads = 1)
    {
        var t = new InputTranslator();
        t.Update(Enumerable.Range(0, pads).Select(i => P(i)).ToArray(), 0);
        return t;
    }

    [Fact]
    public void Buttons_fire_once_on_press_and_again_after_release()
    {
        var t = Fresh();
        Assert.Equal([UiAction.Accept], U(t, 10, P(0, PadButtons.South)));
        for (long ms = 20; ms < 3000; ms += 8) Assert.Empty(U(t, ms, P(0, PadButtons.South))); // no repeat
        Assert.Empty(U(t, 3000, P(0)));
        Assert.Equal([UiAction.Accept], U(t, 3010, P(0, PadButtons.South)));
    }

    [Theory]
    [InlineData(PadButtons.South, UiAction.Accept)]
    [InlineData(PadButtons.East, UiAction.Back)]
    [InlineData(PadButtons.West, UiAction.Favorite)]
    [InlineData(PadButtons.North, UiAction.Search)]
    [InlineData(PadButtons.Start, UiAction.Menu)]
    [InlineData(PadButtons.Back, UiAction.View)]
    [InlineData(PadButtons.LeftShoulder, UiAction.PageLeft)]
    [InlineData(PadButtons.RightShoulder, UiAction.PageRight)]
    [InlineData(PadButtons.Guide, UiAction.Home)]
    [InlineData(PadButtons.DPadUp, UiAction.Up)]
    [InlineData(PadButtons.DPadDown, UiAction.Down)]
    [InlineData(PadButtons.DPadLeft, UiAction.Left)]
    [InlineData(PadButtons.DPadRight, UiAction.Right)]
    public void Button_mapping(PadButtons button, UiAction expected)
    {
        var t = Fresh();
        Assert.Equal([expected], U(t, 10, P(0, button)));
    }

    [Fact]
    public void Stick_clicks_are_ignored()
    {
        var t = Fresh();
        Assert.Empty(U(t, 10, P(0, PadButtons.LeftStick | PadButtons.RightStick)));
    }

    [Fact]
    public void SwapAcceptBack_swaps_south_and_east()
    {
        var t = Fresh();
        t.SwapAcceptBack = true;
        Assert.Equal([UiAction.Back], U(t, 10, P(0, PadButtons.South)));
        Assert.Empty(U(t, 20, P(0)));
        Assert.Equal([UiAction.Accept], U(t, 30, P(0, PadButtons.East)));
        Assert.Empty(U(t, 40, P(0)));
        t.SwapAcceptBack = false;
        Assert.Equal([UiAction.Accept], U(t, 50, P(0, PadButtons.South)));
    }

    [Fact]
    public void Simultaneous_presses_fire_in_stable_order()
    {
        var t = Fresh();
        var a = U(t, 10, P(0, PadButtons.DPadDown | PadButtons.South | PadButtons.West));
        Assert.Equal([UiAction.Down, UiAction.Accept, UiAction.Favorite], a);
    }

    [Fact]
    public void Direction_repeat_timing_400_then_80_then_50_after_2s()
    {
        var t = Fresh();
        var fired = new List<long>();
        for (long ms = 1000; ms <= 1000 + 2300; ms++) // 1 ms polling, press at 1000
            foreach (var a in U(t, ms, P(0, PadButtons.DPadDown)))
            {
                Assert.Equal(UiAction.Down, a);
                fired.Add(ms - 1000);
            }

        var expected = new List<long> { 0 };
        long next = 400;
        while (next <= 2300)
        {
            expected.Add(next);
            next += next >= 2000 ? 50 : 80;
        }
        Assert.Equal(expected, fired);
        Assert.Equal(400, fired[1]);
        Assert.Equal(480, fired[2]);
        Assert.Contains(2000L, fired);
        Assert.Contains(2050L, fired); // accelerated
    }

    [Fact]
    public void Direction_repeat_at_120Hz_polling_is_close_to_nominal()
    {
        var t = Fresh();
        var fired = new List<long>();
        for (long ms = 0; ms <= 1000; ms += 8)
            if (U(t, ms, P(0, PadButtons.DPadRight)).Count > 0) fired.Add(ms);
        Assert.Equal(0, fired[0]);
        Assert.InRange(fired[1], 400, 408);
        for (int i = 2; i < fired.Count; i++) Assert.InRange(fired[i] - fired[i - 1], 72, 88);
    }

    [Fact]
    public void Repeat_does_not_burst_after_a_stall()
    {
        var t = Fresh();
        Assert.Single(U(t, 0, P(0, PadButtons.DPadUp)));
        Assert.Single(U(t, 1500, P(0, PadButtons.DPadUp))); // 1.5 s gap → one repeat, not a burst
        Assert.Empty(U(t, 1510, P(0, PadButtons.DPadUp)));
    }

    [Fact]
    public void Release_stops_repeat_and_next_press_starts_over()
    {
        var t = Fresh();
        U(t, 0, P(0, PadButtons.DPadLeft));
        Assert.Single(U(t, 400, P(0, PadButtons.DPadLeft)));
        Assert.Empty(U(t, 410, P(0)));
        Assert.Equal([UiAction.Left], U(t, 420, P(0, PadButtons.DPadLeft)));
        Assert.Empty(U(t, 800, P(0, PadButtons.DPadLeft))); // 380 ms after the new press
        Assert.Single(U(t, 820, P(0, PadButtons.DPadLeft)));
    }

    [Fact]
    public void Stick_deadzone_with_hysteresis()
    {
        var t = Fresh();
        Assert.Empty(U(t, 10, P(0, x: 0.45f)));                 // below press threshold
        Assert.Equal([UiAction.Right], U(t, 20, P(0, x: 0.5f))); // press at 0.5
        Assert.Empty(U(t, 30, P(0, x: 0.4f)));                  // still held (above 0.35)
        Assert.Single(U(t, 420, P(0, x: 0.36f)));                // ... and repeating
        Assert.Empty(U(t, 430, P(0, x: 0.34f)));                // released below 0.35
        Assert.Empty(U(t, 440, P(0, x: 0.45f)));                // needs 0.5 again
        Assert.Equal([UiAction.Right], U(t, 450, P(0, x: 0.55f)));
    }

    [Fact]
    public void Stick_directions_follow_sdl_axis_convention()
    {
        var t = Fresh();
        Assert.Equal([UiAction.Up], U(t, 10, P(0, y: -0.8f)));
        Assert.Empty(U(t, 20, P(0)));
        Assert.Equal([UiAction.Down], U(t, 30, P(0, y: 0.8f)));
        Assert.Empty(U(t, 40, P(0)));
        Assert.Equal([UiAction.Left], U(t, 50, P(0, x: -0.8f)));
    }

    [Fact]
    public void Stick_diagonal_uses_dominant_axis_without_chatter()
    {
        var t = Fresh();
        Assert.Equal([UiAction.Right], U(t, 10, P(0, x: 0.75f, y: 0.6f)));
        Assert.Empty(U(t, 20, P(0, x: 0.65f, y: 0.7f)));     // y slightly bigger: not enough to switch
        Assert.Equal([UiAction.Down], U(t, 30, P(0, x: 0.55f, y: 0.8f))); // y > x * 1.25 → switch
        Assert.Empty(U(t, 40, P(0, x: 0.1f, y: 0.8f)));
    }

    [Fact]
    public void Dpad_and_stick_for_the_same_direction_do_not_double_fire()
    {
        var t = Fresh();
        Assert.Equal([UiAction.Down], U(t, 10, P(0, PadButtons.DPadDown)));
        Assert.Empty(U(t, 20, P(0, PadButtons.DPadDown, y: 0.9f)));
        Assert.Empty(U(t, 30, P(0, y: 0.9f)));
    }

    [Fact]
    public void Triggers_fire_above_threshold_with_hysteresis()
    {
        var t = Fresh();
        Assert.Empty(U(t, 10, P(0, lt: 0.6f)));                     // not strictly greater
        Assert.Equal([UiAction.TriggerLeft], U(t, 20, P(0, lt: 0.61f)));
        Assert.Empty(U(t, 30, P(0, lt: 0.45f)));                    // still held
        Assert.Empty(U(t, 1000, P(0, lt: 1f)));                     // no repeat
        Assert.Empty(U(t, 1010, P(0, lt: 0.3f)));                   // released
        Assert.Equal([UiAction.TriggerLeft], U(t, 1020, P(0, lt: 0.7f)));
        Assert.Equal([UiAction.TriggerRight], U(t, 1030, P(0, lt: 0.7f, rt: 0.9f)));
    }

    [Fact]
    public void Held_at_connect_does_not_fire_until_released()
    {
        var t = new InputTranslator();
        Assert.Empty(U(t, 0, P(0, PadButtons.South | PadButtons.DPadDown, x: 0.9f, lt: 1f)));
        for (long ms = 8; ms < 2000; ms += 8)
            Assert.Empty(U(t, ms, P(0, PadButtons.South | PadButtons.DPadDown, x: 0.9f, lt: 1f)));
        // a different button pressed meanwhile works
        Assert.Equal([UiAction.Back], U(t, 2000, P(0, PadButtons.South | PadButtons.East | PadButtons.DPadDown, x: 0.9f, lt: 1f)));
        // release South only → pressing it again fires; DPadDown still blocked
        Assert.Empty(U(t, 2010, P(0, PadButtons.DPadDown, x: 0.9f, lt: 1f)));
        Assert.Equal([UiAction.Accept], U(t, 2020, P(0, PadButtons.South | PadButtons.DPadDown, x: 0.9f, lt: 1f)));
        // centre the stick, release trigger + dpad
        Assert.Empty(U(t, 2030, P(0)));
        Assert.Equal([UiAction.Down], U(t, 2040, P(0, PadButtons.DPadDown)));
        Assert.Equal([UiAction.TriggerLeft], U(t, 2050, P(0, PadButtons.DPadDown, lt: 1f)));
    }

    [Fact]
    public void Held_after_reset_does_not_fire_until_released()
    {
        var t = Fresh();
        Assert.Equal([UiAction.Down], U(t, 10, P(0, PadButtons.DPadDown)));
        t.Reset(); // e.g. Resume after a game
        for (long ms = 20; ms < 1500; ms += 8) Assert.Empty(U(t, ms, P(0, PadButtons.DPadDown)));
        Assert.Empty(U(t, 1500, P(0)));
        Assert.Equal([UiAction.Down], U(t, 1510, P(0, PadButtons.DPadDown)));
    }

    [Fact]
    public void Newly_connected_pad_with_held_buttons_does_not_disturb_others()
    {
        var t = Fresh();
        Assert.Empty(U(t, 10, P(0), P(1, PadButtons.South))); // pad 1 appears holding South
        Assert.Equal([UiAction.Accept], U(t, 20, P(0, PadButtons.South), P(1, PadButtons.South)));
    }

    [Fact]
    public void Multiple_pads_are_merged()
    {
        var t = Fresh(2);
        Assert.Equal([UiAction.Down], U(t, 0, P(0, PadButtons.DPadDown), P(1)));
        Assert.Equal(0, t.LastActivePadId);
        // the second pad pressing the same direction neither re-fires nor resets the repeat
        Assert.Empty(U(t, 100, P(0, PadButtons.DPadDown), P(1, PadButtons.DPadDown)));
        Assert.Empty(U(t, 200, P(0), P(1, PadButtons.DPadDown)));
        Assert.Equal([UiAction.Down], U(t, 400, P(0), P(1, PadButtons.DPadDown)));
        // different buttons on different pads both fire
        Assert.Equal([UiAction.Accept, UiAction.Back], U(t, 410, P(0, PadButtons.South), P(1, PadButtons.East)));
        Assert.Empty(U(t, 420, P(0), P(1)));
        Assert.Equal([UiAction.Search], U(t, 430, P(0), P(1, PadButtons.North)));
        Assert.Equal(1, t.LastActivePadId);
    }

    [Fact]
    public void Disconnecting_pad_releases_its_inputs_and_reconnect_is_guarded()
    {
        var t = Fresh(2);
        Assert.Equal([UiAction.Up], U(t, 0, P(0), P(1, PadButtons.DPadUp)));
        Assert.Empty(U(t, 100, P(0)));                                   // pad 1 gone → released
        Assert.Empty(U(t, 600, P(0)));                                   // no repeat any more
        Assert.Empty(U(t, 700, P(0), P(1, PadButtons.DPadUp)));          // back, still holding → guarded
        Assert.Empty(U(t, 710, P(0), P(1)));
        Assert.Equal([UiAction.Up], U(t, 720, P(0), P(1, PadButtons.DPadUp)));
    }

    [Fact]
    public void Invalid_axis_values_are_treated_as_centred()
    {
        var t = Fresh();
        Assert.Empty(U(t, 10, P(0, x: float.NaN, y: float.PositiveInfinity, lt: float.NaN)));
        Assert.Equal([UiAction.Accept], U(t, 20, P(0, PadButtons.South, x: float.NaN)));
    }

    [Fact]
    public void Custom_options_are_honoured()
    {
        var t = new InputTranslator(new InputTranslatorOptions { RepeatDelayMs = 200, RepeatIntervalMs = 100, AccelerateAfterMs = 100_000 });
        U(t, 0, P(0));
        Assert.Single(U(t, 0, P(0, PadButtons.DPadDown)));
        Assert.Empty(U(t, 199, P(0, PadButtons.DPadDown)));
        Assert.Single(U(t, 200, P(0, PadButtons.DPadDown)));
        Assert.Empty(U(t, 299, P(0, PadButtons.DPadDown)));
        Assert.Single(U(t, 300, P(0, PadButtons.DPadDown)));
    }
}
