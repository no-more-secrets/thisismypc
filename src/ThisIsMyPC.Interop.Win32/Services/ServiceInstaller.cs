using System.Runtime.InteropServices;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;
using static ThisIsMyPC.Interop.Win32.Services.NativeServiceControl;

namespace ThisIsMyPC.Interop.Win32.Services;

/// <summary>
/// Registers/unregisters the Owner Mode service with the SCM (28-2). Installation is
/// SERVICE_AUTO_START own-process running as LocalSystem (lpServiceStartName null).
/// Install is idempotent: an existing registration is treated as success so
/// enable-after-crash never dead-ends.
/// </summary>
public sealed class ServiceInstaller : IServiceInstaller
{
    public OperationResult<bool> Install(string serviceName, string displayName, string description, string binaryPath)
    {
        var hScm = OpenSCManagerW(null, null, SC_MANAGER_CONNECT | SC_MANAGER_CREATE_SERVICE);
        if (hScm == 0)
            return Fail("connect to the Service Control Manager", Marshal.GetLastWin32Error());
        try
        {
            var hService = CreateServiceW(
                hScm, serviceName, displayName,
                SERVICE_QUERY_STATUS | SERVICE_CHANGE_CONFIG | SERVICE_START,
                SERVICE_WIN32_OWN_PROCESS,
                SERVICE_AUTO_START,
                SERVICE_ERROR_NORMAL,
                QuoteIfNeeded(binaryPath),
                null, 0, null, null, null);

            if (hService == 0)
            {
                var error = Marshal.GetLastWin32Error();
                if (error == ERROR_SERVICE_EXISTS)
                    return OperationResult<bool>.Success(true);
                if (error == ERROR_SERVICE_MARKED_FOR_DELETE)
                    return OperationResult<bool>.Failure(
                        $"Service '{serviceName}' is marked for deletion; a reboot is required before it can be reinstalled.",
                        ErrorCategory.ServiceUnavailable);
                return Fail($"create service '{serviceName}'", error);
            }

            try
            {
                SetDescription(hService, description);
                return OperationResult<bool>.Success(true);
            }
            finally
            {
                CloseServiceHandle(hService);
            }
        }
        finally
        {
            CloseServiceHandle(hScm);
        }
    }

    public OperationResult<bool> Uninstall(string serviceName)
    {
        var hScm = OpenSCManagerW(null, null, SC_MANAGER_CONNECT);
        if (hScm == 0)
            return Fail("connect to the Service Control Manager", Marshal.GetLastWin32Error());
        try
        {
            var hService = OpenServiceW(hScm, serviceName, DELETE);
            if (hService == 0)
            {
                var error = Marshal.GetLastWin32Error();
                return error == ERROR_SERVICE_DOES_NOT_EXIST
                    ? OperationResult<bool>.Success(true) // already gone
                    : Fail($"open service '{serviceName}' for deletion", error);
            }
            try
            {
                return DeleteService(hService)
                    ? OperationResult<bool>.Success(true)
                    : Fail($"delete service '{serviceName}'", Marshal.GetLastWin32Error());
            }
            finally
            {
                CloseServiceHandle(hService);
            }
        }
        finally
        {
            CloseServiceHandle(hScm);
        }
    }

    public OperationResult<bool> IsInstalled(string serviceName)
    {
        var hScm = OpenSCManagerW(null, null, SC_MANAGER_CONNECT);
        if (hScm == 0)
            return Fail("connect to the Service Control Manager", Marshal.GetLastWin32Error());
        try
        {
            var hService = OpenServiceW(hScm, serviceName, SERVICE_QUERY_STATUS);
            if (hService != 0)
            {
                CloseServiceHandle(hService);
                return OperationResult<bool>.Success(true);
            }
            var error = Marshal.GetLastWin32Error();
            return error == ERROR_SERVICE_DOES_NOT_EXIST
                ? OperationResult<bool>.Success(false)
                : Fail($"query service '{serviceName}'", error);
        }
        finally
        {
            CloseServiceHandle(hScm);
        }
    }

    private static void SetDescription(nint hService, string description)
    {
        // Best-effort; a missing description never fails an install.
        var ptr = Marshal.StringToHGlobalUni(description);
        try
        {
            var info = new ServiceDescription { lpDescription = ptr };
            ChangeServiceConfig2Description(hService, SERVICE_CONFIG_DESCRIPTION, ref info);
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    private static string QuoteIfNeeded(string binaryPath) =>
        binaryPath.Contains(' ', StringComparison.Ordinal) && !binaryPath.StartsWith('"')
            ? $"\"{binaryPath}\""
            : binaryPath;

    private static OperationResult<bool> Fail(string action, int win32Error) =>
        OperationResult<bool>.Failure(
            $"Cannot {action}: Win32 error {win32Error}.",
            win32Error == ERROR_ACCESS_DENIED ? ErrorCategory.AccessDenied : ErrorCategory.ServiceUnavailable);
}
