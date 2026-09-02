using System.Reflection;

namespace ThisIsMyPC.Installer.Services;

/// <summary>
/// The MSI and the license text ride inside the exe as manifest resources
/// (build-release.ps1 passes the packed MSI as EmbeddedMsiPath).
/// </summary>
public sealed class EmbeddedPackage
{
    public const string MsiResourceName = "ThisIsMyPC-win.msi";
    private const string LicenseResourceName = "LICENSE";

    private static Assembly Assembly => typeof(EmbeddedPackage).Assembly;

    public bool IsPresent => Assembly.GetManifestResourceInfo(MsiResourceName) is not null;

    /// <summary>Writes the MSI into <paramref name="directory"/> and returns its path.</summary>
    public string ExtractTo(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        using var source = Assembly.GetManifestResourceStream(MsiResourceName)
            ?? throw new InvalidOperationException("This build carries no installer package.");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, MsiResourceName);
        using var target = File.Create(path);
        source.CopyTo(target);
        return path;
    }

    public static string LoadLicenseText()
    {
        using var stream = Assembly.GetManifestResourceStream(LicenseResourceName);
        if (stream is null)
            return "License text missing from this build.";
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
