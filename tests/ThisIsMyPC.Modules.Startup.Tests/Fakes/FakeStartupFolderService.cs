using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;

namespace ThisIsMyPC.Modules.Startup.Tests.Fakes;

/// <summary>
/// Manual fake for IStartupFolderService: per-scope item lists, an in-memory
/// AutorunsDisabled subfolder per scope, and Move between the two.
/// </summary>
public sealed class FakeStartupFolderService : IStartupFolderService
{
    private readonly Dictionary<StartupFolderScope, List<StartupFolderItem>> _items = new()
    {
        [StartupFolderScope.CurrentUser] = [],
        [StartupFolderScope.AllUsers] = [],
    };

    private readonly Dictionary<StartupFolderScope, List<StartupFolderItem>> _disabled = new()
    {
        [StartupFolderScope.CurrentUser] = [],
        [StartupFolderScope.AllUsers] = [],
    };

    private readonly Dictionary<StartupFolderScope, ErrorCategory> _failures = [];

    public List<(string From, string To)> Moves { get; } = [];

    public void AddItem(StartupFolderScope scope, string filePath, string? resolvedTarget = null)
        => _items[scope].Add(new StartupFolderItem(filePath, resolvedTarget));

    public void AddDisabledItem(StartupFolderScope scope, string filePath, string? resolvedTarget = null)
        => _disabled[scope].Add(new StartupFolderItem(filePath, resolvedTarget));

    public void InjectFailure(StartupFolderScope scope, ErrorCategory category = ErrorCategory.AccessDenied)
        => _failures[scope] = category;

    public OperationResult<IReadOnlyList<StartupFolderItem>> Enumerate(StartupFolderScope scope)
    {
        if (_failures.TryGetValue(scope, out var category))
            return OperationResult<IReadOnlyList<StartupFolderItem>>.Failure("Injected failure", category);
        return OperationResult<IReadOnlyList<StartupFolderItem>>.Success(_items[scope]);
    }

    public OperationResult<IReadOnlyList<StartupFolderItem>> EnumerateDisabled(StartupFolderScope scope)
        => OperationResult<IReadOnlyList<StartupFolderItem>>.Success(_disabled[scope]);

    /// <summary>Every fake file reads as its own path in UTF-8, so a snapshot round trip is checkable.</summary>
    public OperationResult<byte[]> ReadAllBytes(string path, int maxBytes)
        => Find(path) is null
            ? OperationResult<byte[]>.Failure($"File not found: {path}", ErrorCategory.NotFound)
            : OperationResult<byte[]>.Success(System.Text.Encoding.UTF8.GetBytes(path));

    public List<string> Deleted { get; } = [];
    public List<(string Path, byte[] Contents)> Restored { get; } = [];

    public OperationResult<bool> Delete(string path)
    {
        Deleted.Add(path);
        foreach (var list in _items.Values.Concat(_disabled.Values))
            list.RemoveAll(i => string.Equals(i.FilePath, path, StringComparison.OrdinalIgnoreCase));
        return OperationResult<bool>.Success(true);
    }

    public OperationResult<bool> Restore(string path, byte[] contents)
    {
        Restored.Add((path, contents));
        if (Find(path) is null)
        {
            var scope = path.Contains("ProgramData", StringComparison.OrdinalIgnoreCase) ? StartupFolderScope.AllUsers : StartupFolderScope.CurrentUser;
            (path.Contains(IStartupFolderService.DisabledSubfolder, StringComparison.OrdinalIgnoreCase) ? _disabled : _items)[scope]
                .Add(new StartupFolderItem(path, null));
        }
        return OperationResult<bool>.Success(true);
    }

    private StartupFolderItem? Find(string path)
        => _items.Values.Concat(_disabled.Values).SelectMany(l => l)
            .FirstOrDefault(i => string.Equals(i.FilePath, path, StringComparison.OrdinalIgnoreCase));

    /// <summary>Moves between a scope's folder and its AutorunsDisabled subfolder, by path match.</summary>
    public OperationResult<bool> Move(string fromPath, string toPath)
    {
        Moves.Add((fromPath, toPath));
        foreach (var scope in _items.Keys)
        {
            if (TryMove(_items[scope], _disabled[scope], fromPath, toPath) || TryMove(_disabled[scope], _items[scope], fromPath, toPath))
                return OperationResult<bool>.Success(true);
        }
        return OperationResult<bool>.Failure($"File not found: {fromPath}", ErrorCategory.NotFound);
    }

    private static bool TryMove(List<StartupFolderItem> from, List<StartupFolderItem> to, string fromPath, string toPath)
    {
        var item = from.FirstOrDefault(i => string.Equals(i.FilePath, fromPath, StringComparison.OrdinalIgnoreCase));
        if (item is null)
            return false;
        from.Remove(item);
        to.Add(item with { FilePath = toPath });
        return true;
    }
}
