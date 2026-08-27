namespace ThisIsMyPC.Core.Sets;

/// <summary>
/// Discovers and loads set definitions from the built-in and user sets directories.
/// Loading never throws for bad content: unparseable or schema-invalid files are
/// skipped with a warning in the result. Entries referencing modules or settings that
/// don't exist on this system still load — applicability is resolved at preview time
/// (Story 8.3), not here.
/// </summary>
public interface ISetProvider
{
    SetLoadResult LoadSets();
}
