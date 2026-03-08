using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Modules.Shell.Models;

namespace ThisIsMyPC.Modules.Shell.Services;

public sealed class TaskbarSettingsReader
{
    private const string AdvancedKeyPath = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
    private const string ClassicContextMenuKeyPath = @"HKCU\Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32";

    private readonly IRegistryService _registryService;

    public TaskbarSettingsReader(IRegistryService registryService)
    {
        _registryService = registryService;
    }

    public TaskbarSettings Read()
    {
        var alignmentResult = _registryService.ReadDWord(AdvancedKeyPath, "TaskbarAl");
        var alignment = alignmentResult.IsSuccess ? alignmentResult.Value! : 1; // default Center

        var widgetsResult = _registryService.ReadDWord(AdvancedKeyPath, "TaskbarDa");
        var widgetsEnabled = widgetsResult.IsSuccess && widgetsResult.Value! == 1;

        var classicMenuResult = _registryService.KeyExists(ClassicContextMenuKeyPath);
        var classicContextMenu = classicMenuResult.IsSuccess && classicMenuResult.Value!;

        return new TaskbarSettings(alignment, widgetsEnabled, classicContextMenu);
    }
}
