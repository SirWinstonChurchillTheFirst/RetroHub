using System.Collections;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;

namespace RetroHub.App.Controls;

/// <summary>
/// A shelf board with boxes ("Brett", DESIGN 7.1): the boxes stand on a 24 px board, a paper label
/// (<see cref="Header"/>) hangs at the left of the board front instead of a heading. Focus visuals come from one
/// <see cref="FocusLayer"/> in the scroll content. Small item counts (≤ ~16), so no virtualization is needed.
/// </summary>
public class Shelf : TemplatedControl
{
    public static readonly StyledProperty<string?> HeaderProperty = AvaloniaProperty.Register<Shelf, string?>(nameof(Header));
    public static readonly StyledProperty<string?> SubHeaderProperty = AvaloniaProperty.Register<Shelf, string?>(nameof(SubHeader));
    public static readonly StyledProperty<IEnumerable?> ItemsSourceProperty = AvaloniaProperty.Register<Shelf, IEnumerable?>(nameof(ItemsSource));
    public static readonly StyledProperty<IDataTemplate?> ItemTemplateProperty = AvaloniaProperty.Register<Shelf, IDataTemplate?>(nameof(ItemTemplate));
    public static readonly StyledProperty<string?> ActionLabelProperty = AvaloniaProperty.Register<Shelf, string?>(nameof(ActionLabel));
    public static readonly StyledProperty<ICommand?> ActionCommandProperty = AvaloniaProperty.Register<Shelf, ICommand?>(nameof(ActionCommand));
    public static readonly StyledProperty<double> ItemSpacingProperty = AvaloniaProperty.Register<Shelf, double>(nameof(ItemSpacing), 24);
    /// <summary>Inset of the boxes inside the board (the board itself runs over the full width).</summary>
    public static readonly StyledProperty<Thickness> ItemsMarginProperty = AvaloniaProperty.Register<Shelf, Thickness>(nameof(ItemsMargin));
    public static readonly StyledProperty<Thickness> LabelMarginProperty = AvaloniaProperty.Register<Shelf, Thickness>(nameof(LabelMargin));

    public string? Header { get => GetValue(HeaderProperty); set => SetValue(HeaderProperty, value); }
    public string? SubHeader { get => GetValue(SubHeaderProperty); set => SetValue(SubHeaderProperty, value); }
    public IEnumerable? ItemsSource { get => GetValue(ItemsSourceProperty); set => SetValue(ItemsSourceProperty, value); }
    public IDataTemplate? ItemTemplate { get => GetValue(ItemTemplateProperty); set => SetValue(ItemTemplateProperty, value); }
    public string? ActionLabel { get => GetValue(ActionLabelProperty); set => SetValue(ActionLabelProperty, value); }
    public ICommand? ActionCommand { get => GetValue(ActionCommandProperty); set => SetValue(ActionCommandProperty, value); }
    public double ItemSpacing { get => GetValue(ItemSpacingProperty); set => SetValue(ItemSpacingProperty, value); }
    public Thickness ItemsMargin { get => GetValue(ItemsMarginProperty); set => SetValue(ItemsMarginProperty, value); }
    public Thickness LabelMargin { get => GetValue(LabelMarginProperty); set => SetValue(LabelMarginProperty, value); }

    /// <summary>The focus visuals of this shelf (null until the template is applied).</summary>
    public FocusLayer? FocusVisuals { get; private set; }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        var host = e.NameScope.Find<Grid>("PART_Host");
        if (host == null) return;
        FocusVisuals = new FocusLayer(host, host) { BoardFront = ShelfBoard.StageHeight };
        Grid.SetRowSpan(FocusVisuals.Back, 3);
        Grid.SetRowSpan(FocusVisuals.Front, 3);
        host.Children.Insert(0, FocusVisuals.Back);
        host.Children.Add(FocusVisuals.Front);
    }
}
