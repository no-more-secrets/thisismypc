namespace ThisIsMyPC.Modules.Shell.Models;

/// <summary>How a setting imported from ExplorerPatcher is edited.</summary>
public enum ExplorerPatcherSettingKind
{
    /// <summary>On writes 1, off writes 0.</summary>
    Toggle,

    /// <summary>On writes 0: the value name says what it hides, the label says what it shows.</summary>
    InvertedToggle,

    /// <summary>One of several numbered values.</summary>
    Choice,
}

/// <summary>One selectable value of a <see cref="ExplorerPatcherSettingKind.Choice"/> setting.</summary>
public sealed record ExplorerPatcherOption(int Value, string DisplayName);

/// <summary>
/// One ExplorerPatcher setting as the app renders it. The definition comes
/// from ExplorerPatcher's own settings manifest (see
/// tools/import-explorerpatcher-settings.ps1); the live value and
/// <see cref="IsAvailable"/> come from the registry at scan time.
/// </summary>
/// <param name="Description">What the row does, in the app's own words (tools/explorerpatcher-catalog-overrides.psd1).</param>
/// <param name="GroupHeading">Sub-heading the row sits under on its tab, named after ExplorerPatcher's own page; empty for the tab's main run.</param>
/// <param name="Page">The ExplorerPatcher settings page the value came from.</param>
/// <param name="CurrentValue">Null when the value is absent, which means ExplorerPatcher uses <paramref name="DefaultValue"/>.</param>
/// <param name="IsAvailable">False when ExplorerPatcher's own condition for the setting does not hold, so it would do nothing.</param>
/// <param name="AdjustedValue">
/// Set when ExplorerPatcher treats the live value as a different one on this
/// machine (a taskbar style whose files are gone reads as the next one down),
/// so the row shows what is in force. The change pipeline still records the
/// raw value, so undo puts back exactly what was there.
/// </param>
public sealed record ExplorerPatcherSetting(
    string Id,
    string DisplayName,
    string Description,
    string GroupHeading,
    string Page,
    ShellSection Section,
    string RegistryKeyPath,
    string RegistryValueName,
    ExplorerPatcherSettingKind Kind,
    int DefaultValue,
    bool RequiresExplorerRestart,
    string Condition,
    IReadOnlyList<ExplorerPatcherOption> Options,
    int? CurrentValue = null,
    bool IsAvailable = true,
    int? AdjustedValue = null)
{
    /// <summary>Where the value lives, in the "key\value" form the change pipeline uses.</summary>
    public string SystemLocation => $@"{RegistryKeyPath}\{RegistryValueName}";

    /// <summary>The value in force now: what ExplorerPatcher makes of the live one, or its default when absent.</summary>
    public int EffectiveValue => AdjustedValue ?? CurrentValue ?? DefaultValue;

    /// <summary>Toggle rows only: whether the row reads as on right now.</summary>
    public bool IsOn => Kind == ExplorerPatcherSettingKind.InvertedToggle
        ? EffectiveValue == 0
        : EffectiveValue != 0;

    /// <summary>The value a toggle writes for the given switch position.</summary>
    public int ValueFor(bool on) => Kind == ExplorerPatcherSettingKind.InvertedToggle
        ? (on ? 0 : 1)
        : (on ? 1 : 0);

    /// <summary>Human text for one value, from the option list when there is one.</summary>
    public string DisplayFor(int value)
    {
        foreach (var option in Options)
        {
            if (option.Value == value)
                return option.DisplayName;
        }
        if (Kind != ExplorerPatcherSettingKind.Choice)
        {
            var on = Kind == ExplorerPatcherSettingKind.InvertedToggle ? value == 0 : value != 0;
            return on ? "On" : "Off";
        }
        return value.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
}
