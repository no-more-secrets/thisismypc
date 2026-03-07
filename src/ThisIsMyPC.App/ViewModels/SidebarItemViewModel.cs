using System.Collections.Frozen;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using ThisIsMyPC.Core.Modules;

namespace ThisIsMyPC.App.ViewModels;

public partial class SidebarItemViewModel : ViewModelBase
{
    private static readonly FrozenDictionary<string, string> IconPaths = new Dictionary<string, string>
    {
        // Shell & Explorer: folder/window icon
        ["shell"] = "M4,4H20A2,2 0 0,1 22,6V18A2,2 0 0,1 20,20H4A2,2 0 0,1 2,18V6A2,2 0 0,1 4,4M4,6V18H20V8H14L12,6H4Z",
        // Startup & Services: rocket/launch icon
        ["startup"] = "M13.13,22.19L11.5,18.36C13.07,17.78 14.54,17 15.9,16.09L13.13,22.19M5.64,12.5L1.81,10.87L7.91,8.1C7,9.46 6.22,10.93 5.64,12.5M19.22,4C19.5,4 19.75,4 19.96,4.05C20.13,5.44 19.94,8.3 16.96,11.29C16.96,11.29 14.46,13.79 12.74,15.5L8.5,11.26C10.21,9.54 12.71,7.04 12.71,7.04C15.7,4.06 18.56,3.87 19.95,4.04C19.75,4 19.5,4 19.22,4M14.54,9.46C13.76,8.68 13.76,7.41 14.54,6.63C15.32,5.85 16.59,5.85 17.37,6.63C18.14,7.41 18.15,8.68 17.37,9.46C16.59,10.24 15.32,10.24 14.54,9.46M8.88,16.53L7.47,15.12L8.88,16.53M6.24,22L8.4,16.88L11.65,20.13L6.24,22Z",
        // Power Plans: lightning bolt icon
        ["power"] = "M11,21H7V13H3L12,3L21,13H17V21H13V17H11V21Z",
    }.ToFrozenDictionary();

    private const string DefaultIconPath = "M12,2A10,10 0 0,0 2,12A10,10 0 0,0 12,22A10,10 0 0,0 22,12A10,10 0 0,0 12,2Z";

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
