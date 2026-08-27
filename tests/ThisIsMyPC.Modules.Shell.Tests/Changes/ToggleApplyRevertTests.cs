using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Interop.Com.Shell;
using ThisIsMyPC.Modules.Shell.Changes;
using ThisIsMyPC.Modules.Shell.Models;
using ThisIsMyPC.Modules.Shell.Tests.Fakes;

namespace ThisIsMyPC.Modules.Shell.Tests.Changes;

/// <summary>
/// Integration tests: PendingChangesService + ContextMenuModule apply-revert cycle
/// for every toggle mechanism (blocked list, dash-prefix, static verb, orphan cleanup).
/// </summary>
public sealed class ToggleApplyRevertTests
{
    private readonly FakeRegistryService _registry = new();
    // Disable-direction toggles carry informational enforcement metadata (Story 26-4),
    // so applying them requires an executor.
    private readonly PendingChangesService _pendingChanges = new(new PassthroughEnforcementExecutor());
    private readonly ContextMenuModule _module;

    public ToggleApplyRevertTests()
    {
        var shellExtSvc = new ShellExtensionService(_registry);
        _module = new ContextMenuModule(_registry, shellExtSvc, new NullContextMenuProbe());
    }

    // ── 5.1: COM handler toggle via blocked list ──

    [Fact]
    public async Task BlockedList_toggle_disable_enable_cycle()
    {
        var blockedPath = ShellRegistryPaths.BlockedListKeyPath;
        _registry.AddKey(blockedPath);

        var handler = MakeComHandler("{AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE}", "TestHandler");

        // Stage disable (add to blocked list)
        var disableChange = ContextMenuChangeFactory.CreateBlockedListToggle(handler, enable: false);
        _pendingChanges.Stage(disableChange);

        var disableResult = await _pendingChanges.ApplyAllAsync(
            _module.ApplyChangeAsync, _module.RevertChangeAsync);

        Assert.True(disableResult.IsSuccess);
        var readResult = _registry.ReadString(blockedPath, "{AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE}");
        Assert.True(readResult.IsSuccess, "Blocked list entry should exist after disable");

        // Stage enable (remove from blocked list)
        var enableHandler = handler with { IsEnabled = false, DisableMethod = DisableMethod.BlockedList };
        var enableChange = ContextMenuChangeFactory.CreateBlockedListToggle(enableHandler, enable: true);
        _pendingChanges.Stage(enableChange);

        var enableResult = await _pendingChanges.ApplyAllAsync(
            _module.ApplyChangeAsync, _module.RevertChangeAsync);

        Assert.True(enableResult.IsSuccess);
        var existsResult = _registry.ValueExists(blockedPath, "{AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE}");
        Assert.False(existsResult.Value, "Blocked list entry should be gone after enable");
    }

    // ── 5.2: Static verb LegacyDisable (via HKCU overlay) ──

    [Fact]
    public async Task StaticVerb_LegacyDisable_cycle_uses_HKCU()
    {
        var hkcuVerbPath = @"HKCU\Software\Classes\*\shell\edit";
        _registry.AddKey(hkcuVerbPath);

        var handler = MakeStaticVerbHandler("edit", "Edit", isEnabled: true,
            registryPath: @"HKCR\*\shell\edit");

        // Stage disable
        var disableChanges = ContextMenuChangeFactory.CreateStaticVerbToggle(handler, enable: false);
        var disableGroup = new ChangeGroup
        {
            GroupId = "disable-edit",
            DisplayName = "Disable edit",
            Description = "Disable edit verb",
            Changes = [.. disableChanges],
        };
        _pendingChanges.Stage(disableGroup);

        var disableResult = await _pendingChanges.ApplyAllAsync(
            _module.ApplyChangeAsync, _module.RevertChangeAsync);

        Assert.True(disableResult.IsSuccess);
        // Verify LegacyDisable was written to HKCU overlay, not HKCR
        var legacyExists = _registry.ValueExists(hkcuVerbPath, "LegacyDisable");
        Assert.True(legacyExists.Value, "LegacyDisable should exist at HKCU path");

        // Stage enable (remove LegacyDisable)
        var disabledHandler = MakeStaticVerbHandler("edit", "Edit", isEnabled: false,
            registryPath: @"HKCR\*\shell\edit");
        var enableChanges = ContextMenuChangeFactory.CreateStaticVerbToggle(disabledHandler, enable: true);
        var enableGroup = new ChangeGroup
        {
            GroupId = "enable-edit",
            DisplayName = "Enable edit",
            Description = "Enable edit verb",
            Changes = [.. enableChanges],
        };
        _pendingChanges.Stage(enableGroup);

        var enableResult = await _pendingChanges.ApplyAllAsync(
            _module.ApplyChangeAsync, _module.RevertChangeAsync);

        Assert.True(enableResult.IsSuccess);
        var legacyGone = _registry.ValueExists(hkcuVerbPath, "LegacyDisable");
        Assert.False(legacyGone.Value, "LegacyDisable should be removed after enable");
    }

