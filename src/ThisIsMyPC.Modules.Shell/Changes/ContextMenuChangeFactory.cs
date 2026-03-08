using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Modules.Shell.Models;

namespace ThisIsMyPC.Modules.Shell.Changes;

public static class ContextMenuChangeFactory
{
    private const string ModuleId = "Context Menus";

    /// <summary>
    /// Creates toggle change descriptors for a context menu handler.
    /// Produces one ChangeDescriptor per registry path, all sharing the same CLSID-based SettingId
    /// so PendingChangesService auto-groups them.
    /// </summary>
    public static IReadOnlyList<ChangeDescriptor> CreateToggle(ContextMenuHandler handler, bool enable)
    {
        var afterClsid = enable ? handler.Clsid : $"-{handler.Clsid}";
        var settingId = MakeSettingId(handler.Clsid);

        var registryPaths = handler.AllRegistryPaths ?? [handler.RegistryPath];

        return registryPaths.Select(path =>
        {
            // Use per-path enabled state when available (handles inconsistent multi-path state)
            var pathEnabled = handler.PathEnabledStates?.GetValueOrDefault(path, handler.IsEnabled)
                              ?? handler.IsEnabled;
            var beforeClsid = pathEnabled ? handler.Clsid : $"-{handler.Clsid}";

            return new ChangeDescriptor
            {
                ModuleId = ModuleId,
                SettingId = settingId,
                DisplayName = $"Context menu: {handler.Name}",
                SystemLocation = $@"{path}\(Default)",
                BeforeValue = beforeClsid,
                AfterValue = afterClsid,
                BeforeDisplay = pathEnabled ? "Enabled" : "Disabled",
                AfterDisplay = enable ? "Enabled" : "Disabled",
                ValueType = ChangeValueType.Registry_String,
                Category = enable ? ChangeCategory.Enable : ChangeCategory.Disable,
            };
        }).ToList();
    }

    public static string MakeSettingId(string clsid)
    {
        // Strip braces from CLSID for a clean SettingId
        var cleanClsid = clsid.TrimStart('{').TrimEnd('}');
        return $"ctx-handler-{cleanClsid}";
    }
}
