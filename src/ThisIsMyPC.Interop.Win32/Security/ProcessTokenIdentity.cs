using System.Diagnostics;
using System.Runtime.InteropServices;
using ThisIsMyPC.Core.Results;

namespace ThisIsMyPC.Interop.Win32.Security;

public static unsafe class ProcessTokenIdentity
{
    public static OperationResult<bool> IsElevated(int processId)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(processId);
        nint token = 0;
        try
        {
            using var process = Process.GetProcessById(processId);
            if (!NativeProcessToken.OpenProcessToken(process.Handle, NativeProcessToken.TOKEN_QUERY, out token))
                return OperationResult<bool>.Failure(
                    $"OpenProcessToken failed while checking elevation (Win32 {Marshal.GetLastPInvokeError()}).",
                    ErrorCategory.AccessDenied);
            var elevated = 0;
            return NativeProcessToken.GetTokenInformation(
                    token, NativeProcessToken.TokenElevation, &elevated, sizeof(int), out _)
                ? OperationResult<bool>.Success(elevated != 0)
                : OperationResult<bool>.Failure(
                    $"GetTokenInformation failed while checking elevation (Win32 {Marshal.GetLastPInvokeError()}).",
                    ErrorCategory.AccessDenied);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException
                                       or System.ComponentModel.Win32Exception)
        {
            return OperationResult<bool>.Failure(
                "The process elevation could not be inspected: " + ex.Message,
                ErrorCategory.AccessDenied, ex);
        }
        finally
        {
            if (token != 0)
                NativeProcessToken.CloseHandle(token);
        }
    }

    public static OperationResult<string> GetUserSid(int processId)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(processId);
        nint token = 0;
        try
        {
            using var process = Process.GetProcessById(processId);
            if (!NativeProcessToken.OpenProcessToken(process.Handle, NativeProcessToken.TOKEN_QUERY, out token))
                return Failure("OpenProcessToken");

            var buffer = stackalloc byte[256];
            if (!NativeProcessToken.GetTokenInformation(
                    token, NativeProcessToken.TokenUser, buffer, 256, out _))
            {
                return Failure("GetTokenInformation");
            }

            var user = (NativeProcessToken.TOKEN_USER*)buffer;
            if (!NativeProcessToken.ConvertSidToStringSidW(user->Sid, out var sidPointer) || sidPointer == 0)
                return Failure("ConvertSidToStringSid");
            try
            {
                var sid = Marshal.PtrToStringUni(sidPointer);
                return string.IsNullOrWhiteSpace(sid)
                    ? OperationResult<string>.Failure("The process token has no user SID.", ErrorCategory.AccessDenied)
                    : OperationResult<string>.Success(sid);
            }
            finally
            {
                NativeProcessToken.LocalFree(sidPointer);
            }
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException
                                       or System.ComponentModel.Win32Exception)
        {
            return OperationResult<string>.Failure(
                "The process token could not be inspected: " + ex.Message,
                ErrorCategory.AccessDenied, ex);
        }
        finally
        {
            if (token != 0)
                NativeProcessToken.CloseHandle(token);
        }
    }

    private static OperationResult<string> Failure(string operation) => OperationResult<string>.Failure(
        $"{operation} failed while inspecting the UI token (Win32 {Marshal.GetLastPInvokeError()}).",
        ErrorCategory.AccessDenied);
}
