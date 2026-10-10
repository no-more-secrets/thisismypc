using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using ThisIsMyPC.Core.Hardware;
using ThisIsMyPC.Core.Packages;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Modules.Software.Actions;
using ThisIsMyPC.Modules.Software.Models;
using ThisIsMyPC.Modules.Software.Services;

namespace ThisIsMyPC.App.ViewModels;

public partial class SetLoaderViewModel
{
    private readonly IHardwareDetectionService? _packHardware;
    private readonly IAppxPackageService? _packPackages;
    private readonly IPendingActionsService? _packActions;
    private readonly Func<string, Core.Modules.ModuleAvailability?> _packModuleAvailability;
    private readonly HashSet<string> _excludedPackModules = new(StringComparer.Ordinal);
    private int _packGeneration;
    private bool _packDisposed;

    public IReadOnlyList<PackRemovalViewModel> AppRemovals { get; private set; } = [];
    public Task PackInspection { get; private set; } = Task.CompletedTask;
    public bool CanStagePack { get; private set; } = true;
    public string PackNotice { get; private set; } = "";
    public bool HasPackNotice => PackNotice.Length > 0;

    /// <summary>Carry module choices from setup into the existing per-entry review.</summary>
    public void ExcludeModules(IEnumerable<string> moduleNames)
    {
        _excludedPackModules.Clear();
        _excludedPackModules.UnionWith(moduleNames);
        foreach (var row in PreviewGroups.SelectMany(g => g.Entries))
            if (_excludedPackModules.Contains(row.Entry.ModuleId)) row.IsIncluded = false;
        if (_excludedPackModules.Contains("Software"))
            foreach (var row in AppRemovals) row.IsIncluded = false;
        RecountIncluded();
    }

    private async Task InspectPackAsync(bool preserveChoices = false)
    {
        var generation = ++_packGeneration;
        var definition = SelectedSet?.Definition;
        var previousChoices = AppRemovals.ToDictionary(r => r.Entry?.Id ?? r.Name, r => r.IsIncluded, StringComparer.OrdinalIgnoreCase);
        AppRemovals = [];
        if (!preserveChoices) _excludedPackModules.Clear();
        var needsCheck = definition is { DesktopOnly: true } || definition?.AppRemovals.Count > 0;
        CanStagePack = !needsCheck;
        PackNotice = needsCheck ? "Checking this PC…" : "";
        NotifyPackState();
        if (!needsCheck || definition is null) return;
        try
        {
            if (definition.DesktopOnly)
            {
                var snapshot = _packHardware is null ? null : await _packHardware.GetSnapshotAsync().ConfigureAwait(true);
                if (generation != _packGeneration) return;
                var factor = FormFactorClassifier.Classify(snapshot?.Facts.FormFactor).FormFactor;
                if (factor != MachineFormFactor.Desktop)
                {
                    PackNotice = factor == MachineFormFactor.Laptop
                        ? "This pack supports desktops only. Other presets and modules remain available."
                        : "Desktop type could not be verified. This pack cannot stage changes.";
                    return;
                }
            }
            if (definition.AppRemovals.Count > 0)
            {
                if (_packPackages is null || _packActions is null || _packModuleAvailability("Software")?.IsAvailable != true)
                {
                    PackNotice = "App removal review is unavailable. This pack cannot stage changes.";
                    return;
                }
                var result = await _packPackages.EnumeratePackagesAsync().ConfigureAwait(true);
                if (generation != _packGeneration) return;
                if (!result.IsSuccess || result.Value is null)
                {
                    PackNotice = "Installed apps could not be read. Reopen the pack to retry.";
                    return;
                }
                AppRemovals = definition.AppRemovals.Select(id =>
                {
                    var entry = WindowsAppsCatalog.Entries.FirstOrDefault(e => e.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
                    var installed = entry is not null && result.Value.Any(p => !p.IsFramework &&
                        (p.PackageFullName.StartsWith(entry.PackageId + "_", StringComparison.OrdinalIgnoreCase) ||
                         p.PackageFamilyName.StartsWith(entry.PackageId + "_", StringComparison.OrdinalIgnoreCase)));
                    var row = new PackRemovalViewModel(id, entry, installed, _packActions, RecountIncluded);
                    if (preserveChoices) row.IsIncluded = row.CanInclude && previousChoices.GetValueOrDefault(entry?.Id ?? id);
                    return row;
                }).ToArray();
                if (_excludedPackModules.Contains("Software"))
                    foreach (var row in AppRemovals) row.IsIncluded = false;
            }
            CanStagePack = true;
            PackNotice = definition.AppRemovals.Count > 0
                ? "App removals affect all users and prevent installation for new users. No undo: reinstall separately. Save app data first."
                : "Desktop verified. Review each change before staging.";
        }
        catch (Exception ex)
        {
            if (generation != _packGeneration) return;
            NLog.LogManager.GetCurrentClassLogger().Warn(ex, "Desktop pack inspection failed");
            PackNotice = "This PC could not be checked. Reopen the pack to retry.";
        }
        finally
        {
            if (generation == _packGeneration)
            {
                RecountIncluded();
                FilterPreview();
                NotifyPackState();
            }
        }
    }

    private int StagePackRemovals()
    {
        var count = 0;
        foreach (var row in AppRemovals)
        {
            if (!row.IsIncluded || !row.CanInclude || row.Entry is null) continue;
            _packActions!.Stage(SoftwareActionFactory.CreateAppxRemove(row.Entry));
            row.IsIncluded = false;
            count++;
        }
        return count;
    }

    private void OnPackActionsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() => OnPackActionsChanged(sender, e));
            return;
        }
        if (_packDisposed) return;
        foreach (var row in AppRemovals) row.Refresh();
        RecountIncluded();
        if (e.PropertyName == nameof(IPendingActionsService.IsApplying) && _packActions?.IsApplying == false &&
            SelectedSet?.Definition.AppRemovals.Count > 0)
            PackInspection = InspectPackAsync(preserveChoices: true);
    }

    private void NotifyPackState()
    {
        OnPropertyChanged(nameof(CanStagePack));
        OnPropertyChanged(nameof(HasIncludedEntries));
        OnPropertyChanged(nameof(PackNotice));
        OnPropertyChanged(nameof(HasPackNotice));
        OnPropertyChanged(nameof(AppRemovals));
    }
}

