using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using ThisIsMyPC.App.ViewModels;
using ThisIsMyPC.App.Views;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Core.Sets;
using ThisIsMyPC.Modules.Power.Actions;
using ThisIsMyPC.Modules.Power.Models;

namespace ThisIsMyPC.App.UiTests;

/// <summary>
/// The Review Pending Changes panel with staged one-way actions: long lines
/// wrap inside the panel instead of running off its edge, and the list keeps
/// a lane on the right so the scrollbar never covers a Remove button. CI-safe.
/// </summary>
public class ReviewPanelShotTests
{
    private static PowerPlan Plan(string guid, string name) => new()
    {
        PlanGuid = new Guid(guid),
        Name = name,
        IsActive = false,
    };

    [AvaloniaFact]
    public void StagedActions_WrapTheirTextAndLeaveTheScrollbarLane()
    {
        var changes = new PendingChangesService();
        var actions = new PendingActionsService();
        actions.Stage(PowerActionFactory.CreateDeletePlan(Plan("381b4222-f694-41f0-9685-ff5bb260df2e", "Balanced")));
        actions.Stage(PowerActionFactory.CreateDeletePlan(Plan("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c", "High performance")));
        actions.Stage(PowerActionFactory.CreateDeletePlan(Plan("a0a85b5a-f87c-421c-916f-84c7a0062279", "Ultimate Performance")));
        actions.Stage(PowerActionFactory.CreateDeletePlan(Plan("a1841308-3541-4fab-bc81-f71556f20b4a", "Power saver")));
        actions.Stage(PowerActionFactory.CreateDeletePlan(Plan("c2b0925a-6cf8-4cd8-9ac7-fff967b7f4e3", "Ultimate Performance")));
        var writer = new CustomSetWriter(Path.Combine(Path.GetTempPath(), $"tipc-review-{Guid.NewGuid():N}"));
        var viewModel = new ReviewPanelViewModel(changes, writer, actions);

        using var session = UiSession.ForView(new ReviewPanelView(), viewModel, "review-panel", width: 520, height: 560);
        session.Screenshot("actions");

        Assert.True(session.IsTextVisible("Delete power plan Balanced"));
        Assert.True(session.IsTextVisible("5 action(s)"));

        var panel = session.Find<Border>(b => b.Width == 450);
        var panelRight = panel.TranslatePoint(new Avalonia.Point(panel.Bounds.Width, 0), session.Window)!.Value.X;
        var removeButtons = session.FindAll<Button>(b => b.Content as string == "Remove").ToList();
        Assert.Equal(5, removeButtons.Count);
        foreach (var button in removeButtons)
        {
            var right = button.TranslatePoint(new Avalonia.Point(button.Bounds.Width, 0), session.Window)!.Value.X;
            Assert.True(panelRight - right >= 16, $"Remove button ends {panelRight - right:F1}px from the panel edge; the scrollbar lane needs 16");
        }

        // Every text block fits inside the panel: nothing is cut off at the edge.
        foreach (var text in session.FindAll<TextBlock>(t => !string.IsNullOrEmpty(t.Text)))
        {
            var right = text.TranslatePoint(new Avalonia.Point(text.Bounds.Width, 0), session.Window)!.Value.X;
            Assert.True(right <= panelRight - 8, $"'{text.Text}' runs to {right:F0}, past the panel edge at {panelRight:F0}");
        }
    }
}
