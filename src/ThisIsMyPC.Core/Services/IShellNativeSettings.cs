using ThisIsMyPC.Core.Results;
namespace ThisIsMyPC.Core.Services;
public interface IShellNativeSettings
{
    OperationResult<string> ReadTaskbarState();
    OperationResult<bool> WriteTaskbarState(string state);
    OperationResult<string> ReadRoundedCornersState();
    Task<OperationResult<bool>> WriteRoundedCornersStateAsync(string state);
}
