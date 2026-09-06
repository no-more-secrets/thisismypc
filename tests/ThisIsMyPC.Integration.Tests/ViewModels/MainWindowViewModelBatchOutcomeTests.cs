using ThisIsMyPC.App.Services;
using ThisIsMyPC.App.ViewModels;
using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Modules;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;

namespace ThisIsMyPC.Integration.Tests.ViewModels;

/// <summary>
/// The app's Apply flow over the batch-safety contract: finished groups are
/// recorded exactly once on every exit, an unresolved group blocks the queue
/// with instructions the person can follow, and a result without a failed
/// change does not crash the status line. The Discard All rescan is covered
/// by DiscardRescanTests in the UI test project, where a dispatcher runs.
/// </summary>
public sealed class MainWindowViewModelBatchOutcomeTests
{
    private const string ModuleId = "TestModule";

    private static (MainWindowViewModel Vm, Fakes.FakeChangeHistoryService History, ReviewPanelViewModel Review)
        Create(IPendingChangesService pending, params IModule[] modules)
    {
        var navigationService = new NavigationService(modules);
        var history = new Fakes.FakeChangeHistoryService();
        var review = new ReviewPanelViewModel(pending, new Core.Sets.CustomSetWriter(Path.Combine(Path.GetTempPath(), $"tipc-bo-{Guid.NewGuid():N}")));
        var vm = new MainWindowViewModel(
            navigationService, pending, history, new Fakes.FakeRegistryService(), new Fakes.FakeExplorerRestartService(),
            review, new Fakes.FakeSetProvider(), [],
            new Core.Sets.CustomSetWriter(Path.Combine(Path.GetTempPath(), $"tipc-bo-{Guid.NewGuid():N}")),
            new Fakes.FakeRestorePointService());
        return (vm, history, review);
    }

    private static ChangeDescriptor Change(string settingId, string displayName, RestartRequirement restart = RestartRequirement.None) => new()
    {
        ModuleId = ModuleId,
        SettingId = settingId,
        DisplayName = displayName,
        SystemLocation = $@"HKLM\Test\{settingId}",
        BeforeValue = "0",
        AfterValue = "1",
        BeforeDisplay = "Off",
        AfterDisplay = "On",
        ValueType = ChangeValueType.Registry_DWord,
        Category = ChangeCategory.Enable,
        RestartRequirement = restart,
    };

    private static ChangeGroup Group(string id, params ChangeDescriptor[] changes) => new()
    {
        GroupId = id,
        DisplayName = changes[0].DisplayName,
        Description = changes[0].DisplayName,
        Changes = changes,
    };

    /// <summary>A module whose apply fails for one setting id and counts every call.</summary>
    private static Fakes.FakeModule FailingAt(string settingId, List<string> applied) =>
        new(ModuleId, change =>
        {
            applied.Add(change.SettingId);
            return Task.FromResult(change.SettingId == settingId
                ? OperationResult<bool>.Failure("Registry access denied", ErrorCategory.AccessDenied)
                : OperationResult<bool>.Success(true));
        });

