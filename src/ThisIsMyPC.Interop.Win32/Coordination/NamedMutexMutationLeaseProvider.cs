using System.Runtime.InteropServices;
using NLog;
using ThisIsMyPC.Core.Coordination;

namespace ThisIsMyPC.Interop.Win32.Coordination;

/// <summary>
/// The machine-wide mutation lease over a named kernel mutex.
///
/// Thread rule: a Windows mutex belongs to the thread that acquired it and only
/// that thread may release it, so a lease must never be released from whatever
/// thread an async continuation lands on. Each acquisition therefore starts one
/// dedicated owner thread that creates or opens the object, verifies its
/// security, waits, and then parks until the lease is disposed; the release
/// call runs on that same thread. The caller only awaits a task and disposes a
/// handle, from any thread. WAIT_ABANDONED (an owner that died while another
/// handle kept the object alive) is surfaced as a diagnostic only. It cannot
/// gate recovery: a release without recovery consumes the flag, and a sole
/// holder that crashes destroys the object so the next acquisition creates a
/// fresh one. Every lease therefore starts unable to write and the holder
/// must run recovery and call MarkRecovered before its first write.
///
/// Trust rule: the object is created with a protected DACL admitting only
/// SYSTEM and Administrators. Whether the create call made the object or opened
/// an existing one, its owner and DACL are read back and must match exactly;
/// anything else is <see cref="MutationLeaseOutcome.Refused"/>. There is no
/// fallback descriptor.
/// </summary>
public sealed class NamedMutexMutationLeaseProvider : IMutationLeaseProvider
{
    /// <summary>Protected DACL: full access for SYSTEM and Administrators, nobody else.</summary>
    public const string RequiredSddl = "D:P(A;;GA;;;SY)(A;;GA;;;BA)";

    /// <summary>Longest wait accepted; the wait must be finite.</summary>
    public static readonly TimeSpan MaxWait = TimeSpan.FromMilliseconds(int.MaxValue - 1);

    private static readonly TimeSpan ReleaseTimeout = TimeSpan.FromSeconds(10);

    private readonly ILogger _logger;

    public NamedMutexMutationLeaseProvider(string name, string ownerLabel, ILogger? logger = null)
    {
        if (!MutationLeaseNames.IsValid(name))
            throw new ArgumentException($"'{name}' is not a valid Global\\ or Local\\ lock name.", nameof(name));
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerLabel);

