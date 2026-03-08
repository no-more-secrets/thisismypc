using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Modules.Shell.Models;

namespace ThisIsMyPC.Modules.Shell.Changes;

public static class ContextMenuChangeFactory
{
    private const string ModuleId = "Shell & Explorer";

    public static ChangeDescriptor CreateToggle(ContextMenuHandler handler, bool enable)
    {
        // Enable: remove dash prefix from CLSID; Disable: add dash prefix
        var beforeClsid = handler.IsEnabled ? handler.Clsid : $"-{handler.Clsid}";
        var afterClsid = enable ? handler.Clsid : $"-{handler.Clsid}";

        return new ChangeDescriptor
        {
            ModuleId = ModuleId,
            SettingId = $"ctx-handler-{handler.Name}",
            DisplayName = $"Context menu: {handler.Name}",
            SystemLocation = $@"{handler.RegistryPath}\(Default)",
            BeforeValue = beforeClsid,
            AfterValue = afterClsid,
            BeforeDisplay = handler.IsEnabled ? "Enabled" : "Disabled",
            AfterDisplay = enable ? "Enabled" : "Disabled",
            ValueType = ChangeValueType.Registry_String,
            Category = enable ? ChangeCategory.Enable : ChangeCategory.Disable,
        };
    }
}
