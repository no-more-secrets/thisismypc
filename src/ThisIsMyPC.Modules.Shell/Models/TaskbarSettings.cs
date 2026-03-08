namespace ThisIsMyPC.Modules.Shell.Models;

public sealed record TaskbarSettings(
    int Alignment,
    bool WidgetsEnabled,
    bool ClassicContextMenu);
