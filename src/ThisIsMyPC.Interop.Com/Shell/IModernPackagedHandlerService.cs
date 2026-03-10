using ThisIsMyPC.Core.Results;

namespace ThisIsMyPC.Interop.Com.Shell;

public interface IModernPackagedHandlerService
{
    OperationResult<IReadOnlyList<ModernPackagedEntry>> EnumerateModernHandlers();
}
