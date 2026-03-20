using ThisIsMyPC.Core.Results;

namespace ThisIsMyPC.Interop.Com.Shell;

public interface IShellExtensionService
{
    OperationResult<IReadOnlyList<ShellExtensionInfo>> EnumerateContextMenuHandlers();
    OperationResult<IReadOnlyList<DragDropHandlerInfo>> EnumerateDragDropHandlers();
    bool IsBlockedByCLSID(string clsid);
    IReadOnlySet<string> GetBlockedClsids();
}
