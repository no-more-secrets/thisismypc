using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Drift;
using ThisIsMyPC.Core.Enforcement;
using ThisIsMyPC.Core.Services;

namespace ThisIsMyPC.Core.Tests.Drift;

public sealed class RestorationCatalogTests
{
    private const string Module = "Windows Annoyances";
    private const string Cdm = @"HKCU\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager";
    private const string UserSid = "S-1-5-21-1111111111-2222222222-3333333333-1001";

    private static readonly RestorationCatalog Catalog = RestorationCatalog.Default;

    private static DriftBaselineEntry Entry(
        string location = Cdm + @"\SubscribedContent-338388Enabled",
        string moduleId = Module,
        string settingId = "app-suggestions",
        ChangeValueType valueType = ChangeValueType.Registry_DWord,
        string expected = "0",
        string? enforcementJson = null) => new()
    {
        ModuleId = moduleId,
        SettingId = settingId,
        DisplayName = "Suppress Start menu app suggestions",
        SystemLocation = location,
        ValueType = valueType,
        ExpectedValue = expected,
        EnforcementJson = enforcementJson,
        UpdatedAtUtc = DateTimeOffset.UtcNow,
    };

    private static RestorationTarget Target(
        string settingId = "custom",
        string keyPath = @"HKCU\Software\Test",
        string valueName = "Value",
        int suppressed = 0,
        int windowsDefault = 1) => RestorationTarget.DWordToggle(
            Module, settingId, "Custom", keyPath, valueName, suppressed, windowsDefault, "test");

    // ---- shipped catalog: exact identities copied from AnnoyancesSettingsReader.ReadAll ----

    public static TheoryData<string, string, string, int, int> ShippedTargets => new()
    {
        { "scoobe-nags", @"HKCU\Software\Microsoft\Windows\CurrentVersion\UserProfileEngagement", "ScoobeSystemSettingEnabled", 0, 1 },
        { "welcome-experience", Cdm, "SubscribedContent-310093Enabled", 0, 1 },
        { "app-suggestions", Cdm, "SubscribedContent-338388Enabled", 0, 1 },
        { "windows-tips", Cdm, "SubscribedContent-338389Enabled", 0, 1 },
        { "settings-suggestions", Cdm, "SystemPaneSuggestionsEnabled", 0, 1 },
        { "lock-screen-images", Cdm, "RotatingLockScreenEnabled", 0, 1 },
        { "silent-app-installs", Cdm, "SilentInstalledAppsEnabled", 0, 1 },
        { "dynamic-search-box", @"HKCU\Software\Microsoft\Windows\CurrentVersion\SearchSettings", "IsDynamicSearchBoxEnabled", 0, 1 },
        { "advertising-id", @"HKCU\Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo", "Enabled", 0, 1 },
        { "tailored-experiences", @"HKCU\Software\Microsoft\Windows\CurrentVersion\Privacy", "TailoredExperiencesWithDiagnosticDataEnabled", 0, 1 },
        { "language-list-access", @"HKCU\Control Panel\International\User Profile", "HttpAcceptLanguageOptOut", 1, 0 },
    };

    [Theory]
    [MemberData(nameof(ShippedTargets))]
    public void Default_catalog_matches_the_annoyances_reader_definitions(
        string settingId, string keyPath, string valueName, int suppressed, int windowsDefault)
    {
        var target = Assert.Single(Catalog.Targets, t => t.SettingId == settingId);

        Assert.Equal(Module, target.ModuleId);
        Assert.Equal(keyPath, target.KeyPath);
        Assert.Equal(valueName, target.ValueName);
        Assert.Equal(ChangeValueType.Registry_DWord, target.ValueType);
        Assert.Equal(RegistryValueData.FromDWord(suppressed), target.SuppressedValue);
        Assert.Equal(RegistryValueData.FromDWord(windowsDefault), target.WindowsDefaultValue);
        Assert.Equal(
            new[] { target.SuppressedValue, target.WindowsDefaultValue },
            target.AllowedDesiredValues.ToArray());
        Assert.Same(target, Catalog.FindByLocation(keyPath, valueName));
    }

