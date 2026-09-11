using Avalonia;

namespace ThisIsMyPC.App.UiTests;

public sealed class RendererPolicyTests
{
    [Fact]
    public void AcgRendererUsesSoftwareOnly()
    {
        var appOptions = Program.CreateAcgWin32Options();
        Assert.Equal([Win32RenderingMode.Software], appOptions.RenderingMode);
    }
}
