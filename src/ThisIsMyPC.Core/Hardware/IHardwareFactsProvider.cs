namespace ThisIsMyPC.Core.Hardware;

/// <summary>
/// One module-level detection pass: the facts the policy reasons over, the
/// shared inventory they were built from, and what the tabs need that the
/// policy does not (where each companion can be launched from, and the
/// detection notes behind each observation).
/// </summary>
public sealed record HardwareDetectionSnapshot(
    ObservedHardwareFacts Facts,
    HardwareSnapshot Inventory,
    IReadOnlyDictionary<CompanionApp, string> LaunchPaths,
    IReadOnlyList<string> Notes,
    DateTimeOffset ObservedAt)
{
    /// <summary>Executable to launch for <paramref name="app"/>, or null when detection found none.</summary>
    public string? LaunchPathOf(CompanionApp app) => LaunchPaths.GetValueOrDefault(app);
}

/// <summary>
/// Session-scoped facts for the Hardware tabs, layered over
/// <see cref="IHardwareDetectionService"/>. The first call runs a full pass;
/// later calls return the cached snapshot unless a refresh is requested. Every
/// read is a probe of the live machine and nothing is written.
/// </summary>
public interface IHardwareFactsProvider
{
    /// <summary>The last snapshot, or null before the first detection pass.</summary>
    HardwareDetectionSnapshot? Current { get; }

    /// <summary>Returns the cached snapshot, or runs detection when none exists or <paramref name="refresh"/> is set.</summary>
    Task<HardwareDetectionSnapshot> GetAsync(bool refresh = false, CancellationToken cancellationToken = default);

    /// <summary>Raised after every completed detection pass.</summary>
    event EventHandler? Changed;
}
