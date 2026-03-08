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
