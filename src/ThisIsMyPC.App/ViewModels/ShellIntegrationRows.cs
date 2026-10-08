using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Modules.Shell;
using ThisIsMyPC.Modules.Shell.Changes;
using ThisIsMyPC.Modules.Shell.Models;
using ThisIsMyPC.Interop.Win32.Shell;
namespace ThisIsMyPC.App.ViewModels;
public sealed partial class ShellViewModel
{
    private void AddIntegrationRows(ShellScanData scan, IPendingChangesService queue, IRegistryService registry, IShellNativeSettings native)
    {
        static ChangeGroup Wrap(ChangeDescriptor change) => new()
        { GroupId = Guid.NewGuid().ToString(), DisplayName = change.DisplayName, Description = change.DisplayName, Changes = [change] };
        ShellSettingViewModel NativeRow(string location, string label, string description, string? initial)
        {
            string Read() => (location == ShellIntegrationChanges.Taskbar ? native.ReadTaskbarState() : native.ReadRoundedCornersState())
                is { IsSuccess: true, Value: { } value } ? value : throw new InvalidOperationException("The setting could not be read.");
            var row = new ShellSettingViewModel(label, description, location, initial is not null && ShellIntegrationChanges.IsOn(location, initial), queue,
                groupFactory: on => Wrap(ShellIntegrationChanges.Native(location, Read(), on)),
                readRegistryState: () => ShellIntegrationChanges.IsOn(location, Read()), rehydrateSettingId: location);
            row.SetAvailability(initial is null ? "The current Windows state could not be read. Refresh this page to retry." : null);
            return row;
        }
        TaskbarSettings.Add(NativeRow(ShellIntegrationChanges.Taskbar, "Automatically hide the taskbar",
            "Hide the taskbar until the pointer reaches its screen edge.", scan.TaskbarAutoHideState));
        if (!scan.ExplorerPatcherInstalled) return;
        var general = (GeneralPatcherGroups.FirstOrDefault(g => g.Heading.Length == 0) ?? PatcherGroupFor(ShellSection.General, "")).Rows;
        general.Insert(0, new ShellSettingViewModel("Register ExplorerPatcher as a shell extension",
            "Enable ExplorerPatcher in Open and Save dialogs. Affects other apps. Restart those apps after changing registration.",
            ShellIntegrationChanges.Registration, scan.ShellExtensionRegistered, queue,
            groupFactory: on => ShellIntegrationChanges.Register(registry, on),
            readRegistryState: () => ShellIntegrationChanges.IsRegistered(registry), rehydrateSettingId: ShellIntegrationChanges.Registration + "0"));
        general.Insert(1, NativeRow(ShellIntegrationChanges.Corners, "Disable rounded corners for application windows",
            "Use ExplorerPatcher's installed Desktop Window Manager service for square window corners.", scan.RoundedCornersState));
        var navigation = new ShellSettingViewModel("Disable navigation bar",
            "Remove the navigation bar from Open and Save dialogs. Requires ExplorerPatcher shell-extension registration.",
            ShellIntegrationChanges.NavigationKey, ShellIntegrationChanges.NavigationDisabled(registry), queue,
            groupFactory: on => Wrap(ShellIntegrationChanges.NavigationChange(registry, on)),
            readRegistryState: () => ShellIntegrationChanges.NavigationDisabled(registry), rehydrateSettingId: ShellIntegrationChanges.Navigation);
        navigation.SetAvailability(scan.ShellExtensionRegistered ? null : "Requires ExplorerPatcher shell-extension registration. Enable it in General, then refresh this page.");
        (FileExplorerPatcherGroups.FirstOrDefault(g => g.Heading.Length == 0) ?? PatcherGroupFor(ShellSection.FileExplorer, "")).Rows.Add(navigation);
    }
}
