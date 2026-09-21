using ThisIsMyPC.Installer.Win32;

namespace ThisIsMyPC.Installer.Services;

/// <summary>The modern Windows folder dialog, owned by the installer window.</summary>
public sealed class StorageFolderPicker : IFolderPicker
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
        using var dialog = FolderDialog.Create(startFolder, "Choose where to install ThisIsMyPC");
        return Task.FromResult(dialog?.Show(_owner));
    }
}

/// <summary>IFileOpenDialog in folder mode, driven through its raw vtable.</summary>
internal sealed unsafe class FolderDialog : IDisposable
{
    // IModalWindow::Show, then IFileDialog in filedialog.idl order.
    private const int VtblShow = 3;
    private const int VtblSetOptions = 9;
    private const int VtblGetOptions = 10;
    private const int VtblSetFolder = 12;
    private const int VtblGetFolder = 13;
    private const int VtblSetTitle = 17;
    private const int VtblGetResult = 20;
    private const int VtblShellItemGetDisplayName = 5;
    private const int VtblRelease = 2;

    private nint _dialog;

    private FolderDialog(nint dialog) => _dialog = dialog;

    /// <summary>Creates the dialog, or returns null when Windows cannot provide it.</summary>
    internal static FolderDialog? Create(string startFolder, string title)
    {
        if (NativeMethods.CoCreateInstance(in NativeMethods.CLSID_FileOpenDialog, nint.Zero, NativeMethods.CLSCTX_INPROC_SERVER,
                in NativeMethods.IID_IFileOpenDialog, out var handle) < 0 || handle == nint.Zero)
            return null;
        var dialog = new FolderDialog(handle);
        try
        {
            var options = dialog.Options | NativeMethods.FOS_PICKFOLDERS | NativeMethods.FOS_FORCEFILESYSTEM |
                NativeMethods.FOS_PATHMUSTEXIST | NativeMethods.FOS_NOCHANGEDIR;
            _ = ((delegate* unmanaged[Stdcall]<nint, uint, int>)Vtable(handle)[VtblSetOptions])(handle, options);
            fixed (char* text = title)
                _ = ((delegate* unmanaged[Stdcall]<nint, char*, int>)Vtable(handle)[VtblSetTitle])(handle, text);
            var existing = NearestExistingFolder(startFolder);
            if (existing is not null &&
                NativeMethods.SHCreateItemFromParsingName(existing, nint.Zero, in NativeMethods.IID_IShellItem, out var item) >= 0 && item != nint.Zero)
            {
                try { _ = ((delegate* unmanaged[Stdcall]<nint, nint, int>)Vtable(handle)[VtblSetFolder])(handle, item); }
                finally { Release(item); }
            }
            return dialog;
        }
        catch
        {
            dialog.Dispose();
            throw;
        }
    }

    internal uint Options
    {
        get
        {
            uint options = 0;
            _ = ((delegate* unmanaged[Stdcall]<nint, uint*, int>)Vtable(_dialog)[VtblGetOptions])(_dialog, &options);
            return options;
        }
    }

    /// <summary>The folder the dialog opens in, as a file system path.</summary>
    internal string? CurrentFolder
    {
        get
        {
            nint item = 0;
            if (((delegate* unmanaged[Stdcall]<nint, nint*, int>)Vtable(_dialog)[VtblGetFolder])(_dialog, &item) < 0 || item == nint.Zero)
                return null;
            try { return DisplayName(item); }
            finally { Release(item); }
        }
    }

    /// <summary>Shows the dialog and returns the chosen folder, or null when the person cancels.</summary>
    internal string? Show(nint owner)
    {
        if (((delegate* unmanaged[Stdcall]<nint, nint, int>)Vtable(_dialog)[VtblShow])(_dialog, owner) < 0)
            return null;
        nint item = 0;
        if (((delegate* unmanaged[Stdcall]<nint, nint*, int>)Vtable(_dialog)[VtblGetResult])(_dialog, &item) < 0 || item == nint.Zero)
            return null;
        try { return DisplayName(item); }
        finally { Release(item); }
    }

    private static string? DisplayName(nint item)
    {
        nint text = 0;
        if (((delegate* unmanaged[Stdcall]<nint, uint, nint*, int>)Vtable(item)[VtblShellItemGetDisplayName])(item, NativeMethods.SIGDN_FILESYSPATH, &text) < 0 || text == nint.Zero)
            return null;
        try { return new string((char*)text); }
        finally { NativeMethods.CoTaskMemFree(text); }
    }

    private static string? NearestExistingFolder(string folder)
    {
        var candidate = folder;
        while (!string.IsNullOrEmpty(candidate))
        {
            if (Directory.Exists(candidate))
                return candidate;
            candidate = Path.GetDirectoryName(candidate);
        }
        return null;
    }

    private static nint* Vtable(nint instance) => *(nint**)instance;

    private static void Release(nint instance)
        => ((delegate* unmanaged[Stdcall]<nint, uint>)Vtable(instance)[VtblRelease])(instance);

    public void Dispose()
    {
        if (_dialog == nint.Zero)
            return;
        Release(_dialog);
        _dialog = nint.Zero;
    }
}
