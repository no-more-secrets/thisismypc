using ThisIsMyPC.App.ViewModels;
using ThisIsMyPC.Core.Cards;
using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Modules;
using ThisIsMyPC.Core.Services;

namespace ThisIsMyPC.Integration.Tests.ViewModels;

/// <summary>Story 10.3: enforcement badges, SKU callouts, Owner Mode degradation.</summary>
public sealed class SettingCardBadgeTests
{
    private readonly PendingChangesService _pending = new();

    private sealed class StubDetector : ICapabilityDetector
    {
        public WindowsSku? Sku { get; init; }
        public bool OwnerMode { get; set; }
        public string? SkuDetectionFailureReason => null;
        public bool IsSkuRestricted(WindowsSku? restriction)
            => restriction is { } required && Sku is { } current && current.Tier() < required.Tier();
        public bool IsAvailable(SystemCapability capability) => true;
        public ModuleAvailability GetAvailability(SystemCapability capability) => new(true);
        public bool IsOwnerModeAvailable => OwnerMode;
        public IReadOnlyList<CapabilityReportRow> GetCapabilityReport() => [];
    }

    /// <summary>Turning the fake on flips the paired detector and raises StateChanged, like the real service.</summary>
    private sealed class FakeOwnerModeLifecycle(StubDetector detector) : ThisIsMyPC.App.Services.IOwnerModeLifecycle
    {
        public event EventHandler? StateChanged;
        public string? FailWith { get; set; }
        public int EnableCalls { get; private set; }

        public void RaiseStateChanged() => StateChanged?.Invoke(this, EventArgs.Empty);

        public Task<Core.Results.OperationResult<bool>> EnableAsync(CancellationToken cancellationToken = default)
        {
            EnableCalls++;
            if (FailWith is not null)
                return Task.FromResult(Core.Results.OperationResult<bool>.Failure(FailWith, Core.Results.ErrorCategory.ServiceUnavailable));
            detector.OwnerMode = true;
            StateChanged?.Invoke(this, EventArgs.Empty);
            return Task.FromResult(Core.Results.OperationResult<bool>.Success(true));
        }
    }

    private SettingCardViewModel CreateVm(
        EnforcementProfile? enforcement = null,
        WindowsSku? skuRestriction = null,
        bool ownerModeRequired = false,
        ICapabilityDetector? detector = null,
        ThisIsMyPC.App.Services.IOwnerModeLifecycle? ownerMode = null)
    {
        var source = new SettingCardSource
        {
            Model = new SettingCardModel
            {
                SettingId = "badge-card",
                ModuleId = "Test",
                DisplayName = "Badge card",
                Description = "A setting.",
                ControlType = SettingControlType.Toggle,
                CurrentValue = "0",
                Enforcement = enforcement,
                SkuRestriction = skuRestriction,
                OwnerModeRequired = ownerModeRequired,
            },
            CreateToggleGroup = _ => new ChangeGroup
            {
                GroupId = Guid.NewGuid().ToString("N"),
                DisplayName = "Badge card",
                Description = "Badge card",
                Changes = [],
            },
            ReadCurrentState = () => false,
        };
        return new SettingCardViewModel(source, _pending, detector, ownerMode);
    }

    [Fact]
    public void EnforcementProfile_ProducesBadgeAndReversionRisks()
    {
        var vm = CreateVm(enforcement: new EnforcementProfile
        {
            Level = EnforcementLevel.Simple,
            Summary = "Windows is known to revert this setting",
            ReversionRisks = ["Windows Update", "Web Experience Pack deployment"],
        });

        Assert.True(vm.HasEnforcementBadge);
        Assert.Equal("Windows is known to revert this setting", vm.EnforcementSummary);
        Assert.Equal("May revert via: Windows Update, Web Experience Pack deployment", vm.ReversionRisksText);
    }

    [Fact]
    public void NoEnforcement_NoBadge()
    {
        var vm = CreateVm();

        Assert.False(vm.HasEnforcementBadge);
        Assert.False(vm.HasReversionRisks);
    }

    [Fact]
    public void SkuBelowTheMinimumTier_ShowsNotice_ToggleStaysEnabled()
    {
        var vm = CreateVm(
            skuRestriction: WindowsSku.Pro,
            detector: new StubDetector { Sku = WindowsSku.Home });

        Assert.True(vm.HasSkuNotice);
        Assert.Contains("Requires Pro or higher", vm.SkuNotice, StringComparison.Ordinal);
        Assert.True(vm.IsControlEnabled);
    }

