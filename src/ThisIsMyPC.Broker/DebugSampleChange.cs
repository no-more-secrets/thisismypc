using System.Globalization;
using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Results;

namespace ThisIsMyPC.Broker;

internal static class DebugSampleChange
{
    internal const string ModuleId = "DebugModule";
    internal const string Location = "No system target (Debug sample)";

    internal static bool Allows(ChangeDescriptor change)
    {
        if (change.ModuleId != ModuleId
            || change.SystemLocation != Location
            || change.ValueType != ChangeValueType.Registry_DWord
            || change.Enforcement is not null
            || change.RestartRequirement != RestartRequirement.None
            || !change.SettingId.StartsWith("debug-setting-", StringComparison.Ordinal)
            || !int.TryParse(change.SettingId.AsSpan("debug-setting-".Length), NumberStyles.None,
                CultureInfo.InvariantCulture, out var number)
            || number <= 0
            || change.SettingId != $"debug-setting-{number}"
            || change.DisplayName != $"Test Setting {number}")
            return false;

        return change.Category switch
        {
            ChangeCategory.Enable => MatchesEitherDirection(change, "0", "1", "Disabled", "Enabled"),
            ChangeCategory.Disable => MatchesEitherDirection(change, "1", "0", "Enabled", "Disabled"),
            ChangeCategory.Modify => MatchesEitherDirection(change, "0", "1", "Value A", "Value B"),
            _ => false,
        };
    }

    private static bool MatchesEitherDirection(ChangeDescriptor change, string before, string after,
        string beforeDisplay, string afterDisplay) =>
        change.BeforeValue == before && change.AfterValue == after
            && change.BeforeDisplay == beforeDisplay && change.AfterDisplay == afterDisplay
        || change.BeforeValue == after && change.AfterValue == before
            && change.BeforeDisplay == afterDisplay && change.AfterDisplay == beforeDisplay;

    internal static Task<OperationResult<bool>> Apply(ChangeDescriptor change) => Task.FromResult(
        Allows(change)
            ? OperationResult<bool>.Success(true)
            : OperationResult<bool>.Failure("The Debug sample is invalid.", ErrorCategory.AccessDenied));
}
