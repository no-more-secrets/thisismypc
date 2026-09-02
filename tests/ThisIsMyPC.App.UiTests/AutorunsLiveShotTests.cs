using Avalonia.Headless.XUnit;
using ThisIsMyPC.App.ViewModels;

namespace ThisIsMyPC.App.UiTests;

/// <summary>
/// The Autoruns page on the real service graph: live registry, real shell
/// icons, real Authenticode checks. Proves the icon and signer plumbing end
/// to end and screenshots the Logon and Scheduled Tasks tabs into
/// artifacts/ui-shots/autoruns-live/. Read-only; nothing is applied.
/// </summary>
[Trait("Category", "Diagnostic")]
public class AutorunsLiveShotTests
{
    [AvaloniaFact(Timeout = 300_000)]
    public async Task LogonAndTasks_RenderRealIconsAndSigners()
    {
        using var session = UiSession.ForMainWindow("autoruns-live");
        var main = (MainWindowViewModel)session.Window.DataContext!;
        await session.WaitForAsync(() => main.SidebarGroups.Count > 0, timeoutMs: 30_000, what: "sidebar population");

        session.ClickText("Startup & Services");
        await session.WaitForAsync(
            () => main.CurrentContent is StartupViewModel, timeoutMs: 120_000, what: "Startup & Services content load");
        var startup = (StartupViewModel)main.CurrentContent!;
        await session.WaitForAsync(() => !startup.IsCheckingSignatures, timeoutMs: 120_000, what: "icons and signers");
        session.Screenshot("logon");

        var rows = startup.Tabs.SelectMany(t => t.Items).OfType<AutorunItemViewModel>().ToList();
        Assert.NotEmpty(rows);
        var withIcon = rows.Count(r => r.Icon is not null);
        var verified = rows.Count(r => r.PublisherText.StartsWith("(Verified)", StringComparison.Ordinal));
        Assert.True(withIcon > rows.Count / 2, $"{withIcon} of {rows.Count} rows have an icon");
        Assert.True(verified > rows.Count / 2, $"{verified} of {rows.Count} rows verified");

        var tasks = startup.Tabs.First(t => t.Name == "Scheduled Tasks");
        session.Click(session.Find<Avalonia.Controls.TabItem>(t => ReferenceEquals(t.DataContext, tasks)));
        session.Screenshot("scheduled-tasks");
        var taskRows = rows.Where(r => r.Entry.Category == Modules.Startup.Models.AutorunCategory.ScheduledTasks).ToList();
        Assert.True(taskRows.Count(r => r.Entry.ImagePath is not null) > taskRows.Count / 2, "most tasks resolve to a program");

        // What a person sees with the default filter, for reading after the run.
        var dump = tasks.Items.OfType<AutorunItemViewModel>()
            .Select(r => $"{r.Name}\t{r.PublisherText}\t{r.Entry.ImagePath ?? "(no file)"}\t{r.Entry.Data}");
        File.WriteAllLines(Path.Combine(session.ShotDirectory, "scheduled-tasks-visible.txt"), dump);
    }
}
