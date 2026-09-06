using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using ThisIsMyPC.Core.Coordination;
using ThisIsMyPC.Core.Drift;
using ThisIsMyPC.Core.Drift.Baseline;
using ThisIsMyPC.Interop.Win32.Drift.Baseline;

namespace ThisIsMyPC.Security.Tests.Drift.Baseline;

/// <summary>Isolated temporary files only. Requires elevation to assign Administrators ownership; never touches production settings.</summary>
[Trait("Category", "Integration")]
public sealed partial class MachineBaselineStorageTests
{
    private sealed class Lease(string name) : MutationLeaseBase(name, "consent test", false)
    {
        protected override void ReleaseCore() { }
    }

    private sealed class DirectoryFixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "tipc-baseline-tests", Guid.NewGuid().ToString("N"));
        public string Trusted => Path.Combine(Root, "trusted");
        public DirectoryFixture()
        {
            using var identity = WindowsIdentity.GetCurrent();
            Assert.True(new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator), "Run baseline integration tests elevated.");
            Directory.CreateDirectory(Trusted);
            var security = new DirectorySecurity();
            security.SetSecurityDescriptorSddlForm("O:BAG:BAD:P(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)");
            new DirectoryInfo(Trusted).SetAccessControl(security);
        }
        public void Dispose() => Directory.Delete(Root, recursive: true);
    }

    private const string Sid = "S-1-5-21-111-222-333-1001";
    private static SingleOwnerBaselineEntry Entry()
    {
        var target = RestorationCatalog.Default.Targets[0];
        return new(target.ModuleId, target.SettingId, target.KeyPath + "\\" + target.ValueName,
            target.SuppressedValue, DateTimeOffset.UtcNow);
    }

    [Fact]
    public void ChosenValueSurvivesRestartAndOwnerMismatchCannotReplaceIt()
    {
        using var fixture = new DirectoryFixture();
        using var lease = new Lease("baseline");
        lease.MarkRecovered();
        var store = new SingleOwnerBaselineStore(new MachineBaselineStorage(fixture.Trusted), "baseline", Sid);
        Assert.Empty(store.Read(lease));
        var entry = Entry();
        store.RecordApplied([entry], lease);
        var reopened = new SingleOwnerBaselineStore(new MachineBaselineStorage(fixture.Trusted), "baseline", Sid);
        Assert.Equal(entry, Assert.Single(reopened.Read(lease)));
        var other = new SingleOwnerBaselineStore(new MachineBaselineStorage(fixture.Trusted), "baseline", "S-1-5-21-111-222-333-1002");
        Assert.Throws<InvalidDataException>(() => other.RecordApplied([entry], lease));
        Assert.Equal(entry, Assert.Single(reopened.Read(lease)));
        Assert.Single(Directory.GetFiles(fixture.Trusted));
    }

    [Fact]
    public void UntrustedDirectoryAndFileAreRefusedWithoutRepair()
    {
        using var fixture = new DirectoryFixture();
        var weak = new MachineBaselineStorage(fixture.Root);
        Assert.ThrowsAny<IOException>(() => weak.Read(1024));
        Assert.ThrowsAny<IOException>(() => weak.ReplaceDurably("{}"u8.ToArray()));
        var storage = new MachineBaselineStorage(fixture.Trusted);
        storage.ReplaceDurably("{}"u8.ToArray());
        var path = Path.Combine(fixture.Trusted, MachineBaselineStorage.FileName);
        var security = new FileSecurity();
        security.SetSecurityDescriptorSddlForm("O:BAG:BAD:P(A;;FA;;;SY)(A;;FA;;;BA)(A;;FR;;;BU)");
        new FileInfo(path).SetAccessControl(security);
        Assert.ThrowsAny<IOException>(() => storage.Read(1024));
        Assert.ThrowsAny<IOException>(() => storage.ReplaceDurably("[]"u8.ToArray()));
        Assert.Equal("{}", File.ReadAllText(path));
    }

    [Fact]
    public void CorruptTrustedBaselineCannotBeSilentlyOverwritten()
    {
        using var fixture = new DirectoryFixture();
        var storage = new MachineBaselineStorage(fixture.Trusted);
        storage.ReplaceDurably("broken"u8.ToArray());
        using var lease = new Lease("baseline");
        lease.MarkRecovered();
        var store = new SingleOwnerBaselineStore(storage, "baseline", Sid);
        Assert.Throws<InvalidDataException>(() => store.Read(lease));
        Assert.Throws<InvalidDataException>(() => store.RecordApplied([Entry()], lease));
        Assert.Equal("broken", File.ReadAllText(Path.Combine(fixture.Trusted, MachineBaselineStorage.FileName)));
    }

    [Fact]
    public void ExclusiveDestinationLockPreservesPreviousDocumentAndReportsFailure()
    {
        using var fixture = new DirectoryFixture();
        var storage = new MachineBaselineStorage(fixture.Trusted);
        storage.ReplaceDurably("before"u8.ToArray());
        var path = Path.Combine(fixture.Trusted, MachineBaselineStorage.FileName);
        using (var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
            Assert.ThrowsAny<IOException>(() => storage.ReplaceDurably("after"u8.ToArray()));
        Assert.Equal("before", File.ReadAllText(path));
        Assert.Single(Directory.GetFiles(fixture.Trusted));
    }

    [Fact]
    public void ReparseAncestryAndHardlinkedFileAreRefused()
    {
        using var fixture = new DirectoryFixture();
        var alias = Path.Combine(fixture.Root, "alias");
        Directory.CreateSymbolicLink(alias, fixture.Trusted);
        try { Assert.ThrowsAny<IOException>(() => new MachineBaselineStorage(alias).Read(1024)); }
        finally { Directory.Delete(alias); }
        var storage = new MachineBaselineStorage(fixture.Trusted);
        storage.ReplaceDurably("before"u8.ToArray());
        Assert.True(CreateHardLinkW(Path.Combine(fixture.Trusted, "second"), Path.Combine(fixture.Trusted, MachineBaselineStorage.FileName), 0));
        Assert.ThrowsAny<IOException>(() => storage.Read(1024));
        Assert.ThrowsAny<IOException>(() => storage.ReplaceDurably("after"u8.ToArray()));
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLinkW(string newName, string existingName, nint securityAttributes);
}