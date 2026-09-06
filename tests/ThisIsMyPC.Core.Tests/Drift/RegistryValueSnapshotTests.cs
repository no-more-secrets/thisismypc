using ThisIsMyPC.Core.Drift;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Core.Tests.Fakes;

namespace ThisIsMyPC.Core.Tests.Drift;

public sealed class RegistryValueSnapshotTests
{
    [Fact]
    public void Absent_is_not_present_and_matches_nothing()
    {
        var absent = RegistryValueSnapshot.Absent;

        Assert.False(absent.IsPresent);
        Assert.Null(absent.Value);
        Assert.False(absent.Matches(RegistryValueData.FromDWord(0)));
        Assert.False(absent.Matches(RegistryValueData.FromString(string.Empty)));
    }

    [Fact]
    public void Present_empty_string_is_present_and_distinct_from_absent()
    {
        var empty = RegistryValueSnapshot.Present(RegistryValueData.FromString(string.Empty));

        Assert.True(empty.IsPresent);
        Assert.NotEqual(RegistryValueSnapshot.Absent, empty);
        Assert.True(empty.Matches(RegistryValueData.FromString(string.Empty)));
    }

    [Fact]
    public void Present_dword_zero_is_present_and_distinct_from_absent()
    {
        var zero = RegistryValueSnapshot.Present(RegistryValueData.FromDWord(0));

        Assert.True(zero.IsPresent);
        Assert.NotEqual(RegistryValueSnapshot.Absent, zero);
        Assert.True(zero.Matches(RegistryValueData.FromDWord(0)));
        Assert.False(zero.Matches(RegistryValueData.FromDWord(1)));
    }

    [Fact]
    public void Present_snapshots_with_equal_data_are_equal()
    {
        Assert.Equal(
            RegistryValueSnapshot.Present(RegistryValueData.FromDWord(1)),
            RegistryValueSnapshot.Present(RegistryValueData.FromDWord(1)));
        Assert.NotEqual(
            RegistryValueSnapshot.Present(RegistryValueData.FromDWord(1)),
            RegistryValueSnapshot.Present(RegistryValueData.FromString("1")));
    }

    [Fact]
    public void Present_rejects_null()
    {
        Assert.Throws<ArgumentNullException>(() => RegistryValueSnapshot.Present(null!));
    }

    [Fact]
    public void Matches_never_crosses_kinds()
    {
        var text = RegistryValueSnapshot.Present(RegistryValueData.FromString("1"));
        var number = RegistryValueSnapshot.Present(RegistryValueData.FromDWord(1));

        Assert.False(text.Matches(RegistryValueData.FromDWord(1)));
        Assert.False(number.Matches(RegistryValueData.FromString("1")));
        Assert.False(number.Matches(RegistryValueData.FromQWord(1)));
    }

    [Theory]
    [InlineData("01", true)]
    [InlineData("+1", true)]
    [InlineData("1", true)]
    [InlineData(" 1", false)]
    [InlineData("1 ", false)]
    [InlineData("0x1", false)]
    [InlineData("1.0", false)]
    [InlineData("", false)]
    [InlineData("one", false)]
    public void Matches_canonicalizes_dword_text(string stored, bool expected)
    {
        var snapshot = RegistryValueSnapshot.Present(new RegistryValueData(RegistryValueDataKind.DWord, stored));

        Assert.Equal(expected, snapshot.Matches(RegistryValueData.FromDWord(1)));
    }

    [Fact]
    public void Matches_treats_string_case_and_whitespace_as_data()
    {
        var snapshot = RegistryValueSnapshot.Present(RegistryValueData.FromString("Value"));

        Assert.True(snapshot.Matches(RegistryValueData.FromString("Value")));
        Assert.False(snapshot.Matches(RegistryValueData.FromString("value")));
        Assert.False(snapshot.Matches(RegistryValueData.FromString("Value ")));
    }

    [Fact]
    public void Matches_compares_binary_by_bytes()
    {
        var snapshot = RegistryValueSnapshot.Present(RegistryValueData.FromBinary([1, 2, 3]));

        Assert.True(snapshot.Matches(RegistryValueData.FromBinary([1, 2, 3])));
        Assert.False(snapshot.Matches(RegistryValueData.FromBinary([1, 2])));
        Assert.False(snapshot.Matches(new RegistryValueData(RegistryValueDataKind.Binary, "not base64!")));
    }

    [Fact]
    public void TryCanonicalize_reports_invalid_numeric_data()
    {
        Assert.False(RegistryValueSnapshot.TryCanonicalize(new RegistryValueData(RegistryValueDataKind.DWord, "x"), out _));
        Assert.False(RegistryValueSnapshot.TryCanonicalize(new RegistryValueData(RegistryValueDataKind.QWord, "9" + new string('9', 30)), out _));
        Assert.True(RegistryValueSnapshot.TryCanonicalize(new RegistryValueData(RegistryValueDataKind.DWord, "007"), out var canonical));
        Assert.Equal(RegistryValueData.FromDWord(7), canonical);
    }

