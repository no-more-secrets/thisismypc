using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;

namespace ThisIsMyPC.Integration.Tests.Fakes;

/// <summary>
/// Scriptable in-memory IServiceControlService. Seed services with <see cref="AddService"/>;
/// operations mutate the in-memory state and are recorded in <see cref="Calls"/>.
/// Failures are injected per service+operation via <see cref="InjectFailure"/>.
/// </summary>
public sealed class FakeServiceControlService : IServiceControlService
{
    private readonly Dictionary<string, ServiceStatusInfo> _services = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ErrorCategory> _failures = new(StringComparer.OrdinalIgnoreCase);

    public List<string> Calls { get; } = [];

    public void AddService(string name, ServiceState state, ServiceStartType startType)
        => _services[name] = new ServiceStatusInfo(name, name, state, startType);

    public ServiceStatusInfo? GetService(string name)
        => _services.TryGetValue(name, out var info) ? info : null;

    /// <summary>Makes every call of the given operation fail for the service.</summary>
    public void InjectFailure(string operation, string name, ErrorCategory category = ErrorCategory.AccessDenied)
        => _failures[$"{operation}:{name}"] = category;

    public OperationResult<ServiceStatusInfo> Query(string serviceName)
    {
        Calls.Add($"Query:{serviceName}");
        if (_failures.TryGetValue($"Query:{serviceName}", out var fail))
            return OperationResult<ServiceStatusInfo>.Failure($"Injected Query failure for '{serviceName}'.", fail);
        if (!_services.TryGetValue(serviceName, out var info))
            return OperationResult<ServiceStatusInfo>.Failure($"No service '{serviceName}'.", ErrorCategory.NotFound);
        return OperationResult<ServiceStatusInfo>.Success(info);
    }

    public OperationResult<bool> SetStartType(string serviceName, ServiceStartType startType)
    {
        Calls.Add($"SetStartType:{serviceName}:{startType}");
        if (_failures.TryGetValue($"SetStartType:{serviceName}", out var fail))
            return OperationResult<bool>.Failure($"Injected SetStartType failure for '{serviceName}'.", fail);
        if (!_services.TryGetValue(serviceName, out var info))
            return OperationResult<bool>.Failure($"No service '{serviceName}'.", ErrorCategory.NotFound);
        _services[serviceName] = info with { StartType = startType };
        return OperationResult<bool>.Success(true);
    }

    public Task<OperationResult<bool>> StopAsync(
        string serviceName, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        Calls.Add($"Stop:{serviceName}");
        if (_failures.TryGetValue($"Stop:{serviceName}", out var fail))
            return Task.FromResult(OperationResult<bool>.Failure($"Injected Stop failure for '{serviceName}'.", fail));
        if (!_services.TryGetValue(serviceName, out var info))
            return Task.FromResult(OperationResult<bool>.Failure($"No service '{serviceName}'.", ErrorCategory.NotFound));
        _services[serviceName] = info with { State = ServiceState.Stopped };
        return Task.FromResult(OperationResult<bool>.Success(true));
    }

    public Task<OperationResult<bool>> StartAsync(
        string serviceName, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        Calls.Add($"Start:{serviceName}");
        if (_failures.TryGetValue($"Start:{serviceName}", out var fail))
            return Task.FromResult(OperationResult<bool>.Failure($"Injected Start failure for '{serviceName}'.", fail));
        if (!_services.TryGetValue(serviceName, out var info))
            return Task.FromResult(OperationResult<bool>.Failure($"No service '{serviceName}'.", ErrorCategory.NotFound));
        _services[serviceName] = info with { State = ServiceState.Running };
        return Task.FromResult(OperationResult<bool>.Success(true));
    }
}
