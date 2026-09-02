using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;

namespace ThisIsMyPC.App.Controls;

/// <summary>
/// The one card-with-a-switch every module page uses: title, an (i) tooltip
/// carrying the description, an optional monospace detail line (registry
/// path, file), and a content slot below the title for badges, warnings, and
/// buttons. Consumers tint it through the shared pending classes
/// (pending-enable, pending-disable, pending-modify, inactive) on the card
/// itself; the template forwards them to the border.
/// </summary>
public sealed class ToggleCard : ContentControl
{
    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<ToggleCard, string?>(nameof(Title));

    public static readonly StyledProperty<string?> DescriptionProperty =
        AvaloniaProperty.Register<ToggleCard, string?>(nameof(Description));

    public static readonly StyledProperty<string?> DetailProperty =
        AvaloniaProperty.Register<ToggleCard, string?>(nameof(Detail));

    public static readonly StyledProperty<bool> IsOnProperty =
        AvaloniaProperty.Register<ToggleCard, bool>(nameof(IsOn), defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<bool> IsSwitchEnabledProperty =
        AvaloniaProperty.Register<ToggleCard, bool>(nameof(IsSwitchEnabled), true);

    public static readonly StyledProperty<bool> IsSwitchVisibleProperty =
        AvaloniaProperty.Register<ToggleCard, bool>(nameof(IsSwitchVisible), true);

    public static readonly StyledProperty<ICommand?> CommandProperty =
        AvaloniaProperty.Register<ToggleCard, ICommand?>(nameof(Command));

    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>Shown as the (i) tooltip; the card stays one line tall.</summary>
    public string? Description
    {
        get => GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    /// <summary>Monospace tertiary line under the content: a registry path or file.</summary>
    public string? Detail
    {
        get => GetValue(DetailProperty);
        set => SetValue(DetailProperty, value);
    }

    public bool IsOn
    {
        get => GetValue(IsOnProperty);
        set => SetValue(IsOnProperty, value);
    }

    public bool IsSwitchEnabled
    {
        get => GetValue(IsSwitchEnabledProperty);
        set => SetValue(IsSwitchEnabledProperty, value);
    }

    public bool IsSwitchVisible
    {
        get => GetValue(IsSwitchVisibleProperty);
        set => SetValue(IsSwitchVisibleProperty, value);
    }

    /// <summary>Runs when the switch flips, for view models that act on the flip rather than the value.</summary>
    public ICommand? Command
    {
        get => GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    protected override Type StyleKeyOverride => typeof(ToggleCard);
}
