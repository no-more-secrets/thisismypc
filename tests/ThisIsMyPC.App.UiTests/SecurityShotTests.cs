using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using ThisIsMyPC.App.UiTests.Fakes;
using ThisIsMyPC.App.ViewModels;
using ThisIsMyPC.App.Views;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Core.Policies;
using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Enforcement;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Modules.Security;

namespace ThisIsMyPC.App.UiTests;

public sealed class SecurityShotTests
{
    [AvaloniaFact]
    public void AdditionalDefenderControls_RenderAtNarrowWidth()
    {
        var registry = new UiFakeRegistryService();
        registry.WriteString(@"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion", "EditionID", "Enterprise");
        var detector = new CapabilityDetector(registry);
        var pending = new PendingChangesService(capabilityDetector: detector);
        using var vm = new SecurityViewModel(registry, pending, detector);
        using var session = UiSession.ForView(new SecurityView(), vm, "security-additions", width: 590, height: 580);
        string[] ids = ["behavior-monitoring", "file-activity-monitoring", "download-scanning", "script-scanning",
            "scan-on-protection-enable", "cloud-block-level", "heuristic-detection", "archive-scanning",
            "network-file-scanning", "update-before-scan", "intelligence-on-battery", "intelligence-on-startup"];
        foreach (var theme in new[] { ThemeVariant.Dark, ThemeVariant.Light })
        {
            session.SetTheme(theme);
            foreach (var id in ids)
            {
                var row = vm.Rows.Single(r => r.Setting.Id == id);
                vm.SearchText = row.Setting.Title;
                session.Pump();
                Assert.True(session.IsTextVisible(row.Setting.Title));
                Assert.True(row.IsControlEnabled);
                session.Screenshot($"{id}-{theme.Key}");
            }
        }
        vm.SearchText = "Cloud blocking level";
        session.Pump();
        session.Click(session.Find<ComboBox>(c => c.IsEffectivelyVisible));
        session.ClickText("High plus");
        session.Pump();
        Assert.Equal("4", Assert.Single(Assert.Single(pending.PendingGroups).Changes).AfterValue);
        session.Screenshot("cloud-level-staged");
    }

    [AvaloniaFact]
    public async Task ManagedPolicy_UsesShortNoticeAndInformationHover()
    {
        var registry = new UiFakeRegistryService();
        registry.WriteString(@"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion", "EditionID", "Enterprise");
        var setting = SecurityCatalog.Settings.Single(s => s.Id == "real-time-protection");
        var target = setting.Targets[0];
        registry.WriteDWord(target.Key, target.Name, 1);
        var source = new PolicySourceSnapshot("Computer policy", PolicyScope.Machine, PolicyFileStatus.Loaded,
            [new(target.Key[5..], target.Name, 4, [1, 0, 0, 0])]);
        var policies = new PolicyControlStateReader(registry, () => [source]);
        using var vm = new SecurityViewModel(registry, new PendingChangesService(), new CapabilityDetector(registry), policies);
        vm.SearchText = setting.Title;
        using var session = UiSession.ForView(new SecurityView(), vm, "security-copy", width: 650, height: 550);
        var row = vm.Rows.Single(r => r.Setting.Id == setting.Id);
        var info = session.Find<Border>(b => Avalonia.Automation.AutomationProperties.GetName(b) == "Policy details" && b.IsEffectivelyVisible);
        Assert.True(row.IsControlEnabled);
        Assert.True(session.IsTextVisible("Saved local policy"));
        Assert.False(session.IsTextVisible("Policy setting: Off"));
        Assert.DoesNotContain("Application on Pro", row.EditionNotice, StringComparison.Ordinal);
        foreach (var theme in new[] { ThemeVariant.Dark, ThemeVariant.Light })
        {
            session.SetTheme(theme);
            session.Screenshot($"managed-{theme.Key}");
            session.Hover(info);
            ToolTip.SetIsOpen(info, true);
            await Task.Delay(300);
            session.Pump();
            var detail = Assert.IsType<TextBlock>(ToolTip.GetTip(info));
            Assert.Equal(row.PolicyText, detail.Text);
            var popup = TopLevel.GetTopLevel(detail);
            Assert.NotNull(popup);
            using var frame = popup.CaptureRenderedFrame();
            Assert.NotNull(frame);
            frame.Save(System.IO.Path.Combine(UiSession.FindRepoRoot(), "artifacts", "ui-shots", "security-copy", $"tooltip-{theme.Key}.png"));
            ToolTip.SetIsOpen(info, false);
        }
    }

