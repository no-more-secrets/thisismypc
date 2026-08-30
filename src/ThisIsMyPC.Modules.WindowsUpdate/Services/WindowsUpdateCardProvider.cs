using ThisIsMyPC.Core.Cards;
using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Enforcement;
using ThisIsMyPC.Modules.WindowsUpdate.Changes;
using ThisIsMyPC.Modules.WindowsUpdate.Models;

namespace ThisIsMyPC.Modules.WindowsUpdate.Services;

/// <summary>
/// Produces the module's SettingCardSource list for the host card renderer (Epic 10),
/// following the Annoyances reference consumer. Factories re-read live state at stage
/// time via the supplied reader; the version-pin card is omitted entirely when the
/// machine's DisplayVersion is unreadable.
/// </summary>
public sealed class WindowsUpdateCardProvider
{
    private const string UpdateBehaviorGroup = "Update Behavior";
    private const string DeliveryOptimizationGroup = "Delivery Optimization";
    private const string UpdateExperienceGroup = "Update Experience";

    private readonly WindowsUpdateSettingsReader _liveReader;

    public WindowsUpdateCardProvider(WindowsUpdateSettingsReader liveReader)
    {
        ArgumentNullException.ThrowIfNull(liveReader);
        _liveReader = liveReader;
    }

    public IReadOnlyList<SettingCardSource> BuildCards(WindowsUpdateScanData scanData)
    {
        ArgumentNullException.ThrowIfNull(scanData);
        var cards = new List<SettingCardSource>();

        if (scanData.VersionPin.Count > 0)
            cards.Add(VersionPinCard(scanData.VersionPin));

        foreach (var setting in scanData.Settings.Where(s => s.Id != "delivery-optimization"))
            cards.Add(SingleCard(setting, UpdateBehaviorGroup, gpCache: true));

        foreach (var setting in scanData.Settings.Where(s => s.Id == "delivery-optimization"))
            cards.Add(SingleCard(setting, DeliveryOptimizationGroup, gpCache: false));

        foreach (var setting in scanData.UxSettings)
            cards.Add(UxCard(setting));

        return cards;
    }

    private SettingCardSource UxCard(UpdatePolicySetting setting) => new()
    {
        Model = new SettingCardModel
        {
            SettingId = setting.Id,
            ModuleId = WindowsUpdateChangeFactory.ModuleId,
            DisplayName = setting.DisplayName,
            Description = setting.Description,
            ControlType = SettingControlType.Toggle,
            CurrentValue = setting.IsConfigured ? "1" : "0",
            CurrentDisplayValue = setting.IsConfigured ? "Configured" : "Not configured",
            RegistryPath = setting.RegistryKeyPath,
            ValueName = setting.RegistryValueName,
            RegistryValueType = setting.ValueType.ToString(),
            GroupId = UpdateExperienceGroup,
        },
        CreateToggleGroup = configure => WrapSingle(
            WindowsUpdateChangeFactory.CreateUxToggle(ReadLiveUx(setting.Id), configure)),
        ReadCurrentState = () => ReadLiveUx(setting.Id).IsConfigured,
    };

    private SettingCardSource SingleCard(UpdatePolicySetting setting, string group, bool gpCache)
    {
        var scanTimeEnforcement = WindowsUpdateChangeFactory
            .CreateToggle(setting, configure: true, gpCache).Enforcement;

        return new SettingCardSource
        {
            Model = new SettingCardModel
            {
                SettingId = setting.Id,
                ModuleId = WindowsUpdateChangeFactory.ModuleId,
                DisplayName = setting.DisplayName,
                Description = setting.Description,
                ControlType = SettingControlType.Toggle,
                CurrentValue = setting.IsConfigured ? "1" : "0",
                CurrentDisplayValue = setting.IsConfigured ? "Configured" : "Not configured",
                RegistryPath = setting.RegistryKeyPath,
                ValueName = setting.RegistryValueName,
                RegistryValueType = setting.ValueType.ToString(),
                GroupId = group,
                Enforcement = Profile(scanTimeEnforcement),
                SkuRestriction = scanTimeEnforcement?.SkuRestriction,
            },
            CreateToggleGroup = configure => WrapSingle(
                WindowsUpdateChangeFactory.CreateToggle(ReadLive(setting.Id), configure, gpCache)),
            ReadCurrentState = () => ReadLive(setting.Id).IsConfigured,
        };
    }

