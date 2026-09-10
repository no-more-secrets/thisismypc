using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using ThisIsMyPC.Core.Hardware;
using ThisIsMyPC.Core.Hardware.Detection;
using ThisIsMyPC.Core.Hardware.Lighting;
using ThisIsMyPC.Core.Results;

namespace ThisIsMyPC.Interop.Win32.Hardware;

/// <summary>
/// Runs the bundled OpenRGB as a headless SDK server for the life of the app.
/// The process is put in a job object that kills it when the app's handle
/// closes, so a crash never leaves a server behind. It gets its own
/// configuration folder under the app's data directory and binds to
/// localhost only. A server already answering on the port (the user's own
/// OpenRGB, or a copy this app started earlier) is used instead of starting
/// another, which would contend for the same USB and SMBus devices.
/// </summary>
public sealed class BundledOpenRgbHost : IOpenRgbHost, IDisposable
{
    private static readonly NLog.Logger Log = NLog.LogManager.GetLogger("ThisIsMyPC.Interop.Win32.OpenRgb");
    private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(40);
    private static readonly TimeSpan SettleTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan ProbeInterval = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromMilliseconds(750);

    private readonly IHardwareProbeEnvironment _environment;
    private readonly string _configDirectory;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private Process? _process;
    private nint _job;
    private bool _adoptedExternalServer;

