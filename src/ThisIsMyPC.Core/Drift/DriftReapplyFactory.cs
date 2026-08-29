using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Data;

namespace ThisIsMyPC.Core.Drift;

/// <summary>
/// Builds descriptors from drift-report rows (28-3). Lives in Core so the
/// enforcement JSON round-trip (internal set-file DTO shape) stays in one assembly.
/// </summary>
public static class DriftReapplyFactory
{
    /// <summary>Reapply: current (reverted) value back to the expected value, enforcement preserved.</summary>
    public static ChangeDescriptor CreateReapply(
        string moduleId, string settingId, string displayName, string systemLocation,
        ChangeValueType valueType, string expectedValue, string currentValue, string? enforcementJson) => new()
    {
        ModuleId = moduleId,
        SettingId = settingId,
        DisplayName = $"Reapply after drift: {displayName}",
        SystemLocation = systemLocation,
        BeforeValue = currentValue,
        AfterValue = expectedValue,
        BeforeDisplay = "Reverted by Windows",
        AfterDisplay = "Restored",
        ValueType = valueType,
        Category = ChangeCategory.Modify,
        Enforcement = EnforcementJson.Deserialize(enforcementJson),
    };

    /// <summary>History audit row: what the system did (expected → current), distinct SystemReversion category.</summary>
    public static ChangeHistoryEntry CreateDriftHistoryEntry(
        string moduleId, string settingId, string displayName, string systemLocation,
        ChangeValueType valueType, string expectedValue, string currentValue,
        string groupId, DateTimeOffset detectedAtUtc, string? suspectedCause) => new()
    {
        ModuleId = moduleId,
        SettingId = settingId,
        DisplayName = suspectedCause is { Length: > 0 } cause
            ? $"Drift: {displayName} (suspected cause: {cause})"
            : $"Drift: {displayName}",
        SystemLocation = systemLocation,
        BeforeValue = expectedValue,
        AfterValue = currentValue,
        BeforeDisplay = "As applied by ThisIsMyPC",
        AfterDisplay = "Reverted by Windows",
        ValueType = valueType,
        Category = ChangeCategory.SystemReversion,
        GroupId = groupId,
        AppliedAt = detectedAtUtc,
    };
}
