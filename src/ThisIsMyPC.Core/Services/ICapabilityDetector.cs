using ThisIsMyPC.Core.Modules;

namespace ThisIsMyPC.Core.Services;

public interface ICapabilityDetector
{
    bool IsAvailable(SystemCapability capability);
    ModuleAvailability GetAvailability(SystemCapability capability);
}
