using System.Collections.Frozen;
using ThisIsMyPC.App.Icons;
using CommunityToolkit.Mvvm.ComponentModel;
using ThisIsMyPC.Core.Modules;

namespace ThisIsMyPC.App.ViewModels;

public partial class SidebarItemViewModel : ViewModelBase
{
    private static readonly FrozenDictionary<string, FluentSymbol> Icons = new Dictionary<string, FluentSymbol>
    {
        ["shell"] = FluentSymbol.Folder,
        ["startup"] = FluentSymbol.Play,
        ["power"] = FluentSymbol.Flash,
        ["annoyances"] = FluentSymbol.Prohibited,
        ["context-menu"] = FluentSymbol.TextBulletListSquare,
        ["environment"] = FluentSymbol.Code,
        ["windows-update"] = FluentSymbol.ArrowSync,
        ["privacy"] = FluentSymbol.HandRight,
        ["security"] = FluentSymbol.Shield,
        ["display"] = FluentSymbol.Desktop,
        ["software"] = FluentSymbol.ArrowDownload,
        ["system-control"] = FluentSymbol.Laptop,
        ["lighting"] = FluentSymbol.Lightbulb,
        ["cooling"] = FluentSymbol.Temperature,
        ["monitoring"] = FluentSymbol.Gauge,
    }.ToFrozenDictionary();

    public required string Name { get; init; }
    public required string Icon { get; init; }
    public required string? UnavailableReason { get; init; }
    public required string? RemediationHint { get; init; }
    public required bool IsAvailable { get; init; }
    public required IModule Module { get; init; }

    [ObservableProperty]
    private bool _isActive;

    public FluentSymbol IconSymbol => Icons.GetValueOrDefault(Icon, FluentSymbol.Box);

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
