using System.Reflection;
using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Drift;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;

namespace ThisIsMyPC.Core.Tests.Drift;

public sealed class RestorationBatchFactoryTests
{
    private const string UserSid = "S-1-5-21-1111111111-2222222222-3333333333-1001";
    private const string OtherSid = "S-1-5-21-1111111111-2222222222-3333333333-1002";
    private const string Cdm = @"HKCU\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager";
    private const string Location = Cdm + @"\SubscribedContent-338388Enabled";

    private static RestorationCandidate Candidate(string expected = "0", string sid = UserSid)
    {
        var validation = RestorationCatalog.Default.Validate(Entry(expected), sid);
        Assert.True(validation.IsAccepted, validation.Detail);
        return validation.Candidate!;
    }

    private static DriftBaselineEntry Entry(string expected = "0") => new()
    {
        ModuleId = "Windows Annoyances",
        SettingId = "app-suggestions",
        DisplayName = "Suppress Start menu app suggestions",
        SystemLocation = Location,
        ValueType = ChangeValueType.Registry_DWord,
        ExpectedValue = expected,
        UpdatedAtUtc = DateTimeOffset.UtcNow,
    };

    private static RegistryValueSnapshot Present(RegistryValueData value) => RegistryValueSnapshot.Present(value);
    private static RegistryValueSnapshot Drifted => Present(RegistryValueData.FromDWord(1));

    private static RestorationPreparation Ready() => RestorationBatchFactory.Prepare(Candidate(), Drifted);

    // ---- descriptor shape ----

    [Fact]
    public void Present_drifted_dword_becomes_a_descriptor_with_the_snapshot_as_before_state()
    {
        var prepared = RestorationBatchFactory.Prepare(Candidate(expected: "0"), Drifted);

        Assert.True(prepared.IsReady, prepared.Detail);
        var change = prepared.Change!;
        Assert.Equal("Windows Annoyances", change.ModuleId);
        Assert.Equal("app-suggestions", change.SettingId);
        Assert.Equal($@"HKU\{UserSid}\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager\SubscribedContent-338388Enabled", change.SystemLocation);
        Assert.Equal("1", change.BeforeValue);
        Assert.Equal("0", change.AfterValue);
        Assert.Equal(ChangeValueType.Registry_DWord, change.ValueType);
        Assert.Equal(ChangeCategory.Modify, change.Category);
        Assert.Equal(RestartRequirement.None, change.RestartRequirement);
        Assert.Null(change.Enforcement);
        Assert.StartsWith("Restore after drift:", change.DisplayName, StringComparison.Ordinal);
    }

    [Fact]
    public void Non_canonical_present_data_is_canonicalized_into_the_before_value()
    {
        var prepared = RestorationBatchFactory.Prepare(
            Candidate(), Present(new RegistryValueData(RegistryValueDataKind.DWord, "01")));

        Assert.True(prepared.IsReady, prepared.Detail);
        Assert.Equal("1", prepared.Change!.BeforeValue);
    }

    [Fact]
    public void Matching_value_is_a_no_op()
    {
        var prepared = RestorationBatchFactory.Prepare(Candidate(expected: "0"), Present(RegistryValueData.FromDWord(0)));

        Assert.Equal(RestorationPreparationOutcome.AlreadyMatches, prepared.Outcome);
        Assert.Null(prepared.Change);
        Assert.False(prepared.IsReady);
    }

    [Fact]
    public void Absent_value_is_refused_not_fabricated()
    {
        var prepared = RestorationBatchFactory.Prepare(Candidate(), RegistryValueSnapshot.Absent);

        Assert.Equal(RestorationPreparationOutcome.ObservedAbsent, prepared.Outcome);
        Assert.Null(prepared.Change);
    }

    [Theory]
    [InlineData(RegistryValueDataKind.String, "0")]
    [InlineData(RegistryValueDataKind.String, "")]
    [InlineData(RegistryValueDataKind.QWord, "0")]
    [InlineData(RegistryValueDataKind.DWord, "zero")]
    [InlineData(RegistryValueDataKind.DWord, "0x1")]
    public void Wrong_kind_or_unparseable_data_is_refused(RegistryValueDataKind kind, string data)
    {
        var prepared = RestorationBatchFactory.Prepare(Candidate(), Present(new RegistryValueData(kind, data)));

        Assert.Equal(RestorationPreparationOutcome.ObservedKindMismatch, prepared.Outcome);
        Assert.Null(prepared.Change);
    }

