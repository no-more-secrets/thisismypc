using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Modules.Shell.Models;

namespace ThisIsMyPC.Modules.Shell.Services;

/// <summary>
/// Enumerates ThisIsMyPC-created custom context menu entries (2-6): the
/// ThisIsMyPC.*-prefixed verb keys under each supported HKCU classes scope.
/// Foreign verbs are invisible here by design.
/// </summary>
public sealed class CustomVerbService
{
    private readonly IRegistryService _registry;

    public CustomVerbService(IRegistryService registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        _registry = registry;
    }

    public IReadOnlyList<CustomVerbDefinition> Enumerate()
    {
        var entries = new List<CustomVerbDefinition>();
        foreach (var (scope, _) in CustomVerbDefinition.ScopeOptions)
        {
            var shellKey = $@"HKCU\Software\Classes\{scope}\shell";
            var subKeys = _registry.EnumerateSubKeys(shellKey);
            if (!subKeys.IsSuccess || subKeys.Value is null)
                continue;

            foreach (var keyName in subKeys.Value)
            {
                if (!keyName.StartsWith(CustomVerbDefinition.VerbKeyPrefix, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (Read(scope, keyName) is { } definition)
                    entries.Add(definition);
            }
        }
        return entries;
    }

    private CustomVerbDefinition? Read(string scope, string keyName)
    {
        var keyPath = $@"HKCU\Software\Classes\{scope}\shell\{keyName}";
        var label = _registry.ReadString(keyPath, string.Empty);
        var command = _registry.ReadString($@"{keyPath}\command", string.Empty);
        if (!command.IsSuccess || string.IsNullOrWhiteSpace(command.Value))
            return null; // half-written or externally mangled entry — not editable

        var icon = _registry.ReadString(keyPath, "Icon");
        return new CustomVerbDefinition
        {
            Scope = scope,
            VerbId = keyName[CustomVerbDefinition.VerbKeyPrefix.Length..],
            Label = label is { IsSuccess: true, Value: { Length: > 0 } l }
                ? l
                : keyName[CustomVerbDefinition.VerbKeyPrefix.Length..],
            Command = command.Value,
            IconPath = icon is { IsSuccess: true, Value: { Length: > 0 } i } ? i : null,
        };
    }
}
