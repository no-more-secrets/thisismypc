using System.Diagnostics;
using System.Security.Principal;
using ThisIsMyPC.Interop.Win32;

namespace ThisIsMyPC.Integration.Tests.Services;

/// <summary>
/// Reads tokens only; nothing here starts a shell.
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

    [Fact]
    [Trait("Category", "Integration")]
    public void An_unelevated_run_needs_no_borrowed_token_and_an_elevated_one_finds_a_source()
    {
        // Unelevated: the process is already the desktop user, so null.
        // Elevated: the session always has sihost or ctfmon to borrow from.
        using var token = ShellLauncher.CaptureDesktopUserToken(preferred: null);

        if (ShellLauncher.IsElevated)
        {
            Assert.NotNull(token);
            Assert.False(string.IsNullOrEmpty(token!.Source));
        }
        else
        {
            Assert.Null(token);
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void A_running_shell_is_the_preferred_source_when_elevated()
    {
        var shells = Process.GetProcessesByName("explorer");
        try
        {
            if (shells.Length == 0 || !ShellLauncher.IsElevated)
                return;

            using var token = ShellLauncher.CaptureDesktopUserToken(shells[0]);

            Assert.NotNull(token);
            Assert.StartsWith("explorer", token!.Source, StringComparison.Ordinal);
        }
        finally
        {
            foreach (var shell in shells)
                shell.Dispose();
        }
    }
}
