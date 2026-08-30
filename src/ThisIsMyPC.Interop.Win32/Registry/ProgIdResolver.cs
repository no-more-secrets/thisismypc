using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;

namespace ThisIsMyPC.Interop.Win32.Registry;

/// <summary>
/// Resolves a file extension to its full ProgID chain for context menu enumeration.
/// The chain includes: default ProgID, SystemFileAssociations per-extension,
/// SystemFileAssociations per-PerceivedType, and OpenWithProgids entries.
/// </summary>
public sealed class ProgIdResolver
{
    private readonly IRegistryService _registryService;

    public ProgIdResolver(IRegistryService registryService)
    {
        _registryService = registryService;
    }

    /// <summary>
    /// Resolves the full ProgID chain for a file extension.
    /// Returns an ordered list of HKCR key paths to scan for verbs and handlers.
    /// Most specific first (default ProgID), least specific last (PerceivedType).
    /// </summary>
    public OperationResult<IReadOnlyList<ProgIdEntry>> Resolve(string extension)
    {
        if (string.IsNullOrEmpty(extension) || extension[0] != '.')
            return OperationResult<IReadOnlyList<ProgIdEntry>>.Failure(
                $"Invalid extension: {extension}", ErrorCategory.NotFound);

        var entries = new List<ProgIdEntry>();
        var seenProgIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var extKeyPath = $@"HKCR\{extension}";

        // 1. Default ProgID (e.g., .png → pngfile)
        var defaultProgId = _registryService.ReadString(extKeyPath, string.Empty);
        if (defaultProgId.IsSuccess && !string.IsNullOrWhiteSpace(defaultProgId.Value)
            && seenProgIds.Add(defaultProgId.Value))
        {
            entries.Add(new ProgIdEntry($@"HKCR\{defaultProgId.Value}", defaultProgId.Value, ProgIdSource.DefaultProgId));
        }

        // 2. OpenWithProgids; additional ProgIDs from app registrations
        // Note: ProgIDs are stored as value *names* (data is empty/zero per convention)
        var openWithPath = $@"{extKeyPath}\OpenWithProgids";
        var openWithResult = _registryService.EnumerateValues(openWithPath);
        if (openWithResult.IsSuccess)
        {
            foreach (var progId in openWithResult.Value!)
            {
                if (!string.IsNullOrWhiteSpace(progId) && seenProgIds.Add(progId))
                {
                    entries.Add(new ProgIdEntry($@"HKCR\{progId}", progId, ProgIdSource.OpenWithProgids));
                }
            }
        }

        // 3. SystemFileAssociations per-extension (e.g., SystemFileAssociations\.png)
        entries.Add(new ProgIdEntry(
            $@"HKCR\SystemFileAssociations\{extension}",
            $"SystemFileAssociations\\{extension}",
            ProgIdSource.SystemFileAssociations));

        // 4. SystemFileAssociations per-PerceivedType (e.g., SystemFileAssociations\image)
        var perceivedType = _registryService.ReadString(extKeyPath, "PerceivedType");
        if (perceivedType.IsSuccess && !string.IsNullOrWhiteSpace(perceivedType.Value))
        {
            entries.Add(new ProgIdEntry(
                $@"HKCR\SystemFileAssociations\{perceivedType.Value}",
                $"SystemFileAssociations\\{perceivedType.Value}",
                ProgIdSource.PerceivedType));
        }

        return OperationResult<IReadOnlyList<ProgIdEntry>>.Success(entries);
    }
}

public sealed record ProgIdEntry(string KeyPath, string ProgId, ProgIdSource Source);

public enum ProgIdSource
{
    DefaultProgId,
    OpenWithProgids,
    SystemFileAssociations,
    PerceivedType,
}
