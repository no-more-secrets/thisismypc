using System.Collections.Immutable;
using System.Globalization;
using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Data;
using ThisIsMyPC.Core.Services;

namespace ThisIsMyPC.Core.Drift;

public enum RestorationValidationOutcome
{
    Accepted,
    /// <summary>SystemLocation has no key/value separator, an empty value name, or stray whitespace.</summary>
    MalformedLocation,
    /// <summary>The key and value name are not in the catalog.</summary>
    UnknownTarget,
    /// <summary>The location is known but the module or setting id does not belong to it.</summary>
    IdentityMismatch,
    /// <summary>The entry's ValueType differs from the catalog's.</summary>
    ValueTypeMismatch,
    /// <summary>ExpectedValue is not valid data for the value type (includes the absent sentinel).</summary>
    MalformedValue,
    /// <summary>ExpectedValue is valid data but not one of the target's allowed values.</summary>
    ValueNotAllowed,
    /// <summary>EnforcementJson is present but does not parse.</summary>
    MalformedEnforcement,
    /// <summary>Enforcement asks for companion or elevation work this batch cannot perform, or claims an edition tier.</summary>
    IncompatibleEnforcement,
    /// <summary>A user-hive target with no user SID to resolve HKCU against.</summary>
    UserIdentityMissing,
    /// <summary>The user SID is not a well-formed account SID, or names a service account.</summary>
    MalformedUserSid,
}

/// <summary>
/// A baseline entry that passed catalog validation: the exact target, the
/// canonical value to restore, and the profile it belongs to. The SID travels
/// with the candidate so a later machine-scoped pass can restore every
/// profile's own values, not only the consenting user's.
/// </summary>
public sealed record RestorationCandidate
{
    public required RestorationTarget Target { get; init; }
    public required RegistryValueData DesiredValue { get; init; }

    /// <summary>Profile the value belongs to; null only for machine-scope targets (none in this batch).</summary>
    public required string? UserSid { get; init; }

    /// <summary>Key path a SYSTEM process reads and writes: HKU\{sid}\... for user-hive targets.</summary>
    public required string ResolvedKeyPath { get; init; }

    public string ValueName => Target.ValueName;
}

public sealed record RestorationValidation
{
    public required RestorationValidationOutcome Outcome { get; init; }
    public required string Detail { get; init; }
    public RestorationCandidate? Candidate { get; init; }

    public bool IsAccepted => Outcome == RestorationValidationOutcome.Accepted;

    internal static RestorationValidation Reject(RestorationValidationOutcome outcome, string detail)
        => new() { Outcome = outcome, Detail = detail };
}

/// <summary>
/// The closed, immutable list of values Owner Mode restoration may write, and
/// the gate every drift-baseline entry passes before it can become a
/// restoration candidate. The gate matches identity exactly (module id, setting
/// id, key path, value name, value type), accepts only the target's listed
/// values in canonical form, refuses enforcement it cannot honor, and requires
/// a valid user SID for user-hive targets. A baseline entry that fails here is
/// reported, never written.
/// </summary>
public sealed class RestorationCatalog
{
    private const string UserHiveRoot = @"HKCU\";
    private const string WindowsAnnoyancesModuleId = "Windows Annoyances";

    private readonly ImmutableDictionary<string, RestorationTarget> _byLocation;

    /// <summary>The shipped catalog. Service code uses this; custom catalogs are for tests.</summary>
    public static RestorationCatalog Default { get; } = new(AnnoyancesTargets());

    public RestorationCatalog(IEnumerable<RestorationTarget> targets)
    {
        ArgumentNullException.ThrowIfNull(targets);
        var list = targets.ToImmutableArray();
        var byLocation = ImmutableDictionary.CreateBuilder<string, RestorationTarget>(StringComparer.OrdinalIgnoreCase);
        foreach (var target in list)
        {
            Check(target);
            var location = LocationKey(target.KeyPath, target.ValueName);
            if (byLocation.ContainsKey(location))
                throw new ArgumentException($"Duplicate catalog target: {location}", nameof(targets));
            byLocation.Add(location, target);
        }

        Targets = list;
        _byLocation = byLocation.ToImmutable();
    }

