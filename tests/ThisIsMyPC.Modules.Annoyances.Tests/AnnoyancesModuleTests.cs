using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Modules.Annoyances;
using ThisIsMyPC.Modules.Annoyances.Changes;
using ThisIsMyPC.Modules.Annoyances.Models;
using ThisIsMyPC.Modules.Annoyances.Services;
using ThisIsMyPC.Modules.Annoyances.Tests.Fakes;

namespace ThisIsMyPC.Modules.Annoyances.Tests;

public sealed class AnnoyancesModuleTests
{
    private readonly FakeRegistryService _registry = new();
    private readonly AnnoyancesModule _module;

    public AnnoyancesModuleTests()
    {
        _module = new AnnoyancesModule(_registry);
    }

    [Fact]
    public async Task CheckAvailability_AlwaysAvailable()
    {
        var availability = await _module.CheckAvailabilityAsync();

        Assert.True(availability.IsAvailable);
    }

    [Fact]
    public async Task ScanSystemState_ReturnsAnnoyancesScanData()
    {
        var result = await _module.ScanSystemStateAsync();

        Assert.True(result.IsSuccess, result.ErrorMessage);
        var scanData = Assert.IsType<AnnoyancesScanData>(result.Value);
        Assert.Equal(10, scanData.Preferences.Count);
        Assert.False(scanData.BingSearch.IsSuppressed);
        Assert.Equal(3, scanData.SettingsSuggestedContent.Count);
    }

    [Fact]
    public async Task ApplyRevertCycle_ThroughPendingChangesService_RestoresOriginalState()
    {
        var pendingChanges = new PendingChangesService();
        var pref = new AnnoyancesSettingsReader(_registry).ReadAll().Single(p => p.Id == "scoobe-nags");
        pendingChanges.Stage(AnnoyanceChangeFactory.CreateToggle(pref, suppress: true));

        var applyResult = await pendingChanges.ApplyAllAsync(
            _module.ApplyChangeAsync, _module.RevertChangeAsync);

        Assert.True(applyResult.IsSuccess, applyResult.ErrorMessage);
        var written = _registry.ReadDWord(
            AnnoyancesRegistryPaths.UserProfileEngagementKeyPath, "ScoobeSystemSettingEnabled");
        Assert.True(written.IsSuccess);
        Assert.Equal(0, written.Value);

        // Unsuppress restores the Windows default value
        var suppressed = new AnnoyancesSettingsReader(_registry).ReadAll().Single(p => p.Id == "scoobe-nags");
        pendingChanges.Stage(AnnoyanceChangeFactory.CreateToggle(suppressed, suppress: false));
        var revertResult = await pendingChanges.ApplyAllAsync(
            _module.ApplyChangeAsync, _module.RevertChangeAsync);

        Assert.True(revertResult.IsSuccess, revertResult.ErrorMessage);
        Assert.Equal(1, _registry.ReadDWord(
            AnnoyancesRegistryPaths.UserProfileEngagementKeyPath, "ScoobeSystemSettingEnabled").Value);
    }

    [Fact]
    public async Task ApplyChange_WriteFailure_SurfacesError()
    {
        _registry.SetWriteFailure(
            AnnoyancesRegistryPaths.ContentDeliveryManagerKeyPath,
            Core.Results.ErrorCategory.AccessDenied);
        var pref = new AnnoyancesSettingsReader(_registry).ReadAll().Single(p => p.Id == "app-suggestions");

        var result = await _module.ApplyChangeAsync(AnnoyanceChangeFactory.CreateToggle(pref, suppress: true));

        Assert.False(result.IsSuccess);
        Assert.Equal(Core.Results.ErrorCategory.AccessDenied, result.ErrorCategory);
    }
}
