using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Network;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Modules.Network;

namespace ThisIsMyPC.Modules.Network.Tests;

public sealed class NetworkTests
{
    private static readonly Guid Adapter = new("c7d51f0e-d423-430b-82d2-117bf8dd2fa5");
    [Theory]
    [InlineData("1.1.1.1 8.8.8.8", "1.1.1.1,8.8.8.8")]
    [InlineData("", "<automatic>")]
    [InlineData("1.1.1.1,1.1.1.1", "1.1.1.1")]
    public void DnsNormalizesExplicitAddresses(string input, string expected)
    {
        Assert.True(NetworkChanges.TryDns(input, out var actual));
        Assert.Equal(expected, actual);
    }
    [Theory]
    [InlineData("example.com")][InlineData("1.1.1.1 & whoami")][InlineData("1.2.3")]
    [InlineData("127.1")][InlineData("0.0.0.0")][InlineData("255.255.255.255")]
    [InlineData("224.0.0.1")][InlineData("::1")]
    public void InvalidDnsCannotReachWriter(string input) => Assert.False(NetworkChanges.TryDns(input, out _));

    [Fact]
    public async Task SchemaRejectsForgedTargetsBeforeWriter()
    {
        var native = new FakeNative();
        var module = new NetworkModule(native, native);
        var valid = NetworkChanges.Create($"adapter/{Adapter:D}", "Adapter", "true", "false");
        foreach (var change in new[]
        {
            valid with { ModuleId = "Explorer" }, valid with { SystemLocation = "anything" },
            valid with { ValueType = ChangeValueType.Registry_String }, valid with { AfterValue = "yes" },
            valid with { Category = ChangeCategory.Delete }, valid with { SettingId = "firewall/7", SystemLocation = "firewall/7" },
            valid with { SettingId = "adapter/00000000-0000-0000-0000-000000000000", SystemLocation = "adapter/00000000-0000-0000-0000-000000000000" },
        }) Assert.False((await module.ApplyChangeAsync(change)).IsSuccess);
        Assert.Equal(0, native.Writes);
    }

    [Fact]
    public async Task FailedBatchRestoresEarlierAdapterChange()
    {
        var native = new FakeNative { FailDns = true };
        var module = new NetworkModule(native, native);
        var queue = new PendingChangesService();
        queue.Stage(new ChangeGroup { GroupId = "test", DisplayName = "Network", Description = "Network", Changes = [
            NetworkChanges.Create($"adapter/{Adapter:D}", "Adapter", "true", "false"),
            NetworkChanges.Create($"dns/{Adapter:D}", "DNS", NetworkChanges.Automatic, "1.1.1.1"),
        ] });
        var result = await queue.ApplyAllAsync(module.ApplyChangeAsync, module.RevertChangeAsync);
        Assert.False(result.IsSuccess);
        Assert.True(native.Enabled);
        Assert.Equal(2, native.Writes);
    }

    [Fact]
    public async Task UndoUsesCapturedAutomaticDns()
    {
        var native = new FakeNative();
        var module = new NetworkModule(native, native);
        var change = NetworkChanges.Create($"dns/{Adapter:D}", "DNS", NetworkChanges.Automatic, "1.1.1.1");
        Assert.True((await module.ApplyChangeAsync(change)).IsSuccess);
        Assert.True((await module.RevertChangeAsync(change with { BeforeValue = change.AfterValue!, AfterValue = change.BeforeValue })).IsSuccess);
        Assert.Equal(NetworkChanges.Automatic, native.Dns);
    }

    [Fact]
    public async Task OneFailedScanDoesNotHideOtherSection()
    {
        var native = new FakeNative { FailAdapters = true };
        var result = await new NetworkModule(native, native).ScanSystemStateAsync();
        var state = Assert.IsType<NetworkScanData>(result.Value);
        Assert.NotNull(state.AdapterError);
        Assert.Single(state.Firewall.Profiles);
    }

    [Fact, Trait("Category", "Diagnostic")]
    public void NativeReadOnlyInventory()
    {
        var adapters = new Interop.Win32.Network.NetworkAdapterService().Read();
        Assert.NotEmpty(adapters);
        Assert.All(adapters, a => Assert.NotEqual(Guid.Empty, a.Id));
        var firewall = new Interop.Com.Network.FirewallService().Read();
        Assert.Equal(3, firewall.Profiles.Count);
        Assert.Null(firewall.Error);
        Assert.NotEmpty(firewall.Rules);
    }

    private sealed class FakeNative : INetworkAdapterService, IFirewallService
    {
        public bool Enabled = true;
        public string Dns = NetworkChanges.Automatic;
        public int Writes;
        public bool FailDns, FailAdapters;
        public IReadOnlyList<NetworkAdapterState> Read() => FailAdapters ? throw new InvalidOperationException("Unavailable") : [];
        FirewallState IFirewallService.Read() => new([new(4, "Public", true, true, "Block", "Allow", true)], []);
        public OperationResult<bool> SetEnabled(Guid id, bool expected, bool desired)
        {
            if (Enabled != expected) return Failure();
            Enabled = desired; Writes++; return OperationResult<bool>.Success(true);
        }
        public OperationResult<bool> SetEnabled(int profile, bool expected, bool desired) { Writes++; return OperationResult<bool>.Success(true); }
        public OperationResult<bool> SetDns(Guid id, string expected, string desired)
        {
            if (FailDns || Dns != expected) return Failure();
            Dns = desired; Writes++; return OperationResult<bool>.Success(true);
        }
        private static OperationResult<bool> Failure() => OperationResult<bool>.Failure("Unavailable", ErrorCategory.ServiceUnavailable);
    }
}
