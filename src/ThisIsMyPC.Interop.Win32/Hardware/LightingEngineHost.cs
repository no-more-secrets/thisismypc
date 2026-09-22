using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading.Channels;
using ThisIsMyPC.Core.Hardware.Lighting;
using ThisIsMyPC.Core.Results;

namespace ThisIsMyPC.Interop.Win32.Hardware;

/// <summary>
/// Runs the bundled lighting engine (ThisIsMyPC-LightingEngine.exe) as a child
/// process: loopback SDK port chosen here, configuration under the app's data
/// folder, no window, and a kill-on-close job so the engine never outlives the
/// app. The engine announces "ready &lt;port&gt;" on stdout once its devices are
/// detected and its server listens; "rescan" on stdin makes it detect again and
/// answer "detected"; closing stdin or "stop" shuts it down.
/// </summary>
public sealed partial class LightingEngineHost : ILightingEngine, IDisposable
{
    private const string EngineFileName = "ThisIsMyPC-LightingEngine.exe";
    private const int NoteCapacity = 40;
    private static readonly TimeSpan StartTimeout = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan RescanTimeout = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan StopGrace = TimeSpan.FromSeconds(5);
    private static readonly NLog.Logger Log = NLog.LogManager.GetLogger("ThisIsMyPC.Interop.Win32.LightingEngine");

    private readonly string _configDirectory;
    private readonly string? _enginePath;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Queue<string> _notes = new();
    private readonly object _notesLock = new();
    private Process? _process;
    private Channel<string>? _lines;
    private nint _job;
    private int _port;
    private string? _authenticationToken;
    private bool _disposed;

    public LightingEngineHost(string configDirectory, string? enginePath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configDirectory);
        _configDirectory = configDirectory;
        _enginePath = enginePath ?? LocateEngine();
    }

    public bool IsAvailable => _enginePath is not null;

    public IReadOnlyList<string> Notes
    {
        get
        {
            lock (_notesLock)
                return _notes.ToList();
        }
    }

    /// <summary>
    /// The engine ships beside the app in lighting-engine\. Debug builds also
    /// accept the repository's build output so the app runs from Visual Studio
    /// without packaging; Release never walks the disk for an executable.
    /// </summary>
    public static string? LocateEngine()
    {
        var installed = Path.Combine(AppContext.BaseDirectory, "lighting-engine", EngineFileName);
        if (File.Exists(installed))
            return installed;
#if DEBUG
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "artifacts", "lighting-engine", "Release", EngineFileName);
            if (File.Exists(candidate))
                return candidate;
        }
