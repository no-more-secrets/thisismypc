using ThisIsMyPC.Interop.Win32.Services;

namespace ThisIsMyPC.Integration.Tests.Services;

public sealed class NativeServiceStatusTests
{
    [Fact]
    public void ReportsStartupShutdownAndTerminalExitCode()
    {
        var reports = new List<NativeServiceControl.ServiceStatus>();
        var status = new NativeServiceStatus(reports.Add);
        status.ReportState(NativeServiceControl.SERVICE_START_PENDING);
        status.ReportRunning();
        Assert.True(status.TryRequestStop());
        Assert.False(status.TryRequestStop());
        status.ReportRunning();
        status.ReportState(NativeServiceControl.SERVICE_STOPPED, 1064);
        status.Interrogate();
        Assert.False(status.TryRequestStop());
        status.ReportState(NativeServiceControl.SERVICE_STOPPED);
        Assert.Equal([2u, 4u, 3u, 1u], reports.Select(r => r.dwCurrentState));
        Assert.Equal([0u, 5u, 0u, 0u], reports.Select(r => r.dwControlsAccepted));
        Assert.Equal(1064u, reports[^1].dwWin32ExitCode);
        Assert.Equal(0u, reports[^1].dwCheckPoint);
        Assert.Equal(0u, reports[^1].dwWaitHint);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ControlsWaitingForStoppedReportCannotPublishAgain(bool stop)
    {
        using var publishing = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var count = 0;
        var status = new NativeServiceStatus(_ =>
        {
            Interlocked.Increment(ref count);
            publishing.Set();
            Assert.True(release.Wait(TimeSpan.FromSeconds(5)));
        });
        var finish = Task.Run(() => status.ReportState(NativeServiceControl.SERVICE_STOPPED));
        Assert.True(publishing.Wait(TimeSpan.FromSeconds(5)));
        var attempted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var control = Task.Run(() =>
        {
            attempted.SetResult();
            if (stop) Assert.False(status.TryRequestStop());
            else status.Interrogate();
        });
        await attempted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        release.Set();
        await Task.WhenAll(finish, control).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, count);
    }

    [Fact]
    public void FailedStoppedReportStillPreventsFurtherNativeCalls()
    {
        var count = 0;
        var status = new NativeServiceStatus(_ =>
        {
            count++;
            throw new InvalidOperationException("closed connection");
        });
        Assert.Throws<InvalidOperationException>(() => status.ReportState(NativeServiceControl.SERVICE_STOPPED));
        status.Interrogate();
        status.ReportRunning();
        status.ReportState(NativeServiceControl.SERVICE_STOPPED);
        Assert.False(status.TryRequestStop());
        Assert.Equal(1, count);
    }
}