    private SettingCardSource VersionPinCard(IReadOnlyList<UpdatePolicySetting> versionPin) => new()
    {
        Model = new SettingCardModel
        {
            SettingId = "version-pin",
            ModuleId = WindowsUpdateChangeFactory.ModuleId,
            DisplayName = versionPin[0].DisplayName,
            Description = versionPin[0].Description,
            ControlType = SettingControlType.Toggle,
            CurrentValue = versionPin.All(s => s.IsConfigured) ? "1" : "0",
            CurrentDisplayValue = versionPin.All(s => s.IsConfigured) ? "Configured" : "Not configured",
            RegistryPath = WindowsUpdateRegistryPaths.WindowsUpdatePoliciesKeyPath,
            ValueName = "TargetReleaseVersion",
            RegistryValueType = nameof(ChangeValueType.Registry_DWord),
            GroupId = UpdateBehaviorGroup,
            Enforcement = Profile(WindowsUpdateChangeFactory.WUPolicyEnforcement),
            SkuRestriction = WindowsUpdateChangeFactory.WUPolicyEnforcement.SkuRestriction,
        },
        // The live re-read can lose DisplayVersion between scan and stage; fall back to
        // the scan-time pin rather than staging nothing.
        CreateToggleGroup = configure =>
        {
            var live = _liveReader.ReadVersionPin();
            return WindowsUpdateChangeFactory.CreateVersionPinGroup(
                live.Count > 0 ? live : versionPin, configure)!;
        },
        ReadCurrentState = () =>
        {
            var live = _liveReader.ReadVersionPin();
            return live.Count > 0 && live.All(s => s.IsConfigured);
        },
    };

    /// <summary>
    /// Projects the factory's enforcement into the UI-facing profile (Annoyances
    /// pattern): GPCache/companions → Enforced badge, vectors-only → Simple.
    /// SKU-only enforcement produces NO profile; the SkuRestriction model field drives
    /// its own callout, and a "known to revert" badge would be false.
    /// </summary>
    private static EnforcementProfile? Profile(SettingEnforcement? enforcement)
    {
        if (enforcement is null)
            return null;

        var hasCompanions = enforcement.CompanionServices is { Count: > 0 }
            || enforcement.CompanionTasks is { Count: > 0 }
            || enforcement.GPCacheEntries is { Count: > 0 };

        if (!hasCompanions && !enforcement.OwnerModeRequired
            && enforcement.ReversionVectors is not { Count: > 0 })
        {
            return null;
        }

        return new EnforcementProfile
        {
            Level = enforcement.OwnerModeRequired
                ? EnforcementLevel.OwnerRequired
                : hasCompanions ? EnforcementLevel.Enforced : EnforcementLevel.Simple,
            Summary = hasCompanions
                ? "Applied with the Update Orchestrator's policy cache cleared"
                : "Windows is known to revert this setting",
            ReversionRisks = enforcement.ReversionVectors,
        };
    }

    private static ChangeGroup WrapSingle(ChangeDescriptor change) => new()
    {
        GroupId = Guid.NewGuid().ToString("N"),
        DisplayName = change.DisplayName,
        Description = change.DisplayName,
        Changes = [change],
    };

    private UpdatePolicySetting ReadLive(string id)
        => _liveReader.ReadSingles().Single(s => s.Id == id);

    private UpdatePolicySetting ReadLiveUx(string id)
        => _liveReader.ReadUxSettings().Single(s => s.Id == id);
}
