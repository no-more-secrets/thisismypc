using System.Runtime.InteropServices;
using ThisIsMyPC.Core;
using ThisIsMyPC.Core.Drift.Baseline;
using ThisIsMyPC.Interop.Win32.Drift.Consent;

namespace ThisIsMyPC.Interop.Win32.Drift.Baseline;

/// <summary>
/// Single-owner baseline bytes in the existing protected data directory. Uses the consent
/// handle checks and protected file writer. Does not create directories or repair unsafe data.
/// The owning SingleOwnerBaselineStore supplies validation and the caller's mutation lease.
/// </summary>
public sealed class MachineBaselineStorage(string? directory = null) : ITrustedBaselineStorage
{
    public const string FileName = "owner-mode-baseline.json";
    private readonly string _directory = directory ?? AppConstants.DataDirectoryPath;

    public byte[]? Read(int maximumBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBytes);
        using var scope = new TrustedConsentDirectory(_directory);
        return ReadUnderScope(scope, maximumBytes);
    }

    private static byte[]? ReadUnderScope(TrustedConsentDirectory scope, int maximumBytes)
    {
        using var handle = NativeConsentFiles.CreateFileW(Path.Combine(scope.Path, FileName),
            NativeConsentFiles.GenericRead | NativeConsentFiles.ReadControl, 1, 0, 3,
            NativeConsentFiles.OpenReparsePoint, 0);
        if (handle.IsInvalid)
        {
            var error = Marshal.GetLastPInvokeError();
            if (error == 2) return null;
            throw new IOException($"Baseline could not be opened (Win32 {error}).");
        }
        var info = TrustedConsentDirectory.VerifyShape(handle, directory: false);
        TrustedConsentDirectory.VerifySecurity(handle, directory: false);
        if (info.FileSizeHigh != 0 || info.FileSizeLow > maximumBytes)
            throw new InvalidDataException("Baseline document exceeds its size limit.");
        using var stream = new FileStream(handle, FileAccess.Read);
        var bytes = new byte[checked((int)info.FileSizeLow)];
        stream.ReadExactly(bytes);
        return bytes;
    }

    public void ReplaceDurably(ReadOnlyMemory<byte> document)
    {
        if (document.Length == 0 || document.Length > SingleOwnerBaselineStore.MaximumBytes)
            throw new InvalidDataException("Baseline document size is invalid.");
        using var scope = new TrustedConsentDirectory(_directory);
        // Refuse an unsafe destination instead of replacing its evidence with a trusted file.
        _ = ReadUnderScope(scope, SingleOwnerBaselineStore.MaximumBytes);
        var temp = Path.Combine(scope.Path, $".{FileName}.{Guid.NewGuid():N}.tmp");
        try
        {
            MachineConsentStore.WriteNewProtected(temp, document.ToArray());
            if (!NativeConsentFiles.MoveFileExW(temp, Path.Combine(scope.Path, FileName), 0x1 | 0x8))
                throw new IOException($"Baseline replacement failed (Win32 {Marshal.GetLastPInvokeError()}).");
            var loaded = ReadUnderScope(scope, SingleOwnerBaselineStore.MaximumBytes);
            if (loaded is null || !document.Span.SequenceEqual(loaded))
                throw new IOException("Baseline replacement could not be confirmed.");
        }
        finally
        {
            // Only this operation's unique temporary file, inside the held trusted directory.
            if (File.Exists(temp)) File.Delete(temp);
        }
    }
}
