using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using ThisIsMyPC.App.ViewModels;
using ThisIsMyPC.App.Views;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Modules.Power.Models;

namespace ThisIsMyPC.App.UiTests;

/// <summary>
/// Power plan cards with a fake actions queue: pressing Delete stages the
/// one-way action, and the card shows it (red tint, a red "Queued" button,
/// the other buttons greyed) until it is pressed again. CI-safe.
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
}
