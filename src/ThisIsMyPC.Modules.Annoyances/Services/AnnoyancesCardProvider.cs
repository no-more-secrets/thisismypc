using ThisIsMyPC.Core.Cards;
using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Enforcement;
using ThisIsMyPC.Modules.Annoyances.Changes;
using ThisIsMyPC.Modules.Annoyances.Models;

namespace ThisIsMyPC.Modules.Annoyances.Services;

/// <summary>
/// Produces the module's SettingCardSource list for the host card renderer (Epic 10).
/// Mirrors the toggle inventory the pre-card AnnoyancesViewModel built: per-section
/// singles plus the Bing, suggested-content, Copilot-policy, and Recall group toggles,
/// preserving the AiFeatures interleaved ordering. Factories re-read live state at
/// stage time via the supplied reader.
/// </summary>
public sealed class AnnoyancesCardProvider
{
    private const string SuggestedContentDescription =
        "Stops the ad-like \"suggested content\" tiles Microsoft injects into the Settings app. "
        + "The comprehensive privacy suite with companion service management arrives in the Privacy & Telemetry module.";

    private const string LockScreenAdsDescription =
        "Stops \"fun facts, tips, and tricks\" overlays on the lock screen when Windows Spotlight is active "
        + "(two ContentDeliveryManager values, set together like the Settings checkbox does). The Spotlight wallpaper itself keeps working.";

    private const string PreinstalledAppsDescription =
        "Stops Windows from promoting OEM-preinstalled apps and showing \"soft landing\" feature suggestion tips "
        + "(three ContentDeliveryManager values set together).";

    private const string RecallDescription =
        "Blocks Windows Recall from taking and saving screen snapshots and turns off AI analysis of your activity. "
        + "On PCs without Copilot+ hardware these policies are inert today; setting them future-proofs the machine.";

    private static readonly IReadOnlyDictionary<AnnoyanceSection, string> SectionGroups =
        new Dictionary<AnnoyanceSection, string>
        {
            [AnnoyanceSection.ScoobeAndWelcome] = "Nag Screens & Suggestions",
            [AnnoyanceSection.BingAndEdge] = "Bing Search & Edge",
            [AnnoyanceSection.AdvertisingAndTracking] = "Advertising & Tracking",
            [AnnoyanceSection.GamingAndAccessibility] = "Gaming & Accessibility",
            [AnnoyanceSection.AiFeatures] = "AI Features",
        };

    private readonly AnnoyancesSettingsReader _liveReader;

    public AnnoyancesCardProvider(AnnoyancesSettingsReader liveReader)
    {
        ArgumentNullException.ThrowIfNull(liveReader);
        _liveReader = liveReader;
    }

    public IReadOnlyList<SettingCardSource> BuildCards(AnnoyancesScanData scanData)
    {
        ArgumentNullException.ThrowIfNull(scanData);
        var cards = new List<SettingCardSource>();

        AddSectionSingles(cards, scanData, AnnoyanceSection.ScoobeAndWelcome, driftFragile: false);
        cards.Add(LockScreenAdsCard(scanData));
        cards.Add(PreinstalledAppsCard(scanData));

        cards.Add(BingSearchCard(scanData));
        AddSectionSingles(cards, scanData, AnnoyanceSection.BingAndEdge, driftFragile: true);

        AddSectionSingles(cards, scanData, AnnoyanceSection.AdvertisingAndTracking, driftFragile: false);
        cards.Add(SuggestedContentCard(scanData));

        AddSectionSingles(cards, scanData, AnnoyanceSection.GamingAndAccessibility, driftFragile: false);

        // AiFeatures ordering interleaves singles with groups: Copilot policy,
        // copilot-button, Recall, edge-sidebar.
        cards.Add(CopilotPolicyCard(scanData));
        cards.Add(SingleCard(scanData.Preferences.Single(p => p.Id == "copilot-button"), driftFragile: false));
        cards.Add(RecallCard(scanData));
        cards.Add(SingleCard(scanData.Preferences.Single(p => p.Id == "edge-sidebar"), driftFragile: false));

        return cards;
    }

