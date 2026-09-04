using System.Reflection;
using System.Text.Json;
using ThisIsMyPC.Modules.Shell.Models;

namespace ThisIsMyPC.Modules.Shell.Services;

/// <summary>
/// Loads the ExplorerPatcher settings catalog from the embedded
/// <c>Data/explorerpatcher-settings.json</c>: the registry values
/// ExplorerPatcher reads (key, value, type, default, options), with the
/// app's own labels and descriptions. Regenerate against a new pinned
/// release with tools/import-explorerpatcher-settings.ps1.
/// Parsed with JsonDocument, so NativeAOT has nothing to reflect over.
/// </summary>
public static class ExplorerPatcherCatalog
{
    private const string ResourceName = "ThisIsMyPC.Modules.Shell.Data.explorerpatcher-settings.json";

    private static readonly Lazy<CatalogDocument> _document = new(LoadFromResource);

    public static IReadOnlyList<ExplorerPatcherSetting> Entries => _document.Value.Settings;

    /// <summary>
    /// The ExplorerPatcher release these definitions were imported from. The
    /// pin moves only when someone regenerates the catalog and checks the
    /// result, so a machine running a different version is worth saying out
    /// loud rather than silently writing values that may have moved.
    /// </summary>
    public static string Version => _document.Value.Version;

    private sealed record CatalogDocument(string Version, IReadOnlyList<ExplorerPatcherSetting> Settings);

    private static CatalogDocument LoadFromResource()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded catalog resource '{ResourceName}' is missing.");
        return ParseDocument(stream);
    }

    /// <summary>Parses a catalog document. Exposed for tests.</summary>
    public static IReadOnlyList<ExplorerPatcherSetting> Parse(Stream stream) => ParseDocument(stream).Settings;

    private static CatalogDocument ParseDocument(Stream stream)
    {
        using var document = JsonDocument.Parse(stream);

        var version = document.RootElement.TryGetProperty("_version", out var versionElement)
            ? versionElement.GetString() ?? string.Empty
            : string.Empty;

        var settings = new List<ExplorerPatcherSetting>();
        foreach (var element in document.RootElement.GetProperty("settings").EnumerateArray())
        {
            var options = new List<ExplorerPatcherOption>();
            if (element.TryGetProperty("options", out var optionArray)
                && optionArray.ValueKind == JsonValueKind.Array)
            {
                foreach (var option in optionArray.EnumerateArray())
                {
                    options.Add(new ExplorerPatcherOption(
                        option.GetProperty("value").GetInt32(),
                        option.GetProperty("name").GetString() ?? string.Empty));
                }
            }

            settings.Add(new ExplorerPatcherSetting(
                Id: element.GetProperty("id").GetString()!,
                DisplayName: element.GetProperty("name").GetString()!,
                Description: element.GetProperty("description").GetString() ?? string.Empty,
                GroupHeading: element.GetProperty("group").GetString() ?? string.Empty,
                Page: element.GetProperty("page").GetString()!,
                Section: ParseSection(element.GetProperty("section").GetString()!),
                RegistryKeyPath: element.GetProperty("key").GetString()!,
                RegistryValueName: element.GetProperty("value").GetString()!,
                Kind: ParseKind(element.GetProperty("kind").GetString()!),
                DefaultValue: element.GetProperty("default").GetInt32(),
                RequiresExplorerRestart: element.GetProperty("restart").GetBoolean(),
                Condition: element.GetProperty("condition").GetString() ?? string.Empty,
                Options: options.AsReadOnly()));
        }

        return new CatalogDocument(version, settings.AsReadOnly());
    }

    private static ShellSection ParseSection(string name) => name switch
    {
        "General" => ShellSection.General,
        "Taskbar" => ShellSection.Taskbar,
        "Desktop" => ShellSection.Desktop,
        "StartMenu" => ShellSection.StartMenu,
        "FileExplorer" => ShellSection.FileExplorer,
        _ => throw new InvalidOperationException($"Unknown section '{name}' in the ExplorerPatcher catalog."),
    };

    private static ExplorerPatcherSettingKind ParseKind(string name) => name switch
    {
        "toggle" => ExplorerPatcherSettingKind.Toggle,
        "invertedToggle" => ExplorerPatcherSettingKind.InvertedToggle,
        "choice" => ExplorerPatcherSettingKind.Choice,
        _ => throw new InvalidOperationException($"Unknown control kind '{name}' in the ExplorerPatcher catalog."),
    };
}
