using ThisIsMyPC.Broker;
using ThisIsMyPC.Core.Actions;
using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Enforcement;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Ipc.Contracts;
using ThisIsMyPC.Modules.Annoyances.Changes;
using ThisIsMyPC.Modules.Annoyances.Services;
using ThisIsMyPC.Modules.Power.Changes;
using ThisIsMyPC.Modules.Privacy.Changes;
using ThisIsMyPC.Modules.Privacy.Services;
using ThisIsMyPC.Modules.Shell;
using ThisIsMyPC.Modules.Shell.Changes;
using ThisIsMyPC.Modules.Shell.Models;
using ThisIsMyPC.Modules.Shell.Services;
using ThisIsMyPC.Modules.Software.Actions;
using ThisIsMyPC.Modules.Software.Services;
using ThisIsMyPC.Modules.Startup.Changes;
using ThisIsMyPC.Modules.WindowsUpdate.Changes;
using ThisIsMyPC.Modules.WindowsUpdate.Services;

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
            Changes = [ChangeForModule(moduleId)],
        }).IsSuccess);
    }

    [Fact]
    public void ControlCharactersAreRejected()
    {
        var result = BrokerRequestPolicy.Create(new() { Changes = [Change() with { DisplayName = "Safe\nSpoofed" }] });

        Assert.False(result.IsSuccess);
    }

    [Theory]
    [InlineData("Safe\u202ESpoofed")]
    [InlineData("Safe\u2066Spoofed")]
    [InlineData("Safe\u200BSpoofed")]
    [InlineData("Safe\u00ADSpoofed")]
    [InlineData("Safe\uD800Spoofed")]
    public void InvisibleAndMalformedUnicodeIsRejected(string displayName)
    {
        var result = BrokerRequestPolicy.Create(new() { Changes = [Change() with { DisplayName = displayName }] });

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void ArbitraryRegistryTargetIsRejected()
    {
        var result = BrokerRequestPolicy.Create(new()
        {
            Changes = [Change() with { SystemLocation = @"HKLM\Software\Attack\Run" }],
        });

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void UnknownValueInsideApprovedRegistryKeyIsRejected()
    {
        var result = BrokerRequestPolicy.Create(new()
        {
            Changes = [Change() with
            {
                SystemLocation = @"HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU\UnknownValue",
            }],
        });

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void SettingIdCannotBeMovedToAnotherApprovedRegistryTarget()
    {
        var result = BrokerRequestPolicy.Create(new()
        {
            Changes = [Change() with
            {
                SystemLocation = @"HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU\AUOptions",
            }],
        });

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void EveryStaticRegistryFactoryTargetIsAccepted()
    {
        var registry = new SchemaRegistry();
        var annoyances = new AnnoyancesSettingsReader(registry);
        var annoyanceChanges = annoyances.ReadAll()
            .Select(preference => AnnoyanceChangeFactory.CreateToggle(preference, suppress: true))
            .Concat(AnnoyanceChangeFactory.CreateCopilotPolicyToggle(annoyances.ReadCopilotPolicy(), true).Changes)
            .Concat(AnnoyanceChangeFactory.CreateRecallPolicyToggle(annoyances.ReadRecall(), true, "Recall").Changes)
            .Concat(AnnoyanceChangeFactory.CreateActivityHistoryToggle(annoyances.ReadActivityHistory(), true, "History").Changes)
            .Concat(AnnoyanceChangeFactory.CreateGroupToggle(annoyances.ReadSettingsSuggestedContent(),
                "settings-suggested-content", "Suggested content", "Suggested content", true).Changes)
            .Concat(AnnoyanceChangeFactory.CreateGroupToggle(annoyances.ReadLockScreenAds(),
                "lock-screen-ads", "Lock screen ads", "Lock screen ads", true).Changes)
            .Concat(AnnoyanceChangeFactory.CreateGroupToggle(annoyances.ReadPreinstalledApps(),
                "preinstalled-apps", "Preinstalled apps", "Preinstalled apps", true).Changes)
            .Concat(AnnoyanceChangeFactory.CreateGroupToggle(annoyances.ReadEdgeDebloat(),
                "edge-debloat", "Edge debloat", "Edge debloat", true).Changes)
            .Concat(AnnoyanceChangeFactory.CreateBingSearchToggle(annoyances.ReadBingSearch(), true).Changes);

        var privacy = new PrivacySettingsReader(registry).ReadAll();
        var privacyChanges = privacy.Preferences.Concat(privacy.InkingTyping)
            .Select(preference => PrivacyChangeFactory.CreateToggle(preference, configure: true));

        var updates = new WindowsUpdateSettingsReader(registry).ReadAll();
        var updateChanges = updates.Settings
            .Select(setting => WindowsUpdateChangeFactory.CreateToggle(
                setting, configure: true, gpCache: setting.Id != "delivery-optimization"))
            .Concat(updates.VersionPin.Select(setting => WindowsUpdateChangeFactory.CreateToggle(setting, true)))
            .Concat(updates.UxSettings.Select(setting => WindowsUpdateChangeFactory.CreateUxToggle(setting, true)));

        foreach (var change in annoyanceChanges.Concat(privacyChanges).Concat(updateChanges))
            Assert.True(BrokerRequestPolicy.Create(new() { Changes = [change] }).IsSuccess, change.SystemLocation);
    }

    [Fact]
    public void AutorunCannotEscapeTheShippedLocationCatalog()
    {
        var change = ChangeForModule("Startup & Services") with
        {
            SettingId = @"autorun:RegistryValue|HKLM\Software\Attack|Run",
            SystemLocation = @"RegistryValue|HKLM\Software\Attack|Run",
            BeforeValue = "Enabled",
            AfterValue = "Disabled",
            ValueType = ChangeValueType.Autorun_State,
            Category = ChangeCategory.Disable,
        };

        Assert.False(BrokerRequestPolicy.Create(new() { Changes = [change] }).IsSuccess);
    }

    [Fact]
    public void EveryCatalogSoftwareActionIsAccepted()
    {
        foreach (var entry in SoftwareCatalog.Entries)
        {
            Assert.True(BrokerRequestPolicy.Create(new() { Actions = [SoftwareActionFactory.CreateInstall(entry)] }).IsSuccess);
            Assert.True(BrokerRequestPolicy.Create(new() { Actions = [SoftwareActionFactory.CreateUninstall(entry)] }).IsSuccess);
        }

        foreach (var entry in WindowsAppsCatalog.Entries)
            Assert.True(BrokerRequestPolicy.Create(new() { Actions = [SoftwareActionFactory.CreateAppxRemove(entry)] }).IsSuccess);
    }

    [Fact]
    public void UnknownCatalogSoftwareActionIsRejected()
    {
        var action = SoftwareActionFactory.CreateInstall(SoftwareCatalog.Entries[0]) with
        {
            ActionId = "install:not-in-the-catalog",
        };

        Assert.False(BrokerRequestPolicy.Create(new() { Actions = [action] }).IsSuccess);
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
        Assert.False(policy.Authorizes(Command(change with { Category = ChangeCategory.Delete })));
        Assert.False(policy.Authorizes(Command(change with
        {
            Enforcement = change.Enforcement! with { CompanionServices = ["AttackService"] },
        })));
    }

    [Fact]
    public void EveryOperationAppearsOnAConfirmationPage()
    {
        var changes = Enumerable.Range(1, 17)
            .Select(index =>
            {
                var serviceName = $"TestService{index}";
                return ChangeForModule("Startup & Services") with
                {
                    SettingId = ServiceChangeFactory.GetSettingId(serviceName),
                    SystemLocation = serviceName,
                    DisplayName = $"Change {index}",
                };
            })
            .ToList();
        var policy = BrokerRequestPolicy.Create(new() { Changes = changes }).Value!;

        var pages = policy.BuildConfirmationPages();

        Assert.Equal(3, pages.Count);
        foreach (var change in changes)
            Assert.Contains(pages, page => page.Contains(change.SettingId, StringComparison.Ordinal));
        Assert.All(pages, page => Assert.Contains("Page ", page, StringComparison.Ordinal));
        Assert.DoesNotContain(pages, page => page.Contains("Change 1", StringComparison.Ordinal));
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
            Enforcement = Change().Enforcement! with { ReversionVectors = ["Windows feature updates"] },
        };
        var policy = BrokerRequestPolicy.Create(new() { Changes = [change] }).Value!;

        Assert.Contains("Reversion vector: Windows feature updates", policy.BuildConfirmationPages()[0]);
    }

    [Fact]
    public void ActionAndControlCommandsRequireExactSessionApproval()
    {
        var action = SoftwareActionFactory.CreateInstall(SoftwareCatalog.Entries[0]);
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
        var confirmation = policy.BuildConfirmationPages()[0];
        Assert.Contains(action.ActionId, confirmation, StringComparison.Ordinal);
        Assert.DoesNotContain(action.DisplayName, confirmation, StringComparison.Ordinal);
    }

    private static BrokerCommandRequest Command(ChangeDescriptor change) => new()
    {
        Kind = BrokerCommandKind.ApplyChange,
        Change = change,
    };

    private static ChangeDescriptor Change() => new()
    {
        ModuleId = "Windows Update",
        SettingId = "no-auto-reboot",
        DisplayName = "Policy setting",
        SystemLocation = @"HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU\NoAutoRebootWithLoggedOnUsers",
        BeforeValue = "0",
        AfterValue = "1",
        BeforeDisplay = "Off",
        AfterDisplay = "On",
        ValueType = ChangeValueType.Registry_DWord,
        Enforcement = new SettingEnforcement
        {
            GPCacheEntries = [@"HKLM\SOFTWARE\Microsoft\WindowsUpdate\UpdatePolicy\GPCache"],
            ReversionVectors = ["Group Policy refresh", "Windows feature updates"],
            SkuRestriction = ThisIsMyPC.Core.Modules.WindowsSku.Pro,
        },
    };

    private static ChangeDescriptor ChangeForModule(string moduleId) => moduleId switch
    {
        "Explorer" => new()
        {
            ModuleId = moduleId, SettingId = "taskbar-widgets", DisplayName = "Widgets",
            SystemLocation = $@"{ShellRegistryPaths.AdvancedKeyPath}\TaskbarDa", BeforeValue = "1", AfterValue = "0",
            BeforeDisplay = "Shown", AfterDisplay = "Hidden", ValueType = ChangeValueType.Registry_DWord,
            Category = ChangeCategory.Disable,
        },
        "Context Menus" => CustomVerbChangeFactory.CreateNew(new CustomVerbDefinition
        {
            Scope = "*", VerbId = "test-entry", Label = "Test entry", Command = "cmd.exe /c echo test",
        }),
        "Environment" => EnvironmentVariableChangeFactory.CreateAdd(
            "TEST_VARIABLE", "value", "system", EnvironmentVariableReader.SystemEnvKeyPath),
        "Startup & Services" => new()
        {
            ModuleId = moduleId, SettingId = ServiceChangeFactory.GetSettingId("Spooler"),
            DisplayName = "Service startup type: Print Spooler", SystemLocation = "Spooler",
            BeforeValue = "Automatic", AfterValue = "Manual", BeforeDisplay = "Automatic", AfterDisplay = "Manual",
            ValueType = ChangeValueType.Service_StartType, Category = ChangeCategory.Modify,
        },
        "Windows Annoyances" => Change() with
        {
            ModuleId = moduleId, SettingId = "bing-search",
            SystemLocation = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Search\BingSearchEnabled",
            Enforcement = null,
        },
        "Privacy & Telemetry" => Change() with
        {
            ModuleId = moduleId, SettingId = "telemetry-level",
            SystemLocation = @"HKLM\SOFTWARE\Policies\Microsoft\Windows\DataCollection\AllowTelemetry",
            Enforcement = null,
        },
        "Windows Update" => Change(),
        "Power Plans" => PowerPlanChangeFactory.CreateHibernateToggle(currentlyEnabled: true, enable: false),
        _ => throw new ArgumentOutOfRangeException(nameof(moduleId)),
    };

    private sealed class SchemaRegistry : IRegistryService
    {
        public OperationResult<int> ReadDWord(string keyPath, string valueName) => NotFound<int>();
        public OperationResult<string> ReadString(string keyPath, string valueName) =>
            valueName == "DisplayVersion" ? OperationResult<string>.Success("25H2") : NotFound<string>();
        public OperationResult<string> ReadExpandString(string keyPath, string valueName) => NotFound<string>();
        public OperationResult<string[]> ReadMultiString(string keyPath, string valueName) => NotFound<string[]>();
        public OperationResult<byte[]> ReadBinary(string keyPath, string valueName) => NotFound<byte[]>();
        public OperationResult<bool> WriteDWord(string keyPath, string valueName, int value) => Success();
        public OperationResult<bool> WriteString(string keyPath, string valueName, string value) => Success();
        public OperationResult<bool> WriteExpandString(string keyPath, string valueName, string value) => Success();
        public OperationResult<bool> WriteMultiString(string keyPath, string valueName, string[] values) => Success();
        public OperationResult<bool> WriteBinary(string keyPath, string valueName, byte[] value) => Success();
        public OperationResult<bool> DeleteValue(string keyPath, string valueName) => Success();
        public OperationResult<bool> DeleteKey(string keyPath, bool recursive = false) => Success();
        public OperationResult<bool> KeyExists(string keyPath) => OperationResult<bool>.Success(false);
        public OperationResult<bool> ValueExists(string keyPath, string valueName) => OperationResult<bool>.Success(false);
        public OperationResult<IReadOnlyList<string>> EnumerateSubKeys(string keyPath) =>
            OperationResult<IReadOnlyList<string>>.Success([]);
        public OperationResult<IReadOnlyList<string>> EnumerateValues(string keyPath) =>
            OperationResult<IReadOnlyList<string>>.Success([]);
        public OperationResult<string> ReadValueBeforeWrite(string keyPath, string valueName) => NotFound<string>();

        private static OperationResult<T> NotFound<T>() =>
            OperationResult<T>.Failure("Not found", ErrorCategory.NotFound);
        private static OperationResult<bool> Success() => OperationResult<bool>.Success(true);
    }
}
