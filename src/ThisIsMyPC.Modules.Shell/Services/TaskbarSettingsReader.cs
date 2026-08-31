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

        // Win11 values: 0 hidden, 1 icon only, 2 icon and label, 3 search box (default)
        var searchboxResult = _registryService.ReadDWord(ShellRegistryPaths.SearchKeyPath, "SearchboxTaskbarMode");
        var searchboxMode = searchboxResult.IsSuccess ? searchboxResult.Value! : 3;

        // 0 always combine and hide labels (default), 1 combine when full, 2 never
        var glomResult = _registryService.ReadDWord(ShellRegistryPaths.AdvancedKeyPath, "TaskbarGlomLevel");
        var buttonCombining = glomResult.IsSuccess ? glomResult.Value! : 0;

        return new TaskbarSettings(
            alignment, widgetsEnabled, classicContextMenu, classicCommandBar,
            searchboxMode, buttonCombining);
    }
}
