namespace RetroHub.App.Controls;

/// <summary>Result of <see cref="GameScreenFit.Compute"/>: the host size in DIPs and the matching physical pixels.</summary>
public readonly record struct HostFit(double Width, double Height, int PixelWidth, int PixelHeight)
{
    public bool IsEmpty => PixelWidth <= 0 || PixelHeight <= 0;
}

/// <summary>
/// Sizing of the embedded game's place (pure math, no HWNDs). The host is as big as the area allows but only as wide
/// or high as the picture plus the emulator's own bars need – centred, the rest shows RetroHub's background instead of
/// black bars drawn inside the emulator (MonHub GameScreen.SizeHost).
/// </summary>
public static class GameScreenFit
{
    /// <summary>Below this many DIPs of picture room the aspect fit is not attempted (the host just fills the area).</summary>
    public const double MinPicture = 32;

    /// <param name="availableWidth">Room for the host in DIPs.</param>
    /// <param name="availableHeight">Room for the host in DIPs.</param>
    /// <param name="scale">TopLevel.RenderScaling (physical pixels per DIP).</param>
    /// <param name="aspect">Picture width / height (null: fill the area).</param>
    /// <param name="barTopPx">Emulator menu/tool bar height in physical pixels (part of its client area).</param>
    /// <param name="barBottomPx">Emulator status bar height in physical pixels.</param>
    public static HostFit Compute(double availableWidth, double availableHeight, double scale, double? aspect, int barTopPx = 0, int barBottomPx = 0)
    {
        if (!(scale > 0) || double.IsInfinity(scale)) scale = 1;
        double w = Sanitize(availableWidth), h = Sanitize(availableHeight);
        if (w <= 0 || h <= 0) return new HostFit(0, 0, 0, 0);
        double barsY = (Math.Max(0, barTopPx) + Math.Max(0, barBottomPx)) / scale;
        double width = w, height = h;
        if (aspect is double ratio && ratio > 0 && !double.IsInfinity(ratio) && w > MinPicture && h > barsY + MinPicture)
        {
            double pictureW = w, pictureH = h - barsY;
            if (pictureW / pictureH > ratio) pictureW = pictureH * ratio;
            else pictureH = pictureW / ratio;
            width = Math.Floor(pictureW);
            height = Math.Floor(pictureH + barsY);
        }
        width = Math.Min(width, w);
        height = Math.Min(height, h);
        return new HostFit(width, height, (int)Math.Round(width * scale), (int)Math.Round(height * scale));
    }

    /// <summary>Pixel size of a control laid out at <paramref name="width"/> x <paramref name="height"/> DIPs.</summary>
    public static (int Width, int Height) ToPixels(double width, double height, double scale)
    {
        if (!(scale > 0)) scale = 1;
        return ((int)Math.Round(Sanitize(width) * scale), (int)Math.Round(Sanitize(height) * scale));
    }

    /// <summary>A change of less than two DIPs is not worth a new layout (and cannot start a back and forth).</summary>
    public static bool Differs(double current, double wanted) =>
        double.IsNaN(current) != double.IsNaN(wanted) || (!double.IsNaN(current) && Math.Abs(current - wanted) >= 2);

    private static double Sanitize(double v) => double.IsNaN(v) || double.IsInfinity(v) || v < 0 ? 0 : v;
}

/// <summary>A rectangle in physical pixels relative to the host's top-left corner.</summary>
public readonly record struct PixelHole(int Left, int Top, int Right, int Bottom);

/// <summary>
/// Airspace for small overlays: Avalonia content above the embedded game (toasts) is only visible where the native
/// host has a hole. Pure math: where an overlay (in DIPs, relative to the host) cuts into the host, in pixels.
/// </summary>
public static class GameScreenCutout
{
    /// <summary>Extra pixels around the overlay (slide-in animations, shadows).</summary>
    public const int Margin = 8;

    /// <param name="x">The overlay's left edge in DIPs relative to the host's left edge.</param>
    /// <param name="y">The overlay's top edge in DIPs relative to the host's top edge.</param>
    /// <param name="width">The overlay's size in DIPs.</param>
    /// <param name="height">The overlay's size in DIPs.</param>
    /// <param name="hostWidth">The host's size in DIPs.</param>
    /// <param name="hostHeight">The host's size in DIPs.</param>
    /// <param name="scale">Physical pixels per DIP.</param>
    /// <returns>The part of the host to cut out (clipped to the host), or null when the overlay is empty or misses the host.</returns>
    public static PixelHole? Compute(double x, double y, double width, double height, double hostWidth, double hostHeight, double scale)
    {
        if (!(scale > 0) || double.IsInfinity(scale)) scale = 1;
        if (!(width > 0) || !(height > 0) || !(hostWidth > 0) || !(hostHeight > 0)) return null;
        if (double.IsNaN(x) || double.IsNaN(y) || double.IsInfinity(x) || double.IsInfinity(y)) return null;
        int hw = (int)Math.Round(hostWidth * scale), hh = (int)Math.Round(hostHeight * scale);
        int l = Math.Max(0, (int)Math.Floor(x * scale) - Margin);
        int t = Math.Max(0, (int)Math.Floor(y * scale) - Margin);
        int r = Math.Min(hw, (int)Math.Ceiling((x + width) * scale) + Margin);
        int b = Math.Min(hh, (int)Math.Ceiling((y + height) * scale) + Margin);
        return r > l && b > t ? new PixelHole(l, t, r, b) : null;
    }
}

