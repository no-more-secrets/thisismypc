using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Interop.Com.Shell;
using ThisIsMyPC.Modules.Shell.Tests.Fakes;

namespace ThisIsMyPC.Modules.Shell.Tests;

/// <summary>
/// Tests for toggle hardening: dash-prefix best-effort, static verb HKCU override,
/// and orphan cleanup resilience against TrustedInstaller-protected keys.
/// </summary>
public sealed class ContextMenuModuleHardeningTests
{
    private readonly FakeRegistryService _registry = new();
    private readonly ContextMenuModule _module;

    public ContextMenuModuleHardeningTests()
    {
        var shellExtSvc = new ShellExtensionService(_registry);
        _module = new ContextMenuModule(_registry, shellExtSvc, new NullContextMenuProbe());
    }

    // ── Task 2: Dash-prefix best-effort (AC #1, #2) ──

    [Fact]
    public async Task DashPrefix_AccessDenied_returns_success_when_HKCR_protected()
    {
        // Simulate TrustedInstaller-owned HKCR key
        var handlerPath = @"HKCR\*\shellex\ContextMenuHandlers\OpenWith";
        _registry.AddKey(handlerPath);
        _registry.SetString(handlerPath, string.Empty, "{09799AFB-AD67-11d1-ABCD-00C04FC30936}");
        _registry.SetWriteFailure(handlerPath, ErrorCategory.AccessDenied);

        var change = new ChangeDescriptor
        {
            ModuleId = "Context Menus",
            SettingId = "ctx-handler-09799AFB-AD67-11d1-ABCD-00C04FC30936",
            DisplayName = "Context menu: Open With",
            SystemLocation = $@"{handlerPath}\(Default)",
            BeforeValue = "{09799AFB-AD67-11d1-ABCD-00C04FC30936}",
            AfterValue = "-{09799AFB-AD67-11d1-ABCD-00C04FC30936}",
            BeforeDisplay = "Enabled",
            AfterDisplay = "Disabled",
            ValueType = ChangeValueType.Registry_String,
            Category = ChangeCategory.Disable,
        };

        var result = await _module.ApplyChangeAsync(change);

        // Dash-prefix is best-effort: blocked list is primary mechanism
        Assert.True(result.IsSuccess, "Dash-prefix AccessDenied on HKCR should be treated as success");
    }

    [Fact]
    public async Task DashPrefix_revert_AccessDenied_returns_success_when_HKCR_protected()
    {
        var handlerPath = @"HKCR\Directory\shellex\ContextMenuHandlers\Sharing";
        _registry.AddKey(handlerPath);
        _registry.SetString(handlerPath, string.Empty, "-{f81e9010-6ea4-11ce-a7ff-00aa003ca9f6}");
        _registry.SetWriteFailure(handlerPath, ErrorCategory.AccessDenied);

        var change = new ChangeDescriptor
        {
            ModuleId = "Context Menus",
            SettingId = "ctx-handler-f81e9010-6ea4-11ce-a7ff-00aa003ca9f6",
            DisplayName = "Context menu: Shell extensions for sharing",
            SystemLocation = $@"{handlerPath}\(Default)",
            BeforeValue = "{f81e9010-6ea4-11ce-a7ff-00aa003ca9f6}",
            AfterValue = "-{f81e9010-6ea4-11ce-a7ff-00aa003ca9f6}",
            BeforeDisplay = "Enabled",
            AfterDisplay = "Disabled",
            ValueType = ChangeValueType.Registry_String,
            Category = ChangeCategory.Disable,
        };

        var result = await _module.RevertChangeAsync(change);

        Assert.True(result.IsSuccess, "Dash-prefix revert AccessDenied on HKCR should be treated as success");
    }

    [Fact]
    public async Task BlockedList_AccessDenied_still_fails_normally()
    {
        // Non-HKCR paths (e.g., HKLM blocked list in restricted GPO env) should still fail
        var blockedPath = @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Shell Extensions\Blocked";
        _registry.AddKey(blockedPath);
        _registry.SetWriteFailure(blockedPath, ErrorCategory.AccessDenied);

        var change = new ChangeDescriptor
        {
            ModuleId = "Context Menus",
            SettingId = "ctx-handler-12345678-1234-1234-1234-123456789ABC",
            DisplayName = "Context menu: TestHandler",
            SystemLocation = $@"{blockedPath}\{{12345678-1234-1234-1234-123456789ABC}}",
            BeforeValue = "__absent__",
            AfterValue = "",
            BeforeDisplay = "Enabled",
            AfterDisplay = "Disabled",
            ValueType = ChangeValueType.Registry_String,
            Category = ChangeCategory.Disable,
        };

        var result = await _module.ApplyChangeAsync(change);

        Assert.False(result.IsSuccess, "HKLM AccessDenied should fail normally");
        Assert.Equal(ErrorCategory.AccessDenied, result.ErrorCategory);
    }

