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
                // Name/description are best-effort — a plan with an unreadable
                // name still lists (GUID fallback) rather than failing the scan.
                plans.Add(new PowerPlanInfo(
                    guid,
                    ReadPowerString(PowerReadFriendlyName, guid) ?? $"Power plan {guid:D}",
                    ReadPowerString(PowerReadDescription, guid),
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

    private delegate uint PowerStringReader(
        nint rootPowerKey, in Guid schemeGuid, nint subGroup, nint powerSetting, byte[]? buffer, ref uint bufferSize);

    /// <summary>Two-phase buffer read of a UTF-16 power string; null on any failure or empty value.</summary>
    private static string? ReadPowerString(PowerStringReader reader, Guid schemeGuid)
    {
        uint size = 0;
        if (reader(0, in schemeGuid, 0, 0, null, ref size) != ERROR_SUCCESS || size == 0)
            return null;

        var buffer = new byte[size];
        if (reader(0, in schemeGuid, 0, 0, buffer, ref size) != ERROR_SUCCESS)
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