/// <summary>
/// The emulator's bars (menu bar on top, status bar at the bottom) are only taken when two samples a moment apart
/// agree – right after a resize the emulator may not have laid out its inside yet, and a half-way value would make the
/// place jump back and forth.
/// </summary>
public sealed class BarSampler
{
    private (int Top, int Bottom)? _seen;

    /// <summary>The bars currently in use (null until two samples agreed).</summary>
    public (int Top, int Bottom)? Current { get; private set; }

    /// <summary>Feeds a sample; returns true when <see cref="Current"/> changed.</summary>
    public bool Add((int Top, int Bottom) sample)
    {
        bool changed = false;
        if (_seen == sample && Current != sample)
        {
            Current = sample;
            changed = true;
        }
        _seen = sample;
        return changed;
    }

    public void Reset()
    {
        _seen = null;
        Current = null;
    }
}

/// <summary>
/// When an embedded game counts as ready (loading screen goes away, mask off, keys to the game). Driven by a clock
/// the caller passes in, so the rules can be tested without windows:
/// <list type="bullet">
/// <item>normal profiles: the emulator answered <see cref="AnswersNeeded"/> probes in a row and more than
/// <see cref="MinSinceTake"/> passed since the window was taken;</item>
/// <item>profiles with <c>ReadyTitleContains</c> (Azahar answers long before the game runs): as soon as the window
/// title contains that text – answering alone does not count;</item>
/// <item>always after <see cref="MaxSinceTake"/> since the take (never stuck on the loading screen).</item>
/// </list>
/// </summary>
public sealed class EmbedReadiness
{
    public const int AnswersNeeded = 5;
    public static readonly TimeSpan MinSinceTake = TimeSpan.FromSeconds(1.5);
    public static readonly TimeSpan MaxSinceTake = TimeSpan.FromSeconds(30);

    private readonly string? _readyTitle;
    private TimeSpan? _takenAt;

    public EmbedReadiness(string? readyTitleContains) =>
        _readyTitle = string.IsNullOrEmpty(readyTitleContains) ? null : readyTitleContains;

    public int Answers { get; private set; }
    public bool IsReady { get; private set; }
    /// <summary>Why it became ready ("answers", "title", "timeout") – for logs and the test report.</summary>
    public string? Reason { get; private set; }

    /// <summary>A (new) window was taken at <paramref name="now"/>: the count starts again.</summary>
    public void Taken(TimeSpan now)
    {
        _takenAt = now;
        Answers = 0;
    }

    /// <summary>One probe (every tick while not ready). Returns true once ready (and stays true).</summary>
    public bool Update(TimeSpan now, bool responsive, string? title)
    {
        if (IsReady) return true;
        if (_takenAt is not { } taken) return false;
        Answers = responsive ? Answers + 1 : 0;
        var since = now - taken;
        if (since > MaxSinceTake) return Ready("timeout");
        if (_readyTitle != null)
            return title != null && title.Contains(_readyTitle, StringComparison.Ordinal) && Ready("title");
        return Answers >= AnswersNeeded && since > MinSinceTake && Ready("answers");
    }

    /// <summary>Forced (the game was released or the screen goes away).</summary>
    public void Force(string reason) => Ready(reason);

    private bool Ready(string reason)
    {
        IsReady = true;
        Reason ??= reason;
        return true;
    }
}

/// <summary>What the embedded game screen should do on a tick while it has no window (pure; tested headless).</summary>
public enum WindowSearchStep { Wait, Take, GiveUp }

public static class WindowSearch
{
    /// <summary>No window after this long → the game is left to the desktop and runs on as a windowed session.</summary>
    public static readonly TimeSpan GiveUpAfter = TimeSpan.FromSeconds(60);
    /// <summary>A host smaller than this (physical px) is not used: an emulator squeezed to nothing while it sets up its picture can crash.</summary>
    public const int MinHostPixels = 64;

    public static WindowSearchStep Decide(TimeSpan sinceStart, bool everTaken, bool windowFound, int hostWidthPx, int hostHeightPx)
    {
        if (windowFound && hostWidthPx >= MinHostPixels && hostHeightPx >= MinHostPixels) return WindowSearchStep.Take;
        if (!everTaken && sinceStart > GiveUpAfter) return WindowSearchStep.GiveUp;
        return WindowSearchStep.Wait;
    }
}

/// <summary>Full screen auto-hide of the game bar: shown while the cursor is at the top edge or on the bar, hidden 1 s after it left.</summary>
public sealed class BarAutoHide
{
    public static readonly TimeSpan Linger = TimeSpan.FromSeconds(1);
    public static readonly TimeSpan FirstShow = TimeSpan.FromSeconds(2);
    private TimeSpan _seen;

    /// <summary>Full screen was entered at <paramref name="now"/>: the bar stays a little longer first, so it is noticed.</summary>
    public void Start(TimeSpan now) => _seen = now + FirstShow - Linger;

    /// <param name="cursorY">Cursor Y in screen pixels.</param>
    /// <param name="topY">Top edge of the game screen in screen pixels.</param>
    /// <param name="barBottomY">Bottom of the bar in screen pixels while it is visible (else null).</param>
    public bool ShouldShow(TimeSpan now, int cursorY, int topY, int? barBottomY)
    {
        bool near = cursorY <= topY + 3 || (barBottomY is int b && cursorY <= b + 6);
        if (near) _seen = now;
        return near || now - _seen < Linger;
    }
}
