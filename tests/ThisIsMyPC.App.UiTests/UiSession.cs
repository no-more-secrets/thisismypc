using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ThisIsMyPC.App.UiTests.Fakes;
using ThisIsMyPC.App.ViewModels;
using ThisIsMyPC.App.Views;
using ThisIsMyPC.Core.Packages;
using ThisIsMyPC.Core.Services;

namespace ThisIsMyPC.App.UiTests;

/// <summary>
/// A human-shaped driver for the headless app: it looks at rendered pixels
/// (screenshots), finds things by the text a person would read, and interacts
/// through real input events (mouse at coordinates, keystrokes) rather than by
/// invoking commands directly.
/// </summary>
public sealed class UiSession : IDisposable
{
    private readonly string _shotDirectory;
    private int _shotCounter;

    public Window Window { get; }

    /// <summary>Where this session's screenshots land; tests may drop text dumps beside them.</summary>
    public string ShotDirectory => _shotDirectory;

    public ServiceProvider? Services { get; private init; }

    private UiSession(Window window, string suiteName)
    {
        Window = window;
        _shotDirectory = Path.Combine(FindRepoRoot(), "artifacts", "ui-shots", suiteName);
        Directory.CreateDirectory(_shotDirectory);
    }

    /// <summary>
    /// Switches the app-wide theme variant, like the Settings theme choice does.
    /// Reset to Dark on Dispose so sessions stay order-independent.
    /// </summary>
    public void SetTheme(Avalonia.Styling.ThemeVariant variant)
    {
        if (Application.Current is { } app)
            app.RequestedThemeVariant = variant;
        Pump();
    }

    /// <summary>Hosts a single view + view model in a bare window (CI-safe path).</summary>
    public static UiSession ForView(Control view, object viewModel, string suiteName,
        double width = 1000, double height = 700)
    {
        var window = new Window
        {
            Width = width,
            Height = height,
            Content = view,
            DataContext = viewModel,
        };
        // The real MainWindow paints BaseBrush behind every view; without it,
        // light-theme cards would float on the default white void.
        window.Bind(Window.BackgroundProperty, window.GetResourceObservable("BaseBrush"));
        view.DataContext = viewModel;
        var session = new UiSession(window, suiteName);
        window.Show();
        session.Pump();
        return session;
    }

