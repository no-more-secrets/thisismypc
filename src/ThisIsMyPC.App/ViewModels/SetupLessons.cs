namespace ThisIsMyPC.App.ViewModels;

/// <summary>Lessons explain a module through concrete uses rather than a fixed section template.</summary>
public static class SetupLessons
{
    public static SetupLessonViewModel For(string name, string description) => name switch
    {
        "Windows Annoyances" => new(name, "Windows Annoyances",
            "A suggestion to try another Microsoft product is an advertisement, even when it appears in Settings. Windows puts promotions in several places, each with its own switch.\n\nThe pack turns off supported promotions and suggestions. If another appears later, look here for its control."),
        "Privacy & Telemetry" => new(name, "Privacy & Telemetry",
            "Windows separates required diagnostic data from optional data. This module also controls activity tracking and advertising personalization.\n\nThe pack limits optional collection. It cannot stop an installed app from collecting data through its own settings."),
        "Windows Update" => new(name, "Windows Update",
            "Set active hours around when you use the PC to reduce restart interruptions. This module also contains update scheduling and policy controls.\n\nAfter a major update, check whether your preferred settings still apply. Disabling updates also stops security fixes; the pack does not change your update settings."),
        "Software" => new(name, "Software",
            "The Windows Apps list includes bundled apps you may never use. The pack selects Clipchamp, News, Weather, Copilot, and Microsoft 365 Hub for removal when installed. Clear any you want to keep.\n\nThese removals affect every user and stop the apps being added automatically to new user accounts. Save app data first. History cannot undo a removal; you must reinstall the app."),
        "Security" => new(name, "Security",
            "Use this module to check antivirus, sign-in, and app protection settings. An antivirus scan can use CPU and disk while doing necessary work.\n\nTurning off a warning does not fix what caused it. Check the named protection before dismissing the warning."),
        "Network & Firewall" => new(name, "Network & Firewall",
            "Connections lists your network adapters. DNS controls which service looks up website addresses. Firewall shows the profiles and rules controlling network access.\n\nVPN software can add adapters and change DNS. An unfamiliar adapter may belong to your VPN, so identify it before disabling it."),
        "Startup & Services" => new(name, "Startup & Services",
            "An app can start at sign-in even if you never open it. Disable its startup entry if you prefer to open it yourself. This leaves the app installed.\n\nServices and scheduled tasks also run in the background, including updaters and hardware support. Check the publisher and purpose before disabling them. Revisit this list after installing software."),
        "Explorer" => new(name, "Explorer",
            "File extensions distinguish files such as photo.jpg and photo.exe. Show extensions if you want to see the file type in its name.\n\nThis module also changes File Explorer and taskbar behavior. The pack turns off Widgets; choose other Explorer settings here. Some changes may require an Explorer restart."),
        "Context Menus" => new(name, "Context Menus",
            "Apps add shortcuts to the menu you see when you right-click a file. Disabling an unused shortcut removes it from the menu without uninstalling the app.\n\nCheck this module when an installer adds another entry. Keep the shortcuts you actually use."),
        "Environment" => new(name, "Environment",
            "PATH is a list of folders Windows searches when you run a command. An installer can add its folder; an uninstaller can leave that entry behind.\n\nUse this module to inspect variables and correct known problems, such as a command opening the wrong version. Leave unfamiliar entries alone."),
        "Power Plans" => new(name, "Power Plans",
            "Choose when the screen turns off and when the PC sleeps. Power plans also affect processor behavior, heat, and energy use.\n\nCompare performance during your usual workload before keeping a plan change. If the active plan changes unexpectedly, check your hardware utilities too."),
        "Display" => new(name, "Display",
            "Adjust brightness or switch inputs on supported monitors without reaching for their buttons. You can link brightness across displays.\n\nAvailable controls depend on the monitor. If a control is unavailable here, use the monitor's own menu."),
        "System Control" => new(name, "System Control",
            "Manufacturer software may combine promotions with useful hardware controls. Check which functions you need before uninstalling it.\n\nSystem Control provides supported manufacturer controls and companion options. Many target laptops, so this module may have little to offer on a desktop."),
        "Lighting" => new(name, "Lighting",
            "Choose colors and effects for supported lighting devices here. Running two lighting controllers for the same device can make their settings conflict.\n\nIf a color keeps changing back, check whether another lighting app starts with Windows."),
        "Cooling" => new(name, "Cooling",
            "Cooling uses supported fan controls and FanControl integration. A fan curve sets fan speed in response to a temperature sensor.\n\nIdentify each fan and sensor before changing a curve. Check temperatures under load afterward. A curve copied from another PC may not suit your cooling."),
        "Monitoring" => new(name, "Monitoring",
            "Compare load, temperatures, and fan speeds while doing the same task before and after a change. A reading taken at idle cannot show whether a game runs better.\n\nA missing sensor means no reading is available. It does not mean the temperature or fan speed is zero."),
        _ => new(name, name, description),
    };
}
