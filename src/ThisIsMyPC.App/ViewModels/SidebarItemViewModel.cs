using System.Collections.Frozen;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using ThisIsMyPC.Core.Modules;

namespace ThisIsMyPC.App.ViewModels;

public partial class SidebarItemViewModel : ViewModelBase
{
    // Fluent UI System Icons (MIT, Microsoft) — 24x24 filled variants
    private static readonly FrozenDictionary<string, string> IconPaths = new Dictionary<string, string>
    {
        // Shell & Explorer: Fluent Folder 24 Filled
        ["shell"] = "M2 8V6.25C2 4.45507 3.45507 3 5.25 3H8.12868C8.72542 3 9.29771 3.23705 9.71967 3.65901L11.25 5.18934L8.65901 7.78033C8.51836 7.92098 8.32759 8 8.12868 8H2ZM2 9.5V17.75C2 19.5449 3.45507 21 5.25 21H18.75C20.5449 21 22 19.5449 22 17.75V8.75C22 6.95507 20.5449 5.5 18.75 5.5H13.0607L9.71967 8.84099C9.29771 9.26295 8.72542 9.5 8.12868 9.5H2Z",
        // Startup & Services: Fluent Play 24 Filled (consider speedometer/gauge icon to match Task Manager convention)
        ["startup"] = "M5 5.27368C5 3.56682 6.82609 2.48151 8.32538 3.2973L20.687 10.0235C22.2531 10.8756 22.2531 13.124 20.687 13.9762L8.32538 20.7024C6.82609 21.5181 5 20.4328 5 18.726V5.27368Z",
        // Power Plans: Fluent Flash 24 Filled
        ["power"] = "M7.42505 2.83052C7.60245 2.33254 8.07392 2 8.60256 2H15.0562C15.9094 2 16.5118 2.83587 16.242 3.64528L14.7905 8H18.7492C19.8534 8 20.4153 9.32682 19.647 10.1198L8.586 21.536C7.53226 22.6236 5.71405 21.6422 6.04495 20.1645L7.31418 14.4964L5.74573 14.4904C4.53898 14.4858 3.69895 13.2899 4.10392 12.1532L7.42505 2.83052Z",
        // Windows Annoyances: prohibition sign (ring with diagonal slash — suppressed nags)
        ["annoyances"] = "M12 2C17.5228 2 22 6.47715 22 12C22 17.5228 17.5228 22 12 22C6.47715 22 2 17.5228 2 12C2 6.47715 6.47715 2 12 2ZM12 4C10.1786 4 8.5021 4.60879 7.15838 5.63402L18.366 16.8416C19.3912 15.4979 20 13.8214 20 12C20 7.58172 16.4183 4 12 4ZM5.63402 7.15838C4.60879 8.5021 4 10.1786 4 12C4 16.4183 7.58172 20 12 20C13.8214 20 15.4979 19.3912 16.8416 18.366L5.63402 7.15838Z",
        // Context Menus: Fluent TextBulletListSquare 24 Filled
        ["context-menu"] = "M3 6.25C3 4.45507 4.45507 3 6.25 3H17.75C19.5449 3 21 4.45507 21 6.25V17.75C21 19.5449 19.5449 21 17.75 21H6.25C4.45507 21 3 19.5449 3 17.75V6.25ZM8.5 8C8.5 8.55228 8.05228 9 7.5 9C6.94772 9 6.5 8.55228 6.5 8C6.5 7.44772 6.94772 7 7.5 7C8.05228 7 8.5 7.44772 8.5 8ZM10 7.25C9.58579 7.25 9.25 7.58579 9.25 8C9.25 8.41421 9.58579 8.75 10 8.75H17C17.4142 8.75 17.75 8.41421 17.75 8C17.75 7.58579 17.4142 7.25 17 7.25H10ZM10 11.25C9.58579 11.25 9.25 11.5858 9.25 12C9.25 12.4142 9.58579 12.75 10 12.75H17C17.4142 12.75 17.75 12.4142 17.75 12C17.75 11.5858 17.4142 11.25 17 11.25H10ZM10 15.25C9.58579 15.25 9.25 15.5858 9.25 16C9.25 16.4142 9.58579 16.75 10 16.75H17C17.4142 16.75 17.75 16.4142 17.75 16C17.75 15.5858 17.4142 15.25 17 15.25H10ZM8.5 12C8.5 12.5523 8.05228 13 7.5 13C6.94772 13 6.5 12.5523 6.5 12C6.5 11.4477 6.94772 11 7.5 11C8.05228 11 8.5 11.4477 8.5 12ZM7.5 17C8.05228 17 8.5 16.5523 8.5 16C8.5 15.4477 8.05228 15 7.5 15C6.94772 15 6.5 15.4477 6.5 16C6.5 16.5523 6.94772 17 7.5 17Z",
    }.ToFrozenDictionary();

    // Fluent Circle 24 Regular (outlined ring — clearly signals "no icon mapped")
    private const string DefaultIconPath = "M12 3.5C7.30558 3.5 3.5 7.30558 3.5 12C3.5 16.6944 7.30558 20.5 12 20.5C16.6944 20.5 20.5 16.6944 20.5 12C20.5 7.30558 16.6944 3.5 12 3.5ZM2 12C2 6.47715 6.47715 2 12 2C17.5228 2 22 6.47715 22 12C22 17.5228 17.5228 22 12 22C6.47715 22 2 17.5228 2 12Z";

    public required string Name { get; init; }
    public required string Icon { get; init; }
    public required string? UnavailableReason { get; init; }
    public required string? RemediationHint { get; init; }
    public required bool IsAvailable { get; init; }
    public required IModule Module { get; init; }

    [ObservableProperty]
    private bool _isActive;

    private Geometry? _cachedGeometry;

    public Geometry IconGeometry => _cachedGeometry ??= Geometry.Parse(
        IconPaths.GetValueOrDefault(Icon, DefaultIconPath));

    public string TooltipText
    {
        get
        {
            if (IsAvailable)
                return Name;

            var parts = new[] { UnavailableReason, RemediationHint }
                .Where(s => !string.IsNullOrEmpty(s));
            var combined = string.Join("\n", parts);
            return combined.Length > 0 ? combined : "Unavailable";
        }
    }
}
