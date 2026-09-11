using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using ThisIsMyPC.Installer.Win32;

namespace ThisIsMyPC.Installer.Services;

/// <summary>The stock Windows folder dialog, owned by the installer window.</summary>
public sealed unsafe class StorageFolderPicker : IFolderPicker
{
    private readonly nint _owner;

    public StorageFolderPicker(nint owner)
    {
        if (owner == nint.Zero)
            throw new ArgumentException("The folder dialog needs an owner window.", nameof(owner));
        _owner = owner;
    }

    public Task<string?> PickAsync(string startFolder)
    {
        var displayName = Marshal.AllocHGlobal(520 * sizeof(char));
        var title = Marshal.StringToHGlobalUni("Choose where to install ThisIsMyPC");
        var initialFolder = Marshal.StringToHGlobalUni(startFolder);
        try
        {
            var info = new NativeMethods.BROWSEINFOW
            {
                hwndOwner = _owner,
                pszDisplayName = displayName,
                lpszTitle = title,
                ulFlags = NativeMethods.BIF_RETURNONLYFSDIRS | NativeMethods.BIF_USENEWUI,
                lpfn = (nint)(delegate* unmanaged[Stdcall]<nint, uint, nuint, nint, nint>)&BrowseCallback,
                lParam = initialFolder,
            };
            var item = NativeMethods.SHBrowseForFolder(ref info);
            if (item == nint.Zero)
                return Task.FromResult<string?>(null);
            try
            {
                var path = stackalloc char[32768];
                return Task.FromResult<string?>(NativeMethods.SHGetPathFromIDList(item, path)
                    ? new string(path)
                    : null);
            }
            finally
            {
                NativeMethods.CoTaskMemFree(item);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(initialFolder);
            Marshal.FreeHGlobal(title);
            Marshal.FreeHGlobal(displayName);
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static nint BrowseCallback(nint hwnd, uint message, nuint wParam, nint lParam)
    {
        if (message == NativeMethods.BFFM_INITIALIZED && lParam != nint.Zero)
            _ = NativeMethods.SendMessage(hwnd, NativeMethods.BFFM_SETSELECTIONW, 1, lParam);
        return nint.Zero;
    }
}
