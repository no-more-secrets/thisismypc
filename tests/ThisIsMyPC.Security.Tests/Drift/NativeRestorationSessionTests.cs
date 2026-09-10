using System.Security.AccessControl;
using System.Security.Principal;
using ThisIsMyPC.Core.Drift;
using ThisIsMyPC.Core.Drift.Eligibility;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Interop.Win32.Drift;

namespace ThisIsMyPC.Security.Tests.Drift;

[Trait("Category", "Integration")]
public sealed class NativeRestorationSessionTests
{
    [Fact]
    public async Task NativeStorageAndLeaseRestoreEveryCatalogTargetThenPauseAndReopen()
    {
        using var identity = WindowsIdentity.GetCurrent();
        Assert.True(new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator));
        var directory = Path.Combine(Path.GetTempPath(), "tipc-native-session", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var acl = new DirectorySecurity();
        acl.SetSecurityDescriptorSddlForm("O:BAG:BAD:P(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)");
        new DirectoryInfo(directory).SetAccessControl(acl);
        var name = @"Global\tipc-native-session-" + Guid.NewGuid().ToString("N");
        const string sid = "S-1-5-21-111-222-333-1001";
        var registry = new MemoryRegistry();
        try
        {
            using (var session = new NativeRestorationSession(registry, sid, directory, name))
            {
                var saved = await session.Coordinator.RunAsync(TimeSpan.FromSeconds(5), (lease, _) =>
                {
                    session.Baseline.RecordApplied(RestorationCatalog.Default.Targets.Select(target =>
                        new ThisIsMyPC.Core.Drift.Baseline.SingleOwnerBaselineEntry(target.ModuleId, target.SettingId,
                            target.KeyPath + "\\" + target.ValueName, target.SuppressedValue, DateTimeOffset.UtcNow)).ToArray(), lease);
                    return Task.FromResult(session.Consent.SetEnabled(true, lease).IsSuccess);
                });
                Assert.True(saved.OperationRan); Assert.True(saved.Value);
                foreach (var target in RestorationCatalog.Default.Targets)
                {
                    registry.Values[("HKU\\" + sid + "\\" + target.KeyPath[5..], target.ValueName)] = target.WindowsDefaultValue;
                    var loop = new RestorationLoop(session.Coordinator, session.Baseline, target, session.Consent,
                        session.Journal, new(session.History), registry,
                        id => new(new(id.UserSid, RestorationProfileState.SupportedAndLoaded), new(id, RestorationManagementState.Unmanaged)), TimeProvider.System);
                    Assert.Equal(RestorationScanStatus.Restored, (await loop.ScanAsync()).Status);
                    Assert.Equal(RestorationScanStatus.AlreadyMatches, (await loop.ScanAsync()).Status);
                }
                Assert.Equal(11, registry.Writes);
                var history = await session.History.GetAllAsync();
                Assert.Equal(11, history.Count);
                Assert.All(history, row => { Assert.Equal("Applied", row.JournalOutcome); Assert.False(row.SupportsGenericUndo); });
                var interrupted = await session.Coordinator.RunAsync(TimeSpan.FromSeconds(5), (lease, _) =>
                {
                    var target = RestorationCatalog.Default.Targets[0];
                    var candidate = RestorationCatalog.Default.Validate(new DriftBaselineEntry
                    {
                        ModuleId = target.ModuleId, SettingId = target.SettingId, DisplayName = target.DisplayName,
                        SystemLocation = target.KeyPath + "\\" + target.ValueName, ValueType = target.ValueType,
                        ExpectedValue = target.SuppressedValue.Data, UpdatedAtUtc = DateTimeOffset.UtcNow,
                    }, sid).Candidate!;
                    session.Journal.Begin(Guid.NewGuid(), candidate, RegistryValueSnapshot.Present(target.WindowsDefaultValue), lease);
                    return Task.FromResult(true);
                });
                Assert.True(interrupted.OperationRan);
                var acquired = await session.Leases.AcquireAsync(TimeSpan.FromSeconds(5));
                await using var pauseLease = acquired.Lease!;
                Assert.True(session.Consent.SetEnabled(false, pauseLease).IsSuccess);
            }
            using (var reopened = new NativeRestorationSession(registry, dataDirectory: directory, leaseName: name))
            {
                var recovered = await reopened.Coordinator.RunAsync(TimeSpan.FromSeconds(5), async (lease, _) =>
                {
                    Assert.Equal(sid, reopened.Baseline.PrimaryUserSid);
                    Assert.False(reopened.Consent.Read().IsGranted);
                    await new ThisIsMyPC.Core.Drift.Journal.RestorationJournalImporter(reopened.History).ImportAsync(reopened.Journal, lease);
                    var records = await reopened.History.GetAllAsync();
                    Assert.Equal(12, records.Count);
                    Assert.Single(records.Where(row => row.JournalOutcome != "Applied"));
                    return true;
                });
                Assert.True(recovered.OperationRan);
            }
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private sealed class MemoryRegistry : IRegistryService
    {
        public Dictionary<(string, string), RegistryValueData> Values { get; } = [];
        public int Writes { get; private set; }
        public OperationResult<RegistryValueData> ReadValue(string p, string n) => OperationResult<RegistryValueData>.Success(Values[(p, n)]);
        public OperationResult<bool> WriteValue(string p, string n, RegistryValueData value) { Values[(p, n)] = value; Writes++; return OperationResult<bool>.Success(true); }
        public OperationResult<int> ReadDWord(string p, string n) => throw new NotSupportedException();
        public OperationResult<string> ReadString(string p, string n) => throw new NotSupportedException();
        public OperationResult<string> ReadExpandString(string p, string n) => throw new NotSupportedException();
        public OperationResult<string[]> ReadMultiString(string p, string n) => throw new NotSupportedException();
        public OperationResult<byte[]> ReadBinary(string p, string n) => throw new NotSupportedException();
        public OperationResult<bool> WriteDWord(string p, string n, int v) => throw new NotSupportedException();
        public OperationResult<bool> WriteString(string p, string n, string v) => throw new NotSupportedException();
        public OperationResult<bool> WriteExpandString(string p, string n, string v) => throw new NotSupportedException();
        public OperationResult<bool> WriteMultiString(string p, string n, string[] v) => throw new NotSupportedException();
        public OperationResult<bool> WriteBinary(string p, string n, byte[] v) => throw new NotSupportedException();
        public OperationResult<bool> DeleteValue(string p, string n) => throw new NotSupportedException();
        public OperationResult<bool> DeleteKey(string p, bool recursive = false) => throw new NotSupportedException();
        public OperationResult<bool> KeyExists(string p) => throw new NotSupportedException();
        public OperationResult<bool> ValueExists(string p, string n) => throw new NotSupportedException();
        public OperationResult<IReadOnlyList<string>> EnumerateSubKeys(string p) => throw new NotSupportedException();
        public OperationResult<IReadOnlyList<string>> EnumerateValues(string p) => throw new NotSupportedException();
        public OperationResult<string> ReadValueBeforeWrite(string p, string n) => throw new NotSupportedException();
    }
}
