using System.Collections.Immutable;
using System.Text;
using ThisIsMyPC.Core.Policies;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;

namespace ThisIsMyPC.Core.Tests.Policies;

public sealed class PolicyStateReaderTests
{
    private static readonly PolicyDefinition Definition = new("Test", "Test", "Module", "Section", @"Software\Policies\Test", "Setting",
        [PolicyScope.User], 1, 0, "Test.admx", "Test", "https://example.invalid");

    [Theory]
    [InlineData(1, PolicyState.Enabled)]
    [InlineData(0, PolicyState.Disabled)]
    [InlineData(7, PolicyState.OtherValue)]
    public void DwordStatesHavePolicySemantics(int value, PolicyState expected)
    {
        var observation = Read([Source(Entry(value))], Number(value));
        Assert.Equal(expected, observation.SavedState);
        Assert.Equal(expected, observation.RegistryState);
    }

    [Fact]
    public void MissingPreferencesAreNeverProofThatAFeatureIsEnabled()
    {
        var observation = Read([Source()], Missing());
        Assert.Equal(PolicyState.NotConfigured, observation.SavedState);
        Assert.Equal(PolicyState.NotConfigured, observation.RegistryState);
        Assert.Equal(PolicyComparison.NoSetting, observation.Comparison);
    }

    [Theory]
    [InlineData(PolicyFileStatus.Missing, PolicyState.NotConfigured, PolicyComparison.RegistryOnly)]
    [InlineData(PolicyFileStatus.Unreadable, PolicyState.Unknown, PolicyComparison.Unknown)]
    public void MissingAndUnreadableStoresRemainDifferent(PolicyFileStatus status, PolicyState state, PolicyComparison comparison)
    {
        var observation = Read([new("User", PolicyScope.User, status, [], "Read failed")], Number(1));
        Assert.Equal(state, observation.SavedState);
        Assert.Equal(comparison, observation.Comparison);
    }

    [Fact]
    public void ReadFailuresAndWrongTypesDoNotBecomeNotConfigured()
    {
        var denied = Read([Source()], OperationResult<RegistryValueData>.Failure("Denied", ErrorCategory.AccessDenied));
        Assert.Equal(PolicyState.Unknown, denied.RegistryState);
        Assert.Equal("Denied", denied.RegistryError);
        var wrongType = Read([Source(Entry(1) with { ValueType = 1 })],
            OperationResult<RegistryValueData>.Success(RegistryValueData.FromString("1")));
        Assert.Equal(PolicyState.OtherValue, wrongType.SavedState);
        Assert.Equal(PolicyState.OtherValue, wrongType.RegistryState);
        Assert.Equal(PolicyComparison.Unknown, wrongType.Comparison);
        Assert.Equal(PolicyState.Unknown, Read([], Missing()).SavedState);
    }

    [Fact]
    public void LaterLocalSourcesOverrideEarlierSourcesButNotTheOtherHive()
    {
        var sources = new[]
        {
            Source(Entry(1)) with { Name = "Local user" },
            Source(Entry(0)) with { Name = "Administrators" },
            Source() with { Name = "Account" },
            Source(Entry(1)) with { Name = "Machine", Scope = PolicyScope.Machine },
        };
        var observation = Read(sources, Number(1));
        Assert.Equal(3, observation.Sources.Length);
        Assert.Equal(PolicyState.Disabled, observation.SavedState);
        Assert.Equal(PolicyComparison.Differs, observation.Comparison);
        Assert.Throws<ArgumentException>(() => PolicyStateReader.Read(Definition, PolicyScope.Machine, sources, Missing()));
    }

    [Fact]
    public void DuplicateValuesAndDeletionsFollowRecordOrder()
    {
        var delete = Entry(0) with { ValueName = "**Del.Setting", ValueType = 1, Data = [32, 0, 0, 0] };
        Assert.Equal(PolicyState.Disabled, Read([Source(Entry(1), Entry(0))], Number(0)).SavedState);
        var deleted = Read([Source(Entry(1), delete)], Missing());
        Assert.Equal(PolicyState.DeleteValue, deleted.SavedState);
        Assert.Equal(PolicyComparison.Matches, deleted.Comparison);
        Assert.Equal(PolicyState.Enabled, Read([Source(delete, Entry(1))], Number(1)).SavedState);
    }

