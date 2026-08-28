using CommunityToolkit.Mvvm.ComponentModel;
using ThisIsMyPC.Core.Sets;

namespace ThisIsMyPC.App.ViewModels;

/// <summary>One set card in the Set Loader browser.</summary>
public sealed partial class SetItemViewModel : ViewModelBase
{
    public SetItemViewModel(SetDefinition definition)
    {
        Definition = definition;
        ModulesAffected = string.Join(", ", definition.Entries
            .Select(e => e.ModuleId)
            .Distinct(StringComparer.Ordinal));
        MetaLine = $"{definition.Entries.Count} change{(definition.Entries.Count == 1 ? "" : "s")}"
            + $" · {ModulesAffected}"
            + $" · {(definition.Source == SetSource.BuiltIn ? "Built-in" : "User")} v{definition.Version}";
    }

    public SetDefinition Definition { get; }

    public string Name => Definition.Name;
    public string Description => Definition.Description;
    public string Author => Definition.Author;
    public string ModulesAffected { get; }
    public string MetaLine { get; }

    [ObservableProperty]
    private bool _isSelected;
}
