using System.Collections.Immutable;
using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Policies;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Core.Tests.Fakes;

namespace ThisIsMyPC.Core.Tests.Policies;

public sealed class PolicyControlStateReaderTests
{
    private const string Au = @"HKLM\Software\Policies\Microsoft\Windows\WindowsUpdate\AU";
    private readonly FakeRegistryService _registry = new();

    [Theory]
    [InlineData("Professional", false)]
    [InlineData("Core", false)]
    [InlineData("Enterprise", true)]
    public void UnsupportedPolicy_DoesNotOverrideOrdinaryPreference(string edition, bool supported)
    {
        _registry.SetString(@"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion", "EditionID", edition);
        _registry.SetDWord(@"HKLM\Software\Policies\Microsoft\Windows\CloudContent", "DisableSoftLanding", 1);
        var reader = new PolicyControlStateReader(_registry, capabilityDetector: new CapabilityDetector(_registry));
        var state = reader.Read("Windows Annoyances", "windows-tips");
        Assert.Equal(supported, state.BlocksChanges);
        Assert.Equal(supported ? true : (bool?)null, state.ToggleState);
        if (!supported) Assert.Contains("not verified", state.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ManualUpdates_AreDetectedWithoutAuOptions()
    {
        _registry.SetDWord(Au, "NoAutoUpdate", 1);
        var state = new PolicyControlStateReader(_registry).Read("Windows Update", "auto-update-mode", Au + "\\AUOptions");
        Assert.True(state.BlocksChanges);
        Assert.Contains("started manually", state.Message, StringComparison.Ordinal);
        Assert.Null(state.ToggleState); // Notification mode itself is not enabled.
    }

    [Fact]
    public void DisabledPolicy_DoesNotForceThePreferenceOn()
    {
        const string key = @"Software\Policies\Microsoft\Windows\CloudContent";
        _registry.SetDWord("HKCU\\" + key, "DisableThirdPartySuggestions", 0);
        var sources = new[] { new PolicySourceSnapshot("User policy", PolicyScope.User, PolicyFileStatus.Loaded,
            [new(key, "DisableThirdPartySuggestions", 4, [0, 0, 0, 0])]) };
        var state = new PolicyControlStateReader(_registry, () => sources).Read("Windows Annoyances", "app-suggestions");
        Assert.False(state.BlocksChanges);
        Assert.Null(state.ToggleState);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(3, false)]
    [InlineData(99, true)]
    public void DiagnosticData_PreservesStricterAndUnknownValues(int value, bool blocked)
    {
        const string key = @"HKLM\Software\Policies\Microsoft\Windows\DataCollection";
        _registry.SetDWord(key, "AllowTelemetry", value);
        Assert.Equal(blocked, new PolicyControlStateReader(_registry).Read("Privacy & Telemetry", "telemetry-level", key + "\\AllowTelemetry").BlocksChanges);
    }

    [Fact]
    public void WrongRegistryType_IsUnknown_NotDisabled()
    {
        _registry.SetString(Au, "NoAutoUpdate", "1");
        var state = new PolicyControlStateReader(_registry).Read("Windows Update", "auto-update-mode");
        Assert.True(state.BlocksChanges);
        Assert.Contains("unexpected type", state.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("started manually", state.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(1, "Controlled by saved local policy.")]
    [InlineData(0, "Saved local policy differs")]
    public void SavedPolicy_IsComparedWithActualRegistry(int actual, string message)
    {
        _registry.SetDWord(Au, "NoAutoUpdate", actual);
        var source = new PolicySourceSnapshot("Computer policy", PolicyScope.Machine, PolicyFileStatus.Loaded,
            [new(Au[5..], "NoAutoUpdate", 4, [1, 0, 0, 0])]);
        var state = new PolicyControlStateReader(_registry, () => [source]).Read("Windows Update", "auto-update-mode");
        Assert.True(state.BlocksChanges);
        Assert.Contains(message, state.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DeletionInstruction_OverridesEarlierSavedValue()
    {
        var source = new PolicySourceSnapshot("Computer policy", PolicyScope.Machine, PolicyFileStatus.Loaded,
            [new(Au[5..], "NoAutoUpdate", 4, [1, 0, 0, 0]), new(Au[5..], "**Del.NoAutoUpdate", 1, [32, 0, 0, 0])]);
        var state = new PolicyControlStateReader(_registry, () => [source]).Read("Windows Update", "auto-update-mode");
        Assert.DoesNotContain("started manually", state.Message ?? "", StringComparison.Ordinal);
    }

    [Fact]
    public void NotificationPolicy_RequiresBothValues()
    {
        const string key = @"HKLM\Software\Policies\Microsoft\Windows\WindowsUpdate";
        _registry.SetDWord(key, "UpdateNotificationLevel", 2);
        var reader = new PolicyControlStateReader(_registry);
        Assert.False(reader.Read("Windows Update", "restart-notifications").BlocksChanges);
        _registry.SetDWord(key, "SetUpdateNotificationLevel", 1);
        Assert.False(reader.Read("Windows Update", "restart-notifications").ToggleState);
        Assert.True(reader.Read("Windows Update", "restart-notifications").BlocksChanges);
    }

    [Fact]
    public async Task PolicyChangesAfterStaging_StopAllWrites()
    {
        var reader = new PolicyControlStateReader(_registry);
        var queue = new PendingChangesService(policyStates: reader);
        queue.Stage(new ChangeDescriptor
        {
            ModuleId = "Windows Update", SettingId = "auto-update-mode", DisplayName = "Notify", SystemLocation = Au + "\\AUOptions",
            BeforeValue = "", AfterValue = "2", BeforeDisplay = "Not configured", AfterDisplay = "Notify", ValueType = ChangeValueType.Registry_DWord,
        });
        _registry.SetDWord(Au, "NoAutoUpdate", 1);
        var calls = 0;
        Task<OperationResult<bool>> Write(ChangeDescriptor _) { calls++; return Task.FromResult(OperationResult<bool>.Success(true)); }
        var result = await queue.ApplyAllAsync(Write, Write, _ => calls++, CancellationToken.None);
        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCategory.ProtectedByPolicy, result.ErrorCategory);
        Assert.Equal(0, calls);
    }

    [Fact]
    public void PowerPolicy_AffectsOnlyItsPowerSource()
    {
        var id = Guid.Parse("abfc2519-3608-4c2a-94ea-171b0ed546ab");
        _registry.SetDWord($@"HKLM\Software\Policies\Microsoft\Power\PowerSettings\{id:D}", "ACSettingIndex", 0);
        var reader = new PolicyControlStateReader(_registry);
        Assert.True(reader.ReadPowerSetting(id, true).BlocksChanges);
        Assert.False(reader.ReadPowerSetting(id, false).BlocksChanges);
    }

    [Fact]
    public void Home_SavedPowerPolicyDoesNotDisableOrdinaryPlanEditor()
    {
        var id = Guid.Parse("abfc2519-3608-4c2a-94ea-171b0ed546ab");
        var key = $@"Software\Policies\Microsoft\Power\PowerSettings\{id:D}";
        _registry.SetString(@"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion", "EditionID", "Core");
        _registry.SetDWord("HKLM\\" + key, "ACSettingIndex", 0);
        var source = new PolicySourceSnapshot("Computer policy", PolicyScope.Machine, PolicyFileStatus.Loaded,
            [new(key, "ACSettingIndex", 4, [0, 0, 0, 0])]);
        var reader = new PolicyControlStateReader(_registry, () => [source], new CapabilityDetector(_registry));
        var state = reader.ReadPowerSetting(id, true);
        Assert.False(state.BlocksChanges);
        Assert.Contains("not verified", state.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void UnexpectedSleepPolicyValue_CannotBeOverwritten()
    {
        _registry.SetDWord(@"HKLM\Software\Policies\Microsoft\Power\PowerSettings\abfc2519-3608-4c2a-94ea-171b0ed546ab", "ACSettingIndex", 99);
        var state = new PolicyControlStateReader(_registry).Read("Power", "allow-sleep");
        Assert.True(state.BlocksChanges);
        Assert.Contains("unknown", state.Message, StringComparison.Ordinal);
    }
}
