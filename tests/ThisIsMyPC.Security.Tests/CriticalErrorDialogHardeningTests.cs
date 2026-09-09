using ThisIsMyPC.Interop.Win32.Security;

namespace ThisIsMyPC.Security.Tests;

public sealed class CriticalErrorDialogHardeningTests
{
    [Fact]
    public void Apply_EnablesSuppressionAndIsIdempotent()
    {
        Assert.True(CriticalErrorDialogHardening.Apply());
        Assert.True(CriticalErrorDialogHardening.Apply());
        Assert.True(CriticalErrorDialogHardening.IsEnabled());
    }
}
