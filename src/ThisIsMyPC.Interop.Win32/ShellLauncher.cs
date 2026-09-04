using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;

namespace ThisIsMyPC.Interop.Win32;

/// <summary>
/// A primary token of the signed-in user at the desktop's own (unelevated)
/// level, borrowed from a running process of theirs. Dispose closes it.
/// </summary>
public sealed class DesktopUserToken : IDisposable
{
    internal nint Handle { get; private set; }

    /// <summary>Which process lent the token, for the log.</summary>
    public string Source { get; }

    internal DesktopUserToken(nint handle, string source)
    {
        Handle = handle;
        Source = source;
    }

    public void Dispose()
    {
        if (Handle != 0)
        {
            NativeProcessToken.CloseHandle(Handle);
            Handle = 0;
        }
    }
}

/// <summary>
/// Starts explorer.exe as the desktop user, never elevated. The app runs
/// elevated, and a process it starts inherits that, so a shell it started
/// plainly would run as administrator: every app opened from Start would be
/// elevated, and shell extensions that draw the taskbar (ExplorerPatcher's
/// own Windows 10 taskbar, seen on 2026-09-03) do not come up in an
/// elevated Explorer at all.
///
/// The elevated token's own linked token is no use: without the TCB
/// privilege Windows returns it at identification level, which cannot become
/// a primary token (DuplicateTokenEx fails with 1346, seen the same day).
/// So the token is borrowed from a process already running as the desktop
/// user: the shell itself before it is shut down, else the session's other
/// user-level processes (sihost, ctfmon, taskhostw, RuntimeBroker).
/// </summary>
public static unsafe class ShellLauncher
{
    private static readonly NLog.Logger Log = NLog.LogManager.GetLogger("ThisIsMyPC.Interop.Win32.ShellLauncher");

    /// <summary>User-level processes every interactive session runs, in the order tried.</summary>
    private static readonly string[] FallbackTokenSources = ["sihost", "ctfmon", "taskhostw", "RuntimeBroker"];

    /// <summary>True when this process runs with a full (elevated) admin token.</summary>
    public static bool IsElevated
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

    /// <summary>
    /// Borrows the desktop user's token: from <paramref name="preferred"/>
    /// (the shell about to be shut down) when it runs unelevated, else from
    /// the first user-level process of this session that does. Null when
    /// none is found, or when this process is not elevated and needs none.
    /// </summary>
    public static DesktopUserToken? CaptureDesktopUserToken(Process? preferred)
    {
        if (!IsElevated)
            return null;

        if (preferred is not null && TryBorrow(preferred, out var token))
            return token;

        using var self = Process.GetCurrentProcess();
        var session = self.SessionId;
        foreach (var name in FallbackTokenSources)
        {
            foreach (var candidate in Process.GetProcessesByName(name))
            {
                using (candidate)
                {
                    if (candidate.SessionId == session && TryBorrow(candidate, out token))
                        return token;
                }
            }
        }

        Log.Warn("No unelevated process of the desktop user to borrow a token from");
        return null;
    }

    /// <summary>
    /// Starts the shell. With a borrowed token it runs as the desktop user;
    /// without one it runs as this process, which is a last resort that
    /// keeps the person a shell at all, and the log says so.
    /// </summary>
    public static OperationResult<bool> StartExplorer(DesktopUserToken? token)
    {
        var explorerPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");

        if (token is not null)
        {
            try
            {
                StartWithToken(token.Handle, explorerPath);
                Log.Info("Started explorer.exe with the desktop user's token borrowed from {Source}", token.Source);
                return OperationResult<bool>.Success(true);
            }
            catch (Win32Exception ex)
            {
                Log.Error(ex, "Starting explorer.exe with the token borrowed from {Source} failed (Win32 {Code}); starting it plainly instead",
                    token.Source, ex.NativeErrorCode);
            }
        }

        Process.Start(new ProcessStartInfo { FileName = explorerPath, UseShellExecute = false });
        if (IsElevated)
            Log.Warn("Started explorer.exe with this process's elevated token: the shell runs as administrator until the next restart");
        else
            Log.Info("Started explorer.exe with this process's own (unelevated) token");
        return OperationResult<bool>.Success(true);
    }

    private static bool TryBorrow(Process process, out DesktopUserToken? token)
    {
        token = null;
        nint processToken = 0;
        try
        {
            if (!NativeProcessToken.OpenProcessToken(
                    process.Handle, NativeProcessToken.TOKEN_QUERY | NativeProcessToken.TOKEN_DUPLICATE, out processToken))
            {
                Log.Debug("OpenProcessToken on {Name} ({Pid}) failed (Win32 {Code})", process.ProcessName, process.Id, Marshal.GetLastPInvokeError());
                return false;
            }
            if (ElevationTypeOf(processToken) == NativeProcessToken.TokenElevationTypeFull)
            {
                Log.Debug("{Name} ({Pid}) runs elevated; not borrowing its token", process.ProcessName, process.Id);
                return false;
            }
            if (!NativeProcessToken.DuplicateTokenEx(
                    processToken, NativeProcessToken.MAXIMUM_ALLOWED, 0,
                    NativeProcessToken.SecurityImpersonation, NativeProcessToken.TokenPrimary, out var primary))
            {
                Log.Debug("DuplicateTokenEx on {Name} ({Pid}) failed (Win32 {Code})", process.ProcessName, process.Id, Marshal.GetLastPInvokeError());
                return false;
            }
            token = new DesktopUserToken(primary, $"{process.ProcessName} ({process.Id})");
            return true;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            // Process.Handle: access denied, or the process is gone.
            Log.Debug(ex, "Could not open {Name} ({Pid}) to borrow its token", process.ProcessName, process.Id);
            return false;
        }
        finally
        {
            if (processToken != 0)
                NativeProcessToken.CloseHandle(processToken);
        }
    }

    private static int ElevationTypeOf(nint token)
    {
        int elevationType;
        return NativeProcessToken.GetTokenInformation(token, NativeProcessToken.TokenElevationType, &elevationType, sizeof(int), out _)
            ? elevationType
            : NativeProcessToken.TokenElevationTypeDefault;
    }

    private static void StartWithToken(nint primaryToken, string explorerPath)
    {
        var startup = new NativeProcessToken.STARTUPINFOW { cb = (uint)sizeof(NativeProcessToken.STARTUPINFOW) };
        NativeProcessToken.PROCESS_INFORMATION info;
        // CreateProcessWithTokenW may write into the command line, so it gets its own buffer.
        var commandLine = ("\"" + explorerPath + "\"\0").ToCharArray();
        fixed (char* commandLinePtr = commandLine)
        {
            if (!NativeProcessToken.CreateProcessWithTokenW(
                    primaryToken, NativeProcessToken.LOGON_WITH_PROFILE, null, commandLinePtr, 0, 0, null, &startup, &info))
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "CreateProcessWithTokenW");
        }
        NativeProcessToken.CloseHandle(info.hThread);
        NativeProcessToken.CloseHandle(info.hProcess);
    }
}
