namespace ThisIsMyPC.Core.Actions;

/// <summary>
/// A one-way operation staged for batch execution (e.g. package install/uninstall).
/// Unlike <see cref="Changes.ChangeDescriptor"/> there is no before-state and no
/// undo: actions never enter the change history. <see cref="UndoHint"/> tells the
/// user the manual recovery path, if one exists.
/// </summary>
public sealed record ActionDescriptor
{
    /// <summary>Module that executes this action; must match <c>ModuleInfo.Name</c>.</summary>
    public required string ModuleId { get; init; }

    /// <summary>Stable identity within the queue; staging the same id twice is a no-op.</summary>
    public required string ActionId { get; init; }

    /// <summary>User-facing label, e.g. "Install 1Password".</summary>
    public required string DisplayName { get; init; }

    /// <summary>System location line for the review panel, e.g. "winget: AgileBits.1Password".</summary>
    public required string Detail { get; init; }

    /// <summary>How the user gets back if they change their mind, or null when there is no path.</summary>
    public string? UndoHint { get; init; }
}
