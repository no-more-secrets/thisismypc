using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Enforcement;
using ThisIsMyPC.Modules.Shell.Models;

namespace ThisIsMyPC.Modules.Shell.Changes;

public static class ContextMenuChangeFactory
{
    private const string ModuleId = "Context Menus";

    // Vendors re-register their handlers on update/reinstall; informational only;
    // no companion actions. Attached only when disabling (restoring vendor state has
    // no reversion risk).
    private static readonly SettingEnforcement HandlerReRegistrationEnforcement = new()
    {
        ReversionVectors = ["Application updates or reinstalls may re-register this handler"],
    };

    /// <summary>
    /// Creates toggle change descriptors for a context menu handler.
    /// Produces one ChangeDescriptor per registry path, all sharing the same CLSID-based SettingId
    /// so PendingChangesService auto-groups them.
    /// </summary>
    public static IReadOnlyList<ChangeDescriptor> CreateToggle(ContextMenuHandler handler, bool enable)
    {
        var afterClsid = enable ? handler.Clsid : $"-{handler.Clsid}";
        var settingId = MakeSettingId(handler.Clsid);

        var registryPaths = handler.AllRegistryPaths ?? [handler.RegistryPath];

        return registryPaths.Select(path =>
        {
            // Use per-path enabled state for accurate BeforeValue (needed for revert)
            var pathEnabled = handler.PathEnabledStates?.GetValueOrDefault(path, handler.IsEnabled)
                              ?? handler.IsEnabled;
            var beforeClsid = pathEnabled ? handler.Clsid : $"-{handler.Clsid}";

            return new ChangeDescriptor
            {
                ModuleId = ModuleId,
                SettingId = settingId,
                DisplayName = $"Context menu: {handler.Name}",
                SystemLocation = $@"{path}\(Default)",
                BeforeValue = beforeClsid,
                AfterValue = afterClsid,
                BeforeDisplay = enable ? "Disabled" : "Enabled",
                AfterDisplay = enable ? "Enabled" : "Disabled",
                ValueType = ChangeValueType.Registry_String,
                Category = enable ? ChangeCategory.Enable : ChangeCategory.Disable,
                Enforcement = enable ? null : HandlerReRegistrationEnforcement,
            };
        }).ToList();
    }

    /// <summary>
    /// Creates a single ChangeDescriptor to toggle a handler via the Shell Extensions Blocked list.
    /// Unlike CreateToggle (per-path dash-prefix), this produces one system-wide descriptor.
    /// </summary>
    public static ChangeDescriptor CreateBlockedListToggle(ContextMenuHandler handler, bool enable)
    {
        var settingId = MakeSettingId(handler.Clsid);

        return new ChangeDescriptor
        {
            ModuleId = ModuleId,
            SettingId = settingId,
            DisplayName = $"Context menu: {handler.Name}",
            SystemLocation = $@"{ShellRegistryPaths.BlockedListKeyPath}\{handler.Clsid}",
            BeforeValue = enable ? "" : ShellRegistryPaths.AbsentValue,
            AfterValue = enable ? ShellRegistryPaths.AbsentValue : "",
            BeforeDisplay = enable ? "Disabled" : "Enabled",
            AfterDisplay = enable ? "Enabled" : "Disabled",
            ValueType = ChangeValueType.Registry_String,
            Category = enable ? ChangeCategory.Enable : ChangeCategory.Disable,
            RestartRequirement = RestartRequirement.ExplorerRestart,
            Enforcement = enable ? null : HandlerReRegistrationEnforcement,
        };
    }

