namespace ThisIsMyPC.Core.Settings;

public enum ModuleSettingType { Toggle, Text, Choice }

/// <summary>One configurable module setting surfaced in the unified settings panel (FR6).</summary>
public sealed record ModuleSettingDefinition(
    string Key,
    string DisplayName,
    string Description,
    ModuleSettingType Type,
    string DefaultValue,
    IReadOnlyList<(string Value, string DisplayName)>? Options = null);

/// <summary>
/// Modules expose configurable settings by implementing this and registering it in DI
/// (explicit AddSingleton, like ISetEntryInspector). The 7-2 settings panel groups the
/// definitions by module; values live in ISettingsService's module scope.
/// </summary>
public interface IModuleSettingsContributor
{
    /// <summary>The module's IModule.Info.Name string.</summary>
    string ModuleId { get; }

    IReadOnlyList<ModuleSettingDefinition> SettingDefinitions { get; }
}