    [Theory]
    [InlineData(RegistryValueDataKind.String)]
    [InlineData(RegistryValueDataKind.ExpandString)]
    [InlineData(RegistryValueDataKind.Binary)]
    [InlineData(RegistryValueDataKind.DWord)]
    [InlineData(RegistryValueDataKind.QWord)]
    [InlineData(RegistryValueDataKind.MultiString)]
    public void TryCanonicalize_returns_false_for_null_data_of_every_kind(RegistryValueDataKind kind)
    {
        var value = new RegistryValueData(kind, null!);

        Assert.False(RegistryValueSnapshot.TryCanonicalize(value, out var canonical));
        Assert.Same(value, canonical);
    }

    [Theory]
    [InlineData(RegistryValueDataKind.String)]
    [InlineData(RegistryValueDataKind.ExpandString)]
    [InlineData(RegistryValueDataKind.MultiString)]
    public void TryCanonicalize_keeps_empty_text_kinds_as_valid_present_data(RegistryValueDataKind kind)
    {
        var value = new RegistryValueData(kind, string.Empty);

        Assert.True(RegistryValueSnapshot.TryCanonicalize(value, out var canonical));
        Assert.Equal(value, canonical);
        Assert.True(RegistryValueSnapshot.Present(value).Matches(new RegistryValueData(kind, string.Empty)));
    }

    [Fact]
    public void TryCanonicalize_rejects_unrecognized_kinds_without_throwing()
    {
        var value = new RegistryValueData((RegistryValueDataKind)99, "1");

        Assert.False(RegistryValueSnapshot.TryCanonicalize(value, out var canonical));
        Assert.Same(value, canonical);
    }

    [Fact]
    public void TryCanonicalize_rejects_null_binary_without_throwing()
    {
        Assert.False(RegistryValueSnapshot.TryCanonicalize(new RegistryValueData(RegistryValueDataKind.Binary, null!), out _));
        Assert.True(RegistryValueSnapshot.TryCanonicalize(RegistryValueData.FromBinary([]), out var empty));
        Assert.Equal(RegistryValueData.FromBinary([]), empty);
    }

    [Fact]
    public void Matches_returns_false_for_null_data_or_unrecognized_kinds()
    {
        var unknownKind = new RegistryValueData((RegistryValueDataKind)99, "1");
        var nullDword = new RegistryValueData(RegistryValueDataKind.DWord, null!);

        Assert.False(RegistryValueSnapshot.Present(unknownKind).Matches(unknownKind));
        Assert.False(RegistryValueSnapshot.Present(nullDword).Matches(RegistryValueData.FromDWord(0)));
        Assert.False(RegistryValueSnapshot.Present(RegistryValueData.FromDWord(0)).Matches(nullDword));
        Assert.False(RegistryValueSnapshot.Present(RegistryValueData.FromString("x")).Matches(new RegistryValueData(RegistryValueDataKind.String, null!)));
        Assert.False(RegistryValueSnapshot.Absent.Matches(unknownKind));
    }

    [Fact]
    public void FromRead_maps_not_found_to_absent()
    {
        IRegistryService registry = new FakeRegistryService();

        var result = RegistryValueSnapshot.FromRead(registry.ReadValue(@"HKCU\Software\Missing", "Value"));

        Assert.True(result.IsSuccess);
        Assert.Same(RegistryValueSnapshot.Absent, result.Value);
    }

    [Fact]
    public void FromRead_maps_typed_read_to_present()
    {
        var fake = new FakeRegistryService();
        fake.SetDWord(@"HKCU\Software\Test", "Value", 0);
        IRegistryService registry = fake;

        var result = RegistryValueSnapshot.FromRead(registry.ReadValue(@"HKCU\Software\Test", "Value"));

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.IsPresent);
        Assert.Equal(RegistryValueData.FromDWord(0), result.Value.Value);
    }

    [Fact]
    public void FromRead_maps_empty_string_read_to_present()
    {
        var fake = new FakeRegistryService();
        fake.SetString(@"HKCU\Software\Test", "Value", string.Empty);
        IRegistryService registry = fake;

        var result = RegistryValueSnapshot.FromRead(registry.ReadValue(@"HKCU\Software\Test", "Value"));

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.IsPresent);
        Assert.Equal(RegistryValueData.FromString(string.Empty), result.Value.Value);
    }

    [Fact]
    public void FromRead_passes_other_failures_through_instead_of_reporting_absent()
    {
        var denied = OperationResult<RegistryValueData>.Failure("Access denied", ErrorCategory.AccessDenied);

        var result = RegistryValueSnapshot.FromRead(denied);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCategory.AccessDenied, result.ErrorCategory);
        Assert.Null(result.Value);
    }
}
