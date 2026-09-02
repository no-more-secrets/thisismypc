using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Modules.Startup.Models;
using ThisIsMyPC.Modules.Startup.Services;
using ThisIsMyPC.Modules.Startup.Tests.Fakes;

namespace ThisIsMyPC.Modules.Startup.Tests.Services;

public class AutorunsScannerTests
{
    private const string Windows = @"C:\Windows";
    private readonly FakeRegistryService _registry = new();
    private readonly FakeStartupFolderService _folders = new();

    private AutorunsScanner CreateScanner() => new(
        _registry, _folders,
        path => new StartupFileMetadata(
            path.Contains("Microsoft", StringComparison.OrdinalIgnoreCase) ? "Microsoft Corporation" : "Acme",
            Path.GetFileNameWithoutExtension(path) + " description"),
        Windows);

    private IReadOnlyList<AutorunEntry> Scan(IReadOnlyList<ScheduledTaskEntry>? tasks = null, IReadOnlyList<ServiceEntry>? services = null)
        => CreateScanner().Scan(tasks ?? [], services ?? []);

    [Fact]
    public void RunKey_ListsEnabledValuesAndAutorunsDisabledValues()
    {
        _registry.SetString(StartupScanner.UserRunKey, "OneDrive", @"""C:\Users\me\OneDrive.exe"" /background");
        _registry.SetString($@"{StartupScanner.UserRunKey}\AutorunsDisabled", "Discord", @"C:\Discord\Update.exe --processStart Discord.exe");
        _registry.SetBinary(StartupScanner.UserApprovedRunKey, "OneDrive", [0x03, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]);

        var logon = Scan().Where(e => e.Category == AutorunCategory.Logon).ToList();

        var oneDrive = Assert.Single(logon, e => e.Name == "OneDrive");
        Assert.True(oneDrive.IsEnabled);
        Assert.Equal(AutorunItemKind.RegistryValue, oneDrive.Kind);
        Assert.Equal(StartupScanner.UserRunKey, oneDrive.Location);
        Assert.Equal(@"C:\Users\me\OneDrive.exe", oneDrive.ImagePath);
        Assert.Equal("Off in Task Manager", oneDrive.Note);

        var discord = Assert.Single(logon, e => e.Name == "Discord");
        Assert.False(discord.IsEnabled);
        Assert.Equal(StartupScanner.UserRunKey, discord.Location);
        Assert.Equal("Acme", discord.Publisher);
    }

    [Fact]
    public void ContextMenuHandlers_ResolveClsidToInprocServerAndReadDisabledSubkeys()
    {
        const string clsid = "{12345678-1234-1234-1234-123456789ABC}";
        _registry.SetString($@"{AutorunLocations.BackgroundContextMenuHandlersKey}\Foo", "", clsid);
        _registry.SetString($@"HKLM\SOFTWARE\Classes\CLSID\{clsid}", "", "Foo Handler");
        _registry.SetString($@"HKLM\SOFTWARE\Classes\CLSID\{clsid}\InprocServer32", "", @"%SystemRoot%\System32\foo.dll");
        _registry.SetString($@"{AutorunLocations.BackgroundContextMenuHandlersKey}\AutorunsDisabled\Bar", "", "{ABCDEF01-0000-0000-0000-000000000000}");

        var explorer = Scan().Where(e => e.Category == AutorunCategory.Explorer).ToList();

        var foo = Assert.Single(explorer, e => e.Name == "Foo");
        Assert.Equal(AutorunItemKind.RegistryKey, foo.Kind);
        Assert.True(foo.IsEnabled);
        Assert.Equal(clsid, foo.Data);
        Assert.Equal(Environment.ExpandEnvironmentVariables(@"%SystemRoot%\System32\foo.dll"), foo.ImagePath);
        Assert.Equal("Foo Handler", foo.Description);

        var bar = Assert.Single(explorer, e => e.Name == "Bar");
        Assert.False(bar.IsEnabled);
        Assert.Equal(AutorunLocations.BackgroundContextMenuHandlersKey, bar.Location);
    }

    [Fact]
    public void ActiveSetup_SkipsComponentsWithoutStubPath()
    {
        _registry.SetString($@"{AutorunLocations.ActiveSetupKey}\{{A}}", "StubPath", @"C:\Windows\System32\unregmp2.exe /FirstLogon");
        _registry.SetString($@"{AutorunLocations.ActiveSetupKey}\{{A}}", "", "Windows Media Player");
        _registry.SetString($@"{AutorunLocations.ActiveSetupKey}\{{B}}", "", "No stub here");

        var logon = Scan().Where(e => e.Location == AutorunLocations.ActiveSetupKey).ToList();

        var only = Assert.Single(logon);
        Assert.Equal("{A}", only.Name);
        Assert.Equal(@"C:\Windows\System32\unregmp2.exe", only.ImagePath);
        Assert.Equal("Windows Media Player", only.Description);
    }

    [Fact]
    public void KnownDlls_SkipDirectoryValuesAndResolveAgainstSystem32()
    {
        _registry.SetString(AutorunLocations.KnownDllsKey, "DllDirectory", @"C:\Windows\System32");
        _registry.SetString(AutorunLocations.KnownDllsKey, "kernel32", "kernel32.dll");
        _registry.SetString(AutorunLocations.Drivers32WowKey, "msacm.l3acm", "l3codeca.acm");

        var entries = Scan();

        var known = Assert.Single(entries, e => e.Category == AutorunCategory.KnownDlls);
        Assert.Equal("kernel32", known.Name);
        Assert.Equal(@"C:\Windows\System32\kernel32.dll", known.ImagePath);

        var wow = Assert.Single(entries, e => e.Category == AutorunCategory.Drivers32);
        Assert.Equal(@"C:\Windows\SysWOW64\l3codeca.acm", wow.ImagePath);
    }

    [Fact]
    public void Winsock_UsesLibraryPathAndDisplayString()
    {
        var key = $@"{AutorunLocations.WinsockCatalog64Key}\000000000001";
        _registry.SetString(key, "LibraryPath", @"%SystemRoot%\system32\mswsock.dll");
        _registry.SetString(key, "DisplayString", "Tcpip");

        var entry = Assert.Single(Scan(), e => e.Category == AutorunCategory.WinsockProviders);

        Assert.Equal("000000000001", entry.Name);
        Assert.Equal("Tcpip", entry.Description);
        Assert.Equal(Environment.ExpandEnvironmentVariables(@"%SystemRoot%\system32\mswsock.dll"), entry.ImagePath);
    }

    [Fact]
    public void OfficeAddins_ResolveProgIdThroughClsid()
    {
        const string clsid = "{0F000000-0000-0000-0000-000000000001}";
        var addin = $@"{AutorunLocations.OfficeKey}\Outlook\Addins\Acme.Connect";
        _registry.SetString(addin, "FriendlyName", "Acme Connect");
        _registry.SetDWord(addin, "LoadBehavior", 3);
        _registry.SetString(@"HKLM\SOFTWARE\Classes\Acme.Connect\CLSID", "", clsid);
        _registry.SetString($@"HKLM\SOFTWARE\Classes\CLSID\{clsid}\InprocServer32", "", @"C:\Acme\connect.dll");

        var entry = Assert.Single(Scan(), e => e.Category == AutorunCategory.Office);

        Assert.Equal("Acme.Connect", entry.Name);
        Assert.Equal("Acme Connect", entry.Description);
        Assert.Equal(@"C:\Acme\connect.dll", entry.ImagePath);
    }

    [Fact]
    public void Services_SplitDriversFromServicesAndKeepOnlyAutoStartOrAutorunsDisabled()
    {
        var services = AutorunLocations.ServicesKey;
        AddService("Spooler", type: 0x10, start: 2, image: @"%SystemRoot%\System32\spoolsv.exe");
        AddService("Wcmsvc", type: 0x20, start: 2, image: @"%SystemRoot%\system32\svchost.exe -k LocalServiceNetworkRestricted");
        _registry.SetString($@"{services}\Wcmsvc\Parameters", "ServiceDll", @"%SystemRoot%\System32\wcmsvc.dll");
        AddService("disk", type: 1, start: 0, image: @"System32\drivers\disk.sys");
        AddService("ManualOnly", type: 0x10, start: 3, image: @"C:\x.exe");
        AddService("Muted", type: 0x10, start: 4, image: @"C:\muted.exe");
        _registry.SetDWord($@"{services}\Muted", "AutorunsDisabled", 2);
        AddService("CDPUserSvc_5f3a2", type: 0xF0, start: 2, image: @"C:\x.exe");
        _registry.SetDWord($@"{services}\NotAService", "Start", 2);

        var scm = new[]
        {
            new ServiceEntry { ServiceName = "Spooler", DisplayName = "Print Spooler", Description = "Spools", State = ServiceState.Running, StartType = ServiceStartType.Automatic },
        };
        var entries = Scan(services: scm);

        var spooler = Assert.Single(entries, e => e.Name == "Spooler");
        Assert.Equal(AutorunCategory.Services, spooler.Category);
        Assert.Equal(AutorunItemKind.Service, spooler.Kind);
        Assert.Equal(services, spooler.Location);
        Assert.Equal("Print Spooler", spooler.Description);
        Assert.Equal("Automatic", spooler.Note);
        Assert.True(spooler.IsEnabled);

        var wcm = Assert.Single(entries, e => e.Name == "Wcmsvc");
        Assert.Equal(Environment.ExpandEnvironmentVariables(@"%SystemRoot%\System32\wcmsvc.dll"), wcm.ImagePath);

        var disk = Assert.Single(entries, e => e.Name == "disk");
        Assert.Equal(AutorunCategory.Drivers, disk.Category);
        Assert.Equal(@"C:\Windows\System32\drivers\disk.sys", disk.ImagePath);
        Assert.Equal("Boot start", disk.Note);

        var muted = Assert.Single(entries, e => e.Name == "Muted");
        Assert.False(muted.IsEnabled);
        Assert.Equal("Automatic", muted.Note);

        Assert.DoesNotContain(entries, e => e.Name is "ManualOnly" or "CDPUserSvc_5f3a2" or "NotAService");
    }

    [Fact]
    public void StartupFolders_ListEnabledAndDisabledFilesWithTheEnabledFolderAsLocation()
    {
        const string folder = @"C:\Users\me\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Startup";
        _folders.AddItem(StartupFolderScope.CurrentUser, $@"{folder}\Steam.lnk", @"C:\Steam\steam.exe");
        _folders.AddDisabledItem(StartupFolderScope.CurrentUser, $@"{folder}\AutorunsDisabled\Old.lnk", @"C:\Old\old.exe");

        var files = Scan().Where(e => e.Kind == AutorunItemKind.StartupFile).ToList();

        var steam = Assert.Single(files, e => e.Name == "Steam.lnk");
        Assert.True(steam.IsEnabled);
        Assert.Equal(folder, steam.Location);
        Assert.Equal(@"C:\Steam\steam.exe", steam.ImagePath);

        var old = Assert.Single(files, e => e.Name == "Old.lnk");
        Assert.False(old.IsEnabled);
        Assert.Equal(folder, old.Location);
    }

    [Fact]
    public void ScheduledTasks_PassThroughWithAuthorAsPublisher()
    {
        var task = new ScheduledTaskEntry
        {
            Name = "Updater",
            Path = @"\Acme\Updater",
            Author = "Acme",
            Description = "Checks for updates",
            IsEnabled = false,
            Classification = TaskClassification.Unknown,
        };

        var entry = Assert.Single(Scan(tasks: [task]), e => e.Category == AutorunCategory.ScheduledTasks);

        Assert.Equal(AutorunItemKind.ScheduledTask, entry.Kind);
        Assert.Equal(@"\Acme\Updater", entry.Location);
        Assert.Equal("Acme", entry.Publisher);
        Assert.False(entry.IsEnabled);
    }

    [Fact]
    public void ShellHandlers_SwitchedOffOnTheContextMenusPageShowOffAndLocked()
    {
        const string dashed = "{11111111-1111-1111-1111-111111111111}";
        const string blocked = "{22222222-2222-2222-2222-222222222222}";
        const string live = "{33333333-3333-3333-3333-333333333333}";
        _registry.SetString($@"{AutorunLocations.BackgroundContextMenuHandlersKey}\Dashed", "", "-" + dashed);
        _registry.SetString($@"{AutorunLocations.BackgroundContextMenuHandlersKey}\Blocked", "", blocked);
        _registry.SetString(AutorunLocations.BlockedShellExtensionsKey, blocked, "");
        _registry.SetString($@"{AutorunLocations.BackgroundContextMenuHandlersKey}\Live", "", live);
        _registry.SetString($@"HKLM\SOFTWARE\Classes\CLSID\{dashed}\InprocServer32", "", @"C:\Dashed\d.dll");

        var explorer = Scan().Where(e => e.Category == AutorunCategory.Explorer).ToList();

        var dashedRow = Assert.Single(explorer, e => e.Name == "Dashed");
        Assert.False(dashedRow.IsEnabled);
        Assert.False(dashedRow.CanToggle);
        Assert.Equal("Off in Context Menus", dashedRow.Note);
        Assert.Equal(dashed, dashedRow.Data);
        Assert.Equal(@"C:\Dashed\d.dll", dashedRow.ImagePath);

        var blockedRow = Assert.Single(explorer, e => e.Name == "Blocked");
        Assert.False(blockedRow.IsEnabled);
        Assert.False(blockedRow.CanToggle);

        var liveRow = Assert.Single(explorer, e => e.Name == "Live");
        Assert.True(liveRow.IsEnabled);
        Assert.True(liveRow.CanToggle);
    }

    [Fact]
    public void Values_ThatAreNotTextAreNotItems()
    {
        _registry.SetBinary(StartupScanner.MachineRunKey, "Blob", [1, 2, 3]);
        _registry.SetDWord(StartupScanner.MachineRunKey, "Flag", 1);
        _registry.SetString(StartupScanner.MachineRunKey, "Real", @"C:\real.exe");

        var logon = Scan().Where(e => e.Location == StartupScanner.MachineRunKey).ToList();

        var only = Assert.Single(logon);
        Assert.Equal("Real", only.Name);
    }

    [Fact]
    public void ReRegistered_LiveItemBesideItsParkedTwinBecomesOneFlaggedRowWithASnapshot()
    {
        _registry.SetString(StartupScanner.MachineRunKey, "Acme", @"C:\Acme\new.exe");
        _registry.SetString($@"{StartupScanner.MachineRunKey}\AutorunsDisabled", "Acme", @"C:\Acme\old.exe");
        _registry.SetString(StartupScanner.MachineRunKey, "Honest", @"C:\honest.exe");
        _registry.SetString($@"{StartupScanner.MachineRunKey}\AutorunsDisabled", "Parked", @"C:\parked.exe");
        const string folder = @"C:\ProgramData\Microsoft\Windows\Start Menu\Programs\Startup";
        _folders.AddItem(StartupFolderScope.AllUsers, $@"{folder}\Tool.lnk", @"C:\Tool\tool.exe");
        _folders.AddDisabledItem(StartupFolderScope.AllUsers, $@"{folder}\AutorunsDisabled\Tool.lnk", @"C:\Tool\tool.exe");

        var logon = Scan().Where(e => e.Category == AutorunCategory.Logon).ToList();

        var acme = Assert.Single(logon, e => e.Name == "Acme");
        Assert.True(acme.IsEnabled);
        Assert.True(acme.IsReRegistered);
        Assert.True(acme.CanToggle);
        Assert.Equal("Re-registered itself after being switched off", acme.Note);
        var snapshot = AutorunSnapshot.Deserialize(acme.LiveSnapshot);
        Assert.NotNull(snapshot);
        Assert.Equal(@"C:\Acme\new.exe", Assert.Single(snapshot.Values).Value.Data);

        var tool = Assert.Single(logon, e => e.Name == "Tool.lnk");
        Assert.True(tool.IsReRegistered);
        Assert.NotNull(AutorunSnapshot.Deserialize(tool.LiveSnapshot)?.FileBase64);

        Assert.Single(logon, e => e.Name == "Honest" && e.IsEnabled && !e.IsReRegistered);
        Assert.Single(logon, e => e.Name == "Parked" && !e.IsEnabled && !e.IsReRegistered);
    }

    private void AddService(string name, int type, int start, string image)
    {
        var key = $@"{AutorunLocations.ServicesKey}\{name}";
        _registry.SetDWord(key, "Type", type);
        _registry.SetDWord(key, "Start", start);
        _registry.SetString(key, "ImagePath", image);
    }
}
