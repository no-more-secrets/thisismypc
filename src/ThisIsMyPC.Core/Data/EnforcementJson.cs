using System.Text.Json;
using ThisIsMyPC.Core.Enforcement;
using ThisIsMyPC.Core.Sets;
using ThisIsMyPC.Core.Sets.Serialization;

namespace ThisIsMyPC.Core.Data;

/// <summary>
/// Persists SettingEnforcement as JSON for change-history storage, reusing the set-file
/// DTO and source-generated context (NativeAOT-safe, camelCase — the same shape a set
/// file's enforcement object uses).
/// </summary>
internal static class EnforcementJson
{
    public static string? Serialize(SettingEnforcement? enforcement)
    {
        if (enforcement is null)
            return null;

        var document = new SetEnforcementDocument
        {
            CompanionServices = enforcement.CompanionServices,
            CompanionTasks = enforcement.CompanionTasks,
            GpCacheEntries = enforcement.GPCacheEntries,
            ReversionVectors = enforcement.ReversionVectors,
            SkuRestriction = enforcement.SkuRestriction,
            OwnerModeRequired = enforcement.OwnerModeRequired,
            AclElevation = enforcement.AclElevation,
        };

        return JsonSerializer.Serialize(document, SetJsonContext.Default.SetEnforcementDocument);
    }

    /// <summary>Null for null/blank/corrupt JSON — a mangled row degrades to unenforced rather than failing the history load.</summary>
    public static SettingEnforcement? Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            var document = JsonSerializer.Deserialize(json, SetJsonContext.Default.SetEnforcementDocument);
            return SetProvider.MapEnforcement(document);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
