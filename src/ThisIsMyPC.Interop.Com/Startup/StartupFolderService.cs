using System.Runtime.InteropServices;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;

namespace ThisIsMyPC.Interop.Com.Startup;

/// <summary>
/// Enumerates startup folder contents and resolves .lnk shortcut targets via
/// IShellLinkW/IPersistFile (raw vtable calls; NativeAOT-safe, no COM wrappers).
/// </summary>
public sealed partial class StartupFolderService : IStartupFolderService
{
    private const int VtblQueryInterface = 0;
    private const int VtblRelease = 2;
    private const int VtblShellLinkGetPath = 3;   // IShellLinkW: first method after IUnknown
    private const int VtblPersistFileLoad = 5;    // IPersist(3=GetClassID) + IsDirty(4) + Load(5)

    private const uint CLSCTX_INPROC_SERVER = 1;
    private const uint COINIT_APARTMENTTHREADED = 0x2;
    private const int TargetBufferChars = 1024; // GetPath fills what fits; generous for long paths

    private static readonly Guid CLSID_ShellLink = new("00021401-0000-0000-C000-000000000046");
    private static readonly Guid IID_IShellLinkW = new("000214F9-0000-0000-C000-000000000046");
    private static readonly Guid IID_IPersistFile = new("0000010b-0000-0000-C000-000000000046");

    public OperationResult<IReadOnlyList<StartupFolderItem>> Enumerate(StartupFolderScope scope)
    {
        try
        {
            var folder = scope == StartupFolderScope.CurrentUser
                ? Environment.GetFolderPath(Environment.SpecialFolder.Startup)
                : Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup);

            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
                return OperationResult<IReadOnlyList<StartupFolderItem>>.Success(Array.Empty<StartupFolderItem>());

            var items = new List<StartupFolderItem>();
            var needUninit = false;
            try
            {
                // S_OK/S_FALSE must be balanced with CoUninitialize; RPC_E_CHANGED_MODE
                // means the thread is already MTA; proceed without balancing.
                needUninit = CoInitializeEx(0, COINIT_APARTMENTTHREADED) >= 0;

                foreach (var file in Directory.EnumerateFiles(folder))
                {
                    var fileName = Path.GetFileName(file);
                    if (fileName.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase))
                        continue;

                    string? target = null;
                    if (Path.GetExtension(file).Equals(".lnk", StringComparison.OrdinalIgnoreCase))
                        target = ResolveShortcutTarget(file);

                    items.Add(new StartupFolderItem(file, target));
                }
            }
            finally
            {
                if (needUninit)
                    CoUninitialize();
            }

            return OperationResult<IReadOnlyList<StartupFolderItem>>.Success(items);
        }
        catch (UnauthorizedAccessException ex)
        {
            return OperationResult<IReadOnlyList<StartupFolderItem>>.Failure(
                $"Access denied enumerating startup folder ({scope})", ErrorCategory.AccessDenied, ex);
        }
        catch (Exception ex)
        {
            return OperationResult<IReadOnlyList<StartupFolderItem>>.Failure(
                $"Failed to enumerate startup folder ({scope}): {ex.Message}", ErrorCategory.ServiceUnavailable, ex);
        }
    }

    private static unsafe string? ResolveShortcutTarget(string lnkPath)
    {
        nint pShellLink = 0;
        nint pPersistFile = 0;

        try
        {
            var iid = IID_IShellLinkW;
            var hr = CoCreateInstance(in CLSID_ShellLink, 0, CLSCTX_INPROC_SERVER, in iid, out pShellLink);
            if (hr < 0)
                return null;

            var vtable = *(nint**)pShellLink;
            var iidPersistFile = IID_IPersistFile;
            var qiFn = (delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)vtable[VtblQueryInterface];
            nint persistFile;
            hr = qiFn(pShellLink, &iidPersistFile, &persistFile);
            if (hr < 0)
                return null;
            pPersistFile = persistFile;

            var persistVtable = *(nint**)pPersistFile;
            var loadFn = (delegate* unmanaged[Stdcall]<nint, char*, uint, int>)persistVtable[VtblPersistFileLoad];
            fixed (char* pathPtr = lnkPath)
            {
                hr = loadFn(pPersistFile, pathPtr, 0 /* STGM_READ */);
            }
            if (hr < 0)
                return null;

            var buffer = stackalloc char[TargetBufferChars];
            var getPathFn = (delegate* unmanaged[Stdcall]<nint, char*, int, nint, uint, int>)vtable[VtblShellLinkGetPath];
            hr = getPathFn(pShellLink, buffer, TargetBufferChars, 0, 0);
            if (hr != 0) // S_OK only; S_FALSE means no path (e.g., MSI or URL target)
                return null;

            var target = new string(buffer);
            return target.Length == 0 ? null : target;
        }
        catch
        {
            return null; // resolution is best-effort; caller falls back to the .lnk path
        }
        finally
        {
            if (pPersistFile != 0)
                ReleaseComObject(pPersistFile);
            if (pShellLink != 0)
                ReleaseComObject(pShellLink);
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
    private static partial int CoInitializeEx(nint pvReserved, uint dwCoInit);

    [LibraryImport("ole32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial void CoUninitialize();

    [LibraryImport("ole32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int CoCreateInstance(in Guid rclsid, nint pUnkOuter, uint dwClsContext, in Guid riid, out nint ppv);
}
