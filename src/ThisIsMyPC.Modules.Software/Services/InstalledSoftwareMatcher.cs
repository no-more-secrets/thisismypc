using System.Text.RegularExpressions;
using ThisIsMyPC.Core.Packages;
using ThisIsMyPC.Modules.Software.Models;

namespace ThisIsMyPC.Modules.Software.Services;

/// <summary>Exact package identities, or explicit display names for uncorrelated installations.</summary>
public static class InstalledSoftwareMatcher
{
    public static bool Matches(SoftwareCatalogEntry entry, IReadOnlyList<InstalledWingetPackage> installed)
        => installed.Any(package => Matches(entry, package));

    public static InstalledWingetPackage? FindMatch(SoftwareCatalogEntry entry, IReadOnlyList<InstalledWingetPackage> installed)
        => installed.Where(package => Matches(entry, package))
            .OrderByDescending(package => package.CanUninstall)
            .ThenByDescending(package => package.PackageId.Equals(entry.WingetId, StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault();

    private static bool Matches(SoftwareCatalogEntry entry, InstalledWingetPackage package)
    {
        if (package.PackageId.Length > 0)
        {
            if (package.PackageId.Equals(entry.WingetId, StringComparison.OrdinalIgnoreCase)) return true;
            if (entry.InstalledIds.Contains(package.PackageId, StringComparer.OrdinalIgnoreCase)) return true;
            // Numeric version families only. Firefox.ESR and Chrome.Beta are different products.
            var prefix = entry.WingetId + ".";
            return package.PackageId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                && package.PackageId.Length > prefix.Length
                && package.PackageId[prefix.Length..].All(c => char.IsAsciiDigit(c) || c == '.');
        }
        if (string.IsNullOrWhiteSpace(package.Name) || package.Name.Contains('…')) return false;
        // Version-specific packages can coexist. Python 3.13 is not Python 3.14.
        var family = Regex.Match(entry.WingetId, @"\.(\d+(?:\.\d+)*)$").Groups[1].Value;
        if (family.Length > 0)
        {
            var installedVersion = Regex.Match(package.Version ?? "", @"^\d+(?:\.\d+)*").Value;
            if (!installedVersion.Equals(family, StringComparison.Ordinal)
                && !installedVersion.StartsWith(family + ".", StringComparison.Ordinal)) return false;
        }
        var preserveArchitecture = entry.WingetId.EndsWith(".x64", StringComparison.OrdinalIgnoreCase)
            || entry.WingetId.EndsWith(".x86", StringComparison.OrdinalIgnoreCase)
            || entry.WingetId.EndsWith(".arm64", StringComparison.OrdinalIgnoreCase);
        var name = Normalize(package.Name, preserveArchitecture);
        return entry.InstalledNames.Prepend(entry.Name)
            .Any(alias => name.Equals(Normalize(alias, preserveArchitecture), StringComparison.OrdinalIgnoreCase));
    }

    private static string Normalize(string name, bool preserveArchitecture)
    {
        // Strip only terminal installer decorations, never product/channel words such as Beta or ESR.
        const RegexOptions options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
        name = name.Trim();
        for (var i = 0; i < 3; i++)
        {
            if (!preserveArchitecture)
                name = Regex.Replace(name, @"\s+\((?:x64|x86|arm64|32-bit|64-bit)(?:\s+[a-z]{2}(?:-[a-z]{2})?)?\)$", "", options);
            name = Regex.Replace(name, @"(?:\s+-)?\s+v?\d+(?:\.\d+)+(?:\s+\([0-9]+\))?$", "", options);
        }
        return name.Trim();
    }
}
