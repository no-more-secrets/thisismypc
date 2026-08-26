namespace ThisIsMyPC.Core.Packages;

/// <summary>
/// Identity and metadata of an installed AppX/MSIX package — the before-state a
/// ChangeDescriptor needs. <paramref name="IsProvisioned"/> is null when the provisioned
/// package list could not be read (requires elevation); true means the package reinstalls
/// automatically for new user profiles until deprovisioned.
/// </summary>
public sealed record AppxPackageInfo(
    string PackageFullName,
    string PackageFamilyName,
    string DisplayName,
    string PublisherDisplayName,
    string Version,
    bool IsFramework,
    AppxSignatureKind SignatureKind,
    bool? IsProvisioned);
