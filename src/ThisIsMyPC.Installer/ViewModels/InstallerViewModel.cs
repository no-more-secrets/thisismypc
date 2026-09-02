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
    ConfirmUninstall,
    Uninstalling,
    Done,
}

/// <summary>How the version already on the PC compares with the one in this installer.</summary>
public enum InstalledVersionRelation
{
    NotInstalled,
    Older,
    Same,
    Newer,
}

/// <summary>
/// One window, one primary button whose label follows the page. The view
/// model never touches the system; the engine does.
/// </summary>
public sealed partial class InstallerViewModel : ObservableObject
{
    private readonly IInstallEngine _engine;

    public InstallerViewModel(IInstallEngine engine, string licenseText, InstalledApp? installed, InstallOptions? existing)
    {
        ArgumentNullException.ThrowIfNull(engine);
        _engine = engine;
        LicenseText = licenseText ?? string.Empty;
        Installed = installed;
        VersionRelation = Compare(installed?.Version, AppVersion);

        // An update goes where the app already is; the MSI upgrade replaces it in place.
        _installFolder = installed?.InstallFolder ?? existing?.InstallFolder ?? InstallFolderRules.DefaultFolder;
        _desktopShortcut = existing?.DesktopShortcut ?? true;
        _startWithWindows = existing?.StartWithWindows ?? false;
        _checkForUpdates = existing?.CheckForUpdates ?? true;
        RefreshFolderCheck();
    }

    /// <summary>Set by the view: the folder dialog needs a window.</summary>
    public IFolderPicker? FolderPicker { get; set; }

    /// <summary>Set by the host: closes the window.</summary>
    public Action? RequestClose { get; set; }

    public string LicenseText { get; }

