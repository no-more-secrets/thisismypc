using System.Globalization;
using System.Runtime.InteropServices;
using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Interop.Win32.Shell;
namespace ThisIsMyPC.Modules.Shell.Changes;

public static class ShellIntegrationChanges
{
    public const string Taskbar = "shell:taskbar-auto-hide";
    public const string Corners = "shell:ep-rounded-corners";
    public const string Navigation = "shell:ep-navigation-bar";
    public const string NavigationKey = @"HKCU\Software\Classes\CLSID\{056440FD-8568-48e7-A632-72157243B55B}\InprocServer32";
    public const string Registration = "ep-registration:";
    public const string AbsentRegistrationKey = "__ep_key_absent__";
    public const string Clsid = "{D17F1E1A-5919-4427-8F89-A1A8503CA3EB}";
    public static string InstallDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "ExplorerPatcher");
    public static string NativeDll => Path.Combine(InstallDirectory, RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "ExplorerPatcher.arm64.dll" : "ExplorerPatcher.amd64.dll");
    public static IReadOnlyList<(string Key, string Name, string Value, ChangeValueType Type)> RegistrationValues =>
    [
        (@"HKLM\SOFTWARE\Classes\CLSID\" + Clsid + @"\InProcServer32", "", NativeDll, ChangeValueType.Registry_String),
        (@"HKLM\SOFTWARE\Classes\CLSID\" + Clsid + @"\InProcServer32", "ThreadingModel", "Apartment", ChangeValueType.Registry_String),
        (@"HKLM\SOFTWARE\WOW6432Node\Classes\CLSID\" + Clsid + @"\InProcServer32", "", Path.Combine(InstallDirectory, "ExplorerPatcher.IA-32.dll"), ChangeValueType.Registry_String),
        (@"HKLM\SOFTWARE\WOW6432Node\Classes\CLSID\" + Clsid + @"\InProcServer32", "ThreadingModel", "Apartment", ChangeValueType.Registry_String),
        (@"HKLM\SOFTWARE\Classes\Drive\shellex\FolderExtensions\" + Clsid, "DriveMask", "255", ChangeValueType.Registry_DWord),
    ];
    public static bool IsRegistered(IRegistryService registry) => RegistrationValues.All(v => v.Type == ChangeValueType.Registry_DWord
        ? registry.ReadDWord(v.Key, v.Name) is { IsSuccess: true, Value: 255 }
        : registry.ReadString(v.Key, v.Name) is { IsSuccess: true, Value: var value } && string.Equals(value, v.Value, StringComparison.OrdinalIgnoreCase));
    public static ChangeGroup Register(IRegistryService registry, bool enabled, Func<string, bool>? fileExists = null)
    {
        fileExists ??= File.Exists;
        if (enabled && (!fileExists(NativeDll) || !fileExists(Path.Combine(InstallDirectory, "ExplorerPatcher.IA-32.dll"))))
            throw new InvalidOperationException("ExplorerPatcher shell-extension files are missing.");
        var changes = new List<ChangeDescriptor>();
        foreach (var v in RegistrationValues)
        {
            var exists = registry.ValueExists(v.Key, v.Name);
            if (!exists.IsSuccess) throw new InvalidOperationException("Shell-extension registration could not be read.");
            var keyExists = registry.KeyExists(v.Key);
            if (!keyExists.IsSuccess) throw new InvalidOperationException("Shell-extension registration key could not be read.");
            var before = keyExists.Value ? ShellRegistryPaths.AbsentValue : AbsentRegistrationKey;
            if (exists.Value)
            {
                var read = registry.ReadValue(v.Key, v.Name);
                var kind = v.Type == ChangeValueType.Registry_DWord ? RegistryValueDataKind.DWord : RegistryValueDataKind.String;
                if (!read.IsSuccess || read.Value!.Kind != kind)
                    throw new InvalidOperationException("Shell-extension registration could not be captured with its original type.");
                before = read.Value.Data;
                if (!string.Equals(before, v.Value, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Shell-extension registration contains another configuration.");
            }
            changes.Add(new ChangeDescriptor
            {
                ModuleId = "Explorer", SettingId = Registration + changes.Count.ToString(CultureInfo.InvariantCulture),
                DisplayName = "Register ExplorerPatcher as a shell extension", SystemLocation = v.Key + "\\" + v.Name,
                BeforeValue = before, AfterValue = enabled ? v.Value : ShellRegistryPaths.AbsentValue,
                BeforeDisplay = exists.Value ? "Registered" : "Not registered", AfterDisplay = enabled ? "Registered" : "Not registered",
                ValueType = v.Type, Category = enabled ? ChangeCategory.Enable : ChangeCategory.Disable,
                RestartRequirement = RestartRequirement.ExplorerRestart,
            });
        }
        return new() { GroupId = Guid.NewGuid().ToString(), DisplayName = "ExplorerPatcher shell extension", Description = "Register the installed ExplorerPatcher DLLs.", Changes = changes };
    }
    public static ChangeDescriptor Native(string location, string before, bool on)
    {
        var taskbar = location == Taskbar;
        if (!ValidNative(location, before)) throw new InvalidOperationException("The setting could not be read safely.");
        var after = taskbar ? ((uint.Parse(before, CultureInfo.InvariantCulture) & ~1u) | (on ? 1u : 0u)).ToString(CultureInfo.InvariantCulture)
            : on ? "Automatic|Running" : before == "absent" ? "absent" : "Disabled|Stopped";
        return new() { ModuleId = "Explorer", SettingId = location, SystemLocation = location,
            DisplayName = taskbar ? "Automatically hide the taskbar" : "Disable rounded corners for application windows",
            BeforeValue = before, AfterValue = after, BeforeDisplay = IsOn(location, before) ? "On" : "Off", AfterDisplay = on ? "On" : "Off",
            ValueType = ChangeValueType.Shell_NativeSetting, Category = on ? ChangeCategory.Enable : ChangeCategory.Disable };
    }
    public static bool NavigationDisabled(IRegistryService registry) =>
        registry.ReadValue(NavigationKey, "") is { IsSuccess: true, Value.Kind: RegistryValueDataKind.String, Value.Data: "" };
    public static ChangeDescriptor NavigationChange(IRegistryService registry, bool disabled)
    {
        var exists = registry.ValueExists(NavigationKey, "");
        var keyExists = registry.KeyExists(NavigationKey);
        if (!exists.IsSuccess || !keyExists.IsSuccess || exists.Value && !NavigationDisabled(registry))
            throw new InvalidOperationException("The navigation bar setting could not be captured safely.");
        return new() { ModuleId = "Explorer", SettingId = Navigation, DisplayName = "Disable navigation bar",
            SystemLocation = NavigationKey + "\\", BeforeValue = exists.Value ? "" : keyExists.Value ? ShellRegistryPaths.AbsentValue : AbsentRegistrationKey,
            AfterValue = disabled ? "" : ShellRegistryPaths.AbsentValue, BeforeDisplay = exists.Value ? "On" : "Off",
            AfterDisplay = disabled ? "On" : "Off", ValueType = ChangeValueType.Registry_String,
            Category = disabled ? ChangeCategory.Enable : ChangeCategory.Disable, RestartRequirement = RestartRequirement.ExplorerRestart };
    }
    public static bool IsOn(string location, string state) => location == Taskbar ? uint.TryParse(state, out var bits) && (bits & 1) != 0 : state.EndsWith("|Running", StringComparison.Ordinal);
    public static bool ValidNative(string location, string? value) => value is not null && (location == Taskbar
        ? uint.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var bits) && bits <= 3
        : location == Corners && ShellNativeSettings.ValidCornersState(value));
    public static bool Allows(ChangeDescriptor change)
    {
        if (change.SettingId == Navigation)
            return change.SystemLocation == NavigationKey + "\\" && change.ValueType == ChangeValueType.Registry_String
                && change.BeforeValue is "" or ShellRegistryPaths.AbsentValue or AbsentRegistrationKey
                && change.AfterValue is "" or ShellRegistryPaths.AbsentValue or AbsentRegistrationKey;
        if (change.ValueType == ChangeValueType.Shell_NativeSetting)
            return change.SettingId == change.SystemLocation && ValidNative(change.SystemLocation, change.BeforeValue) && ValidNative(change.SystemLocation, change.AfterValue);
        return RegistrationValues.Select((v, i) => (v, i)).Any(pair => change.SettingId == Registration + pair.i.ToString(CultureInfo.InvariantCulture)
            && change.SystemLocation.Equals(pair.v.Key + "\\" + pair.v.Name, StringComparison.OrdinalIgnoreCase)
            && change.ValueType == pair.v.Type
            && (change.BeforeValue is ShellRegistryPaths.AbsentValue or AbsentRegistrationKey || string.Equals(change.BeforeValue, pair.v.Value, StringComparison.OrdinalIgnoreCase))
            && (change.AfterValue is ShellRegistryPaths.AbsentValue or AbsentRegistrationKey || string.Equals(change.AfterValue, pair.v.Value, StringComparison.OrdinalIgnoreCase)));
    }
}
