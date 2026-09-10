using ThisIsMyPC.Core.Hardware;

namespace ThisIsMyPC.Modules.Hardware.Tests.Fakes;

/// <summary>Hands out a scripted snapshot and counts detection passes.</summary>
public sealed class FakeHardwareFactsProvider : IHardwareFactsProvider
{
    private readonly Func<HardwareDetectionSnapshot> _detect;

    public FakeHardwareFactsProvider(
        ObservedHardwareFacts facts,
        IReadOnlyDictionary<CompanionApp, string>? launchPaths = null,
        DateTimeOffset? observedAt = null)
        : this(() => new HardwareDetectionSnapshot(
            facts,
            new HardwareSnapshot { Facts = facts },
            launchPaths ?? new Dictionary<CompanionApp, string>(),
            ["fake detection"],
            observedAt ?? DateTimeOffset.Now))
    {
    }

    public FakeHardwareFactsProvider(Func<HardwareDetectionSnapshot> detect)
    {
        _detect = detect;
    }

    public int Passes { get; private set; }

    /// <summary>Passes requested with refresh.</summary>
    public int Refreshes { get; private set; }

    public HardwareDetectionSnapshot? Current { get; private set; }

    public event EventHandler? Changed;

    public Task<HardwareDetectionSnapshot> GetAsync(bool refresh = false, CancellationToken cancellationToken = default)
    {
        if (refresh)
            Refreshes++;
        if (refresh || Current is null)
        {
            Passes++;
            Current = _detect();
            Changed?.Invoke(this, EventArgs.Empty);
        }
        return Task.FromResult(Current);
    }
}
