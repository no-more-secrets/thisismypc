using Windows.Management.Deployment;
using System.Xml;
using System.Xml.Linq;

namespace ThisIsMyPC.Interop.Com.Packages;

/// <summary>Reads current-user package artwork without launching an app.</summary>
public static class AppxIconReader
{
    public static byte[]? Read(string packageName)
    {
        try
        {
            var package = new PackageManager().FindPackagesForUser("")
                .FirstOrDefault(p => p.Id.Name.Equals(packageName, StringComparison.OrdinalIgnoreCase));
            if (package is null) return null;
            var root = package.InstalledLocation.Path;
            using var reader = XmlReader.Create(Path.Combine(root, "AppxManifest.xml"), new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = 2 * 1024 * 1024,
            });
            var manifest = XDocument.Load(reader);
            var logo = manifest.Descendants().FirstOrDefault(e => e.Name.LocalName == "VisualElements")
                ?.Attribute("Square44x44Logo")?.Value
                ?? manifest.Descendants().FirstOrDefault(e => e.Name.LocalName == "Logo")?.Value;
            if (string.IsNullOrWhiteSpace(logo)) return null;
            var path = Path.Combine(root, logo.Replace('/', Path.DirectorySeparatorChar));
            path = Path.GetFullPath(path);
            if (!path.StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return null;
            if (!File.Exists(path))
            {
                var directory = Path.GetDirectoryName(path)!;
                if (!Directory.Exists(directory)) return null;
                path = Directory.EnumerateFiles(directory, Path.GetFileNameWithoutExtension(path) + "*.png")
                    .Where(p => !p.Contains("contrast-", StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(p => p.Contains("targetsize-32", StringComparison.OrdinalIgnoreCase))
                    .ThenByDescending(p => p.Contains("scale-100", StringComparison.OrdinalIgnoreCase)).FirstOrDefault();
            }
            return path is not null && new FileInfo(path).Length <= 2 * 1024 * 1024 ? File.ReadAllBytes(path) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.Runtime.InteropServices.COMException or InvalidOperationException or XmlException)
        { return null; }
    }
}
