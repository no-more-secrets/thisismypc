using ThisIsMyPC.Core.Changes;

namespace ThisIsMyPC.Modules.Shell.Models;

public sealed record ExplorerPreference(
    string Id,
    string DisplayName,
    string Description,
    string RegistryKeyPath,
    string RegistryValueName,
    ChangeValueType ValueType,
    string CurrentValue,
    string EnabledValue,
    string DisabledValue,
    bool IsEnabled,
    RestartRequirement RestartRequirement);
