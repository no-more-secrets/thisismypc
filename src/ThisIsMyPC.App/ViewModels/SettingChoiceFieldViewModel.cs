using CommunityToolkit.Mvvm.ComponentModel;
using ThisIsMyPC.Core.Cards;

namespace ThisIsMyPC.App.ViewModels;

public sealed partial class SettingChoiceFieldViewModel : ViewModelBase
{
    public SettingChoiceField Field { get; }
    public string DisplayName => Field.DisplayName;
    public IReadOnlyList<SettingOption> Options => Field.Options;
    [ObservableProperty] private SettingOption? _selectedOption;
    [ObservableProperty] private bool _isVisible;

    public SettingChoiceFieldViewModel(SettingChoiceField field)
    {
        Field = field;
        Reload();
    }

    public void Reload() => SelectedOption = Options.FirstOrDefault(option => option.Value == Field.ReadCurrentValue());
}
