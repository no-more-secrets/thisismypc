using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Modules.Startup.Changes;
using ThisIsMyPC.Modules.Startup.Models;
using ThisIsMyPC.Modules.Startup.Services;
using ThisIsMyPC.Modules.Startup.Tests.Fakes;

namespace ThisIsMyPC.Modules.Startup.Tests.Services;

public class AutorunTogglerTests
{
    private readonly FakeRegistryService _registry = new();
    private readonly FakeStartupFolderService _folders = new();
    private readonly FakeScheduledTaskService _tasks = new();

    private AutorunToggler CreateToggler() => new(_registry, _folders, _tasks);

    private StartupModule CreateModule() => new(_registry, _folders, new FakeServiceControlService(), _tasks,
        new TaskClassificationOverrideStore(Path.Combine(Path.GetTempPath(), $"tipc-autorun-{Guid.NewGuid():N}.txt")));

    [Fact]
    public void RegistryValue_DisableMovesItIntoAutorunsDisabledAndEnableMovesItBack()
    {
        _registry.SetString(StartupScanner.MachineRunKey, "Acme", @"C:\Acme\acme.exe");
        var target = new AutorunTarget(AutorunItemKind.RegistryValue, StartupScanner.MachineRunKey, "Acme");
        var disabledKey = $@"{StartupScanner.MachineRunKey}\AutorunsDisabled";

        Assert.True(CreateToggler().Apply(target, enable: false).IsSuccess);
        Assert.False(_registry.ValueExists(StartupScanner.MachineRunKey, "Acme").Value);
        Assert.Equal(@"C:\Acme\acme.exe", _registry.ReadString(disabledKey, "Acme").Value);

        // Already disabled: a second disable is a no-op success.
        Assert.True(CreateToggler().Apply(target, enable: false).IsSuccess);

        Assert.True(CreateToggler().Apply(target, enable: true).IsSuccess);
        Assert.Equal(@"C:\Acme\acme.exe", _registry.ReadString(StartupScanner.MachineRunKey, "Acme").Value);
        Assert.False(_registry.ValueExists(disabledKey, "Acme").Value);
    }

    [Fact]
    public void RegistryValue_KeepsTheValueType()
    {
        _registry.SetBinary(AutorunLocations.FontDriversKey, "Blob", [1, 2, 3]);
        var target = new AutorunTarget(AutorunItemKind.RegistryValue, AutorunLocations.FontDriversKey, "Blob");

        Assert.True(CreateToggler().Apply(target, enable: false).IsSuccess);

        var moved = _registry.ReadBinary($@"{AutorunLocations.FontDriversKey}\AutorunsDisabled", "Blob");
        Assert.True(moved.IsSuccess);
        Assert.Equal(new byte[] { 1, 2, 3 }, moved.Value);
    }

