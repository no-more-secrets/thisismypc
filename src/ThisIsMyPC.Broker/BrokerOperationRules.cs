using System.Globalization;
using ThisIsMyPC.Core.Actions;
using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Enforcement;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Modules.Power.Actions;
using ThisIsMyPC.Modules.Power.Changes;
using ThisIsMyPC.Modules.Power.Models;
using ThisIsMyPC.Modules.Annoyances;
using ThisIsMyPC.Modules.Privacy;
using ThisIsMyPC.Modules.Shell;
using ThisIsMyPC.Modules.Shell.Changes;
using ThisIsMyPC.Modules.Shell.Models;
using ThisIsMyPC.Modules.Shell.Services;
using ThisIsMyPC.Modules.Software.Actions;
using ThisIsMyPC.Modules.Software.Services;
using ThisIsMyPC.Modules.Startup.Changes;
using ThisIsMyPC.Modules.Startup.Services;
using ThisIsMyPC.Modules.WindowsUpdate;

namespace ThisIsMyPC.Broker;

/// <summary>
/// Limits broker input to operations implemented by the named module. This is
/// independent from UI labels because the UI process is outside the trust boundary.
/// </summary>
internal static class BrokerOperationRules
{
    private static readonly HashSet<string> AnnoyanceTargets = RegistryTargets(
    [
        ("scoobe-nags", AnnoyancesRegistryPaths.UserProfileEngagementKeyPath, "ScoobeSystemSettingEnabled", ChangeValueType.Registry_DWord),
        ("welcome-experience", AnnoyancesRegistryPaths.ContentDeliveryManagerKeyPath, "SubscribedContent-310093Enabled", ChangeValueType.Registry_DWord),
        ("app-suggestions", AnnoyancesRegistryPaths.ContentDeliveryManagerKeyPath, "SubscribedContent-338388Enabled", ChangeValueType.Registry_DWord),
        ("windows-tips", AnnoyancesRegistryPaths.ContentDeliveryManagerKeyPath, "SubscribedContent-338389Enabled", ChangeValueType.Registry_DWord),
        ("settings-suggestions", AnnoyancesRegistryPaths.ContentDeliveryManagerKeyPath, "SystemPaneSuggestionsEnabled", ChangeValueType.Registry_DWord),
        ("lock-screen-images", AnnoyancesRegistryPaths.ContentDeliveryManagerKeyPath, "RotatingLockScreenEnabled", ChangeValueType.Registry_DWord),
        ("spotlight-collection-desktop", AnnoyancesRegistryPaths.CloudContentUserPoliciesKeyPath, "DisableSpotlightCollectionOnDesktop", ChangeValueType.Registry_DWord),
        ("consumer-features", AnnoyancesRegistryPaths.CloudContentMachinePoliciesKeyPath, "DisableWindowsConsumerFeatures", ChangeValueType.Registry_DWord),
        ("silent-app-installs", AnnoyancesRegistryPaths.ContentDeliveryManagerKeyPath, "SilentInstalledAppsEnabled", ChangeValueType.Registry_DWord),
        ("edge-shortcuts", AnnoyancesRegistryPaths.EdgeUpdatePoliciesKeyPath, "CreateDesktopShortcutDefault", ChangeValueType.Registry_DWord),
        ("dynamic-search-box", AnnoyancesRegistryPaths.SearchSettingsKeyPath, "IsDynamicSearchBoxEnabled", ChangeValueType.Registry_DWord),
        ("advertising-id", AnnoyancesRegistryPaths.AdvertisingInfoKeyPath, "Enabled", ChangeValueType.Registry_DWord),
        ("tailored-experiences", AnnoyancesRegistryPaths.PrivacyKeyPath, "TailoredExperiencesWithDiagnosticDataEnabled", ChangeValueType.Registry_DWord),
        ("language-list-access", AnnoyancesRegistryPaths.InternationalUserProfileKeyPath, "HttpAcceptLanguageOptOut", ChangeValueType.Registry_DWord),
        ("feedback-frequency", AnnoyancesRegistryPaths.SiufRulesKeyPath, "NumberOfSIUFInPeriod", ChangeValueType.Registry_DWord),
        ("game-dvr", AnnoyancesRegistryPaths.GameDvrKeyPath, "AppCaptureEnabled", ChangeValueType.Registry_DWord),
        ("auto-game-mode", AnnoyancesRegistryPaths.GameBarKeyPath, "AutoGameModeEnabled", ChangeValueType.Registry_DWord),
        ("xbox-game-tips", AnnoyancesRegistryPaths.GameBarKeyPath, "ShowStartupPanel", ChangeValueType.Registry_DWord),
        ("hags", AnnoyancesRegistryPaths.GraphicsDriversKeyPath, "HwSchMode", ChangeValueType.Registry_DWord),
        ("sticky-keys-shortcut", AnnoyancesRegistryPaths.StickyKeysKeyPath, "Flags", ChangeValueType.Registry_String),
        ("filter-keys-shortcut", AnnoyancesRegistryPaths.KeyboardResponseKeyPath, "Flags", ChangeValueType.Registry_String),
        ("copilot-button", AnnoyancesRegistryPaths.ExplorerAdvancedKeyPath, "ShowCopilotButton", ChangeValueType.Registry_DWord),
        ("edge-sidebar", AnnoyancesRegistryPaths.EdgePoliciesKeyPath, "HubsSidebarEnabled", ChangeValueType.Registry_DWord),
        ("copilot", AnnoyancesRegistryPaths.CopilotMachinePoliciesKeyPath, "TurnOffWindowsCopilot", ChangeValueType.Registry_DWord),
        ("copilot", AnnoyancesRegistryPaths.CopilotUserPoliciesKeyPath, "TurnOffWindowsCopilot", ChangeValueType.Registry_DWord),
        ("recall", AnnoyancesRegistryPaths.WindowsAiPoliciesKeyPath, "AllowRecallEnablement", ChangeValueType.Registry_DWord),
        ("recall", AnnoyancesRegistryPaths.WindowsAiPoliciesKeyPath, "DisableAIDataAnalysis", ChangeValueType.Registry_DWord),
        ("recall", AnnoyancesRegistryPaths.WindowsAiPoliciesKeyPath, "TurnOffSavingSnapshots", ChangeValueType.Registry_DWord),
        ("settings-suggested-content", AnnoyancesRegistryPaths.ContentDeliveryManagerKeyPath, "SubscribedContent-338393Enabled", ChangeValueType.Registry_DWord),
        ("settings-suggested-content", AnnoyancesRegistryPaths.ContentDeliveryManagerKeyPath, "SubscribedContent-353694Enabled", ChangeValueType.Registry_DWord),
        ("settings-suggested-content", AnnoyancesRegistryPaths.ContentDeliveryManagerKeyPath, "SubscribedContent-353696Enabled", ChangeValueType.Registry_DWord),
        ("lock-screen-ads", AnnoyancesRegistryPaths.ContentDeliveryManagerKeyPath, "RotatingLockScreenOverlayEnabled", ChangeValueType.Registry_DWord),
        ("lock-screen-ads", AnnoyancesRegistryPaths.ContentDeliveryManagerKeyPath, "SubscribedContent-338387Enabled", ChangeValueType.Registry_DWord),
        ("preinstalled-apps", AnnoyancesRegistryPaths.ContentDeliveryManagerKeyPath, "OemPreInstalledAppsEnabled", ChangeValueType.Registry_DWord),
        ("preinstalled-apps", AnnoyancesRegistryPaths.ContentDeliveryManagerKeyPath, "PreInstalledAppsEnabled", ChangeValueType.Registry_DWord),
        ("preinstalled-apps", AnnoyancesRegistryPaths.ContentDeliveryManagerKeyPath, "SoftLandingEnabled", ChangeValueType.Registry_DWord),
        ("activity-history", AnnoyancesRegistryPaths.SystemPoliciesKeyPath, "EnableActivityFeed", ChangeValueType.Registry_DWord),
        ("activity-history", AnnoyancesRegistryPaths.SystemPoliciesKeyPath, "PublishUserActivities", ChangeValueType.Registry_DWord),
        ("activity-history", AnnoyancesRegistryPaths.SystemPoliciesKeyPath, "UploadUserActivities", ChangeValueType.Registry_DWord),
        ("edge-debloat", AnnoyancesRegistryPaths.EdgePoliciesKeyPath, "EdgeShoppingAssistantEnabled", ChangeValueType.Registry_DWord),
        ("edge-debloat", AnnoyancesRegistryPaths.EdgePoliciesKeyPath, "ShowMicrosoftRewards", ChangeValueType.Registry_DWord),
        ("edge-debloat", AnnoyancesRegistryPaths.EdgePoliciesKeyPath, "PersonalizationReportingEnabled", ChangeValueType.Registry_DWord),
        ("bing-search", AnnoyancesRegistryPaths.SearchKeyPath, "BingSearchEnabled", ChangeValueType.Registry_DWord),
        ("bing-search", AnnoyancesRegistryPaths.ExplorerPoliciesKeyPath, "DisableSearchBoxSuggestions", ChangeValueType.Registry_DWord),
    ]);

