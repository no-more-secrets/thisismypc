using System.Text.Json;
using ThisIsMyPC.Core.Packages;

namespace ThisIsMyPC.Interop.Win32.Packages;

/// <summary>Read-only evidence for installations absent from Add/Remove Programs.</summary>
public static class SupplementalSoftwareInventory
{
    public static IReadOnlyList<InstalledWingetPackage> Read()
    {
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
        }.Where(root => !string.IsNullOrWhiteSpace(root)).Distinct(StringComparer.OrdinalIgnoreCase);
        var dotnetRoots = roots.Select(root => Path.Combine(root, "dotnet"))
            .Concat(new[] { Environment.GetEnvironmentVariable("DOTNET_ROOT") ?? "" });
        var npmRoots = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
            .Append(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm"))
            .Append(Environment.GetEnvironmentVariable("NVM_SYMLINK") ?? "");
        return ReadLocations(dotnetRoots, npmRoots);
    }

    /// <summary>Reads known layouts without executing discovered programs or scripts.</summary>
    public static IReadOnlyList<InstalledWingetPackage> ReadLocations(IEnumerable<string> dotnetRoots, IEnumerable<string> npmRoots)
    {
        var result = new List<InstalledWingetPackage>();
        foreach (var root in dotnetRoots.Where(Path.IsPathFullyQualified).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var shared = Path.Combine(root, "shared", "Microsoft.WindowsDesktop.App");
                if (!Directory.Exists(shared)) continue;
                foreach (var path in Directory.EnumerateDirectories(shared))
                {
                    if (Version.TryParse(Path.GetFileName(path), out var version) && version.Major == 10
                        && File.Exists(Path.Combine(path, "PresentationFramework.dll")))
                        result.Add(new("Microsoft.DotNet.DesktopRuntime.10", version.ToString(), ".NET Desktop Runtime 10") { CanUninstall = false });
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { }
        }
        foreach (var root in npmRoots.Where(Path.IsPathFullyQualified).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var package = Path.Combine(root, "node_modules", "@openai", "codex");
                var manifest = Path.Combine(package, "package.json");
                if (!File.Exists(manifest) || !File.Exists(Path.Combine(package, "bin", "codex.js"))) continue;
                using var json = JsonDocument.Parse(File.ReadAllText(manifest));
                if (json.RootElement.TryGetProperty("name", out var name) && name.GetString() == "@openai/codex"
                    && json.RootElement.TryGetProperty("version", out var version) && version.ValueKind == JsonValueKind.String)
                    result.Add(new("OpenAI.Codex", version.GetString(), "Codex CLI") { CanUninstall = false });
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or JsonException or InvalidOperationException) { }
        }
        return result;
    }
}
