using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Modules.Shell.Models;
namespace ThisIsMyPC.Modules.Shell.Changes;
public static class ExplorerPatcherStartChanges
{
    public const string StartKey = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\ExplorerPatcher";
    public static bool IsMirrored(string name) => name is "Start_MaximumFrequentApps" or "StartDocked_DisableRecommendedSection" or "StartUI_EnableRoundedCorners" or "StartUI_ShowMoreTiles";
    public static ChangeGroup Create(IRegistryService registry, ExplorerPatcherSetting setting, int value)
    {
        var keys = IsMirrored(setting.RegistryValueName) ? new[] { setting.RegistryKeyPath, StartKey } : [setting.RegistryKeyPath];
        var changes = keys.Select(key =>
        {
            var exists = registry.ValueExists(key, setting.RegistryValueName);
            if (!exists.IsSuccess)
                throw new InvalidOperationException("The current ExplorerPatcher setting could not be read.");
            var read = registry.ReadValue(key, setting.RegistryValueName);
            if (exists.Value && (!read.IsSuccess || read.Value!.Kind != RegistryValueDataKind.DWord))
                throw new InvalidOperationException("The current ExplorerPatcher setting could not be captured safely.");
            return ExplorerPatcherChangeFactory.Create(setting with { RegistryKeyPath = key }, exists.Value ? read.Value!.AsDWord() : null, value);
        }).ToArray();
        return new() { GroupId = Guid.NewGuid().ToString(), DisplayName = setting.DisplayName, Description = setting.Description, Changes = changes };
    }
}
