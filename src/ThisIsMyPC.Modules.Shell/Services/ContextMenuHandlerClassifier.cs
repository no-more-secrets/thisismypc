using System.Diagnostics;
using ThisIsMyPC.Modules.Shell.Models;

namespace ThisIsMyPC.Modules.Shell.Services;

public static class ContextMenuHandlerClassifier
{
    private static readonly HashSet<string> CriticalClsids = new(StringComparer.OrdinalIgnoreCase)
    {
        "{09799AFB-AD67-11d1-ABCD-00C04FC30936}", // Open With
        "{7BA4C740-9E81-11CF-99D3-00AA004AE837}", // SendTo
        "{D969A300-E7FF-11d0-A93B-00A0C90F2719}", // New Menu
        "{f3d06e7c-1e45-4a26-847e-f9fcdee59be0}", // Copy as Path
        "{00021401-0000-0000-C000-000000000046}", // Shortcut (.lnk)
        "{85cfccaf-2d14-42b6-80b6-f40f65d016e7}", // Shortcut (.symlink)
        "{f81e9010-6ea4-11ce-a7ff-00aa003ca9f6}", // Sharing
        "{a2a9545d-a0c2-42b4-9708-a0b2badd77c8}", // Start Menu Pin
        "{90AA3A4E-1CBA-4233-B8BB-535773D48449}", // Taskband Pin
        "{A470F8CF-A1E8-4f65-8335-227475AA5C46}", // Encryption
    };

    public static HandlerClassification Classify(string clsid, string? dllPath, string? publisher = null)
    {
        if (CriticalClsids.Contains(clsid))
            return HandlerClassification.Critical;

        if (string.IsNullOrEmpty(dllPath))
            return HandlerClassification.ThirdParty;

        // Use provided publisher or attempt to read from DLL version info
        var companyName = publisher;
        if (companyName is null)
        {
            try
            {
                if (File.Exists(dllPath))
                {
                    var versionInfo = FileVersionInfo.GetVersionInfo(dllPath);
                    companyName = versionInfo.CompanyName;
                }
            }
            catch
            {
                // Fall through to path-based heuristic
            }
        }

        if (companyName is not null &&
            companyName.Contains("Microsoft", StringComparison.OrdinalIgnoreCase))
        {
            if (dllPath.Contains("PowerToys", StringComparison.OrdinalIgnoreCase))
                return HandlerClassification.Optional;

            return HandlerClassification.System;
        }

        // Path-based fallback when version info is unavailable
        if (companyName is null && dllPath.Contains("PowerToys", StringComparison.OrdinalIgnoreCase))
            return HandlerClassification.Optional;

        return HandlerClassification.ThirdParty;
    }
}
