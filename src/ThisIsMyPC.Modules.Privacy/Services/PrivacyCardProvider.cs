using ThisIsMyPC.Core.Cards;
using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Enforcement;
using ThisIsMyPC.Modules.Privacy.Changes;
using ThisIsMyPC.Modules.Privacy.Models;

namespace ThisIsMyPC.Modules.Privacy.Services;

/// <summary>
/// Produces the module's SettingCardSource list for the host card renderer,
/// following the Annoyances reference consumer. Factories re-read live state at
/// stage time via the supplied reader.
/// </summary>
public sealed class PrivacyCardProvider
{
    private const string InkingTypingDescription =
        "Stops Windows from collecting your handwriting, typing history, and contacts to build a personal dictionary.";

    private static readonly IReadOnlyDictionary<PrivacySection, string> SectionGroups =
        new Dictionary<PrivacySection, string>
        {
            [PrivacySection.DiagnosticData] = "Diagnostic Data",
            [PrivacySection.PermissionsAndTracking] = "Permissions & Tracking",
            [PrivacySection.Personalization] = "Personalization",
        };

    private readonly PrivacySettingsReader _liveReader;

    public PrivacyCardProvider(PrivacySettingsReader liveReader)
    {
        ArgumentNullException.ThrowIfNull(liveReader);
        _liveReader = liveReader;
    }

    public IReadOnlyList<SettingCardSource> BuildCards(PrivacyScanData scanData)
    {
        ArgumentNullException.ThrowIfNull(scanData);
        var cards = new List<SettingCardSource>();

        AddSectionSingles(cards, scanData, PrivacySection.DiagnosticData);
        AddSectionSingles(cards, scanData, PrivacySection.PermissionsAndTracking);
        cards.Add(InkingTypingCard(scanData));
        AddSectionSingles(cards, scanData, PrivacySection.Personalization);

        return cards;
    }

    private void AddSectionSingles(List<SettingCardSource> cards, PrivacyScanData scanData, PrivacySection section)
    {
        foreach (var pref in scanData.Preferences.Where(p => p.Section == section))
            cards.Add(SingleCard(pref));
    }

    private SettingCardSource SingleCard(PrivacyPreference pref)
    {
        // Enforcement metadata depends only on the configure direction, never on live
        // values; derive it from the scan-time preference so BuildCards does no
        // registry reads.
        var scanTimeEnforcement = PrivacyChangeFactory.CreateToggle(pref, configure: true).Enforcement;

        return new SettingCardSource
        {
            Model = new SettingCardModel
            {
                SettingId = pref.Id,
                ModuleId = PrivacyChangeFactory.ModuleId,
                DisplayName = pref.DisplayName,
                Description = pref.Description,
                ControlType = SettingControlType.Toggle,
                CurrentValue = pref.IsConfigured ? "1" : "0",
                CurrentDisplayValue = pref.IsConfigured ? "Configured" : "Windows default",
                RegistryPath = pref.RegistryKeyPath,
                ValueName = pref.RegistryValueName,
                RegistryValueType = pref.ValueType.ToString(),
                GroupId = SectionGroups[pref.Section],
                Enforcement = Profile(scanTimeEnforcement),
                SkuRestriction = scanTimeEnforcement?.SkuRestriction,
            },
            CreateToggleGroup = configure => WrapSingle(
                PrivacyChangeFactory.CreateToggle(ReadLive(pref.Id), configure)),
            ReadCurrentState = () => ReadLive(pref.Id).IsConfigured,
        };
    }

    private SettingCardSource InkingTypingCard(PrivacyScanData scanData) => new()
    {
        Model = new SettingCardModel
        {
            SettingId = "inking-typing",
            ModuleId = PrivacyChangeFactory.ModuleId,
            DisplayName = "Disable inking and typing personalization",
            Description = InkingTypingDescription,
            ControlType = SettingControlType.Toggle,
            CurrentValue = scanData.InkingTyping.All(p => p.IsConfigured) ? "1" : "0",
            CurrentDisplayValue = scanData.InkingTyping.All(p => p.IsConfigured) ? "Configured" : "Windows default",
            RegistryPath = PrivacyRegistryPaths.InputPersonalizationKeyPath,
            ValueName = "RestrictImplicitInkCollection",
            RegistryValueType = nameof(ChangeValueType.Registry_DWord),
            GroupId = SectionGroups[PrivacySection.Personalization],
        },
        CreateToggleGroup = configure => PrivacyChangeFactory.CreateInkingTypingGroup(
            _liveReader.ReadInkingTyping(), configure, InkingTypingDescription),
        ReadCurrentState = () => _liveReader.ReadInkingTyping().All(p => p.IsConfigured),
    };

    /// <summary>
    /// Projects the factory's configure-direction enforcement into the UI-facing
    /// profile (Annoyances pattern). Companion services → Enforced badge; SKU-only
    /// enforcement produces NO profile; SkuRestriction drives its own callout.
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
                ? "Applied with companion service handling"
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

    private PrivacyPreference ReadLive(string id)
        => _liveReader.ReadSingles().Single(p => p.Id == id);
}
