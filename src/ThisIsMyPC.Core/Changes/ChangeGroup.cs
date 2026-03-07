namespace ThisIsMyPC.Core.Changes;

public record ChangeGroup
{
    public required string GroupId { get; init; }
    public required string DisplayName { get; init; }
    public required string Description { get; init; }
    public required IReadOnlyList<ChangeDescriptor> Changes { get; init; }
}
