using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;

namespace ThisIsMyPC.Modules.Startup.Tests.Fakes;

/// <summary>Manual fake for IStartupFolderService with per-scope item lists.</summary>
public sealed class FakeStartupFolderService : IStartupFolderService
{
    private readonly Dictionary<StartupFolderScope, List<StartupFolderItem>> _items = new()
    {
        [StartupFolderScope.CurrentUser] = [],
        [StartupFolderScope.AllUsers] = [],
    };

    private readonly Dictionary<StartupFolderScope, ErrorCategory> _failures = [];

    public void AddItem(StartupFolderScope scope, string filePath, string? resolvedTarget = null)
        => _items[scope].Add(new StartupFolderItem(filePath, resolvedTarget));

    public void InjectFailure(StartupFolderScope scope, ErrorCategory category = ErrorCategory.AccessDenied)
        => _failures[scope] = category;

    public OperationResult<IReadOnlyList<StartupFolderItem>> Enumerate(StartupFolderScope scope)
    {
        if (_failures.TryGetValue(scope, out var category))
            return OperationResult<IReadOnlyList<StartupFolderItem>>.Failure("Injected failure", category);
        return OperationResult<IReadOnlyList<StartupFolderItem>>.Success(_items[scope]);
    }
}
