namespace ThisIsMyPC.Core.Sets;

/// <summary>
/// Read-only, per-module resolver of a set entry's live system state, used by the Set
/// Loader preview (8.2) and conflict detection (8.3). Implementations live beside their
/// module (they need the module's readers); the App registers them all and looks one up
/// by <see cref="ModuleId"/> matching <see cref="SetEntry.ModuleId"/>.
/// </summary>
public interface ISetEntryInspector
{
    /// <summary>The module's IModule.Info.Name string, e.g. "Explorer".</summary>
    string ModuleId { get; }

    /// <summary>
    /// Resolves the entry's current system state, or null when the settingId is unknown
    /// to this module (the caller marks the entry "will be skipped").
    /// </summary>
    SetEntryState? Inspect(SetEntry entry);
}
