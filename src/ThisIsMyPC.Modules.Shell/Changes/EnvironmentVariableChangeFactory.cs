using ThisIsMyPC.Core.Changes;

namespace ThisIsMyPC.Modules.Shell.Changes;

// Stub for Story 2.5 — environment variable change creation
public static class EnvironmentVariableChangeFactory
{
    private const string ModuleId = "Explorer";

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
            SettingId = $"env-{scope.ToLowerInvariant()}-{name}",
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
}