    /// <summary>
    /// Creates a migration ChangeGroup that moves a handler from dash-prefix disable to Blocked list.
    /// Adds CLSID to blocked list and removes dash-prefix from all registration paths.
    /// </summary>
    public static ChangeGroup CreateMigration(ContextMenuHandler handler)
    {
        var settingId = MakeSettingId(handler.Clsid);
        var changes = new List<ChangeDescriptor>();

        // Add CLSID to blocked list
        changes.Add(new ChangeDescriptor
        {
            ModuleId = ModuleId,
            SettingId = settingId,
            DisplayName = $"Context menu: {handler.Name} (migrate to blocked list)",
            SystemLocation = $@"{ShellRegistryPaths.BlockedListKeyPath}\{handler.Clsid}",
            BeforeValue = ShellRegistryPaths.AbsentValue,
            AfterValue = "",
            BeforeDisplay = "Not blocked",
            AfterDisplay = "Blocked",
            ValueType = ChangeValueType.Registry_String,
            Category = ChangeCategory.Disable,
            // Same end state as CreateBlockedListToggle(disable); same reversion risk.
            Enforcement = HandlerReRegistrationEnforcement,
        });

        // Remove dash-prefix from each registration path (restore clean CLSID)
        var registryPaths = handler.AllRegistryPaths ?? [handler.RegistryPath];
        foreach (var path in registryPaths)
        {
            var pathEnabled = handler.PathEnabledStates?.GetValueOrDefault(path, handler.IsEnabled)
                              ?? handler.IsEnabled;

            // Only add restore descriptor for paths that are currently dash-prefixed
            if (!pathEnabled)
            {
                changes.Add(new ChangeDescriptor
                {
                    ModuleId = ModuleId,
                    SettingId = settingId,
                    DisplayName = $"Context menu: {handler.Name} (restore CLSID)",
                    SystemLocation = $@"{path}\(Default)",
                    BeforeValue = $"-{handler.Clsid}",
                    AfterValue = handler.Clsid,
                    BeforeDisplay = "Dash-prefixed",
                    AfterDisplay = "Clean CLSID",
                    ValueType = ChangeValueType.Registry_String,
                    Category = ChangeCategory.Modify,
                });
            }
        }

        return new ChangeGroup
        {
            GroupId = Guid.NewGuid().ToString("N"),
            DisplayName = $"Migrate {handler.Name} to blocked list",
            Description = $"Move {handler.Name} from dash-prefix disable to Shell Extensions Blocked list",
            Changes = changes,
        };
    }

    /// <summary>
    /// Creates toggle change descriptors for a static verb handler.
    /// Produces one ChangeDescriptor per registry path, using LegacyDisable mechanism.
    /// </summary>
    public static IReadOnlyList<ChangeDescriptor> CreateStaticVerbToggle(ContextMenuHandler handler, bool enable)
    {
        var verbInfo = handler.VerbInfo!;
        var settingId = MakeStaticVerbSettingId(verbInfo.VerbName, handler.AppliesTo);
        var registryPaths = handler.AllRegistryPaths ?? [handler.RegistryPath];

        return registryPaths.Select(path =>
        {
            // Use per-path LegacyDisable state for accurate BeforeValue (needed for revert)
            var pathEnabled = handler.PathEnabledStates?.GetValueOrDefault(path, handler.IsEnabled)
                              ?? handler.IsEnabled;

            // Remap HKCR → HKCU\Software\Classes so LegacyDisable writes succeed
            // even for TrustedInstaller-owned system verb keys
            var writePath = ShellRegistryPaths.RemapHkcrToHkcu(path);

            return new ChangeDescriptor
            {
                ModuleId = ModuleId,
                SettingId = settingId,
                DisplayName = $"Context menu: {handler.Name}",
                // LegacyDisable value on the verb key itself (HKCU overlay)
                SystemLocation = $@"{writePath}\LegacyDisable",
                BeforeValue = pathEnabled ? ShellRegistryPaths.AbsentValue : "",
                AfterValue = enable ? ShellRegistryPaths.AbsentValue : "",
                BeforeDisplay = enable ? "Disabled" : "Enabled",
                AfterDisplay = enable ? "Enabled" : "Disabled",
                ValueType = ChangeValueType.Registry_String,
                Category = enable ? ChangeCategory.Enable : ChangeCategory.Disable,
                Enforcement = enable ? null : HandlerReRegistrationEnforcement,
            };
        }).ToList();
    }

