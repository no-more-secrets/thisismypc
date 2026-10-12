using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;

namespace ThisIsMyPC.Interop.Win32.Services;

/// <summary>Hosts one Windows service with compiled callbacks that remain usable under Arbitrary Code Guard.</summary>
public sealed partial class NativeServiceHost
{
    private const uint ControlShutdown = 5;
    private const uint ControlInterrogate = 4;
    private const uint ErrorExceptionInService = 1064;
    private static NativeServiceHost? s_current;
    private readonly string _name;
    private readonly Action<Action> _run;
    private readonly Action _stop;
    private readonly NativeServiceStatus _status;
    private nint _statusHandle;
    private ExceptionDispatchInfo? _failure;

    private NativeServiceHost(string name, Action<Action> run, Action stop)
    {
        _name = name;
        _run = run;
        _stop = stop;
        _status = new(PublishStatus);
    }

    /// <summary>Connects to SCM, runs the service, and propagates any failure after the native callbacks return.</summary>
    /// <param name="serviceName">The registered service name.</param>
    /// <param name="run">Runs until shutdown. Calls its argument only after startup succeeds.</param>
    /// <param name="stop">Requests host shutdown without waiting for it.</param>
    public static unsafe void Run(string serviceName, Action<Action> run, Action stop)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(stop);
        var instance = new NativeServiceHost(serviceName, run, stop);
        if (Interlocked.CompareExchange(ref s_current, instance, null) is not null)
            throw new InvalidOperationException("A service dispatcher is already active.");
        try
        {
            fixed (char* name = serviceName)
            {
                ServiceTableEntry* entries = stackalloc ServiceTableEntry[2];
                entries[0] = new() { Name = name, Main = &ServiceMain };
                entries[1] = default;
                if (!StartServiceCtrlDispatcher(entries))
                    throw new Win32Exception(Marshal.GetLastPInvokeError(), "Cannot connect the service dispatcher.");
            }
            instance._failure?.Throw();
        }
        finally
        {
            Volatile.Write(ref s_current, null);
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static unsafe void ServiceMain(uint argumentCount, char** arguments)
    {
        var instance = Volatile.Read(ref s_current)!;
        try
        {
            instance._statusHandle = RegisterServiceCtrlHandlerEx(instance._name, &ControlHandler, 0);
            if (instance._statusHandle == 0)
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "Cannot register service controls.");
            instance._status.ReportState(NativeServiceControl.SERVICE_START_PENDING);
            instance._run(instance._status.ReportRunning);
        }
        catch (Exception ex)
        {
            instance.RecordFailure(ex);
        }
        finally
        {
            // Exceptions must not cross an unmanaged callback boundary.
            try
            {
                if (instance._statusHandle != 0)
                    instance._status.ReportState(NativeServiceControl.SERVICE_STOPPED,
                        instance._failure is null ? 0 : ErrorExceptionInService);
            }
            catch (Exception ex)
            {
                instance.RecordFailure(ex);
            }
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static uint ControlHandler(uint control, uint eventType, nint eventData, nint context)
    {
        var instance = Volatile.Read(ref s_current)!;
        try
        {
            if (control == ControlInterrogate)
            {
                instance._status.Interrogate();
                return 0;
            }
            if (control is not (NativeServiceControl.SERVICE_CONTROL_STOP or ControlShutdown))
                return 120; // ERROR_CALL_NOT_IMPLEMENTED
            if (instance._status.TryRequestStop())
            {
                // SCM serializes controls. Never block its dispatcher while workers drain.
                ThreadPool.QueueUserWorkItem(static host => host.RequestStop(), instance, preferLocal: false);
            }
            return 0;
        }
        catch (Exception ex)
        {
            instance.RecordFailure(ex);
            ThreadPool.QueueUserWorkItem(static host => host.RequestStop(), instance, preferLocal: false);
            return ErrorExceptionInService;
        }
    }

    private void RequestStop()
    {
        try { _stop(); }
        catch (Exception ex) { RecordFailure(ex); }
    }

    private void RecordFailure(Exception exception) =>
        Interlocked.CompareExchange(ref _failure, ExceptionDispatchInfo.Capture(exception), null);

    private void PublishStatus(NativeServiceControl.ServiceStatus status)
    {
        if (!SetServiceStatus(_statusHandle, in status))
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Cannot report service status.");
    }

    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct ServiceTableEntry
    {
        public char* Name;
        public delegate* unmanaged[Stdcall]<uint, char**, void> Main;
    }

    [LibraryImport("advapi32.dll", EntryPoint = "StartServiceCtrlDispatcherW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool StartServiceCtrlDispatcher(ServiceTableEntry* entries);

    [LibraryImport("advapi32.dll", EntryPoint = "RegisterServiceCtrlHandlerExW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static unsafe partial nint RegisterServiceCtrlHandlerEx(string serviceName,
        delegate* unmanaged[Stdcall]<uint, uint, nint, nint, uint> handler, nint context);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetServiceStatus(nint statusHandle, in NativeServiceControl.ServiceStatus status);
}

/// <summary>Serializes status reports and makes the first stopped report terminal, including failed reports.</summary>
internal sealed class NativeServiceStatus(Action<NativeServiceControl.ServiceStatus> publish)
{
    private readonly Lock _gate = new();
    private NativeServiceControl.ServiceStatus _status;

    internal void ReportState(uint state, uint exitCode = 0)
    {
        lock (_gate)
        {
            if (_status.dwCurrentState == NativeServiceControl.SERVICE_STOPPED) return;
            var pending = state is NativeServiceControl.SERVICE_START_PENDING or NativeServiceControl.SERVICE_STOP_PENDING;
            _status = new()
            {
                dwServiceType = 0x10, // SERVICE_WIN32_OWN_PROCESS
                dwCurrentState = state,
                dwControlsAccepted = state == NativeServiceControl.SERVICE_RUNNING ? 0x5u : 0, // STOP | SHUTDOWN
                dwWin32ExitCode = exitCode,
                dwCheckPoint = pending ? 1u : 0,
                dwWaitHint = pending ? 30000u : 0,
            };
            publish(_status);
        }
    }

    internal void ReportRunning()
    {
        lock (_gate)
        {
            if (_status.dwCurrentState == NativeServiceControl.SERVICE_START_PENDING)
                ReportState(NativeServiceControl.SERVICE_RUNNING);
        }
    }

    internal bool TryRequestStop()
    {
        lock (_gate)
        {
            if (_status.dwCurrentState is NativeServiceControl.SERVICE_STOPPED or NativeServiceControl.SERVICE_STOP_PENDING)
                return false;
            ReportState(NativeServiceControl.SERVICE_STOP_PENDING);
            return true;
        }
    }

    internal void Interrogate()
    {
        lock (_gate)
        {
            if (_status.dwCurrentState != NativeServiceControl.SERVICE_STOPPED)
                publish(_status);
        }
    }
}
