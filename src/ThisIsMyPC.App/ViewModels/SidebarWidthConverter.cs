using System.Globalization;
using Avalonia.Data.Converters;

namespace ThisIsMyPC.App.ViewModels;

public sealed class SidebarWidthConverter : IValueConverter
{
    public static readonly SidebarWidthConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is true ? 48.0 : 200.0;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
