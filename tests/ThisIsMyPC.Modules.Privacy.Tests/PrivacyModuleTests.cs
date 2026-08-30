using ThisIsMyPC.Modules.Privacy;
using ThisIsMyPC.Modules.Privacy.Changes;
using ThisIsMyPC.Modules.Privacy.Models;
using ThisIsMyPC.Modules.Privacy.Services;
using ThisIsMyPC.Modules.Privacy.Tests.Fakes;

namespace ThisIsMyPC.Modules.Privacy.Tests;

public sealed class PrivacyModuleTests
{
    private readonly FakeRegistryService _registry = new();
    private readonly PrivacyModule _module;

    public PrivacyModuleTests()
    {
        _module = new PrivacyModule(_registry);
    }

    [Fact]
    public async Task ScanSystemState_ReturnsPrivacyScanData()
    {
        var result = await _module.ScanSystemStateAsync();

        Assert.True(result.IsSuccess, result.ErrorMessage);
        var scanData = Assert.IsType<PrivacyScanData>(result.Value);
        Assert.Equal(5, scanData.Preferences.Count);
        Assert.Equal(4, scanData.InkingTyping.Count);
    }

    [Fact]
    public async Task PolicyToggle_ConfigureWritesValue_RestoreDeletesIt()
    {
        var reader = new PrivacySettingsReader(_registry);

        var configure = PrivacyChangeFactory.CreateToggle(
            reader.ReadSingles().Single(p => p.Id == "telemetry-level"), configure: true);
        Assert.True((await _module.ApplyChangeAsync(configure)).IsSuccess);
        Assert.Equal(1, _registry.ReadDWord(
            PrivacyRegistryPaths.DataCollectionPoliciesKeyPath, "AllowTelemetry").Value);

        var restore = PrivacyChangeFactory.CreateToggle(
            reader.ReadSingles().Single(p => p.Id == "telemetry-level"), configure: false);
        Assert.True((await _module.ApplyChangeAsync(restore)).IsSuccess);
        Assert.False(_registry.ValueExists(
            PrivacyRegistryPaths.DataCollectionPoliciesKeyPath, "AllowTelemetry").Value);
    }

    [Fact]
    public async Task Revert_AppliesTheSwappedDescriptor_RoundTrippingToDelete()
    {
        // History undo hands a Before/After-swapped descriptor; an originally-absent
        // policy (BeforeValue "") must round-trip into a delete.
        var reader = new PrivacySettingsReader(_registry);
        var configure = PrivacyChangeFactory.CreateToggle(
            reader.ReadSingles().Single(p => p.Id == "location"), configure: true);
        await _module.ApplyChangeAsync(configure);

        var swapped = configure with { BeforeValue = configure.AfterValue, AfterValue = configure.BeforeValue };
        var result = await _module.RevertChangeAsync(swapped);

        Assert.True(result.IsSuccess);
        Assert.False(_registry.ValueExists(
            PrivacyRegistryPaths.LocationPoliciesKeyPath, "DisableLocation").Value);
    }
}
