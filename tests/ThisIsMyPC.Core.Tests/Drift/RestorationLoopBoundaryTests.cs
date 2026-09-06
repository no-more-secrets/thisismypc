using ThisIsMyPC.Core.Drift;

namespace ThisIsMyPC.Core.Tests.Drift;

public sealed class RestorationLoopBoundaryTests
{
    [Fact]
    public async Task ConsentWithdrawnDuringAcquisitionPreventsWrite()
    {
        using var f = new RestorationLoopFixture();
        await f.InitializeAsync();
        f.Recovering = _ => f.Consent.Enabled = false;
        Assert.Equal(RestorationScanStatus.ConsentOff, (await f.Loop.ScanAsync()).Status);
        Assert.Equal(0, f.Registry.Writes);
        Assert.Empty(await f.Repository.GetAllAsync());
    }

    [Fact]
    public async Task ThrownWriteRecordsUncertaintyAndBlocksNextScan()
    {
        using var f = new RestorationLoopFixture();
        await f.InitializeAsync();
        f.Registry.BeforeWrite = () => throw new InvalidOperationException("Simulated interrupted write");
        Assert.Equal(RestorationScanStatus.Uncertain, (await f.Loop.ScanAsync()).Status);
        f.Registry.Value = f.Target.SuppressedValue;
        Assert.Equal(RestorationScanStatus.RecoveryRequired, (await f.Loop.ScanAsync()).Status);
        Assert.All(await f.Repository.GetAllAsync(), row => Assert.False(row.SupportsGenericUndo));
    }

    [Fact]
    public async Task CancellationAfterIntentStillRecordsVerifiedOutcome()
    {
        using var f = new RestorationLoopFixture();
        await f.InitializeAsync();
        using var cancellation = new CancellationTokenSource();
        var verifyIntent = f.Registry.BeforeWrite;
        f.Registry.BeforeWrite = () => { verifyIntent!(); cancellation.Cancel(); };
        Assert.Equal(RestorationScanStatus.Restored, (await f.Loop.ScanAsync(cancellation.Token)).Status);
        Assert.Equal("Applied", Assert.Single(await f.Repository.GetAllAsync()).JournalOutcome);
    }
}