    [Fact]
    public void RegistryValue_MissingEverywhereFails()
    {
        _registry.AddKey(StartupScanner.MachineRunKey);
        var target = new AutorunTarget(AutorunItemKind.RegistryValue, StartupScanner.MachineRunKey, "Gone");

        var result = CreateToggler().Apply(target, enable: false);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCategory.NotFound, result.ErrorCategory);
    }

    [Fact]
    public void RegistryKey_MovesTheWholeSubtreeWithTypesAndDeletesTheSource()
    {
        var parent = AutorunLocations.BackgroundContextMenuHandlersKey;
        _registry.SetString($@"{parent}\Foo", "", "{CLSID}");
        _registry.SetDWord($@"{parent}\Foo", "Flags", 7);
        _registry.SetString($@"{parent}\Foo\Nested", "Deep", "value");
        var target = new AutorunTarget(AutorunItemKind.RegistryKey, parent, "Foo");

        Assert.True(CreateToggler().Apply(target, enable: false).IsSuccess);

        Assert.False(_registry.KeyExists($@"{parent}\Foo").Value);
        Assert.Equal("{CLSID}", _registry.ReadString($@"{parent}\AutorunsDisabled\Foo", "").Value);
        Assert.Equal(7, _registry.ReadDWord($@"{parent}\AutorunsDisabled\Foo", "Flags").Value);
        Assert.Equal("value", _registry.ReadString($@"{parent}\AutorunsDisabled\Foo\Nested", "Deep").Value);

        Assert.True(CreateToggler().Apply(target, enable: true).IsSuccess);
        Assert.Equal("value", _registry.ReadString($@"{parent}\Foo\Nested", "Deep").Value);
        Assert.False(_registry.KeyExists($@"{parent}\AutorunsDisabled\Foo").Value);
    }

    [Fact]
    public void StartupFile_MovesBetweenTheFolderAndItsAutorunsDisabledSubfolder()
    {
        const string folder = @"C:\ProgramData\Microsoft\Windows\Start Menu\Programs\Startup";
        _folders.AddItem(StartupFolderScope.AllUsers, $@"{folder}\Tool.lnk", @"C:\Tool\tool.exe");
        var target = new AutorunTarget(AutorunItemKind.StartupFile, folder, "Tool.lnk");

        Assert.True(CreateToggler().Apply(target, enable: false).IsSuccess);

        Assert.Empty(_folders.Enumerate(StartupFolderScope.AllUsers).Value!);
        var parked = Assert.Single(_folders.EnumerateDisabled(StartupFolderScope.AllUsers).Value!);
        Assert.Equal($@"{folder}\AutorunsDisabled\Tool.lnk", parked.FilePath);

        Assert.True(CreateToggler().Apply(target, enable: true).IsSuccess);
        Assert.Single(_folders.Enumerate(StartupFolderScope.AllUsers).Value!);
    }

    [Fact]
    public void Service_DisableParksTheStartTypeAndEnableRestoresIt()
    {
        var key = $@"{AutorunLocations.ServicesKey}\Spooler";
        _registry.SetDWord(key, "Start", 2);
        var target = new AutorunTarget(AutorunItemKind.Service, AutorunLocations.ServicesKey, "Spooler");

        Assert.True(CreateToggler().Apply(target, enable: false).IsSuccess);
        Assert.Equal(4, _registry.ReadDWord(key, "Start").Value);
        Assert.Equal(2, _registry.ReadDWord(key, "AutorunsDisabled").Value);

        Assert.True(CreateToggler().Apply(target, enable: true).IsSuccess);
        Assert.Equal(2, _registry.ReadDWord(key, "Start").Value);
        Assert.False(_registry.ValueExists(key, "AutorunsDisabled").Value);
    }

    [Fact]
    public void Service_EnableWithoutASavedStartTypeFailsInsteadOfGuessing()
    {
        var key = $@"{AutorunLocations.ServicesKey}\Spooler";
        _registry.SetDWord(key, "Start", 4);
        var target = new AutorunTarget(AutorunItemKind.Service, AutorunLocations.ServicesKey, "Spooler");

        var result = CreateToggler().Apply(target, enable: true);

        Assert.False(result.IsSuccess);
        Assert.Contains("Windows Services", result.ErrorMessage, StringComparison.Ordinal);
        Assert.Equal(4, _registry.ReadDWord(key, "Start").Value);
    }

    [Fact]
    public void ScheduledTask_GoesThroughTheScheduler()
    {
        _tasks.AddTask(@"\Acme\Updater", enabled: true);
        var target = new AutorunTarget(AutorunItemKind.ScheduledTask, @"\Acme\Updater", "Updater");

        Assert.True(CreateToggler().Apply(target, enable: false).IsSuccess);

        Assert.False(_tasks.GetTask(@"\Acme\Updater")!.IsEnabled);
    }

    [Fact]
    public void RegistryValue_RefusesToOverwriteAParkedTwin()
    {
        _registry.SetString(StartupScanner.MachineRunKey, "Acme", @"C:\Acme\new.exe");
        _registry.SetString($@"{StartupScanner.MachineRunKey}\AutorunsDisabled", "Acme", @"C:\Acme\old.exe");
        var target = new AutorunTarget(AutorunItemKind.RegistryValue, StartupScanner.MachineRunKey, "Acme");

        var result = CreateToggler().Apply(target, enable: false);

        Assert.False(result.IsSuccess);
        Assert.Contains("already exists", result.ErrorMessage, StringComparison.Ordinal);
        Assert.Equal(@"C:\Acme\new.exe", _registry.ReadString(StartupScanner.MachineRunKey, "Acme").Value);
        Assert.Equal(@"C:\Acme\old.exe", _registry.ReadString($@"{StartupScanner.MachineRunKey}\AutorunsDisabled", "Acme").Value);
    }

    [Fact]
    public void RegistryKey_RefusesToMergeIntoAParkedTwin()
    {
        var parent = AutorunLocations.BackgroundContextMenuHandlersKey;
        _registry.SetString($@"{parent}\Foo", "", "{NEW}");
        _registry.SetString($@"{parent}\AutorunsDisabled\Foo", "", "{OLD}");
        var target = new AutorunTarget(AutorunItemKind.RegistryKey, parent, "Foo");

        var result = CreateToggler().Apply(target, enable: true);

        Assert.False(result.IsSuccess);
        Assert.Equal("{NEW}", _registry.ReadString($@"{parent}\Foo", "").Value);
        Assert.Equal("{OLD}", _registry.ReadString($@"{parent}\AutorunsDisabled\Foo", "").Value);
    }

    [Fact]
    public void RegistryValue_NameMayContainThePipeCharacter()
    {
        _registry.SetString(StartupScanner.MachineRunKey, "Acme|Tray", @"C:\Acme\tray.exe");
        var target = AutorunTarget.TryParse(new AutorunTarget(AutorunItemKind.RegistryValue, StartupScanner.MachineRunKey, "Acme|Tray").Encode());

        Assert.NotNull(target);
        Assert.Equal("Acme|Tray", target.Name);
        Assert.True(CreateToggler().Apply(target, enable: false).IsSuccess);
        Assert.Equal(@"C:\Acme\tray.exe", _registry.ReadString($@"{StartupScanner.MachineRunKey}\AutorunsDisabled", "Acme|Tray").Value);
    }

    [Fact]
    public void Service_AlreadyDisabledOutsideThisAppRefusesBothWays()
    {
        var key = $@"{AutorunLocations.ServicesKey}\Spooler";
        _registry.SetDWord(key, "Start", 4);
        var target = new AutorunTarget(AutorunItemKind.Service, AutorunLocations.ServicesKey, "Spooler");

        var disable = CreateToggler().Apply(target, enable: false);
        var enable = CreateToggler().Apply(target, enable: true);

        Assert.False(disable.IsSuccess);
        Assert.False(enable.IsSuccess);
        Assert.False(_registry.ValueExists(key, "AutorunsDisabled").Value);
        Assert.Equal(4, _registry.ReadDWord(key, "Start").Value);
    }

    [Fact]
    public void StartupFile_DefaultMoveRefusesToOverwrite()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"tipc-startup-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(folder, "AutorunsDisabled"));
        File.WriteAllText(Path.Combine(folder, "Tool.lnk"), "new");
        File.WriteAllText(Path.Combine(folder, "AutorunsDisabled", "Tool.lnk"), "old");
        try
        {
            IStartupFolderService real = new RealFolderMoves();
            var result = real.Move(Path.Combine(folder, "Tool.lnk"), Path.Combine(folder, "AutorunsDisabled", "Tool.lnk"));

            Assert.False(result.IsSuccess);
            Assert.Equal("new", File.ReadAllText(Path.Combine(folder, "Tool.lnk")));
            Assert.Equal("old", File.ReadAllText(Path.Combine(folder, "AutorunsDisabled", "Tool.lnk")));

            // Source gone and destination present is the idempotent success.
            File.Delete(Path.Combine(folder, "Tool.lnk"));
            Assert.True(real.Move(Path.Combine(folder, "Tool.lnk"), Path.Combine(folder, "AutorunsDisabled", "Tool.lnk")).IsSuccess);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>Uses the interface's default Move (the file system) with no enumeration.</summary>
    private sealed class RealFolderMoves : IStartupFolderService
    {
        public OperationResult<IReadOnlyList<StartupFolderItem>> Enumerate(StartupFolderScope scope)
            => OperationResult<IReadOnlyList<StartupFolderItem>>.Success([]);
    }

    [Fact]
    public void ReRegisteredValue_DisablePurgesTheLiveCopyAndEnableRestoresItFromTheSnapshot()
    {
        var parkedKey = $@"{StartupScanner.MachineRunKey}\AutorunsDisabled";
        _registry.SetString(StartupScanner.MachineRunKey, "Acme", @"C:\Acme\new.exe");
        _registry.SetString(parkedKey, "Acme", @"C:\Acme\old.exe");
        var target = new AutorunTarget(AutorunItemKind.RegistryValue, StartupScanner.MachineRunKey, "Acme");
        var snapshot = AutorunSnapshot.Capture(_registry, _folders, target.Kind, target.Location, target.Name);
        Assert.NotNull(snapshot);

        Assert.True(CreateToggler().Apply(target, enable: false, snapshot).IsSuccess);
        Assert.False(_registry.ValueExists(StartupScanner.MachineRunKey, "Acme").Value);
        Assert.Equal(@"C:\Acme\old.exe", _registry.ReadString(parkedKey, "Acme").Value);

        Assert.True(CreateToggler().Apply(target, enable: true, snapshot).IsSuccess);
        Assert.Equal(@"C:\Acme\new.exe", _registry.ReadString(StartupScanner.MachineRunKey, "Acme").Value);
        Assert.Equal(@"C:\Acme\old.exe", _registry.ReadString(parkedKey, "Acme").Value);
    }

    [Fact]
    public void ReRegisteredKey_PurgeAndRestoreKeepTheWholeTree()
    {
        var parent = AutorunLocations.BackgroundContextMenuHandlersKey;
        _registry.SetString($@"{parent}\Foo", "", "{NEW}");
        _registry.SetDWord($@"{parent}\Foo\Nested", "Flag", 5);
        _registry.SetString($@"{parent}\AutorunsDisabled\Foo", "", "{OLD}");
        var target = new AutorunTarget(AutorunItemKind.RegistryKey, parent, "Foo");
        var snapshot = AutorunSnapshot.Capture(_registry, _folders, target.Kind, target.Location, target.Name);

        Assert.True(CreateToggler().Apply(target, enable: false, snapshot).IsSuccess);
        Assert.False(_registry.KeyExists($@"{parent}\Foo").Value);
        Assert.Equal("{OLD}", _registry.ReadString($@"{parent}\AutorunsDisabled\Foo", "").Value);

        Assert.True(CreateToggler().Apply(target, enable: true, snapshot).IsSuccess);
        Assert.Equal("{NEW}", _registry.ReadString($@"{parent}\Foo", "").Value);
        Assert.Equal(5, _registry.ReadDWord($@"{parent}\Foo\Nested", "Flag").Value);
    }

    [Fact]
    public void ReRegisteredFile_PurgeDeletesAndRestoreWritesTheBytesBack()
    {
        const string folder = @"C:\ProgramData\Microsoft\Windows\Start Menu\Programs\Startup";
        _folders.AddItem(StartupFolderScope.AllUsers, $@"{folder}\Tool.lnk", @"C:\Tool\tool.exe");
        _folders.AddDisabledItem(StartupFolderScope.AllUsers, $@"{folder}\AutorunsDisabled\Tool.lnk", @"C:\Tool\tool.exe");
        var target = new AutorunTarget(AutorunItemKind.StartupFile, folder, "Tool.lnk");
        var snapshot = AutorunSnapshot.Capture(_registry, _folders, target.Kind, target.Location, target.Name);
        Assert.NotNull(snapshot?.FileBase64);

        Assert.True(CreateToggler().Apply(target, enable: false, snapshot).IsSuccess);
        Assert.Equal($@"{folder}\Tool.lnk", Assert.Single(_folders.Deleted));
        Assert.Empty(_folders.Enumerate(StartupFolderScope.AllUsers).Value!);
        Assert.Single(_folders.EnumerateDisabled(StartupFolderScope.AllUsers).Value!);

        Assert.True(CreateToggler().Apply(target, enable: true, snapshot).IsSuccess);
        var restored = Assert.Single(_folders.Restored);
        Assert.Equal($@"{folder}\Tool.lnk", restored.Path);
        Assert.Equal(Convert.FromBase64String(snapshot.FileBase64!), restored.Contents);
    }

    [Fact]
    public async Task Module_PurgesAReRegisteredCopyThroughTheDescriptorAndUndoRestoresIt()
    {
        var parkedKey = $@"{StartupScanner.UserRunKey}\AutorunsDisabled";
        _registry.SetString(StartupScanner.UserRunKey, "Acme", @"C:\Acme\new.exe");
        _registry.SetString(parkedKey, "Acme", @"C:\Acme\old.exe");
        var entry = new AutorunsScanner(_registry, _folders, _ => new StartupFileMetadata(null, null), @"C:\Windows")
            .Scan([], []).Single(e => e.Name == "Acme");
        Assert.True(entry.IsReRegistered);
        var change = AutorunChangeFactory.CreateToggle(entry, enable: false);
        Assert.StartsWith("Enabled;", change.BeforeValue, StringComparison.Ordinal);
        var module = CreateModule();

        Assert.True((await module.ApplyChangeAsync(change)).IsSuccess);
        Assert.False(_registry.ValueExists(StartupScanner.UserRunKey, "Acme").Value);
        Assert.Equal(@"C:\Acme\old.exe", _registry.ReadString(parkedKey, "Acme").Value);

        var reverted = await module.RevertChangeAsync(change with { BeforeValue = change.AfterValue!, AfterValue = change.BeforeValue });
        Assert.True(reverted.IsSuccess);
        Assert.Equal(@"C:\Acme\new.exe", _registry.ReadString(StartupScanner.UserRunKey, "Acme").Value);
    }

    [Fact]
    public async Task Module_AppliesAndRevertsAnAutorunDescriptor()
    {
        _registry.SetString(StartupScanner.UserRunKey, "Acme", @"C:\Acme\acme.exe");
        var entry = new AutorunEntry
        {
            Category = AutorunCategory.Logon,
            Kind = AutorunItemKind.RegistryValue,
            Name = "Acme",
            Location = StartupScanner.UserRunKey,
            Data = @"C:\Acme\acme.exe",
            IsEnabled = true,
        };
        var change = AutorunChangeFactory.CreateToggle(entry, enable: false);
        var module = CreateModule();

        var applied = await module.ApplyChangeAsync(change);
        Assert.True(applied.IsSuccess);
        Assert.False(_registry.ValueExists(StartupScanner.UserRunKey, "Acme").Value);

        var reverted = await module.RevertChangeAsync(change with { BeforeValue = change.AfterValue!, AfterValue = change.BeforeValue });
        Assert.True(reverted.IsSuccess);
        Assert.Equal(@"C:\Acme\acme.exe", _registry.ReadString(StartupScanner.UserRunKey, "Acme").Value);
    }

    [Fact]
    public async Task Module_RejectsAMalformedLocation()
    {
        var change = AutorunChangeFactory.CreateToggle(new AutorunEntry
        {
            Category = AutorunCategory.Logon,
            Kind = AutorunItemKind.RegistryValue,
            Name = "Acme",
            Location = StartupScanner.UserRunKey,
            Data = "x",
            IsEnabled = true,
        }, enable: false) with { SystemLocation = "nonsense" };

        var result = await CreateModule().ApplyChangeAsync(change);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCategory.NotFound, result.ErrorCategory);
    }
}
