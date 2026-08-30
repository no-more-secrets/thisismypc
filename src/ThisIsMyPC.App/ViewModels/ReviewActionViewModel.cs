namespace ThisIsMyPC.App.ViewModels;

/// <summary>One staged one-way action in the review panel.</summary>
public sealed class ReviewActionViewModel : ViewModelBase
{
    public required string ActionId { get; init; }
    public required string DisplayName { get; init; }
    public required string Detail { get; init; }
    public string? UndoHint { get; init; }

    public bool HasUndoHint => !string.IsNullOrEmpty(UndoHint);
}
