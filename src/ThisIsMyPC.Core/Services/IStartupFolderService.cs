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

    /// <summary>Reads a startup file whole for a snapshot; fails when it is larger than <paramref name="maxBytes"/>.</summary>
    OperationResult<byte[]> ReadAllBytes(string path, int maxBytes)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists)
                return OperationResult<byte[]>.Failure($"File not found: {path}", ErrorCategory.NotFound);
            if (info.Length > maxBytes)
                return OperationResult<byte[]>.Failure($"{path} is too large to snapshot.", ErrorCategory.ServiceUnavailable);
            return OperationResult<byte[]>.Success(File.ReadAllBytes(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return OperationResult<byte[]>.Failure($"Could not read {path}: {ex.Message}", ErrorCategory.AccessDenied, ex);
        }
    }

    /// <summary>Deletes one startup file (a copy that re-registered itself beside a parked twin). Missing is success.</summary>
    OperationResult<bool> Delete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
            return OperationResult<bool>.Success(true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return OperationResult<bool>.Failure($"Could not delete {path}: {ex.Message}", ErrorCategory.AccessDenied, ex);
        }
    }

    /// <summary>Writes a startup file back from its snapshot (undo of a purge). Never overwrites.</summary>
    OperationResult<bool> Restore(string path, byte[] contents)
    {
        try
        {
            if (File.Exists(path))
                return OperationResult<bool>.Success(true);
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            File.WriteAllBytes(path, contents);
            return OperationResult<bool>.Success(true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return OperationResult<bool>.Failure($"Could not restore {path}: {ex.Message}", ErrorCategory.AccessDenied, ex);
        }
    }

    /// <summary>
    /// Moves one startup file, creating the destination folder. Default: the
    /// file system. Idempotent (source gone, destination present is success)
    /// and never overwrites: both present is a failure before anything moves.
    /// </summary>
    OperationResult<bool> Move(string fromPath, string toPath)
    {
        try
        {
            var atDestination = File.Exists(toPath);
            if (!File.Exists(fromPath))
            {
                return atDestination
                    ? OperationResult<bool>.Success(true)
                    : OperationResult<bool>.Failure($"File not found: {fromPath}", ErrorCategory.NotFound);
            }
            if (atDestination)
            {
                return OperationResult<bool>.Failure(
                    $"{toPath} already exists, so the move would overwrite it. Remove or rename that copy first.",
                    ErrorCategory.ServiceUnavailable);
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
