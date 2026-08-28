using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;

namespace ThisIsMyPC.Modules.Startup.Tests.Fakes;

/// <summary>
/// Scriptable in-memory IServiceControlService (per-project fake convention).
/// Seed services with <see cref="AddService"/>; operations mutate in-memory
/// state and are recorded in <see cref="Calls"/>.
/// </summary>
public sealed class FakeServiceControlService : IServiceControlService
{
    private readonly List<ServiceEntryInfo> _services = [];
    private readonly Dictionary<string, ErrorCategory> _failures = new(StringComparer.OrdinalIgnoreCase);

    public List<string> Calls { get; } = [];

    public void AddService(
        string name, ServiceState state = ServiceState.Running,
        ServiceStartType startType = ServiceStartType.Manual,
        string? displayName = null, string? description = null)
        => _services.Add(new ServiceEntryInfo(name, displayName ?? name, description, state, startType));

    public ServiceEntryInfo? GetService(string name)
        => _services.FirstOrDefault(s => string.Equals(s.ServiceName, name, StringComparison.OrdinalIgnoreCase));

    public void InjectFailure(string operation, string name, ErrorCategory category = ErrorCategory.AccessDenied)
        => _failures[$"{operation}:{name}"] = category;

    public OperationResult<IReadOnlyList<ServiceEntryInfo>> EnumerateAll()
    {
        Calls.Add("EnumerateAll");
        if (_failures.TryGetValue("EnumerateAll:*", out var fail))
            return OperationResult<IReadOnlyList<ServiceEntryInfo>>.Failure("Injected EnumerateAll failure.", fail);
        return OperationResult<IReadOnlyList<ServiceEntryInfo>>.Success(_services.ToList());
    }

    public OperationResult<ServiceStatusInfo> Query(string serviceName)
    {
        Calls.Add($"Query:{serviceName}");
        if (_failures.TryGetValue($"Query:{serviceName}", out var fail))
            return OperationResult<ServiceStatusInfo>.Failure($"Injected Query failure for '{serviceName}'.", fail);
        var info = GetService(serviceName);
        if (info is null)
            return OperationResult<ServiceStatusInfo>.Failure($"No service '{serviceName}'.", ErrorCategory.NotFound);
        return OperationResult<ServiceStatusInfo>.Success(
            new ServiceStatusInfo(info.ServiceName, info.DisplayName, info.State, info.StartType));
    }

    public OperationResult<bool> SetStartType(string serviceName, ServiceStartType startType)
    {
        Calls.Add($"SetStartType:{serviceName}:{startType}");
        if (_failures.TryGetValue($"SetStartType:{serviceName}", out var fail))
            return OperationResult<bool>.Failure($"Injected SetStartType failure for '{serviceName}'.", fail);
        var index = _services.FindIndex(s => string.Equals(s.ServiceName, serviceName, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
            return OperationResult<bool>.Failure($"No service '{serviceName}'.", ErrorCategory.NotFound);
        _services[index] = _services[index] with { StartType = startType };
        return OperationResult<bool>.Success(true);
    }

    public Task<OperationResult<bool>> StopAsync(
        string serviceName, TimeSpan timeout, CancellationToken cancellationToken = default)
        => Task.FromResult(Transition(serviceName, "Stop", ServiceState.Stopped));

    public Task<OperationResult<bool>> StartAsync(
        string serviceName, TimeSpan timeout, CancellationToken cancellationToken = default)
        => Task.FromResult(Transition(serviceName, "Start", ServiceState.Running));

    private OperationResult<bool> Transition(string serviceName, string verb, ServiceState target)
    {
        Calls.Add($"{verb}:{serviceName}");
        if (_failures.TryGetValue($"{verb}:{serviceName}", out var fail))
            return OperationResult<bool>.Failure($"Injected {verb} failure for '{serviceName}'.", fail);
        var index = _services.FindIndex(s => string.Equals(s.ServiceName, serviceName, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
            return OperationResult<bool>.Failure($"No service '{serviceName}'.", ErrorCategory.NotFound);
        _services[index] = _services[index] with { State = target };
        return OperationResult<bool>.Success(true);
    }
}
