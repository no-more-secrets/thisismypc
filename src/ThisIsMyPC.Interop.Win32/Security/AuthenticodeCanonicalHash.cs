using System.Buffers.Binary;
using System.Security.Cryptography;

namespace ThisIsMyPC.Interop.Win32.Security;

/// <summary>
/// Computes a stable PE hash after removing the Authenticode certificate table
/// and zeroing fields that Authenticode changes.
/// </summary>
public static class AuthenticodeCanonicalHash
{
    /// <summary>
    /// Computes the uppercase SHA-256 hash of the canonical unsigned PE image.
    /// </summary>
    /// <param name="filePath">Path to the PE image.</param>
    /// <returns>The uppercase hexadecimal SHA-256 value.</returns>
    public static string ComputeSha256(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        var bytes = File.ReadAllBytes(filePath);
        var canonicalLength = ValidateAndGetCanonicalLength(bytes, out var checksumOffset, out var securityDirectoryOffset);

        bytes.AsSpan(checksumOffset, sizeof(uint)).Clear();
        if (securityDirectoryOffset >= 0)
            bytes.AsSpan(securityDirectoryOffset, sizeof(ulong)).Clear();

        return Convert.ToHexString(SHA256.HashData(bytes.AsSpan(0, canonicalLength)));
    }

    private static int ValidateAndGetCanonicalLength(
        byte[] bytes,
        out int checksumOffset,
        out int securityDirectoryOffset)
    {
        var image = bytes.AsSpan();
        if (image.Length < 64 || image[0] != (byte)'M' || image[1] != (byte)'Z')
            throw new InvalidDataException("Input is not a DOS/PE image.");

        var peOffset = ReadInt32(image, 0x3c);
        if (peOffset < 0 || peOffset > image.Length - 24 ||
            image[peOffset] != (byte)'P' || image[peOffset + 1] != (byte)'E' ||
            image[peOffset + 2] != 0 || image[peOffset + 3] != 0)
        {
            throw new InvalidDataException("Input has an invalid PE header.");
        }

        var sectionCount = ReadUInt16(image, peOffset + 6);
        var optionalHeaderSize = ReadUInt16(image, peOffset + 20);
        var optionalHeaderOffset = checked(peOffset + 24);
        var optionalHeaderEnd = checked(optionalHeaderOffset + optionalHeaderSize);
        var sectionTableSize = checked(sectionCount * 40);
        var sectionTableEnd = checked(optionalHeaderEnd + sectionTableSize);
        if (optionalHeaderEnd > image.Length || sectionTableEnd > image.Length)
            throw new InvalidDataException("Input has a truncated PE optional header.");

        var magic = ReadUInt16(image, optionalHeaderOffset);
        int numberOfDirectoriesOffset;
        int directoriesOffset;
        switch (magic)
        {
            case 0x10b:
                numberOfDirectoriesOffset = optionalHeaderOffset + 92;
                directoriesOffset = optionalHeaderOffset + 96;
                break;
            case 0x20b:
                numberOfDirectoriesOffset = optionalHeaderOffset + 108;
                directoriesOffset = optionalHeaderOffset + 112;
                break;
            default:
                throw new InvalidDataException($"Unsupported PE optional-header magic 0x{magic:X4}.");
        }

        checksumOffset = optionalHeaderOffset + 64;
        if (checksumOffset > optionalHeaderEnd - sizeof(uint) ||
            numberOfDirectoriesOffset > optionalHeaderEnd - sizeof(uint))
        {
            throw new InvalidDataException("Input has an incomplete PE optional header.");
        }

        var directoryCount = ReadUInt32(image, numberOfDirectoriesOffset);
        securityDirectoryOffset = -1;
        uint certificateOffset = 0;
        uint certificateSize = 0;
        if (directoryCount > 4)
        {
            securityDirectoryOffset = directoriesOffset + (4 * 8);
            if (securityDirectoryOffset > optionalHeaderEnd - sizeof(ulong))
                throw new InvalidDataException("Input declares a Security directory outside its optional header.");

            certificateOffset = ReadUInt32(image, securityDirectoryOffset);
            certificateSize = ReadUInt32(image, securityDirectoryOffset + sizeof(uint));
        }

        if ((certificateOffset == 0) != (certificateSize == 0))
            throw new InvalidDataException("PE Security directory is incomplete.");
        if (certificateOffset == 0)
            return image.Length;

        ValidateCertificateTable(image, optionalHeaderEnd, sectionCount, certificateOffset, certificateSize);
        return checked((int)certificateOffset);
    }

