using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using ThisIsMyPC.Core.Coordination;
using ThisIsMyPC.Interop.Win32.Coordination;

namespace ThisIsMyPC.Security.Tests.Coordination;

/// <summary>
/// Live kernel-object tests for the mutation lease. Every test takes a fresh
/// <c>Local\</c> name from <see cref="MutationLeaseNames.ForTest"/>; the
/// production name is never touched. The lock's DACL admits only SYSTEM and
/// Administrators and the verifier requires one of them as owner, so an
/// unelevated process is refused on the object it just created (tested), and a
/// second open of an existing object is access-checked. Tests named
/// <c>Elevated_*</c> therefore return early unelevated; the sentinel test fails
/// to say so (xunit 2.9.3 has no dynamic skip). Run the Integration category
/// elevated for the full set.
/// </summary>
[Trait("Category", "Integration")]
public sealed class NamedMutexMutationLeaseTests
{
    private static readonly TimeSpan Short = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan Long = TimeSpan.FromSeconds(30);

    private static bool IsElevated
    {
        get
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
    }

    private static NamedMutexMutationLeaseProvider Provider(string name, string label = "test")
        => new(name, label);

    /// <summary>
    /// xunit 2.9.3 has no dynamic skip, so the elevation-gated tests return early
    /// and this test fails to say that they did. Their names all start with
    /// "Elevated_".
    /// </summary>
    [Fact]
    public void TestProcess_IsElevated_OtherwiseTheElevatedTestsReturnedEarly()
    {
        Assert.True(IsElevated,
            "The test process is not elevated. The tests in this class whose names start " +
            "with 'Elevated_' returned early without asserting, because a second open of " +
            "the lock object is access-checked against a SYSTEM/Administrators DACL. The " +
            "other tests in this class ran normally. Run the Integration category elevated.");
    }

    [Fact]
    public async Task Unelevated_FreshObject_IsRefused_BecauseTheOwnerIsNotAdminOrSystem()
    {
        if (IsElevated) return;
        string name = MutationLeaseNames.ForTest();

        var result = await Provider(name, "app").AcquireAsync(Short);

        Assert.Equal(MutationLeaseOutcome.Refused, result.Outcome);
        Assert.Null(result.Lease);
        Assert.Contains("owner", result.ErrorMessage, StringComparison.Ordinal);
        Assert.False(Mutex.TryOpenExisting(name, out _));
    }

    [Fact]
    public async Task Elevated_Acquire_ThenDispose_GrantsLease_ThatNeedsClearance_AndFreesTheObject()
    {
        if (!IsElevated) return;
        string name = MutationLeaseNames.ForTest();

        var result = await Provider(name, "app").AcquireAsync(Short);

        Assert.Equal(MutationLeaseOutcome.Acquired, result.Outcome);
        Assert.False(result.Lease!.WasAbandoned);
        Assert.True(result.Lease.RequiresRecovery);
        Assert.False(result.CanWrite);
        Assert.Equal(name, result.Lease.Name);
        Assert.Equal("app", result.Lease.OwnerLabel);
        Assert.True(Mutex.TryOpenExisting(name, out var probe));
        probe!.Dispose();

        result.Lease.MarkRecovered();
        Assert.True(result.CanWrite);

        result.Lease.Dispose();

        Assert.False(result.Lease.IsHeld);
        Assert.False(result.CanWrite);
        Assert.False(Mutex.TryOpenExisting(name, out _));
    }

    [Fact]
    public async Task Elevated_ReleaseWithoutClearance_LeavesTheNextHolderUncleared()
    {
        if (!IsElevated) return;
        string name = MutationLeaseNames.ForTest();

        // First holder never calls MarkRecovered and releases cleanly.
        var first = await Provider(name, "app").AcquireAsync(Short);
        Assert.Equal(MutationLeaseOutcome.Acquired, first.Outcome);
        Assert.False(first.CanWrite);
        first.Lease!.Dispose();

        var second = await Provider(name, "service").AcquireAsync(Short);

        Assert.Equal(MutationLeaseOutcome.Acquired, second.Outcome);
        Assert.False(second.Lease!.WasAbandoned);
        Assert.True(second.Lease.RequiresRecovery);
        Assert.False(second.CanWrite);
        second.Lease.Dispose();
    }

