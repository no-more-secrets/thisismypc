namespace ThisIsMyPC.Core.Hardware;

/// <summary>Shared read-only inventory. Presence never grants permission to control a device.</summary>
public sealed record HardwareSnapshot
{
    public DateTimeOffset ObservedAt { get; init; }
    public ObservedHardwareFacts Facts { get; init; } = ObservedHardwareFacts.Empty;
    public FirmwareInventory Firmware { get; init; } = new();
    public ChipsetIdentity Chipset { get; init; } = ChipsetIdentity.Unknown;
    public IReadOnlyList<HardwareDevice> Devices { get; init; } = [];
    public IReadOnlyList<string> Issues { get; init; } = [];
}

/// <summary>One present Plug and Play device. IDs are evidence, not executable paths.</summary>
public sealed record HardwareDevice(string Name, string ClassName, IReadOnlyList<string> HardwareIds);

/// <summary>Firmware identity without serial numbers or UUIDs.</summary>
public sealed record FirmwareInventory
{
    public string? Manufacturer { get; init; }
    public string? Model { get; init; }
    public string? BoardManufacturer { get; init; }
    public string? BoardProduct { get; init; }
    public string? BoardVersion { get; init; }
    public string? BiosVendor { get; init; }
    public string? BiosVersion { get; init; }
    public string? BiosDate { get; init; }
    public IReadOnlyList<int> ChassisTypes { get; init; } = [];
    public IReadOnlyList<MemoryDeviceIdentity> MemoryDevices { get; init; } = [];
}

/// <summary>One firmware-reported memory socket. Null size means unknown; zero means empty.</summary>
public sealed record MemoryDeviceIdentity(string? Locator, ulong? SizeBytes, string? Type, uint? ConfiguredSpeedMt);

/// <summary>Chipset label and the evidence used to identify it.</summary>
public sealed record ChipsetIdentity(string? Name, string Source)
{
    public static ChipsetIdentity Unknown { get; } = new(null, "No specific chipset identity reported");
}

/// <summary>Shared inventory for Home and hardware modules. Refresh repeats only read-only probes.</summary>
public interface IHardwareDetectionService
{
    Task<HardwareSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default);
    Task<HardwareSnapshot> RefreshAsync(CancellationToken cancellationToken = default);
}
