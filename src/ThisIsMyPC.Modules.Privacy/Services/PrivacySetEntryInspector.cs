using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Core.Sets;
using ThisIsMyPC.Modules.Privacy.Changes;
using ThisIsMyPC.Modules.Privacy.Models;

namespace ThisIsMyPC.Modules.Privacy.Services;

/// <summary>
/// Resolves set entries targeting "Privacy &amp; Telemetry" to live system state.
/// The inking-typing group follows the toggle value convention: the entry value is
/// the FIRST descriptor's configured value ("1", RestrictImplicitInkCollection).
/// </summary>
public sealed class PrivacySetEntryInspector : ISetEntryInspector
{
    private readonly PrivacySettingsReader _reader;

    public PrivacySetEntryInspector(IRegistryService registryService)
    {
        _reader = new PrivacySettingsReader(registryService);
    }

    public string ModuleId => PrivacyChangeFactory.ModuleId;

    public SetEntryState? Inspect(SetEntry entry)
    {
        if (entry.SettingId == "inking-typing")
        {
            var prefs = _reader.ReadInkingTyping();
            var configuredCount = prefs.Count(p => p.IsConfigured);
            var wantsConfigure = string.Equals(entry.Value, prefs[0].ConfiguredValue, StringComparison.Ordinal);
            var wantsDefault = string.Equals(entry.Value, prefs[0].DefaultValue, StringComparison.Ordinal);

            return new SetEntryState
            {
                SettingDisplayName = "Inking and typing personalization",
                CurrentValue = prefs[0].CurrentValue,
                CurrentDisplay = configuredCount == prefs.Count ? "Configured"
                    : configuredCount == 0 ? "Windows default"
                    : "Partially set",
                IsApplied = wantsConfigure ? configuredCount == prefs.Count
                    : wantsDefault && configuredCount == 0,
            };
        }

        var pref = FindSingle(entry.SettingId);
        if (pref is null)
            return null;

        var direction = Direction(entry, pref);

        return new SetEntryState
        {
            SettingDisplayName = pref.DisplayName,
            CurrentValue = pref.CurrentValue,
            CurrentDisplay = pref.IsConfigured ? "Configured" : "Windows default",
            IsApplied = direction is { } configure
                && (configure ? pref.IsConfigured : pref.CurrentValue == pref.DefaultValue),
        };
    }

    public ChangeGroup? CreateChangeGroup(SetEntry entry)
    {
        if (entry.SettingId == "inking-typing")
        {
            var prefs = _reader.ReadInkingTyping();
            return Direction(entry, prefs[0]) is { } configure
                ? PrivacyChangeFactory.CreateInkingTypingGroup(
                    prefs, configure,
                    description: "Stops handwriting, typing history, and contact collection for the personal dictionary.")
                : null;
        }

        var pref = FindSingle(entry.SettingId);
        if (pref is null || Direction(entry, pref) is not { } configureSingle)
            return null;

        var change = PrivacyChangeFactory.CreateToggle(pref, configureSingle);

        return new ChangeGroup
        {
            GroupId = Guid.NewGuid().ToString("N"),
            DisplayName = pref.DisplayName,
            Description = pref.Description,
            Changes = [change],
        };
    }

    /// <summary>Configured value → configure; default value (empty = restore) → restore; else null.</summary>
    private static bool? Direction(SetEntry entry, PrivacyPreference primary)
        => string.Equals(entry.Value, primary.ConfiguredValue, StringComparison.Ordinal) ? true
            : string.Equals(entry.Value, primary.DefaultValue, StringComparison.Ordinal) ? false
            : null;

    private PrivacyPreference? FindSingle(string settingId)
        => _reader.ReadSingles().FirstOrDefault(p => p.Id == settingId);
}
