using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Modules.Startup.Changes;
using ThisIsMyPC.Modules.Startup.Models;
using ThisIsMyPC.Modules.Startup.Services;

namespace ThisIsMyPC.Modules.Startup.Tests.Changes;

public class StartupChangeFactoryTests
{
    private static StartupEntry MakeEntry(StartupSource source = StartupSource.RegistryUserRun, string name = "App") => new()
    {
        Name = name,
        Command = @"C:\app.exe",
        Source = source,
        SourceLocation = StartupScanner.UserRunKey,
        IsEnabled = true,
    };

    [Fact]
    public void CreateToggle_Disable_StagesDisabledBlobAtApprovedKey()
    {
        var change = StartupChangeFactory.CreateToggle(MakeEntry(), enable: false, currentApprovedBlob: null);

        Assert.NotNull(change);
        Assert.Equal("Startup & Services", change.ModuleId);
        Assert.Equal($@"{StartupScanner.UserApprovedRunKey}\App", change.SystemLocation);
        Assert.Equal(string.Empty, change.BeforeValue); // absent value
        Assert.Equal(Convert.ToHexString(StartupChangeFactory.DisabledBlob), change.AfterValue);
        Assert.Equal("Enabled", change.BeforeDisplay);
        Assert.Equal("Disabled", change.AfterDisplay);
        Assert.Equal(ChangeValueType.Registry_Binary, change.ValueType);
        Assert.Equal(ChangeCategory.Disable, change.Category);
    }

    [Fact]
    public void CreateToggle_Enable_FromDisabledBlob()
    {
        var disabled = new byte[] { 0x03, 0, 0, 0, 0x10, 0x20, 0, 0, 0, 0, 0, 0 };
        var change = StartupChangeFactory.CreateToggle(MakeEntry(), enable: true, currentApprovedBlob: disabled);

        Assert.NotNull(change);
        Assert.Equal(Convert.ToHexString(disabled), change.BeforeValue);
        Assert.Equal(Convert.ToHexString(StartupChangeFactory.EnabledBlob), change.AfterValue);
        Assert.Equal("Disabled", change.BeforeDisplay);
        Assert.Equal("Enabled", change.AfterDisplay);
        Assert.Equal(ChangeCategory.Enable, change.Category);
    }

    [Theory]
    [InlineData(StartupSource.RegistryMachineRun, StartupScanner.MachineApprovedRunKey)]
    [InlineData(StartupSource.RegistryMachineRunWow64, StartupScanner.MachineApprovedRun32Key)]
    [InlineData(StartupSource.RegistryUserRun, StartupScanner.UserApprovedRunKey)]
    [InlineData(StartupSource.StartupFolderUser, StartupScanner.UserApprovedStartupFolderKey)]
    [InlineData(StartupSource.StartupFolderCommon, StartupScanner.MachineApprovedStartupFolderKey)]
    public void GetApprovedKeyPath_MapsEverySupportedSource(StartupSource source, string expectedKey)
    {
        Assert.Equal(expectedKey, StartupChangeFactory.GetApprovedKeyPath(source));
    }

    [Fact]
    public void CreateToggle_ScheduledTask_ReturnsNull()
    {
        var entry = MakeEntry(StartupSource.ScheduledTask);
        Assert.Null(StartupChangeFactory.GetApprovedKeyPath(StartupSource.ScheduledTask));
        Assert.Null(StartupChangeFactory.CreateToggle(entry, enable: false, currentApprovedBlob: null));
    }
}
