using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Sets;
using ThisIsMyPC.Modules.Shell.Services;
using ThisIsMyPC.Modules.Shell.Tests.Fakes;

namespace ThisIsMyPC.Modules.Shell.Tests;

public sealed class WidgetsPolicyTests
{
    private const string Key = @"HKLM\SOFTWARE\Policies\Microsoft\Dsh";
    private static SetEntry Disable => new()
    {
        ModuleId = "Explorer", SettingId = "allow-widgets", Value = "0", Description = "",
    };

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(1)]
    public async Task ApplyAndUndo_RestoresExactPolicyWithoutWritingProtectedPreference(int? before)
    {
        var registry = new FakeRegistryService();
        registry.SetWriteFailure(ShellRegistryPaths.AdvancedKeyPath, ErrorCategory.AccessDenied);
        if (before.HasValue) registry.SetDWord(Key, "AllowNewsAndInterests", before.Value);
        var inspector = new ShellSetEntryInspector(registry);
        var change = Assert.Single(inspector.CreateChangeGroup(Disable)!.Changes);
        Assert.Equal(before?.ToString() ?? ShellRegistryPaths.AbsentValue, change.BeforeValue);
        Assert.Equal(Key + "\\AllowNewsAndInterests", change.SystemLocation);
        await using var module = new ShellModule(registry);
        Assert.True((await module.ApplyChangeAsync(change)).IsSuccess);
        Assert.True(inspector.Inspect(Disable)!.IsApplied);
        Assert.True((await module.RevertChangeAsync(change with
            { BeforeValue = change.AfterValue!, AfterValue = change.BeforeValue })).IsSuccess);
        var read = registry.ReadDWord(Key, "AllowNewsAndInterests");
        Assert.Equal(before.HasValue, read.IsSuccess);
        if (before.HasValue) Assert.Equal(before.Value, read.Value);
        Assert.False(registry.ReadDWord(ShellRegistryPaths.AdvancedKeyPath, "TaskbarDa").IsSuccess);
    }

    [Fact]
    public void UnknownPolicy_DoesNotStage()
    {
        var registry = new FakeRegistryService();
        registry.SetDWord(Key, "AllowNewsAndInterests", 7);
        var inspector = new ShellSetEntryInspector(registry);
        Assert.Null(inspector.Inspect(Disable));
        Assert.Null(inspector.CreateChangeGroup(Disable));
        registry.SetString(Key, "AllowNewsAndInterests", "0");
        Assert.Null(inspector.CreateChangeGroup(Disable));
        registry.SetReadFailure(Key, "AllowNewsAndInterests", ErrorCategory.AccessDenied);
        Assert.Null(inspector.CreateChangeGroup(Disable));
    }
}
