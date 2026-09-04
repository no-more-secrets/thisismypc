using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;

namespace ThisIsMyPC.Interop.Win32;

/// <summary>
/// The elevated desktop app's <see cref="IInteractiveUserContext"/>: it acts
/// as the signed-in user by borrowing a primary token from a process the user
/// is already running.
///
/// The elevated token's own linked token is no use: without the TCB privilege
/// Windows returns it at identification level, which cannot become a primary
/// token (DuplicateTokenEx fails with 1346, seen on Sam's PC 2026-09-03). So
/// the token comes from a live user-level process: sihost, ctfmon, taskhostw,
/// RuntimeBroker, or explorer, each of which every interactive session runs.
///
/// The Session 0 service will implement this same interface from SYSTEM
/// through WTSQueryUserToken instead; consumers depend on the interface.
/// </summary>
public sealed unsafe class DesktopUserContext : IInteractiveUserContext
{
    private static readonly NLog.Logger Log = NLog.LogManager.GetLogger("ThisIsMyPC.Interop.Win32.DesktopUserContext");

    /// <summary>User-level processes every interactive session runs, in the order tried.</summary>
    private static readonly string[] TokenSources = ["sihost", "ctfmon", "taskhostw", "RuntimeBroker", "explorer"];

    private InteractiveUser? _current;
    private bool _resolved;

    public bool IsCallerElevated
    {
        get
        {
            if (!NativeProcessToken.OpenProcessToken(NativeProcessToken.GetCurrentProcess(), NativeProcessToken.TOKEN_QUERY, out var token))
                return false;
            try
            {
                return ElevationTypeOf(token) == NativeProcessToken.TokenElevationTypeFull;
            }
            finally
            {
                NativeProcessToken.CloseHandle(token);
            }
        }
    }

    public InteractiveUser? Current
    {
        get
        {
            if (_resolved)
                return _current;
            _resolved = true;

            if (!IsCallerElevated)
            {
                // The process is already the desktop user; read its own token.
                _current = ResolveFromOwnToken();
                return _current;
            }

            var handle = CaptureUserToken(preferred: null, out _);
            if (handle == 0)
                return _current;
            try
            {
                _current = ResolveUser(handle);
            }
            finally
            {
                NativeProcessToken.CloseHandle(handle);
            }
            return _current;
        }
    }

    public OperationResult<bool> LaunchAsUser(string applicationPath, string? arguments = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(applicationPath);

        if (!IsCallerElevated)
        {
            Process.Start(new ProcessStartInfo { FileName = applicationPath, Arguments = arguments ?? string.Empty, UseShellExecute = false });
            Log.Info("Started {App} with this process's own (unelevated) token", applicationPath);
            return OperationResult<bool>.Success(true);
        }

        var handle = CaptureUserToken(preferred: null, out var source);
        if (handle == 0)
        {
            Log.Warn("No user token to launch {App} as the desktop user; starting it elevated as a last resort", applicationPath);
            Process.Start(new ProcessStartInfo { FileName = applicationPath, Arguments = arguments ?? string.Empty, UseShellExecute = false });
            return OperationResult<bool>.Success(true);
        }
        try
        {
            StartWithToken(handle, applicationPath, arguments);
            Log.Info("Started {App} as the desktop user (token from {Source})", applicationPath, source);
            return OperationResult<bool>.Success(true);
        }
        catch (Win32Exception ex)
        {
            Log.Error(ex, "Starting {App} as the desktop user failed (Win32 {Code})", applicationPath, ex.NativeErrorCode);
            return OperationResult<bool>.Failure(
                $"Could not start {Path.GetFileName(applicationPath)} as the signed-in user: {ex.Message}",
                ErrorCategory.ServiceUnavailable, ex);
        }
        finally
        {
            NativeProcessToken.CloseHandle(handle);
        }
    }

    public OperationResult<T> RunAsUser<T>(Func<T> action)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (!IsCallerElevated)
            return Run(action);   // the process is already the user