    private void AddSectionSingles(
        List<SettingCardSource> cards, AnnoyancesScanData scanData, AnnoyanceSection section, bool driftFragile)
    {
        foreach (var pref in scanData.Preferences.Where(p => p.Section == section))
            cards.Add(SingleCard(pref, driftFragile));
    }

    private SettingCardSource SingleCard(AnnoyancePreference pref, bool driftFragile)
    {
        Func<bool, ChangeDescriptor> factory = driftFragile
            ? suppress => AnnoyanceChangeFactory.CreateDriftFragileToggle(ReadLive(pref.Id), suppress)
            : suppress => AnnoyanceChangeFactory.CreateToggle(ReadLive(pref.Id), suppress);

        // Enforcement metadata depends only on the suppress direction, never on live
        // values — derive it from the scan-time preference so BuildCards does no
        // registry reads, and unconditionally so a factory gaining enforcement later
        // can never silently lose its badge.
        var scanTimeEnforcement = driftFragile
            ? AnnoyanceChangeFactory.CreateDriftFragileToggle(pref, suppress: true).Enforcement
            : AnnoyanceChangeFactory.CreateToggle(pref, suppress: true).Enforcement;

        return new SettingCardSource
        {
            Model = new SettingCardModel
            {
                SettingId = pref.Id,
                ModuleId = AnnoyanceChangeFactory.ModuleId,
                DisplayName = pref.DisplayName,
                Description = pref.Description,
                ControlType = SettingControlType.Toggle,
                CurrentValue = pref.IsSuppressed ? "1" : "0",
                CurrentDisplayValue = pref.IsSuppressed ? "Suppressed" : "Windows default",
                RegistryPath = pref.RegistryKeyPath,
                ValueName = pref.RegistryValueName,
                RegistryValueType = pref.ValueType.ToString(),
                GroupId = SectionGroups[pref.Section],
                Enforcement = Profile(scanTimeEnforcement),
                SkuRestriction = scanTimeEnforcement?.SkuRestriction,
            },
            CreateToggleGroup = suppress => WrapSingle(factory(suppress)),
            ReadCurrentState = () => ReadLive(pref.Id).IsSuppressed,
        };
    }

    private SettingCardSource LockScreenAdsCard(AnnoyancesScanData scanData) => new()
    {
        Model = new SettingCardModel
        {
            SettingId = "lock-screen-ads",
            ModuleId = AnnoyanceChangeFactory.ModuleId,
            DisplayName = "Suppress lock screen tips and ads",
            Description = LockScreenAdsDescription,
            ControlType = SettingControlType.Toggle,
            CurrentValue = scanData.LockScreenAds.All(p => p.IsSuppressed) ? "1" : "0",
            CurrentDisplayValue = scanData.LockScreenAds.All(p => p.IsSuppressed) ? "Suppressed" : "Windows default",
            RegistryPath = AnnoyancesRegistryPaths.ContentDeliveryManagerKeyPath,
            ValueName = "RotatingLockScreenOverlayEnabled",
            RegistryValueType = nameof(ChangeValueType.Registry_DWord),
            GroupId = SectionGroups[AnnoyanceSection.ScoobeAndWelcome],
        },
        CreateToggleGroup = suppress => AnnoyanceChangeFactory.CreateGroupToggle(
            _liveReader.ReadLockScreenAds(),
            settingId: "lock-screen-ads",
            displayName: "Lock screen tips and ads",
            description: LockScreenAdsDescription,
            suppress),
        ReadCurrentState = () => _liveReader.ReadLockScreenAds().All(p => p.IsSuppressed),
    };

