using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using RetroHub.App.Theming;

namespace RetroHub.App.Controls;

/// <summary>
/// "Herausziehen und zurückstellen" (R2 animation, DESIGN 9.2): an unscaled layer above the shell on which ONE box flies
/// between a shelf and the detail stage. Opening a game takes a bitmap of the box as it stands (the same copy the
/// start transition uses), the page changes underneath, and the copy flies to the stage box in 300 ms CubicInOut along a
/// slight arc (24 px up at the middle), growing to it; the stage box stays hidden until the copy lands on it. Going back,
/// the stage box flies into its slot on the shelf (260 ms); the slot stays a gap until it lands, then the box settles
/// with a 2 px bounce. Only under full motion (reduced: the page cross-fade alone; off: nothing). The target is looked up
/// every frame (a cover decoding late changes its rectangle); when it disappears (recycled, scrolled away) the flight
/// is dropped and everything shows again. One bitmap, one image, transforms only – virtualised tiles are only hidden.
/// </summary>
public sealed class PullLayer : Canvas
{
    public static readonly TimeSpan OutDuration = TimeSpan.FromMilliseconds(300);
    public static readonly TimeSpan BackDuration = TimeSpan.FromMilliseconds(260);
    /// <summary>The arc: how far the copy rises above the straight line at the middle of the flight.</summary>
    public const double Arc = 24;

    private readonly Image _copy = new() { Stretch = Stretch.Fill, IsVisible = false, IsHitTestVisible = false };
    private Tween? _tween;
    private RenderTargetBitmap? _bitmap;
    private Control? _hidden;

    public PullLayer()
    {
        IsHitTestVisible = false;
        ClipToBounds = true;
        RenderOptions.SetBitmapInterpolationMode(_copy, Avalonia.Media.Imaging.BitmapInterpolationMode.HighQuality);
        Children.Add(_copy);
    }

    /// <summary>A copy of a box as it stands on screen, in this layer's coordinates.</summary>
    public sealed record Snap(Rect Rect, RenderTargetBitmap Bitmap);

    /// <summary>A flight is running (or held by <see cref="Tween.Hold"/>).</summary>
    public bool IsFlying => _tween?.IsRunning == true;

    /// <summary>The copy's rectangle in the frame shown last (tests, previews).</summary>
    public Rect CopyRect { get; private set; }

    /// <summary>Takes the copy of <paramref name="art"/> (null when it is not on screen).</summary>
    public Snap? Take(CoverArt art) => FlightLayer.Snapshot(art, this) is { } s ? new Snap(s.Rect, s.Bitmap) : null;

    /// <summary>
    /// Flies <paramref name="snap"/> to the rectangle <paramref name="target"/> returns (asked every frame, in this layer's
    /// coordinates; null drops the flight). <paramref name="gap"/> is hidden until the copy lands.
    /// </summary>
    public void Fly(Snap snap, Func<Rect?> target, TimeSpan duration, Control? gap, Action? landed = null)
    {
        Stop();
        if (duration <= TimeSpan.Zero)
        {
            snap.Bitmap.Dispose();
            landed?.Invoke();
            return;
        }
        _bitmap = snap.Bitmap;
        _copy.Source = _bitmap;
        _hidden = gap;
        if (gap != null) gap.Opacity = 0;
        var from = snap.Rect;
        var flight = new object();
        _flight = flight;
        _tween = Tween.Run(this, duration, p =>
        {
            if (_flight != flight) return;
            if (target() is not { Width: > 1, Height: > 1 } to)
            {
                End(); // the target is gone: no flight, everything shows again
                return;
            }
            var e = Ease.CubicInOut(p);
            var r = new Rect(Ease.Lerp(from.X, to.X, e), Ease.Lerp(from.Y, to.Y, e) - Arc * Ease.Bump(p),
                Ease.Lerp(from.Width, to.Width, e), Ease.Lerp(from.Height, to.Height, e));
            Place(r);
        }, () =>
        {
            if (_flight != flight) return;
            End();
            landed?.Invoke();
        });
    }

    private object? _flight;

    private void Place(Rect r)
    {
        CopyRect = r;
        SetLeft(_copy, r.X);
        SetTop(_copy, r.Y);
        _copy.Width = Math.Max(1, r.Width);
        _copy.Height = Math.Max(1, r.Height);
        _copy.IsVisible = true;
    }

    /// <summary>Ends a running flight at once: the copy goes, the hidden box shows again.</summary>
    public void Stop() => End();

    private void End()
    {
        _flight = null;
        var t = _tween;
        _tween = null;
        t?.Stop();
        _copy.IsVisible = false;
        _copy.Source = null;
        _bitmap?.Dispose();
        _bitmap = null;
        if (_hidden != null) _hidden.Opacity = 1;
        _hidden = null;
    }

    /// <summary>The 2 px settle of a box that landed in its slot (Step: down 2 px and back; only under full motion).</summary>
    public static void Settle(Control box)
    {
        if (!Motion.AllowsMovement) return;
        if (box.RenderTransform != null && box.RenderTransform is not TranslateTransform) return;
        var t = box.RenderTransform as TranslateTransform ?? new TranslateTransform();
        box.RenderTransform = t;
        Tween.Run(box, Motion.Step, p => t.Y = 2 * Ease.Bump(p));
    }

    /// <summary>The on-screen rectangle of a box's cover in this layer (render transforms included: the lift too).</summary>
    public Rect? RectOf(CoverArt? art)
    {
        if (art == null || !art.IsEffectivelyVisible) return null;
        var local = art.ArtRect;
        if (local.Width < 2 || local.Height < 2) return null;
        if (art.TranslatePoint(local.TopLeft, this) is not { } tl || art.TranslatePoint(local.BottomRight, this) is not { } br) return null;
        return new Rect(tl, br);
    }
}
