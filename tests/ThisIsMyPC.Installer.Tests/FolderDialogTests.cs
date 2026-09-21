using ThisIsMyPC.Installer.Services;
using ThisIsMyPC.Installer.Win32;

namespace ThisIsMyPC.Installer.Tests;

/// <summary>The modern folder picker is configured through raw vtable calls; these exercise every slot except Show.</summary>
public class FolderDialogTests
{
    [Fact]
    public void Create_PicksFoldersAndStartsInTheNearestExistingFolder()
    {
        var existing = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar);
        var missing = Path.Combine(existing, "ThisIsMyPC-missing-" + Guid.NewGuid().ToString("N"), "App");
        RunOnStaThread(() =>
        {
            using var dialog = FolderDialog.Create(missing, "Choose a folder");
            Assert.NotNull(dialog);
            var options = dialog.Options;
            Assert.NotEqual(0u, options & NativeMethods.FOS_PICKFOLDERS);
            Assert.NotEqual(0u, options & NativeMethods.FOS_FORCEFILESYSTEM);
            Assert.NotEqual(0u, options & NativeMethods.FOS_NOCHANGEDIR);
            Assert.Equal(existing, dialog.CurrentFolder, ignoreCase: true);
        });
    }

    private static void RunOnStaThread(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var initialized = NativeMethods.CoInitializeEx(nint.Zero, NativeMethods.COINIT_APARTMENTTHREADED) >= 0;
            try { action(); }
            catch (Exception exception) { failure = exception; }
            finally { if (initialized) NativeMethods.CoUninitialize(); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "The dialog thread did not finish.");
        if (failure is not null)
            throw failure;
    }
}
