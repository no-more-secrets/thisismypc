using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;

namespace ThisIsMyPC.Interop.Win32;

/// <summary>
/// Starts explorer.exe as the desktop user, never elevated. The app runs
/// elevated, and a process it starts inherits that, so a shell it started
/// plainly would run as administrator: every app opened from Start would be
/// elevated, and shell extensions that draw the taskbar (ExplorerPatcher's
/// own Windows 10 taskbar, seen on 2026-09-03) do not come up in an
/// elevated Explorer at all. An elevated admin token carries a link to the
/// unelevated token of the same user; the shell is started with that.
/// </summary>
public static unsafe class ShellLauncher
{
    private static readonly NLog.Logger Log = NLog.LogManager.GetLogger("ThisIsMyPC.Interop.Win32.ShellLauncher");

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
    /// Starts the shell as the desktop user. Elevated: through the linked
    /// token. Not elevated: plainly, since the process is already that user.
    /// The route taken goes to the log.
    /// </summary>
    public static OperationResult<bool> StartExplorerAsDesktopUser()
    {
        var explorerPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");

        if (!IsElevated)
        {
            Process.Start(new ProcessStartInfo { FileName = explorerPath, UseShellExecute = false });
            Log.Info("Started explorer.exe with this process's own (unelevated) token");
            return OperationResult<bool>.Success(true);
        }

        try
        {
            StartWithLinkedToken(explorerPath);
            Log.Info("Started explorer.exe with the desktop user's unelevated linked token");
            return OperationResult<bool>.Success(true);
        }
        catch (Win32Exception ex)
        {
            Log.Error(ex, "Starting explorer.exe with the unelevated linked token failed (Win32 {Code})", ex.NativeErrorCode);
            return OperationResult<bool>.Failure(
                $"Could not start Explorer as the signed-in user: {ex.Message}", ErrorCategory.ServiceUnavailable, ex);
        }
    }

    private static int ElevationTypeOf(nint token)
    {
        int elevationType;
        return NativeProcessToken.GetTokenInformation(token, NativeProcessToken.TokenElevationType, &elevationType, sizeof(int), out _)
            ? elevationType
            : NativeProcessToken.TokenElevationTypeDefault;
    }

    private static void StartWithLinkedToken(string explorerPath)
    {
        if (!NativeProcessToken.OpenProcessToken(
                NativeProcessToken.GetCurrentProcess(),
                NativeProcessToken.TOKEN_QUERY | NativeProcessToken.TOKEN_DUPLICATE,
                out var ownToken))
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "OpenProcessToken");

        nint linked = 0;
        nint primary = 0;
        try
        {
            // TOKEN_LINKED_TOKEN is one HANDLE.
            nint linkedHandle;
            if (!NativeProcessToken.GetTokenInformation(ownToken, NativeProcessToken.TokenLinkedToken, &linkedHandle, (uint)sizeof(nint), out _))
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "GetTokenInformation(TokenLinkedToken)");
            linked = linkedHandle;

            // The linked token is an impersonation token; a new process needs a primary one.
            if (!NativeProcessToken.DuplicateTokenEx(
                    linked, NativeProcessToken.MAXIMUM_ALLOWED, 0,
                    NativeProcessToken.SecurityImpersonation, NativeProcessToken.TokenPrimary, out primary))
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "DuplicateTokenEx");

            var startup = new NativeProcessToken.STARTUPINFOW { cb = (uint)sizeof(NativeProcessToken.STARTUPINFOW) };
            NativeProcessToken.PROCESS_INFORMATION info;
            // CreateProcessWithTokenW may write into the command line, so it gets its own buffer.
            var commandLine = ("\"" + explorerPath + "\"\0").ToCharArray();
            fixed (char* commandLinePtr = commandLine)
            {
                if (!NativeProcessToken.CreateProcessWithTokenW(
                        primary, NativeProcessToken.LOGON_WITH_PROFILE, null, commandLinePtr, 0, 0, null, &startup, &info))
                    throw new Win32Exception(Marshal.GetLastPInvokeError(), "CreateProcessWithTokenW");
            }
            NativeProcessToken.CloseHandle(info.hThread);
            NativeProcessToken.CloseHandle(info.hProcess);
        }
        finally
        {
            if (primary != 0) NativeProcessToken.CloseHandle(primary);
            if (linked != 0) NativeProcessToken.CloseHandle(linked);
            NativeProcessToken.CloseHandle(ownToken);
        }
    }
}
