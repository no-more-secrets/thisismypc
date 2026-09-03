using System.Security.Principal;
using ThisIsMyPC.Interop.Win32;

namespace ThisIsMyPC.Integration.Tests.Services;

/// <summary>
/// Reads this process's token only; nothing here starts a shell.
/// </summary>
public sealed class ShellLauncherTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public void IsElevated_agrees_with_the_Windows_principal()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        var adminNow = principal.IsInRole(WindowsBuiltInRole.Administrator);

        // A full admin token is elevated; a filtered or plain user token is not.
        // Both answers must come from the same token, so they agree.
        Assert.Equal(adminNow, ShellLauncher.IsElevated);
    }
}
