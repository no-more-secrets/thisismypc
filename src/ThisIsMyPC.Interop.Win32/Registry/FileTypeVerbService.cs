using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;

namespace ThisIsMyPC.Interop.Win32.Registry;

/// <summary>
/// Scans per-file-type ProgID paths for static verbs and COM context menu handlers.
/// Reuses StaticVerbEntry and produces ShellExtensionInfo-compatible data for COM handlers.
/// </summary>
public sealed class FileTypeVerbService
{
    private readonly IRegistryService _registryService;

    public FileTypeVerbService(IRegistryService registryService)
    {
        _registryService = registryService;
    }

    /// <summary>
    /// Scans the given ProgID entries for static verbs.
    /// Returns all verbs found across the chain, deduplicated by verb name + execution mechanism.
    /// </summary>
    public OperationResult<IReadOnlyList<StaticVerbEntry>> ScanVerbs(IReadOnlyList<ProgIdEntry> progIdEntries)
    {
        var allVerbs = new List<StaticVerbEntry>();
        var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in progIdEntries)
        {
            var shellPath = $@"{entry.KeyPath}\shell";
            var subKeysResult = _registryService.EnumerateSubKeys(shellPath);
            if (!subKeysResult.IsSuccess)
                continue;

            foreach (var verbName in subKeysResult.Value!)
            {
                if (verbName.Equals("ShellNew", StringComparison.OrdinalIgnoreCase))
                    continue;

                var verbKeyPath = $@"{shellPath}\{verbName}";
                var verbEntry = ReadVerbEntry(verbName, verbKeyPath, entry.ProgId);
                if (verbEntry is null)
                    continue;

                // Dedup: verb name + execution mechanism
                var dedupKey = MakeVerbDedupKey(verbEntry);
                if (!seenKeys.Add(dedupKey))
                    continue;

                allVerbs.Add(verbEntry);
            }
        }