    private static readonly HashSet<string> PrivacyTargets = RegistryTargets(
    [
        ("telemetry-level", PrivacyRegistryPaths.DataCollectionPoliciesKeyPath, "AllowTelemetry", ChangeValueType.Registry_DWord),
        ("error-reporting", PrivacyRegistryPaths.ErrorReportingPoliciesKeyPath, "Disabled", ChangeValueType.Registry_DWord),
        ("location", PrivacyRegistryPaths.LocationPoliciesKeyPath, "DisableLocation", ChangeValueType.Registry_DWord),
        ("app-launch-tracking", PrivacyRegistryPaths.ExplorerAdvancedKeyPath, "Start_TrackProgs", ChangeValueType.Registry_DWord),
        ("cross-device-clipboard", PrivacyRegistryPaths.SystemPoliciesKeyPath, "AllowCrossDeviceClipboard", ChangeValueType.Registry_DWord),
        ("online-speech", PrivacyRegistryPaths.OnlineSpeechKeyPath, "HasAccepted", ChangeValueType.Registry_DWord),
        ("handwriting-data-sharing", PrivacyRegistryPaths.TabletPcPoliciesKeyPath, "PreventHandwritingDataSharing", ChangeValueType.Registry_DWord),
        ("inking-typing", PrivacyRegistryPaths.InputPersonalizationKeyPath, "RestrictImplicitInkCollection", ChangeValueType.Registry_DWord),
        ("inking-typing", PrivacyRegistryPaths.InputPersonalizationKeyPath, "RestrictImplicitTextCollection", ChangeValueType.Registry_DWord),
        ("inking-typing", PrivacyRegistryPaths.TrainedDataStoreKeyPath, "HarvestContacts", ChangeValueType.Registry_DWord),
        ("inking-typing", PrivacyRegistryPaths.PersonalizationSettingsKeyPath, "AcceptedPrivacyPolicy", ChangeValueType.Registry_DWord),
    ]);

