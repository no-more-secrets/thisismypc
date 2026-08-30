namespace ThisIsMyPC.App.ViewModels;

/// <summary>One section of a card-rendered module tab: header + its cards, in module order.</summary>
public sealed partial class SettingCardGroupViewModel : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
{
    public required string Header { get; init; }

    /// <summary>False when a search hides every card in the group.</summary>
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private bool _isSearchVisible = true;

    public void ApplySearch(string query)
    {
        foreach (var card in Cards)
            card.ApplySearch(query);
        IsSearchVisible = Cards.Any(c => c.IsSearchVisible);
    }

    /// <summary>Optional one-line section explainer rendered under the header.</summary>
    public string? Subtitle { get; init; }

    public required IReadOnlyList<SettingCardViewModel> Cards { get; init; }
}
