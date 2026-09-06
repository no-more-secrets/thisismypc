using ThisIsMyPC.Core.Results;

namespace ThisIsMyPC.Interop.Com.Shell;

/// <summary>
/// Refuses to activate shell extensions inside the application process.
/// Registry metadata remains available, and callers use conservative surface
/// classification when runtime probing is unavailable.
/// </summary>
public sealed class ContextMenuProbe : IContextMenuProbe
{
    public OperationResult<bool> HandlerAppearsOnSurface(string clsid, ContextMenuSurface surface)
    {
        _ = clsid;
        _ = surface;
        return OperationResult<bool>.Failure(
            "In-process shell extension activation is disabled.",
            ErrorCategory.ProtectedByPolicy);
    }
}
