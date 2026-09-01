using ThisIsMyPC.Interop.Win32.Security;

namespace ThisIsMyPC.Security.Tests;

/// <summary>
/// The signature gate an elevated launch must pass (hardening checklist):
/// AuthenticodeVerifier (WinVerifyTrust) and AppExecutionAlias (reparse-target
/// resolution). Tests against live system binaries carry Category=Integration.
/// </summary>
[Trait("Category", "Security")]
public sealed class ChildProcessGateTests
{
    private static readonly string DotnetExe = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet", "dotnet.exe");

    private static readonly string WingetAlias = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Microsoft", "WindowsApps", "winget.exe");

    [Fact]
    public void UnsignedFile_IsRejected()
    {
        var path = Path.Combine(Path.GetTempPath(), $"tipc-unsigned-{Guid.NewGuid():N}.exe");
        File.WriteAllBytes(path, [0x4D, 0x5A, 0x90, 0x00]); // MZ header, no signature
        try
        {
            var result = AuthenticodeVerifier.VerifyTrusted(path);
            Assert.False(result.IsSuccess);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void MissingFile_IsRejected()
    {
        var result = AuthenticodeVerifier.VerifyTrusted(
            Path.Combine(Path.GetTempPath(), $"tipc-absent-{Guid.NewGuid():N}.exe"));

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void PlainFile_ResolvesToItself()
    {
        var path = Path.Combine(Path.GetTempPath(), $"tipc-plain-{Guid.NewGuid():N}.txt");
        File.WriteAllText(path, "not a reparse point");
        try
        {
            Assert.Equal(path, AppExecutionAlias.ResolveTarget(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void SignedMicrosoftBinary_PassesWithSubjectCheck()
    {
        if (!File.Exists(DotnetExe))
            return; // machine without dotnet at the default location

        var result = AuthenticodeVerifier.VerifyTrusted(DotnetExe, "Microsoft Corporation");

        Assert.True(result.IsSuccess, result.ErrorMessage);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void SignedBinary_WrongSubject_IsRejected()
    {
        if (!File.Exists(DotnetExe))
            return; // machine without dotnet at the default location

        var result = AuthenticodeVerifier.VerifyTrusted(DotnetExe, "Contoso Ltd");

        Assert.False(result.IsSuccess);
        Assert.Contains("Contoso", result.ErrorMessage, StringComparison.Ordinal);
    }

    /// <summary>The exact path the winget launch gate takes on a real machine.</summary>
    [Fact]
    [Trait("Category", "Integration")]
    public void LiveWingetAlias_ResolvesAndVerifiesAsMicrosoft()
    {
        if (!File.Exists(WingetAlias))
            return; // machine without winget for this user

        var target = AppExecutionAlias.ResolveTarget(WingetAlias);

        Assert.NotNull(target);
        Assert.NotEqual(WingetAlias, target);
        Assert.True(Path.IsPathFullyQualified(target!));
        Assert.True(File.Exists(target));

        var result = AuthenticodeVerifier.VerifyTrusted(target!, "Microsoft Corporation");
        Assert.True(result.IsSuccess, result.ErrorMessage);
    }
}
