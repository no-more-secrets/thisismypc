namespace ThisIsMyPC.Modules.Shell.Models;

public sealed record TaskbarSettings(
    int Alignment,
    bool WidgetsEnabled,
    bool ClassicContextMenu,
    bool ClassicCommandBar,
    int SearchboxMode = 3,     // Win11 default: search box
    int ButtonCombining = 0);  // default: always combine, hide labels
