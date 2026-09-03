using System.Reflection;
using System.Text.Json;
using ThisIsMyPC.Modules.Shell.Models;

namespace ThisIsMyPC.Modules.Shell.Services;

/// <summary>
/// Loads the ExplorerPatcher settings catalog from the embedded
/// <c>Data/explorerpatcher-settings.json</c>. The definitions are imported
/// from ExplorerPatcher's own settings manifest (GPLv2, Copyright (c)
/// valinet); regenerate with tools/import-explorerpatcher-settings.ps1.
/// Parsed with JsonDocument, so NativeAOT has nothing to reflect over.
/// </summary>
public static class ExplorerPatcherCatalog
{
    private const string ResourceName = "ThisIsMyPC.Modules.Shell.Data.explorerpatcher-settings.json";

    private static readonly Lazy<IReadOnlyList<ExplorerPatcherSetting>> _entries = new(LoadFromResource);

    public static IReadOnlyList<ExplorerPatcherSetting> Entries => _entries.Value;

    private static IReadOnlyList<ExplorerPatcherSetting> LoadFromResource()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded catalog resource '{ResourceName}' is missing.");
        return Parse(stream);
    }

    /// <summary>Parses a catalog document. Exposed for tests.</summary>
    public static IReadOnlyList<ExplorerPatcherSetting> Parse(Stream stream)
    {
        using var document = JsonDocument.Parse(stream);

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

        return settings.AsReadOnly();
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
