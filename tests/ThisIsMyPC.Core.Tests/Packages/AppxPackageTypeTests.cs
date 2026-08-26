using ThisIsMyPC.Core.Packages;

namespace ThisIsMyPC.Core.Tests.Packages;

public sealed class AppxPackageTypeTests
{
    [Fact]
    public void AppxPackageInfo_CarriesAllIdentityFields()
    {
        var info = new AppxPackageInfo(
            PackageFullName: "Microsoft.Todos_2.104.62421.0_x64__8wekyb3d8bbwe",
            PackageFamilyName: "Microsoft.Todos_8wekyb3d8bbwe",
            DisplayName: "Microsoft To Do",
            PublisherDisplayName: "Microsoft Corporation",
            Version: "2.104.62421.0",
            IsFramework: false,
            SignatureKind: AppxSignatureKind.Store,
            IsProvisioned: true);

        Assert.Equal("Microsoft.Todos_2.104.62421.0_x64__8wekyb3d8bbwe", info.PackageFullName);
        Assert.Equal("Microsoft.Todos_8wekyb3d8bbwe", info.PackageFamilyName);
        Assert.Equal("Microsoft To Do", info.DisplayName);
        Assert.Equal("Microsoft Corporation", info.PublisherDisplayName);
        Assert.Equal("2.104.62421.0", info.Version);
        Assert.False(info.IsFramework);
        Assert.Equal(AppxSignatureKind.Store, info.SignatureKind);
        Assert.True(info.IsProvisioned);
    }

    [Fact]
    public void AppxPackageInfo_ProvisionedFlagSupportsUnknown()
    {
        var info = new AppxPackageInfo(
            "full", "family", "name", "publisher", "1.0.0.0",
            IsFramework: true, AppxSignatureKind.System, IsProvisioned: null);

        Assert.Null(info.IsProvisioned);
    }

    [Fact]
    public void AppxSignatureKind_MirrorsWinRtOrdinals()
    {
        // Convention only — the service converts via an explicit switch, so ordinals are
        // not load-bearing. This documents the intended 1:1 mirror of PackageSignatureKind.
        Assert.Equal(0, (int)AppxSignatureKind.None);
        Assert.Equal(1, (int)AppxSignatureKind.Developer);
        Assert.Equal(2, (int)AppxSignatureKind.Enterprise);
        Assert.Equal(3, (int)AppxSignatureKind.Store);
        Assert.Equal(4, (int)AppxSignatureKind.System);
    }
}
