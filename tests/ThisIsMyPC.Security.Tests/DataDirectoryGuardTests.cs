using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Interop.Win32;
using ThisIsMyPC.Interop.Win32.Security;

namespace ThisIsMyPC.Security.Tests;

[Trait("Category", "Security")]
public class DataDirectoryGuardTests
{
    private const string TestPath = @"C:\FakePath\ThisIsMyPC";

    private const string AdministratorsSid = "S-1-5-32-544";
    private const string SystemSid = "S-1-5-18";
    private const uint FileAllAccess = 0x1F01FF;

    private static DaclInfo HardenedDacl() => new(
        IsProtected: true,
        Entries: [new AceInfo(AdministratorsSid, FileAllAccess), new AceInfo(SystemSid, FileAllAccess)]);

    private static DaclInfo UnprotectedDacl() => new(
        IsProtected: false,
        Entries: [new AceInfo(AdministratorsSid, FileAllAccess), new AceInfo(SystemSid, FileAllAccess)]);

    private static DaclInfo TamperedDacl() => new(
        IsProtected: true,
        Entries:
        [
            new AceInfo(AdministratorsSid, FileAllAccess),
            new AceInfo(SystemSid, FileAllAccess),
            new AceInfo("S-1-5-21-FAKE-1001", 0x1F01FF),
        ]);

    private static DaclInfo FailedReadDacl() => DaclInfo.Failure("GetNamedSecurityInfoW failed with error 5");

    [Fact]
    public void EnsureHardened_FreshDirectory_ReturnsCreated()
    {
        var fake = new FakeSecurityApi(FailedReadDacl(), applyResult: 0);
        var guard = new DataDirectoryGuard(fake);

        var result = guard.EnsureHardened(TestPath);

        Assert.True(result.IsSuccess);
        Assert.Equal(DaclStatus.Created, result.Value);
        Assert.Equal(1, fake.ApplyCallCount);
    }

    [Fact]
    public void EnsureHardened_AlreadyHardened_ReturnsVerified()
    {
        var fake = new FakeSecurityApi(HardenedDacl(), applyResult: 0);
        var guard = new DataDirectoryGuard(fake);

        var result = guard.EnsureHardened(TestPath);

        Assert.True(result.IsSuccess);
        Assert.Equal(DaclStatus.Verified, result.Value);
        Assert.Equal(0, fake.ApplyCallCount);
    }

    [Fact]
    public void EnsureHardened_TamperedDirectory_ReturnsRepaired()
    {
        var fake = new FakeSecurityApi(TamperedDacl(), applyResult: 0);
        var guard = new DataDirectoryGuard(fake);

        var result = guard.EnsureHardened(TestPath);

        Assert.True(result.IsSuccess);
        Assert.Equal(DaclStatus.Repaired, result.Value);
        Assert.Equal(1, fake.ApplyCallCount);
    }

    [Fact]
    public void EnsureHardened_UnprotectedInheritance_ReturnsCreated()
    {
        var fake = new FakeSecurityApi(UnprotectedDacl(), applyResult: 0);
        var guard = new DataDirectoryGuard(fake);

        var result = guard.EnsureHardened(TestPath);

        Assert.True(result.IsSuccess);
        Assert.Equal(DaclStatus.Created, result.Value);
        Assert.Equal(1, fake.ApplyCallCount);
    }

    [Fact]
    public void EnsureHardened_SetDaclFails_ReturnsFailedWithErrorCode()
    {
        var fake = new FakeSecurityApi(FailedReadDacl(), applyResult: 5);
        var guard = new DataDirectoryGuard(fake);

        var result = guard.EnsureHardened(TestPath);

        Assert.False(result.IsSuccess);
        Assert.Contains("Win32 error 5", result.ErrorMessage);
    }

    [Fact]
    public void EnsureHardened_ApplyCalledWithCorrectEntries()
    {
        var fake = new FakeSecurityApi(FailedReadDacl(), applyResult: 0);
        var guard = new DataDirectoryGuard(fake);

        guard.EnsureHardened(TestPath);

        Assert.Equal(TestPath, fake.LastApplyPath);
        Assert.True(fake.LastDisableInheritance);
        Assert.NotNull(fake.LastApplyEntries);
        Assert.Equal(2, fake.LastApplyEntries!.Length);
        Assert.Contains(fake.LastApplyEntries, e => e.TrusteeName == @"BUILTIN\Administrators");
        Assert.Contains(fake.LastApplyEntries, e => e.TrusteeName == @"NT AUTHORITY\SYSTEM");
        Assert.All(fake.LastApplyEntries, e =>
        {
            Assert.Equal(FileAllAccess, e.AccessPermissions);
            Assert.Equal(0x03u, e.Inheritance); // CONTAINER_INHERIT_ACE | OBJECT_INHERIT_ACE
        });
    }

    [Fact]
    public void EnsureHardened_ExceptionThrown_ReturnsFailedGracefully()
    {
        var fake = new ThrowingSecurityApi();
        var guard = new DataDirectoryGuard(fake);

        var result = guard.EnsureHardened(TestPath);

        Assert.False(result.IsSuccess);
        Assert.Contains("DACL hardening failed", result.ErrorMessage);
    }

    private sealed class FakeSecurityApi : ISecurityApi
    {
        private readonly DaclInfo _readResult;
        private readonly uint _applyResult;

        public int ApplyCallCount { get; private set; }
        public string? LastApplyPath { get; private set; }
        public DaclAccessEntry[]? LastApplyEntries { get; private set; }
        public bool LastDisableInheritance { get; private set; }

        public FakeSecurityApi(DaclInfo readResult, uint applyResult)
        {
            _readResult = readResult;
            _applyResult = applyResult;
        }

        public uint ApplyDacl(string directoryPath, DaclAccessEntry[] entries, bool disableInheritance)
        {
            ApplyCallCount++;
            LastApplyPath = directoryPath;
            LastApplyEntries = entries;
            LastDisableInheritance = disableInheritance;
            return _applyResult;
        }

        public DaclInfo ReadDacl(string directoryPath) => _readResult;
    }

    private sealed class ThrowingSecurityApi : ISecurityApi
    {
        public uint ApplyDacl(string directoryPath, DaclAccessEntry[] entries, bool disableInheritance)
            => throw new InvalidOperationException("Simulated failure");

        public DaclInfo ReadDacl(string directoryPath)
            => throw new InvalidOperationException("Simulated failure");
    }
}