    [Fact]
    public async Task DashPrefix_non_HKCR_path_AccessDenied_still_fails()
    {
        // An HKLM context menu handler path should still fail (not best-effort)
        var handlerPath = @"HKLM\SOFTWARE\Classes\*\shellex\ContextMenuHandlers\TestHandler";
        _registry.AddKey(handlerPath);
        _registry.SetWriteFailure(handlerPath, ErrorCategory.AccessDenied);

        var change = new ChangeDescriptor
        {
            ModuleId = "Context Menus",
            SettingId = "ctx-handler-AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE",
            DisplayName = "Context menu: TestHandler",
            SystemLocation = $@"{handlerPath}\(Default)",
            BeforeValue = "{AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE}",
            AfterValue = "-{AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE}",
            BeforeDisplay = "Enabled",
            AfterDisplay = "Disabled",
            ValueType = ChangeValueType.Registry_String,
            Category = ChangeCategory.Disable,
        };

        var result = await _module.ApplyChangeAsync(change);

        // Only HKCR paths get best-effort treatment
        Assert.False(result.IsSuccess);
    }

    // ── Task 3: Static verb HKCU override (AC #3) ──

    [Fact]
    public async Task StaticVerb_LegacyDisable_targets_HKCU_path()
    {
        // Static verb change should write to HKCU\Software\Classes, not HKCR
        var hkcuVerbPath = @"HKCU\Software\Classes\*\shell\edit";
        _registry.AddKey(hkcuVerbPath);

        var change = new ChangeDescriptor
        {
            ModuleId = "Context Menus",
            SettingId = "ctx-verb-edit-all-files",
            DisplayName = "Context menu: Edit",
            SystemLocation = $@"{hkcuVerbPath}\LegacyDisable",
            BeforeValue = "__absent__",
            AfterValue = "",
            BeforeDisplay = "Enabled",
            AfterDisplay = "Disabled",
            ValueType = ChangeValueType.Registry_String,
            Category = ChangeCategory.Disable,
        };

        var result = await _module.ApplyChangeAsync(change);

        Assert.True(result.IsSuccess);
        var exists = _registry.ValueExists(hkcuVerbPath, "LegacyDisable");
        Assert.True(exists.Value);
    }

    // ── Task 4: Orphan cleanup resilience (AC #4) ──

    [Fact]
    public async Task OrphanCleanup_AccessDenied_on_HKCR_fails_honestly()
    {
        // Orphan at TrustedInstaller-owned HKCR path — cleanup must fail so orphan stays flagged
        var orphanPath = @"HKCR\*\shellex\ContextMenuHandlers\DeadHandler";
        _registry.AddKey(orphanPath);
        _registry.SetString(orphanPath, string.Empty, "{DEADBEEF-0000-0000-0000-000000000000}");
        _registry.SetDeleteFailure(orphanPath, ErrorCategory.AccessDenied);

        var change = new ChangeDescriptor
        {
            ModuleId = "Context Menus",
            SettingId = "ctx-handler-DEADBEEF-0000-0000-0000-000000000000",
            DisplayName = "Clean up orphaned handler: DeadHandler",
            SystemLocation = $@"{orphanPath}\(Default)",
            BeforeValue = "{DEADBEEF-0000-0000-0000-000000000000}",
            AfterValue = "__absent__",
            BeforeDisplay = "Orphaned registration",
            AfterDisplay = "Removed",
            ValueType = ChangeValueType.Registry_String,
            Category = ChangeCategory.Delete,
        };

        var result = await _module.ApplyChangeAsync(change);

        // Orphan cleanup must fail — the orphan needs to remain flagged in UI
        Assert.False(result.IsSuccess, "Orphan cleanup should fail when HKCR delete is AccessDenied");
        Assert.Equal(ErrorCategory.AccessDenied, result.ErrorCategory);
    }

    private sealed class NullContextMenuProbe : IContextMenuProbe
    {
        public OperationResult<bool> HandlerAppearsOnSurface(string clsid, ContextMenuSurface surface)
            => OperationResult<bool>.Success(true);
    }
}
