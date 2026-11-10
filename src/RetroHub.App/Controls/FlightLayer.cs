using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using RetroHub.App.Theming;

namespace RetroHub.App.Controls;

/// <summary>
/// The one staged moment (DESIGN 9.3 "Einschalten"): an unscaled layer above the shell. At a game start
/// <see cref="PowerOn"/> plays, on a timeline of <see cref="Motion.PowerOn"/> (520 ms):
/// <list type="number">
/// <item>0–220 ms: a COPY of the box (a snapshot of its <see cref="CoverArt"/>, the original tile goes to opacity 0 and
/// stays where it is, so virtualisation and focus are untouched) flies to the middle of the window and grows to 1.4×
/// (CubicEaseIn); the room dims to Raum-Tief 94 % (the launch overlay's own scrim, <see cref="Scrim"/>).</item>
/// <item>220–320 ms: the copy fades out; a 2 px line in the system's light grows from the middle to the full width.</item>
/// <item>320 ms: one frame of overexposure, Papier 25 % over the window, 16 ms (never under reduced motion or off).</item>
/// <item>320–520 ms: the line opens vertically into the start surface (<see cref="Reveal"/>, ScaleY, CubicEaseOut); its
/// two edges carry the light and fade.</item>
/// </list>
/// <see cref="PowerOff"/> ("Ausschalten", the return): on a closed room-deep backdrop the picture collapses to a line in
/// the light (160 ms), the line to a point (80 ms); only then the page fades in (280 ms). Reduced motion: a 150 ms cross-fade without flight and overexposure (100 ms
/// out); off: hard. The layer is never above the embedded emulator window: the game screen stays masked while the
/// launch is starting, and the transition ends long before the minimum start time (900 ms).
/// Everything is computed from the elapsed time in <see cref="Apply"/>, so a frame can be shown on its own
/// (<see cref="Freeze"/>, design previews and tests).
/// </summary>
public sealed class FlightLayer : Canvas
{
    /// <summary>Switch-off: collapse 160 + point 80 on a closed room-deep backdrop, then the page fades in (Scene 280).</summary>
    public static readonly TimeSpan OffDuration = TimeSpan.FromMilliseconds(OffPoint + OffFade);
    public const double FlyEnd = 220, LineEnd = 320, FlashEnd = 336, OpenEnd = 520;
    public const double OffCollapse = 160, OffPoint = 240, OffFade = 280;
    public const double ReducedFade = 150, ReducedOff = 100;

    private readonly Image _copy = new() { Stretch = Stretch.Fill, IsVisible = false };
    private readonly Border _line = new() { IsVisible = false };
    private readonly Border _edgeTop = new() { IsVisible = false }, _edgeBottom = new() { IsVisible = false };
    private readonly Border _flash = new() { IsVisible = false, Background = new ImmutableSolidColorBrush(Color.FromArgb(0x40, 0xF4, 0xEC, 0xDF)) };
    private readonly Border _off = new() { IsVisible = false, Background = new ImmutableSolidColorBrush(Color.FromRgb(0x0A, 0x09, 0x08)) };
    /// <summary>The picture collapsing on the switch-off: a lit tube (Regal) on the closed room-deep backdrop.</summary>
    private readonly Border _tube = new() { IsVisible = false, Background = new ImmutableSolidColorBrush(Color.FromRgb(0x1E, 0x1B, 0x17)) };
    private readonly DispatcherTimer _timer;
    private readonly System.Diagnostics.Stopwatch _clock = new();
    private Rect _from;
    private Color _light = Color.Parse("#FFB547");
    private EffectiveMotion _motion = EffectiveMotion.Full;
    private bool _off_mode, _running;
    private Control? _hidden;
    private RenderTargetBitmap? _snapshot;

