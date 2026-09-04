using System.Security.Principal;
using ThisIsMyPC.Interop.Win32;

namespace ThisIsMyPC.Integration.Tests.Services;

/// <summary>
/// Reads tokens and impersonates the running user; it launches nothing and
/// changes nothing. Integration-only, since it needs a real logon session.
/// </summary>
public sealed class DesktopUserContextTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public void IsCallerElevated_agrees_with_the_Windows_principal()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        var adminNow = principal.IsInRole(WindowsBuiltInRole.Administrator);

        Assert.Equal(adminNow, new DesktopUserContext().IsCallerElevated);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void Current_resolves_the_desktop_user()
    {
        var user = new DesktopUserContext().Current;

        Assert.NotNull(user);
        Assert.StartsWith("S-1-", user!.Sid, StringComparison.Ordinal);
        Assert.False(string.IsNullOrEmpty(user.AccountName), "the account name should resolve");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void RunAsUser_runs_the_action_and_returns_its_value()
    {
        var ran = false;
        var result = new DesktopUserContext().RunAsUser(() =>
        {
            ran = true;
            return 42;
        });

        Assert.True(result.IsSuccess);
        Assert.Equal(42, result.Value);
        Assert.True(ran);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void RunAsUser_acts_as_the_desktop_user()
    {
        var context = new DesktopUserContext();

        // Inside the action the thread's identity is the interactive user,
        // whether by impersonation (elevated) or because we already are them.
        var sidSeen = context.RunAsUser(() =>
        {
            using var identity = WindowsIdentity.GetCurrent();
            return identity.User!.Value;
        });

        Assert.True(sidSeen.IsSuccess);
        Assert.Equal(context.Current!.Sid, sidSeen.Value);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void RunAsUser_reverts_impersonation_afterwards()
    {
        var before = WindowsIdentity.GetCurrent().User!.Value;

        new DesktopUserContext().RunAsUser(() => 0);

        // The calling thread is back to its own identity, not left impersonating.
        Assert.Equal(before, WindowsIdentity.GetCurrent().User!.Value);
    }
}
