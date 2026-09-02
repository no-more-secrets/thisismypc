using System.Runtime.InteropServices;
using System.Text;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;
using static ThisIsMyPC.Interop.Win32.Power.NativePower;

namespace ThisIsMyPC.Interop.Win32.Power;

public sealed class PowerService : IPowerService
{
    public OperationResult<IReadOnlyList<PowerPlanInfo>> EnumeratePlans()
    {
        try
        {
            // The active-plan flag is core to the scan; a failure here fails the
            // whole enumeration rather than mislabeling every plan inactive.
            var activeResult = GetActiveScheme();
            if (!activeResult.IsSuccess)
                return OperationResult<IReadOnlyList<PowerPlanInfo>>.Failure(
                    activeResult.ErrorMessage!, activeResult.ErrorCategory!.Value, activeResult.Exception);
            var active = activeResult.Value;

            var plans = new List<PowerPlanInfo>();
            for (uint index = 0; ; index++)
            {
                var buffer = new byte[16];
                var size = (uint)buffer.Length;
                var result = PowerEnumerate(0, 0, 0, ACCESS_SCHEME, index, buffer, ref size);
                if (result == ERROR_NO_MORE_ITEMS)
                    break;
                if (result != ERROR_SUCCESS)
                    return MapError<IReadOnlyList<PowerPlanInfo>>(result, "enumerate power plans");

                var guid = new Guid(buffer);
                // Name/description are best-effort; a plan with an unreadable
                // name still lists (GUID fallback) rather than failing the scan.
                plans.Add(new PowerPlanInfo(
                    guid,
                    ReadPowerString((byte[]? b, ref uint s) => PowerReadFriendlyName(0, in guid, 0, 0, b, ref s))
                        ?? $"Power plan {guid:D}",
                    ReadPowerString((byte[]? b, ref uint s) => PowerReadDescription(0, in guid, 0, 0, b, ref s)),
                    guid == active));
            }

            return OperationResult<IReadOnlyList<PowerPlanInfo>>.Success(plans);
        }
        catch (Exception ex)
        {
            return OperationResult<IReadOnlyList<PowerPlanInfo>>.Failure(
                $"Unexpected error enumerating power plans: {ex.Message}", ErrorCategory.ServiceUnavailable, ex);
        }
    }

    public OperationResult<bool> SetActivePlan(Guid planGuid)
    {
        try
        {
            var result = PowerSetActiveScheme(0, in planGuid);
            return result == ERROR_SUCCESS
                ? OperationResult<bool>.Success(true)
                : MapError<bool>(result, $"activate power plan {planGuid:D}");
        }
        catch (Exception ex)
        {
            return OperationResult<bool>.Failure(
                $"Unexpected error activating power plan {planGuid:D}: {ex.Message}", ErrorCategory.ServiceUnavailable, ex);
        }
    }

    public OperationResult<IReadOnlyList<PowerSettingInfo>> EnumeratePlanSettings(Guid planGuid)
    {
        try
        {
            var settings = new List<PowerSettingInfo>();
            for (uint subIndex = 0; ; subIndex++)
            {
                var subBuffer = new byte[16];
                var subSize = (uint)subBuffer.Length;
                var subResult = PowerEnumerate(0, in planGuid, 0, ACCESS_SUBGROUP, subIndex, subBuffer, ref subSize);
                if (subResult == ERROR_NO_MORE_ITEMS)
                    break;
                if (subResult != ERROR_SUCCESS)
                    return MapError<IReadOnlyList<PowerSettingInfo>>(subResult, $"enumerate subgroups of plan {planGuid:D}");

                var subgroupGuid = new Guid(subBuffer);
                var subgroupName =
                    ReadPowerString((byte[]? b, ref uint s) => PowerReadFriendlyName(0, in planGuid, in subgroupGuid, 0, b, ref s))
                    ?? $"Subgroup {subgroupGuid:D}";

                for (uint index = 0; ; index++)
                {
                    var buffer = new byte[16];
                    var size = (uint)buffer.Length;
                    var result = PowerEnumerate(0, in planGuid, in subgroupGuid, ACCESS_INDIVIDUAL_SETTING, index, buffer, ref size);
                    if (result == ERROR_NO_MORE_ITEMS)
                        break;
                    if (result != ERROR_SUCCESS)
                        return MapError<IReadOnlyList<PowerSettingInfo>>(
                            result, $"enumerate settings of subgroup {subgroupGuid:D}");

                    settings.Add(ReadSetting(planGuid, subgroupGuid, subgroupName, new Guid(buffer)));
                }
            }

            return OperationResult<IReadOnlyList<PowerSettingInfo>>.Success(settings);
        }
        catch (Exception ex)
        {
            return OperationResult<IReadOnlyList<PowerSettingInfo>>.Failure(
                $"Unexpected error enumerating settings of plan {planGuid:D}: {ex.Message}", ErrorCategory.ServiceUnavailable, ex);
        }
    }

