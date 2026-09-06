using System.Globalization;
using Avalonia.Data.Converters;
using ThisIsMyPC.App.Icons;

namespace ThisIsMyPC.App.ViewModels;

/// <summary>Maps catalog categories to Fluent icons. Unknown categories use a package box.</summary>
public sealed class CategoryIconConverter : IValueConverter
{
    public static readonly CategoryIconConverter Instance = new();
    private static readonly Dictionary<string, FluentSymbol> Symbols = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Browsers"] = FluentSymbol.Globe,
        ["Communications"] = FluentSymbol.Chat,
        ["Development"] = FluentSymbol.Code,
        ["Document"] = FluentSymbol.Document,
        ["Games"] = FluentSymbol.Games,
        ["Microsoft Tools"] = FluentSymbol.Window,
        ["Multimedia Tools"] = FluentSymbol.VideoClip,
        ["Pro Tools"] = FluentSymbol.Briefcase,
        ["Selfhosted Tools"] = FluentSymbol.Server,
        ["Utilities"] = FluentSymbol.Wrench,
    };

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Symbols.GetValueOrDefault(value as string ?? string.Empty, FluentSymbol.Box);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
