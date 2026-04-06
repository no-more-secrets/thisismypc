using System.Reflection;
using System.Runtime.InteropServices;

namespace ThisIsMyPC.Security.Tests;

[Trait("Category", "Security")]
public class DllSearchPathHardeningTests
{
    /// <summary>
    /// Every src assembly must have [assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    /// to prevent DLL search-order hijacking in NativeAOT elevated apps (NFR30).
    /// </summary>
    [Theory]
    [MemberData(nameof(SrcAssemblies))]
    public void Assembly_HasDefaultDllImportSearchPaths(string assemblyName)
    {
        var assembly = Assembly.Load(assemblyName);
        var attr = assembly.GetCustomAttribute<DefaultDllImportSearchPathsAttribute>();

        Assert.NotNull(attr);
        Assert.Equal(DllImportSearchPath.System32, attr!.Paths);
    }

    public static TheoryData<string> SrcAssemblies() => new()
    {
        "ThisIsMyPC.App",
        "ThisIsMyPC.Core",
        "ThisIsMyPC.Interop.Com",
        "ThisIsMyPC.Interop.Win32",
        "ThisIsMyPC.Interop.Wmi",
        "ThisIsMyPC.Modules.Shell",
        "ThisIsMyPC.Modules.Startup",
        "ThisIsMyPC.Modules.Power",
    };
}