    private static readonly HashSet<string> WindowsUpdateTargets = RegistryTargets(
    [
        ("auto-update-mode", WindowsUpdateRegistryPaths.AuPoliciesKeyPath, "AUOptions", ChangeValueType.Registry_DWord),
        ("no-auto-reboot", WindowsUpdateRegistryPaths.AuPoliciesKeyPath, "NoAutoRebootWithLoggedOnUsers", ChangeValueType.Registry_DWord),
        ("exclude-drivers", WindowsUpdateRegistryPaths.WindowsUpdatePoliciesKeyPath, "ExcludeWUDriversInQualityUpdate", ChangeValueType.Registry_DWord),
        ("delivery-optimization", WindowsUpdateRegistryPaths.DeliveryOptimizationPoliciesKeyPath, "DODownloadMode", ChangeValueType.Registry_DWord),
        ("version-pin", WindowsUpdateRegistryPaths.WindowsUpdatePoliciesKeyPath, "TargetReleaseVersion", ChangeValueType.Registry_DWord),
        ("version-pin", WindowsUpdateRegistryPaths.WindowsUpdatePoliciesKeyPath, "ProductVersion", ChangeValueType.Registry_String),
        ("version-pin", WindowsUpdateRegistryPaths.WindowsUpdatePoliciesKeyPath, "TargetReleaseVersionInfo", ChangeValueType.Registry_String),
        ("restart-notifications", WindowsUpdateRegistryPaths.UxSettingsKeyPath, "RestartNotificationsAllowed2", ChangeValueType.Registry_DWord),
        ("active-hours-manual", WindowsUpdateRegistryPaths.UxSettingsKeyPath, "SmartActiveHoursState", ChangeValueType.Registry_DWord),
        ("continuous-innovation", WindowsUpdateRegistryPaths.UxSettingsKeyPath, "IsContinuousInnovationOptedIn", ChangeValueType.Registry_DWord),
    ]);

    private static readonly HashSet<string> ExplorerLocations = BuildExplorerLocations();

