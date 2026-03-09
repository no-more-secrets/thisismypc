using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;

namespace ThisIsMyPC.Interop.Win32.Registry;

public sealed class StaticVerbService : IStaticVerbService
{
    private readonly IRegistryService _registryService;
    private readonly IReadOnlyList<(string KeyPath, string Scope)> _scopePaths;

    public StaticVerbService(IRegistryService registryService, IReadOnlyList<(string KeyPath, string Scope)> scopePaths)
    {
        _registryService = registryService;
        _scopePaths = scopePaths;
    }

    public OperationResult<IReadOnlyList<StaticVerbEntry>> EnumerateStaticVerbs()
    {
        var entries = new List<StaticVerbEntry>();

        foreach (var (shellKeyPath, scope) in _scopePaths)
        {
            var subKeysResult = _registryService.EnumerateSubKeys(shellKeyPath);
            if (!subKeysResult.IsSuccess)
                continue;

            foreach (var verbName in subKeysResult.Value!)
            {
                // Skip ShellNew entries — different framework
                if (verbName.Equals("ShellNew", StringComparison.OrdinalIgnoreCase))
                    continue;

                var verbKeyPath = $@"{shellKeyPath}\{verbName}";
                var entry = ReadVerbEntry(verbName, verbKeyPath, scope);
                if (entry is not null)
                    entries.Add(entry);
            }
        }

        return OperationResult<IReadOnlyList<StaticVerbEntry>>.Success(entries);
    }

    private StaticVerbEntry? ReadVerbEntry(string verbName, string verbKeyPath, string scope)
    {
        // Read verb metadata values
        var muiVerb = ReadStringOrNull(verbKeyPath, "MUIVerb");

        // Fall back to the verb key's default value as display name.
        // Some apps (VS Code, WizTree, Notepad++) set the display name here
        // instead of MUIVerb. Skip indirect string references (@dll,-ID) since
        // resolving them requires P/Invoke. Strip & mnemonic markers for display.
        if (muiVerb is null)
        {
            var defaultValue = ReadStringOrNull(verbKeyPath, string.Empty);
            if (defaultValue is not null &&
                !defaultValue.StartsWith('@') &&
                !defaultValue.Equals(verbName, StringComparison.OrdinalIgnoreCase))
            {
                muiVerb = StripMnemonics(defaultValue);
            }
        }

        var icon = ReadStringOrNull(verbKeyPath, "Icon");
        var position = ReadStringOrNull(verbKeyPath, "Position");
        var appliesTo = ReadStringOrNull(verbKeyPath, "AppliesTo");

        // Extended = Shift-only (empty string value existence check)
        var isExtended = _registryService.ValueExists(verbKeyPath, "Extended").IsSuccess
                         && _registryService.ValueExists(verbKeyPath, "Extended").Value;

        // LegacyDisable (empty string value)
        var isLegacyDisabled = _registryService.ValueExists(verbKeyPath, "LegacyDisable").IsSuccess
                               && _registryService.ValueExists(verbKeyPath, "LegacyDisable").Value;

        // HasLUAShield (empty string value)
        var hasLuaShield = _registryService.ValueExists(verbKeyPath, "HasLUAShield").IsSuccess
                           && _registryService.ValueExists(verbKeyPath, "HasLUAShield").Value;

        // ProgrammaticAccessOnly (empty string value)
        var isProgrammaticAccessOnly = _registryService.ValueExists(verbKeyPath, "ProgrammaticAccessOnly").IsSuccess
                                       && _registryService.ValueExists(verbKeyPath, "ProgrammaticAccessOnly").Value;

        // Read command subkey: command line and DelegateExecute CLSID
        var commandKeyPath = $@"{verbKeyPath}\command";
        string? commandLine = null;
        string? delegateExecuteClsid = null;

        var commandResult = _registryService.ReadString(commandKeyPath, string.Empty);
        if (commandResult.IsSuccess && !string.IsNullOrWhiteSpace(commandResult.Value))
            commandLine = commandResult.Value;

        // DelegateExecute lives inside the command subkey per V4 audit
        var delegateResult = _registryService.ReadString(commandKeyPath, "DelegateExecute");
        if (delegateResult.IsSuccess && !string.IsNullOrWhiteSpace(delegateResult.Value))
            delegateExecuteClsid = delegateResult.Value;

        // Check for DropTarget subkey as alternate execution mechanism
        var dropTargetKeyPath = $@"{verbKeyPath}\DropTarget";
        var hasDropTarget = _registryService.KeyExists(dropTargetKeyPath).IsSuccess
                            && _registryService.KeyExists(dropTargetKeyPath).Value;

        // Verbs with no execution mechanism at all are still valid (shell-internal)
        // — e.g., .SpotlightLearnMore, EditStickers at DesktopBackground\shell

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

    private string? ReadStringOrNull(string keyPath, string valueName)
    {
        var result = _registryService.ReadString(keyPath, valueName);
        return result.IsSuccess ? result.Value : null;
    }

    /// <summary>
    /// Strips Windows &amp; mnemonic markers from display names.
    /// Single &amp; is removed; &amp;&amp; becomes a literal &amp;.
    /// </summary>
    private static string StripMnemonics(string value)
    {
        if (!value.Contains('&'))
            return value;

        var sb = new System.Text.StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] == '&' && i + 1 < value.Length)
            {
                if (value[i + 1] == '&')
                {
                    sb.Append('&');
                    i++; // skip second &
                }
                // else: skip the single & (mnemonic marker)
            }
            else
            {
                sb.Append(value[i]);
            }
        }
        return sb.ToString();
    }
}
