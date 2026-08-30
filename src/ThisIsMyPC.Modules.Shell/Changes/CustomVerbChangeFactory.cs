using System.Text;
using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Modules.Shell.Models;

namespace ThisIsMyPC.Modules.Shell.Changes;

/// <summary>
/// Builds change descriptors for user-authored context menu entries (2-6). One
/// descriptor carries the whole entry: the serialized definition is the value, the
/// verb key path is the location, and the module materializes or removes the key
/// tree from it. AbsentValue on either side means "entry does not exist".
/// </summary>
public static class CustomVerbChangeFactory
{
    private const string ModuleId = "Context Menus";

    public static ChangeDescriptor CreateNew(CustomVerbDefinition definition) => new()
    {
        ModuleId = ModuleId,
        SettingId = MakeSettingId(definition),
        DisplayName = $"Custom menu entry: {definition.Label}",
        SystemLocation = definition.KeyPath,
        BeforeValue = ShellRegistryPaths.AbsentValue,
        AfterValue = definition.Serialize(),
        BeforeDisplay = "Not present",
        AfterDisplay = Describe(definition),
        ValueType = ChangeValueType.Shell_CustomVerb,
        Category = ChangeCategory.Create,
    };

    public static ChangeDescriptor CreateEdit(CustomVerbDefinition before, CustomVerbDefinition after)
    {
        if (before.Scope != after.Scope || before.VerbId != after.VerbId)
            throw new ArgumentException("Edit cannot move an entry between scopes or rename its verb key");

        return new ChangeDescriptor
        {
            ModuleId = ModuleId,
            SettingId = MakeSettingId(after),
            DisplayName = $"Custom menu entry: {after.Label}",
            SystemLocation = after.KeyPath,
            BeforeValue = before.Serialize(),
            AfterValue = after.Serialize(),
            BeforeDisplay = Describe(before),
            AfterDisplay = Describe(after),
            ValueType = ChangeValueType.Shell_CustomVerb,
            Category = ChangeCategory.Modify,
        };
    }

    public static ChangeDescriptor CreateDelete(CustomVerbDefinition definition) => new()
    {
        ModuleId = ModuleId,
        SettingId = MakeSettingId(definition),
        DisplayName = $"Custom menu entry: {definition.Label}",
        SystemLocation = definition.KeyPath,
        BeforeValue = definition.Serialize(),
        AfterValue = ShellRegistryPaths.AbsentValue,
        BeforeDisplay = Describe(definition),
        AfterDisplay = "Removed",
        ValueType = ChangeValueType.Shell_CustomVerb,
        Category = ChangeCategory.Delete,
    };

    public static string MakeSettingId(CustomVerbDefinition definition)
    {
        // "." separates scope from verb id; MakeVerbId slugs can't contain it, so
        // "Directory\Background" + "foo" can never collide with "Directory" + "background-foo".
        var scopeSlug = definition.Scope.ToLowerInvariant()
            .Replace('\\', '.').Replace("*", "allfiles");
        return $"ctx-custom-{scopeSlug}.{definition.VerbId.ToLowerInvariant()}";
    }

    /// <summary>Derives a registry-safe verb id from the entry label.</summary>
    public static string MakeVerbId(string label)
    {
        var builder = new StringBuilder(label.Length);
        foreach (var c in label)
        {
            if (char.IsAsciiLetterOrDigit(c))
                builder.Append(char.ToLowerInvariant(c));
            else if (c is ' ' or '-' or '_' && builder.Length > 0 && builder[^1] != '-')
                builder.Append('-');
        }
        var slug = builder.ToString().TrimEnd('-');
        return slug.Length > 0 ? slug : "entry";
    }

    private static string Describe(CustomVerbDefinition definition) =>
        $"\"{definition.Label}\" -> {definition.Command}";
}