    public static string AppVersion { get; } = ReadVersion();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsInstalled), nameof(InstalledSummary), nameof(CanChooseFolder), nameof(ShowRemoveTab))]
    [NotifyCanExecuteChangedFor(nameof(UninstallCommand))]
    private InstalledApp? _installed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(InstallBlockedByNewer), nameof(CanGoPrimary), nameof(PrimaryButtonText), nameof(InstalledSummary), nameof(CanOpenLicense), nameof(CanOpenOptions))]
    [NotifyCanExecuteChangedFor(nameof(PrimaryCommand))]
    private InstalledVersionRelation _versionRelation;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWelcome), nameof(IsLicense), nameof(IsOptions), nameof(IsInstalling), nameof(IsConfirmUninstall), nameof(IsUninstalling), nameof(IsDone))]
    [NotifyPropertyChangedFor(nameof(PrimaryButtonText), nameof(CanGoBack), nameof(CanGoPrimary), nameof(CanCancel), nameof(StepCaption), nameof(IsBusy))]
    [NotifyPropertyChangedFor(nameof(TabIndex), nameof(IsFinished), nameof(CanOpenWelcome), nameof(CanOpenLicense), nameof(CanOpenOptions), nameof(IsInInstallTab), nameof(IsInRemoveTab), nameof(ShowRemoveTab))]
    [NotifyCanExecuteChangedFor(nameof(PrimaryCommand), nameof(BackCommand), nameof(CancelCommand), nameof(UninstallCommand))]
    private InstallStep _step = InstallStep.Welcome;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGoPrimary), nameof(CanOpenOptions))]
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
    [NotifyPropertyChangedFor(nameof(Failed), nameof(PrimaryButtonText), nameof(StepCaption), nameof(DoneInstalled))]
    private string? _errorText;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PrimaryButtonText), nameof(StepCaption), nameof(DoneInstalled), nameof(TabIndex), nameof(IsInInstallTab), nameof(IsInRemoveTab), nameof(ShowRemoveTab))]
    private bool _removed;

    [ObservableProperty]
    private bool _rebootRequired;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLogPath))]
    private string? _logPath;

    public bool IsWelcome => Step == InstallStep.Welcome;
    public bool IsLicense => Step == InstallStep.License;
    public bool IsOptions => Step == InstallStep.Options;
    public bool IsInstalling => Step == InstallStep.Installing;
    public bool IsConfirmUninstall => Step == InstallStep.ConfirmUninstall;
    public bool IsUninstalling => Step == InstallStep.Uninstalling;
    public bool IsDone => Step == InstallStep.Done;
    public bool IsBusy => Step is InstallStep.Installing or InstallStep.Uninstalling;

    public bool IsInstalled => Installed is not null;
    public bool InstallBlockedByNewer => VersionRelation == InstalledVersionRelation.Newer;

    /// <summary>Install or removal ran; the tab strip freezes on the result.</summary>
    public bool IsFinished => Step == InstallStep.Done;

    // ---- Tab strip: Welcome, License, Options, Install, Remove ----

    public const int WelcomeTab = 0;
    public const int LicenseTab = 1;
    public const int OptionsTab = 2;
    public const int InstallTab = 3;
    public const int RemoveTab = 4;

    public bool IsInInstallTab => Step is InstallStep.Installing || (Step == InstallStep.Done && !Removed);
    public bool IsInRemoveTab => Step is InstallStep.ConfirmUninstall or InstallStep.Uninstalling || (Step == InstallStep.Done && Removed);
    public bool ShowRemoveTab => IsInstalled || IsInRemoveTab;

    public bool CanOpenWelcome => !IsBusy && !IsFinished;
    public bool CanOpenLicense => !IsBusy && !IsFinished && !InstallBlockedByNewer;
    public bool CanOpenOptions => CanOpenLicense && LicenseAccepted;

    /// <summary>
    /// Selected tab, two-way. Reading follows the step; writing from a tab
    /// click moves to that page. Install and Remove tabs are reached by the
    /// buttons, never by clicking the tab, so writes to them are ignored.
    /// </summary>
    public int TabIndex
    {
        get => Step switch
        {
            InstallStep.Welcome => WelcomeTab,
            InstallStep.License => LicenseTab,
            InstallStep.Options => OptionsTab,
            InstallStep.ConfirmUninstall or InstallStep.Uninstalling => RemoveTab,
            InstallStep.Done => Removed ? RemoveTab : InstallTab,
            _ => InstallTab,
        };
        set
        {
            if (value == TabIndex)
                return;
            switch (value)
            {
                case WelcomeTab when CanOpenWelcome:
                    Step = InstallStep.Welcome;
                    break;
                case LicenseTab when CanOpenLicense:
                    Step = InstallStep.License;
                    break;
                case OptionsTab when CanOpenOptions:
                    Step = InstallStep.Options;
                    break;
                default:
                    OnPropertyChanged(nameof(TabIndex));
                    break;
            }
        }
    }

    /// <summary>Updates stay in the folder the app already occupies.</summary>
    public bool CanChooseFolder => !IsInstalled;

    public string InstalledSummary => Installed is null
        ? string.Empty
        : VersionRelation switch
        {
            InstalledVersionRelation.Same =>
                $"Version {Installed.Version} is already installed in {Installed.InstallFolder}. Continue to reinstall it, or remove it.",
            InstalledVersionRelation.Newer =>
                $"Version {Installed.Version} is installed in {Installed.InstallFolder}, and it is newer than this installer ({AppVersion}). Remove it first if you want this version.",
            _ =>
                $"Version {Installed.Version} is installed in {Installed.InstallFolder}. Continue to update it to {AppVersion}; your settings and history stay.",
        };

    public bool Failed => ErrorText is not null;
    public bool DoneInstalled => !Failed && !Removed;
    public bool HasFolderError => FolderError is not null;
    public bool HasFolderWarning => FolderWarning is not null;
    public bool HasLogPath => LogPath is not null;

    public string StepCaption => Step switch
    {
        InstallStep.Welcome => "Welcome",
        InstallStep.License => "License",
        InstallStep.Options => "Options",
        InstallStep.Installing => "Installing",
        InstallStep.ConfirmUninstall => "Remove ThisIsMyPC",
        InstallStep.Uninstalling => "Removing",
        InstallStep.Done when Failed => Removed ? "Not removed" : "Not installed",
        InstallStep.Done => Removed ? "Removed" : "Finished",
        _ => string.Empty,
    };

    public string PrimaryButtonText => Step switch
    {
        InstallStep.Welcome => "Next >",
        InstallStep.License => "Next >",
        InstallStep.Options => VersionRelation switch
        {
            InstalledVersionRelation.Same => "Reinstall",
            InstalledVersionRelation.Older => "Update",
            _ => "Install",
        },
        InstallStep.Installing => "Installing...",
        InstallStep.ConfirmUninstall => "Remove",
        InstallStep.Uninstalling => "Removing...",
        InstallStep.Done => Failed || Removed ? "Close" : "Finish",
        _ => "Next >",
    };

    public bool CanGoBack => Step is InstallStep.License or InstallStep.Options or InstallStep.ConfirmUninstall;

    public bool CanCancel => Step is not (InstallStep.Installing or InstallStep.Uninstalling or InstallStep.Done);

    public bool CanUninstall => IsInstalled && Step == InstallStep.Welcome;

    public bool CanGoPrimary => Step switch
    {
        InstallStep.Welcome => !InstallBlockedByNewer,
        InstallStep.License => LicenseAccepted,
        InstallStep.Options => FolderError is null,
        InstallStep.Installing or InstallStep.Uninstalling => false,
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
            case InstallStep.ConfirmUninstall:
                await UninstallAsync().ConfigureAwait(true);
                break;
            case InstallStep.Done:
                if (DoneInstalled && LaunchWhenDone)
                    _engine.Launch(InstallFolder);
                RequestClose?.Invoke();
                break;
            case InstallStep.Installing:
            case InstallStep.Uninstalling:
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
            InstallStep.ConfirmUninstall => InstallStep.Welcome,
            _ => Step,
        };
    }

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel() => RequestClose?.Invoke();

    [RelayCommand(CanExecute = nameof(CanUninstall))]
    private void Uninstall() => Step = InstallStep.ConfirmUninstall;

    [RelayCommand]
    private async Task BrowseAsync()
    {
        if (FolderPicker is null || !CanChooseFolder)
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
        Removed = false;
        Step = InstallStep.Installing;
        StatusText = "Starting...";
        var options = new InstallOptions(
            InstallFolder, DesktopShortcut, StartWithWindows, CheckForUpdates,
            Reinstall: VersionRelation == InstalledVersionRelation.Same);
        var progress = new Progress<string>(text => StatusText = text);

        var outcome = await _engine.InstallAsync(options, progress, CancellationToken.None).ConfigureAwait(true);

        LogPath = outcome.LogPath;
        RebootRequired = outcome.RebootRequired;
        ErrorText = outcome.Succeeded ? null : outcome.Error ?? "The installation did not finish.";
        Step = InstallStep.Done;
    }

    private async Task UninstallAsync()
    {
        if (Installed is null)
            return;

        ErrorText = null;
        Removed = true;
        Step = InstallStep.Uninstalling;
        StatusText = "Starting...";
        var progress = new Progress<string>(text => StatusText = text);

        var outcome = await _engine.UninstallAsync(Installed, progress, CancellationToken.None).ConfigureAwait(true);

        LogPath = outcome.LogPath;
        ErrorText = outcome.Succeeded ? null : outcome.Error ?? "The removal did not finish.";
        if (outcome.Succeeded)
        {
            Installed = null;
            VersionRelation = InstalledVersionRelation.NotInstalled;
        }
        Step = InstallStep.Done;
    }

    /// <summary>Numeric comparison where both parse; otherwise string equality decides Same, and anything else counts as Older.</summary>
    public static InstalledVersionRelation Compare(string? installedVersion, string packageVersion)
    {
        if (string.IsNullOrWhiteSpace(installedVersion))
            return InstalledVersionRelation.NotInstalled;
        if (Version.TryParse(Normalize(installedVersion), out var installed) && Version.TryParse(Normalize(packageVersion), out var package))
        {
            // System.Version ranks 1.0.0.0 above 1.0.0 (a missing part is -1); pad both to four parts.
            var order = Pad(installed).CompareTo(Pad(package));
            return order == 0 ? InstalledVersionRelation.Same
                : order < 0 ? InstalledVersionRelation.Older
                : InstalledVersionRelation.Newer;
        }
        return string.Equals(installedVersion.Trim(), packageVersion.Trim(), StringComparison.OrdinalIgnoreCase)
            ? InstalledVersionRelation.Same
            : InstalledVersionRelation.Older;
    }

    private static Version Pad(Version version)
        => new(version.Major, version.Minor, Math.Max(version.Build, 0), Math.Max(version.Revision, 0));

    /// <summary>System.Version wants dotted numbers only: drop a prerelease tag or build metadata.</summary>
    private static string Normalize(string version)
    {
        var cut = version.IndexOfAny(['-', '+']);
        return cut > 0 ? version[..cut] : version;
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
