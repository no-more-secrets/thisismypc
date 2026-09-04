using System.Buffers.Binary;
using System.Reflection;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;

namespace ThisIsMyPC.Installer.Services;

/// <summary>
/// The MSI follows the native launcher in a length-delimited, hashed envelope.
/// Authenticode covers the launcher, payload, and footer as one file.
/// </summary>
public sealed class EmbeddedPackage
{
    public const string MsiFileName = "ThisIsMyPC-win.msi";
    private const string LicenseResourceName = "LICENSE";
    private const int FooterSize = 72;
    private static ReadOnlySpan<byte> FooterMagic => "TIPC-MSI-PAYLOAD"u8;
    private readonly string? executablePath;

    private static Assembly Assembly => typeof(EmbeddedPackage).Assembly;

    public EmbeddedPackage()
        : this(Environment.ProcessPath)
    {
    }

    internal EmbeddedPackage(string? executablePath)
    {
        this.executablePath = executablePath;
    }

    public bool IsPresent => TryReadDescriptor(out _);

    /// <summary>Writes the MSI into <paramref name="directory"/> and returns its path.</summary>
    public string ExtractTo(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        if (!TryReadDescriptor(out var descriptor) || executablePath is null)
            throw new InvalidOperationException("This build carries no installer package.");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, MsiFileName);
        using var source = File.Open(executablePath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        source.Position = descriptor.Offset;
        using var target = File.Create(path);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[1024 * 1024];
        var remaining = descriptor.Length;
        while (remaining > 0)
        {
            var read = source.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
            if (read <= 0)
                throw new InvalidDataException("The installer package ended before its declared length.");
            target.Write(buffer, 0, read);
            hash.AppendData(buffer, 0, read);
            remaining -= read;
        }
        if (!CryptographicOperations.FixedTimeEquals(hash.GetHashAndReset(), descriptor.Sha256))
            throw new InvalidDataException("The installer package hash is invalid.");
        return path;
    }

    private bool TryReadDescriptor(out PayloadDescriptor descriptor)
    {
        descriptor = default;
        if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
            return false;
        try
        {
            using var stream = File.Open(executablePath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            using var pe = new PEReader(stream, PEStreamOptions.LeaveOpen);
            var certificate = pe.PEHeaders.PEHeader?.CertificateTableDirectory ?? default;
            var contentEnd = certificate.Size == 0 ? stream.Length : certificate.RelativeVirtualAddress;
            if (contentEnd < FooterSize || contentEnd > stream.Length)
                return false;
            stream.Position = contentEnd - FooterSize;
            Span<byte> footer = stackalloc byte[FooterSize];
            stream.ReadExactly(footer);
            if (!footer[..FooterMagic.Length].SequenceEqual(FooterMagic)
                || BinaryPrimitives.ReadUInt32LittleEndian(footer[16..]) != 1
                || BinaryPrimitives.ReadUInt32LittleEndian(footer[20..]) != 0)
                return false;
            var offset = checked((long)BinaryPrimitives.ReadUInt64LittleEndian(footer[24..]));
            var length = checked((long)BinaryPrimitives.ReadUInt64LittleEndian(footer[32..]));
            var footerOffset = contentEnd - FooterSize;
            if (offset < 0 || length <= 0 || offset > footerOffset || length > footerOffset - offset)
                return false;
            var padding = footerOffset - offset - length;
            if (padding is < 0 or > 7)
                return false;
            stream.Position = offset + length;
            for (var index = 0; index < padding; index++)
            {
                if (stream.ReadByte() != 0)
                    return false;
            }
            descriptor = new PayloadDescriptor(offset, length, footer[40..72].ToArray());
            return true;
        }
        catch (Exception ex) when (ex is IOException or BadImageFormatException or OverflowException)
        {
            return false;
        }
    }

    private readonly record struct PayloadDescriptor(long Offset, long Length, byte[] Sha256);

    public static string LoadLicenseText()
    {
        using var stream = Assembly.GetManifestResourceStream(LicenseResourceName);
        if (stream is null)
            return "License text missing from this build.";
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
