namespace ThisIsMyPC.Core.Services;

public record ServiceStatusInfo(
    string ServiceName,
    string DisplayName,
    ServiceState State,
    ServiceStartType StartType);
