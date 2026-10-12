using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ThisIsMyPC.Service;

namespace ThisIsMyPC.Ipc.Tests;

public sealed class ScmHostLifetimeTests
{
    [Fact]
    public async Task RunningFollowsWorkerStartupAndStopDrainsWorkers()
    {
        var events = new List<string>();
        var running = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseStop = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var host = CreateHost(new Worker(
            () => { events.Add("start"); return Task.CompletedTask; },
            async () => { stopped.SetResult(); await releaseStop.Task; events.Add("stop"); }));
        var run = ScmHostLifetime.RunAsync(host, () => { events.Add("running"); running.SetResult(); });
        await running.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(["start", "running"], events);
        host.Services.GetRequiredService<IHostApplicationLifetime>().StopApplication();
        await stopped.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(run.IsCompleted);
        releaseStop.SetResult();
        await run.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(["start", "running", "stop"], events);
    }

    [Fact]
    public async Task FailedStartupNeverReportsRunning()
    {
        using var host = CreateHost(new Worker(
            () => throw new InvalidOperationException("startup failure"), () => Task.CompletedTask));
        var running = false;
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ScmHostLifetime.RunAsync(host, () => running = true));
        Assert.Equal("startup failure", error.Message);
        Assert.False(running);
    }

    [Fact]
    public async Task FailedRunningReportStopsStartedWorkers()
    {
        var stopped = false;
        using var host = CreateHost(new Worker(() => Task.CompletedTask,
            () => { stopped = true; return Task.CompletedTask; }));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ScmHostLifetime.RunAsync(host, () => throw new InvalidOperationException("status failure")));
        Assert.Equal("status failure", error.Message);
        Assert.True(stopped);
    }

    [Fact]
    public async Task CancellationStopsWorkersBeforeReturning()
    {
        using var cancellation = new CancellationTokenSource();
        var stopped = false;
        using var host = CreateHost(new Worker(() => Task.CompletedTask,
            () => { stopped = true; return Task.CompletedTask; }));
        await ScmHostLifetime.RunAsync(host, cancellation.Cancel, cancellation.Token);
        Assert.True(stopped);
    }

    private static IHost CreateHost(IHostedService worker)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton<IHostLifetime, ScmHostLifetime>();
        builder.Services.AddSingleton(worker);
        return builder.Build();
    }

    private sealed class Worker(Func<Task> start, Func<Task> stop) : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken) => start();
        public Task StopAsync(CancellationToken cancellationToken) => stop();
    }
}