        return OperationResult<IReadOnlyList<StaticVerbEntry>>.Success(allVerbs);
    }

    /// <summary>
    /// Scans the given ProgID entries for COM context menu handlers.
    /// Returns handler info (CLSID, name, DLL path) for per-file-type COM registrations.
    /// </summary>
    public OperationResult<IReadOnlyList<FileTypeComHandler>> ScanComHandlers(IReadOnlyList<ProgIdEntry> progIdEntries)
    {
        var handlers = new List<FileTypeComHandler>();
        var seenClsids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in progIdEntries)
        {
            var handlersPath = $@"{entry.KeyPath}\shellex\ContextMenuHandlers";
            var subKeysResult = _registryService.EnumerateSubKeys(handlersPath);
            if (!subKeysResult.IsSuccess)
                continue;

            foreach (var handlerName in subKeysResult.Value!)
            {
                var handlerKeyPath = $@"{handlersPath}\{handlerName}";
                var clsidResult = _registryService.ReadString(handlerKeyPath, string.Empty);
                if (!clsidResult.IsSuccess || string.IsNullOrWhiteSpace(clsidResult.Value))
                    continue;

                var rawClsid = clsidResult.Value!;
                var isEnabled = !rawClsid.StartsWith('-');
                var cleanClsid = isEnabled ? rawClsid : rawClsid[1..];

                if (!seenClsids.Add(cleanClsid))
                    continue;

                // Resolve display name and DLL path
                var displayName = ResolveClsidDisplayName(cleanClsid) ?? handlerName;
                var dllPath = ResolveDllPath(cleanClsid);

                handlers.Add(new FileTypeComHandler(
                    Name: displayName,
                    Clsid: cleanClsid,
                    RegistryPath: handlerKeyPath,
                    Scope: entry.ProgId,
                    DllPath: dllPath,
                    IsEnabled: isEnabled));
            }
        }

        return OperationResult<IReadOnlyList<FileTypeComHandler>>.Success(handlers);
    }

    private StaticVerbEntry? ReadVerbEntry(string verbName, string verbKeyPath, string scope)
    {
        var muiVerb = ReadStringOrNull(verbKeyPath, "MUIVerb");

        // Fall back to default value as display name (skip indirect strings)
        if (muiVerb is null)
        {
            var defaultValue = ReadStringOrNull(verbKeyPath, string.Empty);
            if (defaultValue is not null &&
                !defaultValue.StartsWith('@') &&
                !defaultValue.Equals(verbName, StringComparison.OrdinalIgnoreCase))
            {
                muiVerb = defaultValue;
            }
        }

        var icon = ReadStringOrNull(verbKeyPath, "Icon");
        var position = ReadStringOrNull(verbKeyPath, "Position");
        var appliesTo = ReadStringOrNull(verbKeyPath, "AppliesTo");

        var extendedResult = _registryService.ValueExists(verbKeyPath, "Extended");
        var isExtended = extendedResult.IsSuccess && extendedResult.Value;

        var legacyDisableResult = _registryService.ValueExists(verbKeyPath, "LegacyDisable");
        var isLegacyDisabled = legacyDisableResult.IsSuccess && legacyDisableResult.Value;

        var luaShieldResult = _registryService.ValueExists(verbKeyPath, "HasLUAShield");
        var hasLuaShield = luaShieldResult.IsSuccess && luaShieldResult.Value;

        var programmaticResult = _registryService.ValueExists(verbKeyPath, "ProgrammaticAccessOnly");
        var isProgrammaticAccessOnly = programmaticResult.IsSuccess && programmaticResult.Value;

        var commandKeyPath = $@"{verbKeyPath}\command";
        string? commandLine = null;
        string? delegateExecuteClsid = null;

        var commandResult = _registryService.ReadString(commandKeyPath, string.Empty);
        if (commandResult.IsSuccess && !string.IsNullOrWhiteSpace(commandResult.Value))
            commandLine = commandResult.Value;

        var delegateResult = _registryService.ReadString(commandKeyPath, "DelegateExecute");
        if (delegateResult.IsSuccess && !string.IsNullOrWhiteSpace(delegateResult.Value))
            delegateExecuteClsid = delegateResult.Value;

        var dropTargetKeyPath = $@"{verbKeyPath}\DropTarget";
        var dropTargetResult = _registryService.KeyExists(dropTargetKeyPath);
        var hasDropTarget = dropTargetResult.IsSuccess && dropTargetResult.Value;

        return new StaticVerbEntry(
            VerbName: verbName,
            RegistryPath: verbKeyPath,
            Scope: scope,
            MuiVerb: muiVerb,
            Icon: icon,
            Position: position,
            IsExtended: isExtended,
            CommandLine: commandLine,
            DelegateExecuteClsid: delegateExecuteClsid,
            HasDropTarget: hasDropTarget,
            IsLegacyDisabled: isLegacyDisabled,
            AppliesTo: appliesTo,
            HasLuaShield: hasLuaShield,
            IsProgrammaticAccessOnly: isProgrammaticAccessOnly);
    }

    private static string MakeVerbDedupKey(StaticVerbEntry entry)
    {
        var exec = entry.CommandLine ?? entry.DelegateExecuteClsid ?? "no-exec";
        return $"{entry.VerbName}|{exec}";
    }

    private string? ResolveClsidDisplayName(string clsid)
    {
        var result = _registryService.ReadString($@"HKCR\CLSID\{clsid}", string.Empty);
        if (!result.IsSuccess) return null;
        var value = result.Value!;
        if (string.IsNullOrWhiteSpace(value) || value.StartsWith('@') || (value.Length > 2 && value[0] == '{' && value[^1] == '}'))
            return null;
        return value;
    }

    private string? ResolveDllPath(string clsid)
    {
        var result = _registryService.ReadString($@"HKCR\CLSID\{clsid}\InprocServer32", string.Empty);
        return result.IsSuccess ? result.Value : null;
    }

    private string? ReadStringOrNull(string keyPath, string valueName)
    {
        var result = _registryService.ReadString(keyPath, valueName);
        return result.IsSuccess ? result.Value : null;
    }
}

public sealed record FileTypeComHandler(
    string Name,
    string Clsid,
    string RegistryPath,
    string Scope,
    string? DllPath,
    bool IsEnabled);
