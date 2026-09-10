using ThisIsMyPC.Lighting.Transport;

namespace ThisIsMyPC.Lighting.Tests.Fakes;

/// <summary>A scripted HID bus: collections by path, each answering feature reports from a handler.</summary>
internal sealed class FakeHidTransport : IHidTransport
{
    public List<HidDeviceInfo> Collections { get; } = [];

    /// <summary>Per path, the scripted device; a path without one refuses to open.</summary>
    public Dictionary<string, FakeHidDevice> Devices { get; } = new(StringComparer.OrdinalIgnoreCase);

    public List<string> Opened { get; } = [];

    public IReadOnlyList<HidDeviceInfo> Enumerate() => Collections.ToList();

    public IHidDevice? Open(string path)
    {
        Opened.Add(path);
        if (!Devices.TryGetValue(path, out var device))
            return null;
        device.OpenCount++;
        return device;
    }

    public static HidDeviceInfo Collection(string path, ushort vid, ushort pid, ushort usagePage, ushort usage = 1, int interfaceNumber = 0, int featureLength = 520) =>
        new(path, vid, pid, interfaceNumber, usagePage, usage, "Fake", "Fake device", "SN-1", 65, 65, featureLength);
}

/// <summary>One scripted collection. Feature reports are answered by delegates keyed on the report id.</summary>
internal sealed class FakeHidDevice(HidDeviceInfo info) : IHidDevice
{
    public HidDeviceInfo Info => info;
    public int OpenCount { get; set; }
    public int DisposeCount { get; private set; }
    public List<byte[]> SentFeatureReports { get; } = [];

    /// <summary>Returns the byte count to report, or -1 to refuse.</summary>
    public Func<byte[], int> OnSendFeature { get; set; } = _ => -1;

    /// <summary>Fills the buffer and returns the byte count, or -1 to refuse.</summary>
    public Func<byte[], int> OnGetFeature { get; set; } = _ => -1;

    public int SendFeatureReport(ReadOnlySpan<byte> report)
    {
        var copy = report.ToArray();
        SentFeatureReports.Add(copy);
        return OnSendFeature(copy);
    }

    public int GetFeatureReport(Span<byte> buffer)
    {
        var copy = buffer.ToArray();
        var result = OnGetFeature(copy);
        copy.AsSpan().CopyTo(buffer);
        return result;
    }

    public int Write(ReadOnlySpan<byte> report) => report.Length;

    public int Read(Span<byte> buffer, TimeSpan timeout) => 0;

    public void Dispose() => DisposeCount++;
}
