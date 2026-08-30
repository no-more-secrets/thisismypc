using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Modules.Startup.Models;

namespace ThisIsMyPC.Modules.Startup.Changes;

/// <summary>
/// Builds ChangeDescriptors for service startup-type changes. Start/stop/restart
/// are immediate operational actions and deliberately have no factory; they
/// never enter the pending-changes pipeline or the change history.
/// </summary>
public static class ServiceChangeFactory
{
    private const string ModuleId = "Startup & Services";

    public static string GetSettingId(string serviceName) => $"service-starttype:{serviceName}";

    public static ChangeDescriptor CreateStartTypeChange(ServiceEntry entry, ServiceStartType newStartType)
    {
        return new ChangeDescriptor
        {
            ModuleId = ModuleId,
            SettingId = GetSettingId(entry.ServiceName),
            DisplayName = $"Service startup type: {entry.DisplayName}",
            SystemLocation = entry.ServiceName,
            BeforeValue = entry.StartType.ToString(),
            AfterValue = newStartType.ToString(),
            BeforeDisplay = Describe(entry.StartType),
            AfterDisplay = Describe(newStartType),
            ValueType = ChangeValueType.Service_StartType,
            Category = newStartType == ServiceStartType.Disabled
                ? ChangeCategory.Disable
                : entry.StartType == ServiceStartType.Disabled
                    ? ChangeCategory.Enable
                    : ChangeCategory.Modify,
            RestartRequirement = RestartRequirement.None,
        };
    }

    public static string Describe(ServiceStartType startType) => startType switch
    {
        ServiceStartType.Automatic => "Automatic",
        ServiceStartType.AutomaticDelayed => "Automatic (Delayed)",
        ServiceStartType.Manual => "Manual",
        ServiceStartType.Disabled => "Disabled",
        _ => startType.ToString(),
    };
}
