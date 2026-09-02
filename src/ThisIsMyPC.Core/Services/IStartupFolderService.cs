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
    /// <summary>Subfolder Autoruns parks disabled startup files in; the app uses the same one.</summary>
    const string DisabledSubfolder = "AutorunsDisabled";

    OperationResult<IReadOnlyList<StartupFolderItem>> Enumerate(StartupFolderScope scope);

    /// <summary>Files in the scope's AutorunsDisabled subfolder. Default: none.</summary>
    OperationResult<IReadOnlyList<StartupFolderItem>> EnumerateDisabled(StartupFolderScope scope)
        => OperationResult<IReadOnlyList<StartupFolderItem>>.Success([]);

    /// <summary>Moves one startup file, creating the destination folder. Default: the file system.</summary>
    OperationResult<bool> Move(string fromPath, string toPath)
    {
        try
        {
            if (!File.Exists(fromPath))
            {
                return File.Exists(toPath)
                    ? OperationResult<bool>.Success(true)
                    : OperationResult<bool>.Failure($"File not found: {fromPath}", ErrorCategory.NotFound);
            }
            var directory = Path.GetDirectoryName(toPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            File.Move(fromPath, toPath, overwrite: false);
            return OperationResult<bool>.Success(true);
        }
        catch (UnauthorizedAccessException ex)
        {
            return OperationResult<bool>.Failure($"Access denied moving {fromPath}", ErrorCategory.AccessDenied, ex);
        }
        catch (IOException ex)
        {
            return OperationResult<bool>.Failure($"Could not move {fromPath}: {ex.Message}", ErrorCategory.ServiceUnavailable, ex);
        }
    }
}
