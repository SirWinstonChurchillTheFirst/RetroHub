using RetroHub.Input;
using RetroHub.Input.Native;
using Xunit;

namespace RetroHub.Core.Tests.Input;

/// <summary>
/// The real SDL3 gamepad path (SDL_GetGamepads → SDL_OpenGamepad → SDL_GetGamepadButton/Axis/Type) exercised with
/// SDL virtual gamepads, so hot-plug, state reading and the held-at-connect guard are tested without hardware.
/// </summary>
[Collection(Sdl3Collection.Name)]
public class SdlVirtualPadTests
{
    private const ushort SonyVid = 0x054C, DualSensePid = 0x0CE6;
    private const ushort MicrosoftVid = 0x045E, Xbox360Pid = 0x028E;

    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);

    [Fact]
    public void Virtual_descriptor_has_the_sdl_size()
    {
        Assert.Equal(IntPtr.Size == 8 ? 136 : 84, SdlVirtualGamepad.DescriptorSize);
    }

    [Fact]
    public void Sdl_hands_out_new_instance_ids_after_reinitialisation()
    {
        // the reason GamepadService keeps its own published identity across Suspend/Resume
        uint first, second;
        Assert.True(Sdl3.SDL_Init(Sdl3.SDL_INIT_GAMEPAD), Sdl3.GetError());
        using (var a = SdlVirtualGamepad.Attach("Id probe", MicrosoftVid, Xbox360Pid)) first = a.InstanceId;
        Sdl3.SDL_QuitSubSystem(Sdl3.SDL_INIT_GAMEPAD);
        Assert.True(Sdl3.SDL_Init(Sdl3.SDL_INIT_GAMEPAD), Sdl3.GetError());
        using (var b = SdlVirtualGamepad.Attach("Id probe", MicrosoftVid, Xbox360Pid)) second = b.InstanceId;
        Sdl3.SDL_QuitSubSystem(Sdl3.SDL_INIT_GAMEPAD);
        Assert.NotEqual(first, second);
        Assert.Equal(0u, Sdl3.SDL_WasInit(Sdl3.SDL_INIT_GAMEPAD));
    }

    [Fact]
    public void Gamepad_service_end_to_end_with_a_virtual_sdl_pad()
    {
        using var s = new GamepadService(GamepadBackendPreference.Sdl3Only);
        var connected = new System.Collections.Concurrent.ConcurrentQueue<GamepadInfo>();
        var disconnected = new System.Collections.Concurrent.ConcurrentQueue<GamepadInfo>();
        var actions = new System.Collections.Concurrent.ConcurrentQueue<UiAction>();
        s.Connected += connected.Enqueue;
        s.Disconnected += disconnected.Enqueue;
        s.ActionTriggered += actions.Enqueue;
        s.Start();
        Assert.Equal(GamepadService.BackendSdl3, s.Backend);

        // plugged in while South is already held → must not fire
        using var pad = SdlVirtualGamepad.Attach("Virtual DualSense", SonyVid, DualSensePid, held: PadButtons.South);
        Assert.True(SpinWait.SpinUntil(() => !connected.IsEmpty, Wait), "hot-plug not detected");
        var info = Assert.Single(connected);
        Assert.Equal("Virtual DualSense", info.Name);
        Assert.Equal(PadStyle.PlayStation, info.Style);
        Assert.Equal([info], s.Pads);
        Thread.Sleep(250);
        Assert.Empty(actions);

        pad.Set(PadButtons.None);
        Thread.Sleep(50);
        pad.Set(PadButtons.South);
        Assert.True(SpinWait.SpinUntil(() => actions.Contains(UiAction.Accept), Wait));
        pad.Set(PadButtons.None, leftY: 1f); // stick down (SDL: +Y = down)
        Assert.True(SpinWait.SpinUntil(() => actions.Contains(UiAction.Down), Wait));
        pad.Set(PadButtons.None, rightTrigger: 1f);
        Assert.True(SpinWait.SpinUntil(() => actions.Contains(UiAction.TriggerRight), Wait));
        Assert.Equal(PadStyle.PlayStation, s.LastUsedStyle);
        Assert.Equal(1, actions.Count(a => a == UiAction.Accept));

        pad.Dispose(); // unplug
        Assert.True(SpinWait.SpinUntil(() => !disconnected.IsEmpty, Wait), "unplug not detected");
        Assert.Equal(info, Assert.Single(disconnected));
        Assert.Empty(s.Pads);
    }

    [Fact]
    public void Backend_never_reports_the_pre_update_state_of_a_freshly_opened_pad()
    {
        // A pad that is plugged in (or re-opened after Resume) with a button held: SDL's state for a freshly opened
        // device is all zeros until the next SDL update. If the backend reported that zero snapshot, the translator
        // would take "nothing held" as the connect state and fire the held button as a fresh press.
        var backend = SdlBackend.TryCreate();
        Assert.NotNull(backend);
        try
        {
            using var pad = SdlVirtualGamepad.Attach("RetroHub Test Pad", SonyVid, DualSensePid);
            pad.Set(PadButtons.South | PadButtons.Start);

            var states = new List<PadSnapshot>();
            var pads = new List<PadInput>();
            var added = new List<GamepadInfo>();
            var removed = new List<GamepadInfo>();
            var addedTotal = new List<GamepadInfo>();
            for (long now = 0; now <= 50; now += 10)
            {
                pads.Clear(); added.Clear(); removed.Clear();
                backend.Poll(now, pads, added, removed);
                addedTotal.AddRange(added);
                states.AddRange(pads.Select(p => p.State));
            }

            var info = Assert.Single(addedTotal);
            Assert.Equal("RetroHub Test Pad", info.Name);
            Assert.Equal(PadStyle.PlayStation, info.Style);
            Assert.Equal(unchecked((int)pad.InstanceId), info.InstanceId);
            Assert.NotEmpty(states);
            Assert.All(states, s => Assert.Equal(PadButtons.South | PadButtons.Start, s.Buttons));
        }
        finally
        {
            backend.Dispose();
        }
        Assert.Equal(0u, Sdl3.SDL_WasInit(Sdl3.SDL_INIT_GAMEPAD));
    }

    [Fact]
    public void Backend_reads_buttons_axes_and_hot_unplug()
    {
        var backend = SdlBackend.TryCreate();
        Assert.NotNull(backend);
        try
        {
            var pads = new List<PadInput>();
            var added = new List<GamepadInfo>();
            var removed = new List<GamepadInfo>();
            long now = 0;
            PadSnapshot? Poll()
            {
                pads.Clear(); added.Clear(); removed.Clear();
                backend.Poll(now, pads, added, removed);
                now += 10;
                return pads.Count == 0 ? null : pads[0].State;
            }

            var pad = SdlVirtualGamepad.Attach("Virtual 360", MicrosoftVid, Xbox360Pid);
            Poll(); Poll();
            Assert.Equal(PadStyle.Xbox, Assert.Single(backend.OpenPads).Style);

            pad.Set(PadButtons.East | PadButtons.DPadLeft | PadButtons.Guide, leftX: -1f, leftY: 0.5f, leftTrigger: 1f);
            var s = Poll()!.Value;
            Assert.Equal(PadButtons.East | PadButtons.DPadLeft | PadButtons.Guide, s.Buttons);
            Assert.Equal(-1f, s.LeftX, 0.01f);
            Assert.Equal(0.5f, s.LeftY, 0.01f);
            Assert.Equal(1f, s.LeftTrigger, 0.01f);
            Assert.Equal(0f, s.RightTrigger, 0.01f);

            pad.Set(PadButtons.None);
            s = Poll()!.Value;
            Assert.Equal(PadButtons.None, s.Buttons);
            Assert.Equal(0f, s.LeftX, 0.01f);
            Assert.Equal(0f, s.LeftTrigger, 0.01f);

            pad.Dispose(); // unplug
            Assert.Null(Poll());
            var gone = Assert.Single(removed);
            Assert.Equal("Virtual 360", gone.Name);
            Assert.Empty(backend.OpenPads);
        }
        finally
        {
            backend.Dispose();
        }
    }
}
