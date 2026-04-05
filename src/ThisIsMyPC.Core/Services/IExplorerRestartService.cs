using ThisIsMyPC.Core.Results;

namespace ThisIsMyPC.Core.Services;

public interface IExplorerRestartService
{
    Task<OperationResult<bool>> RestartExplorerAsync();
    Task<OperationResult<bool>> RefreshExplorerViewsAsync();
}