    // ── 5.3: Orphan cleanup ──

    [Fact]
    public async Task OrphanCleanup_removes_handler_registration()
    {
        var orphanPath = @"HKCR\*\shellex\ContextMenuHandlers\DeadApp";
        _registry.AddKey(orphanPath);
        _registry.SetString(orphanPath, string.Empty, "{DEADBEEF-0000-0000-0000-000000000000}");

        var orphan = new ContextMenuHandler(
            Name: "DeadApp",
            Clsid: "{DEADBEEF-0000-0000-0000-000000000000}",
            RegistryPath: orphanPath,
            AppliesTo: "All files",
            DllPath: @"C:\Missing\dead.dll",
            Publisher: null,
            IsEnabled: true,
            AllRegistryPaths: [orphanPath],
            PathEnabledStates: new Dictionary<string, bool> { [orphanPath] = true },
            IsOrphaned: true,
            OrphanReason: "DLL missing: C:\\Missing\\dead.dll");

        var cleanupGroup = ContextMenuChangeFactory.CreateOrphanCleanup(orphan);
        _pendingChanges.Stage(cleanupGroup);

        var result = await _pendingChanges.ApplyAllAsync(
            _module.ApplyChangeAsync, _module.RevertChangeAsync);

        Assert.True(result.IsSuccess);
        // Default value should be deleted (AbsentValue target)
        var exists = _registry.ValueExists(orphanPath, string.Empty);
        Assert.False(exists.Value, "Orphan handler registration should be deleted");
    }

    // ── 5.4: ChangeGroup with simulated failure → rollback ──

    [Fact]
    public async Task ChangeGroup_partial_failure_rolls_back_prior_writes()
    {
        var blockedPath = ShellRegistryPaths.BlockedListKeyPath;
        _registry.AddKey(blockedPath);

        var handlerPath = @"HKCR\*\shellex\ContextMenuHandlers\TestHandler";
        _registry.AddKey(handlerPath);
        _registry.SetString(handlerPath, string.Empty, "{11111111-2222-3333-4444-555555555555}");

        // Simulate: blocked list write succeeds but an HKLM path fails
        // (not HKCR, so best-effort doesn't apply — simulates a non-HKCR failure)
        var hklmPath = @"HKLM\SOFTWARE\Classes\*\shellex\ContextMenuHandlers\TestHandler";
        _registry.AddKey(hklmPath);
        _registry.SetWriteFailure(hklmPath, ErrorCategory.AccessDenied);

        var changes = new List<ChangeDescriptor>
        {
            new()
            {
                ModuleId = "Context Menus",
                SettingId = "ctx-handler-11111111-2222-3333-4444-555555555555",
                DisplayName = "Context menu: TestHandler",
                SystemLocation = $@"{blockedPath}\{{11111111-2222-3333-4444-555555555555}}",
                BeforeValue = "__absent__",
                AfterValue = "",
                ValueType = ChangeValueType.Registry_String,
                Category = ChangeCategory.Disable,
                BeforeDisplay = "Enabled",
                AfterDisplay = "Disabled",
            },
            new()
            {
                ModuleId = "Context Menus",
                SettingId = "ctx-handler-11111111-2222-3333-4444-555555555555",
                DisplayName = "Context menu: TestHandler",
                SystemLocation = $@"{hklmPath}\(Default)",
                BeforeValue = "{11111111-2222-3333-4444-555555555555}",
                AfterValue = "-{11111111-2222-3333-4444-555555555555}",
                ValueType = ChangeValueType.Registry_String,
                Category = ChangeCategory.Disable,
                BeforeDisplay = "Enabled",
                AfterDisplay = "Disabled",
            },
        };

        var group = new ChangeGroup
        {
            GroupId = "test-partial-fail",
            DisplayName = "Toggle TestHandler",
            Description = "Test partial failure rollback",
            Changes = changes,
        };
        _pendingChanges.Stage(group);

        var result = await _pendingChanges.ApplyAllAsync(
            _module.ApplyChangeAsync, _module.RevertChangeAsync);

        Assert.False(result.IsSuccess, "HKLM path failure should cause group failure");
        Assert.Equal(ErrorCategory.AccessDenied, result.ErrorCategory);

        // Blocked list write should have been rolled back
        var blockedExists = _registry.ValueExists(blockedPath, "{11111111-2222-3333-4444-555555555555}");
        Assert.False(blockedExists.Value, "Blocked list entry should be rolled back after group failure");
    }

