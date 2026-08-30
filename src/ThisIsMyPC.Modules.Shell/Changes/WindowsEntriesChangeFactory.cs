using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Modules.Shell.Models;

namespace ThisIsMyPC.Modules.Shell.Changes;

/// <summary>
/// One curated Windows context-menu entry: a named toggle with live state read
/// and a stage-time group factory (before-state is read at stage time, never
/// baked from a scan).
/// </summary>
public sealed record WindowsMenuEntry(
    string SettingId,
    string Label,
    string Description,
    string SystemLocation,
    Func<IRegistryService, bool> ReadState,
    Func<IRegistryService, bool, ChangeGroup> CreateToggle);

/// <summary>
/// Curated "Windows entries" catalog. Recipes ported from Sophia Script (MIT,
/// (c) farag, Inestic and lotpyre); never shells out to it. All paths use real
/// HKCR (the app runs elevated): reads and writes must hit the same store,
/// because an HKCU overlay can neither remove an HKLM-backed key nor unmask an
/// HKLM-backed value from the merged view.
/// </summary>
public static class WindowsEntriesChangeFactory
{
    public const string ModuleId = "Context Menus";

    // Root key paths (write side). Additive verbs use the HKCU overlay.
    public const string MsiExtractKeyPath = @"HKCR\Msi.Package\shell\Extract";
    public const string CabInstallKeyPath = @"HKCR\CABFolder\Shell\runas";
    public const string ZipShellNewKeyPath = @"HKCR\.zip\CompressedFolder\ShellNew";

