namespace ThisIsMyPC.Modules.Annoyances;

public static class AnnoyancesRegistryPaths
{
    public const string UserProfileEngagementKeyPath =
        @"HKCU\Software\Microsoft\Windows\CurrentVersion\UserProfileEngagement";

    public const string ContentDeliveryManagerKeyPath =
        @"HKCU\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager";

    public const string SearchKeyPath =
        @"HKCU\Software\Microsoft\Windows\CurrentVersion\Search";

    public const string SearchSettingsKeyPath =
        @"HKCU\Software\Microsoft\Windows\CurrentVersion\SearchSettings";

    public const string ExplorerPoliciesKeyPath =
        @"HKCU\Software\Policies\Microsoft\Windows\Explorer";

    public const string CloudContentUserPoliciesKeyPath =
        @"HKCU\Software\Policies\Microsoft\Windows\CloudContent";

    public const string EdgeUpdatePoliciesKeyPath =
        @"HKLM\SOFTWARE\Policies\Microsoft\EdgeUpdate";

    public const string AdvertisingInfoKeyPath =
        @"HKCU\Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo";

    public const string SystemPoliciesKeyPath =
        @"HKLM\SOFTWARE\Policies\Microsoft\Windows\System";

    public const string GameDvrKeyPath =
        @"HKCU\Software\Microsoft\Windows\CurrentVersion\GameDVR";

    public const string GameBarKeyPath =
        @"HKCU\Software\Microsoft\GameBar";

    public const string GraphicsDriversKeyPath =
        @"HKLM\SYSTEM\CurrentControlSet\Control\GraphicsDrivers";

    public const string StickyKeysKeyPath =
        @"HKCU\Control Panel\Accessibility\StickyKeys";

    public const string KeyboardResponseKeyPath =
        @"HKCU\Control Panel\Accessibility\Keyboard Response";

    public const string ExplorerAdvancedKeyPath =
        @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";

    public const string CopilotMachinePoliciesKeyPath =
        @"HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsCopilot";

    public const string CopilotUserPoliciesKeyPath =
        @"HKCU\Software\Policies\Microsoft\Windows\WindowsCopilot";

    public const string WindowsAiPoliciesKeyPath =
        @"HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsAI";

    public const string EdgePoliciesKeyPath =
        @"HKLM\SOFTWARE\Policies\Microsoft\Edge";

    public static (string KeyPath, string ValueName) ParseSystemLocation(string systemLocation)
    {
        var lastSeparator = systemLocation.LastIndexOf('\\');
        return (systemLocation[..lastSeparator], systemLocation[(lastSeparator + 1)..]);
    }
}
