using ThisIsMyPC.App.Services;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;

namespace ThisIsMyPC.Integration.Tests.Services;

public sealed class OwnerModeServiceTests : IDisposable
{
    private readonly string _binaryPath = Path.Combine(
        Path.GetTempPath(), $"tipc-ownermode-{Guid.NewGuid():N}", "ThisIsMyPC.Service.exe");
    private readonly FakeInstaller _installer = new();
    private readonly FakeServiceControl _serviceControl = new();

    public void Dispose()
    {
        var dir = Path.GetDirectoryName(_binaryPath)!;
        try
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
        catch (IOException) { }
    }

    private OwnerModeService Create() => new(_installer, _serviceControl, _binaryPath);

    private void WriteBinary()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_binaryPath)!);
        File.WriteAllText(_binaryPath, "stub");
    }

    [Fact]
    public void State_is_not_installed_when_query_finds_nothing()
    {
        Assert.Equal(OwnerModeState.NotInstalled, Create().GetState());
        Assert.False(Create().IsRunning);
    }

    [Fact]
    public async Task Enable_without_binary_fails_with_not_found_and_installs_nothing()
    {
        var result = await Create().EnableAsync();

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCategory.NotFound, result.ErrorCategory);
        Assert.Contains("Reinstall", result.ErrorMessage);
        Assert.False(_installer.Installed);
    }

    [Fact]
    public async Task Enable_installs_sets_auto_start_and_starts()
    {
        WriteBinary();
        var service = Create();
        var raised = 0;
        service.StateChanged += (_, _) => raised++;

        var result = await service.EnableAsync();

        Assert.True(result.IsSuccess);
        Assert.True(_installer.Installed);
        Assert.Equal(_binaryPath, _installer.LastBinaryPath);
        Assert.Equal(ServiceStartType.Automatic, _serviceControl.StartType);
        Assert.Equal(ServiceState.Running, _serviceControl.State);
        Assert.Equal(1, raised);
        Assert.Equal(OwnerModeState.Running, service.GetState());
        Assert.True(service.IsRunning);
    }

    [Fact]
    public async Task Disable_stops_and_sets_start_type_disabled()
    {
        WriteBinary();
        var service = Create();
        await service.EnableAsync();

        var result = await service.DisableAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(ServiceState.Stopped, _serviceControl.State);
        Assert.Equal(ServiceStartType.Disabled, _serviceControl.StartType);
        Assert.Equal(OwnerModeState.Disabled, service.GetState());
        Assert.False(service.IsRunning);
    }

    [Fact]
    public async Task Failed_install_reports_the_error_and_does_not_start()
    {
        WriteBinary();
        _installer.FailWith = "SCM said no";

        var result = await Create().EnableAsync();

        Assert.False(result.IsSuccess);
        Assert.Contains("SCM said no", result.ErrorMessage);
        Assert.Null(_serviceControl.State);
    }

    [Fact]
    public void Capability_probe_reflects_live_state()
    {
        var service = Create();
        var detector = new CapabilityDetector(
            new Fakes.FakeRegistryService(), ownerModeProbe: () => service.IsRunning);

        Assert.False(detector.IsOwnerModeAvailable);

        _installer.Installed = true;
        _serviceControl.State = ServiceState.Running;
        _serviceControl.StartType = ServiceStartType.Automatic;

        Assert.True(detector.IsOwnerModeAvailable);
    }

    private sealed class FakeInstaller : IServiceInstaller
    {
        public bool Installed { get; set; }
        public string? LastBinaryPath { get; private set; }
        public string? FailWith { get; set; }

        public OperationResult<bool> Install(string serviceName, string displayName, string description, string binaryPath)
        {
            if (FailWith is { } error)
                return OperationResult<bool>.Failure(error, ErrorCategory.ServiceUnavailable);
            Installed = true;
            LastBinaryPath = binaryPath;
            return OperationResult<bool>.Success(true);
        }

        public OperationResult<bool> Uninstall(string serviceName)
        {
            Installed = false;
            return OperationResult<bool>.Success(true);
        }

        public OperationResult<bool> IsInstalled(string serviceName) =>
            OperationResult<bool>.Success(Installed);
    }

    /// <summary>SCM fake: Query fails NotFound until State is set (mirrors an uninstalled service).</summary>
    private sealed class FakeServiceControl : IServiceControlService
    {
        public ServiceState? State { get; set; }
        public ServiceStartType StartType { get; set; } = ServiceStartType.Manual;

        public OperationResult<ServiceStatusInfo> Query(string serviceName) =>
            State is { } state
                ? OperationResult<ServiceStatusInfo>.Success(
                    new ServiceStatusInfo(serviceName, serviceName, state, StartType))
                : OperationResult<ServiceStatusInfo>.Failure("No such service", ErrorCategory.NotFound);

        public OperationResult<IReadOnlyList<ServiceEntryInfo>> EnumerateAll() =>
            OperationResult<IReadOnlyList<ServiceEntryInfo>>.Success([]);

        public OperationResult<bool> SetStartType(string serviceName, ServiceStartType startType)
        {
            StartType = startType;
            if (State is null)
                State = ServiceState.Stopped;
            return OperationResult<bool>.Success(true);
        }

        public Task<OperationResult<bool>> StopAsync(string serviceName, TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            State = ServiceState.Stopped;
            return Task.FromResult(OperationResult<bool>.Success(true));
        }

        public Task<OperationResult<bool>> StartAsync(string serviceName, TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            State = ServiceState.Running;
            return Task.FromResult(OperationResult<bool>.Success(true));
        }
    }
}
