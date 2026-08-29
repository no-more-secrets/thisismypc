using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using ThisIsMyPC.Ipc.Contracts;

namespace ThisIsMyPC.App.ViewModels;

/// <summary>
/// Home "Drift" section (28-3): settings the Owner Mode service found reverted since
/// the app last applied them. Reapply stages through the normal pending pipeline —
/// nothing touches the system until the user applies from the review panel.
/// </summary>
public sealed partial class DriftSectionViewModel : ViewModelBase
{
    private readonly Action<DriftRowViewModel> _reapply;
    private readonly Action _dismissed;

    public ObservableCollection<DriftRowViewModel> Rows { get; }

    public DriftSectionViewModel(
        IReadOnlyList<DriftItem> items,
        Action<DriftRowViewModel> reapply,
        Action dismissed)
    {
        _reapply = reapply;
        _dismissed = dismissed;
        Rows = [.. items.Select(i => new DriftRowViewModel(i, this))];
    }

    public string Header => $"Windows reverted {Rows.Count} of your settings";

    internal void Reapply(DriftRowViewModel row)
    {
        _reapply(row);
        Rows.Remove(row);
        if (Rows.Count == 0)
            _dismissed();
    }

    [RelayCommand]
    private void ReapplyAll()
    {
        foreach (var row in Rows.ToList())
        {
            _reapply(row);
            Rows.Remove(row);
        }
        _dismissed();
    }

    [RelayCommand]
    private void Dismiss() => _dismissed();
}

public sealed partial class DriftRowViewModel
{
    private readonly DriftSectionViewModel _section;

    public DriftRowViewModel(DriftItem item, DriftSectionViewModel section)
    {
        Item = item;
        _section = section;
    }

    public DriftItem Item { get; }

    public string DisplayName => Item.DisplayName;

    public string Detail
    {
        get
        {
            var change = $"was \"{Display(Item.ExpectedValue)}\", now \"{Display(Item.CurrentValue)}\"";
            return Item.SuspectedCause is { Length: > 0 } cause
                ? $"{change} - suspected cause: {cause}"
                : change;
        }
    }

    private static string Display(string value) =>
        value.Length == 0 || value == "__absent__" ? "(not set)" : value;

    [RelayCommand]
    private void Reapply() => _section.Reapply(this);
}