    /// <summary>
    /// Creates a ChangeGroup to remove an orphaned handler's registration from all its registry paths.
    /// Each ChangeDescriptor deletes the (Default) value (sets to AbsentValue) so Explorer stops loading it.
    /// </summary>
    public static ChangeGroup CreateOrphanCleanup(ContextMenuHandler handler)
    {
        var settingId = MakeSettingId(handler.Clsid);
        var registryPaths = handler.AllRegistryPaths ?? [handler.RegistryPath];

        var changes = registryPaths.Select(path =>
        {
            var beforeValue = handler.PathEnabledStates?.GetValueOrDefault(path, handler.IsEnabled) ?? handler.IsEnabled
                ? handler.Clsid
                : $"-{handler.Clsid}";

            return new ChangeDescriptor
            {
                ModuleId = ModuleId,
                SettingId = settingId,
                DisplayName = $"Clean up orphaned handler: {handler.Name}",
                SystemLocation = $@"{path}\(Default)",
                BeforeValue = beforeValue,
                AfterValue = ShellRegistryPaths.AbsentValue,
                BeforeDisplay = "Orphaned registration",
                AfterDisplay = "Removed",
                ValueType = ChangeValueType.Registry_String,
                Category = ChangeCategory.Delete,
                RestartRequirement = RestartRequirement.ExplorerRestart,
            };
        }).ToList();

        return new ChangeGroup
        {
            GroupId = Guid.NewGuid().ToString("N"),
            DisplayName = $"Clean up orphaned handler: {handler.Name}",
            Description = $"Remove orphaned registration for {handler.Name} (DLL missing)",
            Changes = changes,
        };
    }

    /// <summary>
    /// Creates a single ChangeGroup containing cleanup descriptors for all provided orphaned handlers.
    /// Applied atomically with full rollback on failure.
    /// </summary>
    public static ChangeGroup CreateBulkOrphanCleanup(IReadOnlyList<ContextMenuHandler> orphans)
    {
        var changes = new List<ChangeDescriptor>();

        foreach (var handler in orphans)
        {
            var settingId = MakeSettingId(handler.Clsid);
            var registryPaths = handler.AllRegistryPaths ?? [handler.RegistryPath];

            foreach (var path in registryPaths)
            {
                var beforeValue = handler.PathEnabledStates?.GetValueOrDefault(path, handler.IsEnabled) ?? handler.IsEnabled
                    ? handler.Clsid
                    : $"-{handler.Clsid}";

                changes.Add(new ChangeDescriptor
                {
                    ModuleId = ModuleId,
                    SettingId = settingId,
                    DisplayName = $"Clean up orphaned handler: {handler.Name}",
                    SystemLocation = $@"{path}\(Default)",
                    BeforeValue = beforeValue,
                    AfterValue = ShellRegistryPaths.AbsentValue,
                    BeforeDisplay = "Orphaned registration",
                    AfterDisplay = "Removed",
                    ValueType = ChangeValueType.Registry_String,
                    Category = ChangeCategory.Delete,
                    RestartRequirement = RestartRequirement.ExplorerRestart,
                });
            }
        }

        return new ChangeGroup
        {
            GroupId = Guid.NewGuid().ToString("N"),
            DisplayName = $"Clean up {orphans.Count} orphaned handlers",
            Description = $"Remove {orphans.Count} orphaned handler registrations (DLLs missing)",
            Changes = changes,
        };
    }

    public static string MakeSettingId(string clsid)
    {
        // Strip braces from CLSID for a clean SettingId
        var cleanClsid = clsid.TrimStart('{').TrimEnd('}');
        return $"ctx-handler-{cleanClsid}";
    }

    public static string MakeStaticVerbSettingId(string verbName, string scope)
    {
        // Normalize verb name for SettingId (lowercase, no spaces)
        var cleanVerb = verbName.ToLowerInvariant().Replace(' ', '-');
        var cleanScope = scope.ToLowerInvariant().Replace(' ', '-');
        return $"ctx-verb-{cleanVerb}-{cleanScope}";
    }
}