    /// <summary>
    /// Boots the real MainWindow on the real service graph, with test-safe
    /// substitutions: fake winget/restore points, temp data paths. Module scans
    /// still read the live system (read-only), so full-app sessions belong in
    /// Category=Diagnostic tests.
    /// </summary>
    public static UiSession ForMainWindow(string suiteName, Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        App.ConfigureServices(services);

        var tempDataDir = Path.Combine(Path.GetTempPath(), "tipc-ui-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDataDir);

        services.RemoveAll<IWingetService>();
        services.AddSingleton<IWingetService, UiFakeWingetService>();
        services.RemoveAll<IRestorePointService>();
        services.AddSingleton<IRestorePointService, UiFakeRestorePointService>();
        // Velopack requires Program.Main's VelopackApp.Build(); the test host has no locator.
        services.RemoveAll<IUpdateService>();
        services.AddSingleton<IUpdateService, UiFakeUpdateService>();
        services.RemoveAll<Core.Settings.ISettingsService>();
        services.AddSingleton<Core.Settings.ISettingsService>(
            new Core.Settings.SettingsService(Path.Combine(tempDataDir, "settings.json")));
        services.RemoveAll<IChangeHistoryService>();
        services.AddSingleton<IChangeHistoryService>(sp => new ChangeHistoryService(
            sp.GetRequiredService<Core.Data.ChangeHistoryRepository>(),
            dbPath: Path.Combine(tempDataDir, "history.db"),
            enforcementExecutor: sp.GetService<Core.Enforcement.IEnforcementExecutor>(),
            driftBaseline: sp.GetService<Core.Drift.IDriftBaselineStore>()));
        services.RemoveAll<DisplayModePreferencesStore>();
        services.AddSingleton(new DisplayModePreferencesStore(
            Path.Combine(tempDataDir, "display-modes.txt")));
        services.RemoveAll<ThisIsMyPC.Modules.Startup.Services.TaskClassificationOverrideStore>();
        services.AddSingleton(new ThisIsMyPC.Modules.Startup.Services.TaskClassificationOverrideStore(
            Path.Combine(tempDataDir, "task-classifications.txt")));
        services.RemoveAll<Core.Sets.ICustomSetWriter>();
        services.AddSingleton<Core.Sets.ICustomSetWriter>(
            new Core.Sets.CustomSetWriter(Path.Combine(tempDataDir, "sets")));

        configure?.Invoke(services);

        var provider = services.BuildServiceProvider();
        var viewModel = provider.GetRequiredService<MainWindowViewModel>();
        var window = new MainWindow
        {
            DataContext = viewModel,
            Width = 1280,
            Height = 800,
        };

        var session = new UiSession(window, suiteName) { Services = provider };
        window.Show();
        session.Pump();
        return session;
    }

    // ---- Sight ----

    /// <summary>Renders the window and saves a numbered PNG. Returns the file path.</summary>
    public string Screenshot(string name)
    {
        Pump();
        using var frame = Window.CaptureRenderedFrame()
            ?? throw new InvalidOperationException("Headless renderer returned no frame — is Skia enabled?");
        var path = Path.Combine(_shotDirectory, $"{++_shotCounter:D2}-{name}.png");
        frame.Save(path);
        return path;
    }

    // ---- Finding things the way a person does: by visible text ----

    public T? TryFind<T>(Func<T, bool> predicate) where T : Visual =>
        Window.GetVisualDescendants().OfType<T>().FirstOrDefault(v => v.IsEffectivelyVisible && predicate(v));

    public IEnumerable<T> FindAll<T>(Func<T, bool> predicate) where T : Visual =>
        Window.GetVisualDescendants().OfType<T>().Where(v => v.IsEffectivelyVisible && predicate(v));

    /// <summary>The control's top edge in window pixels, for ordering assertions across parents.</summary>
    public double TopOf(Visual target) =>
        (target.TranslatePoint(new Point(0, 0), Window)
            ?? throw new InvalidOperationException("Control is not connected to the window's visual tree.")).Y;

    public T Find<T>(Func<T, bool> predicate) where T : Visual =>
        TryFind(predicate) ?? throw new InvalidOperationException(
            $"No visible {typeof(T).Name} matched. Visible text on screen: {DescribeVisibleText()}");

    /// <summary>A control whose visible text equals <paramref name="text"/> (TextBlock, or Button/toggle caption).</summary>
    public Visual FindText(string text) =>
        (Visual?)TryFind<TextBlock>(t => t.Text == text)
        ?? TryFind<ContentControl>(c => c.Content as string == text)
        ?? throw new InvalidOperationException(
            $"Nothing on screen reads '{text}'. Visible text: {DescribeVisibleText()}");

    public bool IsTextVisible(string text) =>
        TryFind<TextBlock>(t => t.Text == text) is not null
        || TryFind<ContentControl>(c => c.Content as string == text) is not null;

    public string DescribeVisibleText()
    {
        var texts = Window.GetVisualDescendants()
            .OfType<TextBlock>()
            .Where(t => t.IsEffectivelyVisible && !string.IsNullOrWhiteSpace(t.Text))
            .Select(t => t.Text!.Trim())
            .Distinct()
            .Take(60);
        return string.Join(" | ", texts);
    }

    // ---- Hands: real input events at real coordinates ----

    /// <summary>Left-clicks the center of a control, like a mouse would.</summary>
    /// <summary>Parks the mouse over a control so :pointerover styles apply; screenshot to inspect them.</summary>
    public void Hover(Visual target)
    {
        Window.MouseMove(CenterOf(target));
        Pump();
    }

    public void HoverText(string text) => Hover(FindText(text));

    public void Click(Visual target)
    {
        var point = CenterOf(target);
        Window.MouseMove(point);
        Window.MouseDown(point, MouseButton.Left);
        Window.MouseUp(point, MouseButton.Left);
        Pump();
    }

    /// <summary>Clicks whatever reads <paramref name="text"/> — the interactive ancestor if there is one.</summary>
    public void ClickText(string text)
    {
        var visual = FindText(text);
        // A caption sits inside its button; click the thing that actually reacts.
        var interactive = visual.GetVisualAncestors()
            .OfType<Button>()
            .FirstOrDefault() ?? (visual as Visual);
        Click(interactive!);
    }

    /// <summary>Clicks into a control and types, keystroke by keystroke.</summary>
    public void Type(Visual target, string text)
    {
        Click(target);
        Window.KeyTextInput(text);
        Pump();
    }

    // ---- Time ----

    /// <summary>Runs pending dispatcher jobs and a render tick.</summary>
    public void Pump()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>Waits for a condition while background work (module scans) completes.</summary>
    public async Task WaitForAsync(Func<bool> condition, int timeoutMs = 15000, string? what = null)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (!condition())
        {
            if (Environment.TickCount64 > deadline)
            {
                throw new TimeoutException(
                    $"Timed out waiting for {what ?? "condition"}. Visible text: {DescribeVisibleText()}");
            }

            await Task.Delay(50);
            Pump();
        }

        Pump();
    }

    private Point CenterOf(Visual target)
    {
        var bounds = target.Bounds;
        return target.TranslatePoint(new Point(bounds.Width / 2, bounds.Height / 2), Window)
            ?? throw new InvalidOperationException("Control is not connected to the window's visual tree.");
    }

    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "ThisIsMyPC.slnx")))
            dir = Path.GetDirectoryName(dir);
        return dir ?? AppContext.BaseDirectory;
    }

    public void Dispose()
    {
        // The Application instance is shared across the whole xUnit run; a test
        // that switched themes must not leak its variant into later tests.
        if (Application.Current is { } app)
            app.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
        Window.Close();
        // Modules implement IAsyncDisposable only; sync Dispose() on the
        // container throws. Their disposals complete synchronously in practice.
        Services?.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}
