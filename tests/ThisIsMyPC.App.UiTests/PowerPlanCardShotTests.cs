using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Avalonia.Headless.XUnit;
using ThisIsMyPC.App.UiTests.Fakes;
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

        viewModel.BeginCreatePlanCommand.Execute(null);
        session.Pump();
        session.Screenshot("new-plan-form");
        Assert.True(viewModel.IsCreatingPlan);
        Assert.Equal("Balanced", viewModel.NewPlanSource?.Name);

        // The "Copy of" list opens on the app palette, the chosen row tinted.
        var source = session.Find<ComboBox>(_ => true);
        session.Click(source);
        Assert.True(source.IsDropDownOpen);
        session.Screenshot("new-plan-source-open");
        session.SetTheme(ThemeVariant.Light);
        session.Screenshot("new-plan-source-open-light");
        session.SetTheme(ThemeVariant.Dark);
        source.IsDropDownOpen = false;
        session.Pump();

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

    [AvaloniaFact]
    public void AddPlanDropdown_ListsMissingWindowsPlans_AndStagesOne()
    {
        // Balanced and High performance exist; Power saver and Ultimate Performance do not.
        var changes = new PendingChangesService();
        var viewModel = new PowerViewModel(ScanData(), changes, powerService: new UiFakePowerService());
        using var session = UiSession.ForView(new PowerView(), viewModel, "power-plan-card", height: 800);

        Assert.Equal(["Power saver", "Ultimate Performance"], viewModel.AddPlanOptions.Select(o => o.Name));

        var addPlan = session.Find<Button>(b => b.Name == "AddPlanButton");
        session.Click(addPlan);
        Assert.True(addPlan.Flyout!.IsOpen);
        session.Screenshot("add-plan-open");

        var flyoutContent = (Control)((Flyout)addPlan.Flyout).Content!;
        var powerSaverItem = flyoutContent.GetVisualDescendants().OfType<Button>()
            .First(b => b.Content as string == "Power saver");
        Assert.True(UiSession.IsTextVisibleIn(flyoutContent, "New plan (copy of an existing plan)"));
        session.Hover(powerSaverItem);
        session.Screenshot("add-plan-hover");
        session.SetTheme(ThemeVariant.Light);
        session.Screenshot("add-plan-light");
        session.SetTheme(ThemeVariant.Dark);
        // A real click, not a raised event: the row's command must run before the
        // dropdown closes (closing detaches the content and its bindings).
        session.Click(powerSaverItem);
        Assert.False(addPlan.Flyout.IsOpen);
        session.Screenshot("power-saver-pending");

        var group = Assert.Single(changes.PendingGroups);
        var change = Assert.Single(group.Changes);
        Assert.Equal("add-stock-plan:a1841308-3541-4fab-bc81-f71556f20b4a", change.SettingId);
        Assert.True(session.IsTextVisible("Power saver"));
        Assert.True(session.IsTextVisible("Pending"));
        Assert.Equal(["Ultimate Performance"], viewModel.AddPlanOptions.Select(o => o.Name));

        session.ClickText("Remove");
        Assert.Empty(changes.PendingGroups);
        Assert.Equal(["Power saver", "Ultimate Performance"], viewModel.AddPlanOptions.Select(o => o.Name));
    }

    [AvaloniaFact]
    public void AddPlanDropdown_UltimatePerformance_StagesTheInstall()
    {
        var changes = new PendingChangesService();
        var viewModel = new PowerViewModel(ScanData(), changes, powerService: new UiFakePowerService());
        using var session = UiSession.ForView(new PowerView(), viewModel, "power-plan-card", height: 800);

        viewModel.AddPlanOptions.First(o => o.Name == "Ultimate Performance").AddCommand.Execute(null);
        session.Pump();
        session.Screenshot("ultimate-performance-pending");

        var change = Assert.Single(Assert.Single(changes.PendingGroups).Changes);
        Assert.Equal(Modules.Power.Changes.PowerPlanChangeFactory.UltimatePerformanceSettingId, change.SettingId);
        Assert.Equal("1", change.AfterValue);
        Assert.DoesNotContain(viewModel.AddPlanOptions, o => o.Name == "Ultimate Performance");
        Assert.True(session.IsTextVisible("Ultimate Performance"));
    }

    [AvaloniaFact]
    public void AddPlanDropdown_HidesPlansThatExist()
    {
        var withAll = ScanData() with
        {
            Plans =
            [
                .. ScanData().Plans,
                new PowerPlan { PlanGuid = new Guid("a1841308-3541-4fab-bc81-f71556f20b4a"), Name = "Power saver", IsActive = false },
                new PowerPlan { PlanGuid = new Guid("c2b0925a-6cf8-4cd8-9ac7-fff967b7f4e3"), Name = "Ultimate Performance", IsActive = false },
            ],
        };
        var viewModel = new PowerViewModel(withAll, new PendingChangesService(), powerService: new UiFakePowerService());
        using var session = UiSession.ForView(new PowerView(), viewModel, "power-plan-card", height: 800);

        Assert.Empty(viewModel.AddPlanOptions);
        Assert.False(viewModel.HasAddPlanOptions);
        Assert.NotNull(session.TryFind<Button>(b => b.Name == "AddPlanButton"));
    }
}

/// <summary>Enough of IPowerService for the plan list: plans only, no settings.</summary>
