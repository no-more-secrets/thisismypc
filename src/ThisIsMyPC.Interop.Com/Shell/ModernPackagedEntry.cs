namespace ThisIsMyPC.Interop.Com.Shell;

public sealed record ModernPackagedEntry(
    string Clsid,
    string HandlerName,
    string PackageFamilyName,
    string PackageDisplayName,
    string PublisherDisplayName,
    IReadOnlyList<string>? ItemTypes,
    string? VerbId,
    string? IconPath,
    string? InstallSource);
