using System.Globalization;
using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Modules.Privacy.Models;

namespace ThisIsMyPC.Modules.Privacy.Services;

public sealed class PrivacySettingsReader
{
    private readonly IRegistryService _registryService;

    public PrivacySettingsReader(IRegistryService registryService)
    {
        _registryService = registryService;
    }

    /// <summary>The single-value toggles, in module display order.</summary>
    public IReadOnlyList<PrivacyPreference> ReadSingles()
    {
        return
        [
            // AllowTelemetry 1 = required diagnostic data only. Policy default is
            // absent (delete to restore). DiagTrack rides along as a companion
            // service via the change factory's enforcement.
            ReadPreference(
                id: "telemetry-level",
                displayName: "Limit diagnostic data to required only",
                description: "Sets the diagnostic data policy to required data and stops the telemetry collection service (DiagTrack). Turning this off restores the policy and re-enables the service.",
                section: PrivacySection.DiagnosticData,
                keyPath: PrivacyRegistryPaths.DataCollectionPoliciesKeyPath,
                valueName: "AllowTelemetry",
                configuredValue: "1",
                defaultValue: ""),

            ReadPreference(
                id: "error-reporting",
                displayName: "Disable Windows Error Reporting",
                description: "Stops crash reports from being sent to Microsoft. Local crash logs in Event Viewer keep working.",
                section: PrivacySection.DiagnosticData,
                keyPath: PrivacyRegistryPaths.ErrorReportingPoliciesKeyPath,
                valueName: "Disabled",
                configuredValue: "1",
                defaultValue: ""),

            ReadPreference(
                id: "location",
                displayName: "Disable location services",
                description: "Turns off location for the whole machine by policy. Find My Device and location-based apps stop working.",
                section: PrivacySection.PermissionsAndTracking,
                keyPath: PrivacyRegistryPaths.LocationPoliciesKeyPath,
                valueName: "DisableLocation",
                configuredValue: "1",
                defaultValue: ""),

            // Start_TrackProgs has a real default (1): restore writes it back.
            ReadPreference(
                id: "app-launch-tracking",
                displayName: "Disable app launch tracking",
                description: "Stops Windows from recording which apps you start to build Start menu and search suggestions.",
                section: PrivacySection.PermissionsAndTracking,
                keyPath: PrivacyRegistryPaths.ExplorerAdvancedKeyPath,
                valueName: "Start_TrackProgs",
                configuredValue: "0",
                defaultValue: "1"),

            ReadPreference(
                id: "handwriting-data-sharing",
                displayName: "Block handwriting data sharing",
                description: "Stops handwriting recognition data from being shared with Microsoft.",
                section: PrivacySection.Personalization,
                keyPath: PrivacyRegistryPaths.TabletPcPoliciesKeyPath,
                valueName: "PreventHandwritingDataSharing",
                configuredValue: "1",
                defaultValue: ""),
        ];
    }

    /// <summary>
    /// The four values behind inking and typing personalization (the Settings toggle
    /// writes them together). Surfaced as ONE toggle (a single atomic group), so not
    /// in ReadSingles. Restore DELETES the values (empty DefaultValue): the as-shipped
    /// state is all four absent, and writing AcceptedPrivacyPolicy=1 back would record
    /// a consent the user never gave. Undo still round-trips exact prior values via
    /// BeforeValue.
    /// </summary>
    public IReadOnlyList<PrivacyPreference> ReadInkingTyping()
    {
        return
        [
            ReadPreference(
                id: "inking-typing",
                displayName: "Ink collection (RestrictImplicitInkCollection)",
                description: "InputPersonalization ink restriction entry.",
                section: PrivacySection.Personalization,
                keyPath: PrivacyRegistryPaths.InputPersonalizationKeyPath,
                valueName: "RestrictImplicitInkCollection",
                configuredValue: "1",
                defaultValue: ""),
            ReadPreference(
                id: "inking-typing",
                displayName: "Text collection (RestrictImplicitTextCollection)",
                description: "InputPersonalization text restriction entry.",
                section: PrivacySection.Personalization,
                keyPath: PrivacyRegistryPaths.InputPersonalizationKeyPath,
                valueName: "RestrictImplicitTextCollection",
                configuredValue: "1",
                defaultValue: ""),
            ReadPreference(
                id: "inking-typing",
                displayName: "Contact harvesting (HarvestContacts)",
                description: "TrainedDataStore contact harvesting entry.",
                section: PrivacySection.Personalization,
                keyPath: PrivacyRegistryPaths.TrainedDataStoreKeyPath,
                valueName: "HarvestContacts",
                configuredValue: "0",
                defaultValue: ""),
            ReadPreference(
                id: "inking-typing",
                displayName: "Personalization consent (AcceptedPrivacyPolicy)",
                description: "Personalization Settings consent entry.",
                section: PrivacySection.Personalization,
                keyPath: PrivacyRegistryPaths.PersonalizationSettingsKeyPath,
                valueName: "AcceptedPrivacyPolicy",
                configuredValue: "0",
                defaultValue: ""),
        ];
    }

    public PrivacyScanData ReadAll() => new(ReadSingles(), ReadInkingTyping());

    private PrivacyPreference ReadPreference(
        string id,
        string displayName,
        string description,
        PrivacySection section,
        string keyPath,
        string valueName,
        string configuredValue,
        string defaultValue)
    {
        // Absent value scans as the preference's default ("" = policy Not configured).
        var read = _registryService.ReadDWord(keyPath, valueName);
        var currentValue = read.IsSuccess
            ? read.Value.ToString(CultureInfo.InvariantCulture)
            : defaultValue;

        return new PrivacyPreference(
            Id: id,
            DisplayName: displayName,
            Description: description,
            Section: section,
            RegistryKeyPath: keyPath,
            RegistryValueName: valueName,
            ValueType: ChangeValueType.Registry_DWord,
            CurrentValue: currentValue,
            ConfiguredValue: configuredValue,
            DefaultValue: defaultValue);
    }
}
