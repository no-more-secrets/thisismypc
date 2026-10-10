namespace ThisIsMyPC.App.ViewModels;

/// <summary>Lessons explain a module through concrete uses rather than a fixed section template.</summary>
public static class SetupLessons
{
    public static SetupLessonViewModel For(string name, string description) => name switch
    {
        "Windows Annoyances" => new(name, "Windows Annoyances",
            "A suggestion to try another Microsoft product is an advertisement, even when it appears in Settings. Windows puts promotions in several places, each with its own switch.\n\nOpen this module and search for suggestions. Read the matching setting, change it, and check the pending change before applying.\n\nIf another promotion appears later, note where it appeared. Start, Settings, and the lock screen have separate controls."),
        "Privacy & Telemetry" => new(name, "Privacy & Telemetry",
            "Windows separates required diagnostic data from optional data. This module also controls activity tracking and advertising personalization.\n\nOpen this module and find Diagnostic data. Compare the current value with the available choices; some depend on your Windows edition.\n\nThe pack limits optional collection. Check privacy settings inside newly installed apps too, because Windows cannot control every app's own collection."),
        "Windows Update" => new(name, "Windows Update",
            "The pack selects Notify before download, keeps Windows on its current feature release, excludes drivers, and limits automatic restarts.\n\nOpen this module to review those choices. If you keep the release pin, remove it before that release stops receiving security fixes. Check for updates regularly when downloads wait for you.\n\nSet active hours around when you use the PC to reduce restart interruptions."),
        "Software" => new(name, "Software",
            "Open Software, then select Windows Apps. Search for an app you recognize and read its description before choosing Remove. Review the pending action before applying it.\n\nThe pack selects Clipchamp, News, Weather, Copilot, and Microsoft 365 Hub for removal when installed. Clear any you want to keep.\n\nThese removals affect every user and stop the apps being added automatically to new user accounts. Save app data first. History cannot undo a removal; you must reinstall the app."),
        "Security" => new(name, "Security",
            "Use this module to check antivirus, sign-in, and app protection settings. An antivirus scan can use CPU and disk while doing necessary work.\n\nIf Windows reports a protection problem, find that protection here and inspect its state. Read the setting before changing it; hiding a notification does not repair the protection."),
        "Network & Firewall" => new(name, "Network & Firewall",
            "Connections lists your network adapters. DNS controls which service looks up website addresses. Firewall shows the profiles and rules controlling network access.\n\nOpen Connections and identify the adapter carrying your connection. Check its DNS settings before changing them. VPN software can add adapters and replace DNS settings, so identify unfamiliar entries before disabling them."),
        "Startup & Services" => new(name, "Startup & Services",
            "An app can start at sign-in even if you never open it. Disable its startup entry if you prefer to open it yourself. This leaves the app installed.\n\nOpen this module and search for an app you recognize. Check its publisher and file location before disabling its startup entry.\n\nThe pack also includes Clean Boot's service and scheduled-task changes. Windows Search is a separate, unchecked choice: disabling its startup stops future indexing after the service stops or Windows restarts. Indexed file and Outlook searches are affected."),
        "Explorer" => new(name, "Explorer",
            "File extensions distinguish files such as photo.jpg and photo.exe. Show extensions if you want to see the file type in its name.\n\nOpen this module and find Show file name extensions. Enable it, apply the change, then open a folder to compare the names.\n\nThe pack hides Widgets and restores the full right-click menu. Some changes may require an Explorer restart."),
        "Context Menus" => new(name, "Context Menus",
            "Apps add shortcuts to the menu you see when you right-click a file. Disabling an unused shortcut removes it from the menu without uninstalling the app.\n\nOpen this module and find an entry from an app you recognize. Disable it, apply the change, then right-click the same file again to check the result. Keep shortcuts you use often."),
        "Environment" => new(name, "Environment",
            "PATH is a list of folders Windows searches when you run a command. An installer can add its folder; an uninstaller can leave that entry behind.\n\nFind PATH and inspect the listed folders. If a command opens the wrong version, check whether an older installation appears earlier in that list. Remove an entry only after confirming which program uses it."),
        "Power Plans" => new(name, "Power Plans",
            "Choose when the screen turns off and when the PC sleeps. Power plans also affect processor behavior, heat, and energy use.\n\nOpen the active plan and find its screen and sleep settings. Set times that suit your work, then review the pending changes. Compare performance during your usual workload before keeping a performance change."),
        "Display" => new(name, "Display",
            "Adjust brightness or switch inputs on supported monitors without reaching for their buttons. You can link brightness across displays.\n\nChoose a monitor and move its brightness slider. Brightness changes immediately. Use Link brightness across displays if you want the sliders to move together. Controls unavailable here may still be available in the monitor's own menu."),
        "System Control" => new(name, "System Control",
            "Manufacturer software may combine promotions with useful hardware controls. Check which functions you need before uninstalling it.\n\nOpen System Control and check the detected hardware and supported companion options. Compare the functions you need with your manufacturer utility before replacing it. Many options target laptops and will be unavailable on a desktop."),
        "Lighting" => new(name, "Lighting",
            "Choose colors and effects for supported lighting devices here. Running two lighting controllers for the same device can make their settings conflict.\n\nSelect a detected device and try a fixed color before configuring an effect. If the color changes back, check Startup & Services for another lighting controller."),
        "Cooling" => new(name, "Cooling",
            "Cooling uses supported fan controls and FanControl integration. A fan curve sets fan speed in response to a temperature sensor.\n\nIdentify each fan and temperature sensor in the cooling controls before changing a curve. Make a small change, run your usual workload, and watch temperatures in Monitoring. A curve copied from another PC may not suit your cooling."),
        "Monitoring" => new(name, "Monitoring",
            "Compare load, temperatures, and fan speeds while doing the same task before and after a change. A reading taken at idle cannot show whether a game runs better.\n\nOpen Monitoring, note the readings at idle, then start your usual workload and watch how they change. Repeat that workload after a setting change. A missing sensor means no reading is available, rather than zero."),
        _ => new(name, name, description),
    };
}
