using ThisIsMyPC.Core.Results;

namespace ThisIsMyPC.App.Helpers;

public static class ErrorCategoryExtensions
{
    public static string ToGuidance(ErrorCategory category) => category switch
    {
        ErrorCategory.AccessDenied => "Access denied. This key is protected by Windows (owned by TrustedInstaller).",
        ErrorCategory.NotFound => "The target setting or key was not found. It may have been removed.",
        ErrorCategory.ServiceUnavailable => "The required service is not available.",
        ErrorCategory.ProtectedByPolicy => "This setting is locked by Group Policy.",
        ErrorCategory.RequiresRestart => "A system restart is required to complete this change.",
        ErrorCategory.HardwareNotPresent => "The required hardware was not detected.",
        _ => "An unexpected error occurred.",
    };
}
