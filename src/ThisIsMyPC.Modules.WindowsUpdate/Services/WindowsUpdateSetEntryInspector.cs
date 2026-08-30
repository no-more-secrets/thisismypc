using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Core.Sets;
using ThisIsMyPC.Modules.WindowsUpdate.Changes;
using ThisIsMyPC.Modules.WindowsUpdate.Models;

namespace ThisIsMyPC.Modules.WindowsUpdate.Services;

/// <summary>
/// Resolves set entries targeting "Windows Update" to live policy state. The
/// version-pin group follows the toggle value convention: the entry value is the FIRST
/// descriptor's configured value ("1", TargetReleaseVersion). An empty entry value
/// means "restore to Not configured".
/// </summary>
public sealed class WindowsUpdateSetEntryInspector : ISetEntryInspector
{
    private readonly WindowsUpdateSettingsReader _reader;

    public WindowsUpdateSetEntryInspector(IRegistryService registryService)
    {
        _reader = new WindowsUpdateSettingsReader(registryService);
    }

    public string ModuleId => WindowsUpdateChangeFactory.ModuleId;

    public SetEntryState? Inspect(SetEntry entry)
    {
        if (entry.SettingId == "version-pin")
        {
            var pin = _reader.ReadVersionPin();
            if (pin.Count == 0)
                return null; // DisplayVersion unreadable; pin unavailable on this machine

            var configuredCount = pin.Count(s => s.IsConfigured);
            var wantsConfigure = string.Equals(entry.Value, pin[0].ConfiguredValue, StringComparison.Ordinal);

            return new SetEntryState
            {
                SettingDisplayName = pin[0].DisplayName,
                CurrentValue = pin[0].CurrentValue,
                CurrentDisplay = configuredCount == pin.Count ? "Configured"
                    : configuredCount == 0 ? "Not configured"
                    : "Partially set",
                IsApplied = wantsConfigure
                    ? configuredCount == pin.Count
                    : entry.Value.Length == 0 && configuredCount == 0,
            };
        }

        var setting = FindSingle(entry.SettingId);
        if (setting is null)
            return null;

        // A value matching neither toggle direction is never "applied"; a bogus entry
        // must not preview as done just because the machine happens to hold that value.
        var direction = Direction(entry, setting);

        return new SetEntryState
        {
            SettingDisplayName = setting.DisplayName,
            CurrentValue = setting.CurrentValue,
            CurrentDisplay = setting.IsConfigured ? "Configured" : "Not configured",
            IsApplied = direction is { } configure
                && (configure ? setting.IsConfigured : setting.CurrentValue.Length == 0),
        };
    }

    public ChangeGroup? CreateChangeGroup(SetEntry entry)
    {
        if (entry.SettingId == "version-pin")
        {
            var pin = _reader.ReadVersionPin();
            if (pin.Count == 0)
                return null;

            return Direction(entry, pin[0]) is { } configure
                ? WindowsUpdateChangeFactory.CreateVersionPinGroup(pin, configure)
                : null;
        }

        var setting = FindSingle(entry.SettingId);
        if (setting is null || Direction(entry, setting) is not { } configureSingle)
            return null;

        // UX\Settings state values carry no enforcement; policies route by GPCache need.
        var change = setting.RegistryKeyPath == WindowsUpdateRegistryPaths.UxSettingsKeyPath
            ? WindowsUpdateChangeFactory.CreateUxToggle(setting, configureSingle)
            : WindowsUpdateChangeFactory.CreateToggle(
                setting, configureSingle, gpCache: entry.SettingId != "delivery-optimization");

        return new ChangeGroup
        {
            GroupId = Guid.NewGuid().ToString("N"),
            DisplayName = setting.DisplayName,
            Description = setting.Description,
            Changes = [change],
        };
    }

    /// <summary>Configured value → configure; empty → restore; anything else → null.</summary>
    private static bool? Direction(SetEntry entry, UpdatePolicySetting primary)
        => string.Equals(entry.Value, primary.ConfiguredValue, StringComparison.Ordinal) ? true
            : entry.Value.Length == 0 ? false
            : null;

    private UpdatePolicySetting? FindSingle(string settingId)
        => _reader.ReadSingles().FirstOrDefault(s => s.Id == settingId)
            ?? _reader.ReadUxSettings().FirstOrDefault(s => s.Id == settingId);
}
