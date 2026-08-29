using ThisIsMyPC.Core.Changes;

namespace ThisIsMyPC.Core.Cards;

/// <summary>
/// A card model paired with the behavior the host needs to make it interactive.
/// The model stays a pure POCO; the delegates carry the module's change factory and
/// live-state read. Factories re-read live state at stage time — BeforeValues must
/// never come from scan-time snapshots.
/// </summary>
public sealed record SettingCardSource
{
    public required SettingCardModel Model { get; init; }

    /// <summary>
    /// Builds the ChangeGroup that staging the toggle's desired state produces
    /// (Toggle control type only for now).
    /// </summary>
    public required Func<bool, ChangeGroup> CreateToggleGroup { get; init; }

    /// <summary>Reads the current live state (registry truth) for the toggle.</summary>
    public required Func<bool> ReadCurrentState { get; init; }
}
