using System.Runtime.InteropServices;
using ThisIsMyPC.Interop.Win32.Security;

namespace ThisIsMyPC.App.Services;

/// <summary>
/// Optional vendor libraries the Lighting tab drives hardware through, mapped
/// before Code Integrity Guard closes image loading. Each one is optional
/// (its hardware may be absent), lives in System32 only, and must carry a
/// valid Authenticode signature from the named vendor. A library that fails
/// any check is left unmapped and Lighting reports that device family as
/// unavailable; the app never stops over it. After CIG the loader hands the
/// already-mapped image back to whoever asks for it by path, so the Lighting
/// backend needs no special case.
/// </summary>
internal static class VendorNativeDependencyLoader
{
    /// <summary>NVIDIA's NvAPI: GPU I2C for the lighting controllers on ASUS graphics cards.</summary>
    internal static readonly VendorNativeDependency NvApi = new("nvapi64.dll", "NVIDIA Corporation");

    internal static IReadOnlyList<VendorNativeDependency> Optional { get; } = [NvApi];

    private static readonly List<nint> Handles = [];
    private static readonly List<string> Outcomes = [];

    /// <summary>One line per optional library saying whether it was mapped, for the startup log (logging is not up when this runs).</summary>
    internal static IReadOnlyList<string> Report => Outcomes;

    internal static void PreloadOptional()
    {
        foreach (var dependency in Optional)
        {
            var path = Path.Combine(Environment.SystemDirectory, dependency.FileName);
            if (!File.Exists(path))
            {
                Outcomes.Add($"{dependency.FileName}: not installed; that hardware family is unavailable.");
                continue;
            }

            var trust = AuthenticodeVerifier.VerifyTrusted(path, dependency.SignerSubjectFragment);
            if (!trust.IsSuccess)
            {
                Outcomes.Add($"{dependency.FileName}: not mapped, {trust.ErrorMessage}");
                continue;
            }

            try
            {
                Handles.Add(NativeLibrary.Load(path));
                Outcomes.Add($"{dependency.FileName}: mapped before CIG from {path}.");
            }
            catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException or IOException)
            {
                Outcomes.Add($"{dependency.FileName}: not mapped, {ex.GetType().Name}: {ex.Message}");
            }
        }
    }
}

/// <param name="SignerSubjectFragment">Text the signing certificate's subject must contain (the vendor's legal name).</param>
internal readonly record struct VendorNativeDependency(string FileName, string SignerSubjectFragment);
