using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;

namespace ThisIsMyPC.Integration.Tests.Fakes;

/// <summary>Empty-folder IStartupFolderService for set-preview tests.</summary>
public sealed class FakeStartupFolderService : IStartupFolderService
{
    public OperationResult<IReadOnlyList<StartupFolderItem>> Enumerate(StartupFolderScope scope)
        => OperationResult<IReadOnlyList<StartupFolderItem>>.Success([]);
}
