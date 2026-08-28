using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;

namespace ThisIsMyPC.Integration.Tests.Fakes;

/// <summary>Scriptable in-memory IScheduledTaskService with call recording and injectable failures.</summary>
internal sealed class FakeScheduledTaskService : IScheduledTaskService
{
    private readonly List<ScheduledTaskInfo> _tasks = [];
    private readonly Dictionary<string, ErrorCategory> _failures = new(StringComparer.OrdinalIgnoreCase);

    public List<string> Calls { get; } = [];

    public void AddTask(
        string path, bool enabled = true, string? author = null, string? description = null,
        IReadOnlyList<string>? triggers = null, DateTime? lastRun = null, int lastResult = 0)
    {
        var name = path[(path.LastIndexOf('\\') + 1)..];
        _tasks.Add(new ScheduledTaskInfo(name, path, author, description, triggers ?? [], lastRun, lastResult, enabled));
    }

    public ScheduledTaskInfo? GetTask(string path)
        => _tasks.FirstOrDefault(t => string.Equals(t.Path, path, StringComparison.OrdinalIgnoreCase));

    public void InjectFailure(string operation, string path, ErrorCategory category = ErrorCategory.AccessDenied)
        => _failures[$"{operation}:{path}"] = category;

    public OperationResult<IReadOnlyList<ScheduledTaskInfo>> EnumerateAll()
    {
        Calls.Add("EnumerateAll");
        if (_failures.TryGetValue("EnumerateAll:*", out var fail))
            return OperationResult<IReadOnlyList<ScheduledTaskInfo>>.Failure("Injected EnumerateAll failure.", fail);
        return OperationResult<IReadOnlyList<ScheduledTaskInfo>>.Success(_tasks.ToList());
    }

    public OperationResult<ScheduledTaskInfo> Query(string taskPath)
    {
        Calls.Add($"Query:{taskPath}");
        if (_failures.TryGetValue($"Query:{taskPath}", out var fail))
            return OperationResult<ScheduledTaskInfo>.Failure($"Injected Query failure for '{taskPath}'.", fail);
        var task = GetTask(taskPath);
        if (task is null)
            return OperationResult<ScheduledTaskInfo>.Failure($"No task '{taskPath}'.", ErrorCategory.NotFound);
        return OperationResult<ScheduledTaskInfo>.Success(task);
    }

    public OperationResult<bool> SetEnabled(string taskPath, bool enabled)
    {
        Calls.Add($"SetEnabled:{taskPath}:{enabled}");
        if (_failures.TryGetValue($"SetEnabled:{taskPath}", out var fail))
            return OperationResult<bool>.Failure($"Injected SetEnabled failure for '{taskPath}'.", fail);
        var index = _tasks.FindIndex(t => string.Equals(t.Path, taskPath, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
            return OperationResult<bool>.Failure($"No task '{taskPath}'.", ErrorCategory.NotFound);
        _tasks[index] = _tasks[index] with { IsEnabled = enabled };
        return OperationResult<bool>.Success(true);
    }
}