        var handle = CaptureUserToken(preferred: null, out var source);
        if (handle == 0)
            return OperationResult<T>.Failure("No signed-in desktop user to act as.", ErrorCategory.ServiceUnavailable);

        try
        {
            if (!NativeProcessToken.ImpersonateLoggedOnUser(handle))
                return OperationResult<T>.Failure(
                    "Could not impersonate the signed-in user.", ErrorCategory.AccessDenied,
                    new Win32Exception(Marshal.GetLastPInvokeError()));
            try
            {
                Log.Debug("Running an action as the desktop user (token from {Source})", source);
                return Run(action);
            }
            finally
            {
                if (!NativeProcessToken.RevertToSelf())
                    Log.Error("RevertToSelf failed after impersonation (Win32 {Code}); the thread may still be impersonating", Marshal.GetLastPInvokeError());
            }
        }
        finally
        {
            NativeProcessToken.CloseHandle(handle);
        }
    }

    private static OperationResult<T> Run<T>(Func<T> action)
    {
        try
        {
            return OperationResult<T>.Success(action());
        }
        catch (Exception ex)
        {
            return OperationResult<T>.Failure($"The action failed while acting as the user: {ex.Message}", ErrorCategory.ServiceUnavailable, ex);
        }
    }

    /// <summary>
    /// A primary token of the desktop user, or 0 when none can be borrowed.
    /// Tries <paramref name="preferred"/> first, then the known user-level
    /// processes of this session. The caller closes the handle.
    /// </summary>
    private static nint CaptureUserToken(Process? preferred, out string source)
    {
        source = string.Empty;
        if (preferred is not null && TryBorrow(preferred, out var handle, out source))
            return handle;

        using var self = Process.GetCurrentProcess();
        var session = self.SessionId;
        foreach (var name in TokenSources)
        {
            foreach (var candidate in Process.GetProcessesByName(name))
            {
                using (candidate)
                {
                    if (candidate.SessionId == session && TryBorrow(candidate, out handle, out source))
                        return handle;
                }
            }
        }

        Log.Warn("No unelevated process of the desktop user to borrow a token from");
        return 0;
    }

    private static bool TryBorrow(Process process, out nint primaryToken, out string source)
    {
        primaryToken = 0;
        source = string.Empty;
        nint processToken = 0;
        try
        {
            if (!NativeProcessToken.OpenProcessToken(
                    process.Handle,
                    NativeProcessToken.TOKEN_QUERY | NativeProcessToken.TOKEN_DUPLICATE,
                    out processToken))
            {
                Log.Debug("OpenProcessToken on {Name} ({Pid}) failed (Win32 {Code})", process.ProcessName, process.Id, Marshal.GetLastPInvokeError());
                return false;
            }
            if (ElevationTypeOf(processToken) == NativeProcessToken.TokenElevationTypeFull)
            {
                Log.Debug("{Name} ({Pid}) runs elevated; not borrowing its token", process.ProcessName, process.Id);
                return false;
            }
            // A primary token that also allows impersonation, so one capture serves both launch and RunAsUser.
            if (!NativeProcessToken.DuplicateTokenEx(
                    processToken,
                    NativeProcessToken.TOKEN_QUERY | NativeProcessToken.TOKEN_DUPLICATE | NativeProcessToken.TOKEN_IMPERSONATE
                        | NativeProcessToken.TOKEN_ASSIGN_PRIMARY | NativeProcessToken.MAXIMUM_ALLOWED,
                    0, NativeProcessToken.SecurityImpersonation, NativeProcessToken.TokenPrimary, out primaryToken))
            {
                Log.Debug("DuplicateTokenEx on {Name} ({Pid}) failed (Win32 {Code})", process.ProcessName, process.Id, Marshal.GetLastPInvokeError());
                return false;
            }
            source = $"{process.ProcessName} ({process.Id})";
            return true;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            Log.Debug(ex, "Could not open {Name} ({Pid}) to borrow its token", process.ProcessName, process.Id);
            return false;
        }
        finally
        {
            if (processToken != 0)
                NativeProcessToken.CloseHandle(processToken);
        }
    }

    private static void StartWithToken(nint primaryToken, string applicationPath, string? arguments)
    {
        var startup = new NativeProcessToken.STARTUPINFOW { cb = (uint)sizeof(NativeProcessToken.STARTUPINFOW) };
        NativeProcessToken.PROCESS_INFORMATION info;
        // CreateProcessWithTokenW may write into the command line, so it gets its own buffer.
        var line = string.IsNullOrEmpty(arguments) ? $"\"{applicationPath}\"" : $"\"{applicationPath}\" {arguments}";
        var commandLine = (line + "\0").ToCharArray();
        fixed (char* commandLinePtr = commandLine)
        {
            if (!NativeProcessToken.CreateProcessWithTokenW(
                    primaryToken, NativeProcessToken.LOGON_WITH_PROFILE, null, commandLinePtr, 0, 0, null, &startup, &info))
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "CreateProcessWithTokenW");
        }
        NativeProcessToken.CloseHandle(info.hThread);
        NativeProcessToken.CloseHandle(info.hProcess);
    }

    private static int ElevationTypeOf(nint token)
    {
        int elevationType;
        return NativeProcessToken.GetTokenInformation(token, NativeProcessToken.TokenElevationType, &elevationType, sizeof(int), out _)
            ? elevationType
            : NativeProcessToken.TokenElevationTypeDefault;
    }

    private static InteractiveUser? ResolveFromOwnToken()
    {
        if (!NativeProcessToken.OpenProcessToken(NativeProcessToken.GetCurrentProcess(), NativeProcessToken.TOKEN_QUERY, out var token))
            return null;
        try
        {
            return ResolveUser(token);
        }
        finally
        {
            NativeProcessToken.CloseHandle(token);
        }
    }

    private static InteractiveUser? ResolveUser(nint token)
    {
        var sid = ReadSid(token);
        if (sid is null)
            return null;
        return new InteractiveUser
        {
            Sid = sid,
            AccountName = ReadAccountName(token) ?? string.Empty,
            SessionId = ReadSessionId(token),
        };
    }

    private static string? ReadSid(nint token)
    {
        // TOKEN_USER plus the SID it points to; 256 bytes covers any SID.
        var buffer = stackalloc byte[256];
        if (!NativeProcessToken.GetTokenInformation(token, NativeProcessToken.TokenUser, buffer, 256, out _))
        {
            Log.Debug("GetTokenInformation(TokenUser) failed (Win32 {Code})", Marshal.GetLastPInvokeError());
            return null;
        }
        var user = (NativeProcessToken.TOKEN_USER*)buffer;
        if (!NativeProcessToken.ConvertSidToStringSidW(user->Sid, out var stringSid) || stringSid == 0)
            return null;
        try
        {
            return Marshal.PtrToStringUni(stringSid);
        }
        finally
        {
            NativeProcessToken.LocalFree(stringSid);
        }
    }

    private static string? ReadAccountName(nint token)
    {
        var buffer = stackalloc byte[256];
        if (!NativeProcessToken.GetTokenInformation(token, NativeProcessToken.TokenUser, buffer, 256, out _))
            return null;
        var user = (NativeProcessToken.TOKEN_USER*)buffer;

        uint cchName = 256;
        uint cchDomain = 256;
        var name = stackalloc char[256];
        var domain = stackalloc char[256];
        if (!NativeProcessToken.LookupAccountSidW(null, user->Sid, name, ref cchName, domain, ref cchDomain, out _))
            return null;

        var user_ = new string(name);
        var domain_ = new string(domain);
        return domain_.Length > 0 ? $@"{domain_}\{user_}" : user_;
    }

    private static uint ReadSessionId(nint token)
    {
        uint sessionId;
        return NativeProcessToken.GetTokenInformation(token, NativeProcessToken.TokenSessionId, &sessionId, sizeof(uint), out _)
            ? sessionId
            : 0;
    }
}
