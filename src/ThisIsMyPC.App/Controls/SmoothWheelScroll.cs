using System.Diagnostics;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace ThisIsMyPC.App.Controls;

/// <summary>Softens wheel movement while retaining the presenter's native input routing.</summary>
public sealed class SmoothWheelScroll : AvaloniaObject
{
    /// <summary>Enables wheel easing on a scroll presenter.</summary>
    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<SmoothWheelScroll, ScrollContentPresenter, bool>("IsEnabled");

    private static readonly ConditionalWeakTable<ScrollContentPresenter, Motion> Motions = new();

    static SmoothWheelScroll() => IsEnabledProperty.Changed.AddClassHandler<ScrollContentPresenter>((presenter, change) =>
    {
        if (change.GetNewValue<bool>())
            Motions.GetValue(presenter, p => new Motion(p));
        else if (Motions.TryGetValue(presenter, out var motion))
        {
            motion.Dispose();
            Motions.Remove(presenter);
        }
    });

    /// <summary>Gets whether wheel easing is enabled.</summary>
    public static bool GetIsEnabled(ScrollContentPresenter presenter) => presenter.GetValue(IsEnabledProperty);

    /// <summary>Sets whether wheel easing is enabled.</summary>
    public static void SetIsEnabled(ScrollContentPresenter presenter, bool value) => presenter.SetValue(IsEnabledProperty, value);

    private sealed class Motion : IDisposable
    {
        private readonly ScrollContentPresenter _presenter;
        private readonly WheelScrollMotion _motion = new();
        private Vector _beforeWheel;
        private Vector _lastWheel;
        private int _frameGeneration;
        private bool _inWheel;
        private bool _writing;
        private bool _framePending;
        private bool _disposed;

        public Motion(ScrollContentPresenter presenter)
        {
            _presenter = presenter;
            presenter.AddHandler(InputElement.PointerWheelChangedEvent, BeforeWheel, RoutingStrategies.Tunnel);
            presenter.AddHandler(InputElement.PointerWheelChangedEvent, AfterWheel, RoutingStrategies.Bubble, handledEventsToo: true);
            presenter.PropertyChanged += PropertyChanged;
            presenter.DetachedFromVisualTree += Detached;
        }

        private void BeforeWheel(object? sender, PointerWheelEventArgs e)
        {
            // Logical units and snap points cannot be interpolated as pixels.
            _inWheel = _presenter.Child is not ILogicalScrollable { IsLogicalScrollEnabled: true }
                && _presenter.HorizontalSnapPointsType == SnapPointsType.None
                && _presenter.VerticalSnapPointsType == SnapPointsType.None;
            _beforeWheel = _presenter.Offset;
        }

        private void AfterWheel(object? sender, PointerWheelEventArgs e)
        {
            if (!_inWheel) return;
            _inWheel = false;
            var delta = _presenter.Offset - _beforeWheel;
            if (delta == default)
            {
                // At an edge, native movement can be zero even when the user reverses direction.
                if (e.Delta.X * _lastWheel.X + e.Delta.Y * _lastWheel.Y < 0) _motion.Stop();
                return;
            }

            // Avalonia already chose the axis, handled nested scrolling, and respected child controls.
            WriteOffset(_beforeWheel);
            _motion.Push(_beforeWheel, delta, Maximum, Now);
            _lastWheel = e.Delta;
            RequestFrame();
        }

        private Vector Maximum => new(Math.Max(0, _presenter.Extent.Width - _presenter.Viewport.Width),
            Math.Max(0, _presenter.Extent.Height - _presenter.Viewport.Height));

        private static double Now => (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency;

        private void RequestFrame()
        {
            if (_framePending || !_motion.IsActive || _disposed) return;
            if (TopLevel.GetTopLevel(_presenter) is not { } topLevel) return;
            _framePending = true;
            var generation = _frameGeneration;
            topLevel.RequestAnimationFrame(_ =>
            {
                if (generation != _frameGeneration) return;
                _framePending = false;
                if (_disposed || !_motion.IsActive) return;
                WriteOffset(_motion.Advance(Now, Maximum));
                RequestFrame();
            });
        }

        private void WriteOffset(Vector offset)
        {
            _writing = true;
            try { _presenter.SetCurrentValue(ScrollContentPresenter.OffsetProperty, offset); }
            finally { _writing = false; }
        }

        private void PropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (!_writing && !_inWheel && (e.Property == ScrollContentPresenter.OffsetProperty
                || e.Property == ContentPresenter.ChildProperty || e.Property == Visual.IsVisibleProperty))
                _motion.Stop();
        }

        private void Detached(object? sender, VisualTreeAttachmentEventArgs e)
        {
            _motion.Stop();
            _frameGeneration++;
            _framePending = false;
            _inWheel = false;
        }

        public void Dispose()
        {
            _disposed = true;
            _motion.Stop();
            _presenter.RemoveHandler(InputElement.PointerWheelChangedEvent, BeforeWheel);
            _presenter.RemoveHandler(InputElement.PointerWheelChangedEvent, AfterWheel);
            _presenter.PropertyChanged -= PropertyChanged;
            _presenter.DetachedFromVisualTree -= Detached;
        }
    }
}

/// <summary>Time-based exponential easing with a small, bounded ramp during continuous input.</summary>
internal sealed class WheelScrollMotion
{
    private Vector _position;
    private Vector _target;
    private Vector _direction;
    private double _lastInput = double.NegativeInfinity;
    private double _lastFrame;
    private double _ramp;
    internal bool IsActive { get; private set; }

    internal void Push(Vector position, Vector delta, Vector maximum, double now)
    {
        var continuing = IsActive && now - _lastInput <= 0.12
            && delta.X * _direction.X + delta.Y * _direction.Y > 0;
        _ramp = continuing ? Math.Min(0.24, _ramp + 0.08) : 0;
        if (!continuing) _target = position;
        if (!IsActive) _lastFrame = now;
        _position = position;
        _target = Clamp(_target + delta * (1 + _ramp), maximum);
        _direction = delta;
        _lastInput = now;
        IsActive = _target != _position;
    }

    internal Vector Advance(double now, Vector maximum)
    {
        _target = Clamp(_target, maximum);
        // One response curve across frame rates. Repeated input moves the target without restarting time.
        var progress = 1 - Math.Exp(-24 * Math.Max(0, now - _lastFrame));
        _position = Clamp(_position + (_target - _position) * progress, maximum);
        _lastFrame = now;
        if ((_target - _position).Length < 0.1)
        {
            _position = _target;
            IsActive = false;
        }
        return _position;
    }

    internal void Stop() => IsActive = false;

    private static Vector Clamp(Vector value, Vector maximum) =>
        new(Math.Clamp(value.X, 0, maximum.X), Math.Clamp(value.Y, 0, maximum.Y));
}
