using ThisIsMyPC.App.ViewModels;

namespace ThisIsMyPC.Integration.Tests.ViewModels;

/// <summary>
/// In-app toast stack (UI/UX chapter). Lifetime Zero disables the auto-dismiss
/// timer so assertions see deterministic state.
/// </summary>
public sealed class ToastStackViewModelTests
{
    private static ToastStackViewModel CreateStack() => new(lifetime: TimeSpan.Zero);

    [Fact]
    public void Show_AddsAToastWithContentAndSeverity()
    {
        var stack = CreateStack();

        stack.Show("New startup entry", "Something registered itself to run at boot.", ToastSeverity.Warning);

        var toast = Assert.Single(stack.Toasts);
        Assert.Equal("New startup entry", toast.Title);
        Assert.Equal("Something registered itself to run at boot.", toast.Message);
        Assert.True(toast.IsWarning);
        Assert.False(toast.IsInfo);
    }

    [Fact]
    public void Dismiss_RemovesThatToastOnly()
    {
        var stack = CreateStack();
        stack.Show("First", "m1", ToastSeverity.Info);
        stack.Show("Second", "m2", ToastSeverity.Success);

        stack.Toasts[0].DismissCommand.Execute(null);

        var remaining = Assert.Single(stack.Toasts);
        Assert.Equal("Second", remaining.Title);
    }

    [Fact]
    public void Show_BeyondTheCap_DropsTheOldest()
    {
        var stack = CreateStack();
        for (var i = 1; i <= 6; i++)
            stack.Show($"Toast {i}", "m", ToastSeverity.Info);

        Assert.Equal(4, stack.Toasts.Count);
        Assert.Equal("Toast 3", stack.Toasts[0].Title);
        Assert.Equal("Toast 6", stack.Toasts[^1].Title);
    }
}