    [Fact]
    public void Default_catalog_has_exactly_the_listed_targets()
    {
        Assert.Equal(ShippedTargets.Count, Catalog.Targets.Length);
        Assert.All(Catalog.Targets, t => Assert.True(t.IsUserHive));
        Assert.All(Catalog.Targets, t => Assert.DoesNotContain(@"\Policies\", t.KeyPath, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(Catalog.Targets.Length, Catalog.Targets.Select(t => t.SettingId).Distinct(StringComparer.Ordinal).Count());
    }

    // ---- immutability ----

    [Fact]
    public void Targets_cannot_be_added_to_or_cleared()
    {
        var list = (IList<RestorationTarget>)Catalog.Targets;

        Assert.True(list.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => list.Add(Target()));
        Assert.Throws<NotSupportedException>(() => list.Clear());
        Assert.Throws<NotSupportedException>(() => list[0] = Target());
    }

    [Fact]
    public void Allowed_values_cannot_be_changed_on_a_shipped_target()
    {
        var values = (IList<RegistryValueData>)Catalog.Targets[0].AllowedDesiredValues;

        Assert.True(values.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => values.Add(RegistryValueData.FromDWord(2)));
    }

    [Fact]
    public void Default_catalog_is_one_shared_instance()
    {
        Assert.Same(RestorationCatalog.Default, RestorationCatalog.Default);
    }

    // ---- catalog construction rules ----

    [Fact]
    public void Constructor_rejects_duplicate_locations_regardless_of_case()
    {
        var ex = Assert.Throws<ArgumentException>(() => new RestorationCatalog([
            Target(settingId: "a", keyPath: @"HKCU\Software\Test", valueName: "Value"),
            Target(settingId: "b", keyPath: @"hkcu\software\test", valueName: "VALUE"),
        ]));
        Assert.Contains("Duplicate", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(@"HKLM\SOFTWARE\Microsoft\Windows")]
    [InlineData(@"HKCU\Software\Policies\Microsoft\Windows\Explorer")]
    [InlineData(@"HKCU\")]
    [InlineData(@"HKCU\Software\Test\")]
    [InlineData(@"HKCU\Software\\Test")]
    [InlineData(@"HKEY_CURRENT_USER\Software\Test")]
    [InlineData(@"Software\Test")]
    public void Constructor_rejects_paths_outside_the_hkcu_non_policy_shape(string keyPath)
    {
        Assert.Throws<ArgumentException>(() => new RestorationCatalog([Target(keyPath: keyPath)]));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" Value")]
    [InlineData("(Default)")]
    [InlineData(@"Sub\Value")]
    public void Constructor_rejects_bad_value_names(string valueName)
    {
        Assert.Throws<ArgumentException>(() => new RestorationCatalog([Target(valueName: valueName)]));
    }

    [Fact]
    public void Constructor_rejects_non_dword_targets()
    {
        var stringTarget = Target() with
        {
            ValueType = ChangeValueType.Registry_String,
            SuppressedValue = RegistryValueData.FromString("506"),
            WindowsDefaultValue = RegistryValueData.FromString("510"),
            AllowedDesiredValues = [RegistryValueData.FromString("506"), RegistryValueData.FromString("510")],
        };
        Assert.Throws<ArgumentException>(() => new RestorationCatalog([stringTarget]));

        var mixedKinds = Target() with { AllowedDesiredValues = [RegistryValueData.FromDWord(0), RegistryValueData.FromString("1")] };
        Assert.Throws<ArgumentException>(() => new RestorationCatalog([mixedKinds]));
    }

    [Fact]
    public void Constructor_rejects_empty_duplicate_or_non_canonical_allowed_values()
    {
        Assert.Throws<ArgumentException>(() => new RestorationCatalog([Target() with { AllowedDesiredValues = [] }]));
        Assert.Throws<ArgumentException>(() => new RestorationCatalog([Target() with { AllowedDesiredValues = default }]));
        Assert.Throws<ArgumentException>(() => new RestorationCatalog([Target(suppressed: 1, windowsDefault: 1)]));
        Assert.Throws<ArgumentException>(() => new RestorationCatalog([Target() with
        {
            AllowedDesiredValues = [new RegistryValueData(RegistryValueDataKind.DWord, "01"), RegistryValueData.FromDWord(0)],
        }]));
        Assert.Throws<ArgumentException>(() => new RestorationCatalog([Target() with
        {
            AllowedDesiredValues = [RegistryValueData.FromDWord(2), RegistryValueData.FromDWord(3)],
        }]));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(" id")]
    public void Constructor_rejects_blank_or_padded_identifiers(string bad)
    {
        Assert.Throws<ArgumentException>(() => new RestorationCatalog([Target() with { SettingId = bad }]));
        Assert.Throws<ArgumentException>(() => new RestorationCatalog([Target() with { ModuleId = bad }]));
        Assert.Throws<ArgumentException>(() => new RestorationCatalog([Target() with { Provenance = bad }]));
    }

    // ---- baseline entry validation: accepted ----

    [Fact]
    public void Validate_accepts_an_exact_entry_and_resolves_the_user_hive()
    {
        var result = Catalog.Validate(Entry(), UserSid);

        Assert.True(result.IsAccepted);
        Assert.Equal(RestorationValidationOutcome.Accepted, result.Outcome);
        var candidate = result.Candidate!;
        Assert.Equal("app-suggestions", candidate.Target.SettingId);
        Assert.Equal(RegistryValueData.FromDWord(0), candidate.DesiredValue);
        Assert.Equal(UserSid, candidate.UserSid);
        Assert.Equal($@"HKU\{UserSid}\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", candidate.ResolvedKeyPath);
        Assert.Equal("SubscribedContent-338388Enabled", candidate.ValueName);
    }

    [Fact]
    public void Validate_accepts_both_listed_values_and_nothing_else()
    {
        Assert.True(Catalog.Validate(Entry(expected: "0"), UserSid).IsAccepted);
        Assert.True(Catalog.Validate(Entry(expected: "1"), UserSid).IsAccepted);

        var two = Catalog.Validate(Entry(expected: "2"), UserSid);
        Assert.Equal(RestorationValidationOutcome.ValueNotAllowed, two.Outcome);
        Assert.Null(two.Candidate);

        var negative = Catalog.Validate(Entry(expected: "-1"), UserSid);
        Assert.Equal(RestorationValidationOutcome.ValueNotAllowed, negative.Outcome);
    }

    [Fact]
    public void Validate_matches_location_case_insensitively_and_canonicalizes_the_value()
    {
        var result = Catalog.Validate(
            Entry(location: Cdm.ToUpperInvariant() + @"\subscribedcontent-338388enabled", expected: "01"), UserSid);

        Assert.True(result.IsAccepted);
        Assert.Equal(RegistryValueData.FromDWord(1), result.Candidate!.DesiredValue);
        Assert.Equal(Cdm, result.Candidate.Target.KeyPath);
    }

    [Fact]
    public void Validate_accepts_reversion_vector_only_enforcement()
    {
        var json = """{"reversionVectors":["Windows Update","Web Experience Pack deployment"]}""";
        var location = @"HKCU\Software\Microsoft\Windows\CurrentVersion\SearchSettings\IsDynamicSearchBoxEnabled";

        var result = Catalog.Validate(
            Entry(location: location, settingId: "dynamic-search-box", enforcementJson: json), UserSid);

        Assert.True(result.IsAccepted);
    }

    [Fact]
    public void Validate_accepts_enforcement_the_baseline_store_wrote_for_a_drift_fragile_toggle()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"tipc-restore-{Guid.NewGuid():N}");
        var path = Path.Combine(dir, DriftBaselineStore.FileName);
        try
        {
            new DriftBaselineStore(path, UserSid).RecordApplied([new ChangeDescriptor
            {
                ModuleId = Module,
                SettingId = "dynamic-search-box",
                DisplayName = "Suppress search highlights in the search box",
                SystemLocation = @"HKCU\Software\Microsoft\Windows\CurrentVersion\SearchSettings\IsDynamicSearchBoxEnabled",
                BeforeValue = "1",
                AfterValue = "0",
                BeforeDisplay = "Windows default",
                AfterDisplay = "Suppressed",
                ValueType = ChangeValueType.Registry_DWord,
                Category = ChangeCategory.Disable,
                Enforcement = new SettingEnforcement { ReversionVectors = ["Windows Update"] },
            }]);

            var document = DriftBaselineStore.Load(path)!;
            var entry = Assert.Single(document.Entries!);
            Assert.NotNull(entry.EnforcementJson);

            var result = Catalog.Validate(entry, document.UserSid);

            Assert.True(result.IsAccepted);
            Assert.Equal(UserSid, result.Candidate!.UserSid);
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }

    // ---- baseline entry validation: rejected ----

    [Theory]
    [InlineData("")]
    [InlineData("NoSeparator")]
    [InlineData(@"\Value")]
    [InlineData(Cdm + @"\")]
    [InlineData(Cdm + @"\SubscribedContent-338388Enabled ")]
    [InlineData(" " + Cdm + @"\SubscribedContent-338388Enabled")]
    public void Validate_rejects_malformed_locations(string location)
    {
        var result = Catalog.Validate(Entry(location: location), UserSid);

        Assert.Equal(RestorationValidationOutcome.MalformedLocation, result.Outcome);
        Assert.Null(result.Candidate);
    }

    [Theory]
    [InlineData(@"HKCU\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager\SubscribedContent-999999Enabled")]
    [InlineData(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\CloudContent\DisableWindowsConsumerFeatures")]
    [InlineData(@"HKCU\Software\Policies\Microsoft\Windows\Explorer\DisableSearchBoxSuggestions")]
    [InlineData(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced\ShowCopilotButton")]
    [InlineData(@"HKCU\Software\Microsoft\Siuf\Rules\NumberOfSIUFInPeriod")]
    [InlineData(@"HKCU\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager\Sub\SubscribedContent-338388Enabled")]
    public void Validate_rejects_unknown_or_custom_paths(string location)
    {
        var result = Catalog.Validate(Entry(location: location), UserSid);

        Assert.Equal(RestorationValidationOutcome.UnknownTarget, result.Outcome);
    }

    [Fact]
    public void Validate_rejects_identity_that_does_not_own_the_location()
    {
        Assert.Equal(
            RestorationValidationOutcome.IdentityMismatch,
            Catalog.Validate(Entry(settingId: "windows-tips"), UserSid).Outcome);
        Assert.Equal(
            RestorationValidationOutcome.IdentityMismatch,
            Catalog.Validate(Entry(settingId: "App-Suggestions"), UserSid).Outcome);
        Assert.Equal(
            RestorationValidationOutcome.IdentityMismatch,
            Catalog.Validate(Entry(moduleId: "Privacy & Telemetry"), UserSid).Outcome);
    }

    [Theory]
    [InlineData(ChangeValueType.Registry_String)]
    [InlineData(ChangeValueType.Registry_Binary)]
    [InlineData(ChangeValueType.Service_StartType)]
    public void Validate_rejects_value_type_mismatch(ChangeValueType valueType)
    {
        var result = Catalog.Validate(Entry(valueType: valueType), UserSid);

        Assert.Equal(RestorationValidationOutcome.ValueTypeMismatch, result.Outcome);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("__absent__")]
    [InlineData("0x0")]
    [InlineData("1 ")]
    [InlineData("zero")]
    [InlineData("4294967296")]
    public void Validate_rejects_malformed_or_absent_expected_values(string expected)
    {
        var result = Catalog.Validate(Entry(expected: expected), UserSid);

        Assert.Equal(RestorationValidationOutcome.MalformedValue, result.Outcome);
    }

    [Theory]
    [InlineData("""{"companionServices":["WSearch"]}""")]
    [InlineData("""{"companionTasks":["\\Microsoft\\Windows\\Task"]}""")]
    [InlineData("""{"gpCacheEntries":["Software\\Policies\\X"]}""")]
    [InlineData("""{"ownerModeRequired":true}""")]
    [InlineData("""{"aclElevation":true}""")]
    [InlineData("""{"restoresCompanions":true}""")]
    [InlineData("""{"skuRestriction":"Pro"}""")]
    [InlineData("""{"reversionVectors":["Windows Update"],"companionServices":["WSearch"]}""")]
    public void Validate_rejects_enforcement_it_cannot_honor(string enforcementJson)
    {
        var result = Catalog.Validate(Entry(enforcementJson: enforcementJson), UserSid);

        Assert.Equal(RestorationValidationOutcome.IncompatibleEnforcement, result.Outcome);
        Assert.Null(result.Candidate);
    }

    [Fact]
    public void Validate_rejects_a_sku_claim_even_when_the_catalog_target_is_non_policy()
    {
        var json = """{"skuRestriction":"Education"}""";

        var result = Catalog.Validate(Entry(enforcementJson: json), UserSid);

        Assert.Equal(RestorationValidationOutcome.IncompatibleEnforcement, result.Outcome);
        Assert.Contains("edition tier", result.Detail, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{")]
    [InlineData("[]")]
    public void Validate_rejects_enforcement_that_does_not_parse(string enforcementJson)
    {
        var result = Catalog.Validate(Entry(enforcementJson: enforcementJson), UserSid);

        Assert.Equal(RestorationValidationOutcome.MalformedEnforcement, result.Outcome);
    }

    [Fact]
    public void Validate_accepts_empty_enforcement_object_and_blank_json()
    {
        Assert.True(Catalog.Validate(Entry(enforcementJson: "{}"), UserSid).IsAccepted);
        Assert.True(Catalog.Validate(Entry(enforcementJson: "   "), UserSid).IsAccepted);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Validate_requires_a_user_sid_for_user_hive_targets(string? sid)
    {
        var result = Catalog.Validate(Entry(), sid);

        Assert.Equal(RestorationValidationOutcome.UserIdentityMissing, result.Outcome);
    }

    [Theory]
    // service, builtin, capability and other non-profile principals
    [InlineData("S-1-5-18")]
    [InlineData("S-1-5-19")]
    [InlineData("S-1-5-20")]
    [InlineData("S-1-5-32-544")]
    [InlineData("S-1-5-80-1-2-3-4-5")]
    [InlineData("S-1-5-90-0-1")]
    [InlineData("S-1-15-2-1-2-3-4-5-6-7")]
    [InlineData("S-1-1-0")]
    [InlineData("S-1-5")]
    // well-known group RIDs in the account shape
    [InlineData("S-1-5-21-1-2-3-498")]
    [InlineData("S-1-5-21-1-2-3-512")]
    [InlineData("S-1-5-21-1-2-3-513")]
    [InlineData("S-1-5-21-1-2-3-572")]
    // wrong revision, wrong first sub-authority, wrong counts
    [InlineData("S-2-5-21-1-2-3-1001")]
    [InlineData("S-1-5-22-1-2-3-1001")]
    [InlineData("S-1-12-2-1-2-3-4")]
    [InlineData("S-1-5-21-1-2-1001")]
    [InlineData("S-1-5-21-1-2-3-4-1001")]
    [InlineData("S-1-12-1-1-2-3")]
    [InlineData("S-1-12-1-1-2-3-4-5")]
    [InlineData("S-1-5-21-1-2-3-4-5-6-7-8-9-10-11-12-13-14-15")]
    // non-canonical or out-of-range numbers
    [InlineData("S-1-05-21-1-2-3-1001")]
    [InlineData("S-1-5-021-1-2-3-1001")]
    [InlineData("S-1-5-21-1-2-3-01001")]
    [InlineData("S-1-5-21-4294967296-2-3-1001")]
    [InlineData("S-1-5-21-1-2-3-4294967296")]
    [InlineData("S-1-12-1-1111111111-2222222222-3333333333-4444444444")]
    [InlineData("S-1-281474976710656-21-1-2-3-1001")]
    [InlineData("S-1-5-21-99999999999999999999-2-3-1001")]
    [InlineData("S-1-5-21-+1-2-3-1001")]
    [InlineData("S-1-5-21-1-2-3-１００１")]
    // malformed text
    [InlineData("S-1-5-21-abc")]
    [InlineData("S-1-5-21-1-2-3-1001-")]
    [InlineData("S-1-5-21-1-2-3-1001\\Software")]
    [InlineData("S-1-5-21-1-2-3--1001")]
    [InlineData("s-1-5-21-1-2-3-1001")]
    [InlineData("S-1-5-21-1-2-3-1001 ")]
    [InlineData(" S-1-5-21-1-2-3-1001")]
    [InlineData("Administrator")]
    public void Validate_rejects_sids_that_are_not_user_accounts(string sid)
    {
        var result = Catalog.Validate(Entry(), sid);

        Assert.Equal(RestorationValidationOutcome.MalformedUserSid, result.Outcome);
    }

    [Theory]
    [InlineData("S-1-5-21-1111111111-2222222222-3333333333-1001")]
    [InlineData("S-1-5-21-0-0-0-500")]
    [InlineData("S-1-5-21-4294967295-4294967295-4294967295-4294967295")]
    [InlineData("S-1-12-1-1111111111-2222222222-3333333333-4294967295")]
    [InlineData("S-1-12-1-4294967295-4294967295-4294967295-4294967295")]
    [InlineData("S-1-12-1-0-0-0-0")]
    public void Validate_keeps_the_profile_sid_on_every_candidate(string sid)
    {
        var result = Catalog.Validate(Entry(), sid);

        Assert.True(result.IsAccepted);
        Assert.Equal(sid, result.Candidate!.UserSid);
        Assert.StartsWith($@"HKU\{sid}\", result.Candidate.ResolvedKeyPath, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_checks_identity_before_it_checks_the_sid()
    {
        var result = Catalog.Validate(Entry(expected: "7"), "S-1-5-18");

        Assert.Equal(RestorationValidationOutcome.ValueNotAllowed, result.Outcome);
    }

    [Fact]
    public void Validate_rejects_null_entry()
    {
        Assert.Throws<ArgumentNullException>(() => Catalog.Validate(null!, UserSid));
    }

    [Fact]
    public void A_custom_catalog_does_not_change_the_default_one()
    {
        var custom = new RestorationCatalog([Target(settingId: "custom", keyPath: @"HKCU\Software\Custom", valueName: "Flag")]);

        Assert.NotNull(custom.FindByLocation(@"HKCU\Software\Custom", "Flag"));
        Assert.Null(RestorationCatalog.Default.FindByLocation(@"HKCU\Software\Custom", "Flag"));
        Assert.Equal(
            RestorationValidationOutcome.UnknownTarget,
            RestorationCatalog.Default.Validate(Entry(location: @"HKCU\Software\Custom\Flag", settingId: "custom"), UserSid).Outcome);
    }

    [Fact]
    public void Snapshot_matching_agrees_with_catalog_desired_values()
    {
        var candidate = Catalog.Validate(Entry(expected: "0"), UserSid).Candidate!;

        Assert.True(RegistryValueSnapshot.Present(RegistryValueData.FromDWord(0)).Matches(candidate.DesiredValue));
        Assert.False(RegistryValueSnapshot.Present(RegistryValueData.FromDWord(1)).Matches(candidate.DesiredValue));
        Assert.False(RegistryValueSnapshot.Present(RegistryValueData.FromString("0")).Matches(candidate.DesiredValue));
        Assert.False(RegistryValueSnapshot.Absent.Matches(candidate.DesiredValue));
    }
}
