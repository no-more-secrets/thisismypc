using ThisIsMyPC.Core.Changes;

namespace ThisIsMyPC.Modules.Shell.Changes;

public static class EnvironmentVariableChangeFactory
{
    private const string ModuleId = "Environment";

    public static ChangeDescriptor CreateModify(
        string name,
        string currentValue,
        string newValue,
        string scope,
        string registryKeyPath)
    {
        return new ChangeDescriptor
        {
            ModuleId = ModuleId,
            SettingId = $"env-{scope.ToLowerInvariant()}-{name.ToLowerInvariant()}",
            DisplayName = $"Environment variable: {name}",
            SystemLocation = $@"{registryKeyPath}\{name}",
            BeforeValue = currentValue,
            AfterValue = newValue,
            BeforeDisplay = currentValue,
            AfterDisplay = newValue,
            ValueType = ChangeValueType.Environment_Variable,
            Category = ChangeCategory.Modify,
        };
    }

    public static ChangeDescriptor CreateAdd(
        string name,
        string value,
        string scope,
        string registryKeyPath)
    {
        return new ChangeDescriptor
        {
            ModuleId = ModuleId,
            SettingId = $"env-{scope.ToLowerInvariant()}-{name.ToLowerInvariant()}",
            DisplayName = $"Environment variable: {name}",
            SystemLocation = $@"{registryKeyPath}\{name}",
            BeforeValue = "",
            AfterValue = value,
            BeforeDisplay = "(new)",
            AfterDisplay = value,
            ValueType = ChangeValueType.Environment_Variable,
            Category = ChangeCategory.Create,
        };
    }

    public static ChangeDescriptor CreateDelete(
        string name,
        string currentValue,
        string scope,
        string registryKeyPath)
    {
        return new ChangeDescriptor
        {
            ModuleId = ModuleId,
            SettingId = $"env-{scope.ToLowerInvariant()}-{name.ToLowerInvariant()}",
            DisplayName = $"Environment variable: {name}",
            SystemLocation = $@"{registryKeyPath}\{name}",
            BeforeValue = currentValue,
            AfterValue = null,
            BeforeDisplay = currentValue,
            AfterDisplay = "(deleted)",
            ValueType = ChangeValueType.Environment_Variable,
            Category = ChangeCategory.Delete,
        };
    }

    public static ChangeDescriptor CreatePathEdit(
        string scope,
        string registryKeyPath,
        string previousFullPath,
        string newFullPath,
        string humanReadableDiff)
    {
        return new ChangeDescriptor
        {
            ModuleId = ModuleId,
            SettingId = $"env-{scope.ToLowerInvariant()}-path",
            DisplayName = "Environment variable: PATH",
            SystemLocation = $@"{registryKeyPath}\PATH",
            BeforeValue = previousFullPath,
            AfterValue = newFullPath,
            BeforeDisplay = humanReadableDiff,
            AfterDisplay = humanReadableDiff,
            ValueType = ChangeValueType.Environment_Variable,
            Category = ChangeCategory.Modify,
        };
    }
}
