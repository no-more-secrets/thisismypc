using System.Runtime.InteropServices;
using ThisIsMyPC.Core;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Interop.Win32.Security;

namespace ThisIsMyPC.App.Services;

/// <summary>
/// Loads the complete packaged native dependency set before CIG closes image
/// loading. The release directory is admin-only, and every file must carry the
/// same trusted publisher as the application.
/// </summary>
internal static class TrustedNativeDependencyLoader
{
    internal static readonly string[] FileNames =
    [
        "av_libglesv2.dll",
        "e_sqlite3.dll",
        "libHarfBuzzSharp.dll",
        "libSkiaSharp.dll",
    ];

    private static readonly List<nint> Handles = [];

    internal static OperationResult<bool> VerifyAndLoad(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        foreach (var fileName in FileNames)
        {
            var path = Path.Combine(directory, fileName);
            var trust = AuthenticodeVerifier.VerifyTrusted(
                path,
                AppConstants.PublisherName,
                exactSignerName: true);
            if (!trust.IsSuccess)
            {
                return OperationResult<bool>.Failure(
                    trust.ErrorMessage ?? $"{fileName} failed signature verification.",
                    ErrorCategory.AccessDenied,
                    trust.Exception);
            }

            try
            {
                Handles.Add(NativeLibrary.Load(path));
            }
            catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException)
            {
                return OperationResult<bool>.Failure(
                    $"The trusted native dependency could not load: {fileName}.",
                    ErrorCategory.ServiceUnavailable,
                    ex);
            }
        }

        return OperationResult<bool>.Success(true);
    }
}
