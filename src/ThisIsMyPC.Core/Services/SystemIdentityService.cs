using System.Runtime.InteropServices;

namespace ThisIsMyPC.Core.Services;

/// <summary>Cheap system identity for the Home tab (10.5). Every field best-effort.</summary>
public sealed record SystemIdentity
{
    public required string MachineName { get; init; }
    public required string WindowsEdition { get; init; }
    public required string WindowsVersion { get; init; }
    public required string Cpu { get; init; }
    public required string Gpu { get; init; }
    public required string Ram { get; init; }
    public required string Manufacturer { get; init; }
    public required string Model { get; init; }
    public required string SystemType { get; init; }
}

/// <summary>Provides installed physical memory without coupling Core to a native API.</summary>
public interface IInstalledMemoryProvider
{
    /// <summary>Gets installed physical memory in bytes, or null when unavailable.</summary>
    ulong? GetInstalledMemoryBytes();
}

/// <summary>Provides display adapters that Windows currently attaches to the desktop.</summary>
public interface IGpuIdentityProvider
{
    /// <summary>Gets current display adapter names.</summary>
    IReadOnlyList<string> GetCurrentAdapterNames();
}

/// <summary>
/// Reads system identity from cheap sources only; registry and BCL, never WMI
/// (banned in Core) and never anything slow enough to delay first paint.
/// </summary>
public sealed class SystemIdentityService
{
    private const string Unknown = "Unknown";
    private const string CurrentVersionKeyPath = @"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion";
    private const string ProcessorKeyPath = @"HKLM\HARDWARE\DESCRIPTION\System\CentralProcessor\0";
    private const string BiosKeyPath = @"HKLM\HARDWARE\DESCRIPTION\System\BIOS";
    private readonly IRegistryService _registry;
    private readonly IInstalledMemoryProvider? _memoryProvider;
    private readonly IGpuIdentityProvider? _gpuProvider;

    public SystemIdentityService(
        IRegistryService registry,
        IInstalledMemoryProvider? memoryProvider = null,
        IGpuIdentityProvider? gpuProvider = null)
    {
        ArgumentNullException.ThrowIfNull(registry);
        _registry = registry;
        _memoryProvider = memoryProvider;
        _gpuProvider = gpuProvider;
    }

    public SystemIdentity Read()
    {
        var edition = CorrectWindowsProductName(
            ReadString(CurrentVersionKeyPath, "ProductName"),
            ReadDWord(CurrentVersionKeyPath, "CurrentMajorVersionNumber"),
            ReadBuildNumber(CurrentVersionKeyPath));
        var displayVersion = ReadString(CurrentVersionKeyPath, "DisplayVersion");
        var build = ReadString(CurrentVersionKeyPath, "CurrentBuildNumber");
        var revision = ReadDWord(CurrentVersionKeyPath, "UBR");

        var version = (displayVersion, build) switch
        {
            (not null, not null) => $"{displayVersion} (OS build {build}{FormatRevision(revision)})",
            (not null, null) => displayVersion,
            (null, not null) => $"OS build {build}{FormatRevision(revision)}",
            _ => Unknown,
        };

        return new SystemIdentity
        {
            MachineName = SafeMachineName(),
            WindowsEdition = edition ?? Unknown,
            WindowsVersion = version,
            Cpu = ReadString(ProcessorKeyPath, "ProcessorNameString")?.Trim() ?? Unknown,
            Gpu = ReadDisplayAdapters(),
            Ram = ReadRam(),
            Manufacturer = ReadString(BiosKeyPath, "SystemManufacturer") ?? Unknown,
            Model = ReadString(BiosKeyPath, "SystemProductName") ?? Unknown,
            SystemType = FormatSystemType(),
        };
    }

    private static string FormatRevision(int? revision) => revision is > 0 ? $".{revision}" : string.Empty;

    private int? ReadBuildNumber(string keyPath)
        => int.TryParse(ReadString(keyPath, "CurrentBuildNumber"), out var build) ? build : null;

    private static string CorrectWindowsProductName(string? productName, int? majorVersion, int? build)
    {
        if (string.IsNullOrWhiteSpace(productName))
            return Unknown;

        return majorVersion >= 10 && build >= 22000
            ? productName.Replace("Windows 10", "Windows 11", StringComparison.OrdinalIgnoreCase)
            : productName;
    }

    private int? ReadDWord(string keyPath, string valueName)
    {
        try
        {
            var result = _registry.ReadDWord(keyPath, valueName);
            return result.IsSuccess ? result.Value : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private string ReadDisplayAdapters()
    {
        try
        {
            var adapters = (_gpuProvider?.GetCurrentAdapterNames() ?? [])
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            return adapters.Count == 0 ? Unknown : string.Join("; ", adapters);
        }
        catch (Exception)
        {
            return Unknown;
        }
    }

    private static string FormatSystemType()
    {
        var architecture = RuntimeInformation.OSArchitecture switch
        {
            Architecture.Arm64 => "ARM64-based processor",
            Architecture.X64 => "x64-based processor",
            Architecture.X86 => "x86-based processor",
            _ => "unknown processor architecture",
        };
        var operatingSystem = Environment.Is64BitOperatingSystem ? "64-bit operating system" : "32-bit operating system";
        return $"{operatingSystem}, {architecture}";
    }

    private string? ReadString(string keyPath, string valueName)
    {
        try
        {
            var result = _registry.ReadString(keyPath, valueName);
            return result.IsSuccess && !string.IsNullOrWhiteSpace(result.Value) ? result.Value : null;
        }
        catch (Exception)
        {
            return null; // identity is decorative; a throwing registry layer must not break Home
        }
    }

    private static string SafeMachineName()
    {
        try
        {
            return Environment.MachineName;
        }
        catch (InvalidOperationException)
        {
            return Unknown;
        }
    }

    private string ReadRam()
    {
        try
        {
            var bytes = _memoryProvider?.GetInstalledMemoryBytes();
            if (bytes is null or 0)
                return Unknown;

            // Windows reports kilobytes from the firmware table. Round to the
            // whole-gigabyte figure shown on the System About page.
            var gb = Math.Round(bytes.Value / 1024d / 1024d / 1024d);
            return $"{gb:0} GB";
        }
        catch (Exception)
        {
            return Unknown;
        }
    }
}