        Name = name;
        OwnerLabel = ownerLabel;
        _logger = logger ?? LogManager.GetLogger("ThisIsMyPC.Interop.Win32.Coordination.NamedMutexMutationLeaseProvider");
    }

    public string Name { get; }

    public string OwnerLabel { get; }

    public Task<MutationLeaseResult> AcquireAsync(TimeSpan maxWait, CancellationToken cancellationToken = default)
    {
        if (maxWait < TimeSpan.Zero || maxWait > MaxWait)
            throw new ArgumentOutOfRangeException(nameof(maxWait), maxWait, "The wait must be finite and non-negative.");

        if (cancellationToken.IsCancellationRequested)
            return Task.FromResult(MutationLeaseResult.Cancelled());

        var session = new OwnerThreadSession(Name, OwnerLabel, maxWait, _logger, cancellationToken);
        session.Start();
        return session.Acquisition;
    }

    /// <summary>
    /// One acquisition attempt and, when granted, the lifetime of the held lease.
    /// Every kernel call happens on the owner thread this session starts.
    /// </summary>
    private sealed class OwnerThreadSession
    {
        private const int OwnerThreadStackBytes = 256 * 1024;

        private readonly string _name;
        private readonly string _ownerLabel;
        private readonly TimeSpan _maxWait;
        private readonly CancellationToken _cancellationToken;
        private readonly ILogger _logger;

        private readonly TaskCompletionSource<MutationLeaseResult> _acquisition =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _released =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly ManualResetEventSlim _release = new(false);

        private readonly Lock _cancelGate = new();
        private nint _cancelEvent;
        private bool _cancelEventClosed;

        internal OwnerThreadSession(
            string name, string ownerLabel, TimeSpan maxWait, ILogger logger, CancellationToken cancellationToken)
        {
            _name = name;
            _ownerLabel = ownerLabel;
            _maxWait = maxWait;
            _cancellationToken = cancellationToken;
            _logger = logger;
        }

        internal Task<MutationLeaseResult> Acquisition => _acquisition.Task;

        internal void Start()
        {
            var thread = new Thread(Run, OwnerThreadStackBytes)
            {
                IsBackground = true,
                Name = "ThisIsMyPC mutation lease owner",
            };
            thread.Start();
        }

        internal void ReleaseBlocking()
        {
            _release.Set();
            if (!_released.Task.Wait(ReleaseTimeout))
                _logger.Error("Mutation lease '{Name}' owner thread did not release within {Timeout}", _name, ReleaseTimeout);
        }

        internal async ValueTask ReleaseAsync()
        {
            _release.Set();
            try
            {
                await _released.Task.WaitAsync(ReleaseTimeout).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                _logger.Error("Mutation lease '{Name}' owner thread did not release within {Timeout}", _name, ReleaseTimeout);
            }
        }

        private void Run()
        {
            nint securityDescriptor = 0;
            nint mutex = 0;
            CancellationTokenRegistration registration = default;
            bool granted = false;

            try
            {
                if (!NativeMutationLease.ConvertStringSecurityDescriptorToSecurityDescriptorW(
                        RequiredSddl, 1 /* SDDL_REVISION_1 */, out securityDescriptor, out _))
                {
                    int sddlError = Marshal.GetLastPInvokeError();
                    Complete(MutationLeaseResult.Refused(
                        $"Security descriptor for lock '{_name}' could not be built (win32={sddlError})", sddlError));
                    return;
                }

                var attributes = new NativeMutationLease.SecurityAttributes
                {
                    Length = (uint)Marshal.SizeOf<NativeMutationLease.SecurityAttributes>(),
                    SecurityDescriptor = securityDescriptor,
                    InheritHandle = 0,
                };

                mutex = NativeMutationLease.CreateMutexExW(
                    in attributes, _name, 0 /* no initial owner */, NativeMutationLease.MutexAllAccess);
                int createError = Marshal.GetLastPInvokeError();
                if (mutex == 0)
                {
                    Complete(MapCreateFailure(createError));
                    return;
                }

                bool existed = createError == NativeMutationLease.ErrorAlreadyExists;
                string? problem = MutexSecurityVerifier.FindTrustProblem(mutex);
                if (problem is not null)
                {
                    string origin = existed ? "Existing" : "Newly created";
                    _logger.Warn("{Origin} lock object '{Name}' refused: {Problem}", origin, _name, problem);
                    Complete(MutationLeaseResult.Refused($"{origin} lock object '{_name}' refused: {problem}"));
                    return;
                }

                nint cancelEvent = NativeMutationLease.CreateEventW(0, bManualReset: true, bInitialState: false, null);
                if (cancelEvent == 0)
                {
                    int eventError = Marshal.GetLastPInvokeError();
                    Complete(MutationLeaseResult.Faulted(
                        $"Cancellation event for lock '{_name}' could not be created (win32={eventError})", eventError));
                    return;
                }

                lock (_cancelGate)
                {
                    _cancelEvent = cancelEvent;
                }

                registration = _cancellationToken.Register(static state => ((OwnerThreadSession)state!).SignalCancel(), this);

                // WaitForMultipleObjects returns the lowest signalled index, so a free
                // mutex would win over an already-set cancel event. Check the token first.
                if (_cancellationToken.IsCancellationRequested)
                {
                    Complete(MutationLeaseResult.Cancelled());
                    return;
                }

                uint wait = NativeMutationLease.WaitForMultipleObjects(
                    2, [mutex, cancelEvent], bWaitAll: false, (uint)_maxWait.TotalMilliseconds);

                bool abandoned;
                switch (wait)
                {
                    case NativeMutationLease.WaitObject0:
                        abandoned = false;
                        break;
                    case NativeMutationLease.WaitAbandoned0:
                        abandoned = true;
                        _logger.Warn("Lock '{Name}' was reported abandoned by the kernel (diagnostic; recovery is required for every lease)", _name);
                        break;
                    case NativeMutationLease.WaitObject0 + 1:
                        Complete(MutationLeaseResult.Cancelled());
                        return;
                    case NativeMutationLease.WaitTimeout:
                        Complete(MutationLeaseResult.TimedOut(_maxWait));
                        return;
                    default:
                        int waitError = Marshal.GetLastPInvokeError();
                        Complete(MutationLeaseResult.Faulted(
                            $"Waiting for lock '{_name}' failed (wait=0x{wait:X}, win32={waitError})", waitError));
                        return;
                }

                // The mutex may have been granted in the same instant the token was
                // cancelled. Never publish a lease the caller no longer wants: give the
                // mutex back on this thread (we own it) and report cancellation.
                if (_cancellationToken.IsCancellationRequested)
                {
                    if (!NativeMutationLease.ReleaseMutex(mutex))
                        _logger.Error("ReleaseMutex after cancelled acquisition failed for lock '{Name}' (win32={Error})", _name, Marshal.GetLastPInvokeError());
                    if (abandoned)
                        _logger.Warn("Lock '{Name}' abandoned flag was consumed by a cancelled acquisition; recovery remains mandatory for the next holder", _name);
                    Complete(MutationLeaseResult.Cancelled());
                    return;
                }

                granted = true;
                Complete(MutationLeaseResult.Held(new NamedMutexMutationLease(this, _name, _ownerLabel, abandoned)));

                // Park here, still on the owning thread, until the lease is disposed.
                _release.Wait();

                if (!NativeMutationLease.ReleaseMutex(mutex))
                {
                    _logger.Error("ReleaseMutex failed for lock '{Name}' (win32={Error})", _name, Marshal.GetLastPInvokeError());
                }
            }
#pragma warning disable CA1031 // The owner thread must never take the process down; the result carries the failure.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                _logger.Error(ex, "Mutation lease owner thread for '{Name}' failed", _name);
                if (!granted)
                    Complete(MutationLeaseResult.Faulted($"Mutation lease owner thread failed: {ex.Message}", null, ex));
            }
            finally
            {
                registration.Dispose();

                lock (_cancelGate)
                {
                    _cancelEventClosed = true;
                    if (_cancelEvent != 0)
                    {
                        NativeMutationLease.CloseHandle(_cancelEvent);
                        _cancelEvent = 0;
                    }
                }

                if (mutex != 0)
                    NativeMutationLease.CloseHandle(mutex);
                if (securityDescriptor != 0)
                    Security.NativeSecurity.LocalFree(securityDescriptor);

                _acquisition.TrySetResult(MutationLeaseResult.Faulted("Mutation lease owner thread exited without a result"));
                _released.TrySetResult();
                // Set() has already happened (or never will); nothing signals the gate after this point.
                _release.Dispose();
            }
        }

        private void Complete(MutationLeaseResult result) => _acquisition.TrySetResult(result);

        private void SignalCancel()
        {
            lock (_cancelGate)
            {
                if (!_cancelEventClosed && _cancelEvent != 0)
                    NativeMutationLease.SetEvent(_cancelEvent);
            }
        }

        private MutationLeaseResult MapCreateFailure(int error) => error switch
        {
            NativeMutationLease.ErrorAccessDenied => MutationLeaseResult.Refused(
                $"Lock '{_name}' exists and denies access; refusing to coordinate behind an object we cannot verify (win32={error})", error),
            NativeMutationLease.ErrorInvalidHandle => MutationLeaseResult.Refused(
                $"Name '{_name}' is held by a kernel object that is not a mutex (win32={error})", error),
            NativeMutationLease.ErrorPrivilegeNotHeld => MutationLeaseResult.Faulted(
                $"This process lacks the privilege to create lock '{_name}' (win32={error})", error),
            _ => MutationLeaseResult.Faulted($"CreateMutexEx failed for lock '{_name}' (win32={error})", error),
        };
    }

    private sealed class NamedMutexMutationLease : MutationLeaseBase
    {
        private readonly OwnerThreadSession _session;

        internal NamedMutexMutationLease(OwnerThreadSession session, string name, string ownerLabel, bool abandoned)
            : base(name, ownerLabel, abandoned)
        {
            _session = session;
        }

        protected override void ReleaseCore() => _session.ReleaseBlocking();

        protected override ValueTask ReleaseCoreAsync() => _session.ReleaseAsync();
    }
}
