namespace ThisIsMyPC.Modules.Privacy.Models;

public sealed record PrivacyScanData(
    IReadOnlyList<PrivacyPreference> Preferences,
    IReadOnlyList<PrivacyPreference> InkingTyping);
