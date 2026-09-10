using System.Buffers.Binary;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using ThisIsMyPC.Core.Hardware;
using ThisIsMyPC.Core.Services;

namespace ThisIsMyPC.Interop.Win32.Hardware;

/// <summary>Read-only firmware and present-device inventory. No WMI, driver loading, network access, or device writes.</summary>
public sealed partial class HardwareDetectionService(IRegistryService registry) : IHardwareDetectionService
{
    private readonly Lock _gate = new();
    private Task<HardwareSnapshot>? _scan;

    public Task<HardwareSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate) return (_scan ??= Task.Run(Scan)).WaitAsync(cancellationToken);
    }

    public Task<HardwareSnapshot> RefreshAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (_scan is null || _scan.IsCompleted) _scan = Task.Run(Scan);
            return _scan.WaitAsync(cancellationToken);
        }
    }

    private HardwareSnapshot Scan()
    {
        var issues = new List<string>();
        var firmware = new FirmwareInventory();
        try { firmware = ReadFirmware(); }
        catch (Exception ex) when (ex is Win32Exception or InvalidDataException) { issues.Add("Firmware: " + ex.Message); }
        const string bios = @"HKLM\HARDWARE\DESCRIPTION\System\BIOS";
        firmware = firmware with
        {
            Manufacturer = firmware.Manufacturer ?? ReadText(bios, "SystemManufacturer"),
            Model = firmware.Model ?? ReadText(bios, "SystemProductName"),
            BoardManufacturer = firmware.BoardManufacturer ?? ReadText(bios, "BaseBoardManufacturer"),
            BoardProduct = firmware.BoardProduct ?? ReadText(bios, "BaseBoardProduct"),
            BoardVersion = firmware.BoardVersion ?? ReadText(bios, "BaseBoardVersion"),
            BiosVendor = firmware.BiosVendor ?? ReadText(bios, "BIOSVendor"),
            BiosVersion = firmware.BiosVersion ?? ReadText(bios, "BIOSVersion"),
            BiosDate = firmware.BiosDate ?? ReadText(bios, "BIOSReleaseDate"),
        };
        var devices = ReadDevices(issues);
        bool? battery = null;
        if (GetSystemPowerStatus(out var power) != 0 && power.BatteryFlag != 255)
            battery = (power.BatteryFlag & 128) == 0;
        var role = PowerDeterminePlatformRoleEx(2);
        var facts = new ObservedHardwareFacts
        {
            Identity = MachineIdentity.From(firmware.Manufacturer, firmware.Model),
            FormFactor = new()
            {
                SmbiosChassisTypes = firmware.ChassisTypes,
                PlatformRole = role <= 8 ? (PlatformRole)role : null,
                HasSystemBattery = battery,
            },
            AsusPlatformDriverPresent = QueryAsusInterface(),
            Companions = ReadCompanions(issues),
        };
        return new()
        {
            ObservedAt = DateTimeOffset.UtcNow, Facts = facts, Firmware = firmware,
            Chipset = ChipsetIdentityResolver.Resolve(firmware.BoardProduct, devices),
            Devices = devices, Issues = issues.AsReadOnly(),
        };
    }

    private string? ReadText(string path, string name)
    {
        var result = registry.ReadString(path, name);
        return result.IsSuccess ? MachineIdentity.From(result.Value, null).Manufacturer : null;
    }

    private static FirmwareInventory ReadFirmware()
    {
        const uint rsmb = 0x52534d42;
        var size = GetSystemFirmwareTable(rsmb, 0, null, 0);
        if (size == 0) throw new Win32Exception(Marshal.GetLastPInvokeError());
        if (size is < 8 or > 16 * 1024 * 1024) throw new InvalidDataException("Firmware table size is invalid.");
        var buffer = new byte[size];
        var written = GetSystemFirmwareTable(rsmb, 0, buffer, size);
        if (written != size) throw new InvalidDataException("Firmware table changed during the read.");
        var tableLength = BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(4));
        if (tableLength > size - 8) throw new InvalidDataException("Firmware table length is invalid.");
        return SmbiosInventoryParser.Parse(buffer.AsSpan(8, (int)tableLength));
    }

    private static IReadOnlyList<HardwareDevice> ReadDevices(List<string> issues)
    {
        var set = SetupDiGetClassDevsW(0, null, 0, 6); // PRESENT | ALLCLASSES, excludes stale registry entries.
        if (set == -1) { issues.Add("Present devices could not be enumerated."); return []; }
        var devices = new List<HardwareDevice>();
        try
        {
            for (uint index = 0; index < 8192; index++)
            {
                var data = new DeviceInfoData { Size = (uint)Marshal.SizeOf<DeviceInfoData>() };
                if (SetupDiEnumDeviceInfo(set, index, ref data) == 0)
                {
                    if (Marshal.GetLastPInvokeError() != 259) issues.Add("Present device enumeration was incomplete.");
                    return devices.AsReadOnly();
                }
                var name = Property(set, ref data, 12).FirstOrDefault() ?? Property(set, ref data, 0).FirstOrDefault();
                var className = Property(set, ref data, 7).FirstOrDefault() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(name))
                    devices.Add(new(name, className, Array.AsReadOnly(Property(set, ref data, 1))));
            }
            issues.Add("Present device enumeration reached its limit.");
            return devices.AsReadOnly();
        }
        finally { _ = SetupDiDestroyDeviceInfoList(set); }
    }

    private static string[] Property(nint set, ref DeviceInfoData data, uint property)
    {
        _ = SetupDiGetDeviceRegistryPropertyW(set, ref data, property, out _, null, 0, out var required);
        if (required is 0 or > 65536 || (required & 1) != 0) return [];
        var bytes = new byte[required];
        if (SetupDiGetDeviceRegistryPropertyW(set, ref data, property, out var type, bytes, required, out var actual) == 0
            || actual > required || (actual & 1) != 0 || type is not (1 or 7)) return [];
        return Encoding.Unicode.GetString(bytes, 0, (int)actual).Split('\0', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static bool? QueryAsusInterface()
    {
        var buffer = new char[4096];
        if (QueryDosDeviceW("ATKACPI", buffer, (uint)buffer.Length) != 0) return true;
        return Marshal.GetLastPInvokeError() is 2 or 3 ? false : null;
    }

    private IReadOnlyList<CompanionObservation> ReadCompanions(List<string> issues)
    {
        var installed = new HashSet<CompanionApp>();
        var running = new HashSet<CompanionApp>();
        foreach (var root in new[] { @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", @"HKLM\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall", @"HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall" })
        {
            var children = registry.EnumerateSubKeys(root);
            if (!children.IsSuccess || children.Value is null) continue;
            foreach (var child in children.Value.Take(4096))
                if (IdentifyCompanion(ReadText(root + "\\" + child, "DisplayName")) is { } app) installed.Add(app);
        }
        try
        {
            foreach (var process in Process.GetProcesses())
            {
                using (process)
                {
                    try { if (IdentifyCompanion(process.ProcessName) is { } app) running.Add(app); }
                    catch (Exception ex) when (ex is InvalidOperationException or Win32Exception) { /* Process exited or is protected. */ }
                }
            }
        }
        catch (Win32Exception) { issues.Add("Running companion applications could not be checked."); }
        // No hit is unknown: portable apps may be anywhere. Process names do not establish device ownership.
        return installed.Union(running).Select(app => new CompanionObservation(app, true, running.Contains(app))).ToArray();
    }

    private static CompanionApp? IdentifyCompanion(string? name)
    {
        if (name is null) return null;
        if (name.Equals("GHelper", StringComparison.OrdinalIgnoreCase) || name.Equals("G-Helper", StringComparison.OrdinalIgnoreCase)) return CompanionApp.GHelper;
        if (name.Equals("FanControl", StringComparison.OrdinalIgnoreCase)) return CompanionApp.FanControl;
        if (name.Equals("OpenRGB", StringComparison.OrdinalIgnoreCase)) return CompanionApp.OpenRgb;
        if (name.Equals("SignalRgb", StringComparison.OrdinalIgnoreCase) || name.Equals("SignalRgbLauncher", StringComparison.OrdinalIgnoreCase)) return CompanionApp.SignalRgb;
        if (name.Equals("LibreHardwareMonitor", StringComparison.OrdinalIgnoreCase)) return CompanionApp.LibreHardwareMonitor;
        if (name.StartsWith("HWiNFO64", StringComparison.OrdinalIgnoreCase) || name.StartsWith("HWiNFO32", StringComparison.OrdinalIgnoreCase)) return CompanionApp.HwInfo;
        if (name.StartsWith("ArmouryCrate", StringComparison.OrdinalIgnoreCase) || name.Equals("ARMOURY CRATE", StringComparison.OrdinalIgnoreCase)) return CompanionApp.ArmouryCrate;
        return null;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DeviceInfoData { public uint Size; public Guid ClassGuid; public uint DevInst; public nuint Reserved; }
    [StructLayout(LayoutKind.Sequential)]
    private struct PowerStatus { public byte AcLineStatus, BatteryFlag, BatteryLifePercent, SystemStatusFlag; public uint BatteryLifeTime, BatteryFullLifeTime; }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial uint GetSystemFirmwareTable(uint provider, uint table, [Out] byte[]? buffer, uint size);
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int GetSystemPowerStatus(out PowerStatus status);
    [LibraryImport("powrprof.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial uint PowerDeterminePlatformRoleEx(uint version);
    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial uint QueryDosDeviceW(string name, [Out] char[] target, uint maximum);
    [LibraryImport("setupapi.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint SetupDiGetClassDevsW(nint classGuid, string? enumerator, nint parent, uint flags);
    [LibraryImport("setupapi.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int SetupDiEnumDeviceInfo(nint set, uint index, ref DeviceInfoData data);
    [LibraryImport("setupapi.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int SetupDiGetDeviceRegistryPropertyW(nint set, ref DeviceInfoData data, uint property, out uint type, [Out] byte[]? buffer, uint size, out uint required);
    [LibraryImport("setupapi.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int SetupDiDestroyDeviceInfoList(nint set);
}
