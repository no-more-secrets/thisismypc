using ThisIsMyPC.App.Services;

namespace ThisIsMyPC.Security.Tests;

[Trait("Category", "Security")]
public class InstallPathValidationTests
{
    [Theory]
    [InlineData(@"C:\Program Files\ThisIsMyPC\")]
    [InlineData(@"C:\Program Files\ThisIsMyPC\subfolder\")]
    public void ProtectedPath_ProgramFiles_IsProtected(string path)
    {
        var guard = new InstallationGuard(path);

        Assert.True(guard.IsProtectedLocation);
        Assert.Null(guard.WarningMessage);
    }

    [Theory]
    [InlineData(@"C:\Program Files (x86)\ThisIsMyPC\")]
    [InlineData(@"C:\Program Files (x86)\ThisIsMyPC\subfolder\")]
    public void ProtectedPath_ProgramFilesX86_IsProtected(string path)
    {
        var guard = new InstallationGuard(path);

        Assert.True(guard.IsProtectedLocation);
        Assert.Null(guard.WarningMessage);
    }

    [Theory]
    [InlineData(@"C:\Users\user\Desktop\ThisIsMyPC\")]
    [InlineData(@"C:\Users\user\Downloads\ThisIsMyPC\")]
    [InlineData(@"C:\Users\user\AppData\Roaming\ThisIsMyPC\")]
    [InlineData(@"D:\Tools\ThisIsMyPC\")]
    [InlineData(@"C:\Temp\ThisIsMyPC\")]
    public void UnprotectedPath_IsNotProtected(string path)
    {
        var guard = new InstallationGuard(path);

        Assert.False(guard.IsProtectedLocation);
        Assert.NotNull(guard.WarningMessage);
        Assert.Contains(path, guard.WarningMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PathComparison_IsCaseInsensitive()
    {
        var guard = new InstallationGuard(@"c:\program files\thisismypc\");

        Assert.True(guard.IsProtectedLocation);
    }

    [Fact]
    public void WarningMessage_ContainsDllPlantingReference()
    {
        var guard = new InstallationGuard(@"C:\Users\user\Desktop\ThisIsMyPC\");

        Assert.Contains("DLL planting", guard.WarningMessage, StringComparison.OrdinalIgnoreCase);
    }
}