    // ---- forged candidates: every field is rechecked against the shipped catalog ----

    [Fact]
    public void Altered_resolved_path_is_refused()
    {
        var forged = Candidate() with { ResolvedKeyPath = @"HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate" };

        var prepared = RestorationBatchFactory.Prepare(forged, Drifted);

        Assert.Equal(RestorationPreparationOutcome.UntrustedCandidate, prepared.Outcome);
        Assert.Null(prepared.Change);
    }

    [Fact]
    public void Resolved_path_for_a_different_sid_than_the_candidate_names_is_refused()
    {
        var real = Candidate();
        var forged = real with { UserSid = OtherSid };

        var prepared = RestorationBatchFactory.Prepare(forged, Drifted);

        Assert.Equal(RestorationPreparationOutcome.UntrustedCandidate, prepared.Outcome);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("S-1-5-18")]
    [InlineData("S-1-5-21-1111111111-2222222222-3333333333-512")]
    public void Non_account_sid_is_refused_even_with_a_matching_path(string? sid)
    {
        var real = Candidate();
        var forged = real with
        {
            UserSid = sid,
            ResolvedKeyPath = $@"HKU\{sid}\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager",
        };

        var prepared = RestorationBatchFactory.Prepare(forged, Drifted);

        Assert.Equal(RestorationPreparationOutcome.UntrustedCandidate, prepared.Outcome);
    }

