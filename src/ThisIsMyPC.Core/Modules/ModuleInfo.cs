namespace ThisIsMyPC.Core.Modules;

public record ModuleInfo(
    string Name,
    string Icon,
    string Description,
    IReadOnlyList<SystemCapability> RequiredCapabilities);
