using System.Runtime.InteropServices;
using ThisIsMyPC.Core.Results;

namespace ThisIsMyPC.Interop.Com.Shell;

public sealed partial class ContextMenuProbe : IContextMenuProbe
{
    // IUnknown vtable indices
    private const int VtblQueryInterface = 0;
    private const int VtblRelease = 2;

    // IShellExtInit vtable index (after IUnknown: 0=QI, 1=AddRef, 2=Release)
    private const int VtblInitialize = 3;

    // IContextMenu vtable index (after IUnknown)
    private const int VtblQueryContextMenu = 3;

    private const uint CLSCTX_INPROC_SERVER = 1;

    private static readonly Guid IID_IShellExtInit = new("000214E8-0000-0000-C000-000000000046");
    private static readonly Guid IID_IContextMenu = new("000214E4-0000-0000-C000-000000000046");
    private static readonly Guid FOLDERID_Desktop = new("B4BFCC3A-DB2C-424C-B029-7FE99A87C641");
    private static readonly Guid FOLDERID_Documents = new("FDD39AD0-238F-46AF-ADB4-6C85480369C7");

    public unsafe OperationResult<bool> HandlerAppearsOnSurface(string clsid, ContextMenuSurface surface)
    {
        if (!Guid.TryParse(clsid, out var clsidGuid))
            return OperationResult<bool>.Success(true); // can't parse → safe fallback

        nint pShellExtInit = 0;
        nint pContextMenu = 0;
        nint pidl = 0;
        nint hmenu = 0;

        try
        {
            // 1. Create the handler COM object requesting IShellExtInit
            Guid iidShellExtInit = IID_IShellExtInit;
            int hr = CoCreateInstance(in clsidGuid, 0, CLSCTX_INPROC_SERVER, in iidShellExtInit, out pShellExtInit);
            if (hr < 0)
                return OperationResult<bool>.Success(true); // COM activation failed → safe fallback

            // 2. Get PIDL for the target surface (virtual namespace PIDLs)
            Guid folderId = surface == ContextMenuSurface.DesktopBackground ? FOLDERID_Desktop : FOLDERID_Documents;
            hr = SHGetKnownFolderIDList(in folderId, 0, 0, out pidl);
            if (hr < 0)
                return OperationResult<bool>.Success(true); // PIDL failed → safe fallback

            // 3. Call IShellExtInit::Initialize via vtable
            //    Pass null for pdtobj and hkeyProgID; handlers that check these
            //    parameters will use the PIDL alone for surface determination
            var vtable = *(nint**)pShellExtInit;
            var initFn = (delegate* unmanaged[Stdcall]<nint, nint, nint, nint, int>)vtable[VtblInitialize];
            hr = initFn(pShellExtInit, pidl, 0, 0);
            if (hr < 0)
                return OperationResult<bool>.Success(false); // handler rejected this surface

            // 4. QueryInterface for IContextMenu
            Guid iidContextMenu = IID_IContextMenu;
            var qiFn = (delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)vtable[VtblQueryInterface];
            hr = qiFn(pShellExtInit, &iidContextMenu, &pContextMenu);
            if (hr < 0)
                return OperationResult<bool>.Success(true); // QI failed → safe fallback

            // 5. Create scratch menu and call QueryContextMenu
            hmenu = CreatePopupMenu();
            if (hmenu == 0)
                return OperationResult<bool>.Success(true); // menu creation failed → safe fallback

            var ctxVtable = *(nint**)pContextMenu;
            var qcmFn = (delegate* unmanaged[Stdcall]<nint, nint, uint, uint, uint, uint, int>)ctxVtable[VtblQueryContextMenu];
            hr = qcmFn(pContextMenu, hmenu, 0, 1, 0x7FFF, 0); // CMF_NORMAL = 0
            if (hr < 0)
                return OperationResult<bool>.Success(true); // QCM failed → safe fallback

            // 6. Check if the handler added any items
            int itemCount = GetMenuItemCount(hmenu);
            return OperationResult<bool>.Success(itemCount > 0);
        }
        catch
        {
            return OperationResult<bool>.Success(true); // any crash → safe fallback
        }
        finally
        {
            if (pContextMenu != 0)
                ReleaseComObject(pContextMenu);
            if (pShellExtInit != 0)
                ReleaseComObject(pShellExtInit);
            if (pidl != 0)
                CoTaskMemFree(pidl);
            if (hmenu != 0)
                DestroyMenu(hmenu);
        }
    }

    private static unsafe void ReleaseComObject(nint pUnk)
    {
        try
        {
            var vtable = *(nint**)pUnk;
            var releaseFn = (delegate* unmanaged[Stdcall]<nint, uint>)vtable[VtblRelease];
            releaseFn(pUnk);
        }
        catch
        {
            // Swallow release failures during cleanup
        }
    }

    [LibraryImport("ole32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int CoCreateInstance(in Guid rclsid, nint pUnkOuter, uint dwClsContext, in Guid riid, out nint ppv);

    [LibraryImport("shell32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int SHGetKnownFolderIDList(in Guid rfid, uint dwFlags, nint hToken, out nint ppidl);

    [LibraryImport("ole32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial void CoTaskMemFree(nint pv);

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint CreatePopupMenu();

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyMenu(nint hMenu);

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int GetMenuItemCount(nint hMenu);
}
