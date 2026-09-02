using ThisIsMyPC.Installer.Services;

namespace ThisIsMyPC.Installer.Tests;

public class InstallFolderRulesTests
{
    [Fact]
    public void DefaultFolder_IsProgramFilesPublisherApp()
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        Assert.Equal(Path.Combine(programFiles, "NMS", "ThisIsMyPC"), InstallFolderRules.DefaultFolder);
        Assert.Null(InstallFolderRules.Check(InstallFolderRules.DefaultFolder).Warning);
    }

    [Theory]
    [InlineData(@"D:\Apps", @"D:\Apps\ThisIsMyPC")]
    [InlineData(@"D:\Apps\", @"D:\Apps\ThisIsMyPC")]
    [InlineData(@"D:\Apps\ThisIsMyPC", @"D:\Apps\ThisIsMyPC")]
    [InlineData(@"D:\Apps\thisismypc\", @"D:\Apps\thisismypc")]
    public void WithAppFolder_AppendsTheAppFolderOnce(string picked, string expected)
    {
        Assert.Equal(expected, InstallFolderRules.WithAppFolder(picked));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(@"Apps\ThisIsMyPC")]
    [InlineData(@"\ThisIsMyPC")]
    [InlineData(@"C:\")]
    [InlineData(@"C:\Apps\Thi|s")]
    public void Check_RejectsUnusableFolders(string folder)
    {
        var check = InstallFolderRules.Check(folder);
        Assert.False(check.IsValid);
        Assert.NotNull(check.Error);
    }

    [Fact]
    public void Check_WarnsOutsideProgramFiles()
    {
        var check = InstallFolderRules.Check(@"D:\Apps\ThisIsMyPC");
        Assert.True(check.IsValid);
        Assert.Null(check.Error);
        Assert.Contains("outside Program Files", check.Warning);
    }

    [Fact]
    public void IsUnderProgramFiles_RequiresASeparatorAfterTheParent()
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        Assert.True(InstallFolderRules.IsUnderProgramFiles(Path.Combine(programFiles, "X")));
        Assert.False(InstallFolderRules.IsUnderProgramFiles(programFiles + "Fake\\X"));
    }
}
