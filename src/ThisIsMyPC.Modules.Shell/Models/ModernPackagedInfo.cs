namespace ThisIsMyPC.Modules.Shell.Models;

public sealed record ModernPackagedInfo(
    string PackageFamilyName,
    string PackageDisplayName,
    string PublisherDisplayName,
    IReadOnlyList<string>? ItemTypes,
    string? VerbId,
    string? InstallSource);
