using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Avalonia.Threading;
using ThisIsMyPC.App.Services;
using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Modules.Startup.Changes;
using ThisIsMyPC.Modules.Startup.Models;

namespace ThisIsMyPC.App.ViewModels;

/// <summary>A guided sign-in example using the module's scan and change factory.</summary>
public sealed partial class GuideAutorunExampleViewModel : ViewModelBase
{
    private readonly Func<Task<OperationResult<object>>> scan;
    private readonly IPendingChangesService pending;
    private readonly IUserFeedback? feedback;
    private readonly Func<bool> supported;
    private readonly AutorunEnrichment? enrichment;
    public GuideAutorunExampleViewModel(Func<Task<OperationResult<object>>> scan,
        IPendingChangesService pending, IUserFeedback? feedback, Func<bool> supported,
        AutorunEnrichment? enrichment = null)
    {
        this.scan = scan;
        this.pending = pending;
        this.feedback = feedback;
        this.supported = supported;
        this.enrichment = enrichment;
        // The cached guide and pending service share the window lifetime.
        pending.PropertyChanged += (_, _) =>
        {
            if (Dispatcher.UIThread.CheckAccess()) RefreshPending();
            else Dispatcher.UIThread.Post(RefreshPending);
        };
    }

    private void RefreshPending()
    {
        IsStaged = Entry is { } entry && HasPending(entry);
        OnPropertyChanged(nameof(CanChoose));
    }