    private SettingCardSource PreinstalledAppsCard(AnnoyancesScanData scanData) => new()
    {
        Model = new SettingCardModel
        {
            SettingId = "preinstalled-apps",
            ModuleId = AnnoyanceChangeFactory.ModuleId,
            DisplayName = "Suppress OEM and preinstalled app promotions",
            Description = PreinstalledAppsDescription,
            ControlType = SettingControlType.Toggle,
            CurrentValue = scanData.PreinstalledApps.All(p => p.IsSuppressed) ? "1" : "0",
            CurrentDisplayValue = scanData.PreinstalledApps.All(p => p.IsSuppressed) ? "Suppressed" : "Windows default",
            RegistryPath = AnnoyancesRegistryPaths.ContentDeliveryManagerKeyPath,
            ValueName = "OemPreInstalledAppsEnabled",
            RegistryValueType = nameof(ChangeValueType.Registry_DWord),
            GroupId = SectionGroups[AnnoyanceSection.ScoobeAndWelcome],
        },
        CreateToggleGroup = suppress => AnnoyanceChangeFactory.CreateGroupToggle(
            _liveReader.ReadPreinstalledApps(),
            settingId: "preinstalled-apps",
            displayName: "OEM and preinstalled app promotions",
            description: PreinstalledAppsDescription,
            suppress),
        ReadCurrentState = () => _liveReader.ReadPreinstalledApps().All(p => p.IsSuppressed),
    };

    private SettingCardSource BingSearchCard(AnnoyancesScanData scanData) => new()
    {
        Model = new SettingCardModel
        {
            SettingId = "bing-search",
            ModuleId = AnnoyanceChangeFactory.ModuleId,
            DisplayName = "Disable Bing web search in Start Menu",
            Description = "Start Menu search stops sending your queries to Bing and shows local results only. "
                + "Windows Update and Web Experience Pack deployments are known to revert this (requires Explorer restart).",
            ControlType = SettingControlType.Toggle,
            CurrentValue = scanData.BingSearch.IsSuppressed ? "1" : "0",
            CurrentDisplayValue = scanData.BingSearch.IsSuppressed ? "Suppressed" : "Windows default",
            RegistryPath = AnnoyancesRegistryPaths.SearchKeyPath,
            ValueName = "BingSearchEnabled",
            RegistryValueType = nameof(ChangeValueType.Registry_DWord),
            GroupId = SectionGroups[AnnoyanceSection.BingAndEdge],
            Enforcement = Profile(
                AnnoyanceChangeFactory.CreateBingSearchToggle(scanData.BingSearch, suppress: true)
                    .Changes[0].Enforcement),
        },
        CreateToggleGroup = suppress =>
            AnnoyanceChangeFactory.CreateBingSearchToggle(_liveReader.ReadBingSearch(), suppress),
        ReadCurrentState = () => _liveReader.ReadBingSearch().IsSuppressed,
    };

    private SettingCardSource SuggestedContentCard(AnnoyancesScanData scanData) => new()
    {
        Model = new SettingCardModel
        {
            SettingId = "settings-suggested-content",
            ModuleId = AnnoyanceChangeFactory.ModuleId,
            DisplayName = "Suppress suggested content in Settings",
            Description = SuggestedContentDescription,
            ControlType = SettingControlType.Toggle,
            CurrentValue = scanData.SettingsSuggestedContent.All(p => p.IsSuppressed) ? "1" : "0",
            CurrentDisplayValue = scanData.SettingsSuggestedContent.All(p => p.IsSuppressed) ? "Suppressed" : "Windows default",
            RegistryPath = AnnoyancesRegistryPaths.ContentDeliveryManagerKeyPath,
            ValueName = "SubscribedContent-338393Enabled",
            RegistryValueType = nameof(ChangeValueType.Registry_DWord),
            GroupId = SectionGroups[AnnoyanceSection.AdvertisingAndTracking],
        },
        CreateToggleGroup = suppress => AnnoyanceChangeFactory.CreateGroupToggle(
            _liveReader.ReadSettingsSuggestedContent(),
            settingId: "settings-suggested-content",
            displayName: "Suggested content in Settings",
            description: SuggestedContentDescription,
            suppress),
        ReadCurrentState = () => _liveReader.ReadSettingsSuggestedContent().All(p => p.IsSuppressed),
    };

