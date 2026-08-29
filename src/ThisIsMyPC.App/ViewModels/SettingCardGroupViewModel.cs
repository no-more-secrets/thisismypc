namespace ThisIsMyPC.App.ViewModels;

/// <summary>One section of a card-rendered module tab: header + its cards, in module order.</summary>
public sealed class SettingCardGroupViewModel
{
    public required string Header { get; init; }

    /// <summary>Optional one-line section explainer rendered under the header.</summary>
    public string? Subtitle { get; init; }

    public required IReadOnlyList<SettingCardViewModel> Cards { get; init; }
}
