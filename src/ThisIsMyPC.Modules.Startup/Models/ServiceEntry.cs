using ThisIsMyPC.Core.Services;

namespace ThisIsMyPC.Modules.Startup.Models;

public sealed record ServiceEntry
{
    public required string ServiceName { get; init; }
    public required string DisplayName { get; init; }
    public string? Description { get; init; }
    public required ServiceState State { get; init; }
    public required ServiceStartType StartType { get; init; }

    /// <summary>True for per-user service template instances (e.g. CDPUserSvc_5f3a2).</summary>
    public bool IsPerUserInstance { get; init; }

    /// <summary>The template service name a per-user instance belongs to (e.g. CDPUserSvc).</summary>
    public string? TemplateServiceName { get; init; }
}
