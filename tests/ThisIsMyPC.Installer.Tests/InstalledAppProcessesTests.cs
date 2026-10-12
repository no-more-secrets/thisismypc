using ThisIsMyPC.Installer.Services;

namespace ThisIsMyPC.Installer.Tests;

public sealed class InstalledAppProcessesTests
{
    private sealed class Running(string imagePath, bool exitsOnClose, bool exitsDuringIdentification = false) : IInstalledAppProcess
    {
        private readonly TaskCompletionSource _exit = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string ImagePath
        {
            get
            {
                if (!exitsDuringIdentification) return imagePath;
                _exit.TrySetResult();
                throw new InvalidOperationException("The process exited during identification.");
            }
        }
        public bool HasExited => _exit.Task.IsCompleted;
        public int CloseRequests { get; private set; }
        public int KillRequests { get; private set; }
        public void RequestClose()
        {
            CloseRequests++;
            if (exitsOnClose) _exit.TrySetResult();
        }
        public void Kill() { KillRequests++; _exit.TrySetResult(); }
        public Task WaitForExitAsync(CancellationToken cancellationToken) => _exit.Task.WaitAsync(cancellationToken);
        public void Dispose() { }
    }

    [Fact]
    public async Task StopsBothAppCopiesAndTrayCompanions_WithoutStoppingServiceOrOtherFolders()
    {
        const string folder = @"C:\Program Files\NMS\ThisIsMyPC";
        var first = new Running(folder + @"\current\ThisIsMyPC.App.exe", true);
        var second = new Running(folder + @"\current\ThisIsMyPC.App.exe", false);
        var broker = new Running(folder + @"\current\ThisIsMyPC (administrator access).exe", false);
        var oldBroker = new Running(folder + @"\current\ThisIsMyPC.Broker.exe", false);
        var lighting = new Running(folder + @"\current\lighting-engine\ThisIsMyPC-LightingEngine.exe", false);
        var unrelated = new Running(@"C:\Elsewhere\ThisIsMyPC.App.exe", false);
        var sibling = new Running(folder + @"-Other\current\ThisIsMyPC.App.exe", false);
        var service = new Running(folder + @"\current\ThisIsMyPC.Service.exe", false);
        await InstalledAppProcesses.StopAsync(
            [first, second, broker, oldBroker, lighting, unrelated, sibling, service], [folder],
            TimeSpan.FromMilliseconds(1), CancellationToken.None);
        Assert.True(first.HasExited);
        Assert.Equal(0, first.KillRequests);
        foreach (var process in new[] { second, broker, oldBroker, lighting })
        {
            Assert.True(process.HasExited);
            Assert.Equal(1, process.KillRequests);
        }
        foreach (var process in new[] { unrelated, sibling, service })
        {
            Assert.Equal(0, process.CloseRequests);
            Assert.Equal(0, process.KillRequests);
        }
    }

    [Fact]
    public async Task StopsPreviousFolderWhenInstallDestinationChanges()
    {
        var old = new Running(@"C:\Program Files\Old\current\ThisIsMyPC.App.exe", true);
        var next = new Running(@"C:\Program Files\New\current\ThisIsMyPC.App.exe", true);
        await InstalledAppProcesses.StopAsync([old, next], [@"C:\Program Files\Old", @"C:\Program Files\New"],
            TimeSpan.FromSeconds(1), CancellationToken.None);
        Assert.True(old.HasExited);
        Assert.True(next.HasExited);
    }

    [Fact]
    public async Task ExitDuringIdentificationDoesNotAbortShutdown()
    {
        var process = new Running(@"C:\Program Files\NMS\ThisIsMyPC\current\ThisIsMyPC.App.exe", false, true);
        await InstalledAppProcesses.StopAsync([process], [@"C:\Program Files\NMS\ThisIsMyPC"],
            TimeSpan.FromMilliseconds(1), CancellationToken.None);
        Assert.True(process.HasExited);
        Assert.Equal(0, process.CloseRequests);
        Assert.Equal(0, process.KillRequests);
    }

    [Fact]
    public async Task CancellationDoesNotForceTermination()
    {
        var process = new Running(@"C:\Program Files\NMS\ThisIsMyPC\current\ThisIsMyPC.App.exe", false);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => InstalledAppProcesses.StopAsync(
            [process], [@"C:\Program Files\NMS\ThisIsMyPC"], TimeSpan.FromSeconds(1), cancellation.Token));
        Assert.Equal(0, process.CloseRequests);
        Assert.Equal(0, process.KillRequests);
    }
}
