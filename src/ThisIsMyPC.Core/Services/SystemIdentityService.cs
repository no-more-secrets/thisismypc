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
}

/// <summary>
/// Reads system identity from cheap sources only — registry and BCL, never WMI
/// (banned in Core) and never anything slow enough to delay first paint.
/// </summary>
public sealed class SystemIdentityService
{
    private const string Unknown = "Unknown";
    private const string CurrentVersionKeyPath = @"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion";
    private const string ProcessorKeyPath = @"HKLM\HARDWARE\DESCRIPTION\System\CentralProcessor\0";
    private const string DisplayAdapterKeyPath =
        @"HKLM\SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}\0000";

    private readonly IRegistryService _registry;

    public SystemIdentityService(IRegistryService registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        _registry = registry;
    }

    public SystemIdentity Read()
    {
        var edition = ReadString(CurrentVersionKeyPath, "ProductName");
        var displayVersion = ReadString(CurrentVersionKeyPath, "DisplayVersion");
        var build = ReadString(CurrentVersionKeyPath, "CurrentBuildNumber");

        var version = (displayVersion, build) switch
        {
            (not null, not null) => $"{displayVersion} (build {build})",
            (not null, null) => displayVersion,
            (null, not null) => $"build {build}",
            _ => Unknown,
        };

        return new SystemIdentity
        {
            MachineName = SafeMachineName(),
            WindowsEdition = edition ?? Unknown,
            WindowsVersion = version,
            Cpu = ReadString(ProcessorKeyPath, "ProcessorNameString")?.Trim() ?? Unknown,
            Gpu = ReadString(DisplayAdapterKeyPath, "DriverDesc") ?? Unknown,
            Ram = ReadRam(),
        };
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
            return null; // identity is decorative — a throwing registry layer must not break Home
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

    private static string ReadRam()
    {
        try
        {
            var bytes = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
            if (bytes <= 0)
                return Unknown;

            // Available-to-process memory sits just under the installed amount;
            // rounding to the nearest whole GB lands on the marketing figure.
            var gb = Math.Round(bytes / 1024d / 1024d / 1024d);
            return $"{gb:0} GB";
        }
        catch (Exception)
        {
            return Unknown;
        }
    }
}
