using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;

namespace ThisIsMyPC.Interop.Win32.Registry;

/// <summary>
/// Discovers file extensions on the system that have registered context menu handlers
/// or static verbs beyond the default 'open' verb.
/// </summary>
public sealed class FileExtensionDiscovery
{
    private readonly IRegistryService _registryService;
    private IReadOnlyList<string>? _cachedExtensions;

    // Shell-internal verbs that never produce visible menu entries.
    // Must match InternalHandlerFilter.HiddenVerbNames in Modules.Shell.
    private static readonly HashSet<string> InternalVerbNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "open", "explore", "find", "removeproperties",
        "opennewprocess", "opennewtab", "opennewwindow",
    };

    public FileExtensionDiscovery(IRegistryService registryService)
    {
        _registryService = registryService;
    }

    /// <summary>
    /// Enumerates all file extensions under HKCR that have custom context menu registrations.
    /// Returns extensions sorted alphabetically. Result is cached after first call.
    /// </summary>
    public OperationResult<IReadOnlyList<string>> DiscoverExtensions()
    {
        if (_cachedExtensions is not null)
            return OperationResult<IReadOnlyList<string>>.Success(_cachedExtensions);

        var result = DiscoverExtensionsCore();
        if (result.IsSuccess)
            _cachedExtensions = result.Value;
        return result;
    }

    private OperationResult<IReadOnlyList<string>> DiscoverExtensionsCore()
    {
        var subKeysResult = _registryService.EnumerateSubKeys("HKCR");
        if (!subKeysResult.IsSuccess)
            return OperationResult<IReadOnlyList<string>>.Failure(
                "Failed to enumerate HKCR", subKeysResult.ErrorCategory ?? ErrorCategory.ServiceUnavailable);

        var extensions = new List<string>();

        foreach (var key in subKeysResult.Value!)
        {
            if (key.Length < 2 || key[0] != '.')
                continue;

            // Check if this extension or its default ProgID has custom verbs/handlers
            if (HasCustomRegistrations(key))
                extensions.Add(key);
        }

        extensions.Sort(StringComparer.OrdinalIgnoreCase);
        return OperationResult<IReadOnlyList<string>>.Success(extensions);
    }

    private bool HasCustomRegistrations(string extension)
    {
        var extKeyPath = $@"HKCR\{extension}";

        // Check direct shell verbs beyond 'open'
        if (HasCustomVerbs($@"{extKeyPath}\shell"))
            return true;

        // Check direct COM handlers
        if (HasSubKeys($@"{extKeyPath}\shellex\ContextMenuHandlers"))
            return true;

        // Check default ProgID's verbs/handlers
        var defaultProgId = _registryService.ReadString(extKeyPath, string.Empty);
        if (defaultProgId.IsSuccess && !string.IsNullOrWhiteSpace(defaultProgId.Value))
        {
            var progIdPath = $@"HKCR\{defaultProgId.Value}";
            if (HasCustomVerbs($@"{progIdPath}\shell"))
                return true;
            if (HasSubKeys($@"{progIdPath}\shellex\ContextMenuHandlers"))
                return true;
        }

        // Check SystemFileAssociations per-extension
        if (HasCustomVerbs($@"HKCR\SystemFileAssociations\{extension}\shell"))
            return true;
        if (HasSubKeys($@"HKCR\SystemFileAssociations\{extension}\shellex\ContextMenuHandlers"))
            return true;

        return false;
    }

    private bool HasCustomVerbs(string shellPath)
    {
        var subKeysResult = _registryService.EnumerateSubKeys(shellPath);
        if (!subKeysResult.IsSuccess)
            return false;

        // Has verbs beyond shell-internal ones
        return subKeysResult.Value!.Any(v =>
            !InternalVerbNames.Contains(v) &&
            !v.Equals("ShellNew", StringComparison.OrdinalIgnoreCase));
    }

    private bool HasSubKeys(string path)
    {
        var result = _registryService.EnumerateSubKeys(path);
        return result.IsSuccess && result.Value!.Count > 0;
    }
}
