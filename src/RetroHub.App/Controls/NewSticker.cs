using System.Globalization;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using Avalonia.VisualTree;
using RetroHub.App.Theming;
using RetroHub.App.ViewModels;

namespace RetroHub.App.Controls;

/// <summary>
/// The sticker "Neu" (DESIGN 7.2.1): a Cyan label with ink text, stuck over the top left corner of the box
/// (−4/−4 from the cover's <see cref="CoverArt.ArtRect"/>, so it sits on the real image, not on the slot). Label 13
/// is the one allowed exception below 15 px (sticker print, not a reading line). On the first keyboard/controller focus
/// it is "unpacked": 250 ms 12 px up-left while fading out; the game then counts as seen for this session.
/// Lives inside the tile's cover area, so it lifts with the box; one FormattedText for all stickers.
/// </summary>
public sealed class NewSticker : Control
{
    public static readonly StyledProperty<bool> IsNewProperty = AvaloniaProperty.Register<NewSticker, bool>(nameof(IsNew));

    private static FormattedText? _text;
    private static readonly IImmutableSolidColorBrush Ink = new ImmutableSolidColorBrush(SystemPalette.Ink);
    private CoverArt? _art;
    private GameTile? _tile;
    private bool _unpacking;

    static NewSticker()
    {
        AffectsRender<NewSticker>(IsNewProperty);
        IsHitTestVisibleProperty.OverrideDefaultValue<NewSticker>(false);
        ClipToBoundsProperty.OverrideDefaultValue<NewSticker>(false);
    }

    public bool IsNew { get => GetValue(IsNewProperty); set => SetValue(IsNewProperty, value); }

    private static FormattedText Text => _text ??= new FormattedText("Neu", CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
        FontRoles.LabelFace, 13, Ink);

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _art = (Parent as Panel)?.Children.OfType<CoverArt>().FirstOrDefault();
        if (_art != null) _art.ArtRectChanged += OnArtChanged;
        _tile = this.FindAncestorOfType<GameTile>();
        if (_tile != null) _tile.GotFocus += OnTileFocused;
        ThemeManager.AccentChanged += InvalidateVisual;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (_art != null) _art.ArtRectChanged -= OnArtChanged;
        if (_tile != null) _tile.GotFocus -= OnTileFocused;
        ThemeManager.AccentChanged -= InvalidateVisual;
        _art = null;
        _tile = null;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsNewProperty || change.Property == DataContextProperty)
        {
            // a recycled tile shows another game: no leftover of an unpacking
            _unpacking = false;
            Transitions = null;
            Opacity = 1;
            RenderTransform = null;
        }
    }

    private void OnArtChanged(object? sender, EventArgs e) => InvalidateVisual();

    private void OnTileFocused(object? sender, Avalonia.Input.FocusChangedEventArgs e)
    {
        if (!IsNew || _unpacking || _tile is not { } tile || tile.DataContext is not GameItemViewModel game) return;
        // only a keyboard/controller focus unpacks (the class is set right after GotFocus)
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (!tile.Classes.Contains(":focus-visible") || tile.DataContext != game || !IsNew) return;
            Unpack(game);
        }, Avalonia.Threading.DispatcherPriority.Loaded);
    }

    private void Unpack(GameItemViewModel game)
    {
        _unpacking = true;
        if (Motion.Current == EffectiveMotion.Full)
        {
            Transitions =
            [
                new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(250), Easing = new CubicEaseIn() },
                new TransformOperationsTransition { Property = RenderTransformProperty, Duration = TimeSpan.FromMilliseconds(250), Easing = new CubicEaseOut() },
            ];
            RenderTransform = TransformOperations.Parse("translate(0px, 0px)");
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                RenderTransform = TransformOperations.Parse("translate(-12px, -12px)");
                Opacity = 0;
            }, Avalonia.Threading.DispatcherPriority.Render);
            Avalonia.Threading.DispatcherTimer.RunOnce(() => game.MarkUnpacked(), TimeSpan.FromMilliseconds(260));
        }
        else game.MarkUnpacked();
    }

    public override void Render(DrawingContext ctx)
    {
        if (!IsNew || _art == null) return;
        var art = _art.ArtRect;
        if (art.Width < 60) return;
        var t = Text;
        // relative to this control: the sticker overlaps the box's top left corner by 4 px
        var origin = _art.TranslatePoint(new Point(art.X - 4, art.Y - 4), this) ?? new Point(art.X - 4, art.Y - 4);
        var rect = new Rect(origin.X, origin.Y, Math.Ceiling(t.Width) + 12, Math.Ceiling(t.Height) + 4);
        ctx.DrawRectangle(new ImmutableSolidColorBrush(ThemeManager.CurrentAccent), null, new RoundedRect(rect, 2));
        ctx.DrawText(t, new Point(rect.X + 6, rect.Y + 2));
    }
}
