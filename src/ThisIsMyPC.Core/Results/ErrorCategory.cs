namespace ThisIsMyPC.Core.Results;

public enum ErrorCategory
{
    AccessDenied,
    NotFound,
    ServiceUnavailable,
    ProtectedByPolicy,
    RequiresRestart,
    HardwareNotPresent,
    EnforcementBlocked,
    SkuRestricted,
    OwnerModeRequired
}
