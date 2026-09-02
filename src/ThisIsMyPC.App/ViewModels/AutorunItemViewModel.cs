using System.ComponentModel;
using System.Globalization;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using ThisIsMyPC.App.Services;
using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Modules.Startup.Changes;
using ThisIsMyPC.Modules.Startup.Models;

namespace ThisIsMyPC.App.ViewModels;

/// <summary>A category header row inside the search results list.</summary>
public sealed record AutorunSearchHeader(string Text);

/// <summary>A location header row: the registry key or folder, and when it was last written, the way Autoruns heads each group.</summary>
public sealed record AutorunLocationHeader(string Location, DateTime? Timestamp)
{
    public string TimestampText => AutorunItemViewModel.FormatTimestamp(Timestamp);
}

/// <summary>
/// One tab of the page, the way Autoruns has one per category. Items holds
/// location headers and rows, swapped whole when a filter changes so the
/// virtualized list sees one reset instead of one event per row.
/// </summary>
public sealed partial class AutorunTabViewModel : ObservableObject
{
    public AutorunTabViewModel(AutorunCategory category)
    {
        Category = category;
        Name = AutorunEntry.CategoryName(category);
        _header = Name;
    }

    public AutorunCategory Category { get; }
    public string Name { get; }

    [ObservableProperty]
    private IReadOnlyList<object> _items = [];

    [ObservableProperty]
    private string _header;

    [ObservableProperty]
    private bool _hasItems;

    public void Replace(IReadOnlyList<object> items, int shown, int total)
    {
        Items = items;
        Header = CountLabel(Name, shown, total);
        HasItems = shown > 0;
    }

    public static string CountLabel(string name, int shown, int total)
        => shown == total ? $"{name} ({total})" : $"{name} ({shown} of {total})";
}

/// <summary>
/// One Autoruns row. The switch stages an enable or disable through the
/// pending pipeline; the baseline is the scan-time state, and the row follows
/// the queue (apply keeps the switch, discard snaps it back). Icon and signer
/// arrive later from <see cref="AutorunEnrichment"/>.
/// </summary>
public sealed partial class AutorunItemViewModel : ObservableObject, IDisposable
{
    private static readonly string WindowsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

