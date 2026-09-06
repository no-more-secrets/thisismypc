using ThisIsMyPC.Broker;
using ThisIsMyPC.Core.Actions;
using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Enforcement;
using ThisIsMyPC.Ipc.Contracts;

namespace ThisIsMyPC.Ipc.Tests;

public sealed class BrokerRequestPolicyTests
{
    [Fact]
    public void EmptySessionIsRejected()
    {
        var result = BrokerRequestPolicy.Create(new());

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void NullCollectionsAndEntriesAreRejected()
    {
        Assert.False(BrokerRequestPolicy.Create(new() { Changes = null! }).IsSuccess);
        Assert.False(BrokerRequestPolicy.Create(new() { Actions = null! }).IsSuccess);
        Assert.False(BrokerRequestPolicy.Create(new() { Changes = [null!] }).IsSuccess);
        Assert.False(BrokerRequestPolicy.Create(new() { Actions = [null!] }).IsSuccess);
    }

    [Fact]
    public void MissingRequiredFieldsAreRejected()
    {
        Assert.False(BrokerRequestPolicy.Create(new()
        {
            Changes = [Change() with { SystemLocation = null! }],
        }).IsSuccess);
        Assert.False(BrokerRequestPolicy.Create(new()
        {
            RestorePointDescription = " ",
        }).IsSuccess);
    }

    [Fact]
    public void UnknownModuleIsRejected()
    {
        var result = BrokerRequestPolicy.Create(new() { Changes = [Change() with { ModuleId = "Unknown" }] });

        Assert.False(result.IsSuccess);
    }

    [Theory]
    [InlineData("Explorer")]
    [InlineData("Context Menus")]
    [InlineData("Environment")]
    [InlineData("Startup & Services")]
    [InlineData("Windows Annoyances")]
    [InlineData("Privacy & Telemetry")]
    [InlineData("Windows Update")]
    [InlineData("Power Plans")]
    public void RegisteredChangeModulesAreAccepted(string moduleId)
    {
        Assert.True(BrokerRequestPolicy.Create(new()
        {
            Changes = [Change() with { ModuleId = moduleId }],
        }).IsSuccess);
    }

    [Fact]
    public void ControlCharactersAreRejected()
    {
        var result = BrokerRequestPolicy.Create(new() { Changes = [Change() with { DisplayName = "Safe\nSpoofed" }] });

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void ApprovedChangeAcceptsApplyAndInverseRollback()
    {
        var change = Change();
        var policy = BrokerRequestPolicy.Create(new() { Changes = [change] }).Value!;

        Assert.True(policy.Authorizes(new() { Kind = BrokerCommandKind.ApplyChange, Change = change }));
        Assert.True(policy.Authorizes(new()
        {
            Kind = BrokerCommandKind.RevertChange,
            Change = change with
            {
                BeforeValue = change.AfterValue!,
                AfterValue = change.BeforeValue,
            },
        }));
    }

    [Fact]
    public void AlteredTargetValueOrEnforcementIsRejected()
    {
        var change = Change();
        var policy = BrokerRequestPolicy.Create(new() { Changes = [change] }).Value!;

        Assert.False(policy.Authorizes(Command(change with { SystemLocation = @"HKLM\Software\Attack\Run" })));
        Assert.False(policy.Authorizes(Command(change with { AfterValue = "attack" })));
        Assert.False(policy.Authorizes(Command(change with
        {
            Enforcement = change.Enforcement! with { CompanionServices = ["AttackService"] },
        })));
    }

    [Fact]
    public void EveryOperationAppearsOnAConfirmationPage()
    {
        var changes = Enumerable.Range(1, 17)
            .Select(index => Change() with { SettingId = $"setting-{index}", DisplayName = $"Change {index}" })
            .ToList();
        var policy = BrokerRequestPolicy.Create(new() { Changes = changes }).Value!;

        var pages = policy.BuildConfirmationPages();

        Assert.Equal(3, pages.Count);
        foreach (var change in changes)
            Assert.Contains(pages, page => page.Contains(change.DisplayName, StringComparison.Ordinal));
        Assert.All(pages, page => Assert.Contains("Page ", page, StringComparison.Ordinal));
    }

    [Fact]
    public void OversizedEnforcementTargetSetIsRejected()
    {
        var change = Change() with
        {
            Enforcement = new SettingEnforcement
            {
                CompanionServices = Enumerable.Range(1, 17).Select(index => $"Service{index}").ToList(),
            },
        };

        Assert.False(BrokerRequestPolicy.Create(new() { Changes = [change] }).IsSuccess);
    }

    [Fact]
    public void ReversionTargetsAppearOnConfirmationPage()
    {
        var change = Change() with
        {
            Enforcement = Change().Enforcement! with { ReversionVectors = ["Example reversion target"] },
        };
        var policy = BrokerRequestPolicy.Create(new() { Changes = [change] }).Value!;

        Assert.Contains("Reversion vector: Example reversion target", policy.BuildConfirmationPages()[0]);
    }

    [Fact]
    public void ActionAndControlCommandsRequireExactSessionApproval()
    {
        var action = new ActionDescriptor
        {
            ModuleId = "Software",
            ActionId = "install:Example.App",
            DisplayName = "Install Example",
            Detail = "winget: Example.App",
        };
        var policy = BrokerRequestPolicy.Create(new()
        {
            Actions = [action],
            AllowOwnerModeEnable = true,
        }).Value!;

        Assert.True(policy.Authorizes(new() { Kind = BrokerCommandKind.ExecuteAction, Action = action }));
        Assert.False(policy.Authorizes(new()
        {
            Kind = BrokerCommandKind.ExecuteAction,
            Action = action with { ActionId = "install:Attack.App" },
        }));
        Assert.True(policy.Authorizes(new() { Kind = BrokerCommandKind.EnableOwnerMode }));
        Assert.False(policy.Authorizes(new() { Kind = BrokerCommandKind.DisableOwnerMode }));
    }

    private static BrokerCommandRequest Command(ChangeDescriptor change) => new()
    {
        Kind = BrokerCommandKind.ApplyChange,
        Change = change,
    };

    private static ChangeDescriptor Change() => new()
    {
        ModuleId = "Windows Update",
        SettingId = "setting",
        DisplayName = "Policy setting",
        SystemLocation = @"HKLM\Software\Policies\Example\Value",
        BeforeValue = "0",
        AfterValue = "1",
        BeforeDisplay = "Off",
        AfterDisplay = "On",
        ValueType = ChangeValueType.Registry_DWord,
        Enforcement = new SettingEnforcement
        {
            CompanionServices = ["ExampleService"],
            CompanionTasks = [@"\Example\Task"],
            GPCacheEntries = [@"Machine\Registry.pol:Software\Policies\Example\Value"],
        },
    };
}
