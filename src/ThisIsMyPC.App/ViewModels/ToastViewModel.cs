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

    public bool IsInfo => Severity == ToastSeverity.Info;
    public bool IsSuccess => Severity == ToastSeverity.Success;
    public bool IsWarning => Severity == ToastSeverity.Warning;

    public ToastViewModel(string title, string message, ToastSeverity severity, Action<ToastViewModel> dismiss)
    {
        ArgumentNullException.ThrowIfNull(dismiss);
        Title = title;
        Message = message;
        Severity = severity;
        _dismiss = dismiss;
    }

    [RelayCommand]
    private void Dismiss() => _dismiss(this);
}

/// <summary>
/// The in-app toast surface (UI/UX chapter): transient notification cards stacked
/// top-right over the content area. UI-thread only; callers marshal.
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

    public void Show(string title, string message, ToastSeverity severity)
    {
        // Newest wins the limited space; the oldest card yields.
        while (Toasts.Count >= MaxVisible)
            Toasts.RemoveAt(0);

        var toast = new ToastViewModel(title, message, severity, t => Toasts.Remove(t));
        Toasts.Add(toast);

        if (_lifetime > TimeSpan.Zero)
            DispatcherTimer.RunOnce(() => Toasts.Remove(toast), _lifetime);
    }
}
