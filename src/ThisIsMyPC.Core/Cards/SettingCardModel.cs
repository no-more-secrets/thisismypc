using ThisIsMyPC.Core.Modules;

namespace ThisIsMyPC.Core.Cards;

/// <summary>
/// Card rendering data model (Epic 10). Modules produce these POCOs from scan data;
/// the host's SettingCardControl renders them. Pure data — no Avalonia dependency,
/// no App types. Pending state is NOT stored here: PendingChangesService is the
/// source of truth and the host card view model binds to both.
/// </summary>
public record SettingCardModel
{
    public required string SettingId { get; init; }
    public required string ModuleId { get; init; }
    public required string DisplayName { get; init; }
    public required string Description { get; init; }
    public required SettingControlType ControlType { get; init; }

    /// <summary>String-typed like ChangeDescriptor values; ControlType is the discriminator.</summary>
    public required string CurrentValue { get; init; }

    /// <summary>Human-readable rendering of CurrentValue, e.g. "Left", "Enabled".</summary>
    public string? CurrentDisplayValue { get; init; }

    /// <summary>Value/display pairs for dropdowns.</summary>
    public IReadOnlyList<SettingOption>? AvailableOptions { get; init; }

    /// <summary>Shown only in the Registry Data display mode.</summary>
    public string? RegistryPath { get; init; }
    public string? ValueName { get; init; }
    public string? RegistryValueType { get; init; }

    /// <summary>Visual grouping within a module tab; cards sharing a GroupId render under one section header.</summary>
    public string? GroupId { get; init; }

    /// <summary>UI-facing enforcement summary; drives badge rendering.</summary>
    public EnforcementProfile? Enforcement { get; init; }

    /// <summary>Drives the Owner Mode degradation pattern (control inert with callout when the service is absent).</summary>
    public bool OwnerModeRequired { get; init; }

    /// <summary>Edition on which this setting is cosmetic/ineffective; drives an informational callout.</summary>
    public WindowsSku? SkuRestriction { get; init; }
}

public record SettingOption(string Value, string DisplayName);

public enum SettingControlType { Toggle, Dropdown, Slider, Action }
