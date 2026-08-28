using ThisIsMyPC.Core.Results;

namespace ThisIsMyPC.Core.Services;

public enum StartupFolderScope
{
    CurrentUser,
    AllUsers,
}

/// <summary>
/// A file discovered in a startup folder. ResolvedTarget is the shortcut target
/// for .lnk files (null when resolution fails or the file is not a shortcut).
/// </summary>
public sealed record StartupFolderItem(string FilePath, string? ResolvedTarget);

public interface IStartupFolderService
{
    OperationResult<IReadOnlyList<StartupFolderItem>> Enumerate(StartupFolderScope scope);
}
