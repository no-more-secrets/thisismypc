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
    WindowsSku Edition, IReadOnlyList<SecurityTarget> Targets, IReadOnlyList<SecurityOption> Choices, string? Help = null);

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
            SecurityOption[] choices, WindowsSku edition = WindowsSku.Enterprise, string? help = null) =>
            settings.Add(new(id, title, description, section, edition, [new(key, name)],
                [new("default", "Not configured", ""), .. choices], help));
        SecurityOption[] Boolean(bool inverted = false) =>
            [new("on", "On", inverted ? "0" : "1"), new("off", "Off", inverted ? "1" : "0")];
        SecurityOption[] Modes() => [new("off", "Off", "0"), new("block", "Block", "1"), new("audit", "Audit only", "2")];

        Single("secure-sign-in", "Require Ctrl+Alt+Delete before sign-in",
            "Require the secure key sequence before sign-in", "Sign-in",
            @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", "DisableCAD", Boolean(true), WindowsSku.Pro, help: "Takes effect at the next sign-in.");
        Single("real-time-protection", "Real-time antivirus protection", "Scan files as they are opened or run",
            "Defender", Defender + @"\Real-Time Protection", "DisableRealtimeMonitoring", Boolean(true), help: "Tamper protection can reject this policy.");
        Single("unwanted-apps", "Potentially unwanted applications", "Detect potentially unwanted apps",
            "Defender", Defender, "PUAProtection", Modes(), help: "Audit only records detections without blocking apps.");
        Single("cloud-protection", "Cloud-delivered protection", "Use Microsoft cloud threat detection",
            "Defender", Defender + @"\Spynet", "SpynetReporting",
            [new("off", "Off", "0"), new("basic", "Basic (legacy)", "1"), new("advanced", "Advanced", "2")], help: "Basic (legacy) is an older Microsoft MAPS policy value.");
        Single("sample-submission", "Automatic sample submission", "Send suspicious files to Microsoft",
            "Defender", Defender + @"\Spynet", "SubmitSamplesConsent",
            [new("prompt", "Ask first", "0"), new("safe", "Send safe samples", "1"), new("never", "Never send", "2"), new("all", "Send all samples", "3")], help: "Files can contain personal information.");
        Single("block-first-sight", "Block at first sight", "Check suspicious content before it runs",
            "Defender", Defender + @"\Spynet", "DisableBlockAtFirstSeen", Boolean(true), help: "Requires cloud protection and automatic sample submission.");
        Single("controlled-folders", "Controlled folder access", "Protect folders from untrusted changes",
            "Defender", Defender + @"\Windows Defender Exploit Guard\Controlled Folder Access", "EnableControlledFolderAccess",
            [.. Modes(), new("disk-block", "Block disk changes only", "3"), new("disk-audit", "Audit disk changes only", "4")], help: "Requires Microsoft Defender Antivirus. Disk-only options protect disk sectors instead of folders.");
        Single("network-protection", "Network protection", "Check connections to malicious destinations",
            "Defender", Defender + @"\Windows Defender Exploit Guard\Network Protection", "EnableNetworkProtection", Modes(), help: "Requires Microsoft Defender Antivirus. Audit only records connections without blocking them.");
        Single("behavior-monitoring", "Monitor program behavior", "Detect suspicious program behavior",
            "Defender", Defender + @"\Real-Time Protection", "DisableBehaviorMonitoring", Boolean(true), help: "Requires Microsoft Defender Antivirus.");
        Single("file-activity-monitoring", "Monitor file and program activity", "Check file and program activity",
            "Defender", Defender + @"\Real-Time Protection", "DisableOnAccessProtection", Boolean(true), help: "Requires Microsoft Defender Antivirus real-time protection.");
        Single("download-scanning", "Scan downloads and attachments", "Check downloads and attachments for threats",
            "Defender", Defender + @"\Real-Time Protection", "DisableIOAVProtection", Boolean(true), help: "Also required for block at first sight.");
        Single("script-scanning", "Scan scripts", "Check scripts for threats",
            "Defender", Defender + @"\Real-Time Protection", "DisableScriptScanning", Boolean(true), help: "Requires Microsoft Defender Antivirus.");
        Single("scan-on-protection-enable", "Scan processes when protection resumes", "Scan running processes when protection resumes",
            "Defender", Defender + @"\Real-Time Protection", "DisableScanOnRealtimeEnable", Boolean(true));
        Single("cloud-block-level", "Cloud blocking level", "Set how strictly cloud protection blocks apps",
            "Defender", Defender + @"\MpEngine", "MpCloudBlockLevel",
            [new("normal", "Default blocking", "0"), new("moderate", "Moderate", "1"), new("high", "High", "2"),
             new("high-plus", "High plus", "4"), new("zero-tolerance", "Zero tolerance", "6")], help: "Requires cloud protection. Higher levels can block legitimate apps. Zero tolerance blocks all unknown executables.");
        Single("low-priority-scans", "Low CPU priority for scheduled scans", "Give other apps priority over scheduled scans",
            "Scanning", Defender + @"\Scan", "LowCpuPriority", Boolean());
        Single("heuristic-detection", "Detect unfamiliar threats", "Detect threats beyond known signatures",
            "Scanning", Defender + @"\Scan", "DisableHeuristics", Boolean(true), help: "Uses heuristic analysis to identify suspicious files.");
        Single("archive-scanning", "Scan archive files", "Scan files inside archives",
            "Scanning", Defender + @"\Scan", "DisableArchiveScanning", Boolean(true), help: "Direct scans of archives still scan their contents when this is off.");
        Single("network-file-scanning", "Scan network files", "Scan files on network shares",
            "Scanning", Defender + @"\Scan", "DisableScanningNetworkFiles", Boolean(true), help: "Does not add mapped network drives to full scans.");
        Single("update-before-scan", "Check for updates before scheduled scans", "Update threat definitions before scheduled scans",
            "Scanning", Defender + @"\Scan", "CheckForSignaturesBeforeRunningScan", Boolean(), help: "Does not affect manually started scans.");
        Single("intelligence-on-battery", "Allow threat updates on battery", "Update threat definitions on battery power",
            "Threat updates", Defender + @"\Signature Updates", "DisableScheduledSignatureUpdateOnBattery", Boolean(true));
        Single("intelligence-on-startup", "Check for threat updates at startup", "Check for threat updates when Defender starts",
            "Threat updates", Defender + @"\Signature Updates", "UpdateOnStartUp", Boolean());
        settings.Add(new("smartscreen", "Downloaded app reputation checks", "Check downloaded apps for a trusted reputation",
            "App protection", WindowsSku.Enterprise,
            [new(WindowsSystem, "EnableSmartScreen"), new(WindowsSystem, "ShellSmartScreenLevel", ChangeValueType.Registry_String)],
            [new("default", "Not configured", "", ""), new("off", "Off", "0", ""), new("warn", "Warn", "1", "Warn"), new("block", "Warn and prevent bypass", "1", "Block")],
            "Warn allows you to continue past a warning. Warn and prevent bypass does not."));
        settings.Add(new("app-install-control", "App installation sources", "Choose where apps can be installed from",
            "App protection", WindowsSku.Enterprise,
            [new(Defender + @"\SmartScreen", "ConfigureAppInstallControlEnabled"), new(Defender + @"\SmartScreen", "ConfigureAppInstallControl", ChangeValueType.Registry_String)],
            [new("default", "Not configured", "", ""), new("disabled", "Policy disabled", "0", ""),
             new("anywhere", "Allow apps from anywhere", "1", "Anywhere"), new("recommend", "Recommend Microsoft Store alternatives", "1", "Recommendations"),
             new("warn", "Warn about apps outside Microsoft Store", "1", "PreferStore"), new("store", "Microsoft Store only", "1", "StoreOnly")], "Requires SmartScreen."));
        Single("phishing-protection", "Enhanced phishing protection", "Protect supported sign-in passwords",
            "App protection", Phishing, "ServiceEnabled", Boolean(), help: "Windows Hello-only sign-in does not supply a password to monitor.");
        Single("phishing-malicious", "Warn about malicious sites and apps", "Warn when a sign-in password is entered into a malicious site or app",
            "App protection", Phishing, "NotifyMalicious", Boolean(), help: "Requires phishing protection and a supported sign-in password.");
        Single("phishing-reuse", "Warn about password reuse", "Warn when a sign-in password is reused elsewhere",
            "App protection", Phishing, "NotifyPasswordReuse", Boolean(), help: "Requires phishing protection and a supported sign-in password.");
        Single("phishing-storage", "Warn about unsafe password storage", "Warn when a sign-in password is saved in an unsafe app",
            "App protection", Phishing, "NotifyUnsafeApp", Boolean(), help: "Requires phishing protection and a supported sign-in password.");
        settings.Add(new("security-notifications", "Windows Security notifications", "Choose which security alerts to show",
            "Notifications", WindowsSku.Enterprise,
            [new(Notifications, "DisableNotifications"), new(Notifications, "DisableEnhancedNotifications")],
            [new("default", "Not configured", "", ""), new("all", "Show all alerts", "0", "0"),
             new("critical", "Show critical alerts only", "0", "1"), new("none", "Hide all alerts", "1", "1")], "Hide all alerts also hides critical alerts."));
        Single("defender-startup-priority", "Defender startup priority", "Set Defender service startup priority",
            "Defender", Defender, "AllowFastServiceStartup", [new("normal", "Normal", "1"), new("low", "Low", "0")], help: "Normal starts Defender sooner but can compete with other startup tasks.");
        Single("automatic-remediation", "Act on detected threats automatically", "Let Defender choose an action for detected threats",
            "Defender", Defender, "DisableRoutinelyTakingAction", Boolean(true), help: "Off asks you to choose an action for detected threats.");
        Single("file-hash-computation", "Compute hashes for scanned files", "Calculate file fingerprints during scans",
            "Defender", Defender + @"\MpEngine", "EnableFileHashComputation", Boolean(), help: "This can add scan overhead.");
        Single("scan-direction", "File monitoring direction", "Choose which file transfers to monitor",
            "Scanning", Defender + @"\Real-Time Protection", "RealtimeScanDirection",
            [new("both", "Incoming and outgoing files", "0"), new("incoming", "Incoming files only", "1"), new("outgoing", "Outgoing files only", "2")], help: "Applies to NTFS volumes. Other file systems keep full monitoring.");
        Single("randomize-schedules", "Randomize scheduled start times", "Vary scheduled scan and threat-update start times",
            "Scanning", Defender, "RandomizeScheduleTaskTimes", Boolean(), help: "Uses the configured randomization window.");
        Single("restore-before-cleaning", "Create restore points before cleaning", "Create a restore point before removing threats",
            "Scanning", Defender + @"\Scan", "DisableRestorePoint", Boolean(true), help: "Requests a daily restore point. Requires System Protection support.");
        Single("catchup-full-scan", "Catch up missed full scans", "Run full scans after missed schedules",
            "Scanning", Defender + @"\Scan", "DisableCatchupFullScan", Boolean(true), help: "Requires a full-scan schedule and repeated missed scans.");
        Single("catchup-quick-scan", "Catch up missed quick scans", "Run quick scans after missed schedules",
            "Scanning", Defender + @"\Scan", "DisableCatchupQuickScan", Boolean(true), help: "Requires a quick-scan schedule and repeated missed scans.");
        Single("reparse-point-scanning", "Scan through file-system links", "Follow file-system links during scans",
            "Scanning", Defender + @"\Scan", "DisableReparsePointScanning", Boolean(true), help: "Includes reparse points such as directory junctions. Following linked paths can slow scanning.");
        Single("rapid-intelligence", "Receive rapid threat updates", "Receive threat updates in response to cloud reports",
            "Threat updates", Defender + @"\Signature Updates", "RealtimeSignatureDelivery", Boolean(), help: "Requires Microsoft MAPS cloud protection.");
        Single("intelligence-from-microsoft-update", "Use Microsoft Update for threat updates", "Allow threat updates from Microsoft Update",
            "Threat updates", Defender + @"\Signature Updates", "ForceUpdateFromMU", Boolean(), help: "Applies even when another update source is configured.");
        Single("cloud-signature-corrections", "Accept cloud corrections for false detections", "Let cloud protection correct false detections",
            "Threat updates", Defender + @"\Signature Updates", "SignatureDisableNotification", Boolean(), help: "Allows MAPS to disable individual threat signatures causing false detections. Requires cloud protection.");
        Single("scan-after-intelligence-update", "Scan after threat updates", "Scan after updating threat definitions",
            "Threat updates", Defender + @"\Signature Updates", "DisableScanOnUpdate", Boolean(true));
        Single("defender-error-reporting", "Send Defender error reports", "Send Defender diagnostics to Microsoft",
            "Notifications", Defender + @"\Reporting", "DisableGenericRePorts", Boolean(true), help: "Uses Defender Watson diagnostic events, separate from cloud threat reports.");
        Single("defender-enhanced-notifications", "Show Defender activity notifications", "Show routine Defender activity alerts",
            "Notifications", Defender + @"\Reporting", "DisableEnhancedNotifications", Boolean(true), help: "Windows Security notification policies can still hide these alerts.");
        Single("defender-reboot-notifications", "Show Defender restart notifications", "Show Defender restart notices",
            "Notifications", Defender + @"\UX Configuration", "SuppressRebootNotification", Boolean(true), help: "Applies to Defender's UI-only mode. Does not control Windows Update restart notices.");
        Single("allow-exploit-protection-edits", "Allow local exploit-protection edits", "Allow changes to exploit protection in Windows Security",
            "App protection", @"HKLM\SOFTWARE\Policies\Microsoft\Windows Defender Security Center\App and Browser protection",
            "DisallowExploitProtectionOverride", Boolean(true), help: "Does not turn protection on or off.");
        Single("merge-local-defender-lists", "Merge local Defender lists with policy", "Include local exclusions and threat lists",
            "Policy overrides", Defender, "DisableLocalAdminMerge", Boolean(true), help: "Merges local administrator lists with Group Policy lists. Group Policy wins conflicts.");
        void Override(string id, string title, string key, string name) => Single(id, title,
            "Choose which settings take priority",
            "Policy overrides", key, name, [new("policy", "Use Group Policy", "0"), new("local", "Use local preference", "1")],
            help: "Does not change the preference itself.");
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
