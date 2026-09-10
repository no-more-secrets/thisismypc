using ThisIsMyPC.Core.Coordination;
using ThisIsMyPC.Core.Drift;
using ThisIsMyPC.Core.Drift.Consent;
using ThisIsMyPC.Core.Drift.Journal;
using ThisIsMyPC.Core.Results;

namespace ThisIsMyPC.Core.Tests.Drift;

public sealed class RestorationRecoveryTests
{
    [Fact]
    public async Task HealthyRecoveryPreservesConsentAndAllowsDeliberateOperation()
    {
        using var f = new RestorationLoopFixture();
        await f.InitializeAsync();
        var result = await Coordinator(f).RunAsync(TimeSpan.Zero, (lease, _) => Task.FromResult(lease.CanWrite));
        Assert.True(result.OperationRan);
        Assert.True(result.Value);
        Assert.True(f.Consent.Enabled);
        Assert.Equal(0, f.Registry.Writes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InterruptedIntentBecomesDiagnosticEvenWhenValueMatches(bool matches)
    {
        using var f = new RestorationLoopFixture();
        await f.InitializeAsync();
        await Begin(f);
        if (matches) f.Registry.Value = f.Target.SuppressedValue;
        var coordinator = Coordinator(f);
        var result = await coordinator.RunAsync(TimeSpan.Zero, async (lease, _) =>
        {
            Assert.False(f.Consent.Enabled);
            Assert.Equal(JournalOutcomeKind.RecoveryObservation, Assert.Single(f.Journal.Read(lease).Attempts).Outcome!.Kind);
            await new RestorationJournalImporter(f.Repository).ImportAsync(f.Journal, lease);
            return true;
        });
        Assert.True(result.OperationRan);
        Assert.Equal(0, f.Registry.Writes);
        Assert.Equal("RecoveryObservation", Assert.Single(await f.Repository.GetAllAsync()).JournalOutcome);
        var again = await coordinator.RunAsync(TimeSpan.Zero, (lease, _) => Task.FromResult(f.Journal.Read(lease).Attempts.Count));
        Assert.Equal(1, again.Value);
    }

    [Fact]
    public async Task FailedObservationDisablesConsentButRefusesRecovery()
    {
        using var f = new RestorationLoopFixture();
        await f.InitializeAsync();
        await Begin(f);
        f.Registry.ReadError = ErrorCategory.AccessDenied;
        var result = await Coordinator(f).RunAsync(TimeSpan.Zero, (_, _) => Task.FromResult(true));
        Assert.False(result.OperationRan);
        Assert.False(f.Consent.Enabled);
        Assert.Equal(0, f.Registry.Writes);
    }

    [Fact]
    public async Task CorruptJournalDisablesConsentWithoutRepair()
    {
        using var f = new RestorationLoopFixture();
        await f.InitializeAsync();
        var path = Path.Combine(f.DirectoryPath, "journal", Guid.NewGuid().ToString("N") + ".tipj");
        await File.WriteAllTextAsync(path, "broken");
        var result = await Coordinator(f).RunAsync(TimeSpan.Zero, (_, _) => Task.FromResult(true));
        Assert.False(result.OperationRan);
        Assert.False(f.Consent.Enabled);
        Assert.Equal("broken", await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task FailedConsentOffCannotAuthorizeOperationOrAppendObservation()
    {
        using var f = new RestorationLoopFixture();
        await f.InitializeAsync();
        await Begin(f);
        var result = await Coordinator(f, new RefusingConsent()).RunAsync(TimeSpan.Zero, (_, _) => Task.FromResult(true));
        Assert.False(result.OperationRan);
        await using var lease = (await f.Provider.AcquireAsync(TimeSpan.Zero)).Lease!;
        Assert.Null(Assert.Single(f.Journal.Read(lease).Attempts).Outcome);
    }

    private static MutationCoordinator Coordinator(RestorationLoopFixture f, IMachineConsentStore? consent = null)
    {
        var recovery = new RestorationRecovery(f.Baseline, f.Journal, consent ?? f.Consent, f.Registry, f.Provider.Name);
        return new(f.Provider, recovery.RecoverAsync);
    }

    private static async Task Begin(RestorationLoopFixture f)
    {
        await using var lease = (await f.Provider.AcquireAsync(TimeSpan.Zero)).Lease!;
        lease.MarkRecovered();
        var candidate = RestorationCatalog.Default.Validate(new DriftBaselineEntry
        {
            ModuleId = f.Target.ModuleId, SettingId = f.Target.SettingId, DisplayName = f.Target.DisplayName,
            SystemLocation = f.Target.KeyPath + "\\" + f.Target.ValueName, ExpectedValue = f.Target.SuppressedValue.Data,
            ValueType = f.Target.ValueType, UpdatedAtUtc = DateTimeOffset.UtcNow,
        }, RestorationLoopFixture.Sid).Candidate!;
        Assert.NotNull(f.Journal.Begin(Guid.NewGuid(), candidate, RegistryValueSnapshot.Present(f.Target.WindowsDefaultValue), lease).Permit);
    }

    private sealed class RefusingConsent : IMachineConsentStore
    {
        public MachineConsentState Read() => new(MachineConsentStatus.Loaded, true, "test");
        public MachineConsentWriteResult SetEnabled(bool enabled, IMutationLease lease) => new(false, Read());
    }
}