    [Fact]
    public void Target_rebuilt_with_a_different_key_path_is_refused()
    {
        var real = Candidate();
        var forged = real with
        {
            Target = real.Target with { KeyPath = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Run" },
        };

        var prepared = RestorationBatchFactory.Prepare(forged, Drifted);

        Assert.Equal(RestorationPreparationOutcome.UntrustedCandidate, prepared.Outcome);
    }

    [Fact]
    public void Target_rebuilt_with_a_different_value_name_is_refused()
    {
        var real = Candidate();
        var forged = real with { Target = real.Target with { ValueName = "Other" } };

        Assert.Equal(RestorationPreparationOutcome.UntrustedCandidate, RestorationBatchFactory.Prepare(forged, Drifted).Outcome);
    }

    [Fact]
    public void Target_structural_copy_at_the_same_location_with_wider_allowed_values_is_refused()
    {
        var real = Candidate();
        var widened = real.Target with
        {
            AllowedDesiredValues = [RegistryValueData.FromDWord(0), RegistryValueData.FromDWord(1), RegistryValueData.FromDWord(7)],
        };
        var forged = real with { Target = widened, DesiredValue = RegistryValueData.FromDWord(7) };

        Assert.Equal(RestorationPreparationOutcome.UntrustedCandidate, RestorationBatchFactory.Prepare(forged, Drifted).Outcome);
    }

    [Fact]
    public void Target_copy_with_swapped_identity_is_refused()
    {
        var real = Candidate();
        var forged = real with { Target = real.Target with { ModuleId = "Shell", SettingId = "elsewhere" } };

        Assert.Equal(RestorationPreparationOutcome.UntrustedCandidate, RestorationBatchFactory.Prepare(forged, Drifted).Outcome);
    }

    [Theory]
    [InlineData(RegistryValueDataKind.DWord, "7")]
    [InlineData(RegistryValueDataKind.DWord, "01")]
    [InlineData(RegistryValueDataKind.DWord, "-1")]
    [InlineData(RegistryValueDataKind.String, "0")]
    [InlineData(RegistryValueDataKind.QWord, "0")]
    [InlineData(RegistryValueDataKind.Binary, "AA==")]
    public void Desired_value_outside_the_targets_canonical_allowed_list_is_refused(RegistryValueDataKind kind, string data)
    {
        var forged = Candidate() with { DesiredValue = new RegistryValueData(kind, data) };

        var prepared = RestorationBatchFactory.Prepare(forged, Drifted);

        Assert.Equal(RestorationPreparationOutcome.UntrustedCandidate, prepared.Outcome);
        Assert.Null(prepared.Change);
    }

    [Fact]
    public void Candidate_from_a_custom_catalog_is_refused()
    {
        var custom = new RestorationCatalog(
        [
            RestorationTarget.DWordToggle(
                "Windows Annoyances", "custom", "Custom",
                @"HKCU\Software\Microsoft\Windows\CurrentVersion\Run", "Something", 0, 1, "test"),
        ]);
        var entry = Entry() with
        {
            SettingId = "custom",
            SystemLocation = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Run\Something",
        };
        var validation = custom.Validate(entry, UserSid);
        Assert.True(validation.IsAccepted, validation.Detail);

        var prepared = RestorationBatchFactory.Prepare(validation.Candidate!, Drifted);

        Assert.Equal(RestorationPreparationOutcome.UntrustedCandidate, prepared.Outcome);
        Assert.Null(prepared.Change);
    }

    [Fact]
    public void Custom_catalog_copy_of_a_shipped_target_is_still_refused()
    {
        // Same location, same values, different catalog instance: only the shipped
        // catalog's own target object is trusted.
        var shipped = RestorationCatalog.Default.FindByLocation(Cdm, "SubscribedContent-338388Enabled")!;
        var copy = new RestorationCatalog([shipped with { }]);
        var validation = copy.Validate(Entry(), UserSid);
        Assert.True(validation.IsAccepted, validation.Detail);

        Assert.Equal(
            RestorationPreparationOutcome.UntrustedCandidate,
            RestorationBatchFactory.Prepare(validation.Candidate!, Drifted).Outcome);
    }

    [Fact]
    public void Untrusted_candidate_is_refused_before_the_snapshot_is_considered()
    {
        var forged = Candidate() with { ResolvedKeyPath = @"HKLM\Forged" };

        Assert.Equal(RestorationPreparationOutcome.UntrustedCandidate, RestorationBatchFactory.Prepare(forged, RegistryValueSnapshot.Absent).Outcome);
        Assert.Equal(RestorationPreparationOutcome.UntrustedCandidate, RestorationBatchFactory.Prepare(forged, Present(RegistryValueData.FromDWord(0))).Outcome);
    }

    // ---- groups ----

    [Fact]
    public void CreateGroup_takes_only_ready_preparations_and_copies_them()
    {
        var preparations = new List<RestorationPreparation> { Ready() };

        var group = RestorationBatchFactory.CreateGroup(preparations);
        preparations.Clear();

        Assert.Equal(RestorationBatchFactory.GroupDisplayName, group.DisplayName);
        Assert.Single(group.Changes);
        Assert.IsNotType<List<ChangeDescriptor>>(group.Changes);
    }

    [Fact]
    public void CreateGroup_refuses_an_empty_batch()
    {
        Assert.Throws<ArgumentException>(() => RestorationBatchFactory.CreateGroup([]));
    }

    [Fact]
    public void CreateGroup_refuses_any_preparation_that_is_not_ready()
    {
        var rejected = new[]
        {
            RestorationBatchFactory.Prepare(Candidate(), Present(RegistryValueData.FromDWord(0))),
            RestorationBatchFactory.Prepare(Candidate(), RegistryValueSnapshot.Absent),
            RestorationBatchFactory.Prepare(Candidate(), Present(RegistryValueData.FromString("0"))),
            RestorationBatchFactory.Prepare(Candidate() with { ResolvedKeyPath = @"HKLM\Forged" }, Drifted),
        };
        Assert.Equal(
            [
                RestorationPreparationOutcome.AlreadyMatches,
                RestorationPreparationOutcome.ObservedAbsent,
                RestorationPreparationOutcome.ObservedKindMismatch,
                RestorationPreparationOutcome.UntrustedCandidate,
            ],
            rejected.Select(p => p.Outcome));

        foreach (var preparation in rejected)
        {
            Assert.False(preparation.IsReady);
            Assert.Null(preparation.Change);
            Assert.Throws<ArgumentException>(() => RestorationBatchFactory.CreateGroup([Ready(), preparation]));
        }
    }

    [Fact]
    public void CreateGroup_refuses_a_null_preparation()
    {
        Assert.Throws<ArgumentNullException>(() => RestorationBatchFactory.CreateGroup([Ready(), null!]));
    }

    [Fact]
    public void A_ready_preparation_cannot_be_built_or_altered_through_the_public_api()
    {
        // The group boundary trusts IsReady, so the type must be factory-issued only:
        // no public constructor, no record clone or init setters, no setters at all.
        var type = typeof(RestorationPreparation);

        Assert.True(type.IsSealed);
        Assert.False(type.IsValueType);
        Assert.Empty(type.GetConstructors(BindingFlags.Public | BindingFlags.Instance));
        Assert.Null(type.GetMethod("<Clone>$", BindingFlags.Public | BindingFlags.Instance));
        Assert.All(
            type.GetProperties(BindingFlags.Public | BindingFlags.Instance),
            p => Assert.Null(p.GetSetMethod(nonPublic: false)));
        Assert.Empty(type.GetMethods(BindingFlags.Public | BindingFlags.Static));
        Assert.Throws<MissingMethodException>(() => Activator.CreateInstance(type));
    }

    [Fact]
    public void Target_with_a_null_key_path_or_value_name_is_refused_by_name_not_by_exception()
    {
        var real = Candidate();
        var noKey = real with { Target = real.Target with { KeyPath = null! } };
        var noValue = real with { Target = real.Target with { ValueName = null! } };
        var blank = real with { Target = real.Target with { KeyPath = " ", ValueName = "" } };

        Assert.Equal(RestorationPreparationOutcome.UntrustedCandidate, RestorationBatchFactory.Prepare(noKey, Drifted).Outcome);
        Assert.Equal(RestorationPreparationOutcome.UntrustedCandidate, RestorationBatchFactory.Prepare(noValue, Drifted).Outcome);
        Assert.Equal(RestorationPreparationOutcome.UntrustedCandidate, RestorationBatchFactory.Prepare(blank, Drifted).Outcome);
    }

    [Fact]
    public void Candidate_with_a_null_target_is_refused_by_name()
    {
        var forged = Candidate() with { Target = null! };

        Assert.Equal(RestorationPreparationOutcome.UntrustedCandidate, RestorationBatchFactory.Prepare(forged, Drifted).Outcome);
    }

    // ---- through the isolated queue ----

    [Fact]
    public async Task Prepared_descriptor_stages_and_applies_through_an_isolated_queue()
    {
        var group = RestorationBatchFactory.CreateGroup([Ready()]);
        var queue = PendingChangesService.Create(new ReversibleChangeExecutor());
        var written = new List<ChangeDescriptor>();

        queue.Stage(group);
        var result = await queue.ApplyAllAsync(
            c =>
            {
                written.Add(c);
                return Task.FromResult(OperationResult<bool>.Success(true));
            },
            _ => Task.FromResult(OperationResult<bool>.Success(true)));

        Assert.True(result.IsSuccess);
        Assert.Single(written);
        Assert.Equal("0", written[0].AfterValue);
        Assert.Empty(result.RequiredRestarts);
        Assert.Equal(0, queue.PendingCount);
    }

    [Fact]
    public async Task Group_rollback_writes_the_observed_value_back()
    {
        var first = Ready();
        var secondEntry = Entry() with
        {
            SettingId = "windows-tips",
            SystemLocation = Cdm + @"\SubscribedContent-338389Enabled",
        };
        var second = RestorationBatchFactory.Prepare(
            RestorationCatalog.Default.Validate(secondEntry, UserSid).Candidate!, Drifted);
        var queue = PendingChangesService.Create(new ReversibleChangeExecutor());
        var reverted = new List<ChangeDescriptor>();

        queue.Stage(RestorationBatchFactory.CreateGroup([first, second]));
        var result = await queue.ApplyAllAsync(
            c => Task.FromResult(c.SettingId == "windows-tips"
                ? OperationResult<bool>.Failure("refused", ErrorCategory.AccessDenied)
                : OperationResult<bool>.Success(true)),
            c =>
            {
                reverted.Add(c);
                return Task.FromResult(OperationResult<bool>.Success(true));
            });

        Assert.False(result.IsSuccess);
        var rollback = Assert.Single(reverted);
        Assert.Equal("app-suggestions", rollback.SettingId);
        Assert.Equal("1", rollback.AfterValue);
        Assert.Equal("0", rollback.BeforeValue);
    }

    [Fact]
    public void Prepare_rejects_nulls()
    {
        Assert.Throws<ArgumentNullException>(() => RestorationBatchFactory.Prepare(null!, RegistryValueSnapshot.Absent));
        Assert.Throws<ArgumentNullException>(() => RestorationBatchFactory.Prepare(Candidate(), null!));
    }
}
