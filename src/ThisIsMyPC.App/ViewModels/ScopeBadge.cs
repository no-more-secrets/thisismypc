namespace ThisIsMyPC.App.ViewModels;

/// <summary>
/// One scope chip on a Multi tab row. Kind selects a style class
/// (Border.scope-badge.{kind}) whose DynamicResource background follows the
/// theme; the bools exist because Classes.x bindings need booleans.
/// </summary>
public sealed record ScopeBadge(string Label, string Kind)
{
    public bool IsFile => Kind == "file";
    public bool IsFolder => Kind == "folder";
    public bool IsBackground => Kind == "background";
    public bool IsDesktop => Kind == "desktop";
    public bool IsMisc => Kind == "misc";
}
