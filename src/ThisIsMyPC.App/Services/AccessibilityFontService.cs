using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using ThisIsMyPC.Core.Settings;

namespace ThisIsMyPC.App.Services;

/// <summary>
/// Applies the 10-4 dyslexia-friendly font preference. When enabled, an OpenDyslexic
/// override is inserted into the application resource dictionary under the BodyFont
/// key, shadowing the IBM Plex Sans default from Styles/Typography.axaml; every
/// consumer reads BodyFont via DynamicResource so the swap is live. DisplayFont
/// (headings) and MonoFont (registry paths) are deliberately untouched.
/// </summary>
public sealed class AccessibilityFontService : IDisposable
{
    public const string BodyFontKey = "BodyFont";
    public const string OpenDyslexicFamily = "avares://ThisIsMyPC.App/Assets/Fonts#OpenDyslexic";

    private readonly ISettingsService _settings;
    private readonly IResourceDictionary _appResources;
    private readonly Action<Action> _dispatch;

    public AccessibilityFontService(
        ISettingsService settings, IResourceDictionary appResources, Action<Action>? dispatch = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(appResources);
        _settings = settings;
        _appResources = appResources;
        _dispatch = dispatch ?? DispatchToUiThread;

        _settings.SettingChanged += OnSettingChanged;
        Apply(_settings.GetAppBool(AppSettingKeys.DyslexiaFont, fallback: false));
    }

    private void OnSettingChanged(object? sender, SettingChangedEventArgs e)
    {
        if (e is not { Scope: SettingChangedEventArgs.AppScope, Key: AppSettingKeys.DyslexiaFont })
            return;
        var enabled = e.Value == "1";
        _dispatch(() => Apply(enabled));
    }

    // Resource dictionaries must only mutate on the UI thread; SettingChanged can
    // raise off it (e.g. a settings-import continuation).
    private static void DispatchToUiThread(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
            action();
        else
            Dispatcher.UIThread.Post(action);
    }

    private void Apply(bool enabled)
    {
        if (enabled)
        {
            // The FontFamily is constructed lazily — Avalonia touches the embedded
            // OTFs only when the first glyph run resolves against this family.
            _appResources[BodyFontKey] = new FontFamily(OpenDyslexicFamily);
        }
        else
        {
            // Removing the override falls back to the Typography.axaml default.
            _appResources.Remove(BodyFontKey);
        }
    }

    public void Dispose() => _settings.SettingChanged -= OnSettingChanged;
}
