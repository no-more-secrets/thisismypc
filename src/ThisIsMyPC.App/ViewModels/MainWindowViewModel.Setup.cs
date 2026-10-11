using CommunityToolkit.Mvvm.Input;
using ThisIsMyPC.Core.Sets;

namespace ThisIsMyPC.App.ViewModels;

public partial class MainWindowViewModel
{
    private SetupGuideViewModel? _setupGuide;
    private string? _guideModuleName;

    public bool CanReturnToGuide => _guideModuleName is not null && SelectedModule?.Name == _guideModuleName;

    private void OpenModuleFromGuide(string moduleName)
    {
        _guideModuleName = moduleName;
        NavigateToModuleByName(moduleName);
        OnPropertyChanged(nameof(CanReturnToGuide));
    }

    partial void OnSelectedModuleChanged(SidebarItemViewModel? value)
    {
        if (value?.Name != _guideModuleName)
            _guideModuleName = null;
        OnPropertyChanged(nameof(CanReturnToGuide));
    }

    [RelayCommand]
    private async Task OpenSetupGuideAsync()
    {
        _contentEpoch++;
        IsModuleLoading = false;
        (CurrentContent as IDisposable)?.Dispose();
        _setupGuide ??= new SetupGuideViewModel(_navigationService.Modules, _hardwareDetection,
            excluded => _ = OpenDesktopPackAsync(excluded), OpenModuleFromGuide, OpenHome, _pendingChangesService, _userFeedback, _autorunEnrichment);
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
