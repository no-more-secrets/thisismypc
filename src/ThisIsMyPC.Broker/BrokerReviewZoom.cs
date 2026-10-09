using System.Globalization;
using System.Security;
using System.Text.Json;
using Microsoft.Win32;

namespace ThisIsMyPC.Broker;

// User-owned settings are only a bounded placement hint. They never authorize a broker operation.
internal static class BrokerReviewZoom
{
    private const int MaxSettingsBytes = 1_048_576;

    internal static double ForUiSid(string? uiSid)
    {
        if (string.IsNullOrWhiteSpace(uiSid))
            return 1;
        try
        {
            using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var profile = machine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList\" + uiSid);
            if (profile?.GetValue("ProfileImagePath") is not string profilePath
                || !Path.IsPathFullyQualified(profilePath) || profilePath.StartsWith(@"\\", StringComparison.Ordinal))
                return 1;
            return FromSettingsFile(Path.Combine(profilePath, "AppData", "Local", "ThisIsMyPC", "settings.json"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException
                                   or ArgumentException or NotSupportedException)
        {
            return 1;
        }
    }

    internal static double FromSettingsFile(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.SequentialScan);
            if (stream.Length is <= 0 or > MaxSettingsBytes)
                return 1;
            var data = new byte[(int)stream.Length];
            stream.ReadExactly(data);
            using var document = JsonDocument.Parse(data, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip,
                MaxDepth = 8,
            });
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("appSettings", out var settings)
                || settings.ValueKind != JsonValueKind.Object
                || !settings.TryGetProperty("uiZoom", out var zoom)
                || zoom.ValueKind != JsonValueKind.String
                || !int.TryParse(zoom.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var percent))
                return 1;
            return Math.Clamp(percent, 50, 150) / 100.0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException
                                   or ArgumentException or JsonException or NotSupportedException)
        {
            return 1;
        }
    }
}
