namespace ThisIsMyPC.Modules.Annoyances;

public static class AnnoyancesRegistryPaths
{
    public const string UserProfileEngagementKeyPath =
        @"HKCU\Software\Microsoft\Windows\CurrentVersion\UserProfileEngagement";

    public const string ContentDeliveryManagerKeyPath =
        @"HKCU\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager";

    public const string SearchKeyPath =
        @"HKCU\Software\Microsoft\Windows\CurrentVersion\Search";

    public const string ExplorerPoliciesKeyPath =
        @"HKCU\Software\Policies\Microsoft\Windows\Explorer";

    public const string EdgeUpdatePoliciesKeyPath =
        @"HKLM\SOFTWARE\Policies\Microsoft\EdgeUpdate";

    public const string AdvertisingInfoKeyPath =
        @"HKCU\Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo";

    public const string SystemPoliciesKeyPath =
        @"HKLM\SOFTWARE\Policies\Microsoft\Windows\System";

    public static (string KeyPath, string ValueName) ParseSystemLocation(string systemLocation)
    {
        var lastSeparator = systemLocation.LastIndexOf('\\');
        return (systemLocation[..lastSeparator], systemLocation[(lastSeparator + 1)..]);
    }
}
