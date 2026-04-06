using ThisIsMyPC.Core.Results;

namespace ThisIsMyPC.Core.Services;

public interface IDataDirectoryGuard
{
    OperationResult<DaclStatus> EnsureHardened(string directoryPath);
}

public enum DaclStatus
{
    Created,
    Verified,
    Repaired,
    Failed,
}
