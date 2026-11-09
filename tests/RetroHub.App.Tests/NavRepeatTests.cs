using Avalonia.Input;
using RetroHub.App.Input;
using Xunit;

namespace RetroHub.App.Tests;

/// <summary>Auto-repeat detection of the shell (DESIGN 8.4): a single press never sets it, release always ends it.</summary>
public class NavRepeatTests
{
    [Fact]
    public Task Keyboard_repeat_starts_with_the_second_keydown_and_ends_on_keyup() => UiTest.Run(() =>
    {
        var t = new NavRepeatTracker();
        var changes = new List<bool>();
        t.Changed += changes.Add;
        Assert.False(t.OnKeyDown(Key.Right));
        Assert.False(t.IsActive); // a single press never sets .nav-repeat
        t.OnKeyUp(Key.Right);
        Assert.False(t.OnKeyDown(Key.Right));
        Assert.True(t.OnKeyDown(Key.Right)); // held: KeyDown again without KeyUp
        Assert.True(t.IsActive);
        Assert.True(t.OnKeyDown(Key.Right));
        t.OnKeyUp(Key.Right);
        Assert.False(t.IsActive);
        Assert.Equal([true, false], changes);
        // other keys never count
        Assert.False(t.OnKeyDown(Key.Enter));
        Assert.False(t.OnKeyDown(Key.Enter));
        Assert.False(t.IsActive);
    });

    [Fact]
    public Task Pad_repeat_flag_and_release() => UiTest.Run(() =>
    {
        var t = new NavRepeatTracker();
        t.OnPadDirection(isRepeat: false);
        Assert.False(t.IsActive);
        t.OnPadDirection(isRepeat: true);
        Assert.True(t.IsActive);
        t.OnPadReleased();
        Assert.False(t.IsActive);
    });

    [Fact]
    public Task Lost_release_expires_after_the_safety_net() => UiTest.Run(() =>
    {
        var t = new NavRepeatTracker();
        t.OnPadDirection(isRepeat: true);
        Assert.True(t.IsActive);
        Thread.Sleep(200); // > RepeatRelease (150 ms)
        t.ExpireIfStale();
        Assert.False(t.IsActive);
    });
}