    private readonly IPendingChangesService _pendingChangesService;
    private bool _liveIsEnabled;
    private string? _stagedGroupId;
    private bool _suppressStaging;
    private bool _isStagingChange;
    private bool _disposed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StateText), nameof(IsPendingEnable), nameof(IsPendingDisable))]
    private bool _isEnabled;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPendingEnable), nameof(IsPendingDisable))]
    private bool _hasPendingChange;

    [ObservableProperty]
    private Bitmap? _icon;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PublisherText), nameof(IsUnverified), nameof(IsWindowsEntry))]
    private SignatureInfo? _signature;

    public AutorunItemViewModel(AutorunEntry entry, IPendingChangesService pendingChangesService)
    {
        Entry = entry;
        _pendingChangesService = pendingChangesService;
        _liveIsEnabled = entry.IsEnabled;

        _suppressStaging = true;
        IsEnabled = entry.IsEnabled;

        var settingId = AutorunChangeFactory.GetSettingId(entry);
        var existing = pendingChangesService.PendingGroups.FirstOrDefault(g =>
            g.Changes.Count == 1 &&
            g.Changes[0].ModuleId == AutorunChangeFactory.ModuleId &&
            g.Changes[0].SettingId == settingId);
        if (existing is not null)
        {
            var pendingEnabled = existing.Changes[0].Category == ChangeCategory.Enable;
            if (pendingEnabled == _liveIsEnabled)
                pendingChangesService.Unstage(existing.GroupId);
            else
            {
                _stagedGroupId = existing.GroupId;
                IsEnabled = pendingEnabled;
            }
        }

        _suppressStaging = false;
        HasPendingChange = IsEnabled != _liveIsEnabled;
        _pendingChangesService.PropertyChanged += OnPendingChangesPropertyChanged;
    }

    public AutorunEntry Entry { get; }

    public string Name => Entry.Name;
    public string CategoryName => AutorunEntry.CategoryName(Entry.Category);
    public string DescriptionText => Entry.Description ?? string.Empty;
    public bool HasDescription => !string.IsNullOrEmpty(Entry.Description);
    public string NoteText => Entry.Note ?? string.Empty;
    public bool HasNote => !string.IsNullOrEmpty(Entry.Note);
    public bool HasPlainNote => HasNote && !Entry.IsReRegistered;
    public bool IsReRegistered => Entry.IsReRegistered;
    public string StateText => IsEnabled ? "Enabled" : "Disabled";

    /// <summary>The row tints green or red while its flip waits in the queue.</summary>
    public bool IsPendingEnable => HasPendingChange && IsEnabled;
    public bool IsPendingDisable => HasPendingChange && !IsEnabled;
    public bool CanToggle => Entry.CanToggle;

    /// <summary>Autoruns' yellow row: the image file is not there.</summary>
    public bool IsMissing => !Entry.FileExists;

    /// <summary>Autoruns' red row: the file is there but nothing verified signs it (checked, and unsigned or a bad chain).</summary>
    public bool IsUnverified => Entry.FileExists && Signature is { State: SignatureState.Unsigned or SignatureState.NotVerified };

    /// <summary>"(Verified) Adobe Inc." once checked; the version-resource company until then.</summary>
    public string PublisherText => Signature switch
    {
        { State: SignatureState.Verified, Signer: { } signer } => $"(Verified) {signer}",
        { State: SignatureState.NotVerified } s => $"(Not verified) {s.Signer ?? Entry.Publisher ?? "Unknown publisher"}",
        _ => Entry.Publisher ?? "Unknown publisher",
    };

    public string ImagePathText => IsMissing && Entry.ImagePath is { } missing
        ? $"File not found: {missing}"
        : Entry.ImagePath ?? Entry.Data;

    /// <summary>A task's data is its own path; showing it twice says nothing.</summary>
    public bool HasImagePath => !string.Equals(ImagePathText, LocationText, StringComparison.OrdinalIgnoreCase);

    public string TimestampText => FormatTimestamp(Entry.Timestamp);
    public bool HasTimestamp => Entry.Timestamp is not null;

    /// <summary>Where the item is registered: key and value, folder and file, task path, or service key.</summary>
    public string LocationText => Entry.Kind switch
    {
        AutorunItemKind.ScheduledTask => Entry.Location,
        _ => $@"{Entry.Location}\{Entry.Name}",
    };

    public bool IsMicrosoft => Entry.Publisher?.Contains("Microsoft", StringComparison.OrdinalIgnoreCase) == true
        || Signature?.Signer?.Contains("Microsoft", StringComparison.OrdinalIgnoreCase) == true
        || IsFilelessWindowsTask;

    /// <summary>
    /// Part of Windows itself: signed by "Microsoft Windows", or Microsoft's
    /// and living under the Windows folder, or a task in Windows' own task
    /// tree that runs a built-in COM handler with no file to sign.
    /// </summary>
    public bool IsWindowsEntry => Signature is { State: SignatureState.Verified, Signer: { } signer } && signer.Contains("Microsoft Windows", StringComparison.OrdinalIgnoreCase)
        || (Signature is null && IsMicrosoft && Entry.ImagePath is { } image && image.StartsWith(WindowsDirectory, StringComparison.OrdinalIgnoreCase))
        || IsFilelessWindowsTask;

    /// <summary>
    /// Some Windows tasks (WaaSMedic, StartComponentCleanup, SharedPC) run a
    /// COM handler registered without a server path, so there is no file to
    /// check. Nothing can hide behind such a task: a handler with real code
    /// resolves to a DLL and gets signature-checked like everything else.
    /// </summary>
    private bool IsFilelessWindowsTask => Entry.Kind == AutorunItemKind.ScheduledTask
        && Entry.ImagePath is null
        && Entry.Location.StartsWith(@"\Microsoft\Windows\", StringComparison.OrdinalIgnoreCase);

    /// <summary>Text the filter box matches against.</summary>
    public bool Matches(string filter)
        => Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
        || DescriptionText.Contains(filter, StringComparison.OrdinalIgnoreCase)
        || PublisherText.Contains(filter, StringComparison.OrdinalIgnoreCase)
        || ImagePathText.Contains(filter, StringComparison.OrdinalIgnoreCase)
        || LocationText.Contains(filter, StringComparison.OrdinalIgnoreCase);

    public static string FormatTimestamp(DateTime? time)
        => time is { } t ? t.ToString("ddd MMM d HH:mm:ss yyyy", CultureInfo.CurrentCulture) : string.Empty;

    /// <summary>Fetches icon and signer in the background and lands them on the UI thread.</summary>
    public async Task EnrichAsync(AutorunEnrichment enrichment, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(enrichment);
        var path = Entry.ImagePath ?? (Entry.Kind == AutorunItemKind.StartupFile ? Entry.Data : null);
        if (path is null)
            return;

        var iconTask = enrichment.GetIconAsync(path);
        var signatureTask = Entry.FileExists ? enrichment.GetSignatureAsync(path) : Task.FromResult<SignatureInfo?>(null);
        var icon = await iconTask.ConfigureAwait(false);
        var signature = await signatureTask.ConfigureAwait(false);
        if (cancellationToken.IsCancellationRequested || _disposed)
            return;

        var bitmap = icon is null ? null : ToBitmap(icon);
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (_disposed)
                return;
            Icon = bitmap;
            Signature = signature;
        });
    }

    private static Bitmap ToBitmap(FileIcon icon)
    {
        var bitmap = new WriteableBitmap(new PixelSize(icon.Width, icon.Height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Unpremul);
        using var buffer = bitmap.Lock();
        var stride = icon.Width * 4;
        for (var y = 0; y < icon.Height; y++)
            System.Runtime.InteropServices.Marshal.Copy(icon.Bgra, y * stride, buffer.Address + y * buffer.RowBytes, stride);
        return bitmap;
    }

    partial void OnIsEnabledChanged(bool value)
    {
        if (_suppressStaging || _disposed)
            return;

        _isStagingChange = true;
        try
        {
            if (_stagedGroupId is not null)
            {
                _pendingChangesService.Unstage(_stagedGroupId);
                _stagedGroupId = null;
            }

            if (value != _liveIsEnabled)
            {
                var change = AutorunChangeFactory.CreateToggle(Entry with { IsEnabled = _liveIsEnabled }, value);
                var group = new ChangeGroup
                {
                    GroupId = Guid.NewGuid().ToString("N"),
                    DisplayName = change.DisplayName,
                    Description = change.DisplayName,
                    Changes = [change],
                };
                _pendingChangesService.Stage(group);
                _stagedGroupId = group.GroupId;
            }
        }
        finally
        {
            _isStagingChange = false;
        }

        HasPendingChange = IsEnabled != _liveIsEnabled;
    }

    private void OnPendingChangesPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_isStagingChange || e.PropertyName is not nameof(IPendingChangesService.PendingGroups))
            return;
        if (Dispatcher.UIThread.CheckAccess())
            HandlePendingGroupsChanged();
        else
            Dispatcher.UIThread.Post(HandlePendingGroupsChanged);
    }

    private void HandlePendingGroupsChanged()
    {
        if (_stagedGroupId is null || _pendingChangesService.PendingGroups.Any(g => g.GroupId == _stagedGroupId))
            return;

        _stagedGroupId = null;
        if (_pendingChangesService.IsApplying)
            _liveIsEnabled = IsEnabled;
        else
        {
            _suppressStaging = true;
            IsEnabled = _liveIsEnabled;
            _suppressStaging = false;
        }
        HasPendingChange = IsEnabled != _liveIsEnabled;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _pendingChangesService.PropertyChanged -= OnPendingChangesPropertyChanged;
    }
}
