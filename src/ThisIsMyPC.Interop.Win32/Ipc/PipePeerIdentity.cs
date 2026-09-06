using System.IO.Pipes;
using System.Runtime.InteropServices;
using ThisIsMyPC.Core.Results;

namespace ThisIsMyPC.Interop.Win32.Ipc;

/// <summary>Reads the peer process identifier bound to a connected local pipe handle.</summary>
public static partial class PipePeerIdentity
{
    public static OperationResult<int> GetClientProcessId(NamedPipeServerStream pipe)
    {
        ArgumentNullException.ThrowIfNull(pipe);
        return GetNamedPipeClientProcessId(pipe.SafePipeHandle.DangerousGetHandle(), out var processId)
            ? OperationResult<int>.Success(checked((int)processId))
            : Failure("client");
    }

    public static OperationResult<int> GetServerProcessId(NamedPipeClientStream pipe)
    {
        ArgumentNullException.ThrowIfNull(pipe);
        return GetNamedPipeServerProcessId(pipe.SafePipeHandle.DangerousGetHandle(), out var processId)
            ? OperationResult<int>.Success(checked((int)processId))
            : Failure("server");
    }

    private static OperationResult<int> Failure(string peer) => OperationResult<int>.Failure(
        $"Could not identify the named pipe {peer} (Win32 {Marshal.GetLastPInvokeError()}).",
        ErrorCategory.AccessDenied);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetNamedPipeClientProcessId(nint pipe, out uint clientProcessId);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetNamedPipeServerProcessId(nint pipe, out uint serverProcessId);
}
