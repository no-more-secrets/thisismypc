using ThisIsMyPC.Broker;
using ThisIsMyPC.Core.Coordination;
using ThisIsMyPC.Core.Drift.Consent;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;

namespace ThisIsMyPC.Ipc.Tests;

public sealed class OwnerModeBrokerPauseTests
{
    [Fact]
    public async Task PausePersistsOffWithoutRecoveryAndReleasesBeforeStop()
    {
        var fixture = new Fixture();
        Assert.True((await fixture.Service.Disable(CancellationToken.None)).IsSuccess);
        Assert.Equal(new[] { "consent-off", "release", "stop", "disable" }, fixture.Events);
        Assert.False(fixture.Consent.Enabled);
    }

    [Fact]
    public async Task FailedConsentPersistenceDoesNotStopService()
    {
        var fixture = new Fixture();
        fixture.Consent.Fail = true;
        Assert.False((await fixture.Service.Disable(CancellationToken.None)).IsSuccess);
        Assert.DoesNotContain("stop", fixture.Events);
        Assert.Contains("release", fixture.Events);
    }

    [Fact]
    public async Task FailedServiceStopLeavesDurableConsentOff()
    {
        var fixture = new Fixture();
        fixture.Control.FailStop = true;
        Assert.False((await fixture.Service.Disable(CancellationToken.None)).IsSuccess);
        Assert.False(fixture.Consent.Enabled);
        Assert.DoesNotContain("disable", fixture.Events);
    }

    [Fact]
    public async Task WaitingForActiveAttemptDoesNotStopServiceEarly()
    {
        var fixture = new Fixture();
        var gate = new TaskCompletionSource<MutationLeaseResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Provider.Pending = gate.Task;
        var pause = fixture.Service.Disable(CancellationToken.None);
        Assert.False(pause.IsCompleted);
        Assert.Empty(fixture.Events);
        gate.SetResult(MutationLeaseResult.Held(fixture.Provider.Lease));
        Assert.True((await pause).IsSuccess);
        Assert.Equal(new[] { "consent-off", "release", "stop", "disable" }, fixture.Events);
    }

    [Fact]
    public async Task UntrustedSuccessStateDoesNotConfirmPause()
    {
        var fixture = new Fixture();
        fixture.Consent.InvalidSuccess = true;
        Assert.False((await fixture.Service.Disable(CancellationToken.None)).IsSuccess);
        Assert.DoesNotContain("stop", fixture.Events);
    }

    [Fact]
    public async Task MissingServiceStillClearsDurableConsent()
    {
        var fixture = new Fixture();
        fixture.Control.Missing = true;
        Assert.True((await fixture.Service.Disable(CancellationToken.None)).IsSuccess);
        Assert.False(fixture.Consent.Enabled);
        Assert.Equal(new[] { "consent-off", "release" }, fixture.Events);
    }

    [Fact]
    public async Task StoppedServiceStillClearsDurableConsent()
    {
        var fixture = new Fixture();
        fixture.Control.Stopped = true;
        Assert.True((await fixture.Service.Disable(CancellationToken.None)).IsSuccess);
        Assert.False(fixture.Consent.Enabled);
        Assert.Equal(new[] { "consent-off", "release", "disable" }, fixture.Events);
    }

    private sealed class Fixture
    {
        public List<string> Events { get; } = [];
        public Provider Provider { get; }
        public Consent Consent { get; }
        public Control Control { get; }
        public OwnerModeBrokerController Service { get; }
        public Fixture()
        {
            Provider = new(Events);
            Consent = new(Events);
            Control = new(Events, Provider);
            Service = new(new Installer(), Control, leases: Provider, consent: Consent);
        }
    }

    private sealed class Lease(List<string> events, string name) : MutationLeaseBase(name, "pause-test", true)
    {
        protected override void ReleaseCore() => events.Add("release");
    }

    private sealed class Provider : IMutationLeaseProvider
    {
        public string Name { get; } = MutationLeaseNames.ForTest();
        public Lease Lease { get; }
        public Task<MutationLeaseResult>? Pending { get; set; }
        public Provider(List<string> events) => Lease = new(events, Name);
        public Task<MutationLeaseResult> AcquireAsync(TimeSpan maxWait, CancellationToken cancellationToken = default)
            => Pending ?? Task.FromResult(MutationLeaseResult.Held(Lease));
    }

    private sealed class Consent(List<string> events) : IMachineConsentStore
    {
        public bool Enabled { get; private set; } = true;
        public bool Fail { get; set; }
        public bool InvalidSuccess { get; set; }
        public MachineConsentState Read() => throw new InvalidOperationException("Pause must not require readable prior state.");
        public MachineConsentWriteResult SetEnabled(bool enabled, IMutationLease lease)
        {
            Assert.False(enabled);
            Assert.True(lease.IsHeld);
            Assert.True(lease.RequiresRecovery);
            Assert.False(lease.CanWrite);
            if (Fail) return new(false, MachineConsentState.Off(MachineConsentStatus.Unavailable, "disk failure"));
            if (InvalidSuccess) return new(true, new(MachineConsentStatus.Untrusted, true, "untrusted result"));
            Enabled = false;
            events.Add("consent-off");
            return new(true, new(MachineConsentStatus.Loaded, false, "saved"));
        }
    }

    private sealed class Control(List<string> events, Provider provider) : IServiceControlService
    {
        public bool FailStop { get; set; }
        public bool Missing { get; set; }
        public bool Stopped { get; set; }
        public OperationResult<ServiceStatusInfo> Query(string name) => Missing
            ? OperationResult<ServiceStatusInfo>.Failure("missing", ErrorCategory.NotFound)
            : OperationResult<ServiceStatusInfo>.Success(
                new(name, name, Stopped ? ServiceState.Stopped : ServiceState.Running, ServiceStartType.Automatic));
        public OperationResult<IReadOnlyList<ServiceEntryInfo>> EnumerateAll() => throw new NotSupportedException();
        public OperationResult<bool> SetStartType(string name, ServiceStartType type)
        {
            Assert.False(provider.Lease.IsHeld);
            Assert.Equal(ServiceStartType.Disabled, type);
            events.Add("disable");
            return OperationResult<bool>.Success(true);
        }
        public Task<OperationResult<bool>> StopAsync(string name, TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            Assert.False(provider.Lease.IsHeld);
            events.Add("stop");
            return Task.FromResult(FailStop ? OperationResult<bool>.Failure("SCM failure", ErrorCategory.ServiceUnavailable)
                : OperationResult<bool>.Success(true));
        }
        public Task<OperationResult<bool>> StartAsync(string name, TimeSpan timeout, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class Installer : IServiceInstaller
    {
        public OperationResult<bool> Install(string name, string displayName, string description, string path) => throw new NotSupportedException();
        public OperationResult<bool> Uninstall(string name) => throw new NotSupportedException();
        public OperationResult<bool> IsInstalled(string name) => throw new NotSupportedException();
    }
}
