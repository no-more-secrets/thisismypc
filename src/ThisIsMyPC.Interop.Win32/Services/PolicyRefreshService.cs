using System.Runtime.InteropServices;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;

namespace ThisIsMyPC.Interop.Win32.Services;

/// <summary>userenv RefreshPolicyEx: the same trigger as "gpupdate /force" for the machine half.</summary>
public sealed partial class PolicyRefreshService : IPolicyRefreshService
{
    private const uint RP_FORCE = 1;

    public OperationResult<bool> RefreshMachinePolicy()
    {
        try
        {
            if (RefreshPolicyEx(true, RP_FORCE))
                return OperationResult<bool>.Success(true);
            var error = Marshal.GetLastWin32Error();
            return OperationResult<bool>.Failure($"Windows refused to refresh machine policy: Win32 error {error}.", ErrorCategory.ServiceUnavailable);
        }
        catch (Exception ex)
        {
            return OperationResult<bool>.Failure($"Unexpected error refreshing machine policy: {ex.Message}", ErrorCategory.ServiceUnavailable, ex);
        }
    }

    [LibraryImport("userenv.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool RefreshPolicyEx([MarshalAs(UnmanagedType.Bool)] bool machine, uint options);
}
