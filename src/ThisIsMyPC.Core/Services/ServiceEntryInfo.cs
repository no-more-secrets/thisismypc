namespace ThisIsMyPC.Core.Services;

/// <summary>
/// One service from a full SCM enumeration. Description is null when the
/// service has none or its config could not be read (protected services).
/// </summary>
public record ServiceEntryInfo(
    string ServiceName,
    string DisplayName,
    string? Description,
    ServiceState State,
    ServiceStartType StartType);
