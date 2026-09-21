using ThisIsMyPC.Core.Hardware.Sensors;

namespace ThisIsMyPC.Core.Tests.Hardware;

public sealed class HardwareSensorHistoryTests
{
    private static readonly DateTimeOffset Epoch = DateTimeOffset.UnixEpoch;
    private static HardwareSensorReading Reading(string id, double? value) =>
        new(id, "device", "GPU", HardwareSensorComponent.Gpu, id, HardwareSensorUnit.Celsius, value);
    private static HardwareSensorSnapshot Snapshot(int seconds, params HardwareSensorReading[] readings) =>
        new(Epoch.AddSeconds(seconds), readings, []);

    [Fact]
    public void MissingAndNonFiniteValuesBecomeGapsAndDoNotCountAsZero()
    {
        var history = new HardwareSensorHistory();
        history.Update(Snapshot(0, Reading("a", 20)));
        history.Update(Snapshot(1, Reading("a", double.NaN)));
        history.Update(Snapshot(2, Reading("a", double.PositiveInfinity)));
        var result = Assert.Single(history.Update(Snapshot(3)));
        Assert.Null(result.Current);
        Assert.Equal(20d, result.Minimum);
        Assert.Equal(20d, result.Maximum);
        Assert.Equal(20d, result.Average);
        Assert.Equal(4, result.History.Count);
        Assert.All(result.History.Skip(1), sample => Assert.Null(sample.Value));
    }

    [Fact]
    public void WindowDropsOldValuesAndRetainsMeasuredZero()
    {
        var history = new HardwareSensorHistory(sampleLimit: 2);
        history.Update(Snapshot(0, Reading("a", 100)));
        history.Update(Snapshot(1, Reading("a", 0)));
        var result = Assert.Single(history.Update(Snapshot(2, Reading("a", 10))));
        Assert.Equal(0d, result.Minimum);
        Assert.Equal(10d, result.Maximum);
        Assert.Equal(5d, result.Average);
        Assert.Equal(2, result.History.Count);
    }

    [Fact]
    public void StaleIdsExpireAndNewIdsCannotExceedBound()
    {
        var history = new HardwareSensorHistory(sensorLimit: 1, staleAfter: TimeSpan.FromSeconds(2));
        Assert.Single(history.Update(Snapshot(0, Reading("a", 1), Reading("b", 2))));
        var result = Assert.Single(history.Update(Snapshot(2, Reading("b", 2))));
        Assert.Equal("b", result.Reading.Id);
    }

    [Fact]
    public void DuplicateAndOlderSnapshotsDoNotAddSamples()
    {
        var history = new HardwareSensorHistory();
        history.Update(Snapshot(2, Reading("a", 5), Reading("a", 90)));
        history.Update(Snapshot(2, Reading("a", 6)));
        var result = Assert.Single(history.Update(Snapshot(1, Reading("a", 7))));
        Assert.Single(result.History);
        Assert.Equal(5d, result.Current);
    }

    [Fact]
    public void UnitChangeStartsNewSeries()
    {
        var history = new HardwareSensorHistory();
        history.Update(Snapshot(0, Reading("a", 100)));
        var result = Assert.Single(history.Update(Snapshot(1, Reading("a", 2) with { Unit = HardwareSensorUnit.Volts })));
        Assert.Single(result.History);
        Assert.Equal(2d, result.Average);
    }

    [Fact]
    public void ClearStartsFreshAndFiniteLargeReadingsKeepFiniteAverage()
    {
        var history = new HardwareSensorHistory();
        history.Update(Snapshot(0, Reading("a", double.MaxValue)));
        history.Update(Snapshot(1, Reading("a", double.MaxValue)));
        var result = Assert.Single(history.Update(Snapshot(2, Reading("a", double.MaxValue))));
        Assert.True(double.IsFinite(result.Average!.Value));
        history.Clear();
        Assert.Empty(history.Update(Snapshot(0)));
    }
}
