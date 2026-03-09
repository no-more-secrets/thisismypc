using ThisIsMyPC.Core.Results;

namespace ThisIsMyPC.Interop.Com.Shell;

public enum ContextMenuSurface
{
    FolderBackground,
    DesktopBackground,
}

public interface IContextMenuProbe
{
    OperationResult<bool> HandlerAppearsOnSurface(string clsid, ContextMenuSurface surface);
}
