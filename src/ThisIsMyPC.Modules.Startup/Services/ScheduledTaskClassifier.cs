using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Modules.Startup.Models;

namespace ThisIsMyPC.Modules.Startup.Services;

/// <summary>
/// Path-, name-, and author-based classification heuristics for scheduled
/// tasks. Deliberately conservative: anything unmatched stays Unknown rather
/// than guessing.
/// </summary>
public static class ScheduledTaskClassifier
{
    private static readonly string[] TelemetryMarkers =
    [
        @"\Customer Experience Improvement Program",
        "Consolidator",
        "UsbCeip",
        "KernelCeipTask",
        @"\Feedback\Siuf",
        @"\Windows Error Reporting",
        @"\DiskDiagnosticDataCollector",
        @"\Device Information\Device",
        @"\Flighting\",
    ];

    private static readonly string[] CompatibilityMarkers =
    [
        @"\Application Experience",
        "Microsoft Compatibility Appraiser",
        "PcaPatchDbTask",
        "StartupAppTask",
        "MareBackup",
        @"\Compatibility\",
    ];

    private static readonly string[] MaintenanceMarkers =
    [
        @"\Defrag\",
        "ScheduledDefrag",
        "SilentCleanup",
        @"\Servicing\",
        @"\DiskCleanup\",
        @"\.NET Framework\",
        @"\Windows Defender\",
        @"\Chkdsk\",
        @"\Maintenance\",
        "WinSAT",
    ];

    // Short bare names are kept delimited or suffixed to avoid substring false
    // positives ("Pegasus" ⊃ ASUS, "Racer" ⊃ Acer, "Dells…" ⊃ Dell).
    private static readonly string[] OemVendors =
    [
        "Hewlett-Packard", "HP Inc", @"\HP\",
        "ASUSTeK", @"\ASUS\", "ASUS Cloud",
        "Dell Inc", @"\Dell\", "Dell Technologies",
        "Lenovo",
        "Acer Inc", @"\Acer\", "Acer Cloud",
        "Micro-Star", @"\MSI\",
        "GIGABYTE",
        "Razer Inc", @"\Razer\",
        "Samsung Electronics",
    ];

    /// <summary>Known enforcement companion tasks: name marker → mechanism description.</summary>
    private static readonly (string Marker, string Description)[] CompanionTasks =
    [
        ("UCPD velocity", "Supports the UCPD (User Choice Protection Driver) mechanism that re-protects default-app and browser associations. Disabling the companion task is part of default-app enforcement."),
    ];

    public static TaskClassification Classify(ScheduledTaskInfo task)
    {
        if (Matches(task, TelemetryMarkers))
            return TaskClassification.Telemetry;
        if (Matches(task, CompatibilityMarkers))
            return TaskClassification.CompatibilityDiagnostics;
        if (MatchesOem(task))
            return TaskClassification.Oem;
        if (Matches(task, MaintenanceMarkers))
            return TaskClassification.Maintenance;
        if (IsUserCreated(task))
            return TaskClassification.UserCreated;
        return TaskClassification.Unknown;
    }

    public static (bool IsCompanion, string? Description) GetCompanionInfo(ScheduledTaskInfo task)
    {
        foreach (var (marker, description) in CompanionTasks)
        {
            if (task.Name.Contains(marker, StringComparison.OrdinalIgnoreCase) ||
                task.Path.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                return (true, description);
            }
        }
        return (false, null);
    }

    private static bool Matches(ScheduledTaskInfo task, string[] markers)
        => markers.Any(m => task.Path.Contains(m, StringComparison.OrdinalIgnoreCase));

    private static bool MatchesOem(ScheduledTaskInfo task)
        => OemVendors.Any(v =>
            task.Path.Contains(v, StringComparison.OrdinalIgnoreCase) ||
            (task.Author?.Contains(v, StringComparison.OrdinalIgnoreCase) ?? false));

    private static bool IsUserCreated(ScheduledTaskInfo task)
    {
        var underMicrosoft = task.Path.StartsWith(@"\Microsoft\", StringComparison.OrdinalIgnoreCase);
        var microsoftAuthor = task.Author?.Contains("Microsoft", StringComparison.OrdinalIgnoreCase) ?? false;
        return !underMicrosoft && !microsoftAuthor;
    }
}
