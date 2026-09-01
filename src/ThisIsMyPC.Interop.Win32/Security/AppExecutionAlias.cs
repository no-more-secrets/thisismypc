using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace ThisIsMyPC.Interop.Win32.Security;

/// <summary>
/// Resolves Windows app-execution aliases (the zero-byte reparse stubs under
/// %LOCALAPPDATA%\Microsoft\WindowsApps, IO_REPARSE_TAG_APPEXECLINK) to the
/// real packaged executable they launch. Signature verification must target
/// the real PE: the stub itself carries no signature.
/// </summary>
public static partial class AppExecutionAlias
{
    private const uint FSCTL_GET_REPARSE_POINT = 0x000900A8;
    private const uint IO_REPARSE_TAG_APPEXECLINK = 0x8000001B;
    private const uint FILE_FLAG_OPEN_REPARSE_POINT = 0x00200000;
    private const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;
    private const uint FILE_READ_ATTRIBUTES = 0x0080;
    private const uint FILE_SHARE_ALL = 0x00000007;
    private const uint OPEN_EXISTING = 3;

    /// <summary>
    /// The packaged executable behind <paramref name="path"/>, or the path
    /// itself when it is not an app-execution alias. Null when the alias is
    /// unreadable or its target cannot be determined (callers fail closed).
    /// </summary>
    public static string? ResolveTarget(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        FileAttributes attributes;
        try
        {
            attributes = File.GetAttributes(path);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }

        if (!attributes.HasFlag(FileAttributes.ReparsePoint))
            return path;

        using var handle = CreateFileW(
            path, FILE_READ_ATTRIBUTES, FILE_SHARE_ALL, 0, OPEN_EXISTING,
            FILE_FLAG_OPEN_REPARSE_POINT | FILE_FLAG_BACKUP_SEMANTICS, 0);
        if (handle.IsInvalid)
            return null;

        var buffer = new byte[16 * 1024];
        if (!DeviceIoControl(
                handle, FSCTL_GET_REPARSE_POINT, 0, 0,
                buffer, (uint)buffer.Length, out var bytesReturned, 0)
            || bytesReturned < 8)
        {
            return null;
        }

        var tag = BitConverter.ToUInt32(buffer, 0);
        if (tag != IO_REPARSE_TAG_APPEXECLINK)
            return null; // some other reparse kind; nothing safe to say about it

        // APPEXECLINK payload: version DWORD, then packed null-terminated UTF-16
        // strings (package id, entry point, executable path, app type). The
        // layout is undocumented, so pick the first string that is a rooted
        // path to an existing file rather than trusting an index.
        var dataLength = BitConverter.ToUInt16(buffer, 4);
        var payloadStart = 8 + 4;
        var payloadEnd = Math.Min(8 + dataLength, (int)bytesReturned);
        if (payloadEnd <= payloadStart)
            return null;

        foreach (var candidate in Encoding.Unicode
                     .GetString(buffer, payloadStart, payloadEnd - payloadStart)
                     .Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            if (Path.IsPathFullyQualified(candidate) && File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial SafeFileHandle CreateFileW(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        nint lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        nint hTemplateFile);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeviceIoControl(
        SafeFileHandle hDevice,
        uint dwIoControlCode,
        nint lpInBuffer,
        uint nInBufferSize,
        [Out] byte[] lpOutBuffer,
        uint nOutBufferSize,
        out uint lpBytesReturned,
        nint lpOverlapped);
}
