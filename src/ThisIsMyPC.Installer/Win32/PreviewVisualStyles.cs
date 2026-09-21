#if DEBUG
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace ThisIsMyPC.Installer.Win32;

/// <summary>dotnet exec uses the host manifest; apply this assembly's native control manifest explicitly.</summary>
internal sealed class PreviewVisualStyles : IDisposable
{
    private readonly nint _context;
    private readonly nuint _cookie;

    internal PreviewVisualStyles()
    {
        var source = Marshal.StringToHGlobalUni(typeof(PreviewVisualStyles).Assembly.Location);
        try
        {
            var context = new NativeMethods.ACTCTX
            {
                Size = (uint)Marshal.SizeOf<NativeMethods.ACTCTX>(), Source = source,
                Flags = 8, ResourceName = 1, // ACTCTX_FLAG_RESOURCE_NAME_VALID, executable manifest.
            };
            _context = NativeMethods.CreateActCtx(in context);
            if (_context == -1)
                throw new Win32Exception(Marshal.GetLastWin32Error());
            if (!NativeMethods.ActivateActCtx(_context, out _cookie))
            {
                var error = Marshal.GetLastWin32Error();
                NativeMethods.ReleaseActCtx(_context);
                throw new Win32Exception(error);
            }
        }
        finally { Marshal.FreeHGlobal(source); }
    }

    public void Dispose()
    {
        _ = NativeMethods.DeactivateActCtx(0, _cookie);
        NativeMethods.ReleaseActCtx(_context);
    }
}
#endif