    /// <summary>Key paths the module accepts Registry_KeyTree changes for.</summary>
    public static readonly IReadOnlySet<string> KeyTreeAllowlist = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        MsiExtractKeyPath,
        CabInstallKeyPath,
        ZipShellNewKeyPath,
    };

    // Blocked-CLSID entries (Windows 11 modern-menu handlers).
    public const string ClipchampClsid = "{8AB635F8-9A67-4698-AB99-784AD929F3B4}";
    public const string PhotosEditClsid = "{BFE0E2A4-C70C-4AD7-AC3D-10D1ECEBB5B4}";
    public const string PaintEditClsid = "{2430F218-B743-4FD6-97BF-5C76541B4AE9}";

    private const string BatPrintKeyPath = @"HKCR\batfile\shell\print";
    private const string CmdPrintKeyPath = @"HKCR\cmdfile\shell\print";
    private const string ExplorerKeyPath = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer";
    private const string ExplorerPolicyKeyPath = @"HKCU\Software\Policies\Microsoft\Windows\Explorer";

    // PK\x05\x06 end-of-central-directory record: a valid empty zip.
    private const string EmptyZipBase64 = "UEsFBgAAAAAAAAAAAAAAAAAAAAAAAA==";

    public static IReadOnlyList<WindowsMenuEntry> Catalog { get; } =
    [
        new(
            SettingId: "ctx-win-msi-extract",
            Label: "Extract all on MSI installers",
            Description: "Adds an Extract all entry to .msi files. Unpacks the installer next to it without installing.",
            SystemLocation: MsiExtractKeyPath,
            ReadState: reg => reg.KeyExists(MsiExtractKeyPath) is { IsSuccess: true, Value: true },
            CreateToggle: (reg, enable) => KeyTreeToggle(
                reg, "ctx-win-msi-extract", "Extract all on MSI installers", MsiExtractKeyPath, enable,
                new RegistryKeyTreeDefinition
                {
                    Values =
                    [
                        new("", "MUIVerb", RegistryKeyTreeValueKind.String, "@shell32.dll,-37514"),
                        new("", "Icon", RegistryKeyTreeValueKind.String, @"%SystemRoot%\System32\shell32.dll,-16817"),
                        new("Command", "", RegistryKeyTreeValueKind.String,
                            "msiexec.exe /a \"%1\" /qb TARGETDIR=\"%1 extracted\""),
                    ],
                })),
        new(
            SettingId: "ctx-win-cab-install",
            Label: "Install on CAB packages",
            Description: "Adds an elevated Install entry to .cab files. Runs DISM to add the package to Windows.",
            SystemLocation: CabInstallKeyPath,
            ReadState: reg => reg.KeyExists(CabInstallKeyPath) is { IsSuccess: true, Value: true },
            CreateToggle: (reg, enable) => KeyTreeToggle(
                reg, "ctx-win-cab-install", "Install on CAB packages", CabInstallKeyPath, enable,
                new RegistryKeyTreeDefinition
                {
                    Values =
                    [
                        new("", "MUIVerb", RegistryKeyTreeValueKind.String, "@shell32.dll,-10210"),
                        new("", "HasLUAShield", RegistryKeyTreeValueKind.String, ""),
                        new("Command", "", RegistryKeyTreeValueKind.String,
                            "cmd /c DISM.exe /Online /Add-Package /PackagePath:\"%1\" /NoRestart & pause"),
                    ],
                })),
        new(
            SettingId: "ctx-win-new-zip",
            Label: "New Compressed folder",
            Description: "Keeps Compressed (zipped) folder in the New menu. Turning it off removes the entry.",
            SystemLocation: ZipShellNewKeyPath,
            ReadState: reg => reg.KeyExists(ZipShellNewKeyPath) is { IsSuccess: true, Value: true },
            CreateToggle: (reg, enable) => KeyTreeToggle(
                reg, "ctx-win-new-zip", "New Compressed folder", ZipShellNewKeyPath, enable,
                new RegistryKeyTreeDefinition
                {
                    Values =
                    [
                        new("", "Data", RegistryKeyTreeValueKind.Binary, EmptyZipBase64),
                        new("", "ItemName", RegistryKeyTreeValueKind.ExpandString,
                            @"@%SystemRoot%\System32\zipfldr.dll,-10194"),
                    ],
                })),
        new(
            SettingId: "ctx-win-edit-clipchamp",
            Label: "Edit with Clipchamp",
            Description: "Shows the Edit with Clipchamp entry on video files. Only appears while Clipchamp is installed.",
            SystemLocation: $@"{ShellRegistryPaths.BlockedListKeyPath}\{ClipchampClsid}",
            ReadState: reg => reg.ValueExists(ShellRegistryPaths.BlockedListKeyPath, ClipchampClsid) is not { IsSuccess: true, Value: true },
            CreateToggle: (reg, enable) => BlockedClsidToggle(
                reg, "ctx-win-edit-clipchamp", "Edit with Clipchamp", ClipchampClsid, enable)),
        new(
            SettingId: "ctx-win-edit-photos",
            Label: "Edit with Photos",
            Description: "Shows the Edit with Photos entry on image files.",
            SystemLocation: $@"{ShellRegistryPaths.BlockedListKeyPath}\{PhotosEditClsid}",
            ReadState: reg => reg.ValueExists(ShellRegistryPaths.BlockedListKeyPath, PhotosEditClsid) is not { IsSuccess: true, Value: true },
            CreateToggle: (reg, enable) => BlockedClsidToggle(
                reg, "ctx-win-edit-photos", "Edit with Photos", PhotosEditClsid, enable)),
        new(
            SettingId: "ctx-win-edit-paint",
            Label: "Edit with Paint",
            Description: "Shows the Edit with Paint entry on image files.",
            SystemLocation: $@"{ShellRegistryPaths.BlockedListKeyPath}\{PaintEditClsid}",
            ReadState: reg => reg.ValueExists(ShellRegistryPaths.BlockedListKeyPath, PaintEditClsid) is not { IsSuccess: true, Value: true },
            CreateToggle: (reg, enable) => BlockedClsidToggle(
                reg, "ctx-win-edit-paint", "Edit with Paint", PaintEditClsid, enable)),
        new(
            SettingId: "ctx-win-print-scripts",
            Label: "Print on batch files",
            Description: "Shows the Print entry on .bat and .cmd files. Turning it off hides it from both.",
            SystemLocation: $@"{BatPrintKeyPath}\ProgrammaticAccessOnly",
            ReadState: reg =>
                reg.ValueExists(@"HKCR\batfile\shell\print", "ProgrammaticAccessOnly") is not { IsSuccess: true, Value: true }
                && reg.ValueExists(@"HKCR\cmdfile\shell\print", "ProgrammaticAccessOnly") is not { IsSuccess: true, Value: true },
            CreateToggle: (reg, enable) => PrintScriptsToggle(reg, enable)),
        new(
            SettingId: "ctx-win-multi-select-verbs",
            Label: "Full menu on 15+ selected files",
            Description: "Windows hides Open, Print, and Edit when more than 15 files are selected. Raises the limit to 300.",
            SystemLocation: $@"{ExplorerKeyPath}\MultipleInvokePromptMinimum",
            ReadState: reg => reg.ReadDWord(ExplorerKeyPath, "MultipleInvokePromptMinimum")
                is { IsSuccess: true, Value: >= 300 },
            CreateToggle: (reg, enable) => DwordToggle(
                reg, "ctx-win-multi-select-verbs", "Full menu on 15+ selected files",
                ExplorerKeyPath, "MultipleInvokePromptMinimum", enabledValue: 300, enable)),
        new(
            SettingId: "ctx-win-store-open-with",
            Label: "Search the Microsoft Store in Open with",
            Description: "Shows the Store suggestion when opening an unknown file type. Turning it off sets the NoUseStoreOpenWith policy.",
            SystemLocation: $@"{ExplorerPolicyKeyPath}\NoUseStoreOpenWith",
            ReadState: reg => reg.ReadDWord(ExplorerPolicyKeyPath, "NoUseStoreOpenWith")
                is not { IsSuccess: true, Value: 1 },
            CreateToggle: (reg, enable) => DwordToggle(
                reg, "ctx-win-store-open-with", "Search the Microsoft Store in Open with",
                ExplorerPolicyKeyPath, "NoUseStoreOpenWith", enabledValue: null,
                enable, disabledValue: 1)),
    ];

    /// <summary>
    /// Enable materializes the tree; disable deletes the root key recursively.
    /// BeforeValue is a live snapshot of the existing tree (never our own
    /// definition), so undo restores exactly what the machine had.
    /// </summary>
    private static ChangeGroup KeyTreeToggle(
        IRegistryService registry, string settingId, string label, string keyPath,
        bool enable, RegistryKeyTreeDefinition definition)
    {
        var currentlyPresent = registry.KeyExists(keyPath) is { IsSuccess: true, Value: true };
        var change = new ChangeDescriptor
        {
            ModuleId = ModuleId,
            SettingId = settingId,
            DisplayName = $"Context menu: {label}",
            SystemLocation = keyPath,
            BeforeValue = currentlyPresent
                ? SnapshotKeyTree(registry, keyPath).Serialize()
                : ShellRegistryPaths.AbsentValue,
            AfterValue = enable ? definition.Serialize() : ShellRegistryPaths.AbsentValue,
            BeforeDisplay = currentlyPresent ? "Shown" : "Hidden",
            AfterDisplay = enable ? "Shown" : "Hidden",
            ValueType = ChangeValueType.Registry_KeyTree,
            Category = enable ? ChangeCategory.Enable : ChangeCategory.Disable,
            RestartRequirement = RestartRequirement.None,
        };
        return Wrap(change);
    }

    private static ChangeGroup BlockedClsidToggle(
        IRegistryService registry, string settingId, string label, string clsid, bool enable)
    {
        var currentlyBlocked = registry.ValueExists(ShellRegistryPaths.BlockedListKeyPath, clsid) is { IsSuccess: true, Value: true };
        var change = new ChangeDescriptor
        {
            ModuleId = ModuleId,
            SettingId = settingId,
            DisplayName = $"Context menu: {label}",
            SystemLocation = $@"{ShellRegistryPaths.BlockedListKeyPath}\{clsid}",
            BeforeValue = currentlyBlocked ? "" : ShellRegistryPaths.AbsentValue,
            AfterValue = enable ? ShellRegistryPaths.AbsentValue : "",
            BeforeDisplay = currentlyBlocked ? "Hidden" : "Shown",
            AfterDisplay = enable ? "Shown" : "Hidden",
            ValueType = ChangeValueType.Registry_String,
            Category = enable ? ChangeCategory.Enable : ChangeCategory.Disable,
            RestartRequirement = RestartRequirement.ExplorerRestart,
        };
        return Wrap(change);
    }

    /// <summary>Hide writes ProgrammaticAccessOnly on both script types (real HKCR paths).</summary>
    private static ChangeGroup PrintScriptsToggle(IRegistryService registry, bool enable)
    {
        var changes = new List<ChangeDescriptor>();
        foreach (var overlayPath in new[] { BatPrintKeyPath, CmdPrintKeyPath })
        {
            var currentlyHidden = registry.ValueExists(overlayPath, "ProgrammaticAccessOnly") is { IsSuccess: true, Value: true };
            changes.Add(new ChangeDescriptor
            {
                ModuleId = ModuleId,
                SettingId = "ctx-win-print-scripts",
                DisplayName = "Context menu: Print on batch files",
                SystemLocation = $@"{overlayPath}\ProgrammaticAccessOnly",
                BeforeValue = currentlyHidden ? "" : ShellRegistryPaths.AbsentValue,
                AfterValue = enable ? ShellRegistryPaths.AbsentValue : "",
                BeforeDisplay = currentlyHidden ? "Hidden" : "Shown",
                AfterDisplay = enable ? "Shown" : "Hidden",
                ValueType = ChangeValueType.Registry_String,
                Category = enable ? ChangeCategory.Enable : ChangeCategory.Disable,
                RestartRequirement = RestartRequirement.None,
            });
        }

        return new ChangeGroup
        {
            GroupId = Guid.NewGuid().ToString("N"),
            DisplayName = "Context menu: Print on batch files",
            Description = "Context menu: Print on batch files",
            Changes = changes,
        };
    }

    /// <summary>
    /// DWORD toggle where one direction is "value absent". enabledValue null means
    /// enable-by-delete (policy toggles); disabledValue null means disable-by-delete.
    /// </summary>
    private static ChangeGroup DwordToggle(
        IRegistryService registry, string settingId, string label,
        string keyPath, string valueName, int? enabledValue, bool enable, int? disabledValue = null)
    {
        var current = registry.ReadDWord(keyPath, valueName);
        var before = current is { IsSuccess: true }
            ? current.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : ShellRegistryPaths.AbsentValue;
        var after = enable
            ? enabledValue?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? ShellRegistryPaths.AbsentValue
            : disabledValue?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? ShellRegistryPaths.AbsentValue;
        var change = new ChangeDescriptor
        {
            ModuleId = ModuleId,
            SettingId = settingId,
            DisplayName = $"Context menu: {label}",
            SystemLocation = $@"{keyPath}\{valueName}",
            BeforeValue = before,
            AfterValue = after,
            BeforeDisplay = before == ShellRegistryPaths.AbsentValue ? "Not set" : before,
            AfterDisplay = after == ShellRegistryPaths.AbsentValue ? "Not set" : after,
            ValueType = ChangeValueType.Registry_DWord,
            Category = enable ? ChangeCategory.Enable : ChangeCategory.Disable,
            RestartRequirement = RestartRequirement.None,
        };
        return Wrap(change);
    }

    /// <summary>
    /// Recursively captures every value in a key tree so undo can restore the
    /// machine's real prior state, third-party additions included. Value kind
    /// is probed strictest-first (binary, expand string, string).
    /// </summary>
    private static RegistryKeyTreeDefinition SnapshotKeyTree(IRegistryService registry, string rootPath)
    {
        var values = new List<RegistryKeyTreeValue>();
        Collect(rootPath, "");
        return new RegistryKeyTreeDefinition { Values = values };

        void Collect(string keyPath, string relative)
        {
            if (registry.EnumerateValues(keyPath) is { IsSuccess: true, Value: { } names })
            {
                foreach (var name in names)
                {
                    if (registry.ReadBinary(keyPath, name) is { IsSuccess: true, Value: { } bin })
                        values.Add(new(relative, name, RegistryKeyTreeValueKind.Binary, Convert.ToBase64String(bin)));
                    else if (registry.ReadExpandString(keyPath, name) is { IsSuccess: true, Value: { } expand })
                        values.Add(new(relative, name, RegistryKeyTreeValueKind.ExpandString, expand));
                    else if (registry.ReadString(keyPath, name) is { IsSuccess: true, Value: { } text })
                        values.Add(new(relative, name, RegistryKeyTreeValueKind.String, text));
                }
            }

            if (registry.EnumerateSubKeys(keyPath) is { IsSuccess: true, Value: { } subKeys })
            {
                foreach (var subKey in subKeys)
                    Collect($@"{keyPath}\{subKey}", relative.Length == 0 ? subKey : $@"{relative}\{subKey}");
            }
        }
    }

    private static ChangeGroup Wrap(ChangeDescriptor change) => new()
    {
        GroupId = Guid.NewGuid().ToString("N"),
        DisplayName = change.DisplayName,
        Description = change.DisplayName,
        Changes = [change],
    };
}
