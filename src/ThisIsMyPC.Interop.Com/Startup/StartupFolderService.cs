using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;

namespace ThisIsMyPC.Interop.Com.Startup;

/// <summary>
/// Enumerates startup folder contents without loading or parsing file contents.
/// Shortcut resolution belongs in a non-elevated process.
/// </summary>
public sealed class StartupFolderService : IStartupFolderService
{
    private readonly string _currentUserFolder;
    private readonly string _allUsersFolder;

    public StartupFolderService()
        : this(
            Environment.GetFolderPath(Environment.SpecialFolder.Startup),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup))
    {
    }

    internal StartupFolderService(string currentUserFolder, string allUsersFolder)
    {
        _currentUserFolder = currentUserFolder;
        _allUsersFolder = allUsersFolder;
    }

    public OperationResult<IReadOnlyList<StartupFolderItem>> Enumerate(StartupFolderScope scope)
        => EnumerateFolder(GetFolder(scope), scope);

    /// <summary>The AutorunsDisabled subfolder, where Autoruns and this app park disabled files.</summary>
    public OperationResult<IReadOnlyList<StartupFolderItem>> EnumerateDisabled(StartupFolderScope scope)
    {
        var folder = GetFolder(scope);
        return string.IsNullOrEmpty(folder)
            ? OperationResult<IReadOnlyList<StartupFolderItem>>.Success([])
            : EnumerateFolder(Path.Combine(folder, IStartupFolderService.DisabledSubfolder), scope);
    }

    public OperationResult<byte[]> ReadAllBytes(string path, int maxBytes)
    {
        if (!IsManagedFile(path))
            return Refused<byte[]>(path);

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.SequentialScan);
            if (stream.Length > maxBytes)
                return OperationResult<byte[]>.Failure($"{path} is too large to snapshot.", ErrorCategory.ServiceUnavailable);

            var bytes = new byte[checked((int)stream.Length)];
            stream.ReadExactly(bytes);
            return OperationResult<byte[]>.Success(bytes);
        }
        catch (FileNotFoundException ex)
        {
            return OperationResult<byte[]>.Failure($"File not found: {path}", ErrorCategory.NotFound, ex);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OverflowException)
        {
            return OperationResult<byte[]>.Failure($"Could not read {path}: {ex.Message}", ErrorCategory.AccessDenied, ex);
        }
    }

    public OperationResult<bool> Delete(string path)
    {
        if (!IsManagedFile(path))
            return Refused<bool>(path);

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

    public OperationResult<bool> Restore(string path, byte[] contents)
    {
        ArgumentNullException.ThrowIfNull(contents);
        if (!IsManagedFile(path, allowMissingDisabledFolder: true))
            return Refused<bool>(path);

        try
        {
            if (File.Exists(path))
                return OperationResult<bool>.Success(true);

            var directory = Path.GetDirectoryName(path)!;
            Directory.CreateDirectory(directory);
            if (!IsManagedFile(path))
                return Refused<bool>(path);

            using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            stream.Write(contents);
            return OperationResult<bool>.Success(true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return OperationResult<bool>.Failure($"Could not restore {path}: {ex.Message}", ErrorCategory.AccessDenied, ex);
        }
    }

    public OperationResult<bool> Move(string fromPath, string toPath)
    {
        if (!IsManagedFile(fromPath, allowMissingDisabledFolder: true) ||
            !IsManagedFile(toPath, allowMissingDisabledFolder: true) ||
            !HaveSameManagedRoot(fromPath, toPath))
        {
            return OperationResult<bool>.Failure(
                "Startup file move escaped its managed folder.", ErrorCategory.ProtectedByPolicy);
        }

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

            Directory.CreateDirectory(Path.GetDirectoryName(toPath)!);
            if (!IsManagedFile(fromPath) || !IsManagedFile(toPath))
            {
                return OperationResult<bool>.Failure(
                    "Startup file move encountered a reparse point.", ErrorCategory.ProtectedByPolicy);
            }

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

    private string GetFolder(StartupFolderScope scope) => scope == StartupFolderScope.CurrentUser
        ? _currentUserFolder
        : _allUsersFolder;

    private bool IsManagedFile(string path, bool allowMissingDisabledFolder = false)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        try
        {
            var fullPath = Path.GetFullPath(path);
            var parent = Path.GetDirectoryName(fullPath);
            var name = Path.GetFileName(fullPath);
            if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(name) || name is "." or "..")
                return false;
            if (!fullPath.Equals(Path.Combine(parent, name), StringComparison.OrdinalIgnoreCase))
                return false;

            foreach (var root in ManagedRoots())
            {
                var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
                var disabled = Path.Combine(fullRoot, IStartupFolderService.DisabledSubfolder);
                if (!parent.Equals(fullRoot, StringComparison.OrdinalIgnoreCase) &&
                    !parent.Equals(disabled, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (IsReparsePoint(fullRoot))
                    return false;
                if (Directory.Exists(parent) && IsReparsePoint(parent))
                    return false;
                if (!Directory.Exists(parent) && !allowMissingDisabledFolder)
                    return false;
                if (File.Exists(fullPath) && IsReparsePoint(fullPath))
                    return false;
                return true;
            }
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException or UnauthorizedAccessException)
        {
        }

        return false;
    }

    private bool HaveSameManagedRoot(string left, string right)
    {
        var leftFull = Path.GetFullPath(left);
        var rightFull = Path.GetFullPath(right);
        return ManagedRoots().Any(root =>
        {
            var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
            return IsDirectManagedChild(leftFull, fullRoot) && IsDirectManagedChild(rightFull, fullRoot);
        });
    }

    private IEnumerable<string> ManagedRoots()
    {
        if (!string.IsNullOrWhiteSpace(_currentUserFolder))
            yield return _currentUserFolder;
        if (!string.IsNullOrWhiteSpace(_allUsersFolder))
            yield return _allUsersFolder;
    }

    private static bool IsDirectManagedChild(string path, string root)
    {
        var parent = Path.GetDirectoryName(path);
        return parent is not null &&
            (parent.Equals(root, StringComparison.OrdinalIgnoreCase) ||
             parent.Equals(Path.Combine(root, IStartupFolderService.DisabledSubfolder), StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsReparsePoint(string path)
        => (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

    private static OperationResult<T> Refused<T>(string path)
        => OperationResult<T>.Failure(
            $"Refused startup file path outside a managed folder: {path}", ErrorCategory.ProtectedByPolicy);

    private static OperationResult<IReadOnlyList<StartupFolderItem>> EnumerateFolder(string folder, StartupFolderScope scope)
    {
        try
        {
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
                return OperationResult<IReadOnlyList<StartupFolderItem>>.Success([]);

            var items = Directory.EnumerateFiles(folder)
                .Where(file => !Path.GetFileName(file).Equals("desktop.ini", StringComparison.OrdinalIgnoreCase))
                .Select(file => new StartupFolderItem(file, ResolvedTarget: null))
                .ToArray();

            return OperationResult<IReadOnlyList<StartupFolderItem>>.Success(items);
        }
        catch (UnauthorizedAccessException ex)
        {
            return OperationResult<IReadOnlyList<StartupFolderItem>>.Failure(
                $"Access denied enumerating startup folder ({scope})", ErrorCategory.AccessDenied, ex);
        }
        catch (Exception ex)
        {
            return OperationResult<IReadOnlyList<StartupFolderItem>>.Failure(
                $"Failed to enumerate startup folder ({scope}): {ex.Message}", ErrorCategory.ServiceUnavailable, ex);
        }
    }
}
