using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ThisIsMyPC.App.ViewModels;

namespace ThisIsMyPC.App.Views;

public partial class SetupGuideView : UserControl
{
    private SetupGuideViewModel? _model;
    private bool _attached;

    public SetupGuideView()
    {
        InitializeComponent();
        SizeChanged += (_, _) => ShowCurrentLesson(resetDetails: false);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _attached = true;
        ObserveModel();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _attached = false;
        ObserveModel();
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        ObserveModel();
    }

    private void ObserveModel()
    {
        if (_model is not null) _model.PropertyChanged -= OnLessonChanged;
        _model = _attached ? DataContext as SetupGuideViewModel : null;
        if (_model is null) return;
        _model.PropertyChanged += OnLessonChanged;
        ShowCurrentLesson();
    }

    private void OnLessonChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SetupGuideViewModel.Current)) ShowCurrentLesson();
    }

    private void ShowCurrentLesson(bool resetDetails = true)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_model is null) return;
            var card = this.GetVisualDescendants().OfType<Border>().FirstOrDefault(b =>
                b.Classes.Contains("set-card-body") && ReferenceEquals(b.DataContext, _model.Current));
            card?.BringIntoView();
            if (resetDetails) DetailScroll.Offset = default;
        }, DispatcherPriority.Loaded);
    }
}
