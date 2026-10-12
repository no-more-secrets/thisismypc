using ThisIsMyPC.Core;

namespace ThisIsMyPC.Core.Tests;

public sealed class WindowsRequirementsTests
{
    [Theory]
    [InlineData(6, 3, 9600, false)]
    [InlineData(10, 0, 19045, false)]
    [InlineData(10, 0, 22000, false)]
    [InlineData(10, 0, 22620, false)]
    [InlineData(10, 0, 22621, true)]
    [InlineData(10, 0, 22631, true)]
    [InlineData(10, 0, 26100, true)]
    public void IsSupported_UsesWindows11Version22H2Cutoff(int major, int minor, int build, bool expected)
    {
        Assert.Equal(expected, WindowsRequirements.IsSupported(new Version(major, minor, build)));
    }

    [Fact]
    public void IsSupported_RejectsMissingVersion()
    {
        Assert.Throws<ArgumentNullException>(() => WindowsRequirements.IsSupported(null!));
    }
}
