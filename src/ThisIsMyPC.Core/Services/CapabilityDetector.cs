using ThisIsMyPC.Core.Modules;

namespace ThisIsMyPC.Core.Services;

/// <summary>
/// Session-scoped capability detection (5-1). SKU is read once (EditionID) at
/// construction; subsystem answers are lazy and cached. Always-present subsystems
/// (registry/COM/WMI/native APIs on an elevated Win11 desktop app) report available by
/// rationale, not probe; hardware ecosystems (HWiNFO/ATKACPI/OpenRGB) use cheap
/// presence probes so the first-launch summary can say what would be possible.
/// </summary>
public sealed class CapabilityDetector : ICapabilityDetector
{
    private const string CurrentVersionKeyPath = @"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion";
    private const string EditionValueName = "EditionID";

    private readonly IRegistryService _registryService;
    private readonly Lock _cacheLock = new();
    private readonly Dictionary<SystemCapability, ModuleAvailability> _cache = [];

    private readonly WindowsSku? _sku;
    private readonly string? _skuDetectionFailureReason;

    public CapabilityDetector(IRegistryService registryService)
    {
        ArgumentNullException.ThrowIfNull(registryService);
        _registryService = registryService;

        try
        {
            var read = registryService.ReadString(CurrentVersionKeyPath, EditionValueName);
            if (!read.IsSuccess || read.Value is null)
            {
                _skuDetectionFailureReason = $"EditionID read failed: {read.ErrorMessage ?? "no value"}";
            }
            else
            {
                _sku = MapEdition(read.Value);
                if (_sku is null)
                    _skuDetectionFailureReason = $"Unrecognized EditionID '{read.Value}'";
            }
        }
        catch (Exception ex)
        {
            // IRegistryService implementations must not throw, but a detection failure
            // must never take down DI container resolution.
            _skuDetectionFailureReason = $"EditionID read threw: {ex.Message}";
        }
    }

    public WindowsSku? Sku => _sku;

    public string? SkuDetectionFailureReason => _skuDetectionFailureReason;

    // The Owner Mode service does not exist yet (Epic 28); until it ships, every card
    // requiring it degrades to the disabled-control-with-callout pattern.
    public bool IsOwnerModeAvailable => false;

    public bool IsSkuRestricted(WindowsSku? restriction) =>
        restriction is not null && _sku is not null && _sku == restriction;

    public bool IsAvailable(SystemCapability capability) => GetAvailability(capability).IsAvailable;

    public ModuleAvailability GetAvailability(SystemCapability capability)
    {
        lock (_cacheLock)
        {
            if (!_cache.TryGetValue(capability, out var availability))
            {
                availability = Detect(capability);
                _cache[capability] = availability;
            }
            return availability;
        }
    }

    public IReadOnlyList<CapabilityReportRow> GetCapabilityReport() =>
        Enum.GetValues<SystemCapability>()
            .Select(c => new CapabilityReportRow(c, DisplayNameOf(c), GetAvailability(c)))
            .ToList();

    private ModuleAvailability Detect(SystemCapability capability) => capability switch
    {
        // Always present for an elevated Win11 desktop app — rationale, not probe.
        SystemCapability.Registry => new ModuleAvailability(true),
        SystemCapability.Com => new ModuleAvailability(true),
        SystemCapability.Wmi => new ModuleAvailability(true),
        SystemCapability.NativeApi => new ModuleAvailability(true),

        // Retired hardware territory — reported honestly so the summary can say what
        // would be possible; the modules themselves arrive with the post-BMAD work.
        SystemCapability.DdcCi => new ModuleAvailability(
            false,
            "Display control (DDC/CI) is not implemented yet.",
            "Monitor brightness/input control arrives with the post-BMAD display work."),
        SystemCapability.HwInfo => DetectHwInfo(),
        SystemCapability.AsusAtkacpi => DetectFile(
            Path.Combine(Environment.SystemDirectory, "drivers", "atkwmiacpi64.sys"),
            presentReason: null,
            missingReason: "ASUS ATKACPI driver not detected.",
            remediation: "Present on ASUS boards with Armoury Crate or the ATK drivers installed."),
        SystemCapability.OpenRgb => DetectFile(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OpenRGB"),
            presentReason: null,
            missingReason: "OpenRGB not detected.",
            remediation: "Install OpenRGB and enable its SDK server for RGB control.",
            isDirectory: true),

        _ => new ModuleAvailability(false, $"Unknown capability '{capability}'.", null),
    };

    private ModuleAvailability DetectHwInfo()
    {
        var installed = _registryService.KeyExists(@"HKCU\Software\HWiNFO64") is { IsSuccess: true, Value: true }
            || _registryService.KeyExists(@"HKCU\Software\HWiNFO32") is { IsSuccess: true, Value: true };

        return installed
            ? new ModuleAvailability(
                true,
                Reason: null,
                RemediationHint: "HWiNFO detected. Enable 'Shared Memory Support' in HWiNFO settings for sensor data.")
            : new ModuleAvailability(
                false,
                "HWiNFO shared memory not detected.",
                "Install HWiNFO and enable shared memory for sensor data.");
    }

    private static ModuleAvailability DetectFile(
        string path, string? presentReason, string missingReason, string remediation, bool isDirectory = false)
    {
        try
        {
            var exists = isDirectory ? Directory.Exists(path) : File.Exists(path);
            return exists
                ? new ModuleAvailability(true, presentReason, null)
                : new ModuleAvailability(false, missingReason, remediation);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new ModuleAvailability(false, $"{missingReason} (probe failed: {ex.Message})", remediation);
        }
    }

    private static string DisplayNameOf(SystemCapability capability) => capability switch
    {
        SystemCapability.Registry => "Registry access",
        SystemCapability.Com => "COM services",
        SystemCapability.Wmi => "WMI",
        SystemCapability.NativeApi => "Native power/system APIs",
        SystemCapability.DdcCi => "Monitor control (DDC/CI)",
        SystemCapability.HwInfo => "HWiNFO sensors",
        SystemCapability.AsusAtkacpi => "ASUS platform (ATKACPI)",
        SystemCapability.OpenRgb => "OpenRGB lighting",
        _ => capability.ToString(),
    };

    private static WindowsSku? MapEdition(string editionId) => editionId.ToUpperInvariant() switch
    {
        "CORE" or "COREN" or "CORESINGLELANGUAGE" or "CORECOUNTRYSPECIFIC" => WindowsSku.Home,
        "PROFESSIONAL" or "PROFESSIONALN" or "PROFESSIONALWORKSTATION" => WindowsSku.Pro,
        "ENTERPRISE" or "ENTERPRISEN" or "ENTERPRISES" or "ENTERPRISESN"
            or "IOTENTERPRISE" or "IOTENTERPRISES" => WindowsSku.Enterprise,
        "EDUCATION" or "EDUCATIONN" or "PROFESSIONALEDUCATION" or "PROFESSIONALEDUCATIONN" => WindowsSku.Education,
        _ => null,
    };
}
