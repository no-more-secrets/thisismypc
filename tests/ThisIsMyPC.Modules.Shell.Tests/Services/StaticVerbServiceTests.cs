using ThisIsMyPC.Interop.Win32.Registry;
using ThisIsMyPC.Modules.Shell.Tests.Fakes;

namespace ThisIsMyPC.Modules.Shell.Tests.Services;

public sealed class StaticVerbServiceTests
{
    private readonly FakeRegistryService _registry = new();

    private static readonly IReadOnlyList<(string KeyPath, string Scope)> TestScopes =
    [
        (@"HKCR\*\shell", "All files"),
        (@"HKCR\Directory\shell", "Directories"),
        (@"HKCR\Directory\Background\shell", "Folder background"),
        (@"HKCR\DesktopBackground\shell", "Desktop background"),
        (@"HKCR\Drive\shell", "Drives"),
        (@"HKCR\Folder\shell", "Folders"),
        (@"HKCR\AllFilesystemObjects\shell", "All filesystem objects"),
    ];

    private StaticVerbService CreateService() => new(_registry, TestScopes);

    private void SetupVerb(string shellPath, string verbName, string? command = null,
        string? muiVerb = null, string? icon = null, string? position = null,
        bool extended = false, bool legacyDisabled = false,
        string? delegateExecuteClsid = null, bool hasDropTarget = false,
        string? appliesTo = null, bool hasLuaShield = false,
        bool programmaticAccessOnly = false)
    {
        var verbKeyPath = $@"{shellPath}\{verbName}";
        _registry.AddKey(verbKeyPath);

        if (muiVerb is not null) _registry.SetString(verbKeyPath, "MUIVerb", muiVerb);
        if (icon is not null) _registry.SetString(verbKeyPath, "Icon", icon);
        if (position is not null) _registry.SetString(verbKeyPath, "Position", position);
        if (extended) _registry.SetString(verbKeyPath, "Extended", "");
        if (legacyDisabled) _registry.SetString(verbKeyPath, "LegacyDisable", "");
        if (appliesTo is not null) _registry.SetString(verbKeyPath, "AppliesTo", appliesTo);
        if (hasLuaShield) _registry.SetString(verbKeyPath, "HasLUAShield", "");
        if (programmaticAccessOnly) _registry.SetString(verbKeyPath, "ProgrammaticAccessOnly", "");

        if (command is not null)
        {
            var commandKeyPath = $@"{verbKeyPath}\command";
            _registry.AddKey(commandKeyPath);
            _registry.SetString(commandKeyPath, "", command);
        }

        if (delegateExecuteClsid is not null)
        {
            var commandKeyPath = $@"{verbKeyPath}\command";
            _registry.AddKey(commandKeyPath);
            _registry.SetString(commandKeyPath, "DelegateExecute", delegateExecuteClsid);
        }

        if (hasDropTarget)
        {
            var dropTargetKeyPath = $@"{verbKeyPath}\DropTarget";
            _registry.AddKey(dropTargetKeyPath);
        }
    }

