using System.Globalization;
using System.Runtime.InteropServices;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Interop.Win32.Registry;
using ThisIsMyPC.Interop.Win32.Services;
namespace ThisIsMyPC.Interop.Win32.Shell;

/// <summary>Windows appbar state and the installed ExplorerPatcher DWM companion. No shell commands.</summary>
public sealed partial class ShellNativeSettings : IShellNativeSettings
{
    public const string DwmService = "ep_dwm_D17F1E1A-5919-4427-8F89-A1A8503CA3EB";
    public static string DwmPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "ExplorerPatcher", "ep_dwm_svc.exe");
    public static string DwmCommand => "\"" + DwmPath + "\" " + DwmService + @" Global\ep_dwm_2_D17F1E1A-5919-4427-8F89-A1A8503CA3EB";
    private readonly IServiceInstaller _installer;
    private readonly IServiceControlService _services;
    private readonly IRegistryService _registry;
    private readonly Func<string, bool> _fileExists;
    public ShellNativeSettings(IServiceInstaller? installer = null, IServiceControlService? services = null, IRegistryService? registry = null,
        Func<string, bool>? fileExists = null)
    { _installer = installer ?? new ServiceInstaller(); _services = services ?? new ServiceControlService(); _registry = registry ?? new RegistryService(); _fileExists = fileExists ?? File.Exists; }

    public OperationResult<string> ReadTaskbarState()
    {
        var window = FindWindowW("Shell_TrayWnd", null);
        if (window == 0) return OperationResult<string>.Failure("The Windows taskbar is unavailable.", ErrorCategory.ServiceUnavailable);
        var data = new AppBarData { Size = (uint)Marshal.SizeOf<AppBarData>(), Window = window };
        return OperationResult<string>.Success(((uint)SHAppBarMessage(4, ref data)).ToString(CultureInfo.InvariantCulture));
    }
    public OperationResult<bool> WriteTaskbarState(string state)
    {
        if (!uint.TryParse(state, NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value > 3)
            return Fail("Invalid taskbar state.");
        var window = FindWindowW("Shell_TrayWnd", null);
        if (window == 0) return Fail("The Windows taskbar is unavailable.");
        var data = new AppBarData { Size = (uint)Marshal.SizeOf<AppBarData>(), Window = window, Parameter = (nint)value };
        SHAppBarMessage(10, ref data);
        return ReadTaskbarState() is { IsSuccess: true, Value: var current } && current == state
            ? OperationResult<bool>.Success(true) : Fail("Windows did not accept the taskbar state.");
    }
    public OperationResult<string> ReadRoundedCornersState()
    {
        var installed = _installer.IsInstalled(DwmService);
        if (!installed.IsSuccess) return OperationResult<string>.Failure(installed.ErrorMessage!, ErrorCategory.ServiceUnavailable);
        if (!installed.Value) return OperationResult<string>.Success("absent");
        var command = _registry.ReadString(@"HKLM\SYSTEM\CurrentControlSet\Services\" + DwmService, "ImagePath");
        if (!command.IsSuccess) command = _registry.ReadExpandString(@"HKLM\SYSTEM\CurrentControlSet\Services\" + DwmService, "ImagePath");
        if (!string.Equals(command.Value, DwmCommand, StringComparison.OrdinalIgnoreCase))
            return OperationResult<string>.Failure("The ExplorerPatcher service has an unrecognized configuration.", ErrorCategory.ServiceUnavailable);
        var state = _services.Query(DwmService);
        if (!state.IsSuccess || state.Value!.State is not (ServiceState.Running or ServiceState.Stopped))
            return OperationResult<string>.Failure("The ExplorerPatcher service state cannot be captured yet.", ErrorCategory.ServiceUnavailable);
        return OperationResult<string>.Success(state.Value.StartType + "|" + state.Value.State);
    }
    public static bool ValidCornersState(string state) => state == "absent" ||
        state.Split('|') is [var start, var run] && Enum.TryParse<ServiceStartType>(start, out var kind)
        && Enum.IsDefined(kind) && start == kind.ToString() && run is "Running" or "Stopped";

    public async Task<OperationResult<bool>> WriteRoundedCornersStateAsync(string state)
    {
        if (!ValidCornersState(state)) return Fail("Invalid rounded-corner state.");
        var before = ReadRoundedCornersState();
        if (!before.IsSuccess) return Fail(before.ErrorMessage!);
        if (before.Value == state) return OperationResult<bool>.Success(true);
        if (state == "absent")
        {
            var stop = await _services.StopAsync(DwmService, TimeSpan.FromSeconds(15)).ConfigureAwait(false);
            var removal = stop.IsSuccess ? _installer.Uninstall(DwmService) : stop;
            if (removal.IsSuccess) return removal;
            var restore = await SetServiceStateAsync(before.Value!).ConfigureAwait(false);
            return restore.IsSuccess ? removal : Fail(removal.ErrorMessage + " Restoration also failed: " + restore.ErrorMessage);
        }
        if (!_fileExists(DwmPath)) return Fail("The ExplorerPatcher DWM companion is missing.");
        var created = before.Value == "absent";
        if (created)
        {
            var install = _installer.Install(DwmService, "ExplorerPatcher Desktop Window Manager Service",
                "Service for managing aspects related to the Desktop Window Manager.", DwmCommand);
            if (!install.IsSuccess) return install;
        }
        var result = await SetServiceStateAsync(state).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            OperationResult<bool> rollback;
            if (created)
            {
                rollback = await _services.StopAsync(DwmService, TimeSpan.FromSeconds(15)).ConfigureAwait(false);
                if (rollback.IsSuccess) rollback = _installer.Uninstall(DwmService);
            }
            else rollback = await SetServiceStateAsync(before.Value!).ConfigureAwait(false);
            if (!rollback.IsSuccess) return Fail(result.ErrorMessage + " Restoration also failed: " + rollback.ErrorMessage);
        }
        return result;
    }
    private async Task<OperationResult<bool>> SetServiceStateAsync(string state)
    {
        var pieces = state.Split('|');
        var startType = Enum.Parse<ServiceStartType>(pieces[0]);
        var runningDisabled = startType == ServiceStartType.Disabled && pieces[1] == "Running";
        var result = _services.SetStartType(DwmService, runningDisabled ? ServiceStartType.Manual : startType);
        if (result.IsSuccess) result = pieces[1] == "Running"
            ? await _services.StartAsync(DwmService, TimeSpan.FromSeconds(15)).ConfigureAwait(false)
            : await _services.StopAsync(DwmService, TimeSpan.FromSeconds(15)).ConfigureAwait(false);
        if (result.IsSuccess && runningDisabled) result = _services.SetStartType(DwmService, startType);
        return result;
    }
    private static OperationResult<bool> Fail(string message) => OperationResult<bool>.Failure(message, ErrorCategory.ServiceUnavailable);
    [StructLayout(LayoutKind.Sequential)]
    private struct AppBarData { public uint Size; public nint Window; public uint Callback, Edge; public int Left, Top, Right, Bottom; public nint Parameter; }
    [LibraryImport("shell32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nuint SHAppBarMessage(uint message, ref AppBarData data);
    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint FindWindowW(string className, string? title);
}