    [AvaloniaFact]
    public void ChangingAChoice_DoesNotRescanUnrelatedPolicies()
    {
        var registry = new UiFakeRegistryService();
        registry.WriteString(@"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion", "EditionID", "Enterprise");
        var detector = new CapabilityDetector(registry);
        var sourceReads = 0;
        var policies = new PolicyControlStateReader(registry, () => { sourceReads++; return []; });
        var pending = new PendingChangesService(capabilityDetector: detector, policyStates: policies);
        using var vm = new SecurityViewModel(registry, pending, detector, policies);
        var row = vm.Rows.Single(r => r.Setting.Id == "secure-sign-in");
        sourceReads = 0;
        row.SelectedOption = row.Choices.Single(o => o.Id == "on");
        Assert.Equal(2, sourceReads); // Fresh before-state and independent queue validation.
        sourceReads = 0;
        row.SelectedOption = row.Choices.Single(o => o.Id == "off");
        Assert.Equal(2, sourceReads);
        Assert.Single(pending.PendingGroups);
        sourceReads = 0;
        pending.DiscardAll();
        Assert.Equal(0, sourceReads);
        Assert.Equal("default", row.SelectedOption?.Id);
    }

    [AvaloniaFact]
    public async Task CompletedApply_RefreshesTheDisplayedLiveState()
    {
        var registry = new UiFakeRegistryService();
        registry.WriteString(@"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion", "EditionID", "Enterprise");
        var detector = new CapabilityDetector(registry);
        var pending = new PendingChangesService(new PrimaryExecutor(), detector);
        using var vm = new SecurityViewModel(registry, pending, detector);
        var row = vm.Rows.Single(r => r.Setting.Id == "secure-sign-in");
        row.SelectedOption = row.Choices.Single(o => o.Id == "on");
        var module = new SecurityModule(registry);
        var result = await pending.ApplyAllAsync(module.ApplyChangeAsync, module.RevertChangeAsync);
        Assert.True(result.IsSuccess);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Equal("on", row.SelectedOption?.Id);
        Assert.False(row.HasPendingChange);
    }

    [AvaloniaFact]
    public void CachedDisplay_DoesNotBypassFreshPolicyValidation()
    {
        var registry = new UiFakeRegistryService();
        registry.WriteString(@"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion", "EditionID", "Enterprise");
        var detector = new CapabilityDetector(registry);
        var unavailable = false;
        var policies = new PolicyControlStateReader(registry, () => unavailable
            ? [new("Computer policy", PolicyScope.Machine, PolicyFileStatus.Unreadable, [])] : []);
        var pending = new PendingChangesService(capabilityDetector: detector, policyStates: policies);
        using var vm = new SecurityViewModel(registry, pending, detector, policies);
        var row = vm.Rows.Single(r => r.Setting.Id == "secure-sign-in");
        row.SelectedOption = row.Choices.Single(o => o.Id == "on");
        unavailable = true;
        row.SelectedOption = row.Choices.Single(o => o.Id == "off");
        Assert.Single(pending.PendingGroups);
        Assert.Equal("0", pending.PendingGroups[0].Changes[0].AfterValue);
        Assert.Equal("on", row.SelectedOption?.Id);
    }

    private sealed class PrimaryExecutor : IEnforcementExecutor
    {
        public async Task<EnforcementResult> ExecuteAsync(ChangeDescriptor change,
            Func<ChangeDescriptor, Task<OperationResult<bool>>> applyPrimary, CancellationToken cancellationToken = default)
        {
            var result = await applyPrimary(change);
            return new() { IsSuccess = result.IsSuccess, ErrorMessage = result.ErrorMessage, ErrorCategory = result.ErrorCategory };
        }
        public Task<EnforcementResult> RevertAsync(ChangeDescriptor change,
            Func<ChangeDescriptor, Task<OperationResult<bool>>> revertPrimary, CancellationToken cancellationToken = default)
            => ExecuteAsync(change, revertPrimary, cancellationToken);
    }