    internal static bool Allows(ChangeDescriptor change) =>
        Enum.IsDefined(change.ValueType)
        && Enum.IsDefined(change.Category)
        && Enum.IsDefined(change.RestartRequirement)
        && ValidValues(change)
        && ValidEnforcement(change.Enforcement)
        && change.ModuleId switch
        {
            "Explorer" => AllowsExplorer(change),
            "Context Menus" => AllowsContextMenu(change),
            "Environment" => AllowsEnvironment(change),
            "Startup & Services" => AllowsStartup(change),
            "Windows Annoyances" => AllowsRegistryModule(change, AnnoyanceTargets),
            "Privacy & Telemetry" => AllowsRegistryModule(change, PrivacyTargets),
            "Windows Update" => AllowsRegistryModule(change, WindowsUpdateTargets),
            "Power Plans" => AllowsPower(change),
            _ => false,
        };

    internal static bool Allows(ActionDescriptor action)
    {
        if (action.ModuleId == "Software")
            return AllowsSoftwareAction(action.ActionId);
        if (action.ModuleId == "Power Plans")
            return action.ActionId.StartsWith(PowerActionFactory.DeletePlanPrefix, StringComparison.Ordinal)
                && Guid.TryParseExact(action.ActionId[PowerActionFactory.DeletePlanPrefix.Length..], "D", out _);
        return false;
    }

    private static bool AllowsExplorer(ChangeDescriptor change)
    {
        if (change.ValueType is not (ChangeValueType.Registry_DWord or ChangeValueType.Registry_String))
            return false;
        if (!ExplorerLocations.Contains(change.SystemLocation))
            return false;

        if (change.SettingId.StartsWith(ExplorerPatcherChangeFactory.SettingIdPrefix, StringComparison.Ordinal))
        {
            return ExplorerPatcherCatalog.Entries.Any(setting =>
                change.SettingId == ExplorerPatcherChangeFactory.SettingIdPrefix + setting.RegistryValueName
                && change.SystemLocation.Equals(setting.SystemLocation, StringComparison.OrdinalIgnoreCase));
        }

        return !change.SettingId.Contains(':', StringComparison.Ordinal);
    }

    private static bool AllowsContextMenu(ChangeDescriptor change)
    {
        if (change.ValueType == ChangeValueType.Registry_KeyTree)
            return WindowsEntriesChangeFactory.KeyTreeAllowlist.Contains(change.SystemLocation);

        if (change.ValueType == ChangeValueType.Shell_CustomVerb)
        {
            var before = ParseDefinition(change.BeforeValue);
            var after = ParseDefinition(change.AfterValue);
            return (before is not null || after is not null)
                && (before is null || ValidCustomVerb(change, before))
                && (after is null || ValidCustomVerb(change, after))
                && (before is null || after is null
                    || before.Scope.Equals(after.Scope, StringComparison.OrdinalIgnoreCase)
                    && before.VerbId.Equals(after.VerbId, StringComparison.OrdinalIgnoreCase));
        }

        if (change.ValueType is not (ChangeValueType.Registry_String or ChangeValueType.Registry_DWord))
            return false;
        if (WindowsEntriesChangeFactory.Catalog.Any(entry =>
            entry.SettingId == change.SettingId
            && (entry.SystemLocation.Equals(change.SystemLocation, StringComparison.OrdinalIgnoreCase)
                || change.SettingId == "ctx-win-print-scripts"
                   && change.SystemLocation is @"HKCR\batfile\shell\print\ProgrammaticAccessOnly"
                       or @"HKCR\cmdfile\shell\print\ProgrammaticAccessOnly")))
        {
            return true;
        }

        if (change.SettingId.StartsWith("ctx-handler-", StringComparison.Ordinal))
        {
            var id = change.SettingId["ctx-handler-".Length..];
            if (!Guid.TryParse(id, out var clsid))
                return false;
            var braced = clsid.ToString("B").ToUpperInvariant();
            if (change.SystemLocation.Equals($@"{ShellRegistryPaths.BlockedListKeyPath}\{braced}", StringComparison.OrdinalIgnoreCase))
                return HandlerValues(change, braced, allowAbsent: true);
            return IsHandlerRegistration(change.SystemLocation)
                && HandlerValues(change, braced, allowAbsent: change.Category == ChangeCategory.Delete);
        }

        return change.SettingId.StartsWith("ctx-verb-", StringComparison.Ordinal)
            && IsStaticVerbLocation(change.SystemLocation)
            && EmptyOrAbsent(change.BeforeValue) && EmptyOrAbsent(change.AfterValue);
    }

