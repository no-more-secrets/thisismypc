using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using ThisIsMyPC.App.ViewModels;
using ThisIsMyPC.App.Views;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Modules.Shell.Models;

namespace ThisIsMyPC.App.UiTests;

/// <summary>
/// The Environment page with fake variables: the PATH editor's rows (one
/// outline per row, no inner borders), the User tab's header row and search
/// box sharing the cards' edges, and "Add Variable" putting the new row at
/// the top where the person is looking. CI-safe: nothing reads the system.
/// </summary>
public class EnvironmentViewShotTests
{
    private static EnvironmentScanData ScanData() => new(
        UserVariables:
        [
            new EnvironmentVariable("Path", @"C:\Users\me\AppData\Local\Programs\Python;C:\Users\me\.dotnet\tools;C:\Users\me\AppData\Roaming\npm", EnvironmentVariableScope.User),
            new EnvironmentVariable("GOPATH", @"%USERPROFILE%\go", EnvironmentVariableScope.User),
            new EnvironmentVariable("NVM_HOME", @"C:\Users\me\AppData\Local\nvm", EnvironmentVariableScope.User),
            new EnvironmentVariable("TEMP", @"C:\Users\me\AppData\Local\Temp", EnvironmentVariableScope.User),
        ],
        SystemVariables:
        [
            new EnvironmentVariable("Path", @"C:\Windows\system32;C:\Windows;C:\Program Files\dotnet\", EnvironmentVariableScope.System),
            new EnvironmentVariable("ComSpec", @"C:\Windows\system32\cmd.exe", EnvironmentVariableScope.System),
            new EnvironmentVariable("windir", @"C:\Windows", EnvironmentVariableScope.System),
        ]);

    private static (EnvironmentViewModel ViewModel, PendingChangesService Queue) Build()
    {
        var queue = new PendingChangesService();
        return (new EnvironmentViewModel(ScanData(), queue), queue);
    }

    [AvaloniaFact]
    public void PathTab_RowsCarryOneOutlineEach()
    {
        var (viewModel, _) = Build();
        using var session = UiSession.ForView(new EnvironmentView(), viewModel, "environment-view");

        session.Screenshot("path");
        Assert.True(session.IsTextVisible("User PATH"));
        Assert.NotNull(session.TryFind<TextBox>(t => t.Text == @"C:\Users\me\.dotnet\tools"));
        Assert.True(session.IsTextVisible("System PATH"));

        // The path text boxes draw no border of their own; the row card is the outline.
        var boxes = session.FindAll<TextBox>(t => t.Classes.Contains("inline")).ToList();
        Assert.NotEmpty(boxes);
        Assert.All(boxes, b => Assert.Equal(new Avalonia.Thickness(0), b.BorderThickness));
    }

    [AvaloniaFact]
    public void PathTab_InsertLine_SitsCenteredInTheSlotWithRoundedEnds()
    {
        var (viewModel, _) = Build();
        using var session = UiSession.ForView(new EnvironmentView(), viewModel, "environment-view");

        var editor = session.Find<PathEditorView>(v => ReferenceEquals(v.DataContext, viewModel.UserPathEditor));
        var line = editor.PreviewInsertLine(1);
        session.Pump();
        session.Screenshot("path-insert-line");

        Assert.NotNull(line);
        Assert.True(line.CornerRadius.TopLeft > 0, "line ends are rounded");

        var rows = session.FindAll<Border>(b => b.Classes.Contains("card") && b.DataContext is PathEntryViewModel)
            .OrderBy(session.TopOf).Take(2).ToList();
        var slotTop = session.TopOf(rows[0]) + rows[0].Bounds.Height;
        var slotBottom = session.TopOf(rows[1]);
        var lineCenter = session.TopOf(line) + line.Bounds.Height / 2;
        var slotCenter = (slotTop + slotBottom) / 2;
        Assert.True(Math.Abs(lineCenter - slotCenter) < 0.51,
            $"line center {lineCenter} vs slot center {slotCenter} (slot {slotTop}..{slotBottom})");
    }

    [AvaloniaFact]
    public void UserTab_AddVariable_PutsTheNewRowFirst()
    {
        var (viewModel, _) = Build();
        using var session = UiSession.ForView(new EnvironmentView(), viewModel, "environment-view");

        session.ClickText("User (3)");
        session.Screenshot("user");
        Assert.True(session.IsTextVisible("Per-user variables (excluding PATH)"));
        Assert.True(session.IsTextVisible("GOPATH"));

        session.ClickText("Add Variable");
        session.Screenshot("user-add-variable");
        Assert.True(viewModel.UserVariables[0].IsNew);
        Assert.True(viewModel.UserVariables[0].IsEditing);
        Assert.True(session.IsTextVisible("User (4)"));

        // The new row's name box sits above the first existing card.
        var nameBox = session.Find<TextBox>(t => t.Watermark == "Variable name" && t.IsVisible);
        var firstCard = session.Find<TextBlock>(t => t.Text == "GOPATH");
        Assert.True(session.TopOf(nameBox) < session.TopOf(firstCard), "new row renders above the existing rows");
    }
}
