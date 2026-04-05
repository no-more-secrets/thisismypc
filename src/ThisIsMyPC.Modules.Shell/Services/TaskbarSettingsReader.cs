using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Modules.Shell.Models;

namespace ThisIsMyPC.Modules.Shell.Services;

public sealed class TaskbarSettingsReader
{

    private readonly IRegistryService _registryService;

    public TaskbarSettingsReader(IRegistryService registryService)
    {
        _registryService = registryService;
    }

    public TaskbarSettings Read()
    {
        var alignmentResult = _registryService.ReadDWord(ShellRegistryPaths.AdvancedKeyPath, "TaskbarAl");
        var alignment = alignmentResult.IsSuccess ? alignmentResult.Value! : 1; // default Center

        var widgetsResult = _registryService.ReadDWord(ShellRegistryPaths.AdvancedKeyPath, "TaskbarDa");
        var widgetsEnabled = widgetsResult.IsSuccess && widgetsResult.Value! == 1;

        var classicMenuResult = _registryService.KeyExists(ShellRegistryPaths.ClassicContextMenuKeyPath);
        var classicContextMenu = classicMenuResult.IsSuccess && classicMenuResult.Value!;

        var commandBarResult = _registryService.KeyExists(ShellRegistryPaths.CommandBarKeyPath);
        var classicCommandBar = commandBarResult.IsSuccess && commandBarResult.Value!;

        return new TaskbarSettings(alignment, widgetsEnabled, classicContextMenu, classicCommandBar);
    }
}