    private static bool AllowsEnvironment(ChangeDescriptor change)
    {
        if (change.ValueType != ChangeValueType.Environment_Variable
            || change.Enforcement is not null
            || !TrySplitLocation(change.SystemLocation, out var key, out var name)
            || name.Length == 0 || name.Contains('=') || name is "(Default)")
        {
            return false;
        }

        var scope = key.Equals(EnvironmentVariableReader.UserEnvKeyPath, StringComparison.OrdinalIgnoreCase)
            ? "user"
            : key.Equals(EnvironmentVariableReader.SystemEnvKeyPath, StringComparison.OrdinalIgnoreCase)
                ? "system"
                : null;
        return scope is not null
            && change.SettingId.Equals($"env-{scope}-{name.ToLowerInvariant()}", StringComparison.Ordinal);
    }

    private static bool AllowsStartup(ChangeDescriptor change)
    {
        if (change.Enforcement is not null)
            return false;
        return change.ValueType switch
        {
            ChangeValueType.Service_StartType =>
                change.SettingId == ServiceChangeFactory.GetSettingId(change.SystemLocation)
                && IsServiceName(change.SystemLocation),
            ChangeValueType.ScheduledTask_State =>
                change.SystemLocation.StartsWith('\\')
                && change.SettingId == ScheduledTaskChangeFactory.GetSettingId(change.SystemLocation),
            ChangeValueType.Registry_Binary => AllowsStartupEntry(change),
            ChangeValueType.Autorun_State => AllowsAutorun(change),
            _ => false,
        };
    }

    private static bool AllowsStartupEntry(ChangeDescriptor change)
    {
        if (!TrySplitLocation(change.SystemLocation, out var key, out var name))
            return false;
        var sources = Enum.GetValues<ThisIsMyPC.Modules.Startup.Models.StartupSource>();
        foreach (var source in sources)
        {
            if (!key.Equals(StartupChangeFactory.GetApprovedKeyPath(source), StringComparison.OrdinalIgnoreCase))
                continue;
            return change.SettingId.Equals($"startup-entry:{source}:{name}", StringComparison.Ordinal);
        }
        return false;
    }

