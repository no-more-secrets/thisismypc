using ThisIsMyPC.Core.Changes;

namespace ThisIsMyPC.Modules.Hardware.Cooling;

/// <summary>One reversible profile file change, separate from loading the profile in FanControl.</summary>
public static class FanControlProfileChangeFactory
{
    public static ChangeGroup Create(string name, string path, string before, byte[] after) => new()
    {
        GroupId = FanControlProfileStore.SettingPrefix + name.ToUpperInvariant(),
        DisplayName = $"Save cooling profile: {name}",
        Description = "Save the profile file. Load it in FanControl when you want to use it.",
        Changes = [new ChangeDescriptor
        {
            ModuleId = CoolingModule.ModuleName,
            SettingId = FanControlProfileStore.SettingPrefix + name,
            DisplayName = $"Cooling profile: {name}",
            SystemLocation = path,
            BeforeValue = before,
            AfterValue = Convert.ToBase64String(after),
            BeforeDisplay = before == FanControlProfileStore.Missing ? "No saved profile" : "Existing saved profile",
            AfterDisplay = "Edited saved profile",
            ValueType = ChangeValueType.File_Content,
            Category = before == FanControlProfileStore.Missing ? ChangeCategory.Create : ChangeCategory.Modify,
        }],
    };
}
