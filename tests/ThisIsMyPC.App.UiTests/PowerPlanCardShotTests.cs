using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using ThisIsMyPC.App.ViewModels;
using ThisIsMyPC.App.Views;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Modules.Power.Models;

namespace ThisIsMyPC.App.UiTests;

/// <summary>
/// Power plan cards with a fake actions queue: pressing Delete stages the
/// one-way action, and the card shows it (red tint, a red "Queued" button,
/// the other buttons greyed) until it is pressed again. New plan opens an
/// inline form; Create stages a reversible change and lists the plan as
/// pending until Apply. CI-safe: the fake power service backs both.
/// </summary>
public class PowerPlanCardShotTests
{
    private static PowerScanData ScanData() => new(
    [
        new PowerPlan
        {
            PlanGuid = new Guid("381b4222-f694-41f0-9685-ff5bb260df2e"),
            Name = "Balanced",
            Description = "Automatically balances performance with energy consumption on capable hardware.",
            IsActive = true,
        },
        new PowerPlan
        {
            PlanGuid = new Guid("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c"),
            Name = "High performance",
            Description = "Favors performance, but may use more energy.",
            IsActive = false,
        },
    ], HibernateEnabled: true);

    [AvaloniaFact]
    public void Delete_StagesTheAction_AndTheCardShowsItQueued()
    {
        var changes = new PendingChangesService();
        var actions = new PendingActionsService();
        var viewModel = new PowerViewModel(ScanData(), changes, pendingActionsService: actions);
        using var session = UiSession.ForView(new PowerView(), viewModel, "power-plan-card", height: 700);

        session.Screenshot("plans");
        var plan = viewModel.Plans.First(p => p.Name == "High performance");
        var deleteButton = session.Find<Button>(b => ReferenceEquals(b.DataContext, plan) && b.Content as string == "Delete");
        var setActive = session.Find<Button>(b => ReferenceEquals(b.DataContext, plan) && b.Content as string == "Set active");

        session.Click(deleteButton);
        session.Screenshot("plan-delete-queued");

        Assert.Equal(1, actions.PendingCount);
        Assert.True(plan.IsDeleteQueued);
        Assert.Equal("Queued", deleteButton.Content);
        Assert.Contains("queued", deleteButton.Classes);
        Assert.False(setActive.IsEnabled);
        var card = session.Find<Border>(b => ReferenceEquals(b.DataContext, plan) && b.Classes.Contains("card"));
        Assert.Contains("pending-disable", card.Classes);

        session.Click(deleteButton);
        Assert.Equal(0, actions.PendingCount);
        Assert.Equal("Delete", deleteButton.Content);
        Assert.DoesNotContain("pending-disable", card.Classes);
    }

    [AvaloniaFact]
    public void NewPlan_StagesACopyAndListsItAsPending()
    {
        var changes = new PendingChangesService();
        var power = new UiFakePowerService();
        var viewModel = new PowerViewModel(ScanData(), changes, powerService: power);
        using var session = UiSession.ForView(new PowerView(), viewModel, "power-plan-card", height: 800);

        session.ClickText("New plan");
        session.Screenshot("new-plan-form");
        Assert.True(viewModel.IsCreatingPlan);
        Assert.Equal("Balanced", viewModel.NewPlanSource?.Name);

        var nameBox = session.Find<TextBox>(t => t.Watermark == "Plan name");
        session.Type(nameBox, "Balanced");
        Assert.True(session.IsTextVisible("A plan with this name already exists."));
        Assert.False(viewModel.CanConfirmCreatePlan);

        nameBox.Text = string.Empty;
        session.Type(nameBox, "Gaming");
        Assert.True(viewModel.CanConfirmCreatePlan);
        session.ClickText("Create");
        session.Screenshot("new-plan-pending");

        Assert.False(viewModel.IsCreatingPlan);
        var group = Assert.Single(changes.PendingGroups);
        var change = Assert.Single(group.Changes);
        Assert.Equal("Create power plan Gaming", change.DisplayName);
        Assert.Equal("Copy of Balanced", change.AfterDisplay);
        Assert.True(session.IsTextVisible("Gaming"));
        Assert.True(session.IsTextVisible("Pending"));

        session.ClickText("Remove");
        Assert.Empty(changes.PendingGroups);
        Assert.Empty(viewModel.PendingCreations);
    }
}

/// <summary>Enough of IPowerService for the plan list: plans only, no settings.</summary>
file sealed class UiFakePowerService : IPowerService
{
    public OperationResult<IReadOnlyList<PowerPlanInfo>> EnumeratePlans() =>
        OperationResult<IReadOnlyList<PowerPlanInfo>>.Success([]);
    public OperationResult<bool> SetActivePlan(Guid planGuid) => OperationResult<bool>.Success(true);
    public OperationResult<IReadOnlyList<PowerSettingInfo>> EnumeratePlanSettings(Guid planGuid) =>
        OperationResult<IReadOnlyList<PowerSettingInfo>>.Success([]);
    public OperationResult<bool> WriteSettingIndex(Guid planGuid, Guid subgroupGuid, Guid settingGuid, bool ac, uint valueIndex) =>
        OperationResult<bool>.Success(true);
    public bool SupportsModernStandby() => false;
    public OperationResult<bool> SetHibernateEnabled(bool enable) => OperationResult<bool>.Success(true);
    public OperationResult<Guid> DuplicateScheme(Guid sourceSchemeGuid) => OperationResult<Guid>.Success(Guid.NewGuid());
    public OperationResult<bool> DeleteScheme(Guid schemeGuid) => OperationResult<bool>.Success(true);
    public OperationResult<bool> WriteSchemeText(Guid schemeGuid, string name, string description) => OperationResult<bool>.Success(true);
}
