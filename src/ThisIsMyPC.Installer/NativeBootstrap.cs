using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using ThisIsMyPC.Installer.Services;

namespace ThisIsMyPC.Installer;

/// <summary>
/// The download is one file. NativeAOT folds the managed code into the exe
/// but not SkiaSharp's and HarfBuzz's native DLLs, so the build embeds those
/// as resources (see the EmbedNativeLibraries target in the csproj) and this
/// class writes them into the hardened data directory and points the runtime
/// at them by absolute path. Dev builds carry no such resources and load the
/// DLLs from beside the exe as usual.
/// </summary>
internal static partial class NativeBootstrap
{
    private static readonly string[] Libraries = ["libSkiaSharp", "libHarfBuzzSharp"];
    private static string? _directory;

    public static void Prepare()
    {
        var assembly = typeof(NativeBootstrap).Assembly;
        if (assembly.GetManifestResourceInfo(ResourceName(Libraries[0])) is null)
            return;

        var version = assembly.GetName().Version?.ToString() ?? "0";
        var dir = HardenedDataDirectory.EnsureChildDirectory("installer", "native-" + version);

        foreach (var name in Libraries)
        {
            using var source = assembly.GetManifestResourceStream(ResourceName(name))
                ?? throw new InvalidOperationException($"{name}.dll is missing from this build.");
            var target = Path.Combine(dir, name + ".dll");
            EnsureResourceFile(source, target);
        }

        _directory = dir;
        NativeLibrary.SetDllImportResolver(typeof(SkiaSharp.SKObject).Assembly, Resolve);
        NativeLibrary.SetDllImportResolver(typeof(HarfBuzzSharp.Blob).Assembly, Resolve);
    }

    /// <summary>Shows a plain Windows message box; used before Avalonia exists.</summary>
    public static void ReportFatal(string message)
    {
        _ = MessageBoxW(IntPtr.Zero, message, "ThisIsMyPC installer", 0x10 /* MB_ICONERROR */);
    }

    private static string ResourceName(string library) => "native/" + library + ".dll";

    internal static void EnsureResourceFile(Stream source, string target)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(target);
        if (!source.CanSeek)
            throw new InvalidOperationException("The embedded native library stream must support seeking.");

        source.Position = 0;
        var expectedHash = SHA256.HashData(source);
        var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            source.Position = 0;
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                source.CopyTo(output);
                output.Flush(flushToDisk: true);
            }

            using (var written = File.Open(temporary, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(written), expectedHash))
                    throw new InvalidDataException("The extracted native library hash is invalid.");
            }

            File.Move(temporary, target, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    private static IntPtr Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (_directory is null)
            return IntPtr.Zero;

        foreach (var name in Libraries)
        {
            if (!libraryName.Equals(name, StringComparison.OrdinalIgnoreCase)
                && !libraryName.Equals(name + ".dll", StringComparison.OrdinalIgnoreCase))
                continue;
            var path = Path.Combine(_directory, name + ".dll");
            if (File.Exists(path))
                return NativeLibrary.Load(path);
        }

        return IntPtr.Zero;
    }

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);
}
