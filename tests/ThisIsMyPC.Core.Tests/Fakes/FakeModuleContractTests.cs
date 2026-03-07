using Microsoft.Extensions.DependencyInjection;
using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Modules;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;

namespace ThisIsMyPC.Core.Tests.Fakes;

public class FakeModuleContractTests
{
    [Fact]
    public async Task FakeModule_RegistersViaDI_AndAllMethodsExecuteSuccessfully()
    {
        // Arrange — register via DI
        var services = new ServiceCollection();
        services.AddSingleton<IModule, FakeModule>();
        services.AddSingleton<IPendingChangesService, PendingChangesService>();
        await using var provider = services.BuildServiceProvider();

        var module = provider.GetRequiredService<IModule>();
        var pendingChanges = provider.GetRequiredService<IPendingChangesService>();

        // Act & Assert — CheckAvailability
        var availability = await module.CheckAvailabilityAsync();
        Assert.True(availability.IsAvailable);

        // Act & Assert — ScanSystemState
        var scanResult = await module.ScanSystemStateAsync();
        Assert.True(scanResult.IsSuccess);
        Assert.NotNull(scanResult.Value);

        // Act & Assert — ApplyChange
        var change = new ChangeDescriptor
        {
            ModuleId = "FakeModule",
            SettingId = "test-setting",
            DisplayName = "Test Setting",
            SystemLocation = "HKCU\\Test",
            BeforeValue = "0",
            AfterValue = "1",
            BeforeDisplay = "Disabled",
            AfterDisplay = "Enabled",
            ValueType = ChangeValueType.Registry_DWord,
            Category = ChangeCategory.Modify
        };

        var applyResult = await module.ApplyChangeAsync(change);
        Assert.True(applyResult.IsSuccess);

        // Act & Assert — RevertChange
        var revertResult = await module.RevertChangeAsync(change);
        Assert.True(revertResult.IsSuccess);

        // Act & Assert — ModuleInfo
        Assert.Equal("FakeModule", module.Info.Name);
        Assert.Single(module.Info.RequiredCapabilities);
        Assert.Equal(SystemCapability.Registry, module.Info.RequiredCapabilities[0]);

        // Act & Assert — PendingChangesService stages and validates module's changes
        pendingChanges.Stage(change);
        Assert.Equal(1, pendingChanges.PendingCount);

        var mutationResult = await pendingChanges.ApplyAllAsync(
            applyFunc: module.ApplyChangeAsync,
            revertFunc: module.RevertChangeAsync);

        Assert.True(mutationResult.IsSuccess);
        Assert.Single(mutationResult.Applied);
        Assert.Equal(0, pendingChanges.PendingCount);

        // Verify FakeModule state reflects the applied change
        var fakeModule = (FakeModule)module;
        Assert.Equal("1", fakeModule.GetCurrentState()["test-setting"]);
    }
}
