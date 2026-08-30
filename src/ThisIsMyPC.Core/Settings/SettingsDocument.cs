using System.Text.Json;
using System.Text.Json.Serialization;

namespace ThisIsMyPC.Core.Settings;

/// <summary>
/// JSON shape of settings.json. Everything nullable; validation happens in
/// SettingsService. Unknown top-level properties round-trip via the extension bag so
/// files written by newer app versions survive an older version's save.
/// </summary>
public sealed class SettingsDocument
{
    public Dictionary<string, string>? AppSettings { get; set; }
    public Dictionary<string, Dictionary<string, string>>? ModuleSettings { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true,
    WriteIndented = true)]
[JsonSerializable(typeof(SettingsDocument))]
public sealed partial class SettingsJsonContext : JsonSerializerContext;
