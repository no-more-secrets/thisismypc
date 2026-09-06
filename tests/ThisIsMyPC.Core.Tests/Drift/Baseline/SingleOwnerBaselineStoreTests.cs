using ThisIsMyPC.Core.Drift;
using ThisIsMyPC.Core.Drift.Baseline;
using ThisIsMyPC.Core.Tests.Coordination;

namespace ThisIsMyPC.Core.Tests.Drift.Baseline;

public sealed class SingleOwnerBaselineStoreTests
{
    private const string Sid = "S-1-5-21-111-222-333-1001";
    private static SingleOwnerBaselineEntry Entry(int index = 0)
    {
        var target = RestorationCatalog.Default.Targets[index];
        return new(target.ModuleId, target.SettingId, target.KeyPath + "\\" + target.ValueName,
            target.SuppressedValue, DateTimeOffset.UtcNow);
    }
    private static FakeMutationLease Lease(bool recovered = true)
    {
        var lease = new FakeMutationLease("test", "test", false);
        if (recovered) lease.MarkRecovered();
        return lease;
    }
    [Fact]
    public void MergePreservesChosenValuesAndCanonicalizesLocationsAcrossReopen()
    {
        var storage = new Storage();
        var store = new SingleOwnerBaselineStore(storage, "test", Sid);
        using var lease = Lease();
        var first = Entry();
        var second = Entry(1);
        store.RecordApplied([first with { CanonicalLocation = first.CanonicalLocation.ToLowerInvariant() }, second], lease);
        var updated = first with { Expected = RestorationCatalog.Default.Targets[0].WindowsDefaultValue };
        store.RecordApplied([updated], lease);
        var rows = new SingleOwnerBaselineStore(storage, "test", Sid).Read(lease);
        Assert.Equal(2, rows.Count);
        Assert.Contains(updated, rows);
        Assert.Contains(second, rows);
    }
    [Fact]
    public void InvalidBatchNeverChangesExistingDocument()
    {
        var storage = new Storage();
        var store = new SingleOwnerBaselineStore(storage, "test", Sid);
        using var lease = Lease();
        store.RecordApplied([Entry()], lease);
        var original = storage.Bytes;
        Assert.Throws<InvalidDataException>(() => store.RecordApplied([Entry(), Entry()], lease));
        Assert.Throws<InvalidDataException>(() => store.RecordApplied([Entry() with { CanonicalLocation = "HKU\\wrong" }], lease));
        Assert.Same(original, storage.Bytes);
    }
    [Theory]
    [InlineData("null")]
    [InlineData("{\"Version\":2,\"Entries\":[]}")]
    [InlineData("{\"Version\":1,\"Entries\":[],\"Unknown\":true}")]
    [InlineData("broken")]
    public void CorruptionCannotBecomeEmptyBaseline(string json)
    {
        var storage = new Storage { Bytes = System.Text.Encoding.UTF8.GetBytes(json) };
        var store = new SingleOwnerBaselineStore(storage, "test", Sid);
        using var lease = Lease();
        Assert.Throws<InvalidDataException>(() => store.Read(lease));
        Assert.Throws<InvalidDataException>(() => store.RecordApplied([Entry()], lease));
        Assert.Equal(0, storage.Writes);
    }
    [Fact]
    public void OriginalStorageErrorsPropagateAndFailedWriteDoesNotPretendSuccess()
    {
        var storage = new Storage();
        var store = new SingleOwnerBaselineStore(storage, "test", Sid);
        using var lease = Lease();
        var failure = new IOException("disk failure");
        storage.ReadFailure = failure;
        Assert.Same(failure, Assert.Throws<IOException>(() => store.RecordApplied([Entry()], lease)));
        storage.ReadFailure = null;
        storage.WriteFailure = failure;
        Assert.Same(failure, Assert.Throws<IOException>(() => store.RecordApplied([Entry()], lease)));
        Assert.Null(storage.Bytes);
    }
    [Fact]
    public void RecoveryCanReadButCannotWriteAndWrongLeaseCannotRead()
    {
        var store = new SingleOwnerBaselineStore(new Storage(), "test", Sid);
        using var lease = Lease(false);
        Assert.Empty(store.Read(lease));
        Assert.Throws<InvalidOperationException>(() => store.RecordApplied([Entry()], lease));
        using var other = new FakeMutationLease("other", "test", false);
        Assert.Throws<InvalidOperationException>(() => store.Read(other));
        lease.Dispose();
        Assert.Throws<InvalidOperationException>(() => store.Read(lease));
    }
    [Theory]
    [InlineData("")]
    [InlineData("S-1-5-18")]
    [InlineData("S-1-5-21-111-222-333-1001/other")]
    public void InvalidPrimaryOwnerIsRefused(string sid)
        => Assert.Throws<ArgumentException>(() => new SingleOwnerBaselineStore(new Storage(), "test", sid));

