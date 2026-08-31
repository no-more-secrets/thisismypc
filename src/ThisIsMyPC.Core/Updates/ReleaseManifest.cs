namespace ThisIsMyPC.Core.Updates;

/// <summary>
/// The signed release manifest: sha256sum format, one entry per release asset
/// (lowercase hex digest, two spaces, file name). Published as SHA256SUMS next
/// to the packages, with a detached GPG signature in SHA256SUMS.asc, so users
/// can verify releases with stock tooling (sha256sum -c, gpg --verify) and the
/// app can verify updates against the offline release key.
/// </summary>
public sealed class ReleaseManifest
{
    private readonly Dictionary<string, string> _digestsByFileName;

    private ReleaseManifest(Dictionary<string, string> digestsByFileName)
    {
        _digestsByFileName = digestsByFileName;
    }

    public int Count => _digestsByFileName.Count;

    /// <summary>Lowercase hex SHA-256 for the asset, or null when the manifest does not list it.</summary>
    public string? DigestFor(string fileName) =>
        _digestsByFileName.TryGetValue(fileName, out var digest) ? digest : null;

    /// <summary>
    /// Parses sha256sum output: exactly "&lt;64 hex&gt;  &lt;name&gt;" (text mode) or
    /// "&lt;64 hex&gt; *&lt;name&gt;" (binary mode) per line. Tolerates blank lines,
    /// CRLF, and a leading UTF-8 BOM; anything else rejects the whole manifest
    /// (a manifest that cannot be read exactly must not be trusted partially).
    /// </summary>
    public static ReleaseManifest? TryParse(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return null;
        content = content.TrimStart('\uFEFF');

        var digests = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rawLine in content.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (line.Length == 0)
                continue;

            if (line.Length < 67)
                return null;
            var digest = line[..64];
            if (!digest.All(Uri.IsHexDigit))
                return null;
            var separator = line[64..66];
            if (separator is not ("  " or " *"))
                return null;

            var name = line[66..];
            if (name.Length == 0 || name.Contains('/', StringComparison.Ordinal)
                || name.Contains('\\', StringComparison.Ordinal))
            {
                return null;
            }

            digests[name] = digest.ToLowerInvariant();
        }

        return digests.Count > 0 ? new ReleaseManifest(digests) : null;
    }
}
