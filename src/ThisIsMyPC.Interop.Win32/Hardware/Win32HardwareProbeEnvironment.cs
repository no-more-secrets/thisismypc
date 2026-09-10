using System.Diagnostics;
using System.Net.Sockets;
using ThisIsMyPC.Core.Hardware.Detection;

namespace ThisIsMyPC.Interop.Win32.Hardware;

/// <summary>
/// The live machine behind the Hardware tabs' own probes: file system,
/// process list and the OpenRGB SDK socket. Firmware, platform role and
/// battery come from the shared <see cref="HardwareDetectionService"/>. Every
/// member is a read. Failures come back as null or an unreachable result,
/// never as exceptions, so detection degrades to "not observed".
/// </summary>
public sealed class Win32HardwareProbeEnvironment : IHardwareProbeEnvironment
{
    public ProbeFolders Folders { get; } = new(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));

    public bool FileExists(string path) => Guard(() => File.Exists(path), false);

    public bool DirectoryExists(string path) => Guard(() => Directory.Exists(path), false);

    public IReadOnlyList<string> EnumerateDirectories(string parent, string pattern) =>
        Guard<IReadOnlyList<string>>(
            () => Directory.Exists(parent) ? Directory.GetDirectories(parent, pattern) : [],
            []);

    public IReadOnlyList<string> EnumerateFiles(string directory, string pattern) =>
        Guard<IReadOnlyList<string>>(
            () => Directory.Exists(directory) ? Directory.GetFiles(directory, pattern) : [],
            []);

    /// <summary>
    /// Opens each candidate with query-limited access, which works across the
    /// elevation boundary (the unelevated UI can read an elevated FanControl's
    /// path), so a running companion is never mistaken for an absent one.
    /// </summary>
    public string? RunningProcessPath(string processName)
    {
        Process[] processes;
        try
        {
            processes = Process.GetProcessesByName(processName);
        }
        catch (InvalidOperationException)
        {
            return null;
        }

        if (processes.Length == 0)
            return null;

        try
        {
            foreach (var process in processes)
            {
                var path = QueryImagePath((uint)process.Id);
                if (!string.IsNullOrEmpty(path))
                    return path;
            }
            return string.Empty;
        }
        finally
        {
            foreach (var process in processes)
                process.Dispose();
        }
    }

    private static string? QueryImagePath(uint processId)
    {
        var handle = NativeHardware.OpenProcess(NativeHardware.PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
        if (handle == 0)
            return null;
        try
        {
            Span<char> buffer = stackalloc char[1024];
            var size = (uint)buffer.Length;
            return NativeHardware.QueryFullProcessImageNameW(handle, 0, buffer, ref size)
                ? new string(buffer[..(int)size])
                : null;
        }
        finally
        {
            NativeHardware.CloseHandle(handle);
        }
    }

    /// <summary>
    /// Connects to the SDK server, announces a client name (never answered),
    /// then asks for the controller count and waits for the reply. A refused
    /// connection means no server is listening; a timeout means something
    /// listens but did not answer the SDK request.
    /// </summary>
    public OpenRgbProbeResult ProbeOpenRgbServer(int port, TimeSpan timeout)
    {
        try
        {
            using var client = new TcpClient();
            // A cancelled connect is observed here, so a silent port never
            // leaves a faulted task behind after the client is disposed.
            using (var connectTimeout = new CancellationTokenSource(timeout))
            {
                client.ConnectAsync("127.0.0.1", port, connectTimeout.Token).AsTask().GetAwaiter().GetResult();
            }

            using var stream = client.GetStream();
            stream.ReadTimeout = (int)timeout.TotalMilliseconds;
            stream.WriteTimeout = (int)timeout.TotalMilliseconds;
            stream.Write(OpenRgbSdkProtocol.BuildSetClientName("ThisIsMyPC"));
            stream.Write(OpenRgbSdkProtocol.BuildControllerCountRequest());

            // The server may push a device-list notification before it
            // answers; skip a few of those, then insist on the count reply.
            var header = new byte[OpenRgbSdkProtocol.HeaderLength];
            OpenRgbSdkProtocol.PacketHeader parsed;
            for (var skipped = 0; ; skipped++)
            {
                if (!ReadExactly(stream, header))
                    return OpenRgbProbeResult.Unreachable($"port {port} accepted the connection but sent no SDK header");
                if (!OpenRgbSdkProtocol.TryParseHeader(header, out parsed))
                    return OpenRgbProbeResult.Unreachable($"port {port} answered with a non-OpenRGB header");
                if (parsed.PacketId == OpenRgbSdkProtocol.DeviceListUpdated && parsed.PayloadLength <= 64 && skipped < 4)
                {
                    if (parsed.PayloadLength > 0 && !ReadExactly(stream, new byte[parsed.PayloadLength]))
                        return OpenRgbProbeResult.Unreachable("device-list notification was truncated");
                    continue;
                }
                break;
            }
            if (parsed.PacketId != OpenRgbSdkProtocol.RequestControllerCount || parsed.PayloadLength > 64)
                return OpenRgbProbeResult.Unreachable($"unexpected SDK reply (id {parsed.PacketId}, {parsed.PayloadLength} bytes)");

            var payload = new byte[parsed.PayloadLength];
            if (!ReadExactly(stream, payload) || !OpenRgbSdkProtocol.TryParseUInt32Payload(payload, out var count))
                return OpenRgbProbeResult.Unreachable("controller count reply was malformed");

            var deviceCount = count > int.MaxValue ? -1 : (int)count;
            return new OpenRgbProbeResult(true, deviceCount, $"answered on port {port} with {count} controller(s)");
        }
        catch (OperationCanceledException)
        {
            return OpenRgbProbeResult.Unreachable($"no connection to port {port} within {timeout.TotalMilliseconds:0} ms");
        }
        catch (Exception ex) when (ex is SocketException or IOException or AggregateException or ObjectDisposedException)
        {
            var inner = ex is AggregateException aggregate ? aggregate.InnerException ?? ex : ex;
            return OpenRgbProbeResult.Unreachable(inner is SocketException socket
                ? $"port {port}: {socket.SocketErrorCode}"
                : $"port {port}: {inner.GetType().Name}");
        }
    }

    public string? BundledCompanionExecutable(Core.Hardware.CompanionApp app) => BundledCompanionLocator.Find(app);

    private static bool ReadExactly(NetworkStream stream, byte[] buffer)
    {
        var read = 0;
        while (read < buffer.Length)
        {
            var chunk = stream.Read(buffer, read, buffer.Length - read);
            if (chunk <= 0)
                return false;
            read += chunk;
        }
        return true;
    }

    private static T Guard<T>(Func<T> read, T fallback)
    {
        try
        {
            return read();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return fallback;
        }
    }
}