    [Theory]
    [InlineData("**DeleteValues", "Other;Setting", true)]
    [InlineData("**DeleteValues", "Other", false)]
    [InlineData("**DelVals.", " ", true)]
    public void NamedAndWholeKeyDeletionInstructionsAreRecognized(string name, string text, bool removes)
    {
        var directive = Entry(0) with { ValueName = name, ValueType = 1, Data = ImmutableArray.CreateRange(Encoding.Unicode.GetBytes(text + "\0")) };
        Assert.Equal(removes ? PolicyState.DeleteValue : PolicyState.Enabled,
            Read([Source(Entry(1), directive)], Missing()).SavedState);
    }

    [Fact]
    public void AncestorKeyDeletionDoesNotAffectSimilarlyNamedSiblings()
    {
        var delete = Entry(0) with { KeyPath = @"Software\Policies", ValueName = "**DeleteKeys", ValueType = 1,
            Data = ImmutableArray.CreateRange(Encoding.Unicode.GetBytes("TestOther\0")) };
        Assert.Equal(PolicyState.Enabled, Read([Source(Entry(1), delete)], Missing()).SavedState);
        delete = delete with { Data = ImmutableArray.CreateRange(Encoding.Unicode.GetBytes("Test\0")) };
        Assert.Equal(PolicyState.DeleteValue, Read([Source(Entry(1), delete)], Missing()).SavedState);
    }

    [Fact]
    public void MalformedAndUnknownInstructionsStayUnknown()
    {
        var malformed = Entry(0) with { ValueName = "**DeleteValues", ValueType = 1, Data = [1] };
        Assert.Equal(PolicyState.Unknown, Read([Source(Entry(1), malformed)], Missing()).SavedState);
        Assert.Equal(PolicyState.Unknown, Read([Source(Entry(1), malformed with { ValueName = "**FutureDirective" })], Missing()).SavedState);
        Assert.Equal(PolicyState.OtherValue, Read([Source(Entry(1) with { Data = [1] })], Missing()).SavedState);
    }

    [Theory]
    [InlineData("**Del.Setting", 4, true)]
    [InlineData("**DelVals.", 4, true)]
    [InlineData("**Del.Setting", 1, false)]
    [InlineData("**DelVals.", 1, false)]
    [InlineData("**DelVals", 1, true)]
    public void InvalidDeletionPayloadsAndMisspelledInstructionsAreUnknown(string name, uint type, bool space)
    {
        var directive = Entry(0) with { ValueName = name, ValueType = type, Data = space ? [32, 0, 0, 0] : [0, 0] };
        Assert.Equal(PolicyState.Unknown, Read([Source(Entry(1), directive)], Missing()).SavedState);
    }

    [Fact]
    public void CatalogUsesMachineSearchPolicyAndUserThirdPartyPolicy()
    {
        var search = Assert.Single(PracticalPolicyCatalog.Definitions, p => p.Id == "AllowSearchHighlights");
        Assert.Equal(PolicyScope.Machine, Assert.Single(search.Scopes));
        Assert.Equal("EnableDynamicContentInWSB", search.ValueName);
        Assert.Equal(PolicyState.Disabled, search.Decode(0));
        var thirdParty = Assert.Single(PracticalPolicyCatalog.Definitions, p => p.Id == "DisableThirdPartySuggestions");
        Assert.Equal(PolicyScope.User, Assert.Single(thirdParty.Scopes));
    }

    private static RegistryPolicyEntry Entry(int value) => new(Definition.KeyPath, Definition.ValueName, 4, [unchecked((byte)value), 0, 0, 0]);
    private static PolicySourceSnapshot Source(params RegistryPolicyEntry[] entries) => new("User", PolicyScope.User, PolicyFileStatus.Loaded, [.. entries]);
    private static OperationResult<RegistryValueData> Missing() => OperationResult<RegistryValueData>.Failure("Missing", ErrorCategory.NotFound);
    private static OperationResult<RegistryValueData> Number(int value) => OperationResult<RegistryValueData>.Success(RegistryValueData.FromDWord(value));
    private static PolicyObservation Read(IEnumerable<PolicySourceSnapshot> sources, OperationResult<RegistryValueData> value) =>
        PolicyStateReader.Read(Definition, PolicyScope.User, sources, value);
}
