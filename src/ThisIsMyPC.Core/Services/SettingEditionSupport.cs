using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Modules;

namespace ThisIsMyPC.Core.Services;

/// <summary>Shared edition rules for cards, presets, and the interactive change queue.</summary>
public static class SettingEditionSupport
{
    private static readonly string[] EnterpriseContentPolicies =
        ["DisableWindowsConsumerFeatures", "DisableSoftLanding", "DisableCloudOptimizedContent", "DisableConsumerAccountStateContent"];

    public static WindowsSku? RequiredEdition(string? location, WindowsSku? declared, string? value = null)
    {
        // An explicit Home declaration is reserved for verified support through this method.
        if (declared == WindowsSku.Home)
            return null;

        var path = location?.Replace('/', '\\') ?? string.Empty;
        var windowsPolicy = path.Contains(@"\Software\Policies\Microsoft\", StringComparison.OrdinalIgnoreCase)
            || path.Contains(@"\Software\Microsoft\Windows\CurrentVersion\Policies\", StringComparison.OrdinalIgnoreCase);
        var required = declared ?? (windowsPolicy ? WindowsSku.Pro : (WindowsSku?)null);

        // These consumer-content policies require Enterprise or Education.
        if (path.Contains(@"\Policies\Microsoft\Windows\CloudContent\", StringComparison.OrdinalIgnoreCase)
            && EnterpriseContentPolicies
                .Any(name => path.EndsWith("\\" + name, StringComparison.OrdinalIgnoreCase)))
            required = WindowsSku.Enterprise;

        // Conservative product rule: significant Defender policies remain unavailable
        // on Pro until their effect and persistence are verified for the applied method.
        if (declared is null && path.Contains(@"\Policies\Microsoft\Windows Defender\", StringComparison.OrdinalIgnoreCase))
            required = WindowsSku.Enterprise;

        // Required diagnostic data (1) remains available on Pro. Off (0) does not.
        if (value == "0" && path.EndsWith(@"\Policies\Microsoft\Windows\DataCollection\AllowTelemetry", StringComparison.OrdinalIgnoreCase))
            required = WindowsSku.Enterprise;

        return required;
    }

    public static string? RequirementLabel(WindowsSku? required) => required switch
    {
        WindowsSku.Pro => "Requires Windows Pro",
        WindowsSku.Enterprise or WindowsSku.Education => "Requires Windows Enterprise or Education",
        _ => null,
    };

    public static string? BlockReason(WindowsSku? current, WindowsSku? required)
    {
        if (required is null || required == WindowsSku.Home)
            return null;
        if (current is null)
            return "Windows edition is unknown. Support for this setting is unverified.";
        return current.Value.Tier() < required.Value.Tier() ? RequirementLabel(required) : null;
    }

    public static string? BlockReason(WindowsSku? current, string? location, WindowsSku? declared, string? value = null)
    {
        var required = RequiredEdition(location, declared, value);
        var reason = BlockReason(current, required);
        if (reason is not null && current is not null && declared is null
            && (required == WindowsSku.Pro
                || location?.Contains(@"\Policies\Microsoft\Windows Defender\", StringComparison.OrdinalIgnoreCase) == true))
            return reason + ". Not verified on this edition.";
        return reason;
    }

    public static string? BlockReason(WindowsSku? current, ChangeDescriptor change) =>
        BlockReason(current, change.SystemLocation, change.Enforcement?.SkuRestriction, change.AfterValue);
}