    // ── 6.5: Combined toggle (blocked list + dash-prefix) for system handler ──

    [Fact]
    public async Task SystemHandler_toggle_succeeds_via_blocked_list_despite_HKCR_AccessDenied()
    {
        // "Shell extensions for sharing" registered at *, Directory, Drive (3 HKCR paths)
        var blockedPath = ShellRegistryPaths.BlockedListKeyPath;
        _registry.AddKey(blockedPath);

        var paths = new[]
        {
            @"HKCR\*\shellex\ContextMenuHandlers\SharingExtensions",
            @"HKCR\Directory\shellex\ContextMenuHandlers\SharingExtensions",
            @"HKCR\Drive\shellex\ContextMenuHandlers\SharingExtensions",
        };

        foreach (var path in paths)
        {
            _registry.AddKey(path);
            _registry.SetString(path, string.Empty, "{f81e9010-6ea4-11ce-a7ff-00aa003ca9f6}");
            // Simulate TrustedInstaller on all HKCR paths
            _registry.SetWriteFailure(path, ErrorCategory.AccessDenied);
        }

        var handler = new ContextMenuHandler(
            Name: "Shell extensions for sharing",
            Clsid: "{f81e9010-6ea4-11ce-a7ff-00aa003ca9f6}",
            RegistryPath: paths[0],
            AppliesTo: "All files",
            DllPath: @"C:\Windows\System32\ntshrui.dll",
            Publisher: "Microsoft",
            IsEnabled: true,
            Classification: HandlerClassification.Critical,
            AllRegistryPaths: paths,
            PathEnabledStates: paths.ToDictionary(p => p, _ => true));

        // Create the same changes the ViewModel would create
        var blockedListChange = ContextMenuChangeFactory.CreateBlockedListToggle(handler, enable: false);
        var dashPrefixChanges = ContextMenuChangeFactory.CreateToggle(handler, enable: false);
        var group = new ChangeGroup
        {
            GroupId = "system-handler-disable",
            DisplayName = "Context menu: Shell extensions for sharing",
            Description = "Toggle sharing handler",
            Changes = [blockedListChange, .. dashPrefixChanges],
        };
        _pendingChanges.Stage(group);

        var result = await _pendingChanges.ApplyAllAsync(
            _module.ApplyChangeAsync, _module.RevertChangeAsync);

        // Should succeed: blocked list works, dash-prefix failures are best-effort
        Assert.True(result.IsSuccess, "System handler toggle should succeed via blocked list even when HKCR dash-prefix fails");

        // Blocked list entry should be written
        var blockedExists = _registry.ReadString(blockedPath, "{f81e9010-6ea4-11ce-a7ff-00aa003ca9f6}");
        Assert.True(blockedExists.IsSuccess, "CLSID should be in blocked list");

        // Dash-prefix values should be unchanged (writes failed, which is OK)
        var hkcrValue = _registry.ReadString(paths[0], string.Empty);
        Assert.Equal("{f81e9010-6ea4-11ce-a7ff-00aa003ca9f6}", hkcrValue.Value);
    }

    // ── Helpers ──

    private static ContextMenuHandler MakeComHandler(string clsid, string name, bool isEnabled = true)
    {
        return new ContextMenuHandler(
            Name: name,
            Clsid: clsid,
            RegistryPath: $@"HKCR\*\shellex\ContextMenuHandlers\{name}",
            AppliesTo: "All files",
            DllPath: null,
            Publisher: null,
            IsEnabled: isEnabled);
    }

    private static ContextMenuHandler MakeStaticVerbHandler(
        string verbName, string displayName, bool isEnabled, string registryPath)
    {
        return new ContextMenuHandler(
            Name: displayName,
            Clsid: string.Empty,
            RegistryPath: registryPath,
            AppliesTo: "All files",
            DllPath: null,
            Publisher: null,
            IsEnabled: isEnabled,
            AllRegistryPaths: [registryPath],
            PathEnabledStates: new Dictionary<string, bool> { [registryPath] = isEnabled },
            HandlerType: HandlerType.StaticVerb,
            VerbInfo: new StaticVerbInfo(
                VerbName: verbName, MuiVerb: displayName,
                Icon: null, Position: null, IsExtended: false,
                CommandLine: null, DelegateExecuteClsid: null,
                IsLegacyDisabled: !isEnabled, AppliesTo: null,
                HasLuaShield: false, IsProgrammaticAccessOnly: false));
    }

    private sealed class NullContextMenuProbe : IContextMenuProbe
    {
        public OperationResult<bool> HandlerAppearsOnSurface(string clsid, ContextMenuSurface surface)
            => OperationResult<bool>.Success(true);
    }
}