    public BundledOpenRgbHost(IHardwareProbeEnvironment environment, string configDirectory, int port = OpenRgbSdkProtocol.DefaultPort)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentException.ThrowIfNullOrEmpty(configDirectory);
        _environment = environment;
        _configDirectory = configDirectory;
        Port = port;
        BundledExecutablePath = environment.BundledCompanionExecutable(CompanionApp.OpenRgb);
        State = BundledExecutablePath is null ? OpenRgbHostState.NotBundled : OpenRgbHostState.Stopped;
    }

    public string? BundledExecutablePath { get; }

    public OpenRgbHostState State { get; private set; }

    public string? LastError { get; private set; }

    public int Port { get; }

    public async Task<OperationResult<bool>> EnsureRunningAsync(CancellationToken cancellationToken = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        var ct = linked.Token;
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_process is { HasExited: false } && State == OpenRgbHostState.Running)
                return OperationResult<bool>.Success(true);

            // The probe is a blocking socket call; keep it off the caller's thread.
            var probe = await Task.Run(() => _environment.ProbeOpenRgbServer(Port, ProbeTimeout), ct).ConfigureAwait(false);
            if (probe.Reachable)
            {
                if (_process is null)
                    _adoptedExternalServer = true;
                return OperationResult<bool>.Success(true);
            }
            _adoptedExternalServer = false;

            if (BundledExecutablePath is null)
            {
                LastError = "This build ships without the bundled OpenRGB.";
                return OperationResult<bool>.Failure(LastError, ErrorCategory.NotFound);
            }

            return await StartAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            return OperationResult<bool>.Failure("The app is closing.", ErrorCategory.ServiceUnavailable);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>True when a server this app did not start answered the last check.</summary>
    public bool UsesExternalServer => _adoptedExternalServer;

    private async Task<OperationResult<bool>> StartAsync(CancellationToken cancellationToken)
    {
        StopCore();
        State = OpenRgbHostState.Starting;
        LastError = null;
        try
        {
            Directory.CreateDirectory(_configDirectory);
            var start = new ProcessStartInfo
            {
                FileName = BundledExecutablePath!,
                WorkingDirectory = Path.GetDirectoryName(BundledExecutablePath!) ?? string.Empty,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            start.ArgumentList.Add("--server");
            start.ArgumentList.Add("--server-host");
            start.ArgumentList.Add("127.0.0.1");
            start.ArgumentList.Add("--server-port");
            start.ArgumentList.Add(Port.ToString(System.Globalization.CultureInfo.InvariantCulture));
            start.ArgumentList.Add("--config");
            start.ArgumentList.Add(_configDirectory);
            start.ArgumentList.Add("--noautoconnect");
            start.ArgumentList.Add("--loglevel");
            start.ArgumentList.Add("3");

            var process = Process.Start(start)
                ?? throw new InvalidOperationException("Process.Start returned null.");
            _process = process;
            AttachToJob(process);
            Log.Info("Started bundled OpenRGB {Pid} from {Path}", process.Id, BundledExecutablePath);

            // The server listens before detection finishes, and it answers
            // the count request with whatever it has found so far. Wait for
            // the port, then for the count to hold still, so the first read
            // of the page is the whole device list.
            var deadline = DateTimeOffset.UtcNow + StartTimeout;
            OpenRgbProbeResult probe;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (process.HasExited)
                {
                    State = OpenRgbHostState.Failed;
                    LastError = $"The lighting service exited with code {process.ExitCode} while starting.";
                    return OperationResult<bool>.Failure(LastError, ErrorCategory.ServiceUnavailable);
                }
                probe = await Task.Run(() => _environment.ProbeOpenRgbServer(Port, ProbeTimeout), cancellationToken).ConfigureAwait(false);
                if (probe.Reachable)
                    break;
                if (DateTimeOffset.UtcNow >= deadline)
                {
                    State = OpenRgbHostState.Failed;
                    LastError = $"The lighting service did not answer on port {Port} within {StartTimeout.TotalSeconds:0} seconds.";
                    return OperationResult<bool>.Failure(LastError, ErrorCategory.ServiceUnavailable);
                }
                await Task.Delay(ProbeInterval, cancellationToken).ConfigureAwait(false);
            }

            var settleDeadline = DateTimeOffset.UtcNow + SettleTimeout;
            var lastCount = probe.DeviceCount;
            while (DateTimeOffset.UtcNow < settleDeadline)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
                if (process.HasExited)
                    break;
                probe = await Task.Run(() => _environment.ProbeOpenRgbServer(Port, ProbeTimeout), cancellationToken).ConfigureAwait(false);
                if (probe.Reachable && probe.DeviceCount == lastCount)
                    break;
                lastCount = probe.DeviceCount;
            }

            State = OpenRgbHostState.Running;
            Log.Info("Bundled OpenRGB answering on port {Port} with {Count} device(s)", Port, lastCount);
            return OperationResult<bool>.Success(true);
        }
        catch (OperationCanceledException)
        {
            State = OpenRgbHostState.Failed;
            LastError = "Starting the lighting service was cancelled.";
            throw;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            State = OpenRgbHostState.Failed;
            LastError = $"The lighting service could not be started: {ex.Message}";
            Log.Error(ex, "Starting bundled OpenRGB failed");
            return OperationResult<bool>.Failure(LastError, ErrorCategory.ServiceUnavailable, ex);
        }
    }

    /// <summary>Kill-on-close job: the server cannot outlive this process. A refused assignment is logged and the explicit stop still applies.</summary>
    private void AttachToJob(Process process)
    {
        if (_job == 0)
        {
            _job = NativeHardware.CreateJobObjectW(0, null);
            if (_job == 0)
            {
                Log.Warn("CreateJobObject failed (Win32 {Code}); the lighting service is not tied to this process", Marshal.GetLastPInvokeError());
                return;
            }
            var limits = new NativeHardware.JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
            limits.BasicLimitInformation.LimitFlags = NativeHardware.JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;
            if (!NativeHardware.SetInformationJobObject(_job, NativeHardware.JobObjectExtendedLimitInformation, ref limits,
                    (uint)Marshal.SizeOf<NativeHardware.JOBOBJECT_EXTENDED_LIMIT_INFORMATION>()))
            {
                Log.Warn("SetInformationJobObject failed (Win32 {Code})", Marshal.GetLastPInvokeError());
            }
        }
        if (!NativeHardware.AssignProcessToJobObject(_job, process.Handle))
            Log.Warn("AssignProcessToJobObject failed (Win32 {Code}); the lighting service is stopped explicitly on exit only", Marshal.GetLastPInvokeError());
    }

    public async Task StopAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            StopCore();
        }
        finally
        {
            _gate.Release();
        }
    }

    private void StopCore()
    {
        if (_process is { } process)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(3000);
                    Log.Info("Stopped bundled OpenRGB {Pid}", process.Id);
                }
            }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
            {
                Log.Warn(ex, "Stopping bundled OpenRGB failed");
            }
            process.Dispose();
            _process = null;
        }
        if (State is OpenRgbHostState.Running or OpenRgbHostState.Starting or OpenRgbHostState.Failed)
            State = OpenRgbHostState.Stopped;
    }

    /// <summary>
    /// Cancels a start in flight, waits for it to let go of the gate, then
    /// stops the server. The gate itself is left for the collector: a start
    /// that is still unwinding must be able to release it.
    /// </summary>
    public void Dispose()
    {
        _lifetime.Cancel();
        var held = _gate.Wait(TimeSpan.FromSeconds(5));
        try
        {
            StopCore();
        }
        finally
        {
            if (held)
                _gate.Release();
        }
        if (_job != 0)
        {
            NativeHardware.CloseHandle(_job);
            _job = 0;
        }
        _lifetime.Dispose();
    }
}