    [AvaloniaTheory]
    [InlineData("Core")]
    [InlineData("Professional")]
    [InlineData("Enterprise")]
    public void EverySection_ShowsPolicyStatesAndEditionGating(string edition)
    {
        var registry = new UiFakeRegistryService();
        registry.WriteString(@"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion", "EditionID", edition);
        var detector = new CapabilityDetector(registry);
        var pending = new PendingChangesService(capabilityDetector: detector);
        using var vm = new SecurityViewModel(registry, pending, detector);
        var card = new Border { Margin = new Thickness(16), BorderThickness = new Thickness(1), Child = new SecurityView() };
        card.Bind(Border.BackgroundProperty, card.GetResourceObservable("RaisedBrush"));
        card.Bind(Border.BorderBrushProperty, card.GetResourceObservable("OutlineBrush"));
        using var session = UiSession.ForView(card, vm, "security", width: 1040, height: 850);
        Assert.Equal(28, vm.Rows.Count);
        foreach (var theme in new[] { ThemeVariant.Dark, ThemeVariant.Light })
        {
            session.SetTheme(theme);
            for (var index = 0; index < vm.Sections.Count; index++)
            {
                session.ClickText(vm.Sections[index].Header);
                session.Pump();
                Assert.Equal(index, vm.SelectedTabIndex);
                session.Screenshot($"{edition}-{index}-{theme.Key}");
            }
        }
        foreach (var row in vm.Rows)
            Assert.Equal(edition == "Enterprise" || (edition == "Professional" && row.Setting.Id == "secure-sign-in"), row.IsControlEnabled);
        vm.SearchText = "Ctrl+Alt+Delete";
        session.Pump();
        Assert.True(session.IsTextVisible("Requires Windows Pro or higher"));
        session.Screenshot($"{edition}-search");
    }

    [AvaloniaFact]
    public void Choices_ReplacePendingAndDiscardReturnsToLiveState()
    {
        var registry = new UiFakeRegistryService();
        registry.WriteString(@"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion", "EditionID", "Enterprise");
        var detector = new CapabilityDetector(registry);
        var pending = new PendingChangesService(capabilityDetector: detector);
        using var vm = new SecurityViewModel(registry, pending, detector);
        var row = vm.Rows.Single(r => r.Setting.Id == "smartscreen");
        row.SelectedOption = row.Choices.Single(o => o.Id == "warn");
        row.SelectedOption = row.Choices.Single(o => o.Id == "block");
        Assert.Single(pending.PendingGroups);
        Assert.Equal("Block", pending.PendingGroups[0].Changes[1].AfterValue);
        using var reopened = new SecurityViewModel(registry, pending, detector);
        Assert.Equal("block", reopened.Rows.Single(r => r.Setting.Id == "smartscreen").SelectedOption?.Id);
        pending.DiscardAll();
        Assert.Equal("default", row.SelectedOption?.Id);
        Assert.False(row.HasPendingChange);
    }

    [AvaloniaFact]
    public void NarrowPage_StagesUsingTheDropdown()
    {
        var registry = new UiFakeRegistryService();
        registry.WriteString(@"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion", "EditionID", "Enterprise");
        var detector = new CapabilityDetector(registry);
        var pending = new PendingChangesService(capabilityDetector: detector);
        using var vm = new SecurityViewModel(registry, pending, detector);
        using var session = UiSession.ForView(new SecurityView(), vm, "security-narrow", width: 590, height: 650);
        var combo = session.Find<ComboBox>(_ => true);
        session.Click(combo);
        session.ClickText("On");
        session.Pump();
        Assert.Single(pending.PendingGroups);
        Assert.Equal("0", pending.PendingGroups[0].Changes[0].AfterValue);
        session.Screenshot("staged-sign-in");
        session.ClickText("App protection");
        session.Screenshot("app-protection");
    }
}
