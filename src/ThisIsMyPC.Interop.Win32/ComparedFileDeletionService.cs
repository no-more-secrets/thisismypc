using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;

namespace ThisIsMyPC.Interop.Win32;

/// <summary>Compares and marks the same exclusively opened file for deletion, without elevation.</summary>
public sealed partial class ComparedFileDeletionService : IComparedFileDeletionService
{
    public OperationResult<bool> DeleteIfMatches(string path, byte[] expected)
    {
        ArgumentNullException.ThrowIfNull(expected);
        try
        {
            // GENERIC_READ | DELETE, no sharing, OPEN_EXISTING, FILE_FLAG_OPEN_REPARSE_POINT.
            using var handle = CreateFileW(path, 0x80010000, 0, 0, 3, 0x00200000, 0);
            if (handle.IsInvalid) throw new Win32Exception(Marshal.GetLastPInvokeError());
            if (!GetFileInformationByHandleEx(handle, 9, out var attributes, 8))
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            if ((attributes.Attributes & 0x410) != 0)
                throw new IOException("Linked files and directories cannot be deleted here.");
            if (RandomAccess.GetLength(handle) != expected.Length)
                throw new IOException("The profile changed before undo could remove it.");
            var actual = new byte[expected.Length];
            var offset = 0;
            while (offset < actual.Length)
            {
                var count = RandomAccess.Read(handle, actual.AsSpan(offset), offset);
                if (count == 0) throw new EndOfStreamException("The profile could not be read completely.");
                offset += count;
            }
            if (!actual.AsSpan().SequenceEqual(expected))
                throw new IOException("The profile changed before undo could remove it.");
            // FILE_DISPOSITION_INFO contains one BOOLEAN. Deletion completes when this handle closes.
            byte deleteFile = 1;
            if (!SetFileInformationByHandle(handle, 4, ref deleteFile, 1))
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            return OperationResult<bool>.Success(true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception or ArgumentException)
        {
            return OperationResult<bool>.Failure(ex.Message,
                ex is UnauthorizedAccessException || ex is Win32Exception { NativeErrorCode: 5 }
                    ? ErrorCategory.AccessDenied : ErrorCategory.ServiceUnavailable, ex);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileAttributeTagInfo
    {
        public uint Attributes;
        public uint ReparseTag;
    }

    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial SafeFileHandle CreateFileW(string path, uint access, uint sharing,
        nint securityAttributes, uint disposition, uint flags, nint template);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetFileInformationByHandleEx(SafeFileHandle handle, int informationClass,
        out FileAttributeTagInfo information, uint size);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetFileInformationByHandle(SafeFileHandle handle, int informationClass,
        ref byte information, uint size);
}