/// <summary>A catalog-backed one-way removal with a separate, explicit selection.</summary>
public sealed partial class PackRemovalViewModel : ViewModelBase
{
    private readonly IPendingActionsService _actions;
    private readonly Action _changed;
    private readonly bool _installed;
    private readonly string _id;
    public WindowsAppEntry? Entry { get; }
    public string Name => Entry?.Name ?? _id;
    public string Description => Entry?.Description ?? "Unknown app catalog entry. Removal is unavailable.";
    public bool CanInclude => Entry is not null && _installed && !_actions.IsApplying &&
        !_actions.IsStaged(SoftwareActionFactory.AppxRemovePrefix + _id) &&
        !_actions.IsStaged(SoftwareActionFactory.AppxReinstallPrefix + _id);
    public string State => Entry is null ? "Unavailable" : !_installed ? "Not installed for this user" :
        _actions.IsStaged(SoftwareActionFactory.AppxRemovePrefix + _id) ? "Removal staged" :
        _actions.IsStaged(SoftwareActionFactory.AppxReinstallPrefix + _id) ? "Reinstall already staged" : "Remove for all users · No undo";

    [ObservableProperty] private bool _isIncluded;
    partial void OnIsIncludedChanged(bool value) => _changed();

    public PackRemovalViewModel(string id, WindowsAppEntry? entry, bool installed, IPendingActionsService actions, Action changed)
    {
        _id = entry?.Id ?? id;
        Entry = entry;
        _installed = installed;
        _actions = actions;
        _changed = changed;
        _isIncluded = CanInclude;
    }

    public void Refresh()
    {
        if (Entry is null || !_installed ||
            _actions.IsStaged(SoftwareActionFactory.AppxRemovePrefix + _id) ||
            _actions.IsStaged(SoftwareActionFactory.AppxReinstallPrefix + _id)) IsIncluded = false;
        OnPropertyChanged(nameof(CanInclude));
        OnPropertyChanged(nameof(State));
    }
}