    [Fact]
    public void EducationMinimum_NoticeNamesBothTopTierEditions()
    {
        var vm = CreateVm(
            skuRestriction: WindowsSku.Education,
            detector: new StubDetector { Sku = WindowsSku.Pro });

        Assert.True(vm.HasSkuNotice);
        Assert.Contains("Enterprise or Education", vm.SkuNotice, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(WindowsSku.Pro)]       // meets the minimum
    [InlineData(WindowsSku.Education)] // above the minimum
    [InlineData(null)]                 // unknown edition
    public void SkuAtOrAboveTheMinimum_NoNotice(WindowsSku? detectedSku)
    {
        var vm = CreateVm(
            skuRestriction: WindowsSku.Pro,
            detector: new StubDetector { Sku = detectedSku });

        Assert.False(vm.HasSkuNotice);
    }

    [Fact]
    public void NullDetector_NoSkuNotice()
    {
        var vm = CreateVm(skuRestriction: WindowsSku.Pro, detector: null);

        Assert.False(vm.HasSkuNotice);
    }

    [Fact]
    public void OwnerModeRequired_ServiceUnavailable_ControlDisabledWithCallout()
    {
        var vm = CreateVm(
            ownerModeRequired: true,
            detector: new StubDetector { OwnerMode = false });

        Assert.True(vm.IsOwnerModeDegraded);
        Assert.False(vm.IsControlEnabled);
        Assert.Contains("Owner Mode", vm.OwnerModeCallout, StringComparison.Ordinal);
        Assert.False(vm.ShowOwnerModeBadge);
    }

    [Fact]
    public void OwnerModeRequired_ServiceAvailable_ControlEnabledWithSubtleBadge()
    {
        var vm = CreateVm(
            ownerModeRequired: true,
            detector: new StubDetector { OwnerMode = true });

        Assert.False(vm.IsOwnerModeDegraded);
        Assert.True(vm.IsControlEnabled);
        Assert.True(vm.ShowOwnerModeBadge);
        // The callout string always exists; IsOwnerModeDegraded gates its visibility.
        Assert.False(vm.CanTurnOnOwnerMode);
    }

    [Fact]
    public void OwnerModeRequired_NullDetector_TreatedAsUnavailable()
    {
        var vm = CreateVm(ownerModeRequired: true, detector: null);

        Assert.True(vm.IsOwnerModeDegraded);
        Assert.False(vm.IsControlEnabled);
    }

    [Fact]
    public void NotOwnerModeRequired_NeverDegraded()
    {
        var vm = CreateVm(ownerModeRequired: false, detector: new StubDetector { OwnerMode = false });

        Assert.False(vm.IsOwnerModeDegraded);
        Assert.True(vm.IsControlEnabled);
        Assert.False(vm.ShowOwnerModeBadge);
    }

    [Fact]
    public void DegradedWithLifecycle_OffersTurnOnAction()
    {
        var detector = new StubDetector { OwnerMode = false };
        var vm = CreateVm(
            ownerModeRequired: true,
            detector: detector,
            ownerMode: new FakeOwnerModeLifecycle(detector));

        Assert.True(vm.IsOwnerModeDegraded);
        Assert.True(vm.CanTurnOnOwnerMode);
    }

    [Fact]
    public void DegradedWithoutLifecycle_NoTurnOnAction()
    {
        var vm = CreateVm(ownerModeRequired: true, detector: new StubDetector { OwnerMode = false });

        Assert.True(vm.IsOwnerModeDegraded);
        Assert.False(vm.CanTurnOnOwnerMode);
    }

    [Fact]
    public async Task TurnOnOwnerMode_Success_UnDegradesTheCardLive()
    {
        var detector = new StubDetector { OwnerMode = false };
        var lifecycle = new FakeOwnerModeLifecycle(detector);
        var vm = CreateVm(ownerModeRequired: true, detector: detector, ownerMode: lifecycle);

        await vm.TurnOnOwnerModeCommand.ExecuteAsync(null);

        Assert.Equal(1, lifecycle.EnableCalls);
        Assert.False(vm.IsOwnerModeDegraded);
        Assert.True(vm.IsControlEnabled);
        Assert.True(vm.ShowOwnerModeBadge);
        Assert.Null(vm.OwnerModeError);
    }

    [Fact]
    public async Task TurnOnOwnerMode_Failure_SurfacesErrorAndStaysDegraded()
    {
        var detector = new StubDetector { OwnerMode = false };
        var lifecycle = new FakeOwnerModeLifecycle(detector) { FailWith = "SCM said no" };
        var vm = CreateVm(ownerModeRequired: true, detector: detector, ownerMode: lifecycle);

        await vm.TurnOnOwnerModeCommand.ExecuteAsync(null);

        Assert.True(vm.IsOwnerModeDegraded);
        Assert.False(vm.IsControlEnabled);
        Assert.Equal("SCM said no", vm.OwnerModeError);
    }

    [Fact]
    public void ExternalStateChange_RefreshesDegradation()
    {
        var detector = new StubDetector { OwnerMode = false };
        var lifecycle = new FakeOwnerModeLifecycle(detector);
        var vm = CreateVm(ownerModeRequired: true, detector: detector, ownerMode: lifecycle);

        // Another card's button (or the Settings section) started the service.
        detector.OwnerMode = true;
        lifecycle.RaiseStateChanged();

        Assert.False(vm.IsOwnerModeDegraded);
        Assert.True(vm.ShowOwnerModeBadge);
    }

    [Fact]
    public void Badges_UnaffectedByDisplayModeFlags()
    {
        var vm = CreateVm(
            enforcement: new EnforcementProfile { Level = EnforcementLevel.Simple, Summary = "s" },
            skuRestriction: WindowsSku.Pro,
            ownerModeRequired: true,
            detector: new StubDetector { Sku = WindowsSku.Home, OwnerMode = false });

        // Flip through all four display modes — badge state never changes.
        foreach (var (registry, compact) in new[] { (false, false), (true, false), (false, true), (true, true) })
        {
            vm.IsRegistryDataVisible = registry;
            vm.IsDescriptionVisible = !compact;

            Assert.True(vm.HasEnforcementBadge);
            Assert.True(vm.HasSkuNotice);
            Assert.True(vm.IsOwnerModeDegraded);
        }
    }
}
