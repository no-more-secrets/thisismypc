using CommunityToolkit.Mvvm.ComponentModel;

namespace ThisIsMyPC.App.ViewModels;

public partial class PathEntryViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _path;

    [ObservableProperty]
    private int _index;

    [ObservableProperty]
    private bool _isValid;

    public PathEntryViewModel(string path, int index)
    {
        _path = path;
        _index = index;
        _isValid = System.IO.Directory.Exists(path);
    }

    partial void OnPathChanged(string value)
    {
        IsValid = System.IO.Directory.Exists(value);
    }
}