    [Fact]
    public void EnumerateStaticVerbs_scans_all_scope_paths()
    {
        // Place one verb in each scope
        SetupVerb(@"HKCR\*\shell", "testverb1", command: "notepad.exe");
        SetupVerb(@"HKCR\Directory\shell", "testverb2", command: "cmd.exe");
        SetupVerb(@"HKCR\Drive\shell", "testverb3", command: "explorer.exe");

        var result = CreateService().EnumerateStaticVerbs();

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value!.Count);
    }

    [Fact]
    public void EnumerateStaticVerbs_reads_verb_metadata()
    {
        SetupVerb(@"HKCR\*\shell", "AnyCode",
            command: @"C:\Program Files\VS Code\code.exe ""%1""",
            muiVerb: "Open with Code",
            icon: @"C:\Program Files\VS Code\code.exe,0",
            position: "Top");

        var result = CreateService().EnumerateStaticVerbs();

        Assert.True(result.IsSuccess);
        var verb = Assert.Single(result.Value!);
        Assert.Equal("AnyCode", verb.VerbName);
        Assert.Equal("Open with Code", verb.MuiVerb);
        Assert.Equal(@"C:\Program Files\VS Code\code.exe,0", verb.Icon);
        Assert.Equal("Top", verb.Position);
        Assert.Equal(@"C:\Program Files\VS Code\code.exe ""%1""", verb.CommandLine);
        Assert.Equal("All files", verb.Scope);
    }

    [Fact]
    public void EnumerateStaticVerbs_detects_Extended_flag()
    {
        SetupVerb(@"HKCR\*\shell", "runas", command: "cmd.exe /k", extended: true);

        var result = CreateService().EnumerateStaticVerbs();
        var verb = Assert.Single(result.Value!);
        Assert.True(verb.IsExtended);
    }

    [Fact]
    public void EnumerateStaticVerbs_detects_ProgrammaticAccessOnly()
    {
        SetupVerb(@"HKCR\*\shell", "hiddenverb", command: "hidden.exe",
            programmaticAccessOnly: true);

        var result = CreateService().EnumerateStaticVerbs();
        var verb = Assert.Single(result.Value!);
        Assert.True(verb.IsProgrammaticAccessOnly);
    }

    [Fact]
    public void EnumerateStaticVerbs_detects_LegacyDisable()
    {
        SetupVerb(@"HKCR\Directory\shell", "find", command: "explorer.exe",
            legacyDisabled: true);

        var result = CreateService().EnumerateStaticVerbs();
        var verb = Assert.Single(result.Value!);
        Assert.True(verb.IsLegacyDisabled);
    }

    [Fact]
    public void EnumerateStaticVerbs_detects_DelegateExecute_inside_command_subkey()
    {
        SetupVerb(@"HKCR\Directory\Background\shell", "opennewtab",
            delegateExecuteClsid: "{11dbb47c-a525-400b-9e80-a54615a090c0}");

        var result = CreateService().EnumerateStaticVerbs();
        var verb = Assert.Single(result.Value!);
        Assert.Equal("{11dbb47c-a525-400b-9e80-a54615a090c0}", verb.DelegateExecuteClsid);
        Assert.Null(verb.CommandLine); // DelegateExecute only — no command line
    }

    [Fact]
    public void EnumerateStaticVerbs_handles_verb_with_both_command_and_DelegateExecute()
    {
        // V4 audit: 13 verbs have both
        SetupVerb(@"HKCR\*\shell", "cmd",
            command: @"cmd.exe /s /k pushd ""%V""",
            delegateExecuteClsid: "{b455f46e-e4af-4035-b0a4-cf18d2f6f28e}");

        var result = CreateService().EnumerateStaticVerbs();
        var verb = Assert.Single(result.Value!);
        Assert.NotNull(verb.CommandLine);
        Assert.NotNull(verb.DelegateExecuteClsid);
    }

    [Fact]
    public void EnumerateStaticVerbs_detects_DropTarget_subkey()
    {
        SetupVerb(@"HKCR\*\shell", "removeproperties", hasDropTarget: true);

        var result = CreateService().EnumerateStaticVerbs();
        var verb = Assert.Single(result.Value!);
        Assert.True(verb.HasDropTarget);
        Assert.Null(verb.CommandLine);
        Assert.Null(verb.DelegateExecuteClsid);
    }

    [Fact]
    public void EnumerateStaticVerbs_includes_shell_internal_verbs_with_no_execution_data()
    {
        // Verbs like .SpotlightLearnMore have no subkeys at all
        _registry.AddKey(@"HKCR\DesktopBackground\shell\.SpotlightLearnMore");

        var result = CreateService().EnumerateStaticVerbs();
        var verb = Assert.Single(result.Value!);
        Assert.Equal(".SpotlightLearnMore", verb.VerbName);
        Assert.Null(verb.CommandLine);
        Assert.Null(verb.DelegateExecuteClsid);
        Assert.False(verb.HasDropTarget);
    }

    [Fact]
    public void EnumerateStaticVerbs_skips_ShellNew_entries()
    {
        _registry.AddKey(@"HKCR\*\shell");
        _registry.AddKey(@"HKCR\*\shell\ShellNew");
        SetupVerb(@"HKCR\*\shell", "realverb", command: "test.exe");

        var result = CreateService().EnumerateStaticVerbs();
        Assert.Single(result.Value!);
        Assert.Equal("realverb", result.Value![0].VerbName);
    }

    [Fact]
    public void EnumerateStaticVerbs_reads_HasLUAShield()
    {
        SetupVerb(@"HKCR\*\shell", "runas", command: "cmd.exe", hasLuaShield: true);

        var result = CreateService().EnumerateStaticVerbs();
        var verb = Assert.Single(result.Value!);
        Assert.True(verb.HasLuaShield);
    }

    [Fact]
    public void EnumerateStaticVerbs_reads_AppliesTo_filter()
    {
        SetupVerb(@"HKCR\*\shell", "filtered",
            command: "test.exe",
            appliesTo: "System.PerceivedType:=image");

        var result = CreateService().EnumerateStaticVerbs();
        var verb = Assert.Single(result.Value!);
        Assert.Equal("System.PerceivedType:=image", verb.AppliesTo);
    }

    [Fact]
    public void EnumerateStaticVerbs_returns_empty_for_nonexistent_scope_paths()
    {
        // No keys setup at all
        var result = CreateService().EnumerateStaticVerbs();

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!);
    }

    [Fact]
    public void EnumerateStaticVerbs_records_correct_registry_path()
    {
        SetupVerb(@"HKCR\Drive\shell", "myverb", command: "test.exe");

        var result = CreateService().EnumerateStaticVerbs();
        var verb = Assert.Single(result.Value!);
        Assert.Equal(@"HKCR\Drive\shell\myverb", verb.RegistryPath);
        Assert.Equal("Drives", verb.Scope);
    }
}
