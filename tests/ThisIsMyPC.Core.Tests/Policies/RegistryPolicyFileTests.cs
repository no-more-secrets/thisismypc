using System.Collections.Immutable;
using ThisIsMyPC.Core.Policies;

namespace ThisIsMyPC.Core.Tests.Policies;

public sealed class RegistryPolicyFileTests
{
    [Fact]
    public void KnownDwordFixtureDecodesAndEncodesExactly()
    {
        // PReg v1, [K\0;V\0;REG_DWORD;4;1]. Independent wire-format fixture.
        var bytes = Convert.FromHexString("50526567010000005B004B0000003B00560000003B00040000003B00040000003B00010000005D00");
        var entry = Assert.Single(RegistryPolicyFile.Parse(bytes));
        Assert.Equal("K", entry.KeyPath);
        Assert.Equal("V", entry.ValueName);
        Assert.Equal(4u, entry.ValueType);
        Assert.Equal(new byte[] { 1, 0, 0, 0 }, entry.Data);
        Assert.False(entry.IsDirective);
        Assert.Equal(bytes, RegistryPolicyFile.Serialize([entry]));
    }

    [Fact]
    public void EmptyDocumentIsDifferentFromMissingOrTruncatedFile()
    {
        var bytes = Convert.FromHexString("5052656701000000");
        Assert.Empty(RegistryPolicyFile.Parse(bytes));
        Assert.Equal(bytes, RegistryPolicyFile.Serialize([]));
        for (var length = 0; length < bytes.Length; length++)
            Assert.Throws<EndOfStreamException>(() => RegistryPolicyFile.Parse(bytes[..length]));
    }

    [Fact]
    public void DuplicatesDirectivesUnknownTypesAndBinaryDelimitersRetainOrderAndBytes()
    {
        var entries = new RegistryPolicyEntry[]
        {
            new(@"Software\Policies\Example", "Choice", 4, [1, 0, 0, 0]),
            new(@"Software\Policies\Example", "**Del.Choice", 1, [0, 0]),
            new(@"Software\Policies\Example", "Choice", 4, [0, 0, 0, 0]),
            new(@"Software\例", "未知", uint.MaxValue, [0, 0, 59, 0, 93, 0, 91, 0, 255]),
            new(@"Software\Policies\Example", "", 0, []),
        };
        var bytes = RegistryPolicyFile.Serialize(entries);
        var parsed = RegistryPolicyFile.Parse(bytes);
        Assert.Equal(entries.Length, parsed.Length);
        for (var i = 0; i < entries.Length; i++)
        {
            Assert.Equal(entries[i].KeyPath, parsed[i].KeyPath);
            Assert.Equal(entries[i].ValueName, parsed[i].ValueName);
            Assert.Equal(entries[i].ValueType, parsed[i].ValueType);
            Assert.Equal<byte>(entries[i].Data, parsed[i].Data);
        }
        Assert.True(parsed[1].IsDirective);
        Assert.Equal(bytes, RegistryPolicyFile.Serialize(parsed));
    }

    [Theory]
    [InlineData("**DeleteValues")]
    [InlineData("**DelVals")]
    [InlineData("**DeleteKeys")]
    [InlineData("**SecureKey")]
    public void SpecialInstructionsAreExplicit(string name)
    {
        var entry = new RegistryPolicyEntry("Key", name, 1, [0, 0]);
        Assert.True(Assert.Single(RegistryPolicyFile.Parse(RegistryPolicyFile.Serialize([entry]))).IsDirective);
    }

    [Fact]
    public void EveryTruncatedRecordFailsInsteadOfReturningPartialPolicy()
    {
        var bytes = RegistryPolicyFile.Serialize([new("Key", "Value", 3, [0, 59, 93, 0, 0])]);
        for (var length = 9; length < bytes.Length; length++)
        {
            var error = Record.Exception(() => RegistryPolicyFile.Parse(bytes[..length]));
            Assert.True(error is IOException or InvalidDataException, $"Accepted truncation at {length}.");
        }
        Assert.Throws<InvalidDataException>(() => RegistryPolicyFile.Parse([.. bytes, 0, 0]));
    }

    [Fact]
    public void WrongSignatureVersionDelimitersAndOversizedDataFail()
    {
        var bytes = Convert.FromHexString("50526567010000005B004B0000003B00560000003B00040000003B00040000003B00010000005D00");
        foreach (var offset in new[] { 0, 4, 8, 14, 20, 26, 32, 38 })
        {
            var bad = (byte[])bytes.Clone();
            bad[offset] = 255;
            Assert.Throws<InvalidDataException>(() => RegistryPolicyFile.Parse(bad));
        }
        var badSize = (byte[])bytes.Clone();
        Array.Fill(badSize, (byte)255, 28, 4);
        Assert.Throws<InvalidDataException>(() => RegistryPolicyFile.Parse(badSize));
    }

    [Fact]
    public void TextAndDocumentLimitsApplyBeforeAllocationOrSerialization()
    {
        Assert.Throws<InvalidDataException>(() => RegistryPolicyFile.Parse(new byte[RegistryPolicyFile.MaximumFileBytes + 1]));
        Assert.Throws<InvalidDataException>(() => RegistryPolicyFile.Serialize([new(new string('K', 32768), "V", 0, [])]));
        Assert.Throws<InvalidDataException>(() => RegistryPolicyFile.Serialize([new("K\0extra", "V", 0, [])]));
        Assert.Throws<InvalidDataException>(() => RegistryPolicyFile.Serialize([new("K", "V", 0, default)]));
        Assert.Throws<InvalidDataException>(() => RegistryPolicyFile.Serialize([new("K", "V", 0,
            ImmutableArray.CreateRange(new byte[RegistryPolicyFile.MaximumFileBytes]))]));
        var valid = RegistryPolicyFile.Serialize([new(new string('K', 32767), "V", 0, [])]);
        Assert.Equal(32767, Assert.Single(RegistryPolicyFile.Parse(valid)).KeyPath.Length);
    }

    [Fact]
    public void InvalidUnicodeIsRejectedWithoutReplacingCharacters()
    {
        Assert.Throws<InvalidDataException>(() => RegistryPolicyFile.Serialize([new("\uD800", "V", 0, [])]));
        var bytes = RegistryPolicyFile.Serialize([new("K", "V", 0, [])]);
        bytes[10] = 0;
        bytes[11] = 0xD8;
        Assert.Throws<InvalidDataException>(() => RegistryPolicyFile.Parse(bytes));
    }
}
