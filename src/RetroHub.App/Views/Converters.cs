using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace RetroHub.App.Views;

/// <summary>Resource key ("Icon.Home") → Geometry from the application resources.</summary>
public sealed class IconKeyConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string key || Application.Current is not { } app) return null;
        return app.TryGetResource(key, app.ActualThemeVariant, out var res) && res is Geometry g ? g : null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
