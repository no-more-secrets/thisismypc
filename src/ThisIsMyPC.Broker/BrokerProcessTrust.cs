using System.Diagnostics;
using ThisIsMyPC.Core;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Interop.Win32.Security;

namespace ThisIsMyPC.Broker;

internal static class BrokerProcessTrust
{
    internal static OperationResult<bool> VerifyUiProcess(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            var actual = Path.GetFullPath(process.MainModule?.FileName ?? string.Empty);
            var expected = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "ThisIsMyPC.App.exe"));
            if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
            {
                return OperationResult<bool>.Failure(
                    "The callback pipe is not owned by the installed ThisIsMyPC UI.",
                    ErrorCategory.AccessDenied);
            }
#if !DEBUG
            var trust = AuthenticodeVerifier.VerifyTrusted(expected, AppConstants.PublisherName, exactSignerName: true);
            if (!trust.IsSuccess)
                return trust;
#endif
            return OperationResult<bool>.Success(true);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException
                                       or System.ComponentModel.Win32Exception)
        {
            return OperationResult<bool>.Failure(
                $"The ThisIsMyPC UI process could not be verified: {ex.Message}",
                ErrorCategory.AccessDenied, ex);
        }
    }
}
