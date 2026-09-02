using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ThisIsMyPC.Installer.Services;

namespace ThisIsMyPC.Installer.ViewModels;

public enum InstallStep
{
    Welcome,
    License,
    Options,
    Installing,
    Done,
}

/// <summary>
/// One window, five pages, one primary button whose label follows the page.
/// The view model never touches the system; the engine does.
/// </summary>
public sealed partial class InstallerViewModel : ObservableObject
{
    private readonly IInstallEngine _engine;

    public InstallerViewModel(IInstallEngine engine, string licenseText, InstallOptions? existing)
    {
        ArgumentNullException.ThrowIfNull(engine);
        _engine = engine;
        LicenseText = licenseText ?? string.Empty;

        _installFolder = existing?.InstallFolder ?? InstallFolderRules.DefaultFolder;
        _desktopShortcut = existing?.DesktopShortcut ?? true;
        _startWithWindows = existing?.StartWithWindows ?? false;
        _checkForUpdates = existing?.CheckForUpdates ?? true;
        IsUpgrade = existing is not null;
        RefreshFolderCheck();
    }

    /// <summary>Set by the view: the folder dialog needs a window.</summary>
    public IFolderPicker? FolderPicker { get; set; }

    /// <summary>Set by the host: closes the window.</summary>
    public Action? RequestClose { get; set; }

    public string LicenseText { get; }

    public bool IsUpgrade { get; }

    public static string AppVersion { get; } = ReadVersion();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWelcome), nameof(IsLicense), nameof(IsOptions), nameof(IsInstalling), nameof(IsDone))]
    [NotifyPropertyChangedFor(nameof(PrimaryButtonText), nameof(CanGoBack), nameof(CanGoPrimary), nameof(CanCancel), nameof(StepCaption))]
    [NotifyCanExecuteChangedFor(nameof(PrimaryCommand), nameof(BackCommand), nameof(CancelCommand))]
    private InstallStep _step = InstallStep.Welcome;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGoPrimary))]
    [NotifyCanExecuteChangedFor(nameof(PrimaryCommand))]
    private bool _licenseAccepted;

    [ObservableProperty]
    private string _installFolder;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFolderError), nameof(CanGoPrimary))]
    [NotifyCanExecuteChangedFor(nameof(PrimaryCommand))]
    private string? _folderError;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFolderWarning))]
    private string? _folderWarning;

    [ObservableProperty]
    private bool _desktopShortcut;

    [ObservableProperty]
    private bool _startWithWindows;

    [ObservableProperty]
    private bool _checkForUpdates;

    [ObservableProperty]
    private bool _launchWhenDone = true;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Failed), nameof(PrimaryButtonText))]
    private string? _errorText;

    [ObservableProperty]
    private bool _rebootRequired;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLogPath))]
    private string? _logPath;

    public bool IsWelcome => Step == InstallStep.Welcome;
    public bool IsLicense => Step == InstallStep.License;
    public bool IsOptions => Step == InstallStep.Options;
    public bool IsInstalling => Step == InstallStep.Installing;
    public bool IsDone => Step == InstallStep.Done;

    public bool Failed => ErrorText is not null;
    public bool HasFolderError => FolderError is not null;
    public bool HasFolderWarning => FolderWarning is not null;
    public bool HasLogPath => LogPath is not null;

    public string StepCaption => Step switch
    {
        InstallStep.Welcome => "Welcome",
        InstallStep.License => "License",
        InstallStep.Options => "Options",
        InstallStep.Installing => "Installing",
        InstallStep.Done => Failed ? "Not installed" : "Finished",
        _ => string.Empty,
    };

    public string PrimaryButtonText => Step switch
    {
        InstallStep.Welcome => "Next",
        InstallStep.License => "Next",
        InstallStep.Options => IsUpgrade ? "Update" : "Install",
        InstallStep.Installing => "Installing...",
        InstallStep.Done => Failed ? "Close" : "Finish",
        _ => "Next",
    };

    public bool CanGoBack => Step is InstallStep.License or InstallStep.Options;

    public bool CanCancel => Step is not InstallStep.Installing and not InstallStep.Done;

    public bool CanGoPrimary => Step switch
    {
        InstallStep.License => LicenseAccepted,
        InstallStep.Options => FolderError is null,
        InstallStep.Installing => false,
        _ => true,
    };

    partial void OnInstallFolderChanged(string value) => RefreshFolderCheck();

    private void RefreshFolderCheck()
    {
        var check = InstallFolderRules.Check(InstallFolder);
        FolderError = check.Error;
        FolderWarning = check.Warning;
    }

    [RelayCommand(CanExecute = nameof(CanGoPrimary))]
    private async Task PrimaryAsync()
    {
        switch (Step)
        {
            case InstallStep.Welcome:
                Step = InstallStep.License;
                break;
            case InstallStep.License:
                Step = InstallStep.Options;
                break;
            case InstallStep.Options:
                await InstallAsync().ConfigureAwait(true);
                break;
            case InstallStep.Done:
                if (!Failed && LaunchWhenDone)
                    _engine.Launch(InstallFolder);
                RequestClose?.Invoke();
                break;
            case InstallStep.Installing:
            default:
                break;
        }
    }

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void Back()
    {
        Step = Step switch
        {
            InstallStep.License => InstallStep.Welcome,
            InstallStep.Options => InstallStep.License,
            _ => Step,
        };
    }

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel() => RequestClose?.Invoke();

    [RelayCommand]
    private async Task BrowseAsync()
    {
        if (FolderPicker is null)
            return;
        var picked = await FolderPicker.PickAsync(InstallFolder).ConfigureAwait(true);
        if (!string.IsNullOrWhiteSpace(picked))
            InstallFolder = InstallFolderRules.WithAppFolder(picked);
    }

    private async Task InstallAsync()
    {
        if (!_engine.HasPackage)
        {
            ErrorText = "This copy of the installer carries no package. Download it again from the Releases page.";
            Step = InstallStep.Done;
            return;
        }

        ErrorText = null;
        Step = InstallStep.Installing;
        StatusText = "Starting...";
        var options = new InstallOptions(InstallFolder, DesktopShortcut, StartWithWindows, CheckForUpdates);
        var progress = new Progress<string>(text => StatusText = text);

        var outcome = await _engine.InstallAsync(options, progress, CancellationToken.None).ConfigureAwait(true);

        LogPath = outcome.LogPath;
        RebootRequired = outcome.RebootRequired;
        ErrorText = outcome.Succeeded ? null : outcome.Error ?? "The installation did not finish.";
        Step = InstallStep.Done;
    }

    private static string ReadVersion()
    {
        var informational = typeof(InstallerViewModel).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (string.IsNullOrWhiteSpace(informational))
            return string.Empty;
        // Strip the source-link build metadata (+sha) the SDK appends.
        var plus = informational.IndexOf('+', StringComparison.Ordinal);
        return plus > 0 ? informational[..plus] : informational;
    }
}
