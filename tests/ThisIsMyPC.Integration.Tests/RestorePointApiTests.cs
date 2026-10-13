using System.Runtime.InteropServices;

namespace ThisIsMyPC.Integration.Tests;

public class RestorePointApiTests
{
    // Verifies the srclient.dll export the RestorePointService P/Invokes actually resolves
    // on this machine. Read-only. Never creates a restore point from tests.
    [Theory]
    [InlineData("SRSetRestorePointW")]
    [InlineData("EnableSR")]
    [Trait("Category", "Diagnostic")]
    public void SRSetRestorePointW_EntryPoint_Resolves(string export)
    {
        Assert.True(NativeLibrary.TryLoad("srclient.dll", out var handle), "srclient.dll failed to load");
        try
        {
            Assert.True(
                NativeLibrary.TryGetExport(handle, export, out _),
                $"{export} export not found in srclient.dll");
        }
        finally
        {
            NativeLibrary.Free(handle);
        }
    }
}
