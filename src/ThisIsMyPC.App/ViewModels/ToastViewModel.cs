using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ThisIsMyPC.App.ViewModels;

public enum ToastSeverity
{
    Info,
    Success,
    Warning,
}

/// <summary>One transient notification card in the toast stack.</summary>
public sealed partial class ToastViewModel : ViewModelBase
{
    private readonly Action<ToastViewModel> _dismiss;

    public string Title { get; }
    public string Message { get; }
    public ToastSeverity Severity { get; }

    /// <summary>Stays until closed by hand or replaced by its key; never auto-dismissed, never evicted.</summary>
    public bool IsSticky { get; }

    /// <summary>Names a toast a later call may replace or close (the restart notice).</summary>
    public string? Key { get; }

    public bool IsInfo => Severity == ToastSeverity.Info;
    public bool IsSuccess => Severity == ToastSeverity.Success;
    public bool IsWarning => Severity == ToastSeverity.Warning;

    public ToastViewModel(string title, string message, ToastSeverity severity, Action<ToastViewModel> dismiss,
        bool sticky = false, string? key = null)
    {
        ArgumentNullException.ThrowIfNull(dismiss);
        Title = title;
        Message = message;
        Severity = severity;
        IsSticky = sticky;
        Key = key;
        _dismiss = dismiss;
    }

    [RelayCommand]
    private void Dismiss() => _dismiss(this);
}

/// <summary>
/// The in-app toast surface (UI/UX chapter): transient notification cards stacked
/// top-right over the content area. UI-thread only; callers marshal. Nothing here
/// takes layout space, so a notice never moves the page under it.
/// </summary>
public sealed class ToastStackViewModel
{
    private static readonly TimeSpan DefaultLifetime = TimeSpan.FromSeconds(6);
    private const int MaxVisible = 4;

    private readonly TimeSpan _lifetime;

    public ObservableCollection<ToastViewModel> Toasts { get; } = [];

    /// <param name="lifetime">
    /// Auto-dismiss delay; null means the 6 s default. TimeSpan.Zero disables
    /// auto-dismiss (tests and screenshot suites need deterministic frames).
    /// </param>
    public ToastStackViewModel(TimeSpan? lifetime = null)
    {
        _lifetime = lifetime ?? DefaultLifetime;
    }

    /// <param name="sticky">Keep the card until it is closed or replaced: for a notice that must not be missed, such as a reboot.</param>
    /// <param name="key">Replaces any card shown earlier under the same key, so one notice never stacks up.</param>
    public void Show(string title, string message, ToastSeverity severity, bool sticky = false, string? key = null)
    {
        if (key is not null)
            Dismiss(key);

        // Newest transient card wins the limited space; the oldest transient card
        // yields. Sticky cards are not counted, never yield, and push nothing out.
        while (!sticky && Toasts.Count(t => !t.IsSticky) >= MaxVisible)
            Toasts.Remove(Toasts.First(t => !t.IsSticky));

        var toast = new ToastViewModel(title, message, severity, t => Toasts.Remove(t), sticky, key);
        Toasts.Add(toast);

        if (!sticky && _lifetime > TimeSpan.Zero)
            DispatcherTimer.RunOnce(() => Toasts.Remove(toast), _lifetime);
    }

    /// <summary>Closes the card shown under <paramref name="key"/>, if any.</summary>
    public void Dismiss(string key)
    {
        for (var i = Toasts.Count - 1; i >= 0; i--)
        {
            if (Toasts[i].Key == key)
                Toasts.RemoveAt(i);
        }
    }
}
