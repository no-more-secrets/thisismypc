using ThisIsMyPC.Core.Services;

namespace ThisIsMyPC.Modules.Startup.Models;

public enum TaskClassification
{
    Unknown,
    Telemetry,
    Oem,
    CompatibilityDiagnostics,
    Maintenance,
    UserCreated,
}

public sealed record ScheduledTaskEntry
{
    public required string Name { get; init; }
    public required string Path { get; init; }
    public string? Author { get; init; }
    public string? Description { get; init; }
    public IReadOnlyList<string> TriggerTypes { get; init; } = [];
    public DateTime? LastRunTime { get; init; }
    public int LastTaskResult { get; init; }
    public required bool IsEnabled { get; init; }
    public required TaskClassification Classification { get; init; }

    /// <summary>The first Exec action's program, as written in the task (may be quoted, relative, or use %vars%).</summary>
    public string? Command { get; init; }
    public string? Arguments { get; init; }

    /// <summary>The first ComHandler action's class id when the task runs COM code instead of a program.</summary>
    public string? ComHandlerClsid { get; init; }

    /// <summary>True when the user overrode the auto-classification.</summary>
    public bool IsClassificationOverridden { get; init; }

    /// <summary>True for tasks that back an enforcement mechanism (e.g. UCPD velocity).</summary>
    public bool IsCompanionTask { get; init; }

    /// <summary>Human explanation of the enforcement mechanism a companion task supports.</summary>
    public string? CompanionDescription { get; init; }

    /// <summary>True when the task fires at logon or boot; surfaces in the Startup section.</summary>
    public bool IsStartupTask =>
        TriggerTypes.Contains("LogonTrigger") || TriggerTypes.Contains("BootTrigger");
}