    private static bool AllowsAutorun(ChangeDescriptor change)
    {
        var target = AutorunTarget.TryParse(change.SystemLocation);
        if (target is null)
            return false;
        if (target.Kind is ThisIsMyPC.Modules.Startup.Models.AutorunItemKind.RegistryValue
            or ThisIsMyPC.Modules.Startup.Models.AutorunItemKind.RegistryKey)
        {
            if (!AutorunLocations.Registry.Any(location => location.Kind == target.Kind
                && location.KeyPath.Equals(target.Location, StringComparison.OrdinalIgnoreCase)))
                return false;
        }
        else if (target.Kind == ThisIsMyPC.Modules.Startup.Models.AutorunItemKind.Service
                 && !target.Location.Equals(AutorunLocations.ServicesKey, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        else if (target.Kind == ThisIsMyPC.Modules.Startup.Models.AutorunItemKind.ScheduledTask
                 && !target.Location.StartsWith('\\'))
        {
            return false;
        }
        else if (target.Kind == ThisIsMyPC.Modules.Startup.Models.AutorunItemKind.StartupFile
                 && !target.Location.EndsWith(@"\Microsoft\Windows\Start Menu\Programs\Startup", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        var expected = AutorunChangeFactory.SettingIdPrefix + target.Encode();
        return change.SettingId == expected || change.SettingId == expected + AutorunChangeFactory.ParkedSuffix;
    }

    private static bool AllowsPower(ChangeDescriptor change)
    {
        if (change.Enforcement is not null)
            return false;
        if (change.ValueType == ChangeValueType.Registry_DWord)
        {
            return change.SettingId == PowerPlanChangeFactory.ModernStandbySettingId
                && change.SystemLocation.Equals(
                    $@"{PowerPlanChangeFactory.ModernStandbyKeyPath}\{PowerPlanChangeFactory.ModernStandbyValueName}",
                    StringComparison.OrdinalIgnoreCase);
        }
        if (change.ValueType != ChangeValueType.PowerPlan_Setting)
            return false;

        if (change.SettingId == PowerPlanChangeFactory.ActivePlanSettingId)
            return change.SystemLocation == "powrprof:ActiveScheme" && GuidPair(change);
        if (change.SettingId == PowerPlanChangeFactory.ActivePlanPolicyPinSettingId)
            return change.SystemLocation.Equals(
                $@"{PowerPlanChangeFactory.ActivePlanPolicyKeyPath}\{PowerPlanChangeFactory.ActivePlanPolicyValueName}",
                StringComparison.OrdinalIgnoreCase) && GuidOrEmptyPair(change);
        if (change.SettingId == PowerPlanChangeFactory.HibernateSettingId)
            return change.SystemLocation == "powrprof:SystemReserveHiberFile" && BinaryPair(change);
        if (change.SettingId == PowerPlanChangeFactory.UltimatePerformanceSettingId)
            return change.SystemLocation == $"powrprof:PowerDuplicateScheme {PowerPlanChangeFactory.UltimatePerformanceSourceGuid:D}"
                && BinaryPair(change);
        if (change.SettingId.StartsWith(PowerPlanChangeFactory.AddStockPlanPrefix, StringComparison.Ordinal))
        {
            return Guid.TryParseExact(change.SettingId[PowerPlanChangeFactory.AddStockPlanPrefix.Length..], "D", out var guid)
                && StockPowerPlan.FindByGuid(guid) is not null
                && change.SystemLocation == $"powrprof:PowerDuplicateScheme {guid:D}"
                && BinaryPair(change);
        }
        if (change.SettingId.StartsWith(PowerPlanChangeFactory.CreatePlanPrefix, StringComparison.Ordinal))
            return change.SettingId.Length > PowerPlanChangeFactory.CreatePlanPrefix.Length
                && PowerPlanChangeFactory.TryParseSourceGuid(change.SystemLocation, out _)
                && change.SystemLocation.StartsWith("powrprof:PowerDuplicateScheme ", StringComparison.Ordinal)
                && BinaryPair(change);
        if (!change.SettingId.StartsWith(PowerPlanChangeFactory.SettingIdPrefix, StringComparison.Ordinal))
            return false;
        var parts = change.SystemLocation.Split('/');
        if (parts.Length != 4 || !Guid.TryParseExact(parts[0], "D", out _)
            || !Guid.TryParseExact(parts[1], "D", out _) || !Guid.TryParseExact(parts[2], "D", out _)
            || parts[3] is not ("AC" or "DC"))
        {
            return false;
        }
        return change.SettingId == $"{PowerPlanChangeFactory.SettingIdPrefix}{parts[0]}:{parts[2]}:{parts[3]}";
    }

    private static bool AllowsSoftwareAction(string actionId)
    {
        if (actionId.StartsWith(SoftwareActionFactory.InstallPrefix, StringComparison.Ordinal)
            || actionId.StartsWith(SoftwareActionFactory.UninstallPrefix, StringComparison.Ordinal))
        {
            var prefix = actionId.StartsWith(SoftwareActionFactory.InstallPrefix, StringComparison.Ordinal)
                ? SoftwareActionFactory.InstallPrefix : SoftwareActionFactory.UninstallPrefix;
            return SoftwareCatalog.Entries.Any(entry => actionId == prefix + entry.Id);
        }
        if (actionId.StartsWith(SoftwareActionFactory.AppxRemovePrefix, StringComparison.Ordinal)
            || actionId.StartsWith(SoftwareActionFactory.AppxReinstallPrefix, StringComparison.Ordinal))
        {
            var prefix = actionId.StartsWith(SoftwareActionFactory.AppxRemovePrefix, StringComparison.Ordinal)
                ? SoftwareActionFactory.AppxRemovePrefix : SoftwareActionFactory.AppxReinstallPrefix;
            return WindowsAppsCatalog.Entries.Any(entry => actionId == prefix + entry.Id);
        }
        if (!actionId.StartsWith(SoftwareActionFactory.UpgradePrefix, StringComparison.Ordinal))
            return false;
        var id = actionId[SoftwareActionFactory.UpgradePrefix.Length..];
        return id.Length is > 0 and <= 255
            && id.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_');
    }

    private static bool ValidValues(ChangeDescriptor change) => change.ValueType switch
    {
        ChangeValueType.Registry_DWord => NumericOrAbsent(change.BeforeValue) && NumericOrAbsent(change.AfterValue),
        ChangeValueType.Registry_Binary => HexOrEmpty(change.BeforeValue) && HexOrEmpty(change.AfterValue),
        ChangeValueType.Service_StartType => EnumValue<ServiceStartType>(change.BeforeValue)
                                             && EnumValue<ServiceStartType>(change.AfterValue),
        ChangeValueType.ScheduledTask_State => State(change.BeforeValue) && State(change.AfterValue),
        ChangeValueType.Autorun_State => AutorunChangeFactory.ParseState(change.BeforeValue, out _) is not null
                                         && AutorunChangeFactory.ParseState(change.AfterValue, out _) is not null,
        ChangeValueType.PowerPlan_Setting when change.SettingId.StartsWith(PowerPlanChangeFactory.SettingIdPrefix, StringComparison.Ordinal)
            => uint.TryParse(change.BeforeValue, NumberStyles.None, CultureInfo.InvariantCulture, out _)
               && uint.TryParse(change.AfterValue, NumberStyles.None, CultureInfo.InvariantCulture, out _),
        _ => true,
    };

    private static bool ValidEnforcement(SettingEnforcement? enforcement)
    {
        if (enforcement is null)
            return true;
        if (enforcement.AclElevation || enforcement.OwnerModeRequired)
            return false;
        return Allowed(enforcement.CompanionServices, "DiagTrack", "WerSvc")
            && Allowed(enforcement.CompanionTasks)
            && Allowed(enforcement.GPCacheEntries, WindowsUpdateRegistryPaths.GPCacheKeyPath)
            && Allowed(enforcement.ReversionVectors,
                "Windows Update", "Web Experience Pack deployment", "Windows feature updates",
                "Copilot app deployment", "Group Policy refresh",
                "Application updates or reinstalls may re-register this handler",
                "Windows feature updates may restore the modern behavior (undocumented Explorer shim override)");
    }

    private static bool AllowsRegistryModule(ChangeDescriptor change, HashSet<string> targets) =>
        targets.Contains(RegistryTarget(change.SettingId, change.SystemLocation, change.ValueType));

    private static HashSet<string> RegistryTargets(
        IEnumerable<(string SettingId, string Key, string Name, ChangeValueType Type)> targets) =>
        new(targets.Select(target => RegistryTarget(
            target.SettingId, $@"{target.Key}\{target.Name}", target.Type)), StringComparer.OrdinalIgnoreCase);

    private static string RegistryTarget(string settingId, string location, ChangeValueType type) =>
        $"{settingId}\0{location}\0{(int)type}";

    private static bool IsHandlerRegistration(string location)
    {
        if (!location.EndsWith(@"\(Default)", StringComparison.OrdinalIgnoreCase))
            return false;
        if (!location.StartsWith("HKCR\\", StringComparison.OrdinalIgnoreCase))
            return false;
        return location.Contains(@"\shellex\ContextMenuHandlers\", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsStaticVerbLocation(string location) =>
        location.EndsWith(@"\LegacyDisable", StringComparison.OrdinalIgnoreCase)
        && (location.StartsWith(@"HKCR\", StringComparison.OrdinalIgnoreCase)
            || location.StartsWith(@"HKCU\Software\Classes\", StringComparison.OrdinalIgnoreCase))
        && location.Contains(@"\shell\", StringComparison.OrdinalIgnoreCase);

    private static bool TrySplitLocation(string location, out string key, out string name)
    {
        var separator = location.LastIndexOf('\\');
        if (separator <= 0 || separator == location.Length - 1)
        {
            key = name = string.Empty;
            return false;
        }
        key = location[..separator];
        name = location[(separator + 1)..];
        return true;
    }

    private static HashSet<string> BuildExplorerLocations()
    {
        var locations = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            $@"{ShellRegistryPaths.AdvancedKeyPath}\HideFileExt",
            $@"{ShellRegistryPaths.AdvancedKeyPath}\Hidden",
            $@"{ShellRegistryPaths.ExplorerKeyPath}\LaunchTo",
            $@"{ShellRegistryPaths.AdvancedKeyPath}\AutoCheckSelect",
            $@"{ShellRegistryPaths.AdvancedKeyPath}\UseCompactMode",
            $@"{ShellRegistryPaths.ExplorerKeyPath}\ShowRecent",
            $@"{ShellRegistryPaths.ExplorerKeyPath}\ShowFrequent",
            $@"{ShellRegistryPaths.AdvancedKeyPath}\NavPaneShowAllFolders",
            $@"{ShellRegistryPaths.AdvancedKeyPath}\NavPaneExpandToCurrentFolder",
            $@"{ShellRegistryPaths.AdvancedKeyPath}\SeparateProcess",
            $@"{ShellRegistryPaths.AdvancedKeyPath}\ShowSuperHidden",
            $@"{ShellRegistryPaths.AdvancedKeyPath}\ShowSyncProviderNotifications",
            $@"{ShellRegistryPaths.OperationStatusManagerKeyPath}\EnthusiastMode",
            $@"{ShellRegistryPaths.AdvancedKeyPath}\PersistBrowsers",
            $@"{ShellRegistryPaths.AdvancedKeyPath}\HideMergeConflicts",
            $@"{ShellRegistryPaths.NamingTemplatesKeyPath}\ShortcutNameTemplate",
            $@"{ShellRegistryPaths.AdvancedKeyPath}\ShowSecondsInSystemClock",
            $@"{ShellRegistryPaths.AdvancedKeyPath}\ShowTaskViewButton",
            $@"{ShellRegistryPaths.TaskbarDeveloperSettingsKeyPath}\TaskbarEndTask",
            $@"{ShellRegistryPaths.AdvancedKeyPath}\Start_IrisRecommendations",
            $@"{ShellRegistryPaths.AdvancedKeyPath}\Start_AccountNotifications",
            $@"{ShellRegistryPaths.AdvancedKeyPath}\SnapAssist",
            $@"{ShellRegistryPaths.AdvancedKeyPath}\DisallowShaking",
            $@"{ShellRegistryPaths.AdvancedKeyPath}\TaskbarAl",
            $@"{ShellRegistryPaths.SearchKeyPath}\SearchboxTaskbarMode",
            $@"{ShellRegistryPaths.AdvancedKeyPath}\TaskbarGlomLevel",
            $@"{ShellRegistryPaths.AdvancedKeyPath}\TaskbarDa",
            ShellRegistryPaths.ClassicContextMenuKeyPath,
            ShellRegistryPaths.CommandBarKeyPath,
        };
        foreach (var setting in ExplorerPatcherCatalog.Entries)
            locations.Add(setting.SystemLocation);
        return locations;
    }

    private static CustomVerbDefinition? ParseDefinition(string? value) =>
        value == ShellRegistryPaths.AbsentValue ? null : CustomVerbDefinition.Deserialize(value);
    private static bool ValidCustomVerb(ChangeDescriptor change, CustomVerbDefinition definition) =>
        CustomVerbDefinition.ScopeOptions.Any(option =>
            option.Scope.Equals(definition.Scope, StringComparison.OrdinalIgnoreCase))
        && definition.VerbId.Length is > 0 and <= 80
        && definition.VerbId.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_')
        && change.SystemLocation.Equals(definition.KeyPath, StringComparison.OrdinalIgnoreCase)
        && change.SettingId == CustomVerbChangeFactory.MakeSettingId(definition);
    private static bool IsServiceName(string value) => value.Length <= 256
        && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-' or '.' or '$');
    private static bool NumericOrAbsent(string? value) => string.IsNullOrEmpty(value)
        || value == ShellRegistryPaths.AbsentValue
        || int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _);
    private static bool HexOrEmpty(string? value) => string.IsNullOrEmpty(value)
        || value.Length % 2 == 0 && value.All(Uri.IsHexDigit);
    private static bool State(string? value) => value is "Enabled" or "Disabled";
    private static bool EnumValue<T>(string? value) where T : struct, Enum =>
        Enum.TryParse<T>(value, out var parsed) && Enum.IsDefined(parsed);
    private static bool BinaryPair(ChangeDescriptor change) => change.BeforeValue is "0" or "1"
        && change.AfterValue is "0" or "1";
    private static bool GuidPair(ChangeDescriptor change) => Guid.TryParse(change.BeforeValue, out _)
        && Guid.TryParse(change.AfterValue, out _);
    private static bool GuidOrEmptyPair(ChangeDescriptor change) => GuidOrEmpty(change.BeforeValue)
        && GuidOrEmpty(change.AfterValue);
    private static bool GuidOrEmpty(string? value) => string.IsNullOrEmpty(value) || Guid.TryParse(value, out _);
    private static bool HandlerValues(ChangeDescriptor change, string clsid, bool allowAbsent) =>
        HandlerValue(change.BeforeValue, clsid, allowAbsent) && HandlerValue(change.AfterValue, clsid, allowAbsent);
    private static bool HandlerValue(string? value, string clsid, bool allowAbsent) =>
        value is not null && (value.Equals(clsid, StringComparison.OrdinalIgnoreCase)
            || value.Equals("-" + clsid, StringComparison.OrdinalIgnoreCase)
            || allowAbsent && EmptyOrAbsent(value));
    private static bool EmptyOrAbsent(string? value) => value is "" or ShellRegistryPaths.AbsentValue;
    private static bool Allowed(IReadOnlyList<string>? values, params string[] allowed) => values is null
        || values.All(value => allowed.Contains(value, StringComparer.OrdinalIgnoreCase));
}
