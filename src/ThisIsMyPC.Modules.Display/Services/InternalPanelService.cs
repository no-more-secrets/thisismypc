using ThisIsMyPC.Core.Display;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;

namespace ThisIsMyPC.Modules.Display.Services;

/// <summary>
/// The laptop's built-in panel, controlled through the active power plan's
/// display-brightness setting (VIDEO subgroup). No WMI, no drivers: the same
/// powrprof path the Windows brightness slider ultimately writes, and
/// IPowerService.WriteSettingIndex re-activates the scheme so it applies live.
/// Both AC and DC get the value; a brightness the user picks should not jump
/// when the charger state changes.
/// </summary>
public sealed class InternalPanelService
{
    public const string PanelId = "internal-panel";

    public static readonly Guid VideoSubgroup = new("7516b95f-f776-4464-8c53-06167f40cc99");
    public static readonly Guid BrightnessSetting = new("aded5e82-b909-4619-9949-f5d71dac0bcb");

    private readonly IPowerService _power;

    public InternalPanelService(IPowerService power)
    {
        _power = power;
    }

    /// <summary>Null when the active plan does not expose the brightness setting.</summary>
    public MonitorDevice? ReadPanel()
    {
        var active = ActivePlan();
        if (active is null)
            return null;

        var settings = _power.EnumeratePlanSettings(active.PlanGuid);
        if (!settings.IsSuccess)
            return null;

        var brightness = settings.Value!.FirstOrDefault(s =>
            s.SubgroupGuid == VideoSubgroup && s.SettingGuid == BrightnessSetting);
        if (brightness is null)
            return null;

        // The current charger state decides which index the panel is showing.
        var current = brightness.AcIndex ?? brightness.DcIndex;
        if (current is null)
            return null;

        return new MonitorDevice
        {
            Id = PanelId,
            Name = "Built-in display",
            IsInternalPanel = true,
            SupportsDdc = true, // controls enabled; writes route here, not DDC
            Brightness = (int)current.Value,
            BrightnessMax = (int)Math.Max(1, brightness.Max),
        };
    }

    public OperationResult<bool> SetBrightness(int value)
    {
        var active = ActivePlan();
        if (active is null)
        {
            return OperationResult<bool>.Failure(
                "No active power plan; cannot set panel brightness.", ErrorCategory.ServiceUnavailable);
        }

        var ac = _power.WriteSettingIndex(active.PlanGuid, VideoSubgroup, BrightnessSetting, ac: true, (uint)value);
        if (!ac.IsSuccess)
            return ac;

        return _power.WriteSettingIndex(active.PlanGuid, VideoSubgroup, BrightnessSetting, ac: false, (uint)value);
    }

    private PowerPlanInfo? ActivePlan()
    {
        var plans = _power.EnumeratePlans();
        return plans.IsSuccess ? plans.Value!.FirstOrDefault(p => p.IsActive) : null;
    }
}
