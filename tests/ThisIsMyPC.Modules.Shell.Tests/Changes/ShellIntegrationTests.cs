using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Interop.Win32.Shell;
using ThisIsMyPC.Modules.Shell.Changes;
using ThisIsMyPC.Modules.Shell.Services;
using ThisIsMyPC.Modules.Shell.Tests.Fakes;

namespace ThisIsMyPC.Modules.Shell.Tests.Changes;

public class ShellIntegrationTests
{
    [Theory]
    [InlineData("0", true, "1")]
    [InlineData("2", true, "3")]
    [InlineData("3", false, "2")]
    [InlineData("1", false, "0")]
    public void AutoHidePreservesOtherAppbarFlags(string before, bool on, string after)
    {
        var change = ShellIntegrationChanges.Native(ShellIntegrationChanges.Taskbar, before, on);
        Assert.Equal(after, change.AfterValue);
        Assert.Equal(before, change.BeforeValue);
        Assert.True(ShellIntegrationChanges.Allows(change));
        Assert.False(ShellIntegrationChanges.Allows(change with { SystemLocation = "shell:other" }));
        Assert.False(ShellIntegrationChanges.Allows(change with { AfterValue = "4" }));
    }

    [Fact]
    public async Task RegistrationCanApplyAndUndoWithoutTouchingOtherValues()
    {
        var registry = new FakeRegistryService();
        var entry = ShellIntegrationChanges.RegistrationValues[0];
        registry.SetString(entry.Key, "Other", "keep");
        var group = ShellIntegrationChanges.Register(registry, true, _ => true);
        Assert.Equal(5, group.Changes.Count);
        var module = new ShellModule(registry, new FakeNative());
        foreach (var change in group.Changes)
        {
            Assert.True(ShellIntegrationChanges.Allows(change));
            Assert.Contains(change.BeforeValue, new[] { ShellRegistryPaths.AbsentValue, ShellIntegrationChanges.AbsentRegistrationKey });
            Assert.True((await module.ApplyChangeAsync(change)).IsSuccess);
        }
        Assert.True(ShellIntegrationChanges.IsRegistered(registry));
        foreach (var change in group.Changes.Reverse())
            Assert.True((await module.ApplyChangeAsync(change with { BeforeValue = change.AfterValue!, AfterValue = change.BeforeValue })).IsSuccess);
        Assert.False(ShellIntegrationChanges.IsRegistered(registry));
        Assert.Equal("keep", registry.ReadString(entry.Key, "Other").Value);
    }

    [Fact]
    public void RegistrationRejectsForeignTypesAndMissingFiles()
    {
        var registry = new FakeRegistryService();
        Assert.Throws<InvalidOperationException>(() => ShellIntegrationChanges.Register(registry, true, _ => false));
        var entry = ShellIntegrationChanges.RegistrationValues[^1];
        registry.SetString(entry.Key, entry.Name, "255");
        Assert.Throws<InvalidOperationException>(() => ShellIntegrationChanges.Register(registry, true, _ => true));
    }

    [Fact]
    public void StartMenuMirrorsCaptureEachOriginalValueAndRejectWrongTypes()
    {
        var registry = new FakeRegistryService();
        var setting = ExplorerPatcherCatalog.Entries.Single(e => e.RegistryValueName == "StartUI_ShowMoreTiles");
        registry.SetDWord(setting.RegistryKeyPath, setting.RegistryValueName, 1);
        registry.SetDWord(ExplorerPatcherStartChanges.StartKey, setting.RegistryValueName, 0);
        var changes = ExplorerPatcherStartChanges.Create(registry, setting, 1).Changes;
        Assert.Equal(new[] { "1", "0" }, changes.Select(c => c.BeforeValue));
        registry.SetString(ExplorerPatcherStartChanges.StartKey, setting.RegistryValueName, "unknown");
        Assert.Throws<InvalidOperationException>(() => ExplorerPatcherStartChanges.Create(registry, setting, 1));
    }

    [Fact]
    public void MissingRegistrationDisablesDependentRowsAndStartStyleStaysVisible()
    {
        var registry = new FakeRegistryService();
        var rows = new ExplorerPatcherSettingsReader(registry, 26100, _ => false).ReadAll();
        foreach (var name in new[] { "ShrinkExplorerAddressBar", "HideExplorerSearchBar" })
        {
            var row = rows.Single(e => e.RegistryValueName == name);
            Assert.False(row.IsAvailable);
            Assert.Contains("registration", row.UnavailableReason);
        }
        var style = rows.Single(e => e.RegistryValueName == "Start_ShowClassicMode");
        Assert.False(style.IsAvailable);
        Assert.NotNull(style.UnavailableReason);
    }

