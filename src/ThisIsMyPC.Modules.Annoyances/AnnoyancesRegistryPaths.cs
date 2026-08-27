namespace ThisIsMyPC.Modules.Annoyances;

public static class AnnoyancesRegistryPaths
{
    public const string UserProfileEngagementKeyPath =
        @"HKCU\Software\Microsoft\Windows\CurrentVersion\UserProfileEngagement";

    public const string ContentDeliveryManagerKeyPath =
        @"HKCU\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager";

    public static (string KeyPath, string ValueName) ParseSystemLocation(string systemLocation)
    {
        var lastSeparator = systemLocation.LastIndexOf('\\');
        return (systemLocation[..lastSeparator], systemLocation[(lastSeparator + 1)..]);
    }
}