    [Fact]
    public async Task Elevated_SoleHolderCrash_DestroysTheObject_AndTheFreshMutexLooksClean()
    {
        if (!IsElevated) return;
        string name = MutationLeaseNames.ForTest();

        // Stage the object with our DACL, then let a raw thread take it as the sole
        // handle holder and close that handle while owning it (a crashed process
        // does the same: its handles close, the object is destroyed).
        var keeper = await Provider(name, "app").AcquireAsync(Short);
        Assert.Equal(MutationLeaseOutcome.Acquired, keeper.Outcome);

        using var rawOpened = new ManualResetEventSlim(false);
        using var rawOwned = new ManualResetEventSlim(false);
        var crashing = new Thread(() =>
        {
            using var raw = Mutex.OpenExisting(name);
            rawOpened.Set();
            raw.WaitOne();
            rawOwned.Set();
            // Dispose closes the last handle while owned; no ReleaseMutex.
        })
        { IsBackground = true };

        crashing.Start();
        // The raw handle must exist before the keeper releases, or the object vanishes early.
        Assert.True(rawOpened.Wait(TimeSpan.FromSeconds(10)));
        keeper.Lease!.Dispose();
        Assert.True(rawOwned.Wait(TimeSpan.FromSeconds(10)));
        crashing.Join();
        Assert.False(Mutex.TryOpenExisting(name, out _));

        var next = await Provider(name, "service").AcquireAsync(Short);

        Assert.Equal(MutationLeaseOutcome.Acquired, next.Outcome);
        Assert.False(next.Lease!.WasAbandoned);
        Assert.True(next.Lease.RequiresRecovery);
        Assert.False(next.CanWrite);
        next.Lease.Dispose();
    }

    [Fact]
    public async Task Elevated_SecondInstance_TimesOutWhileHeld_AndAcquiresAfterRelease()
    {
        if (!IsElevated) return;
        string name = MutationLeaseNames.ForTest();
        var app = Provider(name, "app");
        var service = Provider(name, "service");

        var held = await app.AcquireAsync(Short);
        Assert.Equal(MutationLeaseOutcome.Acquired, held.Outcome);

        var sw = Stopwatch.StartNew();
        var blocked = await service.AcquireAsync(Short);
        sw.Stop();

        Assert.Equal(MutationLeaseOutcome.TimedOut, blocked.Outcome);
        Assert.Null(blocked.Lease);
        Assert.InRange(sw.Elapsed, Short - TimeSpan.FromMilliseconds(50), TimeSpan.FromSeconds(10));

        var probe = await service.AcquireAsync(TimeSpan.Zero);
        Assert.Equal(MutationLeaseOutcome.TimedOut, probe.Outcome);

        held.Lease!.Dispose();

        var granted = await service.AcquireAsync(Short);
        Assert.Equal(MutationLeaseOutcome.Acquired, granted.Outcome);
        granted.Lease!.Dispose();
    }

    [Fact]
    public async Task Elevated_SameProvider_SecondLease_IsExcluded()
    {
        if (!IsElevated) return;
        var provider = Provider(MutationLeaseNames.ForTest());

        var first = await provider.AcquireAsync(Short);
        var second = await provider.AcquireAsync(Short);

        Assert.Equal(MutationLeaseOutcome.Acquired, first.Outcome);
        Assert.Equal(MutationLeaseOutcome.TimedOut, second.Outcome);
        first.Lease!.Dispose();
    }

    [Fact]
    public async Task Elevated_Cancellation_DuringWait_ReturnsCancelledPromptly()
    {
        if (!IsElevated) return;
        string name = MutationLeaseNames.ForTest();
        var holder = await Provider(name).AcquireAsync(Short);
        Assert.Equal(MutationLeaseOutcome.Acquired, holder.Outcome);

        using var cts = new CancellationTokenSource();
        var sw = Stopwatch.StartNew();
        var waiting = Provider(name).AcquireAsync(Long, cts.Token);
        Assert.False(waiting.IsCompleted);
        cts.CancelAfter(100);
        var result = await waiting;
        sw.Stop();

        Assert.Equal(MutationLeaseOutcome.Cancelled, result.Outcome);
        Assert.Null(result.Lease);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(10), $"took {sw.Elapsed}");

