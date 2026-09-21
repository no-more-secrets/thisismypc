namespace ThisIsMyPC.Core.Hardware.Sensors;

/// <summary>Bounded, UI-independent sensor history. Call from one sampling loop.</summary>
public sealed class HardwareSensorHistory
{
    private readonly int _sampleLimit;
    private readonly int _sensorLimit;
    private readonly TimeSpan _staleAfter;
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private DateTimeOffset? _latest;

    public HardwareSensorHistory(int sampleLimit = 120, int sensorLimit = 256, TimeSpan? staleAfter = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(sampleLimit, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(sampleLimit, 3600);
        ArgumentOutOfRangeException.ThrowIfLessThan(sensorLimit, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(sensorLimit, 4096);
        _staleAfter = staleAfter ?? TimeSpan.FromMinutes(2);
        if (_staleAfter <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(staleAfter));
        _sampleLimit = sampleLimit;
        _sensorLimit = sensorLimit;
    }

    public IReadOnlyList<HardwareSensorStatistics> Update(HardwareSensorSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (_latest is { } latest && snapshot.CapturedAt <= latest) return GetStatistics();
        _latest = snapshot.CapturedAt;
        foreach (var id in _entries.Where(pair => snapshot.CapturedAt - pair.Value.LastSeen >= _staleAfter)
                     .Select(pair => pair.Key).ToArray())
            _entries.Remove(id);

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var reading in snapshot.Readings)
        {
            if (string.IsNullOrWhiteSpace(reading.Id) || !seen.Add(reading.Id)) continue;
            if (!_entries.TryGetValue(reading.Id, out var entry))
            {
                if (_entries.Count >= _sensorLimit) continue;
                entry = new Entry(reading);
                _entries.Add(reading.Id, entry);
            }
            if (entry.Reading.Unit != reading.Unit || entry.Reading.DeviceId != reading.DeviceId)
                entry.Samples.Clear();
            var value = reading.Value is { } number && double.IsFinite(number) ? number : (double?)null;
            entry.Reading = reading with { Value = value };
            entry.LastSeen = snapshot.CapturedAt;
            Add(entry, snapshot.CapturedAt, value);
        }
        foreach (var pair in _entries)
        {
            if (seen.Contains(pair.Key)) continue;
            pair.Value.Reading = pair.Value.Reading with { Value = null };
            Add(pair.Value, snapshot.CapturedAt, null);
        }
        return GetStatistics();
    }

    public void Clear()
    {
        _entries.Clear();
        _latest = null;
    }

    private void Add(Entry entry, DateTimeOffset time, double? value)
    {
        entry.Samples.Enqueue(new(time, value));
        while (entry.Samples.Count > _sampleLimit) entry.Samples.Dequeue();
    }

    private HardwareSensorStatistics[] GetStatistics() => _entries.Values.Select(entry =>
    {
        var samples = entry.Samples.ToArray();
        var valid = samples.Where(sample => sample.Value.HasValue).Select(sample => sample.Value!.Value).ToArray();
        // Normalize before summing. Clamp rounding at the finite range boundary.
        var scale = valid.Length == 0 ? 0 : valid.Max(value => Math.Abs(value));
        double? average = valid.Length == 0 ? null : scale == 0 ? 0
            : Math.Clamp(valid.Sum(value => value / scale) / valid.Length, -1d, 1d) * scale;
        return new HardwareSensorStatistics(entry.Reading, entry.Reading.Value,
            valid.Length == 0 ? null : valid.Min(), valid.Length == 0 ? null : valid.Max(),
            average, Array.AsReadOnly(samples));
    }).ToArray();

    private sealed class Entry(HardwareSensorReading reading)
    {
        public HardwareSensorReading Reading { get; set; } = reading;
        public DateTimeOffset LastSeen { get; set; }
        public Queue<HardwareSensorSample> Samples { get; } = new();
    }
}