    /// <summary>No stock setting lists more than a dozen choices; the cap only stops a runaway walk.</summary>
    private const uint MaxPossibleValues = 64;

    /// <summary>Everything per-setting is best-effort; an unreadable field folds to null/default, never fails the scan.</summary>
    private static PowerSettingInfo ReadSetting(Guid planGuid, Guid subgroupGuid, string subgroupName, Guid settingGuid)
    {
        var name =
            ReadPowerString((byte[]? b, ref uint s) => PowerReadFriendlyName(0, in planGuid, in subgroupGuid, in settingGuid, b, ref s))
            ?? $"Setting {settingGuid:D}";
        var description =
            ReadPowerString((byte[]? b, ref uint s) => PowerReadDescription(0, in planGuid, in subgroupGuid, in settingGuid, b, ref s));
        var units =
            ReadPowerString((byte[]? b, ref uint s) => PowerReadValueUnitsSpecifier(0, in subgroupGuid, in settingGuid, b, ref s));

        uint? acIndex = PowerReadACValueIndex(0, in planGuid, in subgroupGuid, in settingGuid, out var ac) == ERROR_SUCCESS
            ? ac : null;
        uint? dcIndex = PowerReadDCValueIndex(0, in planGuid, in subgroupGuid, in settingGuid, out var dc) == ERROR_SUCCESS
            ? dc : null;

        // PowerIsSettingRangeDefined answers false for plain range settings
        // ("Turn off hard disk after") on Windows 11 26200, and for those
        // PowerReadPossibleFriendlyName returns the setting's own name at
        // every index, so an unbounded walk never ends. A readable Min and
        // Max is the reliable sign of a range (enumerated settings answer
        // ERROR_FILE_NOT_FOUND to both).
        var hasMin = PowerReadValueMin(0, in subgroupGuid, in settingGuid, out var readMin) == ERROR_SUCCESS;
        var hasMax = PowerReadValueMax(0, in subgroupGuid, in settingGuid, out var readMax) == ERROR_SUCCESS;
        var isRange = (hasMin && hasMax) || PowerIsSettingRangeDefined(0, in subgroupGuid, in settingGuid);
        uint min = 0, max = 0, increment = 1;
        var possibleValues = new List<PowerPossibleValue>();
        if (isRange)
        {
            if (hasMin)
                min = readMin;
            if (hasMax)
                max = readMax;
            if (PowerReadValueIncrement(0, in subgroupGuid, in settingGuid, out var readIncrement) == ERROR_SUCCESS
                && readIncrement > 0)
            {
                increment = readIncrement;
            }
        }
        else
        {
            // Bounded, and a repeated label ends the walk: both guard against
            // an API that keeps answering.
            for (uint possibleIndex = 0; possibleIndex < MaxPossibleValues; possibleIndex++)
            {
                var label = ReadPowerString(
                    (byte[]? b, ref uint s) => PowerReadPossibleFriendlyName(0, in subgroupGuid, in settingGuid, possibleIndex, b, ref s));
                if (label is null || (possibleValues.Count > 0 && possibleValues[^1].Name == label))
                    break;
                possibleValues.Add(new PowerPossibleValue(possibleIndex, label));
            }
        }

        return new PowerSettingInfo(
            subgroupGuid, subgroupName, settingGuid, name, description,
            acIndex, dcIndex, units, isRange, min, max, increment, possibleValues);
    }

