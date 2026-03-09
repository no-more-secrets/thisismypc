using System.Collections.ObjectModel;

namespace ThisIsMyPC.App.ViewModels;

public partial class ChangeHistoryGroupViewModel : ViewModelBase
{
    public required string DateHeader { get; init; }
    public ObservableCollection<HistoryBatchViewModel> Batches { get; } = [];
}
