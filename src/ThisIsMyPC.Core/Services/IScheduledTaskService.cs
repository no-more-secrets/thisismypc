using ThisIsMyPC.Core.Results;

namespace ThisIsMyPC.Core.Services;

/// <summary>
/// One scheduled task. Author/Description come from the task definition XML;
/// TriggerTypes are the trigger element names (LogonTrigger, BootTrigger,
/// CalendarTrigger, ...). LastRunTime is null when the task has never run.
/// Command/Arguments are the first Exec action; ComHandlerClsid the first
/// ComHandler action's class id; both null when the task has neither.
/// </summary>
public sealed record ScheduledTaskInfo(
    string Name,
    string Path,
    string? Author,
    string? Description,
    IReadOnlyList<string> TriggerTypes,
    DateTime? LastRunTime,
    int LastTaskResult,
    bool IsEnabled,
    string? Command = null,
    string? Arguments = null,
    string? ComHandlerClsid = null);

/// <summary>
/// Task Scheduler access via the ITaskService COM API. The app runs elevated;
/// AccessDenied failures indicate protected tasks (e.g. \Microsoft\Windows\TaskScheduler\),
/// never missing elevation.
/// </summary>
public interface IScheduledTaskService
{
    /// <summary>All tasks on the system, recursing every folder, including hidden tasks.</summary>
    OperationResult<IReadOnlyList<ScheduledTaskInfo>> EnumerateAll();

    /// <summary>Single task by full path (e.g. \Microsoft\Windows\Defrag\ScheduledDefrag).</summary>
    OperationResult<ScheduledTaskInfo> Query(string taskPath);

    OperationResult<bool> SetEnabled(string taskPath, bool enabled);
}
