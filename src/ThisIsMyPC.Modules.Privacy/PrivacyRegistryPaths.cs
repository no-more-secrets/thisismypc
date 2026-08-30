namespace ThisIsMyPC.Modules.Privacy;

public static class PrivacyRegistryPaths
{
    public const string DataCollectionPoliciesKeyPath =
        @"HKLM\SOFTWARE\Policies\Microsoft\Windows\DataCollection";

    public const string ErrorReportingPoliciesKeyPath =
        @"HKLM\SOFTWARE\Policies\Microsoft\Windows\Windows Error Reporting";

    public const string LocationPoliciesKeyPath =
        @"HKLM\SOFTWARE\Policies\Microsoft\Windows\LocationAndSensors";

    public const string TabletPcPoliciesKeyPath =
        @"HKLM\SOFTWARE\Policies\Microsoft\Windows\TabletPC";

    public const string SystemPoliciesKeyPath =
        @"HKLM\SOFTWARE\Policies\Microsoft\Windows\System";

    public const string OnlineSpeechKeyPath =
        @"HKCU\Software\Microsoft\Speech_OneCore\Settings\OnlineSpeechPrivacy";

    public const string ExplorerAdvancedKeyPath =
        @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";

    public const string InputPersonalizationKeyPath =
        @"HKCU\Software\Microsoft\InputPersonalization";

    public const string TrainedDataStoreKeyPath =
        @"HKCU\Software\Microsoft\InputPersonalization\TrainedDataStore";

    public const string PersonalizationSettingsKeyPath =
        @"HKCU\Software\Microsoft\Personalization\Settings";

    public static (string KeyPath, string ValueName) ParseSystemLocation(string systemLocation)
    {
        var lastSeparator = systemLocation.LastIndexOf('\\');
        return (systemLocation[..lastSeparator], systemLocation[(lastSeparator + 1)..]);
    }
}
