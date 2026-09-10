using System.Buffers.Binary;
using ThisIsMyPC.Interop.Win32.Security;

namespace ThisIsMyPC.Security.Tests;

[Trait("Category", "Security")]
public sealed class AuthenticodeCanonicalHashTests
{
    [Fact]
    public void ComputeSha256_IgnoresValidTerminalCertificateTable()
    {
        var unsignedBytes = CreateMinimalPe();
        var signedBytes = AddDummyCertificate(unsignedBytes);
        var directory = Path.Combine(Path.GetTempPath(), $"tipc-authenticode-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var unsignedPath = Path.Combine(directory, "unsigned.exe");
        var signedPath = Path.Combine(directory, "signed.exe");
        try
        {
            File.WriteAllBytes(unsignedPath, unsignedBytes);
            File.WriteAllBytes(signedPath, signedBytes);

            Assert.Equal(
                AuthenticodeCanonicalHash.ComputeSha256(unsignedPath),
                AuthenticodeCanonicalHash.ComputeSha256(signedPath));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ComputeSha256_RejectsDataAfterCertificateTable()
    {
        var bytes = AddDummyCertificate(CreateMinimalPe());
        Array.Resize(ref bytes, bytes.Length + 1);
        var path = Path.Combine(Path.GetTempPath(), $"tipc-overlay-{Guid.NewGuid():N}.exe");
        try
        {
            File.WriteAllBytes(path, bytes);

            Assert.Throws<InvalidDataException>(() => AuthenticodeCanonicalHash.ComputeSha256(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static byte[] CreateMinimalPe()
    {
        const int peOffset = 0x80;
        const int optionalHeaderOffset = peOffset + 24;
        const int optionalHeaderSize = 0xF0;
        const int sectionTableOffset = optionalHeaderOffset + optionalHeaderSize;
        var bytes = new byte[512];

        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(0), 0x5A4D);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(0x3C), peOffset);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(peOffset), 0x00004550);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(peOffset + 6), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(peOffset + 20), optionalHeaderSize);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(optionalHeaderOffset), 0x20B);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(optionalHeaderOffset + 108), 16);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(sectionTableOffset + 16), 80);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(sectionTableOffset + 20), 432);
        return bytes;
    }

    private static byte[] AddDummyCertificate(byte[] unsignedBytes)
    {
        const int peOffset = 0x80;
        const int optionalHeaderOffset = peOffset + 24;
        const int securityDirectoryOffset = optionalHeaderOffset + 112 + (4 * 8);
        const int certificateSize = 16;
        var bytes = new byte[unsignedBytes.Length + certificateSize];
        unsignedBytes.CopyTo(bytes, 0);

        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(securityDirectoryOffset), (uint)unsignedBytes.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(securityDirectoryOffset + 4), certificateSize);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(unsignedBytes.Length), certificateSize);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(unsignedBytes.Length + 4), 0x0200);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(unsignedBytes.Length + 6), 0x0002);
        bytes.AsSpan(unsignedBytes.Length + 8, 8).Fill(0x5A);
        return bytes;
    }
}
