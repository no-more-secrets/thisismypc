using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.VisualTree;
using ThisIsMyPC.App.ViewModels;
using ThisIsMyPC.App.Views;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Core.Sets;

namespace ThisIsMyPC.App.UiTests;

/// <summary>
/// The Presets page with fake sets: cards wear the shared clickable states
/// (hover wash, active tint with the accent ring when chosen), and the list
/// keeps the standard 16px edge beside the overlay scrollbar so the thumb
/// never sits on a card. CI-safe: no inspectors, every entry previews as
/// skipped.
/// </summary>
public class SetLoaderViewShotTests
{
    private const double ContentEdge = 16;

    private static SetDefinition Definition(string name, SetCategory category, int entries, string description) => new()
    {
        Name = name,
        Description = description,
        Category = category,
        Version = "1.0.0",
        Author = "ThisIsMyPC",
        Source = SetSource.BuiltIn,
        FilePath = $@"C:\sets\{name}.json",
        Entries = Enumerable.Range(1, entries).Select(i => new SetEntry
        {
            ModuleId = "Windows Annoyances",
            SettingId = $"setting-{i}",
            Value = "0",
            Description = $"Change {i} of {name}",
        }).ToList(),
    };

    private static SetLoaderViewModel Build() => new(
        new SetLoadResult
        {
            Sets =
            [
                Definition("Clean Boot", SetCategory.TweakSet, 32,
                    "Disables the telemetry, diagnostics, and unused-feature services and scheduled tasks that are safe to turn off on any Windows 11 machine."),
                Definition("NukeCopilot", SetCategory.TweakSet, 3,
                    "Turns off Windows Copilot everywhere it surfaces: the assistant itself, the taskbar button, and Edge's Copilot sidebar."),
                Definition("Privacy Baseline", SetCategory.TweakSet, 15,
                    "Limits diagnostic data, disables error reporting, the advertising ID, activity history, Windows Recall, and the ad-like suggestion surfaces."),
                Definition("Quiet Desktop", SetCategory.TweakSet, 9,
                    "Removes widgets, news, and the search highlights from the taskbar and lock screen."),
                Definition("Everything", SetCategory.OptimizationPack, 40,
                    "Every tweak set above in one bundle, grouped by the set each change came from."),
            ],
            Warnings = [],
        },
        [],
        _ => null,
        new PendingChangesService());

    private static Border Card(UiSession session, string name)
        => session.Find<Border>(b => b.Classes.Contains("set-card-body") && b.DataContext is SetItemViewModel { Name: var n } && n == name);

    /// <summary>The control's right edge in window pixels.</summary>
    private static double RightOf(UiSession session, Control control)
        => (control.TranslatePoint(new Avalonia.Point(control.Bounds.Width, 0), session.Window)
            ?? throw new InvalidOperationException("Control is not connected to the window's visual tree.")).X;

    [AvaloniaFact]
    public void Cards_HoverAndSelectLikeEveryOtherControl()
    {
        var viewModel = Build();
        using var session = UiSession.ForView(new SetLoaderView(), viewModel, "set-loader");

        session.Screenshot("rest");
        Assert.True(session.IsTextVisible("TWEAK SETS"));
        Assert.True(session.IsTextVisible("OPTIMIZATION PACKS"));

        session.Hover(Card(session, "NukeCopilot"));
        session.Screenshot("nukecopilot-hovered");
        Assert.Null(viewModel.SelectedSet);

        session.Click(Card(session, "Clean Boot"));
        session.Screenshot("clean-boot-selected");
        var cleanBoot = viewModel.TweakSets.Single(s => s.Name == "Clean Boot");
        Assert.Same(cleanBoot, viewModel.SelectedSet);
        Assert.True(cleanBoot.IsSelected);
        Assert.True(Card(session, "Clean Boot").Classes.Contains("selected"));
        Assert.False(Card(session, "NukeCopilot").Classes.Contains("selected"));
        Assert.True(session.IsTextVisible("Change 1 of Clean Boot"));

        // Selecting another card moves the tint; the pointer resting on it does not remove it.
        session.Click(Card(session, "NukeCopilot"));
        session.Screenshot("nukecopilot-selected-hovered");
        Assert.False(cleanBoot.IsSelected);
        Assert.True(viewModel.TweakSets.Single(s => s.Name == "NukeCopilot").IsSelected);
        Assert.True(Card(session, "NukeCopilot").Classes.Contains("selected"));
    }

    [AvaloniaFact]
    public void List_KeepsTheContentEdgeBesideTheScrollbar()
    {
        using var session = UiSession.ForView(new SetLoaderView(), Build(), "set-loader");

        var card = Card(session, "Clean Boot");
        var scroller = card.FindAncestorOfType<ScrollViewer>()!;
        Assert.Equal(ContentEdge, RightOf(session, scroller) - RightOf(session, card), 0.5);
        // Enough cards to scroll, so the screenshot shows the thumb beside the cards.
        Assert.True(scroller.Extent.Height > scroller.Viewport.Height);
        session.Screenshot("list-edge");
    }

    [AvaloniaFact]
    public void Cards_RenderInLightTheme()
    {
        var viewModel = Build();
        using var session = UiSession.ForView(new SetLoaderView(), viewModel, "set-loader");
        try
        {
            session.SetTheme(ThemeVariant.Light);
            session.Click(Card(session, "Privacy Baseline"));
            session.Hover(Card(session, "Clean Boot"));
            session.Screenshot("light-selected-and-hovered");
            Assert.True(viewModel.TweakSets.Single(s => s.Name == "Privacy Baseline").IsSelected);
        }
        finally
        {
            session.SetTheme(ThemeVariant.Dark);
        }
    }
}
