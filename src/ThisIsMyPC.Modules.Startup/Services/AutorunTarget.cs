using ThisIsMyPC.Modules.Startup.Models;

namespace ThisIsMyPC.Modules.Startup.Services;

/// <summary>
/// Names one autostart item to the toggler, and round-trips through a
/// ChangeDescriptor's SystemLocation as "kind|location|name". '|' cannot
/// appear in a registry key name, a file name, or a task path, so the split
/// is unambiguous.
/// </summary>
public sealed record AutorunTarget(AutorunItemKind Kind, string Location, string Name)
{
    public const string DisabledName = "AutorunsDisabled";

    public static AutorunTarget For(AutorunEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return new(entry.Kind, entry.Location, entry.Name);
    }

    public string Encode() => $"{Kind}|{Location}|{Name}";

    public static AutorunTarget? TryParse(string? encoded)
    {
        if (string.IsNullOrEmpty(encoded))
            return null;
        var parts = encoded.Split('|');
        if (parts.Length != 3 || !Enum.TryParse<AutorunItemKind>(parts[0], out var kind) || !Enum.IsDefined(kind))
            return null;
        if (parts[1].Length == 0 || parts[2].Length == 0)
            return null;
        return new AutorunTarget(kind, parts[1], parts[2]);
    }

    /// <summary>Where the item sits when enabled: the key, folder, task, or service key path plus the name where that applies.</summary>
    public string EnabledPath => Kind switch
    {
        AutorunItemKind.ScheduledTask or AutorunItemKind.Service => Location,
        _ => $@"{Location}\{Name}",
    };

    /// <summary>Where the item sits when disabled by Autoruns; null for tasks and services, which flip in place.</summary>
    public string? DisabledPath => Kind switch
    {
        AutorunItemKind.RegistryValue or AutorunItemKind.RegistryKey or AutorunItemKind.StartupFile
            => $@"{Location}\{DisabledName}\{Name}",
        _ => null,
    };

    /// <summary>The AutorunsDisabled key or folder next to the item.</summary>
    public string DisabledContainer => $@"{Location}\{DisabledName}";
}
