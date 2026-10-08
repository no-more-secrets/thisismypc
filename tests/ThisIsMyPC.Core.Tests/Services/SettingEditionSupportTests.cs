using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Modules;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;

namespace ThisIsMyPC.Core.Tests.Services;

public sealed class SettingEditionSupportTests
{
    private const string Policy = @"HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU\NoAutoUpdate";

    [Theory]
    [InlineData(WindowsSku.Home, true)]
    [InlineData(WindowsSku.Pro, false)]
    [InlineData(WindowsSku.Enterprise, false)]
    [InlineData(WindowsSku.Education, false)]
    [InlineData(null, true)]
    public void PolicyWithoutMetadata_DefaultsToPro(WindowsSku? edition, bool blocked)
    {
        var required = SettingEditionSupport.RequiredEdition(Policy, null);
        Assert.Equal(WindowsSku.Pro, required);
        Assert.Equal(blocked, SettingEditionSupport.BlockReason(edition, required) is not null);
    }

    [Fact]
    public void OrdinaryPreference_IsAvailableOnHome()
    {
        Assert.Null(SettingEditionSupport.RequiredEdition(@"HKCU\Control Panel\Accessibility\StickyKeys\Flags", null));
        Assert.Null(SettingEditionSupport.RequiredEdition(Policy, WindowsSku.Home));
    }

    [Fact]
    public void PrecautionaryExclusions_ExplainUnverifiedSupport()
    {
        Assert.Contains("Not verified", SettingEditionSupport.BlockReason(WindowsSku.Home, Policy, null), StringComparison.Ordinal);
        const string defender = @"HKLM\SOFTWARE\Policies\Microsoft\Windows Defender\DisableAntiSpyware";
        Assert.Contains("Not verified", SettingEditionSupport.BlockReason(WindowsSku.Pro, defender, null), StringComparison.Ordinal);
        Assert.Null(SettingEditionSupport.BlockReason(WindowsSku.Pro, defender, WindowsSku.Pro));
    }

    [Theory]
    [InlineData(@"HKLM\SOFTWARE\Policies\Microsoft\Windows Defender\DisableAntiSpyware", "1", WindowsSku.Enterprise)]
    [InlineData(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\CloudContent\DisableWindowsConsumerFeatures", "1", WindowsSku.Enterprise)]
    [InlineData(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\DataCollection\AllowTelemetry", "0", WindowsSku.Enterprise)]
    [InlineData(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\DataCollection\AllowTelemetry", "1", WindowsSku.Pro)]
    public void SpecificPolicyAndValue_SetRequiredEdition(string location, string value, WindowsSku required)
        => Assert.Equal(required, SettingEditionSupport.RequiredEdition(location, null, value));

    [Fact]
    public void QueueRejectsWholeGroup_WhenAnyChangeNeedsHigherEdition()
    {
        var pending = new PendingChangesService(capabilityDetector: new Detector { Sku = WindowsSku.Home });
        var group = new ChangeGroup
        {
            GroupId = "mixed", DisplayName = "Mixed", Description = "Mixed",
            Changes = [Change(@"HKCU\Control Panel\Test\Preference"), Change(Policy)],
        };
        Assert.Throws<InvalidOperationException>(() => pending.Stage(group));
        Assert.Empty(pending.PendingGroups);
    }

    [Fact]
    public async Task EditionChangesAfterStaging_ApplyRefusesBeforePrepareOrWrites()
    {
        var detector = new Detector { Sku = WindowsSku.Pro };
        var pending = new PendingChangesService(capabilityDetector: detector);
        pending.Stage(Change(Policy));
        detector.Sku = WindowsSku.Home;
        var calls = 0;
        Task<OperationResult<bool>> Write(ChangeDescriptor _) { calls++; return Task.FromResult(OperationResult<bool>.Success(true)); }
        var result = await pending.ApplyAllAsync(Write, Write, _ => calls++, CancellationToken.None);
        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCategory.SkuRestricted, result.ErrorCategory);
        Assert.Equal(0, calls);
        Assert.Equal(1, pending.PendingCount);
    }

    private static ChangeDescriptor Change(string location) => new()
    {
        ModuleId = "test", SettingId = location, DisplayName = "Test", SystemLocation = location,
        BeforeValue = "0", AfterValue = "1", BeforeDisplay = "Off", AfterDisplay = "On",
        ValueType = ChangeValueType.Registry_DWord,
    };

    private sealed class Detector : ICapabilityDetector
    {
        public WindowsSku? Sku { get; set; }
        public string? SkuDetectionFailureReason => null;
        public bool IsOwnerModeAvailable => false;
        public bool IsSkuRestricted(WindowsSku? restriction) => SettingEditionSupport.BlockReason(Sku, restriction) is not null;
        public bool IsAvailable(SystemCapability capability) => true;
        public ModuleAvailability GetAvailability(SystemCapability capability) => new(true);
        public IReadOnlyList<CapabilityReportRow> GetCapabilityReport() => [];
    }
}