    [Fact]
    public void DifferentPrimaryOwnerCannotReadOrReplaceExistingChoices()
    {
        var storage = new Storage();
        using var lease = Lease();
        new SingleOwnerBaselineStore(storage, "test", Sid).RecordApplied([Entry()], lease);
        var original = storage.Bytes;
        var other = new SingleOwnerBaselineStore(storage, "test", "S-1-5-21-111-222-333-1002");
        Assert.Throws<InvalidDataException>(() => other.Read(lease));
        Assert.Throws<InvalidDataException>(() => other.RecordApplied([Entry(1)], lease));
        Assert.Same(original, storage.Bytes);
    }

    [Theory]
    [InlineData("root")]
    [InlineData("entry")]
    [InlineData("value")]
    [InlineData("owner")]
    public void DuplicatedPropertiesAndMissingOwnerPreserveEvidence(string kind)
    {
        var storage = new Storage();
        using var lease = Lease();
        var store = new SingleOwnerBaselineStore(storage, "test", Sid);
        store.RecordApplied([Entry()], lease);
        var json = System.Text.Encoding.UTF8.GetString(storage.Bytes!);
        json = kind switch
        {
            "root" => json.Replace("\"Version\":1", "\"Version\":1,\"Version\":1", StringComparison.Ordinal),
            "entry" => json.Replace("\"ModuleId\":", "\"ModuleId\":\"bad\",\"ModuleId\":", StringComparison.Ordinal),
            "value" => json.Replace("\"Data\":", "\"Data\":\"bad\",\"Data\":", StringComparison.Ordinal),
            _ => json.Replace($"\"PrimaryUserSid\":\"{Sid}\",", "", StringComparison.Ordinal),
        };
        storage.Bytes = System.Text.Encoding.UTF8.GetBytes(json);
        var original = storage.Bytes;
        Assert.Throws<InvalidDataException>(() => store.Read(lease));
        Assert.Throws<InvalidDataException>(() => store.RecordApplied([Entry()], lease));
        Assert.Same(original, storage.Bytes);
    }

    [Fact]
    public void FailedUpdatePreservesLastChosenValuesAndBoundsCatalog()
    {
        var storage = new Storage();
        using var lease = Lease();
        var store = new SingleOwnerBaselineStore(storage, "test", Sid);
        var entries = Enumerable.Range(0, RestorationCatalog.Default.Targets.Length).Select(Entry).ToArray();
        store.RecordApplied(entries, lease);
        storage.WriteFailure = new IOException("full");
        Assert.Throws<IOException>(() => store.RecordApplied([Entry() with { Expected = RestorationCatalog.Default.Targets[0].WindowsDefaultValue }], lease));
        Assert.Equal(entries.Length, store.Read(lease).Count);
        Assert.Contains(Entry() with { UpdatedAtUtc = entries[0].UpdatedAtUtc }, store.Read(lease));
        Assert.Throws<InvalidDataException>(() => store.RecordApplied([.. entries, Entry()], lease));
    }

    [Theory]
    [InlineData("ModuleId")]
    [InlineData("SettingId")]
    [InlineData("CanonicalLocation")]
    [InlineData("Expected")]
    [InlineData("UpdatedAtUtc")]
    [InlineData("Kind")]
    [InlineData("Data")]
    public void MissingRequiredEntryFieldCannotAuthorizeRestoration(string field)
    {
        var storage = new Storage();
        using var lease = Lease();
        var store = new SingleOwnerBaselineStore(storage, "test", Sid);
        store.RecordApplied([Entry()], lease);
        var document = System.Text.Json.Nodes.JsonNode.Parse(storage.Bytes!)!;
        var entry = document["Entries"]![0]!.AsObject();
        if (field is "Kind" or "Data") entry["Expected"]!.AsObject().Remove(field);
        else entry.Remove(field);
        storage.Bytes = System.Text.Encoding.UTF8.GetBytes(document.ToJsonString());
        Assert.Throws<InvalidDataException>(() => store.Read(lease));
        Assert.Throws<InvalidDataException>(() => store.RecordApplied([Entry()], lease));
    }

    private sealed class Storage : ITrustedBaselineStorage
    {
        public byte[]? Bytes { get; set; }
        public IOException? ReadFailure { get; set; }
        public IOException? WriteFailure { get; set; }
        public int Writes { get; private set; }
        public byte[]? Read(int maximumBytes)
        {
            if (ReadFailure is not null) throw ReadFailure;
            return Bytes?.ToArray();
        }
        public void ReplaceDurably(ReadOnlyMemory<byte> document)
        {
            if (WriteFailure is not null) throw WriteFailure;
            Bytes = document.ToArray();
            Writes++;
        }
    }
}
