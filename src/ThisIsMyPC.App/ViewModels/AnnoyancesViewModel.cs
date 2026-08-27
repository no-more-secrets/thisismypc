using System.Collections.ObjectModel;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Modules.Annoyances.Changes;
using ThisIsMyPC.Modules.Annoyances.Models;

namespace ThisIsMyPC.App.ViewModels;

public partial class AnnoyancesViewModel : ViewModelBase
{
    public ObservableCollection<ShellSettingViewModel> ScoobeAndWelcomeSettings { get; } = [];

    public AnnoyancesViewModel(
        AnnoyancesScanData scanData,
        IPendingChangesService pendingChangesService,
        IRegistryService registryService)
    {
        foreach (var pref in scanData.Preferences.Where(p => p.Section == AnnoyanceSection.ScoobeAndWelcome))
        {
            var captured = pref;
            ScoobeAndWelcomeSettings.Add(new ShellSettingViewModel(
                label: captured.DisplayName,
                description: captured.Description,
                systemPath: $@"{captured.RegistryKeyPath}\{captured.RegistryValueName}",
                isEnabled: captured.IsSuppressed,
                pendingChangesService: pendingChangesService,
                changeFactory: suppress => AnnoyanceChangeFactory.CreateToggle(captured, suppress),
                readRegistryState: () => ReadSuppressed(registryService, captured)));
        }
    }

    private static bool ReadSuppressed(IRegistryService registryService, AnnoyancePreference pref)
    {
        var result = registryService.ReadDWord(pref.RegistryKeyPath, pref.RegistryValueName);
        if (!result.IsSuccess)
            return pref.DefaultValue == pref.SuppressedValue; // missing value = Windows default
        return result.Value.ToString() == pref.SuppressedValue;
    }
}
