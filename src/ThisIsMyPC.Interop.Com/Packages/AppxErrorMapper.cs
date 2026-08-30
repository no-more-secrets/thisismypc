using ThisIsMyPC.Core.Results;

namespace ThisIsMyPC.Interop.Com.Packages;

/// <summary>Pure HRESULT → ErrorCategory mapping for deployment-stack failures.</summary>
public static class AppxErrorMapper
{
#pragma warning disable CA1707 // Underscores are intentional; Win32/HRESULT constant naming
    public const int E_ACCESSDENIED = unchecked((int)0x80070005);
    public const int ERROR_NOT_SUPPORTED = unchecked((int)0x80070032);
    public const int ERROR_INSTALL_PACKAGE_NOT_FOUND = unchecked((int)0x80073CF1);
#pragma warning restore CA1707

    // The app always runs elevated: access-denied means the deployment stack refused the
    // operation (e.g. per-user removal of another user's package), never missing elevation.
    // <paramref name="subject"/> is a full phrase: "package 'Foo_1.0_x64__abc'" or "installed packages".
    // ERROR_NOT_SUPPORTED only arises from remove/deprovision, so the non-removable wording is safe.
    public static (ErrorCategory Category, string Message) Map(int hresult, string subject, string verb)
        => hresult switch
        {
            E_ACCESSDENIED => (
                ErrorCategory.AccessDenied,
                $"Cannot {verb} {subject}: the Windows deployment stack denied access."),
            ERROR_INSTALL_PACKAGE_NOT_FOUND => (
                ErrorCategory.NotFound,
                $"Cannot {verb} {subject}: no such package is installed."),
            ERROR_NOT_SUPPORTED => (
                ErrorCategory.ProtectedByPolicy,
                $"Cannot {verb} {subject}: Windows marks this package as non-removable."),
            _ => (
                ErrorCategory.ServiceUnavailable,
                $"Cannot {verb} {subject}: deployment error 0x{hresult:X8}."),
        };
}
