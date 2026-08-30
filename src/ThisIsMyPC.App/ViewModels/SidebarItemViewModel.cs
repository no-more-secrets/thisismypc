using System.Collections.Frozen;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using ThisIsMyPC.Core.Modules;

namespace ThisIsMyPC.App.ViewModels;

public partial class SidebarItemViewModel : ViewModelBase
{
    // Mostly Fluent UI System Icons (MIT, Microsoft), 24x24 filled variants;
    // environment/windows-update/privacy/annoyances are hand-drawn on the same grid.
    private static readonly FrozenDictionary<string, string> IconPaths = new Dictionary<string, string>
    {
        // Shell & Explorer: Fluent Folder 24 Filled
        ["shell"] = "M2 8V6.25C2 4.45507 3.45507 3 5.25 3H8.12868C8.72542 3 9.29771 3.23705 9.71967 3.65901L11.25 5.18934L8.65901 7.78033C8.51836 7.92098 8.32759 8 8.12868 8H2ZM2 9.5V17.75C2 19.5449 3.45507 21 5.25 21H18.75C20.5449 21 22 19.5449 22 17.75V8.75C22 6.95507 20.5449 5.5 18.75 5.5H13.0607L9.71967 8.84099C9.29771 9.26295 8.72542 9.5 8.12868 9.5H2Z",
        // Startup & Services: Fluent Play 24 Filled (consider speedometer/gauge icon to match Task Manager convention)
        ["startup"] = "M5 5.27368C5 3.56682 6.82609 2.48151 8.32538 3.2973L20.687 10.0235C22.2531 10.8756 22.2531 13.124 20.687 13.9762L8.32538 20.7024C6.82609 21.5181 5 20.4328 5 18.726V5.27368Z",
        // Power Plans: Fluent Flash 24 Filled
        ["power"] = "M7.42505 2.83052C7.60245 2.33254 8.07392 2 8.60256 2H15.0562C15.9094 2 16.5118 2.83587 16.242 3.64528L14.7905 8H18.7492C19.8534 8 20.4153 9.32682 19.647 10.1198L8.586 21.536C7.53226 22.6236 5.71405 21.6422 6.04495 20.1645L7.31418 14.4964L5.74573 14.4904C4.53898 14.4858 3.69895 13.2899 4.10392 12.1532L7.42505 2.83052Z",
        // Windows Annoyances: prohibition sign (ring with diagonal slash; suppressed nags)
        ["annoyances"] = "M12 2C17.5228 2 22 6.47715 22 12C22 17.5228 17.5228 22 12 22C6.47715 22 2 17.5228 2 12C2 6.47715 6.47715 2 12 2ZM12 4C10.1786 4 8.5021 4.60879 7.15838 5.63402L18.366 16.8416C19.3912 15.4979 20 13.8214 20 12C20 7.58172 16.4183 4 12 4ZM5.63402 7.15838C4.60879 8.5021 4 10.1786 4 12C4 16.4183 7.58172 20 12 20C13.8214 20 15.4979 19.3912 16.8416 18.366L5.63402 7.15838Z",
        // Context Menus: Fluent TextBulletListSquare 24 Filled
        ["context-menu"] = "M3 6.25C3 4.45507 4.45507 3 6.25 3H17.75C19.5449 3 21 4.45507 21 6.25V17.75C21 19.5449 19.5449 21 17.75 21H6.25C4.45507 21 3 19.5449 3 17.75V6.25ZM8.5 8C8.5 8.55228 8.05228 9 7.5 9C6.94772 9 6.5 8.55228 6.5 8C6.5 7.44772 6.94772 7 7.5 7C8.05228 7 8.5 7.44772 8.5 8ZM10 7.25C9.58579 7.25 9.25 7.58579 9.25 8C9.25 8.41421 9.58579 8.75 10 8.75H17C17.4142 8.75 17.75 8.41421 17.75 8C17.75 7.58579 17.4142 7.25 17 7.25H10ZM10 11.25C9.58579 11.25 9.25 11.5858 9.25 12C9.25 12.4142 9.58579 12.75 10 12.75H17C17.4142 12.75 17.75 12.4142 17.75 12C17.75 11.5858 17.4142 11.25 17 11.25H10ZM10 15.25C9.58579 15.25 9.25 15.5858 9.25 16C9.25 16.4142 9.58579 16.75 10 16.75H17C17.4142 16.75 17.75 16.4142 17.75 16C17.75 15.5858 17.4142 15.25 17 15.25H10ZM8.5 12C8.5 12.5523 8.05228 13 7.5 13C6.94772 13 6.5 12.5523 6.5 12C6.5 11.4477 6.94772 11 7.5 11C8.05228 11 8.5 11.4477 8.5 12ZM7.5 17C8.05228 17 8.5 16.5523 8.5 16C8.5 15.4477 8.05228 15 7.5 15C6.94772 15 6.5 15.4477 6.5 16C6.5 16.5523 6.94772 17 7.5 17Z",
        // Environment: code chevrons (variables live in shells and scripts)
        ["environment"] = "M9.4 16.6L4.8 12L9.4 7.4C9.79 7.01 9.79 6.39 9.4 6C9.01 5.61 8.39 5.61 8 6L2.7 11.3C2.31 11.69 2.31 12.31 2.7 12.7L8 18C8.39 18.39 9.01 18.39 9.4 18C9.79 17.61 9.79 16.99 9.4 16.6ZM14.6 16.6L19.2 12L14.6 7.4C14.21 7.01 14.21 6.39 14.6 6C14.99 5.61 15.61 5.61 16 6L21.3 11.3C21.69 11.69 21.69 12.31 21.3 12.7L16 18C15.61 18.39 14.99 18.39 14.6 18C14.21 17.61 14.21 16.99 14.6 16.6Z",
        // Windows Update: circular refresh arrow
        ["windows-update"] = "M12 4C7.58 4 4 7.58 4 12C4 16.42 7.58 20 12 20C16.42 20 20 16.42 20 12C20 11.45 20.45 11 21 11C21.55 11 22 11.45 22 12C22 17.52 17.52 22 12 22C6.48 22 2 17.52 2 12C2 6.48 6.48 2 12 2C14.76 2 17.26 3.12 19.07 4.93L20.29 3.71C20.92 3.08 22 3.53 22 4.42V8.3C22 8.69 21.69 9 21.3 9H17.42C16.53 9 16.08 7.92 16.71 7.29L17.65 6.35C16.2 4.9 14.21 4 12 4Z",
        // Privacy & Telemetry: shield
        ["privacy"] = "M11.6 2.15L4.6 4.78C4.24 4.91 4 5.26 4 5.64V11C4 16.34 7.25 21.29 11.7 22.44C11.9 22.49 12.1 22.49 12.3 22.44C16.75 21.29 20 16.34 20 11V5.64C20 5.26 19.76 4.91 19.4 4.78L12.4 2.15C12.14 2.05 11.86 2.05 11.6 2.15Z",
        // Software: Fluent ArrowDownload 24 Filled (package installs)
        ["software"] = "M12 2C12.5523 2 13 2.44772 13 3V13.586L16.2929 10.2929C16.6834 9.90237 17.3166 9.90237 17.7071 10.2929C18.0976 10.6834 18.0976 11.3166 17.7071 11.7071L12.7071 16.7071C12.3166 17.0976 11.6834 17.0976 11.2929 16.7071L6.29289 11.7071C5.90237 11.3166 5.90237 10.6834 6.29289 10.2929C6.68342 9.90237 7.31658 9.90237 7.70711 10.2929L11 13.586V3C11 2.44772 11.4477 2 12 2ZM4 20C4 19.4477 4.44772 19 5 19H19C19.5523 19 20 19.4477 20 20C20 20.5523 19.5523 21 19 21H5C4.44772 21 4 20.5523 4 20Z",
    }.ToFrozenDictionary();

    // Fluent Circle 24 Regular (outlined ring; clearly signals "no icon mapped")
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
