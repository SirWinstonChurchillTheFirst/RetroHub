using System.Collections;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;

namespace RetroHub.App.Controls;

/// <summary>
/// A horizontal dashboard row ("Zuletzt gespielt", "Favoriten", …): header with an optional "show all" action and a
/// horizontally scrolling strip of items. Small item counts (≤ ~16), so no virtualization is needed here.
/// </summary>
public class Shelf : TemplatedControl
{
    public static readonly StyledProperty<string?> HeaderProperty = AvaloniaProperty.Register<Shelf, string?>(nameof(Header));
    public static readonly StyledProperty<string?> SubHeaderProperty = AvaloniaProperty.Register<Shelf, string?>(nameof(SubHeader));
    public static readonly StyledProperty<IEnumerable?> ItemsSourceProperty = AvaloniaProperty.Register<Shelf, IEnumerable?>(nameof(ItemsSource));
    public static readonly StyledProperty<IDataTemplate?> ItemTemplateProperty = AvaloniaProperty.Register<Shelf, IDataTemplate?>(nameof(ItemTemplate));
    public static readonly StyledProperty<string?> ActionLabelProperty = AvaloniaProperty.Register<Shelf, string?>(nameof(ActionLabel));
    public static readonly StyledProperty<ICommand?> ActionCommandProperty = AvaloniaProperty.Register<Shelf, ICommand?>(nameof(ActionCommand));
    public static readonly StyledProperty<double> ItemSpacingProperty = AvaloniaProperty.Register<Shelf, double>(nameof(ItemSpacing), 22);

    public string? Header { get => GetValue(HeaderProperty); set => SetValue(HeaderProperty, value); }
    public string? SubHeader { get => GetValue(SubHeaderProperty); set => SetValue(SubHeaderProperty, value); }
    public IEnumerable? ItemsSource { get => GetValue(ItemsSourceProperty); set => SetValue(ItemsSourceProperty, value); }
    public IDataTemplate? ItemTemplate { get => GetValue(ItemTemplateProperty); set => SetValue(ItemTemplateProperty, value); }
    public string? ActionLabel { get => GetValue(ActionLabelProperty); set => SetValue(ActionLabelProperty, value); }
    public ICommand? ActionCommand { get => GetValue(ActionCommandProperty); set => SetValue(ActionCommandProperty, value); }
    public double ItemSpacing { get => GetValue(ItemSpacingProperty); set => SetValue(ItemSpacingProperty, value); }
}
