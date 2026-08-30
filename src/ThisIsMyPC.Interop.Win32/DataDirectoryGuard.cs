using Serilog;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Interop.Win32.Security;

namespace ThisIsMyPC.Interop.Win32;

public sealed class DataDirectoryGuard : IDataDirectoryGuard
{
    // S-1-5-32-544 = BUILTIN\Administrators
    // S-1-5-18     = NT AUTHORITY\SYSTEM
    private const string AdministratorsSid = "S-1-5-32-544";
    private const string SystemSid = "S-1-5-18";

    private const uint FileAllAccess = 0x1F01FF;
    private const uint SubContainersAndObjectsInherit = 0x03; // CONTAINER_INHERIT_ACE | OBJECT_INHERIT_ACE

    private static readonly DaclAccessEntry[] RequiredEntries =
    [
        new("BUILTIN\\Administrators", FileAllAccess, SubContainersAndObjectsInherit),
        new("NT AUTHORITY\\SYSTEM", FileAllAccess, SubContainersAndObjectsInherit),
    ];

    private static readonly HashSet<string> ExpectedSids = [AdministratorsSid, SystemSid];

    private readonly ISecurityApi _securityApi;
    private readonly ILogger _logger;

    public DataDirectoryGuard(ISecurityApi? securityApi = null, ILogger? logger = null)
    {
        _securityApi = securityApi ?? new SecurityApi();
        _logger = logger ?? Log.Logger;
    }

    public OperationResult<DaclStatus> EnsureHardened(string directoryPath)
    {
        try
        {
            var daclInfo = _securityApi.ReadDacl(directoryPath);
            bool isFirstTime = daclInfo.Error is not null || !daclInfo.IsProtected;

            if (VerifyDacl(daclInfo, directoryPath))
            {
                _logger.Debug("Data directory DACL verified: {Path}", directoryPath);
                return OperationResult<DaclStatus>.Success(DaclStatus.Verified);
            }

            _logger.Information("Data directory DACL requires update, applying: {Path}", directoryPath);
            return ApplyDacl(directoryPath, isFirstTime);
        }
#pragma warning disable CA1031 // Guard must not crash the app; log and return failure
        catch (Exception ex)
#pragma warning restore CA1031
        {
            _logger.Error(ex, "DACL hardening failed for {Path}", directoryPath);
            return OperationResult<DaclStatus>.Failure(
                $"DACL hardening failed: {ex.Message}",
                ErrorCategory.AccessDenied,
                ex);
        }
    }

    private bool VerifyDacl(DaclInfo info, string directoryPath)
    {
        if (info.Error is not null)
        {
            _logger.Warning("Failed to read DACL for verification: {Error}", info.Error);
            return false;
        }

        if (!info.IsProtected)
        {
            _logger.Warning("DACL inheritance is not disabled on {Path}", directoryPath);
            return false;
        }

        if (info.Entries.Count != 2)
        {
            _logger.Warning("Expected 2 ACEs, found {Count} on {Path}", info.Entries.Count, directoryPath);
            return false;
        }

        foreach (var ace in info.Entries)
        {
            if (!ExpectedSids.Contains(ace.Sid))
            {
                _logger.Warning("Unexpected ACE SID {Sid} on {Path}", ace.Sid, directoryPath);
                return false;
            }

            if (ace.AccessMask != FileAllAccess)
            {
                _logger.Warning("Unexpected access mask {Mask} for SID {Sid} on {Path}",
                    ace.AccessMask, ace.Sid, directoryPath);
                return false;
            }
        }

        var foundSids = new HashSet<string>(info.Entries.Select(e => e.Sid));
        if (!ExpectedSids.SetEquals(foundSids))
        {
            _logger.Warning("Missing expected SIDs on {Path}", directoryPath);
            return false;
        }

        return true;
    }

    private OperationResult<DaclStatus> ApplyDacl(string directoryPath, bool isFirstTime)
    {
        uint error = _securityApi.ApplyDacl(directoryPath, RequiredEntries, disableInheritance: true);

        if (error != 0)
        {
            _logger.Error("SetDacl failed with Win32 error {ErrorCode} on {Path}", error, directoryPath);
            return OperationResult<DaclStatus>.Failure(
                $"Failed to apply DACL (Win32 error {error})",
                ErrorCategory.AccessDenied);
        }

        var status = isFirstTime ? DaclStatus.Created : DaclStatus.Repaired;
        _logger.Information("DACL {Status} successfully on {Path}", status, directoryPath);
        return OperationResult<DaclStatus>.Success(status);
    }
}
