using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Modules.Shell.Models;

namespace ThisIsMyPC.Modules.Shell.Services;

public sealed class NotificationSettingsReader
{
    private const string ContentDeliveryManagerPath = @"HKCU\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager";

    private readonly IRegistryService _registryService;

    public NotificationSettingsReader(IRegistryService registryService)
    {
        _registryService = registryService;
    }

    public IReadOnlyList<NotificationSetting> ReadAll()
    {
        var settings = new List<NotificationSetting>();

        AddContentDeliveryManagerSetting(settings, "tips-suggestions", "Tips and suggestions",
            "Show tips and suggestions when using Windows",
            "SubscribedContent-338389Enabled");

        AddContentDeliveryManagerSetting(settings, "get-started-prompts", "Get started prompts",
            "Show 'Get Started' and welcome experience suggestions",
            "SubscribedContent-310093Enabled");

        AddContentDeliveryManagerSetting(settings, "suggested-in-settings", "Suggested in Settings",
            "Show suggested content in Settings app",
            "SubscribedContent-338393Enabled");

        AddContentDeliveryManagerSetting(settings, "auto-install-apps", "Auto-install suggested apps",
            "Automatically install suggested apps from the Store",
            "SilentInstalledAppsEnabled");

        AddContentDeliveryManagerSetting(settings, "lock-screen-spotlight", "Lock screen spotlight overlay",
            "Show rotating spotlight overlay on lock screen",
            "RotatingLockScreenOverlayEnabled");

        AddContentDeliveryManagerSetting(settings, "lock-screen-tips", "Lock screen tips",
            "Show tips and tricks on the lock screen",
            "SubscribedContent-338387Enabled");

        AddContentDeliveryManagerSetting(settings, "lock-screen-images", "Lock screen rotating images",
            "Show Windows Spotlight images on lock screen",
            "RotatingLockScreenEnabled");

        AddContentDeliveryManagerSetting(settings, "settings-content-1", "Settings promoted content 1",
            "Show promoted content in Windows Settings",
            "SubscribedContent-353694Enabled");

        AddContentDeliveryManagerSetting(settings, "settings-content-2", "Settings promoted content 2",
            "Show additional promoted content in Windows Settings",
            "SubscribedContent-353696Enabled");

        AddContentDeliveryManagerSetting(settings, "oem-preinstalled", "OEM preinstalled apps",
            "Allow OEM preinstalled app suggestions",
            "OemPreInstalledAppsEnabled");

        AddContentDeliveryManagerSetting(settings, "preinstalled-apps", "Preinstalled apps",
            "Allow preinstalled app suggestions",
            "PreInstalledAppsEnabled");

        AddContentDeliveryManagerSetting(settings, "soft-landing-tips", "Software landing tips",
            "Show tips about new software features",
            "SoftLandingEnabled");

        // Additional suppression settings from different registry paths
        AddSetting(settings, "scoobe-system-setting", "Welcome experience suggestions",
            "Show system setting suggestions after updates",
            @"HKCU\Software\Microsoft\Windows\CurrentVersion\UserProfileEngagement",
            "ScoobeSystemSettingEnabled");

        AddSetting(settings, "spotlight-collection-desktop", "Desktop spotlight collection",
            "Show Spotlight collection images on desktop",
            @"HKCU\Software\Policies\Microsoft\Windows\CloudContent",
            "DisableSpotlightCollectionOnDesktop",
            invertLogic: true);

        AddSetting(settings, "dynamic-search-box", "Dynamic search box suggestions",
            "Show dynamic search suggestions in the search bar",
            @"HKCU\Software\Microsoft\Windows\CurrentVersion\SearchSettings",
            "IsDynamicSearchBoxEnabled");

        return settings;
    }

    private void AddContentDeliveryManagerSetting(
        List<NotificationSetting> settings,
        string id,
        string displayName,
        string description,
        string valueName)
    {
        AddSetting(settings, id, displayName, description, ContentDeliveryManagerPath, valueName);
    }

    private void AddSetting(
        List<NotificationSetting> settings,
        string id,
        string displayName,
        string description,
        string keyPath,
        string valueName,
        bool invertLogic = false)
    {
        var result = _registryService.ReadDWord(keyPath, valueName);
        bool isEnabled;
        if (result.IsSuccess)
            isEnabled = invertLogic ? result.Value! == 0 : result.Value! == 1;
        else
            isEnabled = !invertLogic; // default: enabled (or disabled for inverted)

        settings.Add(new NotificationSetting(id, displayName, description, keyPath, valueName, isEnabled));
    }
}
