using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using ThisIsMyPC.Core.Coordination;
using ThisIsMyPC.Core.Drift.Consent;
using ThisIsMyPC.Interop.Win32.Drift.Consent;

namespace ThisIsMyPC.Security.Tests.Drift.Consent;

/// <summary>Isolated temporary files only. Requires elevation to assign Administrators ownership; never touches production consent.</summary>
[Trait("Category", "Integration")]
public sealed partial class MachineConsentStoreTests
{
    private sealed class Lease(string name) : MutationLeaseBase(name, "consent test", false)
    {
        protected override void ReleaseCore() { }
    }

    private sealed class DirectoryFixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "tipc-consent-tests", Guid.NewGuid().ToString("N"));
        public string Trusted => Path.Combine(Root, "trusted");
        public DirectoryFixture()
        {
            using var identity = WindowsIdentity.GetCurrent();
            Assert.True(new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator), "Run consent integration tests elevated.");
            Directory.CreateDirectory(Trusted);
            var security = new DirectorySecurity();
            security.SetSecurityDescriptorSddlForm("O:BAG:BAD:P(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)");
            new DirectoryInfo(Trusted).SetAccessControl(security);
        }
        public void Dispose() => Directory.Delete(Root, recursive: true);
    }

    [Fact]
    public void Recovered_lease_can_enable_and_disable_existing_trusted_storage()
    {
        using var fixture = new DirectoryFixture();
        var name = MutationLeaseNames.ForTest();
        var store = new MachineConsentStore(fixture.Trusted, name);
        Assert.Equal(MachineConsentStatus.Missing, store.Read().Status);
        using var lease = new Lease(name);
        Assert.False(store.SetEnabled(true, lease).IsSuccess);
        Assert.Empty(Directory.GetFiles(fixture.Trusted));
        lease.MarkRecovered();
        var enabled = store.SetEnabled(true, lease);
        Assert.True(enabled.IsSuccess, enabled.State.Detail);
        Assert.True(store.Read().IsGranted);
        var disabled = store.SetEnabled(false, lease);
        Assert.True(disabled.IsSuccess, disabled.State.Detail);
        Assert.False(store.Read().IsGranted);
        Assert.Single(Directory.GetFiles(fixture.Trusted));
        using var wrong = new Lease(MutationLeaseNames.ForTest());
        wrong.MarkRecovered();
        Assert.False(store.SetEnabled(true, wrong).IsSuccess);
    }

    [Fact]
    public void Unrecovered_matching_lease_can_durably_disable_but_cannot_enable()
    {
        using var fixture = new DirectoryFixture();
        var name = MutationLeaseNames.ForTest();
        var store = new MachineConsentStore(fixture.Trusted, name);
        using (var recovered = new Lease(name))
        {
            recovered.MarkRecovered();
            Assert.True(store.SetEnabled(true, recovered).IsSuccess);
        }

        using var unrecovered = new Lease(name);
        Assert.True(unrecovered.RequiresRecovery);
        Assert.False(store.SetEnabled(true, unrecovered).IsSuccess);
        Assert.True(store.Read().IsGranted);
        var disabled = store.SetEnabled(false, unrecovered);
        Assert.True(disabled.IsSuccess, disabled.State.Detail);
        var persisted = new MachineConsentStore(fixture.Trusted, name).Read();
        Assert.Equal(MachineConsentStatus.Loaded, persisted.Status);
        Assert.False(persisted.IsGranted);
        Assert.True(unrecovered.RequiresRecovery);
        Assert.False(unrecovered.CanWrite);
        unrecovered.Dispose();
        Assert.False(store.SetEnabled(false, unrecovered).IsSuccess);
        using var wrong = new Lease(MutationLeaseNames.ForTest());
        Assert.False(store.SetEnabled(false, wrong).IsSuccess);
    }
    [Fact]
    public void Weak_parent_and_weak_file_are_refused_without_repair()
    {
        using var fixture = new DirectoryFixture();
        var name = MutationLeaseNames.ForTest();
        using var lease = new Lease(name);
        lease.MarkRecovered();
        var weak = new MachineConsentStore(fixture.Root, name);
        Assert.False(weak.Read().IsGranted);
        Assert.False(weak.SetEnabled(true, lease).IsSuccess);
        Assert.False(File.Exists(Path.Combine(fixture.Root, MachineConsentStore.FileName)));
        var store = new MachineConsentStore(fixture.Trusted, name);
        Assert.True(store.SetEnabled(true, lease).IsSuccess);
        var path = Path.Combine(fixture.Trusted, MachineConsentStore.FileName);
        var security = new FileSecurity();
        security.SetSecurityDescriptorSddlForm("O:BAG:BAD:P(A;;FA;;;SY)(A;;FA;;;BA)(A;;FR;;;BU)");
        new FileInfo(path).SetAccessControl(security);
        Assert.Equal(MachineConsentStatus.Untrusted, store.Read().Status);
        Assert.False(store.SetEnabled(false, lease).IsSuccess);
        Assert.Contains("BU", new FileInfo(path).GetAccessControl().GetSecurityDescriptorSddlForm(AccessControlSections.Access), StringComparison.Ordinal);
    }

    [Fact]
    public void Corrupt_trusted_content_is_off_and_explicit_change_can_replace_it()
    {
        using var fixture = new DirectoryFixture();
        var name = MutationLeaseNames.ForTest();
        using var lease = new Lease(name);
        lease.MarkRecovered();
        var store = new MachineConsentStore(fixture.Trusted, name);
        Assert.True(store.SetEnabled(true, lease).IsSuccess);
        File.WriteAllText(Path.Combine(fixture.Trusted, MachineConsentStore.FileName), "{}");
        Assert.Equal(MachineConsentStatus.Corrupt, store.Read().Status);
        Assert.False(store.Read().IsGranted);
        Assert.True(store.SetEnabled(false, lease).IsSuccess);
    }

    [Fact]
    public void Reparse_ancestry_and_hardlinked_consent_are_refused()
    {
        using var fixture = new DirectoryFixture();
        var name = MutationLeaseNames.ForTest();
        using var lease = new Lease(name);
        lease.MarkRecovered();
        var alias = Path.Combine(fixture.Root, "alias");
        Directory.CreateSymbolicLink(alias, fixture.Trusted);
        try
        {
            var linked = new MachineConsentStore(alias, name);
            Assert.Equal(MachineConsentStatus.Untrusted, linked.Read().Status);
            Assert.False(linked.SetEnabled(true, lease).IsSuccess);
        }
        finally { Directory.Delete(alias); }
        var store = new MachineConsentStore(fixture.Trusted, name);
        Assert.True(store.SetEnabled(true, lease).IsSuccess);
        var second = Path.Combine(fixture.Trusted, "second-link");
        Assert.True(CreateHardLinkW(second, Path.Combine(fixture.Trusted, MachineConsentStore.FileName), 0));
        Assert.Equal(MachineConsentStatus.Untrusted, store.Read().Status);
        Assert.False(store.SetEnabled(false, lease).IsSuccess);
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLinkW(string newName, string existingName, nint securityAttributes);
}