    [Fact]
    public async Task FailedDwmStartRestoresExistingConfiguration()
    {
        var fake = new FakeService { Installed = true, StartType = ServiceStartType.Manual, FailStart = true };
        var native = Native(fake);
        var result = await native.WriteRoundedCornersStateAsync("Automatic|Running");
        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceStartType.Manual, fake.StartType);
        Assert.Equal(ServiceState.Stopped, fake.State);
        Assert.True(fake.Installed);
    }

    [Fact]
    public async Task NewDwmServiceIsRemovedOnFailureAndUndo()
    {
        var fake = new FakeService { FailStart = true };
        var native = Native(fake);
        Assert.False((await native.WriteRoundedCornersStateAsync("Automatic|Running")).IsSuccess);
        Assert.False(fake.Installed);
        fake.FailStart = false;
        Assert.True((await native.WriteRoundedCornersStateAsync("Automatic|Running")).IsSuccess);
        Assert.Equal("Automatic|Running", native.ReadRoundedCornersState().Value);
        Assert.True((await native.WriteRoundedCornersStateAsync("absent")).IsSuccess);
        Assert.False(fake.Installed);
    }

    [Fact]
    public async Task FailedDwmRemovalRestoresRunningState()
    {
        var fake = new FakeService { Installed = true, State = ServiceState.Running, FailRemove = true };
        Assert.False((await Native(fake).WriteRoundedCornersStateAsync("absent")).IsSuccess);
        Assert.True(fake.Installed);
        Assert.Equal(ServiceState.Running, fake.State);
    }

    [Fact]
    public async Task NavigationUsesItsOwnClsidAndRestoresEmptyExistingKeys()
    {
        var registry = new FakeRegistryService();
        registry.AddKey(ShellIntegrationChanges.NavigationKey);
        var change = ShellIntegrationChanges.NavigationChange(registry, true);
        Assert.NotEqual(ShellRegistryPaths.CommandBarKeyPath + "\\", change.SystemLocation);
        Assert.Equal(ShellRegistryPaths.AbsentValue, change.BeforeValue);
        var module = new ShellModule(registry, new FakeNative());
        Assert.True((await module.ApplyChangeAsync(change)).IsSuccess);
        Assert.True(ShellIntegrationChanges.NavigationDisabled(registry));
        Assert.True((await module.ApplyChangeAsync(change with { BeforeValue = change.AfterValue!, AfterValue = change.BeforeValue })).IsSuccess);
        Assert.False(ShellIntegrationChanges.NavigationDisabled(registry));
        Assert.True(registry.KeyExists(ShellIntegrationChanges.NavigationKey).Value);
    }

    private static ShellNativeSettings Native(FakeService fake)
    {
        var registry = new FakeRegistryService();
        registry.SetString(@"HKLM\SYSTEM\CurrentControlSet\Services\" + ShellNativeSettings.DwmService, "ImagePath", ShellNativeSettings.DwmCommand);
        return new(fake, fake, registry, _ => true);
    }
    private sealed class FakeNative : IShellNativeSettings
    {
        public OperationResult<string> ReadTaskbarState() => OperationResult<string>.Success("2");
        public OperationResult<string> ReadRoundedCornersState() => OperationResult<string>.Success("absent");
        public OperationResult<bool> WriteTaskbarState(string state) => throw new NotSupportedException();
        public Task<OperationResult<bool>> WriteRoundedCornersStateAsync(string state) => throw new NotSupportedException();
    }
    private sealed class FakeService : IServiceInstaller, IServiceControlService
    {
        public bool Installed, FailStart, FailRemove;
        public ServiceStartType StartType = ServiceStartType.Automatic;
        public ServiceState State = ServiceState.Stopped;
        private static OperationResult<bool> Ok() => OperationResult<bool>.Success(true);
        public OperationResult<bool> IsInstalled(string name) => OperationResult<bool>.Success(Installed);
        public OperationResult<bool> Install(string name, string display, string description, string path) { Installed = true; return Ok(); }
        public OperationResult<bool> Uninstall(string name) { if (FailRemove) return OperationResult<bool>.Failure("Remove failed", ErrorCategory.ServiceUnavailable); Installed = false; return Ok(); }
        public OperationResult<ServiceStatusInfo> Query(string name) => OperationResult<ServiceStatusInfo>.Success(new(name, name, State, StartType));
        public OperationResult<IReadOnlyList<ServiceEntryInfo>> EnumerateAll() => throw new NotSupportedException();
        public OperationResult<bool> SetStartType(string name, ServiceStartType type) { StartType = type; return Ok(); }
        public Task<OperationResult<bool>> StartAsync(string name, TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            if (FailStart) return Task.FromResult(OperationResult<bool>.Failure("Start failed", ErrorCategory.ServiceUnavailable));
            State = ServiceState.Running; return Task.FromResult(Ok());
        }
        public Task<OperationResult<bool>> StopAsync(string name, TimeSpan timeout, CancellationToken cancellationToken = default)
        { State = ServiceState.Stopped; return Task.FromResult(Ok()); }
    }
}
