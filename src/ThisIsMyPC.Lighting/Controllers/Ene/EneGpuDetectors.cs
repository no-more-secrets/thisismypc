using ThisIsMyPC.Core.Hardware.Lighting;
using ThisIsMyPC.Lighting.Transport;

namespace ThisIsMyPC.Lighting.Controllers.Ene;

/// <summary>
/// ASUS graphics cards with an ENE controller on the GPU's I2C port
/// (ENESMBusControllerDetect.cpp, DetectENESMBusGPUControllers). The id
/// table in EneGpuDetectors.g.cs is generated from OpenRGB's registrations.
/// </summary>
public static partial class EneGpuDetectors
{
    internal static Controllers.ILightingController? DetectGpu(II2cBus bus, byte address, string name, List<string> notes)
    {
        if (!EneSmBusController.Probe(bus, address, notes))
            return null;
        return new EneSmBusController(bus, address, name, LightingDeviceType.Gpu);
    }
}
