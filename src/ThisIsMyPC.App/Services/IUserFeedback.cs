namespace ThisIsMyPC.App.Services;

/// <summary>
/// Where the outcome of a click goes instead of into the card it came from,
/// so no card ever changes height after it is laid out. A finished action's
/// result is a toast; a failure is the window status line (and the log). A
/// card that keeps its failure marks it with an icon whose tooltip carries the
/// same text, so nothing is lost when the toast is gone or the line is reused.
/// </summary>
public interface IUserFeedback
{
    /// <summary>A finished action's outcome: a toast titled after the thing acted on.</summary>
    void Report(string title, string message);

    /// <summary>A failure: the window status line, in the error colour.</summary>
    void Fail(string message);
}

/// <summary>
/// The app-wide sink. View models call it from any thread; MainWindowViewModel
/// subscribes and marshals to the UI thread. Nothing is queued: a report raised
/// before the window subscribes is only logged.
/// </summary>
public sealed class UserFeedbackHub : IUserFeedback
{
    private static readonly NLog.Logger Log = NLog.LogManager.GetLogger("ThisIsMyPC.App.UserFeedback");

    public event Action<string, string>? Reported;
    public event Action<string>? Failed;

    public void Report(string title, string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        Log.Info("Result: {Title}: {Message}", title, message);
        Reported?.Invoke(title, message);
    }

    public void Fail(string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        Log.Error("Failure: {Message}", message);
        Failed?.Invoke(message);
    }
}
