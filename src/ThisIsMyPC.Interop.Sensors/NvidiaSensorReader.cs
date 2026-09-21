// Read-only polling derived from LibreHardwareMonitor, MPL-2.0.
// Source attribution and pinned revision: third-party/librehardwaremonitor/README.md.
using ThisIsMyPC.Core.Hardware.Sensors;
using ThisIsMyPC.Interop.Sensors.LibreHardwareMonitor;

namespace ThisIsMyPC.Interop.Sensors;

internal static class NvidiaSensorReader
{
    internal static void Read(NvApi.NvPhysicalGpuHandle handle, string id, string name,
        List<HardwareSensorReading> readings)
    {
        void Add(string key, string label, HardwareSensorUnit unit, double value) =>
            readings.Add(new(id + "/" + key, id, name, HardwareSensorComponent.Gpu, label, unit,
                double.IsFinite(value) ? value : null));

        if (NvApi.NvAPI_GPU_GetThermalSettings is { } temperature)
        {
            var data = new NvApi.NvThermalSettings
            {
                Version = (uint)NvApi.MAKE_NVAPI_VERSION<NvApi.NvThermalSettings>(2),
                Sensor = new NvApi.NvSensor[NvApi.MAX_THERMAL_SENSORS_PER_GPU],
            };
            if (temperature(handle, (int)NvApi.NvThermalTarget.All, ref data) == NvApi.NvStatus.OK)
                for (var index = 0; index < Math.Min(data.Count, data.Sensor.Length); index++)
                {
                    var sensor = data.Sensor[index];
                    var label = sensor.Target switch
                    {
                        NvApi.NvThermalTarget.Gpu => "GPU temperature",
                        NvApi.NvThermalTarget.Memory => "Memory temperature",
                        NvApi.NvThermalTarget.Board => "Board temperature",
                        _ => "Temperature sensor " + (index + 1),
                    };
                    Add("temperature/" + index, label, HardwareSensorUnit.Celsius, sensor.CurrentTemp);
                }
        }

        if (NvApi.NvAPI_GPU_GetAllClockFrequencies is { } clock)
        {
            for (var version = 1; version <= 3; version++)
            {
                var data = new NvApi.NvGpuClockFrequencies
                {
                    Version = (uint)NvApi.MAKE_NVAPI_VERSION<NvApi.NvGpuClockFrequencies>(version),
                    Clocks = new NvApi.NvGpuClockFrequenciesDomain[NvApi.MAX_GPU_PUBLIC_CLOCKS],
                };
                if (clock(handle, ref data) != NvApi.NvStatus.OK) continue;
                foreach (var (index, label) in new[] { (0, "Core clock"), (4, "Memory clock"), (8, "Video clock") })
                    if (data.Clocks[index].IsPresent)
                        Add("clock/" + index, label, HardwareSensorUnit.Megahertz, data.Clocks[index].Frequency / 1000d);
                break;
            }
        }

        if (NvApi.NvAPI_GPU_GetThermalSensors is { } thermal)
        {
            uint mask = 0;
            for (var bit = 0; bit < 32; bit++)
            {
                var probe = ThermalData(1u << bit);
                if (thermal(handle, ref probe) != NvApi.NvStatus.OK) break;
                mask |= 1u << bit;
            }
            var data = ThermalData(mask);
            if (mask != 0 && thermal(handle, ref data) == NvApi.NvStatus.OK)
            {
                var is50 = name.StartsWith("NVIDIA GeForce RTX 50", StringComparison.OrdinalIgnoreCase);
                var is40 = name.StartsWith("NVIDIA GeForce RTX 40", StringComparison.OrdinalIgnoreCase);
                if (!is50 && data.Temperatures[1] > 0)
                    Add("hotspot", "Hot spot temperature", HardwareSensorUnit.Celsius, data.Temperatures[1] / 256d);
                var memoryIndex = is50 ? 2 : is40 ? 7 : 9;
                if (data.Temperatures[memoryIndex] > 0)
                    Add("memory-junction", "Memory junction temperature", HardwareSensorUnit.Celsius,
                        data.Temperatures[memoryIndex] / 256d);
            }
        }

        if (NvApi.NvAPI_GPU_GetDynamicPstatesInfoEx is { } load)
        {
            var data = new NvApi.NvDynamicPStatesInfo
            {
                Version = (uint)NvApi.MAKE_NVAPI_VERSION<NvApi.NvDynamicPStatesInfo>(1),
                Utilizations = new NvApi.NvDynamicPState[NvApi.MAX_GPU_UTILIZATIONS],
            };
            if (load(handle, ref data) == NvApi.NvStatus.OK)
                foreach (var (index, label) in new[] { (0, "GPU load"), (1, "Memory controller load"), (2, "Video engine load"), (3, "Bus load") })
                    if (data.Utilizations[index].IsPresent && data.Utilizations[index].Percentage is >= 0 and <= 100)
                        Add("load/" + index, label, HardwareSensorUnit.Percent, data.Utilizations[index].Percentage);
        }

        if (NvApi.NvAPI_GPU_ClientFanCoolersGetStatus is { } fans)
        {
            var data = new NvApi.NvFanCoolersStatus
            {
                Version = (uint)NvApi.MAKE_NVAPI_VERSION<NvApi.NvFanCoolersStatus>(1),
                Items = new NvApi.NvFanCoolersStatusItem[NvApi.MAX_FAN_COOLERS_STATUS_ITEMS],
            };
            if (fans(handle, ref data) == NvApi.NvStatus.OK)
                for (var index = 0; index < Math.Min(data.Count, data.Items.Length); index++)
                    Add("fan/" + data.Items[index].CoolerId, "Fan " + (index + 1), HardwareSensorUnit.Rpm,
                        data.Items[index].CurrentRpm);
        }

        if (NvApi.NvAPI_GPU_GetMemoryInfoEx is { } memory)
        {
            var data = new NvApi.NvMemoryInfoEx { Version = (uint)NvApi.MAKE_NVAPI_VERSION<NvApi.NvMemoryInfoEx>(1) };
            if (memory(handle, ref data) == NvApi.NvStatus.OK && data.DedicatedVideoMemory >= data.CurrentAvailableDedicatedVideoMemory)
            {
                const double mib = 1024d * 1024;
                Add("memory/total", "Memory total", HardwareSensorUnit.Megabytes, data.DedicatedVideoMemory / mib);
                Add("memory/free", "Memory available", HardwareSensorUnit.Megabytes, data.CurrentAvailableDedicatedVideoMemory / mib);
                Add("memory/used", "Memory used", HardwareSensorUnit.Megabytes,
                    (data.DedicatedVideoMemory - data.CurrentAvailableDedicatedVideoMemory) / mib);
            }
        }

        if (NvApi.NvAPI_GPU_ClientVoltRailsGetStatus is { } voltage)
        {
            var data = new NvApi.NvGpuClientVoltRailsStatus
            { Version = (uint)NvApi.MAKE_NVAPI_VERSION<NvApi.NvGpuClientVoltRailsStatus>(1) };
            if (voltage(handle, ref data) == NvApi.NvStatus.OK)
                Add("voltage", "Core voltage", HardwareSensorUnit.Volts,
                    (((ulong)data.CoreMicrovoltsHigh << 32) | data.CoreMicrovolts) / 1_000_000d);
        }
    }

    private static NvApi.NvThermalSensors ThermalData(uint mask) => new()
    {
        Version = (uint)NvApi.MAKE_NVAPI_VERSION<NvApi.NvThermalSensors>(2), Mask = mask,
        Reserved = new int[NvApi.THERMAL_SENSOR_RESERVED_COUNT],
        Temperatures = new int[NvApi.THERMAL_SENSOR_TEMPERATURE_COUNT],
    };
}
