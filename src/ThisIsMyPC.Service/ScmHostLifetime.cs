using Microsoft.Extensions.Hosting;

namespace ThisIsMyPC.Service;

/// <summary>Leaves service callbacks to the native dispatcher instead of ServiceBase or ConsoleLifetime.</summary>
public sealed class ScmHostLifetime : IHostLifetime
{
    /// <summary>The native dispatcher already accepted startup before starting the host.</summary>
    public Task WaitForStartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>The native dispatcher reports stopped only after the host finishes shutdown.</summary>
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>Starts the workers, reports running, and drains the workers before returning to the dispatcher.</summary>
    public static async Task RunAsync(IHost host, Action reportRunning, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(reportRunning);
        await host.StartAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            reportRunning();
        }
        catch
        {
            await host.StopAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        await host.WaitForShutdownAsync(cancellationToken).ConfigureAwait(false);
    }
}