    [Fact]
    public async Task FirstGroupSucceeds_SecondFails_HistoryHoldsTheFirstOnly()
    {
        var applied = new List<string>();
        var pending = new PendingChangesService();
        var (vm, history, review) = Create(pending, FailingAt("b", applied));
        await vm.InitializeAsync();
        pending.Stage(Group("g1", Change("a", "First setting", RestartRequirement.Reboot)));
        pending.Stage(Group("g2", Change("b", "Second setting")));
        vm.IsReviewPanelOpen = true;

        await vm.ApplyAllCommand.ExecuteAsync(null);

        // Recorded once, with only the group that completed. The failed change is
        // uncertain and never reaches history.
        var recorded = Assert.Single(history.RecordedResults);
        Assert.Equal(["a"], recorded.Applied.Select(c => c.SettingId));
        Assert.False(recorded.IsSuccess);
        Assert.Equal(MutationFailureKind.ChangeFailed, recorded.FailureKind);
        Assert.DoesNotContain(recorded.Applied, c => c.SettingId == "b");

        // The failed group stays staged and marked; the panel stays open to show it.
        Assert.Equal(["g2"], pending.PendingGroups.Select(g => g.GroupId));
        Assert.True(vm.HasUnresolvedGroups);
        Assert.False(vm.CanApplyPending);
        Assert.True(vm.IsReviewPanelOpen);
        var group = Assert.Single(review.ReviewGroups);
        Assert.True(group.NeedsReview);
        Assert.Contains("unknown", group.ReviewNote, StringComparison.OrdinalIgnoreCase);
        Assert.True(review.HasUnresolvedGroups);
        Assert.Contains("Discard All", review.UnresolvedNotice, StringComparison.Ordinal);
        Assert.Contains("does not undo", review.UnresolvedNotice, StringComparison.Ordinal);

        // The status says what completed, what is unknown, and what to click.
        Assert.Equal(StatusSeverity.Error, vm.StatusSeverity);
        Assert.Contains("\"Second setting\" could not be applied", vm.StatusMessage, StringComparison.Ordinal);
        Assert.Contains("Registry access denied", vm.StatusMessage, StringComparison.Ordinal);
        Assert.Contains("current value is unknown", vm.StatusMessage, StringComparison.Ordinal);
        Assert.Contains("1 change before it completed", vm.StatusMessage, StringComparison.Ordinal);
        Assert.Contains("Discard All", vm.StatusMessage, StringComparison.Ordinal);

        // The completed change needs a reboot; the partial outcome says so.
        Assert.True(vm.IsRestartNotificationVisible);
        Assert.Contains("reboot", vm.RestartNotificationMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("reboot required", vm.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ApplyAgainWhileUnresolved_WritesNothing_RecordsNothing_AndExplains()
    {
        var applied = new List<string>();
        var pending = new PendingChangesService();
        var (vm, history, _) = Create(pending, FailingAt("b", applied));
        await vm.InitializeAsync();
        pending.Stage(Group("g1", Change("a", "First setting")));
        pending.Stage(Group("g2", Change("b", "Second setting")));
        await vm.ApplyAllCommand.ExecuteAsync(null);
        var writesAfterFirstApply = applied.Count;
        pending.Stage(Group("g3", Change("c", "Third setting")));

        // Never retried automatically, and a second click does not retry either.
        await vm.ApplyAllCommand.ExecuteAsync(null);

        Assert.Equal(writesAfterFirstApply, applied.Count);
        Assert.Single(history.RecordedResults);
        Assert.Equal(["g2", "g3"], pending.PendingGroups.Select(g => g.GroupId));
        Assert.Equal(StatusSeverity.Error, vm.StatusSeverity);
        Assert.Contains("\"Second setting\"", vm.StatusMessage, StringComparison.Ordinal);
        Assert.Contains("nothing was applied", vm.StatusMessage, StringComparison.Ordinal);
        Assert.Contains("Discard All", vm.StatusMessage, StringComparison.Ordinal);
        Assert.True(vm.HasUnresolvedGroups);
    }

    [Fact]
    public async Task CancelledResult_WithoutFailedChange_IsReportedAsAWarning_AndRecordedOnce()
    {
        var pending = new Fakes.FakePendingChangesService();
        var done = Change("a", "First setting", RestartRequirement.ExplorerRestart);
        var putBack = Change("b", "Second setting");
        pending.ApplyResult = new MutationResult
        {
            IsSuccess = false,
            FailureKind = MutationFailureKind.Cancelled,
            Failed = null,
            Applied = [done],
            RolledBack = [putBack],
            ErrorMessage = "The batch was cancelled before every change was applied.",
            RequiredRestarts = [RestartRequirement.ExplorerRestart],
        };
        var (vm, history, _) = Create(pending, new Fakes.FakeModule(ModuleId));
        await vm.InitializeAsync();
        pending.Stage(Group("g1", done));

        await vm.ApplyAllCommand.ExecuteAsync(null);

        Assert.Equal(1, pending.ApplyCalls);
        var recorded = Assert.Single(history.RecordedResults);
        Assert.Equal(["a"], recorded.Applied.Select(c => c.SettingId));
        Assert.Equal(StatusSeverity.Warning, vm.StatusSeverity);
        Assert.StartsWith("Apply cancelled.", vm.StatusMessage, StringComparison.Ordinal);
        Assert.Contains("1 change completed", vm.StatusMessage, StringComparison.Ordinal);
        Assert.Contains("1 change was put back", vm.StatusMessage, StringComparison.Ordinal);
        Assert.True(vm.IsRestartNotificationVisible);
        Assert.True(vm.IsRestartActionAvailable);
        Assert.False(vm.HasUnresolvedGroups);
    }

    [Fact]
    public async Task FailedResult_WithoutFailedChange_DoesNotCrash_AndRecordsNothingWhenNothingApplied()
    {
        var pending = new Fakes.FakePendingChangesService();
        pending.ApplyResult = new MutationResult
        {
            IsSuccess = false,
            FailureKind = MutationFailureKind.ChangeFailed,
            Failed = null,
            Applied = [],
            RolledBack = [],
            ErrorMessage = null,
            ErrorCategory = null,
        };
        var (vm, history, _) = Create(pending, new Fakes.FakeModule(ModuleId));
        await vm.InitializeAsync();
        pending.Stage(Group("g1", Change("a", "First setting")));

        await vm.ApplyAllCommand.ExecuteAsync(null);

        Assert.Empty(history.RecordedResults);
        Assert.Equal(StatusSeverity.Error, vm.StatusSeverity);
        Assert.Contains("A change could not be applied", vm.StatusMessage, StringComparison.Ordinal);
        Assert.Contains("Nothing else was changed", vm.StatusMessage, StringComparison.Ordinal);
        Assert.False(vm.IsApplying);
    }

    [Fact]
    public async Task CancelledResult_WithARollbackFailure_IsAnError_AndNamesTheChange()
    {
        var pending = new Fakes.FakePendingChangesService();
        var stuck = Change("b", "Stuck setting");
        pending.ApplyResult = new MutationResult
        {
            IsSuccess = false,
            FailureKind = MutationFailureKind.Cancelled,
            Failed = null,
            Applied = [],
            RolledBack = [],
            RollbackFailures = [new RollbackFailure(stuck, "revert refused", null)],
            Uncertain = [stuck],
        };
        var (vm, history, _) = Create(pending, new Fakes.FakeModule(ModuleId));
        await vm.InitializeAsync();
        pending.Stage(Group("g1", stuck));

        await vm.ApplyAllCommand.ExecuteAsync(null);

        Assert.Empty(history.RecordedResults);
        Assert.Equal(StatusSeverity.Error, vm.StatusSeverity);
        Assert.Contains("\"Stuck setting\" could not be put back", vm.StatusMessage, StringComparison.Ordinal);
        Assert.Contains("review panel", vm.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatApplyError_ReconciliationRequired_NamesTheGroupAndTheNextClick()
    {
        var result = new MutationResult
        {
            IsSuccess = false,
            FailureKind = MutationFailureKind.ReconciliationRequired,
            Failed = null,
            Applied = [],
            RolledBack = [],
        };

        var text = MainWindowViewModel.FormatApplyError(result, ["Classic context menu"]);

        Assert.Contains("\"Classic context menu\" did not finish last time", text, StringComparison.Ordinal);
        Assert.Contains("nothing was applied", text, StringComparison.Ordinal);
        Assert.Contains("Click Discard All", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ReviewPanel_DescribesAnUnresolvedGroupInPlainWords()
    {
        var failed = Change("b", "Second setting");
        var stuck = Change("a", "First setting");
        var record = new GroupReconciliation
        {
            Group = Group("g", stuck, failed),
            Kind = MutationFailureKind.ChangeFailed,
            Failed = failed,
            Uncertain = [stuck, failed],
            RolledBack = [],
            RollbackFailures = [new RollbackFailure(stuck, "revert refused", null)],
            ErrorMessage = "Registry access denied",
            RecordedAt = DateTimeOffset.UtcNow,
        };

        var note = ReviewPanelViewModel.DescribeUnresolved(record);

        Assert.Equal(
            "Did not finish at \"Second setting\": Registry access denied. \"First setting\" could not be put back. Current values of \"First setting\" and \"Second setting\" are unknown.",
            note);
    }
}