    private readonly HashSet<string> _seen = new(StringComparer.OrdinalIgnoreCase);
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanChoose))]
    private bool _isBusy;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanChoose))]
    private bool _isStaged;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasEntry)), NotifyPropertyChangedFor(nameof(CanChoose)),
        NotifyPropertyChangedFor(nameof(Explanation)), NotifyPropertyChangedFor(nameof(SourceText))]
    private AutorunEntry? _entry;
    [ObservableProperty] private string _emptyText = "Choose Try live example to find a sign-in entry on this PC.";
    public bool HasEntry => Entry is not null;
    public bool CanChoose => HasEntry && !IsBusy && !IsStaged && !pending.IsApplying && supported();
    public string SourceText => Entry is { } e ? $"Publisher: {e.Publisher}\nLaunch command: {e.Data}\nRegistered at: {e.Location}" : "";
    public string Explanation => Entry is not { } e ? "" :
        (e.Kind == AutorunItemKind.StartupFile
            ? "Windows opens this Startup folder entry when you sign in. The Startup folder contains shortcuts or files Windows launches automatically."
            : "Windows reads this Run key when you sign in. A Run key is a list in the Registry, Windows' settings database, of commands to launch automatically.")
        + "\n\nDisabling this entry stops this launch route after you apply the change. It does not uninstall the app or stop a copy already running. Other entries may still launch it."
        + "\n\nRecognize the app? Keep it enabled if you want it ready at sign-in, such as for syncing files or managing a device. If you only use it occasionally, you can disable this entry and open the app yourself. If you do not recognize it, keep it for now and check its name and file below."
        + "\n\nDisable adds the change to Pending changes. You can remove it there before applying, or restore it through History afterward.";

    private bool HasPending(AutorunEntry entry) => pending.PendingGroups.SelectMany(g => g.Changes)
        .Any(c => c.ModuleId == AutorunChangeFactory.ModuleId &&
            c.SettingId.Equals(AutorunChangeFactory.GetSettingId(entry), StringComparison.OrdinalIgnoreCase));

    private bool Suitable(AutorunEntry e) => Candidate(e) && !string.IsNullOrWhiteSpace(e.Publisher)
        && !e.Publisher.Contains("Microsoft", StringComparison.OrdinalIgnoreCase);

    private bool Candidate(AutorunEntry e) => e.Category == AutorunCategory.Logon && e.IsEnabled && e.CanToggle && e.FileExists
        && e.ImagePath is not null
        && !e.ImagePath.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.Windows) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
        && string.IsNullOrEmpty(e.Note) && !e.IsReRegistered && !HasPending(e)
        && (e.Kind == AutorunItemKind.StartupFile || e.Kind == AutorunItemKind.RegistryValue
            && e.Location.EndsWith(@"\CurrentVersion\Run", StringComparison.OrdinalIgnoreCase));

    private async Task<StartupScanData> ReadAsync()
    {
        var result = await scan().ConfigureAwait(true);
        if (!result.IsSuccess || result.Value is not StartupScanData data)
            throw new InvalidOperationException(result.ErrorMessage ?? "Startup entries could not be read.");
        if (data.AutorunsScanError is { } error)
            throw new InvalidOperationException(error);
        if (enrichment is null) return data;
        var entries = new List<AutorunEntry>();
        foreach (var entry in data.Autoruns.Where(Candidate))
        {
            var signature = await enrichment.GetSignatureAsync(entry.ImagePath!).ConfigureAwait(true);
            entries.Add(entry with { Publisher = signature switch
            {
                { State: SignatureState.Verified, Signer: { } signer } => $"{signer} (verified signer)",
                { State: SignatureState.Unsigned } => "Unknown; the file is not signed",
                { State: SignatureState.NotVerified } => $"{signature.Signer ?? "Unknown"} (signature not verified)",
                _ => "Unknown; signature could not be checked",
            } });
        }
        return data with { Autoruns = entries };
    }

    [RelayCommand]
    private async Task ShowAnotherAsync()
    {
        if (IsBusy || !supported()) return;
        IsBusy = true;
        IsStaged = false;
        try
        {
            var previous = Entry;
            var data = await ReadAsync().ConfigureAwait(true);
            var choices = data.Autoruns.Where(Suitable).ToArray();
            var unseen = choices.Where(e => !_seen.Contains(AutorunChangeFactory.GetSettingId(e))).ToArray();
            if (unseen.Length == 0)
            {
                _seen.Clear();
                unseen = choices.Where(e => previous is null || AutorunChangeFactory.GetSettingId(e) != AutorunChangeFactory.GetSettingId(previous)).ToArray();
                if (unseen.Length == 0) unseen = choices;
            }
            Entry = unseen.Length == 0 ? null : unseen[Random.Shared.Next(unseen.Length)];
            if (Entry is { } entry) _seen.Add(AutorunChangeFactory.GetSettingId(entry));
            EmptyText = "No suitable sign-in entry was found. Sample: a chat app can register itself to open when you sign in. Disabling that entry leaves the app installed, but you open it yourself. Open Startup & Services to inspect other Autoruns on this PC.";
        }
        catch (Exception ex)
        {
            Entry = null;
            EmptyText = "Startup entries could not be read. Try live example again to retry.";
            feedback?.Fail($"Could not load the guide example: {ex.Message}");
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task KeepEnabledAsync()
    {
        if (!CanChoose) return;
        feedback?.Report(Entry!.Name, "No change added.");
        await ShowAnotherAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task DisableAsync()
    {
        if (!CanChoose || Entry is not { } shown) return;
        IsBusy = true;
        try
        {
            var data = await ReadAsync().ConfigureAwait(true);
            var current = data.Autoruns.FirstOrDefault(e => AutorunChangeFactory.GetSettingId(e) == AutorunChangeFactory.GetSettingId(shown));
            // A new scan prevents staging an obsolete command after another page or program changes it.
            if (pending.IsApplying || !supported() || current is null || !Suitable(current) || current.Data != shown.Data || current.ImagePath != shown.ImagePath)
            {
                Entry = null;
                EmptyText = "This entry changed or already has a pending change. Choose Try live example to read another entry.";
                feedback?.Report(shown.Name, "The entry changed. No change added.");
                return;
            }
            pending.Stage(AutorunChangeFactory.CreateToggle(current, false));
            IsStaged = true;
            feedback?.Report(shown.Name, "Disable added to Pending changes.");
        }
        catch (Exception ex) { feedback?.Fail($"Could not stage the guide example: {ex.Message}"); }
        finally { IsBusy = false; }
    }
}