#endif
        return null;
    }

    public async Task<OperationResult<LightingEngineEndpoint>> StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_process is { HasExited: false })
                return OperationResult<LightingEngineEndpoint>.Success(new(_port, _authenticationToken!));
            ReleaseProcess();
            if (_enginePath is null)
                return OperationResult<LightingEngineEndpoint>.Failure("This build has no lighting engine.", ErrorCategory.ServiceUnavailable);

            Directory.CreateDirectory(_configDirectory);
            var port = FindFreePort();
            var authenticationToken = RandomNumberGenerator.GetHexString(64);
            var startInfo = new ProcessStartInfo(_enginePath)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = Path.GetDirectoryName(_enginePath)!,
            };
            startInfo.ArgumentList.Add("--config");
            startInfo.ArgumentList.Add(_configDirectory);
            startInfo.ArgumentList.Add("--server-host");
            startInfo.ArgumentList.Add("127.0.0.1");
            startInfo.ArgumentList.Add("--server-port");
            startInfo.ArgumentList.Add(port.ToString(System.Globalization.CultureInfo.InvariantCulture));
            startInfo.ArgumentList.Add("--loglevel");
            startInfo.ArgumentList.Add("4");

            var lines = Channel.CreateUnbounded<string>();
            Process process;
            try
            {
                process = Process.Start(startInfo) ?? throw new InvalidOperationException("Process.Start returned null.");
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
            {
                Log.Warn(ex, "Lighting engine did not start");
                return OperationResult<LightingEngineEndpoint>.Failure($"The lighting engine could not be started: {ex.Message}", ErrorCategory.ServiceUnavailable, ex);
            }

            _job = NativeJobObject.Adopt(process.Handle);
            _process = process;
            try
            {
                await process.StandardInput.WriteLineAsync("auth " + authenticationToken).ConfigureAwait(false);
                await process.StandardInput.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                Stop();
                throw;
            }
            process.OutputDataReceived += (_, args) =>
            {
                if (args.Data is null)
                    return;
                Note(args.Data);
                lines.Writer.TryWrite(args.Data);
            };
            process.ErrorDataReceived += (_, args) =>
            {
                if (args.Data is not null)
                    Note(args.Data);
            };
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            _authenticationToken = authenticationToken;
            _lines = lines;

            var announced = await WaitForLineAsync(line => line.StartsWith("ready ", StringComparison.Ordinal) || line.StartsWith("error ", StringComparison.Ordinal),
                StartTimeout, cancellationToken).ConfigureAwait(false);
            if (announced is null || announced.StartsWith("error ", StringComparison.Ordinal))
            {
                var reason = announced?[6..] ?? (process.HasExited ? $"it exited with code {process.ExitCode}" : "it did not answer in time");
                Log.Warn("Lighting engine failed: {Reason}; last lines: {Lines}", reason, string.Join(" | ", Notes.TakeLast(5)));
                Stop();
                return OperationResult<LightingEngineEndpoint>.Failure($"The lighting engine did not start: {reason}.", ErrorCategory.ServiceUnavailable);
            }

            if (!int.TryParse(announced.AsSpan(6), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out _port))
                _port = port;
            Log.Info("Lighting engine serving on 127.0.0.1:{Port}", _port);
            return OperationResult<LightingEngineEndpoint>.Success(new(_port, _authenticationToken!));
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<OperationResult<bool>> RescanAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_process is not { HasExited: false } process)
                return OperationResult<bool>.Failure("The lighting engine is not running.", ErrorCategory.ServiceUnavailable);
            await process.StandardInput.WriteLineAsync("rescan").ConfigureAwait(false);
            await process.StandardInput.FlushAsync(cancellationToken).ConfigureAwait(false);
            var done = await WaitForLineAsync(line => line.StartsWith("detected", StringComparison.Ordinal), RescanTimeout, cancellationToken).ConfigureAwait(false);
            return done is null
                ? OperationResult<bool>.Failure("The lighting engine did not finish detecting devices.", ErrorCategory.ServiceUnavailable)
                : OperationResult<bool>.Success(true);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Asks the engine to shut down and ends it when it does not.</summary>
    public void Stop()
    {
        var process = _process;
        if (process is null)
            return;
        try
        {
            if (!process.HasExited)
            {
                try
                {
                    process.StandardInput.WriteLine("stop");
                    process.StandardInput.Flush();
                }
                catch (IOException) { }
                if (!process.WaitForExit((int)StopGrace.TotalMilliseconds))
                    process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException) { }
        finally
        {
            ReleaseProcess();
        }
    }

    private void ReleaseProcess()
    {
        _process?.Dispose();
        _process = null;
        _authenticationToken = null;
        _lines?.Writer.TryComplete();
        _lines = null;
        NativeJobObject.Close(_job);
        _job = nint.Zero;
    }

    private async Task<string?> WaitForLineAsync(Func<string, bool> match, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var lines = _lines;
        var process = _process;
        if (lines is null || process is null)
            return null;
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        try
        {
            while (await lines.Reader.WaitToReadAsync(timeoutSource.Token).ConfigureAwait(false))
            {
                while (lines.Reader.TryRead(out var line))
                {
                    if (match(line))
                        return line;
                }
                if (process.HasExited && lines.Reader.Count == 0)
                    return null;
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        return null;
    }

    /// <summary>
    /// Keeps the engine's plain log lines for the tab's evidence: no protocol
    /// lines, no HTML (OpenRGB's I2C warning is a dialog body), and a line that
    /// repeats one per bus is kept once.
    /// </summary>
    private void Note(string line)
    {
        // OpenRGB 1.0 prefixes "[   179][Error  ]"; the tab wants the message, deduplicated across timestamps.
        var trimmed = LogPrefix().Replace(line, string.Empty).Trim();
        if (trimmed.Length == 0 || trimmed.StartsWith('<')
            || trimmed.StartsWith("ready ", StringComparison.Ordinal) || trimmed.StartsWith("detected", StringComparison.Ordinal))
            return;
        lock (_notesLock)
        {
            if (_notes.Contains(trimmed))
                return;
            _notes.Enqueue(trimmed);
            while (_notes.Count > NoteCapacity)
                _notes.Dequeue();
        }
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"^\s*\[\s*\d+\s*\]\[[A-Za-z ]+\]")]
    private static partial System.Text.RegularExpressions.Regex LogPrefix();

    private static int FindFreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        Stop();
        _gate.Dispose();
    }
}
