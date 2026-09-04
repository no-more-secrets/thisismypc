using System.Globalization;
using Avalonia.Data.Converters;

namespace ThisIsMyPC.App.ViewModels;

public sealed class SidebarWidthConverter : IValueConverter
{
    public static readonly SidebarWidthConverter Instance = new();

    public const double CollapsedWidth = 48.0;
    public const double ExpandedWidth = 200.0;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is true ? CollapsedWidth : ExpandedWidth;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