    public OperationResult<bool> WriteSettingIndex(Guid planGuid, Guid subgroupGuid, Guid settingGuid, bool ac, uint valueIndex)
    {
        try
        {
            var result = ac
                ? PowerWriteACValueIndex(0, in planGuid, in subgroupGuid, in settingGuid, valueIndex)
                : PowerWriteDCValueIndex(0, in planGuid, in subgroupGuid, in settingGuid, valueIndex);
            if (result != ERROR_SUCCESS)
                return MapError<bool>(result, $"write setting {settingGuid:D} of plan {planGuid:D}");

            // Writes to the active scheme only take effect after re-activation
            var activeResult = GetActiveScheme();
            if (activeResult.IsSuccess && activeResult.Value == planGuid)
            {
                var reapply = PowerSetActiveScheme(0, in planGuid);
                if (reapply != ERROR_SUCCESS)
                    return MapError<bool>(reapply, $"re-activate plan {planGuid:D} after the setting write");
            }

            return OperationResult<bool>.Success(true);
        }
        catch (Exception ex)
        {
            return OperationResult<bool>.Failure(
                $"Unexpected error writing setting {settingGuid:D}: {ex.Message}", ErrorCategory.ServiceUnavailable, ex);
        }
    }

    public bool SupportsModernStandby()
    {
        try
        {
            return GetPwrCapabilities(out var capabilities) && capabilities.AoAc != 0;
        }
        catch
        {
            return false;
        }
    }

    public OperationResult<bool> SetHibernateEnabled(bool enable)
    {
        try
        {
            byte input = enable ? (byte)1 : (byte)0;
            var status = CallNtPowerInformation(SystemReserveHiberFile, ref input, sizeof(byte), 0, 0);
            return status == 0
                ? OperationResult<bool>.Success(true)
                : OperationResult<bool>.Failure(
                    $"Cannot {(enable ? "enable" : "disable")} hibernation: NTSTATUS 0x{status:X8}.",
                    status == 0xC0000022 ? ErrorCategory.AccessDenied : ErrorCategory.ServiceUnavailable);
        }
        catch (Exception ex)
        {
            return OperationResult<bool>.Failure(
                $"Unexpected error toggling hibernation: {ex.Message}", ErrorCategory.ServiceUnavailable, ex);
        }
    }

    public OperationResult<Guid> DuplicateScheme(Guid sourceSchemeGuid)
    {
        try
        {
            var result = PowerDuplicateScheme(0, in sourceSchemeGuid, out var guidPtr);
            if (result != ERROR_SUCCESS)
                return MapError<Guid>(result, $"duplicate power plan {sourceSchemeGuid:D}");
            try
            {
                return OperationResult<Guid>.Success(Marshal.PtrToStructure<Guid>(guidPtr));
            }
            finally
            {
                LocalFree(guidPtr);
            }
        }
        catch (Exception ex)
        {
            return OperationResult<Guid>.Failure(
                $"Unexpected error duplicating power plan {sourceSchemeGuid:D}: {ex.Message}",
                ErrorCategory.ServiceUnavailable, ex);
        }
    }

    public unsafe OperationResult<Guid> DuplicateSchemeAs(Guid sourceSchemeGuid, Guid destinationSchemeGuid)
    {
        try
        {
            var destination = destinationSchemeGuid;
            var destinationPtr = (nint)(&destination);
            var result = PowerDuplicateSchemeTo(0, in sourceSchemeGuid, ref destinationPtr);
            return result != ERROR_SUCCESS
                ? MapError<Guid>(result, $"recreate power plan {destinationSchemeGuid:D}")
                : OperationResult<Guid>.Success(destinationSchemeGuid);
        }
        catch (Exception ex)
        {
            return OperationResult<Guid>.Failure(
                $"Unexpected error recreating power plan {destinationSchemeGuid:D}: {ex.Message}",
                ErrorCategory.ServiceUnavailable, ex);
        }
    }

