using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace ThisIsMyPC.Installer.Services;

/// <summary>The stock Windows folder dialog, through Avalonia's storage provider.</summary>
public sealed class StorageFolderPicker : IFolderPicker
{
    private readonly Window _owner;

    public StorageFolderPicker(Window owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        _owner = owner;
    }

    public async Task<string?> PickAsync(string startFolder)
    {
        var provider = _owner.StorageProvider;
        IStorageFolder? start = null;
        if (!string.IsNullOrWhiteSpace(startFolder))
        {
            var parent = Path.GetDirectoryName(startFolder);
            if (parent is not null && Directory.Exists(parent))
                start = await provider.TryGetFolderFromPathAsync(parent).ConfigureAwait(true);
        }

        var picked = await provider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Choose where to install ThisIsMyPC",
            AllowMultiple = false,
            SuggestedStartLocation = start,
        }).ConfigureAwait(true);

        return picked.Count == 0 ? null : picked[0].TryGetLocalPath();
    }
}
