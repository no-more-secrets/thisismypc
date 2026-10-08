using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Modules;

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
        Single("low-priority-scans", "Low CPU priority for scheduled scans", "Lets other applications take priority over scheduled antivirus scans.",
            "Defender", Defender + @"\Scan", "LowCpuPriority", Boolean());
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
        return settings;
    }

    public static bool Allows(ChangeDescriptor change)
    {
        var setting = Settings.FirstOrDefault(s => s.Id == change.SettingId);
        if (setting is null || change.ModuleId != ModuleId) return false;
        var index = setting.Targets.ToList().FindIndex(t => t.Location.Equals(change.SystemLocation, StringComparison.OrdinalIgnoreCase) && t.Type == change.ValueType);
        return index >= 0 && change.Enforcement?.SkuRestriction == setting.Edition
            && !change.Enforcement.AclElevation && !change.Enforcement.OwnerModeRequired
            && change.Enforcement.CompanionServices is not { Count: > 0 }
            && change.Enforcement.CompanionTasks is not { Count: > 0 }
            && change.Enforcement.GPCacheEntries is not { Count: > 0 }
            && change.Enforcement.ReversionVectors is not { Count: > 0 }
            && setting.Choices.Any(o => o.Values[index] == change.BeforeValue)
            && setting.Choices.Any(o => o.Values[index] == change.AfterValue);
    }
}
