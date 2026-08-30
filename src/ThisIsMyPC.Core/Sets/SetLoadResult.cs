namespace ThisIsMyPC.Core.Sets;

/// <summary>
/// Outcome of a set discovery pass. Invalid files never fail the load; they are
/// skipped and reported in <see cref="Warnings"/> (the host logs them).
/// </summary>
public sealed record SetLoadResult
{
    public required IReadOnlyList<SetDefinition> Sets { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
}