    public OperationResult<bool> RestoreDefaultScheme(Guid schemeGuid)
    {
        try
        {
            var result = PowerRestoreIndividualDefaultPowerScheme(in schemeGuid);
            return result == ERROR_SUCCESS
                ? OperationResult<bool>.Success(true)
                : MapError<bool>(result, $"restore power plan {schemeGuid:D} from Windows' defaults");
        }
        catch (Exception ex)
        {
            return OperationResult<bool>.Failure(
                $"Unexpected error restoring power plan {schemeGuid:D}: {ex.Message}",
                ErrorCategory.ServiceUnavailable, ex);
        }
    }

    public OperationResult<bool> DeleteScheme(Guid schemeGuid)
    {
        try
        {
            var result = PowerDeleteScheme(0, in schemeGuid);
            return result == ERROR_SUCCESS
                ? OperationResult<bool>.Success(true)
                : MapError<bool>(result, $"delete power plan {schemeGuid:D}");
        }
        catch (Exception ex)
        {
            return OperationResult<bool>.Failure(
                $"Unexpected error deleting power plan {schemeGuid:D}: {ex.Message}",
                ErrorCategory.ServiceUnavailable, ex);
        }
    }

    public OperationResult<bool> WriteSchemeText(Guid schemeGuid, string name, string description)
    {
        try
        {
            var nameBytes = Encoding.Unicode.GetBytes(name + '\0');
            var result = PowerWriteFriendlyName(0, in schemeGuid, 0, 0, nameBytes, (uint)nameBytes.Length);
            if (result != ERROR_SUCCESS)
                return MapError<bool>(result, $"rename power plan {schemeGuid:D}");

            var descriptionBytes = Encoding.Unicode.GetBytes(description + '\0');
            result = PowerWriteDescription(0, in schemeGuid, 0, 0, descriptionBytes, (uint)descriptionBytes.Length);
            return result == ERROR_SUCCESS
                ? OperationResult<bool>.Success(true)
                : MapError<bool>(result, $"set the description of power plan {schemeGuid:D}");
        }
        catch (Exception ex)
        {
            return OperationResult<bool>.Failure(
                $"Unexpected error writing text of power plan {schemeGuid:D}: {ex.Message}",
                ErrorCategory.ServiceUnavailable, ex);
        }
    }

    private static OperationResult<Guid> GetActiveScheme()
    {
        var result = PowerGetActiveScheme(0, out var guidPtr);
        if (result != ERROR_SUCCESS)
            return MapError<Guid>(result, "read the active power plan");
        try
        {
            return OperationResult<Guid>.Success(Marshal.PtrToStructure<Guid>(guidPtr));
        }
        finally
        {
            LocalFree(guidPtr);
        }
    }

    private delegate uint BufferedRead(byte[]? buffer, ref uint bufferSize);

    /// <summary>Two-phase buffer read of a UTF-16 power string; null on any failure or empty value.</summary>
    private static string? ReadPowerString(BufferedRead read)
    {
        uint size = 0;
        if (read(null, ref size) != ERROR_SUCCESS || size == 0)
            return null;

        var buffer = new byte[size];
        if (read(buffer, ref size) != ERROR_SUCCESS)
            return null;

        var text = Encoding.Unicode.GetString(buffer, 0, (int)Math.Min(size, (uint)buffer.Length)).TrimEnd('\0');
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private static OperationResult<T> MapError<T>(uint win32Error, string verb)
    {
        (string message, ErrorCategory category) = win32Error switch
        {
            ERROR_FILE_NOT_FOUND => (
                $"Cannot {verb}: no power plan with that GUID is registered.",
                ErrorCategory.NotFound),
            ERROR_ACCESS_DENIED => (
                $"Cannot {verb}: access denied by Windows (power policy may be locked by group policy).",
                ErrorCategory.AccessDenied),
            _ => (
                $"Cannot {verb}: Win32 error {win32Error}.",
                ErrorCategory.ServiceUnavailable),
        };
        return OperationResult<T>.Failure(message, category);
    }
}