    public ImmutableArray<RestorationTarget> Targets { get; }

    /// <summary>The target at a key and value name (case-insensitive, exact otherwise), or null.</summary>
    public RestorationTarget? FindByLocation(string keyPath, string valueName)
    {
        ArgumentNullException.ThrowIfNull(keyPath);
        ArgumentNullException.ThrowIfNull(valueName);
        return _byLocation.TryGetValue(LocationKey(keyPath, valueName), out var target) ? target : null;
    }

    /// <summary>
    /// Validates one baseline entry against the catalog. <paramref name="userSid"/>
    /// is the profile the baseline's HKCU entries belong to
    /// (<see cref="DriftBaselineDocument.UserSid"/>).
    /// </summary>
    public RestorationValidation Validate(DriftBaselineEntry entry, string? userSid)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (!TrySplitLocation(entry.SystemLocation, out var keyPath, out var valueName))
        {
            return RestorationValidation.Reject(
                RestorationValidationOutcome.MalformedLocation,
                $"'{entry.SystemLocation}' is not a key path plus value name.");
        }

        var target = FindByLocation(keyPath, valueName);
        if (target is null)
        {
            return RestorationValidation.Reject(
                RestorationValidationOutcome.UnknownTarget,
                $"'{entry.SystemLocation}' is not a restorable target.");
        }

        if (!string.Equals(target.ModuleId, entry.ModuleId, StringComparison.Ordinal)
            || !string.Equals(target.SettingId, entry.SettingId, StringComparison.Ordinal))
        {
            return RestorationValidation.Reject(
                RestorationValidationOutcome.IdentityMismatch,
                $"'{entry.SystemLocation}' belongs to {target.ModuleId}/{target.SettingId}, not {entry.ModuleId}/{entry.SettingId}.");
        }

        if (entry.ValueType != target.ValueType)
        {
            return RestorationValidation.Reject(
                RestorationValidationOutcome.ValueTypeMismatch,
                $"{target.SettingId} is {target.ValueType}, baseline says {entry.ValueType}.");
        }

        if (!TryParseDesired(entry.ExpectedValue, target.ValueType, out var desired))
        {
            return RestorationValidation.Reject(
                RestorationValidationOutcome.MalformedValue,
                $"'{entry.ExpectedValue}' is not valid {target.ValueType} data for {target.SettingId}.");
        }

        if (!target.AllowedDesiredValues.Contains(desired))
        {
            return RestorationValidation.Reject(
                RestorationValidationOutcome.ValueNotAllowed,
                $"{target.SettingId} may only be restored to one of its listed values; '{entry.ExpectedValue}' is not one.");
        }

        var enforcementOutcome = CheckEnforcement(entry.EnforcementJson, out var enforcementDetail);
        if (enforcementOutcome != RestorationValidationOutcome.Accepted)
            return RestorationValidation.Reject(enforcementOutcome, enforcementDetail);

        string resolvedKeyPath;
        if (target.IsUserHive)
        {
            if (string.IsNullOrEmpty(userSid))
            {
                return RestorationValidation.Reject(
                    RestorationValidationOutcome.UserIdentityMissing,
                    $"{target.SettingId} lives in a user profile and the baseline names no user SID.");
            }

            if (!IsAccountSid(userSid))
            {
                return RestorationValidation.Reject(
                    RestorationValidationOutcome.MalformedUserSid,
                    $"'{userSid}' is not a user account SID.");
            }

            resolvedKeyPath = $@"HKU\{userSid}\{target.KeyPath[UserHiveRoot.Length..]}";
        }
        else
        {
            resolvedKeyPath = target.KeyPath;
        }

