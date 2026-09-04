@{
    # Hand-written curation merged over ExplorerPatcher's manifest by
    # tools/import-explorerpatcher-settings.ps1. The manifest gives the key,
    # value, default, options, and conditions; this file says how the row
    # reads in the app. Every imported value needs a Description, and the
    # import fails on one that has none, so a pin bump surfaces new rows.
    # Keys are ExplorerPatcher's registry value names.

    # Values that configure ExplorerPatcher itself (its debugging, its update
    # channel, its own settings window) rather than the Windows shell. They
    # are not part of a machine's configuration and stay out.
    Exclude = @{
        'AllocConsole'             = 'debug console'
        'Memcheck'                 = 'memory leak dump'
        'EnableSymbolDownload'     = 'symbol download for its own patching'
        'LastSectionInProperties'  = 'its own settings window'
        'PropertiesInWinX'         = 'shortcut to its own settings window'
        'NoMenuAccelerator'        = 'shortcut key of its own settings item'
        'NoPropertiesInContextMenu' = 'its own Properties item in the taskbar menu'
        'UpdatePreferStaging'      = 'its update channel'
        'UpdateAllowDowngrades'    = 'its update channel'
        'UpdateUseLocal'           = 'builds its own updates locally'
        'NoPerApplicationList'     = 'no label in ExplorerPatcher itself (placeholder string)'
    }

    # Which tab of the Explorer page each ExplorerPatcher page lands on.
    PageToSection = @{
        'Taskbar'         = 'Taskbar'
        'System tray'     = 'Taskbar'
        'Weather'         = 'Taskbar'
        'File Explorer'   = 'FileExplorer'
        'Start menu'      = 'StartMenu'
        'Window switcher' = 'Desktop'
        'Spotlight'       = 'Desktop'
        'Other'           = 'General'
        'Advanced'        = 'General'
        'Updates'         = 'General'
    }

    # ExplorerPatcher's Other and Advanced pages mix taskbar, desktop, and
    # system-wide rows; each goes to the tab it belongs on.
    Section = @{
        'ClockFlyoutOnWinC'               = 'Taskbar'
        'ToolbarSeparators'               = 'Taskbar'
        'TaskbarAutohideOnDoubleClick'    = 'Taskbar'
        'PinnedItemsActAsQuickLaunch'     = 'Taskbar'
        'RemoveExtraGapAroundPinnedItems' = 'Taskbar'
        'DisableAeroSnapQuadrants'        = 'Desktop'
        'SnapAssistSettings'              = 'Desktop'
        'Start_PowerButtonAction'         = 'Desktop'
        'PaintDesktopVersion'             = 'Desktop'
    }

    # Sub-heading under the ExplorerPatcher heading on a tab, by page. An
    # empty heading is the tab's main run of rows and comes first.
    PageToGroup = @{
        'Taskbar'         = ''
        'System tray'     = 'System tray'
        'Weather'         = 'Weather widget'
        'File Explorer'   = ''
        'Start menu'      = ''
        'Window switcher' = 'Window switcher (Alt+Tab)'
        'Spotlight'       = 'Windows Spotlight'
        'Other'           = ''
        'Advanced'        = ''
        'Updates'         = 'Updates'
    }

    # Per-value group, overriding the page's. "Advanced" collects the niche
    # rendering and sound tweaks that ExplorerPatcher itself keeps on its
    # Advanced page; they stay available but sit last, out of the way.
    Group = @{
        'DoNotRedirectSystemToSettingsApp'              = 'Control Panel'
        'DoNotRedirectProgramsAndFeaturesToSettingsApp' = 'Control Panel'
        'DoNotRedirectDateAndTimeToSettingsApp'         = 'Control Panel'
        'DoNotRedirectNotificationIconsToSettingsApp'   = 'Control Panel'
        'ClassicThemeMitigations'                       = 'Advanced'
        'XamlSounds'                                    = 'Advanced'
    }

    # Order of groups within a tab; the empty group is always first, Advanced last.
    GroupOrder = @(
        '', 'System tray', 'Weather widget', 'Window switcher (Alt+Tab)',
        'Windows Spotlight', 'Control Panel', 'Updates', 'Advanced'
    )

    # Relevance order within a tab and group: lower sorts earlier. A row with
    # no entry falls to the end in ExplorerPatcher's own manifest order. This
    # is the knob for "most useful first"; retune it freely.
    RowOrder = @{
        # File Explorer
        'DisableImmersiveContextMenu' = 10   # pairs with the classic context menu
        'LegacyFileTransferDialog'    = 20
        'UseClassicDriveGrouping'     = 30
        'HideIconAndTitleInExplorer'  = 40
        'HideExplorerSearchBar'       = 50
        'ShrinkExplorerAddressBar'    = 60
        'MicaEffectOnTitlebar'        = 70
        # General, main run: the keyboard and sound tweaks are all niche, ordered gently
        'DisableWinFHotkey'           = 10
        'DisableOfficeHotkeys'        = 20
        'LogonLogoffShutdownSounds'   = 30
    }

    # Labels rewritten where ExplorerPatcher's only reads under its own page
    # heading, or where a switch label should say what on does.
    Label = @{
        'FlyoutMenus'                                   = 'Open tray icon menus as flyouts'
        'HideControlCenterButton'                       = 'Show Quick Settings button'
        'ReplaceNetwork'                                = 'Network icon right-click opens'
        'ReplaceVan'                                    = 'Network icon opens'
        'EnableMtcUvc'                                  = 'Sound icon opens'
        'UseWin32TrayClockExperience'                   = 'Clock opens'
        'UseWin32BatteryFlyout'                         = 'Battery icon opens'
        'IMEStyle'                                      = 'Language switcher style'
        'DisableImmersiveContextMenu'                   = 'Use immersive style for Windows 10 context menus'
        'HideIconAndTitleInExplorer'                    = 'Title bar shows'
        'MicaEffectOnTitlebar'                          = 'Mica effect on the Windows 7 navigation bar'
        'MonitorOverride'                               = 'Open Start on this monitor (keyboard)'
        'IncludeWallpaper'                              = 'Show the desktop as the last window'
        'PerMonitor'                                    = 'Show only windows on the monitor with the pointer'
        'Grid_backgroundPercent'                        = 'Background opacity'
        'CornerPreference'                              = 'Corner style'
        'MaxWidth'                                      = 'Maximum width'
        'MaxHeight'                                     = 'Maximum height'
        'ScrollWheelBehavior'                           = 'Scroll wheel changes selection'
        'WeatherWindowCornerPreference'                 = 'Corner style'
        'WeatherContentsMode'                           = 'Contents layout'
        'SpotlightDisableIcon'                          = 'Hide the "Learn about this picture" icon'
        'SpotlightDesktopMenuMask'                      = 'Spotlight items in the desktop menu'
        'ClockFlyoutOnWinC'                             = 'Win+C opens the clock flyout'
        'DisableAeroSnapQuadrants'                      = 'Disable quadrant snapping'
        'Start_PowerButtonAction'                       = 'Alt+F4 on the desktop defaults to'
        'DoNotRedirectSystemToSettingsApp'              = 'Keep System / About in Control Panel'
        'DoNotRedirectProgramsAndFeaturesToSettingsApp' = 'Keep Programs and Features in Control Panel'
        'DoNotRedirectDateAndTimeToSettingsApp'         = 'Keep Adjust date/time in Control Panel'
        'DoNotRedirectNotificationIconsToSettingsApp'   = 'Keep Customize notification icons in Control Panel'
        'UpdatePolicy'                                  = 'Check for updates'
        'DisableOfficeHotkeys'                          = 'Disable Office hotkeys'
        'TaskbarAutohideOnDoubleClick'                  = 'Double-click the taskbar to toggle auto-hide'
        'ClassicThemeMitigations'                       = 'Fix rendering under the classic theme'
        'PinnedItemsActAsQuickLaunch'                   = 'Pinned items act as quick launch'
        'RemoveExtraGapAroundPinnedItems'               = 'Remove the extra gap around pinned items with labels'
        'XamlSounds'                                    = 'Play UI sounds in Explorer''s XAML views'
    }

    # What the row does, shown in its (i) tooltip and searched. Terse product
    # copy: what on does, or what the choice controls.
    Description = @{
        # Taskbar
        'OldTaskbar'                      = 'Which taskbar Explorer draws. Windows 10 (ExplorerPatcher) is ExplorerPatcher''s own build of the Windows 10 taskbar, and the other taskbar rows here apply to it.'
        'OrbStyle'                        = 'The Start button drawn on the Windows 10 taskbar.'
        'OldTaskbarAl'                    = 'Where the buttons sit on the main taskbar: at the edge like Windows 10, or centered like Windows 11.'
        'MMOldTaskbarAl'                  = 'Where the buttons sit on the taskbars of other monitors.'
        'MMTaskbarGlomLevel'              = 'When windows of the same app share one button on the taskbars of other monitors.'
        'TaskbarSmallIcons'               = 'Small icons make the Windows 10 taskbar shorter.'
        'ClockFlyoutOnWinC'               = 'Pressing Win+C opens the clock and calendar instead of Microsoft Teams chat.'
        'ToolbarSeparators'               = 'Draw a separator line between toolbars on the taskbar.'
        'TaskbarAutohideOnDoubleClick'    = 'Double-clicking an empty part of the taskbar turns auto-hide on or off. Works only while the taskbar is locked.'
        'PinnedItemsActAsQuickLaunch'     = 'A pinned button always starts a new window instead of switching to the app''s open windows, and running apps get their own button.'
        'RemoveExtraGapAroundPinnedItems' = 'When taskbar buttons show labels, a pinned item takes no more room than its icon.'
        # System tray
        'SkinMenus'                       = 'Draw the right-click menus of the taskbar and tray icons in the Windows 11 style instead of the classic look.'
        'CenterMenus'                     = 'Open a tray icon''s menu centered above the icon instead of at the pointer.'
        'FlyoutMenus'                     = 'Show tray icon menus in the Windows 11 flyout style. Off uses the classic pop-up menu.'
        'TipbandDesiredVisibility'        = 'Show the touch keyboard button in the tray.'
        'HideControlCenterButton'         = 'Show the Windows 11 button in the tray that groups network, sound, and battery and opens Quick Settings.'
        'TaskbarSD'                       = 'The thin button at the end of the taskbar that shows the desktop. Hidden removes it; Disabled keeps the space but does nothing.'
        'SkinIcons'                       = 'Draw the network, sound, and battery icons in the Windows 11 style on the Windows 10 taskbar.'
        'TrayOverflowStyle'               = 'The look of the pop-up that opens from the hidden icons arrow.'
        'ReplaceNetwork'                  = 'What "Open Network & Internet settings" in the network icon''s right-click menu opens.'
        'ReplaceVan'                      = 'The panel that opens when you click the network icon in the tray.'
        'EnableMtcUvc'                    = 'The volume panel that opens when you click the sound icon in the tray.'
        'UseWin32TrayClockExperience'     = 'The calendar panel that opens when you click the clock.'
        'UseWin32BatteryFlyout'           = 'The panel that opens when you click the battery icon.'
        'IMEStyle'                        = 'The look of the keyboard language switcher in the tray.'
        # Weather widget
        'WeatherViewMode'                 = 'What the widget shows: icon, description, temperature.'
        'WeatherFixedSize'                = 'Whether the widget grows with its text or keeps one fixed width.'
        'WeatherToLeft'                   = 'Which end of the taskbar the widget sits at.'
        'WeatherContentUpdateMode'        = 'How often the widget fetches new weather.'
        'WeatherTemperatureUnit'          = 'Celsius or Fahrenheit.'
        'WeatherTheme'                    = 'Light or dark widget, or follow the Windows setting.'
        'WeatherWindowCornerPreference'   = 'How rounded the weather pop-up''s corners are.'
        'WeatherIconPack'                 = 'Whose weather icons the widget uses.'
        'WeatherContentsMode'             = 'One line, or two lines when the taskbar is tall enough.'
        'WeatherZoomFactor'               = 'Scale of the widget''s contents.'
        # File Explorer
        'LegacyFileTransferDialog'        = 'Copy and move files with the Windows 7 progress dialog instead of the newer one with the speed graph.'
        'UseClassicDriveGrouping'         = 'Group drives in This PC the Windows 7 way (hard disks, removable drives, network locations) instead of one Devices and drives list.'
        'DisableImmersiveContextMenu'     = 'Draw Windows 10 style right-click menus with the larger touch-friendly look. Off uses the compact classic menu. New windows only.'
        'ShrinkExplorerAddressBar'        = 'Make the address bar in File Explorer windows shorter. New windows only.'
        'HideExplorerSearchBar'           = 'Remove the search box from File Explorer windows. New windows only.'
        'HideIconAndTitleInExplorer'      = 'Whether File Explorer windows show the folder icon and name in the title bar. New windows only.'
        'MicaEffectOnTitlebar'            = 'Tint the navigation bar of File Explorer windows with the Mica effect when the Windows 7 command bar is in use. New windows only.'
        # Start menu
        'Start_ShowClassicMode'           = 'Windows 10 opens the Windows 10 Start menu, which needs its code to still be present in this Windows build.'
        'MonitorOverride'                 = 'With more than one display, which monitor the Start menu opens on when you press the Windows key.'
        'MakeAllAppsDefault'              = 'Open the Windows 11 Start menu on the All apps list instead of Pinned.'
        # Desktop
        'DisableAeroSnapQuadrants'        = 'Dragging a window to a corner snaps it to half the screen, not a quarter.'
        'SnapAssistSettings'              = 'Which Snap Assist appears after you snap a window.'
        'Start_PowerButtonAction'         = 'The action preselected in the Shut Down Windows dialog that Alt+F4 opens on the desktop.'
        'PaintDesktopVersion'             = 'Print the Windows edition and build number in the corner of the desktop.'
        # Window switcher
        'AltTabSettings'                  = 'Which window switcher Alt+Tab opens. Simple Window Switcher is ExplorerPatcher''s own, and the rows below apply to it.'
        'IncludeWallpaper'                = 'Add the desktop at the end of the Alt+Tab list so you can switch to it.'
        'PrimaryOnly'                     = 'Open the switcher on the main monitor even when the pointer is on another one.'
        'PerMonitor'                      = 'List only the windows on the monitor the pointer is on.'
        'SwitcherIsPerApplication'        = 'Show one entry per app, with its windows listed under it.'
        'Theme'                           = 'The switcher''s background. Acrylic blurs what is behind it; Mica is a solid tinted surface.'
        'Grid_backgroundPercent'          = 'How solid the switcher''s background is.'
        'ColorScheme'                     = 'Light or dark switcher, or follow the Windows setting.'
        'CornerPreference'                = 'How rounded the switcher window''s corners are.'
        'RowHeight'                       = 'Height of each row of window previews.'
        'MaxWidth'                        = 'The widest the switcher grows, as a share of the screen width.'
        'MaxHeight'                       = 'The tallest the switcher grows, as a share of the screen height.'
        'MasterPadding'                   = 'Space between the switcher''s edge and its contents.'
        'ShowDelay'                       = 'How long Alt+Tab is held before the switcher appears. A short delay lets a quick Alt+Tab switch without drawing it.'
        'ScrollWheelBehavior'             = 'Whether the mouse wheel moves the selection while the switcher is open.'
        # Windows Spotlight
        'SpotlightDisableIcon'            = 'Remove the "Learn about this picture" icon Spotlight places on the desktop.'
        'SpotlightDesktopMenuMask'        = 'Which Spotlight commands appear in the desktop''s right-click menu.'
        'SpotlightUpdateSchedule'         = 'How often Spotlight fetches a new desktop picture.'
        # General
        'DisableOfficeHotkeys'            = 'Stop the Ctrl+Alt+Shift+Windows key shortcuts from opening Office apps.'
        'DisableWinFHotkey'               = 'Stop Win+F from opening the Feedback Hub.'
        'LogonLogoffShutdownSounds'       = 'Play the Windows 7 sign-in, sign-out, and shutdown sounds.'
        'ClassicThemeMitigations'         = 'Extra patches so the taskbar, tray, and folder views draw correctly when the Windows classic theme is forced on.'
        'XamlSounds'                      = 'Enable the click and navigation sounds in the parts of Explorer built with XAML, such as the Start menu and taskbar flyouts.'
        # Control Panel
        'DoNotRedirectSystemToSettingsApp'              = 'Open the classic Control Panel page instead of the Settings app when this link is used.'
        'DoNotRedirectProgramsAndFeaturesToSettingsApp' = 'Open the classic Control Panel page instead of the Settings app when this link is used.'
        'DoNotRedirectDateAndTimeToSettingsApp'         = 'Open the classic Control Panel page instead of the Settings app when this link is used.'
        'DoNotRedirectNotificationIconsToSettingsApp'   = 'Open the classic Control Panel page instead of the Settings app when this link is used.'
        # Updates
        'UpdatePolicy'                    = 'What ExplorerPatcher''s own updater does when File Explorer starts. Do not check keeps the installed version, which is the one these rows were written for.'
    }
}
