using ThisIsMyPC.Modules.Startup.Models;

namespace ThisIsMyPC.Modules.Startup.Services;

/// <summary>
/// Names one autostart item to the toggler, and round-trips through a
/// ChangeDescriptor's SystemLocation as "kind|location|name". Location is
/// always one of the fixed catalog keys, a Startup folder path, or a task
/// path, none of which can hold '|' (file and task names forbid it; the
/// catalog keys are constants). Name is the remainder after the second
/// separator, so a value, subkey, or service name may contain '|'.
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
        var parts = encoded.Split('|', 3);
        if (parts.Length != 3 || !Enum.TryParse<AutorunItemKind>(parts[0], out var kind) || !Enum.IsDefined(kind))
            return null;
        if (parts[1].Length == 0 || parts[2].Length == 0)
            return null;
        return new AutorunTarget(kind, parts[1], parts[2]);
    }

    /// <summary>Where the item sits when enabled: the value's key and name, the subkey, the file, the task path, or the service key.</summary>
    public string EnabledPath => Kind switch
    {
        AutorunItemKind.ScheduledTask => Location,
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