        return new RestorationValidation
        {
            Outcome = RestorationValidationOutcome.Accepted,
            Detail = string.Empty,
            Candidate = new RestorationCandidate
            {
                Target = target,
                DesiredValue = desired,
                UserSid = target.IsUserHive ? userSid : null,
                ResolvedKeyPath = resolvedKeyPath,
            },
        };
    }

    /// <summary>
    /// Only informational enforcement is compatible with background restoration:
    /// reversion vectors describe why a value drifts and change nothing. Companion
    /// services or tasks, GPCache entries, ACL elevation, companion restoration,
    /// and Owner-Mode-required flags all need the shared reversible executor, which
    /// this batch does not wire. A SKU tier claim is refused because none of the
    /// catalog targets are policies and no tier support has been verified here.
    /// </summary>
    private static RestorationValidationOutcome CheckEnforcement(string? enforcementJson, out string detail)
    {
        detail = string.Empty;
        if (string.IsNullOrWhiteSpace(enforcementJson))
            return RestorationValidationOutcome.Accepted;

        var enforcement = EnforcementJson.Deserialize(enforcementJson);
        if (enforcement is null)
        {
            detail = "Enforcement metadata does not parse.";
            return RestorationValidationOutcome.MalformedEnforcement;
        }

        var problems = new List<string>(4);
        if (enforcement.CompanionServices is { Count: > 0 })
            problems.Add("companion services");
        if (enforcement.CompanionTasks is { Count: > 0 })
            problems.Add("companion tasks");
        if (enforcement.GPCacheEntries is { Count: > 0 })
            problems.Add("GPCache entries");
        if (enforcement.OwnerModeRequired)
            problems.Add("Owner Mode required flag");
        if (enforcement.AclElevation)
            problems.Add("ACL elevation");
        if (enforcement.RestoresCompanions)
            problems.Add("companion restoration");
        if (enforcement.SkuRestriction is not null)
            problems.Add("edition tier claim");

        if (problems.Count == 0)
            return RestorationValidationOutcome.Accepted;

        detail = $"Restoration cannot honor: {string.Join(", ", problems)}.";
        return RestorationValidationOutcome.IncompatibleEnforcement;
    }

    private static bool TryParseDesired(string expected, ChangeValueType valueType, out RegistryValueData desired)
    {
        desired = null!;
        if (valueType != ChangeValueType.Registry_DWord || expected is null)
            return false;
        var raw = new RegistryValueData(RegistryValueDataKind.DWord, expected);
        return RegistryValueSnapshot.TryCanonicalize(raw, out desired);
    }

    private static bool TrySplitLocation(string? location, out string keyPath, out string valueName)
    {
        keyPath = string.Empty;
        valueName = string.Empty;
        if (string.IsNullOrEmpty(location) || location != location.Trim())
            return false;
        var separator = location.LastIndexOf('\\');
        if (separator <= 0 || separator == location.Length - 1)
            return false;
        keyPath = location[..separator];
        valueName = location[(separator + 1)..];
        return keyPath.Length > 0 && !keyPath.EndsWith('\\');
    }

    // MS-DTYP 2.4.2: revision 1, a 48-bit identifier authority, one to fifteen
    // 32-bit sub-authorities, every number canonical decimal (no leading zeros).
    private const ulong MaxIdentifierAuthority = (1UL << 48) - 1;
    private const int MaxSubAuthorities = 15;
    private const int MaxSidTextLength = 184;

    // MS-DTYP 2.4.2.4 well-known domain-relative RIDs that name groups, not
    // accounts. User-created groups (RID 1000+) are not distinguishable by shape
    // and are excluded later by profile enumeration, not here.
    private static readonly HashSet<uint> WellKnownGroupRids =
    [
        498, 512, 513, 514, 515, 516, 517, 518, 519, 520, 521, 522, 525, 526, 527, 553, 571, 572,
    ];

    /// <summary>
    /// True only for the two SID shapes that name a Windows user profile:
    /// a local or domain account, S-1-5-21-{d1}-{d2}-{d3}-{rid}, with a RID that
    /// is not a well-known group; or a Microsoft Entra account, S-1-12-1-{a}-{b}-{c}-{d}.
    /// Every number is canonical decimal within its MS-DTYP width. Service,
    /// builtin, capability, and other principals fail the shape test. Pure string
    /// work; no Win32 lookups in Core.
    /// </summary>
    internal static bool IsAccountSid(string sid)
    {
        if (sid is null || sid.Length > MaxSidTextLength || !sid.StartsWith("S-1-", StringComparison.Ordinal))
            return false;

        var parts = sid[4..].Split('-');
        if (parts.Length < 2 || parts.Length > MaxSubAuthorities + 1)
            return false;
        if (!TryParseCanonical(parts[0], MaxIdentifierAuthority, out var authority))
            return false;

        var subAuthorities = new uint[parts.Length - 1];
        for (var i = 1; i < parts.Length; i++)
        {
            if (!TryParseCanonical(parts[i], uint.MaxValue, out var value))
                return false;
            subAuthorities[i - 1] = (uint)value;
        }

        if (subAuthorities.Length != 5)
            return false;

        return (authority, subAuthorities[0]) switch
        {
            (5, 21) => !WellKnownGroupRids.Contains(subAuthorities[4]),
            (12, 1) => true,
            _ => false,
        };
    }

    private static bool TryParseCanonical(string text, ulong max, out ulong value)
    {
        value = 0;
        if (text.Length is 0 or > 20 || (text.Length > 1 && text[0] == '0'))
            return false;
        foreach (var c in text)
        {
            if (!char.IsAsciiDigit(c))
                return false;
        }

        return ulong.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value) && value <= max;
    }

    private static string LocationKey(string keyPath, string valueName) => $@"{keyPath}\{valueName}";

    private static void Check(RestorationTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        RequireToken(target.ModuleId, nameof(target.ModuleId));
        RequireToken(target.SettingId, nameof(target.SettingId));
        RequireToken(target.DisplayName, nameof(target.DisplayName));
        RequireToken(target.Provenance, nameof(target.Provenance));
        RequireToken(target.KeyPath, nameof(target.KeyPath));
        RequireToken(target.ValueName, nameof(target.ValueName));

        if (!target.KeyPath.StartsWith(UserHiveRoot, StringComparison.OrdinalIgnoreCase)
            || target.KeyPath.Length <= UserHiveRoot.Length
            || target.KeyPath.EndsWith('\\')
            || target.KeyPath.Contains(@"\\", StringComparison.Ordinal))
        {
            throw new ArgumentException($"Catalog key paths must be HKCU\\... without empty segments: '{target.KeyPath}'.");
        }

        if (target.KeyPath.Contains(@"\Policies\", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"Policy keys are outside this catalog: '{target.KeyPath}'.");

        if (target.ValueName.Contains('\\') || target.ValueName == "(Default)")
            throw new ArgumentException($"Catalog value names must be a plain named value: '{target.ValueName}'.");

        if (target.ValueType != ChangeValueType.Registry_DWord)
            throw new ArgumentException($"Only Registry_DWord targets are supported: {target.SettingId} is {target.ValueType}.");

        if (target.AllowedDesiredValues.IsDefaultOrEmpty)
            throw new ArgumentException($"{target.SettingId} lists no allowed values.");

        foreach (var value in target.AllowedDesiredValues)
        {
            if (value is null || value.Kind != RegistryValueDataKind.DWord)
                throw new ArgumentException($"{target.SettingId} allows a non-DWORD value.");
            if (!RegistryValueSnapshot.TryCanonicalize(value, out var canonical) || canonical != value)
                throw new ArgumentException($"{target.SettingId} allows non-canonical value '{value.Data}'.");
        }

        if (target.AllowedDesiredValues.Distinct().Count() != target.AllowedDesiredValues.Length)
            throw new ArgumentException($"{target.SettingId} lists a value twice.");

        if (!target.AllowedDesiredValues.Contains(target.SuppressedValue)
            || !target.AllowedDesiredValues.Contains(target.WindowsDefaultValue))
        {
            throw new ArgumentException($"{target.SettingId} must list its suppressed and default values as allowed.");
        }
    }

    private static void RequireToken(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value) || value != value.Trim())
            throw new ArgumentException($"{name} must be non-empty without surrounding whitespace.");
    }

    /// <summary>
    /// Single-value, non-policy, HKCU DWORD toggles from Windows Annoyances,
    /// copied from AnnoyancesSettingsReader.ReadAll (key, value name, suppressed
    /// and default pair) and staged through AnnoyanceChangeFactory.CreateToggle
    /// or CreateDriftFragileToggle (AfterValue is always the explicit pair value,
    /// never a delete). Excluded on purpose: every Policies key, HKLM values,
    /// string-typed Flags, delete-to-restore values (feedback-frequency), grouped
    /// toggles, and anything with a restart requirement, because a background
    /// restore cannot restart Explorer or reboot.
    /// </summary>
    private static IEnumerable<RestorationTarget> AnnoyancesTargets()
    {
        const string module = WindowsAnnoyancesModuleId;
        const string reader = "AnnoyancesSettingsReader.ReadAll; AnnoyanceChangeFactory.CreateToggle";
        const string fragileReader = "AnnoyancesSettingsReader.ReadAll; AnnoyanceChangeFactory.CreateDriftFragileToggle";
        const string cdm = @"HKCU\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager";

        yield return RestorationTarget.DWordToggle(module, "scoobe-nags",
            "Suppress \"Finish setting up your device\" nags",
            @"HKCU\Software\Microsoft\Windows\CurrentVersion\UserProfileEngagement", "ScoobeSystemSettingEnabled",
            suppressed: 0, windowsDefault: 1, reader);
        yield return RestorationTarget.DWordToggle(module, "welcome-experience",
            "Suppress the Windows welcome experience",
            cdm, "SubscribedContent-310093Enabled", suppressed: 0, windowsDefault: 1, reader);
        yield return RestorationTarget.DWordToggle(module, "app-suggestions",
            "Suppress Start menu app suggestions",
            cdm, "SubscribedContent-338388Enabled", suppressed: 0, windowsDefault: 1, reader);
        yield return RestorationTarget.DWordToggle(module, "windows-tips",
            "Suppress Windows tips and \"Get started\" prompts",
            cdm, "SubscribedContent-338389Enabled", suppressed: 0, windowsDefault: 1, reader);
        yield return RestorationTarget.DWordToggle(module, "settings-suggestions",
            "Suppress suggestions in Settings and Start",
            cdm, "SystemPaneSuggestionsEnabled", suppressed: 0, windowsDefault: 1, reader);
        yield return RestorationTarget.DWordToggle(module, "lock-screen-images",
            "Turn off Windows Spotlight lock screen images",
            cdm, "RotatingLockScreenEnabled", suppressed: 0, windowsDefault: 1, reader);
        yield return RestorationTarget.DWordToggle(module, "silent-app-installs",
            "Suppress automatic promoted app installs",
            cdm, "SilentInstalledAppsEnabled", suppressed: 0, windowsDefault: 1, reader);
        yield return RestorationTarget.DWordToggle(module, "dynamic-search-box",
            "Suppress search highlights in the search box",
            @"HKCU\Software\Microsoft\Windows\CurrentVersion\SearchSettings", "IsDynamicSearchBoxEnabled",
            suppressed: 0, windowsDefault: 1, fragileReader);
        yield return RestorationTarget.DWordToggle(module, "advertising-id",
            "Disable the Advertising ID",
            @"HKCU\Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo", "Enabled",
            suppressed: 0, windowsDefault: 1, reader);
        yield return RestorationTarget.DWordToggle(module, "tailored-experiences",
            "Disable tailored experiences",
            @"HKCU\Software\Microsoft\Windows\CurrentVersion\Privacy", "TailoredExperiencesWithDiagnosticDataEnabled",
            suppressed: 0, windowsDefault: 1, reader);
        yield return RestorationTarget.DWordToggle(module, "language-list-access",
            "Block website access to your language list",
            @"HKCU\Control Panel\International\User Profile", "HttpAcceptLanguageOptOut",
            suppressed: 1, windowsDefault: 0, reader);
    }
}
