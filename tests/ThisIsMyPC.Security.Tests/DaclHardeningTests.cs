using System.Reflection;
using System.Runtime.InteropServices;
using ThisIsMyPC.Interop.Win32;
using ThisIsMyPC.Interop.Win32.Security;

namespace ThisIsMyPC.Security.Tests;

[Trait("Category", "Security")]
public class DaclHardeningTests
{
    private const uint FileAllAccess = 0x1F01FF;
    private const uint SubContainersAndObjectsInherit = 0x03;

    [Fact]
    public void DataDirectoryGuard_RequiresAdministratorsAndSystemOnly()
    {
        // Verify the guard's required entries via applying to a fake
        var fake = new CapturingSecurityApi();
        var guard = new DataDirectoryGuard(fake);

        guard.EnsureHardened(@"C:\FakePath");

        Assert.NotNull(fake.CapturedEntries);
        Assert.Equal(2, fake.CapturedEntries!.Length);

        var adminEntry = Assert.Single(fake.CapturedEntries, e => e.TrusteeName == @"BUILTIN\Administrators");
        Assert.Equal(FileAllAccess, adminEntry.AccessPermissions);
        Assert.Equal(SubContainersAndObjectsInherit, adminEntry.Inheritance);

        var systemEntry = Assert.Single(fake.CapturedEntries, e => e.TrusteeName == @"NT AUTHORITY\SYSTEM");
        Assert.Equal(FileAllAccess, systemEntry.AccessPermissions);
        Assert.Equal(SubContainersAndObjectsInherit, systemEntry.Inheritance);
    }

    [Fact]
    public void DataDirectoryGuard_DisablesInheritance()
    {
        var fake = new CapturingSecurityApi();
        var guard = new DataDirectoryGuard(fake);

        guard.EnsureHardened(@"C:\FakePath");

        Assert.True(fake.CapturedDisableInheritance);
    }

    [Fact]
    public void SecurityApi_ImplementsISecurityApi()
    {
        // Structural test: SecurityApi must implement ISecurityApi
        Assert.True(typeof(ISecurityApi).IsAssignableFrom(typeof(SecurityApi)));
    }

    [Fact]
    public void NativeSecurity_AllPInvokes_HaveDefaultDllImportSearchPaths()
    {
        // Verify all P/Invoke methods in NativeSecurity have [DefaultDllImportSearchPaths]
        var nativeSecurityType = typeof(SecurityApi).Assembly
            .GetType("ThisIsMyPC.Interop.Win32.Security.NativeSecurity");

        Assert.NotNull(nativeSecurityType);

        var methods = nativeSecurityType!.GetMethods(
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);

        Assert.NotEmpty(methods);

        foreach (var method in methods)
        {
            var attr = method.GetCustomAttribute<DefaultDllImportSearchPathsAttribute>();
            Assert.NotNull(attr);
            Assert.Equal(DllImportSearchPath.System32, attr!.Paths);
        }
    }

    private sealed class CapturingSecurityApi : ISecurityApi
    {
        public DaclAccessEntry[]? CapturedEntries { get; private set; }
        public bool CapturedDisableInheritance { get; private set; }

        public uint ApplyDacl(string directoryPath, DaclAccessEntry[] entries, bool disableInheritance)
        {
            CapturedEntries = entries;
            CapturedDisableInheritance = disableInheritance;
            return 0;
        }

        public DaclInfo ReadDacl(string directoryPath)
            => DaclInfo.Failure("Fake — not set");
    }
}
