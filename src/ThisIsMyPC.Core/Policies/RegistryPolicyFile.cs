using System.Collections.Immutable;
using System.Text;

namespace ThisIsMyPC.Core.Policies;

/// <summary>
/// Bounded, lossless Registry.pol decoding and encoding. This class does not access Windows or apply policies.
/// Format: https://learn.microsoft.com/en-us/openspecs/windows_protocols/ms-gpreg/5c092c22-bf6b-4e7f-b180-b20743d368f5
/// </summary>
public static class RegistryPolicyFile
{
    /// <summary>Application limit shared by readers and writers.</summary>
    public const int MaximumFileBytes = 4 * 1024 * 1024;
    private const int MaximumTextCharacters = 32767;
    private static readonly Encoding Unicode = new UnicodeEncoding(false, false, true);

    /// <summary>Reads every instruction in order. Malformed documents throw rather than appear unconfigured.</summary>
    public static ImmutableArray<RegistryPolicyEntry> Parse(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.Length > MaximumFileBytes) throw new InvalidDataException("Local policy file exceeds limit.");
        using var input = new MemoryStream(bytes, writable: false);
        using var reader = new BinaryReader(input, Unicode);
        if (reader.ReadUInt32() != 0x67655250 || reader.ReadUInt32() != 1)
            throw new InvalidDataException("Invalid local policy header.");
        var entries = ImmutableArray.CreateBuilder<RegistryPolicyEntry>();
        while (input.Position < input.Length)
        {
            Require('[');
            var key = ReadText(); Require(';');
            var name = ReadText(); Require(';');
            var type = reader.ReadUInt32(); Require(';');
            var size = reader.ReadUInt32(); Require(';');
            if (size > input.Length - input.Position)
                throw new InvalidDataException("Invalid local policy data size.");
            var data = reader.ReadBytes(checked((int)size));
            Require(']');
            entries.Add(new(key, name, type, ImmutableArray.CreateRange(data)));
        }
        return entries.ToImmutable();

        void Require(char expected)
        {
            if (reader.ReadUInt16() != expected) throw new InvalidDataException("Invalid local policy delimiter.");
        }

        string ReadText()
        {
            var start = checked((int)input.Position);
            for (var i = 0; i <= MaximumTextCharacters; i++)
            {
                if (reader.ReadUInt16() != 0) continue;
                try { return Unicode.GetString(bytes, start, i * 2); }
                catch (DecoderFallbackException ex) { throw new InvalidDataException("Invalid local policy text.", ex); }
            }
            throw new InvalidDataException("Local policy field exceeds limit.");
        }
    }

    /// <summary>Encodes instructions without sorting, merging duplicates, interpreting directives, or writing a file.</summary>
    public static byte[] Serialize(IEnumerable<RegistryPolicyEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        using var output = new MemoryStream();
        using var writer = new BinaryWriter(output, Unicode, leaveOpen: true);
        writer.Write(0x67655250u);
        writer.Write(1u);
        foreach (var entry in entries)
        {
            ArgumentNullException.ThrowIfNull(entry);
            ValidateText(entry.KeyPath);
            ValidateText(entry.ValueName);
            if (entry.Data.IsDefault) throw new InvalidDataException("Local policy data is missing.");
            var recordLength = 20L + (entry.KeyPath.Length + entry.ValueName.Length + 2L) * 2 + entry.Data.Length;
            if (output.Length + recordLength > MaximumFileBytes)
                throw new InvalidDataException("Local policy file exceeds limit.");
            writer.Write((ushort)'[');
            WriteText(entry.KeyPath); writer.Write((ushort)';');
            WriteText(entry.ValueName); writer.Write((ushort)';');
            writer.Write(entry.ValueType); writer.Write((ushort)';');
            writer.Write((uint)entry.Data.Length); writer.Write((ushort)';');
            writer.Write(entry.Data.AsSpan());
            writer.Write((ushort)']');
        }
        return output.ToArray();

        static void ValidateText(string value)
        {
            ArgumentNullException.ThrowIfNull(value);
            if (value.Length > MaximumTextCharacters || value.Contains('\0', StringComparison.Ordinal))
                throw new InvalidDataException("Invalid local policy field.");
        }

        void WriteText(string value)
        {
            try { writer.Write(Unicode.GetBytes(value)); }
            catch (EncoderFallbackException ex) { throw new InvalidDataException("Invalid local policy text.", ex); }
            writer.Write((ushort)0);
        }
    }
}
