using System.Collections.Immutable;
using System.Text;
using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;

namespace ThisIsMyPC.Core.Policies;

public sealed record PolicyControlState(string? Message = null, bool BlocksChanges = false, bool? ToggleState = null, int? ChoiceValue = null)
{
    public static PolicyControlState None { get; } = new();
}

/// <summary>Reads current policy values and saved local sources without changing either.</summary>
public sealed class PolicyControlStateReader(IRegistryService registry,
    Func<IReadOnlyList<PolicySourceSnapshot>>? readSources = null, ICapabilityDetector? capabilityDetector = null)
{
    public bool HasLocalSourceReader => readSources is not null;
    public IReadOnlyList<PolicySourceSnapshot> ReadLocalSources() => readSources?.Invoke() ?? [];

    public PolicyControlState Read(string moduleId, string settingId, string? ownedLocation = null, ChangeValueType? valueType = null)
    {
        var messages = new List<string>();
        var blocked = false;
        bool? toggle = null;
        int? choice = null;
        var conflicting = false;
        IReadOnlyList<PolicySourceSnapshot>? sources = null;
        IReadOnlyList<PolicySourceSnapshot> Sources()
        {
            if (sources is not null) return sources;
            try { return sources = readSources?.Invoke() ?? []; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.Security.SecurityException)
            {
                return sources = [new("Local computer policy", PolicyScope.Machine, PolicyFileStatus.Unreadable, [], ex.Message),
                    new("Local user policy", PolicyScope.User, PolicyFileStatus.Unreadable, [], ex.Message)];
            }
        }

        foreach (var rule in PolicyControlCatalog.Rules.Where(r => r.ModuleId == moduleId && r.SettingId == settingId))
        {
            var matches = true;
            var matchedSource = false;
            var supported = capabilityDetector is null || rule.Conditions.All(condition =>
                SettingEditionSupport.BlockReason(capabilityDetector.Sku, condition.Location, null,
                    condition.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)) is null);
            foreach (var condition in rule.Conditions)
            {
                var value = ReadValue(condition.Location);
                var evidence = SavedEvidence(condition.Location, value, Sources());
                if (evidence == "Controlled by saved local policy.") matchedSource = true;
                else if (evidence is not null) { messages.Add(evidence); blocked |= supported; }
                if (!value.IsSuccess)
                {
                    matches = false;
                    if (value.ErrorCategory != ErrorCategory.NotFound)
                    { messages.Add("A policy value could not be read. Its state is unknown."); blocked |= supported; }
                }
                else if (value.Value!.Kind != RegistryValueDataKind.DWord)
                { matches = false; messages.Add("A policy value has an unexpected type. Its state is unknown."); blocked |= supported; }
                else if (!(condition.ValidValues ?? (condition.Location.EndsWith("\\AllowTelemetry", StringComparison.OrdinalIgnoreCase) ? [0, 1, 3]
                    : condition.Location.EndsWith("\\UpdateNotificationLevel", StringComparison.OrdinalIgnoreCase) ? [0, 1, 2] : new[] { 0, 1 })).Contains(value.Value.AsDWord()))
                { matches = false; messages.Add("A policy value is not recognized. Its state is unknown."); blocked |= supported; }
                else if (value.Value.AsDWord() != condition.Value) matches = false;
            }
            if (!matches) continue;
            if (!supported)
            {
                messages.Add("A policy value is configured, but its effect is not verified on this Windows edition.");
                continue;
            }
            messages.Add(rule.Message);
            if (matchedSource) messages.Add("Controlled by saved local policy.");
            blocked |= rule.BlocksChanges;
            if (toggle is not null && rule.ToggleState is not null && toggle != rule.ToggleState) conflicting = true;
            toggle ??= rule.ToggleState;
            choice ??= rule.ChoiceValue;
        }

        var locations = PolicyControlCatalog.CompanionLocations(moduleId, settingId)
            .Concat(ownedLocation is not null && IsRegistryLocation(ownedLocation) && IsPolicyLocation(ownedLocation) ? [ownedLocation] : [])
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var configuredCount = 0;
        foreach (var location in locations)
        {
            var value = ReadValue(location);
            if (!value.IsSuccess && value.ErrorCategory != ErrorCategory.NotFound)
            { messages.Add("The policy value could not be read. Its state is unknown."); blocked = true; }
            else if (value.IsSuccess)
            {
                configuredCount++;
                var expected = (location == ownedLocation && valueType == ChangeValueType.Registry_String)
                    || location.EndsWith("\\ProductVersion", StringComparison.OrdinalIgnoreCase)
                    || location.EndsWith("\\TargetReleaseVersionInfo", StringComparison.OrdinalIgnoreCase)
                    ? RegistryValueDataKind.String : RegistryValueDataKind.DWord;
                if (value.Value!.Kind != expected)
                { messages.Add("The policy value has an unexpected type. Its state is unknown."); blocked = true; }
                else if (expected == RegistryValueDataKind.DWord && !IsKnownValue(location, value.Value.AsDWord()))
                { messages.Add("The policy value is not recognized. Its state is unknown."); blocked = true; }
                else if (locations.Count == 1) messages.Add("Current policy value: " + value.Value.Data + ".");
            }
            var saved = SavedEvidence(location, value, Sources());
            if (saved is not null) { messages.Add(saved); blocked = true; }
        }
        if (locations.Count > 1 && configuredCount > 0)
            messages.Add(configuredCount == locations.Count ? "Policy values are configured." : "Some policy values are configured; others are not set.");

        if (conflicting) { messages.Add("Policy values conflict. The resulting state is unknown."); blocked = true; }
        if (messages.Any(message => message.Contains("unknown", StringComparison.Ordinal)
            || message.Contains("differs", StringComparison.Ordinal))) { toggle = null; choice = null; }
        return new(messages.Count == 0 ? null : string.Join(" ", messages.Distinct(StringComparer.Ordinal)), blocked, toggle, choice);
    }

    public PolicyControlState Read(ChangeDescriptor change)
    {
        if (LocalPolicyValue.IsPolicyType(change.ValueType))
        {
            try
            {
                var before = LocalPolicyValue.Decode(change.BeforeValue);
                var current = LocalPolicyValue.Read(change.SystemLocation, change.ValueType, ReadLocalSources(), ReadValue(change.SystemLocation));
                return before == current ? PolicyControlState.None : new("The saved or current policy changed. Refresh before applying.", true);
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException or System.Security.SecurityException or DecoderFallbackException)
            { return new(ex.Message, true); }
        }
        if (change.ValueType == ChangeValueType.PowerPlan_Setting)
        {
            var parts = change.SystemLocation.Split('/');
            if (parts.Length == 4 && Guid.TryParse(parts[2], out var setting) && parts[3] is "AC" or "DC")
                return ReadPowerSetting(setting, parts[3] == "AC");
        }
        return Read(change.ModuleId, change.SettingId, change.SystemLocation, change.ValueType);
    }

    public PolicyControlState ReadPowerSetting(Guid setting, bool ac)
    {
        var location = $@"HKLM\SOFTWARE\Policies\Microsoft\Power\PowerSettings\{setting:D}\{(ac ? "AC" : "DC")}SettingIndex";
        var value = ReadValue(location);
        if (capabilityDetector is not null && SettingEditionSupport.BlockReason(capabilityDetector.Sku, location, null) is not null)
            return value.IsSuccess
                ? new("A power policy value is configured, but its effect is not verified on this Windows edition.")
                : PolicyControlState.None;
        var state = Read("Power", "", location);
        if (!value.IsSuccess || value.Value!.Kind != RegistryValueDataKind.DWord || !IsKnownValue(location, value.Value.AsDWord())) return state;
        return state with { BlocksChanges = true, Message = "Controlled by power policy. " + state.Message };
    }

    private static bool IsKnownValue(string location, int value)
    {
        if (location.Equals(@"HKLM\SOFTWARE\Policies\Microsoft\Windows Defender\MpEngine\MpCloudBlockLevel", StringComparison.OrdinalIgnoreCase))
            return value is 0 or 1 or 2 or 4 or 6;
        if (location.Equals(@"HKLM\SOFTWARE\Policies\Microsoft\Windows Defender\Real-Time Protection\RealtimeScanDirection", StringComparison.OrdinalIgnoreCase))
            return value is 0 or 1 or 2;
        if (location.EndsWith("\\PUAProtection", StringComparison.OrdinalIgnoreCase)
            || location.EndsWith("\\SpynetReporting", StringComparison.OrdinalIgnoreCase)
            || location.EndsWith("\\EnableNetworkProtection", StringComparison.OrdinalIgnoreCase)) return value is 0 or 1 or 2;
        if (location.EndsWith("\\SubmitSamplesConsent", StringComparison.OrdinalIgnoreCase)) return value is >= 0 and <= 3;
        if (location.EndsWith("\\EnableControlledFolderAccess", StringComparison.OrdinalIgnoreCase)) return value is >= 0 and <= 4;
        if (location.EndsWith("\\AUOptions", StringComparison.OrdinalIgnoreCase)) return value is 2 or 3 or 4 or 5 or 7;
        if (location.EndsWith("\\AllowTelemetry", StringComparison.OrdinalIgnoreCase)) return value is 0 or 1 or 3;
        if (location.EndsWith("\\DODownloadMode", StringComparison.OrdinalIgnoreCase)) return value is 0 or 1 or 2 or 3 or 99 or 100;
        if (location.Contains(@"\abfc2519-3608-4c2a-94ea-171b0ed546ab\", StringComparison.OrdinalIgnoreCase)) return value is 0 or 1;
        if (location.Contains(@"\Microsoft\Power\", StringComparison.OrdinalIgnoreCase)) return true;
        return value is 0 or 1;
    }

    private static bool IsRegistryLocation(string location) => location.StartsWith("HKLM\\", StringComparison.OrdinalIgnoreCase)
        || location.StartsWith("HKCU\\", StringComparison.OrdinalIgnoreCase);

    private OperationResult<RegistryValueData> ReadValue(string location)
    {
        var split = location.LastIndexOf('\\');
        return registry.ReadValue(location[..split], location[(split + 1)..]);
    }

    public static bool IsPolicyLocation(string location) =>
        location.Contains(@"\Software\Policies\", StringComparison.OrdinalIgnoreCase)
        || location.Contains(@"\CurrentVersion\Policies\", StringComparison.OrdinalIgnoreCase);

    private static string? SavedEvidence(string location, OperationResult<RegistryValueData> actual,
        IReadOnlyList<PolicySourceSnapshot> sources)
    {
        if (sources.Count == 0) return null;
        var split = location.LastIndexOf('\\');
        var scope = location.StartsWith("HKLM\\", StringComparison.OrdinalIgnoreCase) ? PolicyScope.Machine : PolicyScope.User;
        var key = location[5..split];
        var name = location[(split + 1)..];
        var definition = new PolicyDefinition(name, name, "", "", key, name, [scope], 1, 0, "", "", "");
        // Project exact value equality into a boolean policy. The existing parser still
        // handles source order and every deletion directive, including parent deletion.
        var projected = sources.Select(source => source with
        {
            Entries = source.Entries.IsDefault ? default : source.Entries.Select(entry =>
                !entry.IsDirective && string.Equals(entry.KeyPath, key, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(entry.ValueName, name, StringComparison.OrdinalIgnoreCase)
                    ? entry with { ValueType = 4, Data = ImmutableArray.Create<byte>(Matches(entry, actual) ? (byte)1 : (byte)0, 0, 0, 0) }
                    : entry).ToImmutableArray(),
        });
        var observation = PolicyStateReader.Read(definition, scope, projected,
            actual.IsSuccess ? OperationResult<RegistryValueData>.Success(RegistryValueData.FromDWord(1)) : actual);
        if (observation.SavedState == PolicyState.Unknown)
            return "Saved local policy could not be read. Its state is unknown.";
        if (observation.SavedState == PolicyState.NotConfigured) return null;
        return observation.Comparison == PolicyComparison.Matches
            ? "Controlled by saved local policy."
            : "Saved local policy differs from the current registry value.";
    }

    private static bool Matches(RegistryPolicyEntry entry, OperationResult<RegistryValueData> actual)
    {
        if (!actual.IsSuccess) return false;
        var value = actual.Value!;
        if (entry.ValueType == 4 && value.Kind == RegistryValueDataKind.DWord)
            return entry.Data.AsSpan().SequenceEqual(BitConverter.GetBytes(value.AsDWord()));
        return entry.ValueType == 1 && value.Kind == RegistryValueDataKind.String
            && entry.Data.AsSpan().SequenceEqual(Encoding.Unicode.GetBytes(value.Data + '\0'));
    }
}