    private static void ValidateCertificateTable(
        ReadOnlySpan<byte> image,
        int sectionTableOffset,
        ushort sectionCount,
        uint certificateOffset,
        uint certificateSize)
    {
        if ((certificateOffset & 7) != 0)
            throw new InvalidDataException("Authenticode certificate table is not aligned to eight bytes.");

        ulong lastSectionByte = (ulong)sectionTableOffset + ((ulong)sectionCount * 40);
        for (var index = 0; index < sectionCount; index++)
        {
            var sectionOffset = checked(sectionTableOffset + (index * 40));
            var rawSize = ReadUInt32(image, sectionOffset + 16);
            var rawOffset = ReadUInt32(image, sectionOffset + 20);
            var rawEnd = (ulong)rawOffset + rawSize;
            if (rawEnd > (ulong)image.Length)
                throw new InvalidDataException("PE section raw data extends beyond the file.");
            lastSectionByte = Math.Max(lastSectionByte, rawEnd);
        }

        var certificateEnd = (ulong)certificateOffset + certificateSize;
        if (certificateOffset < lastSectionByte || certificateEnd != (ulong)image.Length)
            throw new InvalidDataException("Authenticode certificate table must be terminal and have no overlay.");

        var cursor = (ulong)certificateOffset;
        while (cursor < certificateEnd)
        {
            if (certificateEnd - cursor < 8)
                throw new InvalidDataException("Authenticode certificate header is incomplete.");

            var cursorOffset = checked((int)cursor);
            var certificateLength = ReadUInt32(image, cursorOffset);
            if (certificateLength < 8)
                throw new InvalidDataException("Authenticode certificate length is invalid.");

            var revision = ReadUInt16(image, cursorOffset + 4);
            var certificateType = ReadUInt16(image, cursorOffset + 6);
            if (revision != 0x0200 || certificateType != 0x0002)
                throw new InvalidDataException("Authenticode certificate is not revision 2 PKCS SignedData.");

            var alignedLength = ((ulong)certificateLength + 7) & ~7UL;
            if (cursor + alignedLength > certificateEnd)
                throw new InvalidDataException("Authenticode certificate record is truncated.");
            cursor += alignedLength;
        }

        if (cursor != certificateEnd)
            throw new InvalidDataException("Authenticode certificates do not fill the Security directory.");
    }

    private static ushort ReadUInt16(ReadOnlySpan<byte> bytes, int offset)
    {
        if ((uint)offset > (uint)(bytes.Length - sizeof(ushort)))
            throw new InvalidDataException("PE field is outside the file.");
        return BinaryPrimitives.ReadUInt16LittleEndian(bytes[offset..]);
    }

    private static uint ReadUInt32(ReadOnlySpan<byte> bytes, int offset)
    {
        if ((uint)offset > (uint)(bytes.Length - sizeof(uint)))
            throw new InvalidDataException("PE field is outside the file.");
        return BinaryPrimitives.ReadUInt32LittleEndian(bytes[offset..]);
    }

    private static int ReadInt32(ReadOnlySpan<byte> bytes, int offset)
    {
        if ((uint)offset > (uint)(bytes.Length - sizeof(int)))
            throw new InvalidDataException("PE field is outside the file.");
        return BinaryPrimitives.ReadInt32LittleEndian(bytes[offset..]);
    }
}
