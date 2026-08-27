namespace ThisIsMyPC.Core.Sets;

public sealed record SetDefinition
{
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required SetCategory Category { get; init; }
    public required string Version { get; init; }
    public required string Author { get; init; }
    public required IReadOnlyList<SetEntry> Entries { get; init; }

    /// <summary>Assigned by the provider from the directory the file was found in.</summary>
    public required SetSource Source { get; init; }

    /// <summary>Absolute path of the JSON file this definition was loaded from.</summary>
    public required string FilePath { get; init; }
}
