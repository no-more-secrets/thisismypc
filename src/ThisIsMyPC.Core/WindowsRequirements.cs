namespace ThisIsMyPC.Core;

/// <summary>Defines the minimum Windows version for the app and installer.</summary>
public static class WindowsRequirements
{
    /// <summary>The message shown when Windows does not meet the minimum version.</summary>
    public const string UnsupportedVersionMessage =
        "ThisIsMyPC requires Windows 11 22H2 or later.";

    /// <summary>Checks whether a Windows version meets the minimum requirement.</summary>
    /// <param name="version">The Windows version, including its build number.</param>
    /// <returns>True for Windows build 22621 or later.</returns>
    public static bool IsSupported(Version version)
    {
        ArgumentNullException.ThrowIfNull(version);
        return version >= new Version(10, 0, 22621);
    }
}
