using ThisIsMyPC.Core.Modules;

namespace ThisIsMyPC.Core.Services;

/// <summary>
/// Session-scoped capability detection. SKU is read once (EditionID) at construction;
/// full subsystem detection (COM/WMI/DDC/HWiNFO/ATKACPI/OpenRGB) lands in Story 5-1.
/// </summary>
public sealed class CapabilityDetector : ICapabilityDetector
{
    private const string CurrentVersionKeyPath = @"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion";
    private const string EditionValueName = "EditionID";

    private readonly WindowsSku? _sku;
    private readonly string? _skuDetectionFailureReason;

    public CapabilityDetector(IRegistryService registryService)
    {
        ArgumentNullException.ThrowIfNull(registryService);

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

    public bool IsSkuRestricted(WindowsSku? restriction) =>
        restriction is not null && _sku is not null && _sku == restriction;

    public bool IsAvailable(SystemCapability capability) => GetAvailability(capability).IsAvailable;

    public ModuleAvailability GetAvailability(SystemCapability capability) => capability switch
    {
        // The app runs elevated (requireAdministrator); the registry is always reachable.
        SystemCapability.Registry => new ModuleAvailability(true),
        _ => new ModuleAvailability(
            false,
            "Detection for this subsystem is not yet implemented.",
            "Capability detection engine arrives in Story 5-1."),
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
