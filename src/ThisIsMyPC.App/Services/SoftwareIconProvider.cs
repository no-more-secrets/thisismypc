using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using ThisIsMyPC.Core.Packages;
using ThisIsMyPC.Modules.Software.Services;

namespace ThisIsMyPC.App.Services;

public interface ISoftwareIconProvider
{
    Task<Bitmap?> ReadAsync(string packageId, string displayName, bool appx);
}

/// <summary>Cached installed-app artwork. Native reads stay off the UI thread.</summary>
public sealed class SoftwareIconProvider : ISoftwareIconProvider
{
    public static SoftwareIconProvider Instance { get; } = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, Lazy<Task<Bitmap?>>> _cache = new();
    private readonly SemaphoreSlim _gate = new(2);

    public Task<Bitmap?> ReadAsync(string packageId, string displayName, bool appx)
        => _cache.GetOrAdd($"{appx}:{packageId}:{displayName}", _ => new(() => LoadAsync(packageId, displayName, appx))).Value;

    private async Task<Bitmap?> LoadAsync(string packageId, string displayName, bool appx)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            return await Task.Run(() =>
            {
                if (appx)
                {
                    var bytes = Interop.Com.Packages.AppxIconReader.Read(packageId);
                    if (bytes is null) return null;
                    using var stream = new MemoryStream(bytes);
                    return new Bitmap(stream);
                }
                var pixels = Interop.Win32.Packages.InstalledAppIconReader.Read(displayName);
                if (pixels is null) return null;
                var bitmap = new WriteableBitmap(new PixelSize(pixels.Width, pixels.Height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Unpremul);
                using var buffer = bitmap.Lock();
                for (var y = 0; y < pixels.Height; y++)
                    System.Runtime.InteropServices.Marshal.Copy(pixels.Bgra, y * pixels.Width * 4, buffer.Address + y * buffer.RowBytes, pixels.Width * 4);
                return (Bitmap)bitmap;
            }).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or System.Runtime.InteropServices.COMException)
        { return null; }
        finally { _gate.Release(); }
    }
}

/// <summary>Artwork state shared by update and Windows app rows.</summary>
public sealed class SoftwareRowIcon : ObservableObject
{
    public Bitmap? Icon { get; private set; }
    public bool HasIcon => Icon is not null;
    public bool IsLoading { get; private set; }

    public SoftwareRowIcon(string packageId, string displayName, bool appx, ISoftwareIconProvider? provider)
    {
        var entry = SoftwareCatalog.Entries.FirstOrDefault(e => InstalledSoftwareMatcher.Matches(e,
            [new InstalledWingetPackage(packageId, null, displayName)]));
        if (appx)
        {
            var storeId = WindowsAppsCatalog.Entries.FirstOrDefault(e => e.PackageId == packageId)?.StoreId;
            entry = SoftwareCatalog.Entries.FirstOrDefault(e => e.WingetId == storeId || e.InstalledNames.Contains(displayName));
        }
        Icon = entry is null ? null : SoftwareIcons.Get(entry.Id);
        if (Icon is null && provider is not null)
        {
            IsLoading = true;
            _ = LoadAsync(provider, packageId, displayName, appx);
        }
    }

    private async Task LoadAsync(ISoftwareIconProvider provider, string id, string name, bool appx)
    {
        var icon = await provider.ReadAsync(id, name, appx).ConfigureAwait(false);
        Dispatcher.UIThread.Post(() =>
        {
            Icon = icon;
            IsLoading = false;
            OnPropertyChanged(nameof(Icon));
            OnPropertyChanged(nameof(HasIcon));
            OnPropertyChanged(nameof(IsLoading));
        });
    }
}
