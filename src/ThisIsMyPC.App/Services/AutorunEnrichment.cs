using ThisIsMyPC.Core.Services;

namespace ThisIsMyPC.App.Services;

/// <summary>
/// The slow facts about an autostart image, fetched after the page is up:
/// its shell icon and who signed it. Results are cached per path for the
/// life of the app (hundreds of rows share a few dozen files), and at most
/// two verifications run at once so WinVerifyTrust never floods the disk.
/// Either service may be absent (tests, or a platform without it), in which
/// case that fact stays null.
/// </summary>
public sealed class AutorunEnrichment
{
    private readonly IFileIconService? _icons;
    private readonly IAuthenticodeService? _signatures;
    private readonly Dictionary<string, Task<FileIcon?>> _iconCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Task<SignatureInfo?>> _signatureCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _gate = new(2);
    private readonly object _lock = new();

    public AutorunEnrichment(IFileIconService? icons = null, IAuthenticodeService? signatures = null)
    {
        _icons = icons;
        _signatures = signatures;
    }

    public bool HasIcons => _icons is not null;
    public bool HasSignatures => _signatures is not null;

    public Task<FileIcon?> GetIconAsync(string path)
    {
        if (_icons is null)
            return Task.FromResult<FileIcon?>(null);
        lock (_lock)
        {
            if (!_iconCache.TryGetValue(path, out var task))
            {
                task = Run(() => _icons.GetSmallIcon(path) is { IsSuccess: true, Value: { } icon } ? icon : null);
                _iconCache[path] = task;
            }
            return task;
        }
    }

    public Task<SignatureInfo?> GetSignatureAsync(string path)
    {
        if (_signatures is null)
            return Task.FromResult<SignatureInfo?>(null);
        lock (_lock)
        {
            if (!_signatureCache.TryGetValue(path, out var task))
            {
                task = Run<SignatureInfo?>(() => _signatures.Check(path));
                _signatureCache[path] = task;
            }
            return task;
        }
    }

    private async Task<T> Run<T>(Func<T> work)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            return await Task.Run(work).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }
}
