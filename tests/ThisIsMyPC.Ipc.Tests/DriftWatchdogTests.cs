using Microsoft.Extensions.Logging.Abstractions;
using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Drift;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Service;

namespace ThisIsMyPC.Ipc.Tests;

public sealed class DriftWatchdogTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"tipc-watchdog-{Guid.NewGuid():N}");
    private readonly string _baselinePath;
    private readonly FakeRegistry _registry = new();

    public DriftWatchdogTests()
    {
        _baselinePath = Path.Combine(_dir, "drift-baseline.json");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_dir))
                Directory.Delete(_dir, recursive: true);
        }
        catch (IOException) { }
    }

    private const string TestSid = "S-1-5-21-1111-2222-3333-1001";

    private DriftWatchdog Create(bool trusted = true) =>
        new(_registry, NullLogger<DriftWatchdog>.Instance, _baselinePath, baselineTrustCheck: _ => trusted);

    private void WriteBaseline(params ChangeDescriptor[] applied) =>
        new DriftBaselineStore(_baselinePath, userSid: TestSid).RecordApplied(applied);

    private void LoadUserHive() => _registry.Keys.Add($@"HKU\{TestSid}");

    /// <summary>The hive path the SYSTEM watchdog must translate an HKCU location to.</summary>
    private static string AsUserHive(string hkcuPath) => $@"HKU\{TestSid}\{hkcuPath[5..]}";

    private static ChangeDescriptor Applied(
        string location, string after, ChangeValueType valueType = ChangeValueType.Registry_DWord) => new()
    {
        ModuleId = "Windows Update",
        SettingId = "au-options",
        DisplayName = "Update behavior",
        SystemLocation = location,
        BeforeValue = "",
        AfterValue = after,
        BeforeDisplay = "b",
        AfterDisplay = "a",
        ValueType = valueType,
        Category = ChangeCategory.Modify,
    };

    [Fact]
    public void No_baseline_reports_baseline_absent()
    {
        var watchdog = Create();
        watchdog.ScanOnce();

        Assert.False(watchdog.BaselinePresent);
        Assert.Empty(watchdog.GetReport().Items);
        Assert.NotNull(watchdog.LastScanUtc);
    }

    [Fact]
    public void Matching_values_produce_no_drift_via_the_users_hku_hive()
    {
        WriteBaseline(Applied(@"HKCU\Software\Test\Dword", "5"));
        LoadUserHive();
        // Present ONLY under HKU\{sid} — a SYSTEM-hive (literal HKCU) read would miss it.
        _registry.DWords[AsUserHive(@"HKCU\Software\Test\Dword")] = 5;

        var watchdog = Create();
        watchdog.ScanOnce();

        Assert.True(watchdog.BaselinePresent);
        Assert.Empty(watchdog.GetReport().Items);
    }

    [Fact]
    public void Hkcu_entries_are_skipped_when_the_user_hive_is_not_loaded()
    {
        // Pre-logon boot scan: hive absent. The value is also absent — without the
        // hive-loaded guard this would false-positive as drift.
        WriteBaseline(Applied(@"HKCU\Software\Test\Dword", "5"));

        var watchdog = Create();
        watchdog.ScanOnce();

        Assert.True(watchdog.BaselinePresent);
        Assert.Empty(watchdog.GetReport().Items);
    }

    [Fact]
    public void Untrusted_baseline_file_is_refused()
    {
        WriteBaseline(Applied(@"HKLM\SOFTWARE\Test\Dword", "5"));

        var watchdog = Create(trusted: false);
        watchdog.ScanOnce();

        Assert.False(watchdog.BaselinePresent);
        Assert.Empty(watchdog.GetReport().Items);
    }

    [Fact]
    public void Reverted_policy_value_is_reported_with_expected_current_and_cause()
    {
        var location = @"HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU\AUOptions";
        WriteBaseline(Applied(location, "2"));
        _registry.DWords[location] = 3;

        var watchdog = Create();
        watchdog.ScanOnce();

        var item = Assert.Single(watchdog.GetReport().Items);
        Assert.Equal("2", item.ExpectedValue);
        Assert.Equal("3", item.CurrentValue);
        Assert.Equal("au-options", item.SettingId);
        Assert.Contains("Windows Update", item.SuspectedCause);
    }

    [Fact]
    public void Deleted_expected_value_reports_absent_current()
    {
        var location = @"HKCU\Software\Test\Str";
        WriteBaseline(Applied(location, "hello", ChangeValueType.Registry_String));
        LoadUserHive();
        // registry has no value at all

        var watchdog = Create();
        watchdog.ScanOnce();

        var item = Assert.Single(watchdog.GetReport().Items);
        Assert.Equal("hello", item.ExpectedValue);
        Assert.Equal("__absent__", item.CurrentValue);
        // The report shows the app-side location, untranslated
        Assert.Equal(location, item.SystemLocation);
    }

    [Fact]
    public void Expected_absent_and_actually_absent_is_not_drift()
    {
        // A delete-style change: expectation is "no value" (empty AfterValue)
        WriteBaseline(Applied(@"HKCU\Software\Test\Gone", "", ChangeValueType.Registry_String));
        LoadUserHive();

        var watchdog = Create();
        watchdog.ScanOnce();

        Assert.Empty(watchdog.GetReport().Items);
    }

    private sealed class FakeRegistry : IRegistryService
    {
        public Dictionary<string, int> DWords { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> Strings { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> Keys { get; } = new(StringComparer.OrdinalIgnoreCase);

        public OperationResult<int> ReadDWord(string keyPath, string valueName) =>
            DWords.TryGetValue($@"{keyPath}\{valueName}", out var v)
                ? OperationResult<int>.Success(v)
                : OperationResult<int>.Failure("Not found", ErrorCategory.NotFound);

        public OperationResult<string> ReadString(string keyPath, string valueName) =>
            Strings.TryGetValue($@"{keyPath}\{valueName}", out var v)
                ? OperationResult<string>.Success(v)
                : OperationResult<string>.Failure("Not found", ErrorCategory.NotFound);

        public OperationResult<string> ReadExpandString(string keyPath, string valueName) => ReadString(keyPath, valueName);
        public OperationResult<string[]> ReadMultiString(string keyPath, string valueName) =>
            OperationResult<string[]>.Failure("Not found", ErrorCategory.NotFound);
        public OperationResult<byte[]> ReadBinary(string keyPath, string valueName) =>
            OperationResult<byte[]>.Failure("Not found", ErrorCategory.NotFound);
        public OperationResult<bool> WriteBinary(string keyPath, string valueName, byte[] value) => Ok();
        public OperationResult<bool> WriteDWord(string keyPath, string valueName, int value) => Ok();
        public OperationResult<bool> WriteString(string keyPath, string valueName, string value) => Ok();
        public OperationResult<bool> WriteExpandString(string keyPath, string valueName, string value) => Ok();
        public OperationResult<bool> WriteMultiString(string keyPath, string valueName, string[] values) => Ok();
        public OperationResult<bool> DeleteValue(string keyPath, string valueName) => Ok();
        public OperationResult<bool> DeleteKey(string keyPath, bool recursive = false) => Ok();
        public OperationResult<bool> KeyExists(string keyPath) =>
            OperationResult<bool>.Success(Keys.Contains(keyPath));
        public OperationResult<bool> ValueExists(string keyPath, string valueName) => OperationResult<bool>.Success(false);
        public OperationResult<IReadOnlyList<string>> EnumerateSubKeys(string keyPath) =>
            OperationResult<IReadOnlyList<string>>.Success([]);
        public OperationResult<IReadOnlyList<string>> EnumerateValues(string keyPath) =>
            OperationResult<IReadOnlyList<string>>.Success([]);
        public OperationResult<string> ReadValueBeforeWrite(string keyPath, string valueName) => ReadString(keyPath, valueName);

        private static OperationResult<bool> Ok() => OperationResult<bool>.Success(true);
    }
}
