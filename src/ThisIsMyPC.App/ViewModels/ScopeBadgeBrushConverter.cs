using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace ThisIsMyPC.App.ViewModels;

/// <summary>
/// Converts a brush resource key (e.g., "ScopeBadgeFileBrush") to the actual SolidColorBrush
/// from the application's resource dictionary.
/// </summary>
public sealed class ScopeBadgeBrushConverter : IValueConverter
{
    public static readonly ScopeBadgeBrushConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string brushKey && Application.Current is not null)
        {
            if (Application.Current.TryGetResource(brushKey, Application.Current.ActualThemeVariant, out var resource)
                && resource is ISolidColorBrush brush)
            {
                return brush;
            }
        }

        // Fallback: neutral gray
        return new SolidColorBrush(Color.Parse("#3a3a50"));
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