    private SettingCardSource CopilotPolicyCard(AnnoyancesScanData scanData) => new()
    {
        Model = new SettingCardModel
        {
            SettingId = "copilot",
            ModuleId = AnnoyanceChangeFactory.ModuleId,
            DisplayName = "Disable Windows Copilot",
            Description = "Turns the Windows Copilot assistant off by policy, in both machine and user scope. "
                + "Windows feature updates and Copilot app deployments are known to bring Copilot surfaces back (requires Explorer restart).",
            ControlType = SettingControlType.Toggle,
            CurrentValue = scanData.CopilotPolicy.All(p => p.IsSuppressed) ? "1" : "0",
            CurrentDisplayValue = scanData.CopilotPolicy.All(p => p.IsSuppressed) ? "Suppressed" : "Windows default",
            RegistryPath = AnnoyancesRegistryPaths.CopilotMachinePoliciesKeyPath,
            ValueName = "TurnOffWindowsCopilot",
            RegistryValueType = nameof(ChangeValueType.Registry_DWord),
            GroupId = SectionGroups[AnnoyanceSection.AiFeatures],
            Enforcement = Profile(
                AnnoyanceChangeFactory.CreateCopilotPolicyToggle(scanData.CopilotPolicy, suppress: true)
                    .Changes[0].Enforcement),
            SkuRestriction = AnnoyanceChangeFactory
                .CreateCopilotPolicyToggle(scanData.CopilotPolicy, suppress: true)
                .Changes[0].Enforcement?.SkuRestriction,
        },
        CreateToggleGroup = suppress =>
            AnnoyanceChangeFactory.CreateCopilotPolicyToggle(_liveReader.ReadCopilotPolicy(), suppress),
        ReadCurrentState = () => _liveReader.ReadCopilotPolicy().All(p => p.IsSuppressed),
    };

    private SettingCardSource RecallCard(AnnoyancesScanData scanData) => new()
    {
        Model = new SettingCardModel
        {
            SettingId = "recall",
            ModuleId = AnnoyanceChangeFactory.ModuleId,
            DisplayName = "Disable Windows Recall and AI data analysis",
            Description = RecallDescription,
            ControlType = SettingControlType.Toggle,
            CurrentValue = scanData.Recall.All(p => p.IsSuppressed) ? "1" : "0",
            CurrentDisplayValue = scanData.Recall.All(p => p.IsSuppressed) ? "Suppressed" : "Windows default",
            RegistryPath = AnnoyancesRegistryPaths.WindowsAiPoliciesKeyPath,
            ValueName = "AllowRecallEnablement",
            RegistryValueType = nameof(ChangeValueType.Registry_DWord),
            GroupId = SectionGroups[AnnoyanceSection.AiFeatures],
            SkuRestriction = AnnoyanceChangeFactory
                .CreateRecallPolicyToggle(scanData.Recall, suppress: true, RecallDescription)
                .Changes[0].Enforcement?.SkuRestriction,
        },
        CreateToggleGroup = suppress => AnnoyanceChangeFactory.CreateRecallPolicyToggle(
            _liveReader.ReadRecall(), suppress, RecallDescription),
        ReadCurrentState = () => _liveReader.ReadRecall().All(p => p.IsSuppressed),
    };

    /// <summary>
    /// Projects the factory's suppress-direction enforcement into the UI-facing profile.
    /// The card layer gets posture + reversion risks, never enforcement internals.
    /// Reversion-vector-only enforcement maps to Simple (informational), not Enforced.
    /// SKU-only enforcement produces NO profile — SkuRestriction drives its own callout
    /// and a "known to revert" badge would be false.
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
                ? "Applied with companion service/task handling"
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

    private AnnoyancePreference ReadLive(string id)
        => _liveReader.ReadAll().Single(p => p.Id == id);
}
