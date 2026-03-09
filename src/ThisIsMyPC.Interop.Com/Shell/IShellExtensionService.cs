using ThisIsMyPC.Core.Results;

namespace ThisIsMyPC.Interop.Com.Shell;

public interface IShellExtensionService
{
    OperationResult<IReadOnlyList<ShellExtensionInfo>> EnumerateContextMenuHandlers();
    bool IsBlockedByCLSID(string clsid);
    IReadOnlySet<string> GetBlockedClsids();
}