    public FlightLayer()
    {
        IsHitTestVisible = false;
        ClipToBounds = true;
        Children.Add(_off);
        Children.Add(_tube);
        Children.Add(_copy);
        Children.Add(_edgeTop);
        Children.Add(_edgeBottom);
        Children.Add(_line);
        Children.Add(_flash);
        _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(15) };
        _timer.Tick += (_, _) => Tick();
    }

    /// <summary>The launch overlay's dim (opacity 0 → 1 over the flight).</summary>
    public Control? Scrim { get; set; }

    /// <summary>The start surface ("Spiel wird gestartet" with the box, title, emulator, segment light) that opens from the line.</summary>
    public Control? Reveal { get; set; }

    /// <summary>
    /// Where the copy lands (M6 QA): the box slot of the start surface (240 × 336, standing on its shelf edge). The copy
    /// flies there and shrinks to it, and the start surface shows the SAME bitmap in <see cref="LandingImage"/> instead
    /// of its own <see cref="LandingArt"/> – one box goes up on the shelf, no other box appears.
    /// </summary>
    public Control? LandingSlot { get; set; }
    public Image? LandingImage { get; set; }
    public Control? LandingArt { get; set; }

    /// <summary>The UI scale (the layer itself is unscaled): line thickness and the copy follow it.</summary>
    public double Scale { get; set; } = 1;

    /// <summary>A transition is playing (or frozen).</summary>
    public bool IsActive => _running || _copy.IsVisible || _line.IsVisible || _off.IsVisible;

    /// <summary>The frame shown last (ms on the timeline) – for tests.</summary>
    public double LastFrame { get; private set; } = -1;

    /// <summary>Was the overexposure frame drawn in the last <see cref="PowerOn"/>? (tests: never under reduced motion)</summary>
    public bool FlashShown { get; private set; }

    /// <summary>
    /// Starts the power-on. <paramref name="source"/> is the box the game was started from (null: no flight, the line
    /// alone); <paramref name="light"/> the system's light colour.
    /// </summary>
    public void PowerOn(CoverArt? source, Color light, EffectiveMotion motion)
    {
        Stop(restoreSource: true);
        _off_mode = false;
        _light = light;
        _motion = motion;
        FlashShown = false;
        _copy.Source = null;
        _snapshot?.Dispose();
        _snapshot = null;
        if (source != null && motion == EffectiveMotion.Full && Snapshot(source) is { } snap)
        {
            _from = snap.Rect;
            _snapshot = snap.Bitmap;
            _copy.Source = _snapshot;
            // the original stays in its row (focus, virtualisation) but is not seen twice – the whole box, spine and all
            _hidden = (Control?)source.FindAncestorOfType<GameTile>() ?? source;
            _hidden.Opacity = 0;
            if (LandingImage != null)
            {
                LandingImage.Source = _snapshot;
                LandingImage.IsVisible = true;
                if (LandingArt != null) LandingArt.IsVisible = false;
            }
        }
        Start();
    }

    /// <summary>The return: the picture collapses to a line, the line to a point; the hidden box comes back at the end.</summary>
    public void PowerOff(Color light, EffectiveMotion motion)
    {
        Stop(restoreSource: false);
        _off_mode = true;
        _light = light;
        _motion = motion;
        if (motion == EffectiveMotion.Off)
        {
            Finish();
            return;
        }
        // while the screen goes dark the page below is already back: a stray click must not hit an invisible control
        // (R2) – the layer takes the pointer until the switch-off is done (HideParts gives it back)
        Background = Brushes.Transparent;
        IsHitTestVisible = true;
        Start();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (IsHitTestVisible) e.Handled = true;
    }

    /// <summary>The launch ended before the start surface was needed (failed start): everything goes, the box comes back.</summary>
    public void Cancel() => Stop(restoreSource: true);

    /// <summary>The start surface stays (the launch overlay takes over); only the transition's own parts go.</summary>
    public void EndPowerOn()
    {
        if (_off_mode) return;
        _timer.Stop();
        _running = false;
        Apply(Duration);
        HideParts();
        ResetTargets(visible: true);
    }

    /// <summary>Shows one frame of the power-on (or power-off) without running the clock (previews, tests).</summary>
    public void Freeze(double ms)
    {
        _timer.Stop();
        _running = false;
        Apply(ms);
    }

    /// <summary>Shows one frame of the switch-off in <paramref name="light"/> (previews).</summary>
    public void FreezeOff(Color light, double ms)
    {
        _timer.Stop();
        _running = false;
        _off_mode = true;
        _light = light;
        _motion = EffectiveMotion.Full;
        Apply(ms);
    }

    /// <summary>Removes every part at once (previews).</summary>
    public void Clear() => Stop(restoreSource: true);

    private double Duration => _off_mode
        ? (_motion == EffectiveMotion.Full ? OffDuration.TotalMilliseconds : ReducedOff)
        : (_motion == EffectiveMotion.Full ? OpenEnd : _motion == EffectiveMotion.Reduced ? ReducedFade : 0);

    private void Start()
    {
        _running = true;
        _clock.Restart();
        Apply(0);
        if (Duration <= 0)
        {
            Finish();
            return;
        }
        _timer.Start();
    }

    private void Tick()
    {
        var ms = _clock.Elapsed.TotalMilliseconds;
        Apply(Math.Min(ms, Duration));
        if (ms >= Duration) Finish();
    }

    private void Finish()
    {
        _timer.Stop();
        _running = false;
        if (_off_mode)
        {
            HideParts();
            RestoreSource();
            LastFrame = Duration;
        }
        else
        {
            Apply(Duration);
            HideParts();
            ResetTargets(visible: true);
        }
    }

    private void Stop(bool restoreSource)
    {
        _timer.Stop();
        _running = false;
        HideParts();
        ResetTargets(visible: true);
        if (restoreSource) RestoreSource();
    }

    private void RestoreSource()
    {
        if (_hidden != null) _hidden.Opacity = 1;
        _hidden = null;
        ResetLanding();
    }

    private void ResetLanding()
    {
        if (LandingImage != null)
        {
            LandingImage.Source = null;
            LandingImage.IsVisible = false;
        }
        if (LandingArt != null) LandingArt.IsVisible = true;
    }

    /// <summary>
    /// The landing rectangle in this layer: the snapshot's aspect contained in the box slot, bottom on the shelf edge,
    /// centred (the slot's position includes UiScale). Null while the start surface is not laid out.
    /// </summary>
    private Rect? Landing()
    {
        if (LandingSlot is not { } slot || slot.Bounds.Width < 4 || _from.Width < 1 || _from.Height < 1) return null;
        if (slot.TranslatePoint(default, this) is not { } tl || slot.TranslatePoint(new Point(slot.Bounds.Width, slot.Bounds.Height), this) is not { } br) return null;
        var box = new Rect(tl, br);
        var aspect = _from.Width / _from.Height;
        var w = Math.Min(box.Width, box.Height * aspect);
        var h = w / aspect;
        return new Rect(box.X + (box.Width - w) / 2, box.Bottom - h, w, h);
    }

    private void HideParts()
    {
        IsHitTestVisible = false;
        Background = null;
        _copy.IsVisible = _line.IsVisible = _edgeTop.IsVisible = _edgeBottom.IsVisible = _flash.IsVisible = _off.IsVisible = _tube.IsVisible = false;
    }

    private void ResetTargets(bool visible)
    {
        if (Scrim != null) Scrim.Opacity = visible ? 1 : 0;
        if (Reveal != null)
        {
            Reveal.Opacity = visible ? 1 : 0;
            Reveal.RenderTransform = null;
        }
    }

    /// <summary>Sets every part for the moment <paramref name="ms"/> of the current timeline.</summary>
    public void Apply(double ms)
    {
        LastFrame = ms;
        var w = Bounds.Width;
        var h = Bounds.Height;
        if (w <= 0 || h <= 0) return;
        var light = new ImmutableSolidColorBrush(_light);
        var thick = Math.Max(2, Math.Round(2 * Scale));
        if (_off_mode)
        {
            ApplyOff(ms, w, h, light, thick);
            return;
        }
        if (_motion != EffectiveMotion.Full)
        {
            // reduced: a plain cross-fade of the start surface (150 ms), no flight, no line, no overexposure
            HideParts();
            var t = _motion == EffectiveMotion.Off ? 1 : Math.Clamp(ms / ReducedFade, 0, 1);
            if (Scrim != null) Scrim.Opacity = t;
            if (Reveal != null)
            {
                Reveal.RenderTransform = null;
                Reveal.Opacity = t;
            }
            return;
        }

        // 1–2: the copy flies to the middle and grows (CubicEaseIn), then fades while the line grows
        if (Scrim != null) Scrim.Opacity = Math.Clamp(ms / FlyEnd, 0, 1);
        if (_copy.Source != null && ms < LineEnd)
        {
            var p = Math.Clamp(ms / FlyEnd, 0, 1);
            var e = p * p * p;
            // the copy lands where the start surface's box will stand (fallback: the middle at 1.4×)
            var target = Landing() ?? new Rect(w / 2 - _from.Width * 1.4 / 2, h / 2 - _from.Height * 1.4 / 2, _from.Width * 1.4, _from.Height * 1.4);
            var r = Lerp(_from, target, e);
            Place(_copy, r);
            _copy.Opacity = ms <= FlyEnd ? 1 : 1 - Math.Clamp((ms - FlyEnd) / (LineEnd - FlyEnd), 0, 1);
            _copy.IsVisible = true;
        }
        else _copy.IsVisible = false;

        // 3: the line in the system's light grows from the middle to the full width
        if (ms >= FlyEnd && ms < LineEnd)
        {
            var p = Math.Clamp((ms - FlyEnd) / (LineEnd - FlyEnd), 0, 1);
            var lw = Math.Max(thick, w * (1 - Math.Pow(1 - p, 3)));
            _line.Background = light;
            Place(_line, new Rect((w - lw) / 2, Math.Round(h / 2 - thick / 2), lw, thick));
            _line.IsVisible = true;
        }
        else _line.IsVisible = false;

        // 4: one frame of overexposure
        var flash = ms >= LineEnd && ms < FlashEnd;
        if (flash)
        {
            Place(_flash, new Rect(0, 0, w, h));
            FlashShown = true;
        }
        _flash.IsVisible = flash;

        // 5: the line opens into the start surface; its two edges carry the light and fade
        if (Reveal != null)
        {
            if (ms < LineEnd)
            {
                Reveal.Opacity = 0;
                Reveal.RenderTransform = null;
            }
            else
            {
                var p = Math.Clamp((ms - LineEnd) / (OpenEnd - LineEnd), 0, 1);
                var e = 1 - Math.Pow(1 - p, 3);
                var open = Math.Max(thick / h, e);
                Reveal.Opacity = 1;
                Reveal.RenderTransformOrigin = RelativePoint.Center;
                Reveal.RenderTransform = open >= 1 ? null : new ScaleTransform(1, open);
                var half = h * open / 2;
                var fade = 1 - p;
                foreach (var (edge, y) in new[] { (_edgeTop, h / 2 - half), (_edgeBottom, h / 2 + half - thick) })
                {
                    edge.Background = light;
                    edge.Opacity = fade;
                    Place(edge, new Rect(0, Math.Round(y), w, thick));
                    edge.IsVisible = fade > 0.01;
                }
            }
        }
    }

    private void ApplyOff(double ms, double w, double h, IBrush light, double thick)
    {
        _copy.IsVisible = _flash.IsVisible = _edgeTop.IsVisible = _edgeBottom.IsVisible = _tube.IsVisible = false;
        if (_motion != EffectiveMotion.Full)
        {
            // reduced: the dark surface fades away (100 ms)
            _line.IsVisible = false;
            _off.Opacity = 1 - Math.Clamp(ms / ReducedOff, 0, 1);
            Place(_off, new Rect(0, 0, w, h));
            _off.IsVisible = _off.Opacity > 0.01;
            return;
        }
        // a closed room-deep backdrop over the whole window until the point has gone (M6 QA: the line was drawn over the
        // restored page and read as a stray underline); then the page fades in (Scene 280, CubicEaseOut)
        Place(_off, new Rect(0, 0, w, h));
        if (ms < OffPoint)
        {
            _off.Opacity = 1;
            _off.IsVisible = true;
        }
        else
        {
            var f = Math.Clamp((ms - OffPoint) / OffFade, 0, 1);
            _off.Opacity = Math.Pow(1 - f, 3);
            _off.IsVisible = _off.Opacity > 0.01;
        }
        if (ms < OffCollapse)
        {
            // the picture (a lit tube) collapses vertically to a line in the light
            var p = Math.Clamp(ms / OffCollapse, 0, 1);
            var e = p * p;
            var oh = Math.Max(thick, h * (1 - e));
            Place(_tube, new Rect(0, (h - oh) / 2, w, oh));
            _tube.IsVisible = true;
            _line.Background = light;
            _line.Opacity = Math.Clamp(p * 1.5, 0, 1);
            Place(_line, new Rect(0, Math.Round(h / 2 - thick / 2), w, thick));
            _line.IsVisible = true;
        }
        else
        {
            // the line shrinks to a point in the middle, on the dark
            _tube.IsVisible = false;
            var p = Math.Clamp((ms - OffCollapse) / (OffPoint - OffCollapse), 0, 1);
            var lw = Math.Max(thick, w * (1 - p) * (1 - p));
            _line.Background = light;
            _line.Opacity = 1;
            Place(_line, new Rect((w - lw) / 2, Math.Round(h / 2 - thick / 2), lw, thick));
            _line.IsVisible = p < 1;
        }
    }
    private static Rect Lerp(Rect a, Rect b, double t) =>
        new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t, a.Width + (b.Width - a.Width) * t, a.Height + (b.Height - a.Height) * t);

    private static void Place(Control c, Rect r)
    {
        SetLeft(c, r.X);
        SetTop(c, r.Y);
        c.Width = Math.Max(0, r.Width);
        c.Height = Math.Max(0, r.Height);
    }

    /// <summary>The box as it looks on screen: its art rectangle in this layer's coordinates and a bitmap of it.</summary>
    private (Rect Rect, RenderTargetBitmap Bitmap)? Snapshot(CoverArt art)
    {
        try
        {
            var local = art.ArtRect;
            if (local.Width < 4 || local.Height < 4 || !art.IsEffectivelyVisible) return null;
            if (art.TranslatePoint(local.TopLeft, this) is not { } tl || art.TranslatePoint(local.BottomRight, this) is not { } br) return null;
            var onScreen = new Rect(tl, br);
            var scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
            var k = Math.Max(0.25, onScreen.Width / local.Width) * scaling;
            var size = new PixelSize(Math.Max(1, (int)Math.Ceiling(art.Bounds.Width * k)), Math.Max(1, (int)Math.Ceiling(art.Bounds.Height * k)));
            var full = new RenderTargetBitmap(size, new Vector(96 * k, 96 * k));
            full.Render(art);
            // only the art rectangle (the cover itself, not the empty slot around a narrow box)
            var crop = new PixelRect((int)Math.Floor(local.X * k), (int)Math.Floor(local.Y * k), (int)Math.Ceiling(local.Width * k), (int)Math.Ceiling(local.Height * k));
            crop = crop.Intersect(new PixelRect(size));
            if (crop.Width <= 0 || crop.Height <= 0)
            {
                full.Dispose();
                return null;
            }
            var bmp = new RenderTargetBitmap(new PixelSize(crop.Width, crop.Height), new Vector(96 * k, 96 * k));
            using (var ctx = bmp.CreateDrawingContext())
                ctx.DrawImage(full, new Rect(crop.X / k, crop.Y / k, crop.Width / k, crop.Height / k), new Rect(0, 0, crop.Width / k, crop.Height / k));
            full.Dispose();
            return (onScreen, bmp);
        }
        catch (Exception ex)
        {
            RetroHub.Core.Logging.Log.Warn("Start transition: the box could not be copied", ex);
            return null;
        }
    }
}
