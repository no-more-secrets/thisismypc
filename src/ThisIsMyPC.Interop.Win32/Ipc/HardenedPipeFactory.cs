using System.IO.Pipes;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using ThisIsMyPC.Core.Results;

namespace ThisIsMyPC.Interop.Win32.Ipc;

/// <summary>
/// Creates the service-side named pipe with the 28-1 hardening flags that
/// System.IO.Pipes cannot express together:
/// FILE_FLAG_FIRST_PIPE_INSTANCE (creation FAILS if the name already exists; a
/// squatter cannot sit behind our name), PIPE_REJECT_REMOTE_CLIENTS (local machine
/// only), and an SDDL DACL admitting only SYSTEM and Administrators.
/// </summary>
public static partial class HardenedPipeFactory
{
    // Protected DACL: full access for SYSTEM and Administrators, nobody else.
    private const string PipeSddl = "D:P(A;;GA;;;SY)(A;;GA;;;BA)";

    private const uint PIPE_ACCESS_DUPLEX = 0x00000003;
    private const uint FILE_FLAG_OVERLAPPED = 0x40000000;
    private const uint FILE_FLAG_FIRST_PIPE_INSTANCE = 0x00080000;
    private const uint PIPE_TYPE_BYTE = 0x00000000;
    private const uint PIPE_READMODE_BYTE = 0x00000000;
    private const uint PIPE_WAIT = 0x00000000;
    private const uint PIPE_REJECT_REMOTE_CLIENTS = 0x00000008;
    private const int ERROR_ACCESS_DENIED = 5;
    private const int ERROR_PIPE_BUSY = 231;

    /// <summary>
    /// Creates one hardened server instance for <paramref name="pipeName"/>
    /// (name only, no \\.\pipe\ prefix). AccessDenied error category signals the
    /// name is squatted (or a previous instance is still open).
    /// </summary>
    public static OperationResult<NamedPipeServerStream> Create(string pipeName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);

        nint securityDescriptor = 0;
        try
        {
            if (!NativePipe.ConvertStringSecurityDescriptorToSecurityDescriptorW(
                    PipeSddl, 1 /* SDDL_REVISION_1 */, out securityDescriptor, out _))
            {
                return OperationResult<NamedPipeServerStream>.Failure(
                    $"SDDL conversion failed (win32={Marshal.GetLastPInvokeError()})",
                    ErrorCategory.ServiceUnavailable);
            }

            var attributes = new NativePipe.SecurityAttributes
            {
                Length = (uint)Marshal.SizeOf<NativePipe.SecurityAttributes>(),
                SecurityDescriptor = securityDescriptor,
                InheritHandle = 0,
            };

            var handle = NativePipe.CreateNamedPipeW(
                $@"\\.\pipe\{pipeName}",
                PIPE_ACCESS_DUPLEX | FILE_FLAG_OVERLAPPED | FILE_FLAG_FIRST_PIPE_INSTANCE,
                PIPE_TYPE_BYTE | PIPE_READMODE_BYTE | PIPE_WAIT | PIPE_REJECT_REMOTE_CLIENTS,
                1 /* single instance */,
                65536, 65536,
                0 /* default timeout */,
                in attributes);

            if (handle.IsInvalid)
            {
                var error = Marshal.GetLastPInvokeError();
                handle.Dispose();
                return OperationResult<NamedPipeServerStream>.Failure(
                    error is ERROR_ACCESS_DENIED or ERROR_PIPE_BUSY
                        ? $"Pipe name '{pipeName}' already exists; refusing to serve behind a squatter (win32={error})"
                        : $"CreateNamedPipe failed (win32={error})",
                    error is ERROR_ACCESS_DENIED or ERROR_PIPE_BUSY
                        ? ErrorCategory.AccessDenied
                        : ErrorCategory.ServiceUnavailable);
            }

            return OperationResult<NamedPipeServerStream>.Success(
                new NamedPipeServerStream(PipeDirection.InOut, isAsync: true, isConnected: false, handle));
        }
        finally
        {
            if (securityDescriptor != 0)
                NativePipe.LocalFree(securityDescriptor);
        }
    }

    private static partial class NativePipe
    {
        [StructLayout(LayoutKind.Sequential)]
        internal struct SecurityAttributes
        {
            public uint Length;
            public nint SecurityDescriptor;
            public int InheritHandle;
        }

        [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
        internal static partial SafePipeHandle CreateNamedPipeW(
            string lpName,
            uint dwOpenMode,
            uint dwPipeMode,
            uint nMaxInstances,
            uint nOutBufferSize,
            uint nInBufferSize,
            uint nDefaultTimeOut,
            in SecurityAttributes lpSecurityAttributes);

        [LibraryImport("advapi32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool ConvertStringSecurityDescriptorToSecurityDescriptorW(
            string stringSecurityDescriptor,
            uint stringSdRevision,
            out nint securityDescriptor,
            out uint securityDescriptorSize);

        [LibraryImport("kernel32.dll")]
        internal static partial nint LocalFree(nint mem);
    }
}