        holder.Lease!.Dispose();
        var after = await Provider(name).AcquireAsync(Short);
        Assert.Equal(MutationLeaseOutcome.Acquired, after.Outcome);
        after.Lease!.Dispose();
    }

    [Fact]
    public async Task PreCancelledToken_ReturnsCancelled_WithoutTouchingTheObject()
    {
        string name = MutationLeaseNames.ForTest();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var result = await Provider(name).AcquireAsync(Long, cts.Token);

        Assert.Equal(MutationLeaseOutcome.Cancelled, result.Outcome);
        Assert.False(Mutex.TryOpenExisting(name, out _));
    }

    [Fact]
    public async Task Elevated_AbandonedByPreviousHolder_IsReported_WithTheSameClearanceGate()
    {
        if (!IsElevated) return;
        string name = MutationLeaseNames.ForTest();

        // Keep the object alive with our DACL while a raw thread takes it and dies holding it.
        var keeper = await Provider(name, "app").AcquireAsync(Short);
        Assert.Equal(MutationLeaseOutcome.Acquired, keeper.Outcome);

        Mutex? rawHandle = null;
        using var rawOpened = new ManualResetEventSlim(false);
        using var rawAcquired = new ManualResetEventSlim(false);
        var dying = new Thread(() =>
        {
            rawHandle = Mutex.OpenExisting(name);
            rawOpened.Set();
            rawHandle.WaitOne();
            rawAcquired.Set();
            // Exit without ReleaseMutex; the handle stays open so the object survives.
        })
        { IsBackground = true };

        dying.Start();
        // The raw handle must exist before the keeper releases, or the object vanishes.
        Assert.True(rawOpened.Wait(TimeSpan.FromSeconds(10)));
        keeper.Lease!.Dispose();
        Assert.True(rawAcquired.Wait(TimeSpan.FromSeconds(10)));
        dying.Join();

        var recovered = await Provider(name, "service").AcquireAsync(Short);

        Assert.Equal(MutationLeaseOutcome.AcquiredAbandoned, recovered.Outcome);
        Assert.True(recovered.Lease!.WasAbandoned);
        Assert.True(recovered.Lease.RequiresRecovery);
        Assert.False(recovered.CanWrite);
        recovered.Lease.MarkRecovered();
        Assert.True(recovered.CanWrite);
        recovered.Lease.Dispose();

        // The kernel flag is consumed, but the next holder is still gated.
        var next = await Provider(name, "app").AcquireAsync(Short);
        Assert.Equal(MutationLeaseOutcome.Acquired, next.Outcome);
        Assert.False(next.Lease!.WasAbandoned);
        Assert.True(next.Lease.RequiresRecovery);
        Assert.False(next.CanWrite);
        next.Lease.Dispose();

        GC.KeepAlive(rawHandle);
        rawHandle?.Dispose();
    }

    [Fact]
    public async Task PreCreatedObject_WithPermissiveDacl_IsRefused()
    {
        string name = MutationLeaseNames.ForTest();

        // Two entries so the count check passes and the trustee check is what refuses.
        using var squatter = TestMutex.CreateWithSddl(name, "D:(A;;GA;;;WD)(A;;GA;;;SY)");

        var result = await Provider(name).AcquireAsync(Short);

        Assert.Equal(MutationLeaseOutcome.Refused, result.Outcome);
        Assert.Null(result.Lease);
        Assert.Contains("Existing lock object", result.ErrorMessage, StringComparison.Ordinal);
        // Unelevated, the owner check fires first (the test user owns the object).
        if (IsElevated)
            Assert.Contains("S-1-1-0", result.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PreCreatedObject_WithExtraTrustee_IsRefused()
    {
        string name = MutationLeaseNames.ForTest();

        using var squatter = TestMutex.CreateWithSddl(name, "D:P(A;;GA;;;SY)(A;;GA;;;BA)(A;;GA;;;IU)");

        var result = await Provider(name).AcquireAsync(Short);

        Assert.Equal(MutationLeaseOutcome.Refused, result.Outcome);
        if (IsElevated)
            Assert.Contains("3 entries", result.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PreCreatedObject_WithDenyEntry_IsRefused()
    {
        string name = MutationLeaseNames.ForTest();

        using var squatter = TestMutex.CreateWithSddl(name, "D:P(D;;GA;;;WD)(A;;GA;;;SY)(A;;GA;;;BA)");

        var result = await Provider(name).AcquireAsync(Short);

        Assert.Equal(MutationLeaseOutcome.Refused, result.Outcome);
    }

    [Fact]
    public async Task Elevated_PreCreatedObject_WithOurExactDacl_IsAccepted()
    {
        if (!IsElevated) return;
        string name = MutationLeaseNames.ForTest();

        using var precreated = TestMutex.CreateWithSddl(name, NamedMutexMutationLeaseProvider.RequiredSddl);

        var result = await Provider(name).AcquireAsync(Short);

        Assert.Equal(MutationLeaseOutcome.Acquired, result.Outcome);
        Assert.False(result.CanWrite);
        result.Lease!.Dispose();
    }

    [Fact]
    public async Task PreCreatedObject_OfAnotherKernelType_IsRefused()
    {
        string name = MutationLeaseNames.ForTest();

        using var squatter = new EventWaitHandle(false, EventResetMode.ManualReset, name);

        var result = await Provider(name).AcquireAsync(Short);

        Assert.Equal(MutationLeaseOutcome.Refused, result.Outcome);
        Assert.Equal(6, result.NativeErrorCode);
    }

    [Fact]
    public async Task Elevated_Lease_SurvivesAsyncContinuations_AndDisposesFromAnyThread()
    {
        if (!IsElevated) return;
        string name = MutationLeaseNames.ForTest();

        var result = await Provider(name).AcquireAsync(Short);
        Assert.Equal(MutationLeaseOutcome.Acquired, result.Outcome);
        var lease = result.Lease!;
        lease.MarkRecovered();

        for (int i = 0; i < 5; i++)
        {
            await Task.Yield();
            await Task.Delay(10);
            Assert.True(lease.IsHeld);
            Assert.True(lease.CanWrite);
        }

        var stillBlocked = await Provider(name).AcquireAsync(TimeSpan.Zero);
        Assert.Equal(MutationLeaseOutcome.TimedOut, stillBlocked.Outcome);

        await Task.Run(() => lease.Dispose());
        Assert.False(lease.IsHeld);

        var after = await Provider(name).AcquireAsync(Short);
        Assert.Equal(MutationLeaseOutcome.Acquired, after.Outcome);
        await after.Lease!.DisposeAsync();
        Assert.False(Mutex.TryOpenExisting(name, out _));
    }

    [Fact]
    public async Task Elevated_DisposeAsync_ReleasesForTheNextHolder()
    {
        if (!IsElevated) return;
        string name = MutationLeaseNames.ForTest();

        var first = await Provider(name).AcquireAsync(Short);
        await first.Lease!.DisposeAsync();
        first.Lease.Dispose();

        var second = await Provider(name).AcquireAsync(Short);
        Assert.Equal(MutationLeaseOutcome.Acquired, second.Outcome);
        second.Lease!.Dispose();
    }

    /// <summary>
    /// Real cross-process check: a PowerShell child (elevated, because the test
    /// process is) opens the test mutex and holds it; the provider must time out
    /// against it. Killing the child while a test-side handle keeps the object
    /// alive yields the abandoned diagnostic, still gated.
    /// </summary>
    [Fact]
    public async Task Elevated_ChildProcess_ExcludesUs_AndItsKillIsReportedAbandoned()
    {
        if (!IsElevated) return;
        string name = MutationLeaseNames.ForTest();

        var keeper = await Provider(name, "app").AcquireAsync(Short);
        Assert.Equal(MutationLeaseOutcome.Acquired, keeper.Outcome);

        string script =
            "$m = [System.Threading.Mutex]::OpenExisting('" + name + "'); " +
            "[Console]::Out.WriteLine('opened'); [Console]::Out.Flush(); " +
            "$null = $m.WaitOne(); " +
            "[Console]::Out.WriteLine('held'); [Console]::Out.Flush(); " +
            "Start-Sleep -Seconds 300";
        var startInfo = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (string arg in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", script })
            startInfo.ArgumentList.Add(arg);

        using var child = Process.Start(startInfo)!;
        try
        {
            Assert.Equal("opened", await ReadLineAsync(child));
            keeper.Lease!.Dispose();
            Assert.Equal("held", await ReadLineAsync(child));

            var blocked = await Provider(name, "service").AcquireAsync(Short);
            Assert.Equal(MutationLeaseOutcome.TimedOut, blocked.Outcome);

            // Keep the object alive across the crash so the kernel can report abandonment.
            using var probe = Mutex.OpenExisting(name);
            child.Kill();
            await child.WaitForExitAsync();

            var after = await Provider(name, "service").AcquireAsync(Short);
            Assert.Equal(MutationLeaseOutcome.AcquiredAbandoned, after.Outcome);
            Assert.True(after.Lease!.WasAbandoned);
            Assert.False(after.CanWrite);
            after.Lease.Dispose();
        }
        finally
        {
            if (!child.HasExited)
                child.Kill();
        }

        static async Task<string?> ReadLineAsync(Process child)
        {
            try
            {
                return await child.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(30));
            }
            catch (TimeoutException)
            {
                string stderr = child.HasExited ? await child.StandardError.ReadToEndAsync() : "(child still running)";
                throw new TimeoutException($"Child produced no line within 30 s. stderr: {stderr}");
            }
        }
    }

    [Fact]
    public void Constructor_RejectsInvalidNames()
    {
        Assert.Throws<ArgumentException>(() => new NamedMutexMutationLeaseProvider("NoPrefix", "app"));
        Assert.Throws<ArgumentException>(() => new NamedMutexMutationLeaseProvider(@"Global\a\b", "app"));
        Assert.Throws<ArgumentException>(() => new NamedMutexMutationLeaseProvider(@"Local\ok", ""));
    }

    [Fact]
    public async Task Acquire_RejectsUnboundedOrNegativeWaits()
    {
        var provider = Provider(MutationLeaseNames.ForTest());

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => provider.AcquireAsync(Timeout.InfiniteTimeSpan));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => provider.AcquireAsync(TimeSpan.FromMilliseconds(-2)));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => provider.AcquireAsync(TimeSpan.FromDays(30)));
    }

    /// <summary>
    /// Creates a named mutex with an arbitrary SDDL so tests can stage squatters.
    /// Classic DllImport: the test project is never NativeAOT and does not allow unsafe code.
    /// </summary>
    private static class TestMutex
    {
        internal static SafeHandleWrapper CreateWithSddl(string name, string sddl)
        {
            if (!ConvertStringSecurityDescriptorToSecurityDescriptorW(sddl, 1, out nint sd, out _))
                throw new InvalidOperationException($"SDDL conversion failed (win32={Marshal.GetLastPInvokeError()})");

            try
            {
                var attributes = new SecurityAttributes
                {
                    Length = (uint)Marshal.SizeOf<SecurityAttributes>(),
                    SecurityDescriptor = sd,
                    InheritHandle = 0,
                };
                nint handle = CreateMutexExW(in attributes, name, 0, 0x001F0001);
                if (handle == 0)
                    throw new InvalidOperationException($"CreateMutexEx failed (win32={Marshal.GetLastPInvokeError()})");
                return new SafeHandleWrapper(handle);
            }
            finally
            {
                LocalFree(sd);
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SecurityAttributes
        {
            public uint Length;
            public nint SecurityDescriptor;
            public int InheritHandle;
        }

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern nint CreateMutexExW(in SecurityAttributes attributes, string name, uint flags, uint access);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ConvertStringSecurityDescriptorToSecurityDescriptorW(
            string sddl, uint revision, out nint securityDescriptor, out uint size);

        [DllImport("kernel32.dll", ExactSpelling = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern nint LocalFree(nint mem);

        [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(nint handle);

        internal sealed class SafeHandleWrapper(nint handle) : IDisposable
        {
            public void Dispose() => CloseHandle(handle);
        }
    }
}
