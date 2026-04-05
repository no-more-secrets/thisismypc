using ThisIsMyPC.Modules.Shell.Models;

namespace ThisIsMyPC.Modules.Shell.Services;

/// <summary>
/// Filters out shell verbs and handlers that are internal to Explorer and never produce
/// visible user-facing context menu entries. These are hardwired into the shell — toggling
/// them via LegacyDisable or the blocked list has no observable effect.
/// </summary>
internal static class InternalHandlerFilter
{
    // Static verbs that are shell-internal or ProgrammaticAccessOnly navigation actions.
    // These never appear as right-click menu entries for the user.
    private static readonly HashSet<string> HiddenVerbNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "explore",           // Shell-internal: opens folder in Explorer tree view
        "open",              // Shell-internal: default open action (double-click)
        "find",              // Legacy search handler, superseded by Windows Search
        "removeproperties",  // Properties dialog "Remove Personal Information" — not a context menu entry
        "opennewprocess",    // Shell-internal: "Open in new process" (folder navigation)
        "opennewtab",        // Shell-internal: "Open in new tab" (folder navigation)
        "opennewwindow",     // Shell-internal: "Open in new window" (folder navigation)
    };

    // Desktop-specific verbs that are triggered from Spotlight wallpaper toasts,
    // not from the desktop right-click context menu.
    private static readonly HashSet<string> HiddenDesktopVerbs = new(StringComparer.OrdinalIgnoreCase)
    {
        ".SpotlightLearnMore",
        ".SpotlightNextImage",
    };

    /// <summary>
    /// Returns true if the handler should be excluded from the UI entirely.
    /// </summary>
    public static bool ShouldHide(ContextMenuHandler handler)
    {
        if (handler.HandlerType != HandlerType.StaticVerb)
            return false;

        var verbName = handler.VerbInfo?.VerbName ?? handler.Name;
        return HiddenVerbNames.Contains(verbName) || HiddenDesktopVerbs.Contains(verbName);
    }
}
