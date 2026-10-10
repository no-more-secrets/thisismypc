using CommunityToolkit.Mvvm.Input;
using ThisIsMyPC.Core.Sets;

namespace ThisIsMyPC.App.ViewModels;

public partial class MainWindowViewModel
{
    private SetupGuideViewModel? _setupGuide;

    [RelayCommand]
    private async Task OpenSetupGuideAsync()
    {
        _contentEpoch++;
        IsModuleLoading = false;
        (CurrentContent as IDisposable)?.Dispose();
        _setupGuide ??= new SetupGuideViewModel(_navigationService.Modules, _hardwareDetection,
            excluded => _ = OpenDesktopPackAsync(excluded), NavigateToModuleByName, OpenHome);
        ContentTitle = "Intro guide";
        ContentDescription = "Learn each module, choose your changes, and keep control";
        CurrentContent = _setupGuide;
        IsHomeActive = false;
        IsSetLoaderActive = false;
        IsSettingsActive = false;
        IsDebugActive = false;
        SelectedModule = null;
        ClearSidebarActives();
        await _setupGuide.CheckDesktopAsync().ConfigureAwait(true);
    }

    private async Task OpenDesktopPackAsync(IReadOnlyList<string> excluded)
    {
        OpenSetLoader();
        if (CurrentContent is not SetLoaderViewModel loader) return;
        var pack = loader.OptimizationPacks.FirstOrDefault(s =>
            s.Definition.Source == SetSource.BuiltIn && s.Name == SetupGuideViewModel.PackName);
        if (pack is null)
        {
            SetStatus("The Desktop Grand Pack is missing. Repair the installation to restore bundled packs.", StatusSeverity.Warning);
            return;
        }
        loader.SelectSetCommand.Execute(pack);
        loader.ExcludeModules(excluded);
        await loader.PackInspection.ConfigureAwait(true);
    }
}
