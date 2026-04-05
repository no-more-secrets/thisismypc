using ThisIsMyPC.Modules.Shell.Services;
using ThisIsMyPC.Modules.Shell.Tests.Fakes;

namespace ThisIsMyPC.Modules.Shell.Tests.Services;

public sealed class TaskbarSettingsReaderTests
{
    private const string AdvancedKeyPath = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";

    private readonly FakeRegistryService _registry = new();
    private readonly TaskbarSettingsReader _sut;

    public TaskbarSettingsReaderTests()
    {
        _sut = new TaskbarSettingsReader(_registry);
    }

    [Fact]
    public void Read_returns_center_alignment_when_TaskbarAl_is_1()
    {
        _registry.SetDWord(AdvancedKeyPath, "TaskbarAl", 1);
        _registry.SetDWord(AdvancedKeyPath, "TaskbarDa", 1);

        var result = _sut.Read();

        Assert.Equal(1, result.Alignment);
    }

    [Fact]
    public void Read_returns_left_alignment_when_TaskbarAl_is_0()
    {
        _registry.SetDWord(AdvancedKeyPath, "TaskbarAl", 0);
        _registry.SetDWord(AdvancedKeyPath, "TaskbarDa", 1);

        var result = _sut.Read();

        Assert.Equal(0, result.Alignment);
    }

    [Fact]
    public void Read_defaults_alignment_to_center_when_missing()
    {
        _registry.SetDWord(AdvancedKeyPath, "TaskbarDa", 1);
        // TaskbarAl not set

        var result = _sut.Read();

        Assert.Equal(1, result.Alignment);
    }

    [Fact]
    public void Read_returns_widgets_enabled_when_TaskbarDa_is_1()
    {
        _registry.SetDWord(AdvancedKeyPath, "TaskbarAl", 1);
        _registry.SetDWord(AdvancedKeyPath, "TaskbarDa", 1);

        var result = _sut.Read();

        Assert.True(result.WidgetsEnabled);
    }

    [Fact]
    public void Read_returns_widgets_disabled_when_TaskbarDa_is_0()
    {
        _registry.SetDWord(AdvancedKeyPath, "TaskbarAl", 1);
        _registry.SetDWord(AdvancedKeyPath, "TaskbarDa", 0);

        var result = _sut.Read();

        Assert.False(result.WidgetsEnabled);
    }

    [Fact]
    public void Read_returns_classic_context_menu_enabled_when_key_exists()
    {
        _registry.SetDWord(AdvancedKeyPath, "TaskbarAl", 1);
        _registry.SetDWord(AdvancedKeyPath, "TaskbarDa", 1);
        _registry.AddKey(ShellRegistryPaths.ClassicContextMenuKeyPath);

        var result = _sut.Read();

        Assert.True(result.ClassicContextMenu);
    }

    [Fact]
    public void Read_returns_classic_context_menu_disabled_when_key_absent()
    {
        _registry.SetDWord(AdvancedKeyPath, "TaskbarAl", 1);
        _registry.SetDWord(AdvancedKeyPath, "TaskbarDa", 1);
        // Classic context menu key not set

        var result = _sut.Read();

        Assert.False(result.ClassicContextMenu);
    }

    [Fact]
    public void Read_returns_classic_command_bar_enabled_when_key_exists()
    {
        _registry.SetDWord(AdvancedKeyPath, "TaskbarAl", 1);
        _registry.SetDWord(AdvancedKeyPath, "TaskbarDa", 1);
        _registry.AddKey(ShellRegistryPaths.CommandBarKeyPath);

        var result = _sut.Read();

        Assert.True(result.ClassicCommandBar);
    }

    [Fact]
    public void Read_returns_classic_command_bar_disabled_when_key_absent()
    {
        _registry.SetDWord(AdvancedKeyPath, "TaskbarAl", 1);
        _registry.SetDWord(AdvancedKeyPath, "TaskbarDa", 1);
        // Command bar key not set

        var result = _sut.Read();

        Assert.False(result.ClassicCommandBar);
    }
}
