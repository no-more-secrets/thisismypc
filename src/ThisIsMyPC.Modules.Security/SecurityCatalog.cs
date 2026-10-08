using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Modules;
using ThisIsMyPC.Core.Policies;

namespace ThisIsMyPC.Modules.Security;

public sealed record SecurityTarget(string Key, string Name, ChangeValueType Type = ChangeValueType.Registry_DWord)
{
    public string Location => Key + "\\" + Name;
}

public sealed record SecurityOption(string Id, string Label, params string[] Values);

public sealed record SecuritySetting(string Id, string Title, string Description, string Section,
    WindowsSku Edition, IReadOnlyList<SecurityTarget> Targets, IReadOnlyList<SecurityOption> Choices);

/// <summary>Exact machine policy addresses and options, verified against Windows ADMX definitions.</summary>
public static class SecurityCatalog
{
    public const string ModuleId = "Security";
    private const string Defender = @"HKLM\SOFTWARE\Policies\Microsoft\Windows Defender";
    private const string WindowsSystem = @"HKLM\SOFTWARE\Policies\Microsoft\Windows\System";
    private const string Phishing = @"HKLM\SOFTWARE\Policies\Microsoft\Windows\WTDS\Components";
    private const string Notifications = @"HKLM\SOFTWARE\Policies\Microsoft\Windows Defender Security Center\Notifications";

    public static IReadOnlyList<SecuritySetting> Settings { get; } = Build();

