namespace ThisIsMyPC.Modules.Shell.Models;

public sealed record NotificationSetting(
    string Id,
    string DisplayName,
    string Description,
    string RegistryKeyPath,
    string RegistryValueName,
    bool IsEnabled);