    private static IReadOnlyList<SecuritySetting> Build()
    {
        var settings = new List<SecuritySetting>();
        void Single(string id, string title, string description, string section, string key, string name,
            SecurityOption[] choices, WindowsSku edition = WindowsSku.Enterprise) =>
            settings.Add(new(id, title, description, section, edition, [new(key, name)],
                [new("default", "Not configured", ""), .. choices]));
        SecurityOption[] Boolean(bool inverted = false) =>
            [new("on", "On", inverted ? "0" : "1"), new("off", "Off", inverted ? "1" : "0")];
        SecurityOption[] Modes() => [new("off", "Off", "0"), new("block", "Block", "1"), new("audit", "Audit only", "2")];

        Single("secure-sign-in", "Require Ctrl+Alt+Delete before sign-in",
            "Requires the secure key sequence before Windows shows the sign-in screen. Takes effect at the next sign-in.", "Sign-in",
            @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", "DisableCAD", Boolean(true), WindowsSku.Pro);
        Single("real-time-protection", "Real-time antivirus protection", "Scans files as they are opened or run. Tamper protection can reject this policy.",
            "Defender", Defender + @"\Real-Time Protection", "DisableRealtimeMonitoring", Boolean(true));
        Single("unwanted-apps", "Potentially unwanted applications", "Block unwanted applications, record detections only, or turn this policy off.",
            "Defender", Defender, "PUAProtection", Modes());
        Single("cloud-protection", "Cloud-delivered protection", "Sends threat information to Microsoft MAPS. Basic is a legacy policy value.",
            "Defender", Defender + @"\Spynet", "SpynetReporting",
            [new("off", "Off", "0"), new("basic", "Basic (legacy)", "1"), new("advanced", "Advanced", "2")]);
        Single("sample-submission", "Automatic sample submission", "Controls sending suspicious files to Microsoft. Files can contain personal information.",
            "Defender", Defender + @"\Spynet", "SubmitSamplesConsent",
            [new("prompt", "Ask first", "0"), new("safe", "Send safe samples", "1"), new("never", "Never send", "2"), new("all", "Send all samples", "3")]);
        Single("block-first-sight", "Block at first sight", "Checks suspicious content before it runs. Requires cloud protection and automatic sample submission.",
            "Defender", Defender + @"\Spynet", "DisableBlockAtFirstSeen", Boolean(true));
        Single("controlled-folders", "Controlled folder access", "Restricts untrusted writes to protected folders or disk sectors. Requires Microsoft Defender Antivirus.",
            "Defender", Defender + @"\Windows Defender Exploit Guard\Controlled Folder Access", "EnableControlledFolderAccess",
            [.. Modes(), new("disk-block", "Block disk changes only", "3"), new("disk-audit", "Audit disk changes only", "4")]);
        Single("network-protection", "Network protection", "Blocks or records connections to malicious destinations. Requires Microsoft Defender Antivirus.",
            "Defender", Defender + @"\Windows Defender Exploit Guard\Network Protection", "EnableNetworkProtection", Modes());
        Single("behavior-monitoring", "Monitor program behavior", "Detects threats through suspicious program behavior. Requires Microsoft Defender Antivirus.",
            "Defender", Defender + @"\Real-Time Protection", "DisableBehaviorMonitoring", Boolean(true));
        Single("file-activity-monitoring", "Monitor file and program activity", "Checks file and program activity through real-time protection. Requires Microsoft Defender Antivirus.",
            "Defender", Defender + @"\Real-Time Protection", "DisableOnAccessProtection", Boolean(true));
        Single("download-scanning", "Scan downloads and attachments", "Checks downloaded files and attachments. Also required for block at first sight.",
            "Defender", Defender + @"\Real-Time Protection", "DisableIOAVProtection", Boolean(true));
        Single("script-scanning", "Scan scripts", "Checks scripts for malicious content. Requires Microsoft Defender Antivirus.",
            "Defender", Defender + @"\Real-Time Protection", "DisableScriptScanning", Boolean(true));
        Single("scan-on-protection-enable", "Scan processes when protection resumes", "Scans running processes when real-time protection turns on again.",
            "Defender", Defender + @"\Real-Time Protection", "DisableScanOnRealtimeEnable", Boolean(true));
        Single("cloud-block-level", "Cloud blocking level", "Requires cloud protection. Higher levels can block legitimate apps; zero tolerance blocks all unknown executables.",
            "Defender", Defender + @"\MpEngine", "MpCloudBlockLevel",
            [new("normal", "Default blocking", "0"), new("moderate", "Moderate", "1"), new("high", "High", "2"),
             new("high-plus", "High plus", "4"), new("zero-tolerance", "Zero tolerance", "6")]);
        Single("low-priority-scans", "Low CPU priority for scheduled scans", "Lets other applications take priority over scheduled antivirus scans.",
            "Scanning", Defender + @"\Scan", "LowCpuPriority", Boolean());
        Single("heuristic-detection", "Detect unfamiliar threats", "Uses heuristic analysis to identify suspicious files beyond known threat signatures.",
            "Scanning", Defender + @"\Scan", "DisableHeuristics", Boolean(true));
        Single("archive-scanning", "Scan archive files", "Scans contents of ZIP, CAB, and other archives. Direct scans of archives still scan their contents when this is off.",
            "Scanning", Defender + @"\Scan", "DisableArchiveScanning", Boolean(true));
        Single("network-file-scanning", "Scan network files", "Scans files on network shares. Does not add mapped network drives to full scans.",
            "Scanning", Defender + @"\Scan", "DisableScanningNetworkFiles", Boolean(true));
        Single("update-before-scan", "Check for updates before scheduled scans", "Checks for new threat definitions before scheduled scans. Does not affect manually started scans.",
            "Scanning", Defender + @"\Scan", "CheckForSignaturesBeforeRunningScan", Boolean());
        Single("intelligence-on-battery", "Allow threat updates on battery", "Allows Defender security intelligence updates while running on battery power.",
            "Threat updates", Defender + @"\Signature Updates", "DisableScheduledSignatureUpdateOnBattery", Boolean(true));
        Single("intelligence-on-startup", "Check for threat updates at startup", "Checks for new security intelligence when the Defender service starts.",
            "Threat updates", Defender + @"\Signature Updates", "UpdateOnStartUp", Boolean());
        settings.Add(new("smartscreen", "Downloaded app reputation checks", "Warns about unrecognized downloaded apps. Block mode prevents bypassing the warning.",
            "App protection", WindowsSku.Enterprise,
            [new(WindowsSystem, "EnableSmartScreen"), new(WindowsSystem, "ShellSmartScreenLevel", ChangeValueType.Registry_String)],
            [new("default", "Not configured", "", ""), new("off", "Off", "0", ""), new("warn", "Warn", "1", "Warn"), new("block", "Warn and prevent bypass", "1", "Block")]));
        settings.Add(new("app-install-control", "App installation sources", "Choose recommendations, warnings, or Store-only installation. Depends on SmartScreen.",
            "App protection", WindowsSku.Enterprise,
            [new(Defender + @"\SmartScreen", "ConfigureAppInstallControlEnabled"), new(Defender + @"\SmartScreen", "ConfigureAppInstallControl", ChangeValueType.Registry_String)],
            [new("default", "Not configured", "", ""), new("disabled", "Policy disabled", "0", ""),
             new("anywhere", "Allow apps from anywhere", "1", "Anywhere"), new("recommend", "Recommend Microsoft Store alternatives", "1", "Recommendations"),
             new("warn", "Warn about apps outside Microsoft Store", "1", "PreferStore"), new("store", "Microsoft Store only", "1", "StoreOnly")]));
        Single("phishing-protection", "Enhanced phishing protection", "Protects supported sign-in passwords. Windows Hello-only sign-in does not supply a password to monitor.",
            "App protection", Phishing, "ServiceEnabled", Boolean());
        Single("phishing-malicious", "Warn about malicious sites and apps", "Warns when a protected sign-in password is entered into a malicious destination. Requires phishing protection.",
            "App protection", Phishing, "NotifyMalicious", Boolean());
        Single("phishing-reuse", "Warn about password reuse", "Warns when a protected sign-in password is reused elsewhere. Requires phishing protection.",
            "App protection", Phishing, "NotifyPasswordReuse", Boolean());
        Single("phishing-storage", "Warn about unsafe password storage", "Warns when a protected sign-in password is saved in an unsafe app. Requires phishing protection.",
            "App protection", Phishing, "NotifyUnsafeApp", Boolean());
        settings.Add(new("security-notifications", "Windows Security notifications", "Choose which security alerts Windows shows. Hiding all also hides critical alerts.",
            "Notifications", WindowsSku.Enterprise,
            [new(Notifications, "DisableNotifications"), new(Notifications, "DisableEnhancedNotifications")],
            [new("default", "Not configured", "", ""), new("all", "Show all alerts", "0", "0"),
             new("critical", "Show critical alerts only", "0", "1"), new("none", "Hide all alerts", "1", "1")]));
        Single("defender-startup-priority", "Defender startup priority", "Normal priority starts Defender sooner but can compete with other startup tasks.",
            "Defender", Defender, "AllowFastServiceStartup", [new("normal", "Normal", "1"), new("low", "Low", "0")]);
        Single("automatic-remediation", "Act on detected threats automatically", "Off asks you to choose an action for detected threats. On lets Defender choose an action automatically.",
            "Defender", Defender, "DisableRoutinelyTakingAction", Boolean(true));
        Single("file-hash-computation", "Compute hashes for scanned files", "Calculates file fingerprints during Defender scans. This can add scan overhead.",
            "Defender", Defender + @"\MpEngine", "EnableFileHashComputation", Boolean());
        Single("scan-direction", "File monitoring direction", "Limits real-time file monitoring on NTFS volumes. Other file systems keep full monitoring.",
            "Scanning", Defender + @"\Real-Time Protection", "RealtimeScanDirection",
            [new("both", "Incoming and outgoing files", "0"), new("incoming", "Incoming files only", "1"), new("outgoing", "Outgoing files only", "2")]);
        Single("randomize-schedules", "Randomize scheduled start times", "Varies scheduled scan and threat-update start times within the configured randomization window.",
            "Scanning", Defender, "RandomizeScheduleTaskTimes", Boolean());
        Single("restore-before-cleaning", "Create restore points before cleaning", "Requests a daily restore point before Defender removes threats. Requires System Protection support.",
            "Scanning", Defender + @"\Scan", "DisableRestorePoint", Boolean(true));
        Single("catchup-full-scan", "Catch up missed full scans", "Runs a catch-up scan after repeated missed full scans. Requires a full-scan schedule.",
            "Scanning", Defender + @"\Scan", "DisableCatchupFullScan", Boolean(true));
        Single("catchup-quick-scan", "Catch up missed quick scans", "Runs a catch-up scan after repeated missed quick scans. Requires a quick-scan schedule.",
            "Scanning", Defender + @"\Scan", "DisableCatchupQuickScan", Boolean(true));
        Single("reparse-point-scanning", "Scan through file-system links", "Follows reparse points such as directory junctions during scans. Following linked paths can slow scanning.",
            "Scanning", Defender + @"\Scan", "DisableReparsePointScanning", Boolean(true));
        Single("rapid-intelligence", "Receive rapid threat updates", "Receives threat-specific intelligence in response to cloud reports. Requires Microsoft MAPS cloud protection.",
            "Threat updates", Defender + @"\Signature Updates", "RealtimeSignatureDelivery", Boolean());
        Single("intelligence-from-microsoft-update", "Use Microsoft Update for threat updates", "Allows threat updates from Microsoft Update even when another update source is configured.",
            "Threat updates", Defender + @"\Signature Updates", "ForceUpdateFromMU", Boolean());
        Single("cloud-signature-corrections", "Accept cloud corrections for false detections", "Allows MAPS to disable individual threat signatures causing false detections. Requires cloud protection.",
            "Threat updates", Defender + @"\Signature Updates", "SignatureDisableNotification", Boolean());
        Single("scan-after-intelligence-update", "Scan after threat updates", "Starts an antivirus scan after a security intelligence update.",
            "Threat updates", Defender + @"\Signature Updates", "DisableScanOnUpdate", Boolean(true));
        Single("defender-error-reporting", "Send Defender error reports", "Sends Defender Watson diagnostic events to Microsoft. Separate from cloud threat reports.",
            "Notifications", Defender + @"\Reporting", "DisableGenericRePorts", Boolean(true));
        Single("defender-enhanced-notifications", "Show Defender activity notifications", "Allows Defender's enhanced notifications. Windows Security notification policies can still hide alerts.",
            "Notifications", Defender + @"\Reporting", "DisableEnhancedNotifications", Boolean(true));
        Single("defender-reboot-notifications", "Show Defender restart notifications", "Controls antimalware restart notices in Defender's UI-only mode. Does not control Windows Update restart notices.",
            "Notifications", Defender + @"\UX Configuration", "SuppressRebootNotification", Boolean(true));
        Single("allow-exploit-protection-edits", "Allow local exploit-protection edits", "Allows users to change exploit-protection settings in Windows Security. Does not turn protection on or off.",
            "App protection", @"HKLM\SOFTWARE\Policies\Microsoft\Windows Defender Security Center\App and Browser protection",
            "DisallowExploitProtectionOverride", Boolean(true));
        Single("merge-local-defender-lists", "Merge local Defender lists with policy", "Includes local administrator exclusions and threat lists alongside Group Policy lists. Policy wins conflicts.",
            "Policy overrides", Defender, "DisableLocalAdminMerge", Boolean(true));
        void Override(string id, string title, string key, string name) => Single(id, title,
            "Chooses whether the local preference or Group Policy takes priority. Does not change the preference itself.",
            "Policy overrides", key, name, [new("policy", "Use Group Policy", "0"), new("local", "Use local preference", "1")]);
        Override("override-cloud-protection", "Cloud protection preference source", Defender + @"\Spynet", "LocalSettingOverrideSpynetReporting");
        Override("override-file-monitoring", "File monitoring preference source", Defender + @"\Real-Time Protection", "LocalSettingOverrideDisableOnAccessProtection");
        Override("override-scan-direction", "File monitoring direction source", Defender + @"\Real-Time Protection", "LocalSettingOverrideRealtimeScanDirection");
        Override("override-download-scanning", "Download scanning preference source", Defender + @"\Real-Time Protection", "LocalSettingOverrideDisableIOAVProtection");
        Override("override-behavior-monitoring", "Behavior monitoring preference source", Defender + @"\Real-Time Protection", "LocalSettingOverrideDisableBehaviorMonitoring");
        Override("override-real-time-protection", "Real-time protection preference source", Defender + @"\Real-Time Protection", "LocalSettingOverrideDisableRealtimeMonitoring");
        return settings;
    }

    public static bool Allows(ChangeDescriptor change)
    {
        var setting = Settings.FirstOrDefault(s => s.Id == change.SettingId);
        if (setting is null || change.ModuleId != ModuleId) return false;
        var local = LocalPolicyValue.IsPolicyType(change.ValueType);
        if (local && setting.Id == "secure-sign-in") return false;
        var type = local ? LocalPolicyValue.RegistryType(change.ValueType) : change.ValueType;
        var index = setting.Targets.ToList().FindIndex(t => t.Location.Equals(change.SystemLocation, StringComparison.OrdinalIgnoreCase) && t.Type == type);
        return index >= 0 && change.Enforcement?.SkuRestriction == setting.Edition
            && !change.Enforcement.AclElevation && !change.Enforcement.OwnerModeRequired
            && change.Enforcement.CompanionServices is not { Count: > 0 }
            && change.Enforcement.CompanionTasks is not { Count: > 0 }
            && change.Enforcement.GPCacheEntries is not { Count: > 0 }
            && change.Enforcement.ReversionVectors is not { Count: > 0 }
            && Valid(change.BeforeValue) && Valid(change.AfterValue);

        bool Valid(string? text)
        {
            if (!local) return setting.Choices.Any(o => o.Values[index] == text);
            var value = LocalPolicyValue.Decode(text);
            return value is not null && setting.Choices.Any(o => o.Values[index] == value.Live)
                && (value.Saved is null || value.Saved.Length > 0 && setting.Choices.Any(o => o.Values[index] == value.Saved));
        }
    }
}
