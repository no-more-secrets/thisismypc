using System.ComponentModel;
using System.Runtime.InteropServices;
using ThisIsMyPC.Installer.ViewModels;

namespace ThisIsMyPC.Installer.Win32;

internal sealed unsafe partial class InstallerWindow
{
    private readonly Dictionary<int, NativeControl> _nativeControls = new();
    private bool _preview;
    private bool _updatingScroll;
    private int _scrollX;
    private int _scrollY;

    private const int IconTextLeft = InstallerRenderer.AppIconLeft + InstallerRenderer.AppIconSize + 20;

    private sealed record ControlSpec(int Id, string Text, UiRect Bounds, HitTarget Target = HitTarget.None,
        bool Enabled = true, bool? Checked = null);
    private sealed record NativeControl(nint Handle, ControlSpec Spec);

    internal nint ControlHandle(HitTarget target)
        => _nativeControls.Values.FirstOrDefault(control => control.Spec.Target == target)?.Handle ?? _hwnd;

    private List<ControlSpec> DescribeControls()
    {
        var vm = _viewModel;
        var controls = new List<ControlSpec>();
        void Label(int id, string text, int left, int top, int right, int bottom)
            => controls.Add(new(id, text, UiRect.FromEdges(left, top, right, bottom)));
        void Button(HitTarget target, string text, int left, int top, int right, int bottom, bool enabled = true, bool? check = null)
            => controls.Add(new(2000 + (int)target, text, UiRect.FromEdges(left, top, right, bottom), target, enabled, check));
        Label(3020, vm.IsWelcome ? "Welcome to ThisIsMyPC Setup" : vm.IsLicense ? "License Agreement" :
            vm.IsOptions ? "Installation Options" : vm.IsConfirmUninstall ? "Remove ThisIsMyPC" :
            vm.IsBusy ? vm.StepCaption : vm.Failed ? "Setup Failed" : "Setup Complete", 28, 16, 470, 44);
        Label(3021, InstallerViewModel.BuildLabel, 28, 48, 470, 68);
        if (vm.IsWelcome)
        {
            // The app icon sits at the left of the welcome and done pages; text starts beside it.
            Label(3000, "Welcome to the installer for ThisIsMyPC.", IconTextLeft, 92, 572, 116);
            Label(3001, "This program will be installed for everybody who uses this PC. This installer has administrator permissions.", IconTextLeft, 126, 572, 172);
            if (vm.IsInstalled)
            {
                Label(3002, vm.InstalledSummary, IconTextLeft, 188, 572, 286);
                Button(HitTarget.Uninstall, "Uninstall ThisIsMyPC", IconTextLeft, 294, 572, 324, check: vm.UninstallMode);
            }
            if (vm.ShowWelcomeHint)
                Label(3003, vm.WelcomeHint, IconTextLeft, 380, 572, 404);
        }
        else if (vm.IsLicense)
        {
            Label(3000, "GNU General Public License, version 3. You may use, share, and change ThisIsMyPC under these terms.", 28, 84, 572, 122);
            Button(HitTarget.LicenseAccepted, "I accept the terms of the GNU General Public License, version 3", 28, 384, 572, 413, check: vm.LicenseAccepted);
        }
        else if (vm.IsOptions)
        {
            Label(3000, "Install folder", 28, 84, 472, 104);
            Button(HitTarget.Browse, "Browse...", 480, 105, 572, 133, vm.CanChooseFolder);
            var detail = string.Join("\r\n", new[] { vm.CanChooseFolder ? null : "Updates go into the folder the app is already in.", vm.FolderError ?? vm.FolderWarning }.Where(text => text is not null));
            Label(3001, detail, 28, 140, 572, OptionsShortcutsY(vm) - 4);
            var y = OptionsShortcutsY(vm);
            Label(3002, "Shortcuts", 28, y, 572, y + 20);
            Button(HitTarget.StartMenu, "Add ThisIsMyPC to the Start menu", 28, y + 20, 572, y + 44, check: vm.StartMenuShortcut);
            Button(HitTarget.Desktop, "Add a shortcut on the Desktop", 28, y + 46, 572, y + 70, check: vm.DesktopShortcut);
            Label(3003, "Behavior", 28, y + 82, 572, y + 102);
            Button(HitTarget.StartWithWindows, "Start with Windows, in the tray", 28, y + 102, 572, y + 126, check: vm.StartWithWindows);
            Button(HitTarget.CheckForUpdates, "Check for updates automatically", 28, y + 128, 572, y + 152, check: vm.CheckForUpdates);
            Label(3004, "Both can be changed later in Settings inside the app.", 48, y + 157, 572, y + 189);
        }
        else if (vm.IsBusy)
        {
            Label(3000, vm.StatusText, 28, 104, 572, 150);
            Label(3030, "", 28, 168, 572, 188);
        }
        else if (vm.IsConfirmUninstall)
        {
            Label(3000, "Remove ThisIsMyPC from this PC? The app, its shortcuts, and its entry in Installed apps go away.", 28, 90, 572, 138);
            Label(3001, "Your settings and change history stay on this PC for a later install. Changes you applied to Windows stay as they are. Undo them in the app first if you want them reverted.", 28, 150, 572, 240);
            Label(3002, "Choose Remove to continue, or Back to keep it.", 28, 270, 572, 296);
        }
        else if (vm.IsDone)
        {
            if (vm.Failed)
            {
                Label(3000, vm.ErrorText!, 28, 90, 572, 230);
                if (vm.HasLogPath)
                    Label(3001, "Details were saved to " + vm.LogPath, 28, 250, 572, 370);
            }
            else if (vm.Removed)
                Label(3000, "ThisIsMyPC was removed. Your settings and change history stay on this PC in case you install it again.", 28, 90, 572, 160);
            else
            {
                Label(3000, "ThisIsMyPC is installed. Windows asks for permission only when a change needs administrator access.", IconTextLeft, 92, 572, 136);
                if (vm.RebootRequired)
                    Label(3001, "Windows asked for a restart to finish. Restart when convenient.", IconTextLeft, 144, 572, 180);
                var y = vm.RebootRequired ? 192 : 148;
                Button(HitTarget.Launch, "Open ThisIsMyPC when I choose Finish", IconTextLeft, y, 572, y + 28, check: vm.LaunchWhenDone);
            }
        }
        if (vm.CanGoBack)
            Button(HitTarget.Back, "< Back", 296, 439, 384, 467);
        var left = !vm.CanGoBack && !vm.CanCancel ? 492 : 394;
        Button(HitTarget.Primary, vm.PrimaryButtonText.Trim(), left, 439, left + 88, 467, vm.CanGoPrimary);
        if (vm.CanCancel)
            Button(HitTarget.Cancel, "Cancel", 492, 439, 580, 467);
        return controls;
    }

    private void UpdateNativeControls()
    {
        var specs = DescribeControls();
        foreach (var pair in _nativeControls)
            if (!specs.Exists(spec => spec.Id == pair.Key))
                _ = NativeMethods.ShowWindow(pair.Value.Handle, NativeMethods.SW_HIDE);
        foreach (var spec in specs)
        {
            if (!_nativeControls.TryGetValue(spec.Id, out var control))
            {
                var style = NativeMethods.WS_CHILD;
                if (spec.Target != HitTarget.None)
                    style |= NativeMethods.WS_TABSTOP | 0x2000u | // BS_MULTILINE.
                        (spec.Checked.HasValue ? 2u : spec.Target == HitTarget.Primary ? 1u : 0u);
                else
                    style |= 0x80u; // SS_NOPREFIX.
                if (spec.Id == 3030)
                    style = NativeMethods.WS_CHILD | 8u; // PBS_MARQUEE.
                var handle = NativeMethods.CreateWindowEx(0, spec.Id == 3030 ? "msctls_progress32" : spec.Target == HitTarget.None ? "STATIC" : "BUTTON", spec.Text,
                    style, 0, 0, 0, 0, _hwnd, (nint)spec.Id, NativeMethods.GetModuleHandle(null), 0);
                if (handle == nint.Zero)
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows could not create an installer control.");
                control = new(handle, spec);
                _nativeControls.Add(spec.Id, control);

            }
            if (control.Spec.Text != spec.Text)
                _ = NativeMethods.SetWindowText(control.Handle, spec.Text);
            _nativeControls[spec.Id] = control with { Spec = spec };
            _ = NativeMethods.SendMessage(control.Handle, NativeMethods.WM_SETFONT, (nuint)(spec.Id == 3020 ? _headingFont : _bodyFont), 1);
            if (spec.Id == 3030)
                _ = NativeMethods.SendMessage(control.Handle, 0x40A, 1, 30); // PBM_SETMARQUEE.
            _ = NativeMethods.EnableWindow(control.Handle, spec.Enabled);
            if (spec.Checked.HasValue)
                _ = NativeMethods.SendMessage(control.Handle, NativeMethods.BM_SETCHECK, spec.Checked.Value ? 1u : 0u, 0);
            var scale = _dpi / 96d;
            _ = NativeMethods.MoveWindow(control.Handle, Scale(spec.Bounds.Left, scale) - _scrollX, Scale(spec.Bounds.Top, scale) - _scrollY,
                Scale(spec.Bounds.Right - spec.Bounds.Left, scale), Scale(spec.Bounds.Bottom - spec.Bounds.Top, scale), true);
            _ = NativeMethods.ShowWindow(control.Handle, NativeMethods.SW_SHOWNA);
        }
        // Native EDIT accessibility obtains the field name from the preceding STATIC sibling.
        if (_nativeControls.TryGetValue(3000, out var label))
        {
            if (_viewModel.IsOptions)
                _ = NativeMethods.SetWindowPos(_folderEdit, label.Handle, 0, 0, 0, 0, 0x13);
            if (_viewModel.IsLicense)
                _ = NativeMethods.SetWindowPos(_licenseEdit, label.Handle, 0, 0, 0, 0, 0x13);
        }
    }

    private void Activate(HitTarget target)
    {
        var item = KeyboardTargets().Find(item => item.Target == target);
        if (item.Target == HitTarget.None)
            return;
        var step = _viewModel.Step;
        HandleLogicalClick((item.Bounds.Left + item.Bounds.Right) / 2, (item.Bounds.Top + item.Bounds.Bottom) / 2);
        if (_viewModel.Step == step)
        {
            _keyboardFocus = target;
            _ = NativeMethods.SetFocus(ControlHandle(target));
        }
    }

    internal void CreatePreview(int width, int height)
    {
        _preview = true;
        EnsureWindowClass();
        _selfHandle = GCHandle.Alloc(this);
        _hwnd = NativeMethods.CreateWindowEx(0x08000080, WindowClassName, "Installer preview", WindowStyle,
            -32000, -32000, width, height, 0, 0, NativeMethods.GetModuleHandle(null), GCHandle.ToIntPtr(_selfHandle));
        if (_hwnd == nint.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error());
        _dpi = Scale(96, width / (double)LogicalWidth);
        ReplaceControlFonts();
        Refresh();
        // EDIT skips printing when its ancestors are hidden. This tool window never activates or enters the taskbar.
        _ = NativeMethods.ShowWindow(_hwnd, NativeMethods.SW_SHOWNA);
    }

    private void PrintControls(nint dc)
    {
        var scale = _dpi / 96d;
        void Print(nint handle, int x, int y)
        {
            var saved = NativeMethods.SaveDC(dc);
            try
            {
                _ = NativeMethods.SetViewportOrgEx(dc, Scale(x, scale), Scale(y, scale), 0);
                _ = NativeMethods.SendMessage(handle, NativeMethods.WM_PRINT, (nuint)dc, 0x0E);
            }
            finally { _ = NativeMethods.RestoreDC(dc, saved); }
        }
        foreach (var spec in DescribeControls())
            Print(_nativeControls[spec.Id].Handle, spec.Bounds.Left, spec.Bounds.Top);
        if (_viewModel.IsLicense)
            Print(_licenseEdit, 28, 130);
        if (_viewModel.IsOptions)
            Print(_folderEdit, 28, 105);
    }

    private static NativeMethods.RECT GetWorkArea(nint hwnd)
    {
        var info = new NativeMethods.MONITORINFO { cbSize = (uint)sizeof(NativeMethods.MONITORINFO) };
        if (!NativeMethods.GetMonitorInfo(NativeMethods.MonitorFromWindow(hwnd, 2), ref info))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        return info.rcWork;
    }

    private static NativeMethods.RECT GetWorkArea(NativeMethods.RECT suggested)
    {
        var info = new NativeMethods.MONITORINFO { cbSize = (uint)sizeof(NativeMethods.MONITORINFO) };
        if (!NativeMethods.GetMonitorInfo(NativeMethods.MonitorFromRect(in suggested, 2), ref info))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        return info.rcWork;
    }

    private void UpdateScrollBars()
    {
        if (_updatingScroll || _hwnd == nint.Zero || _preview)
            return;
        _updatingScroll = true;
        try
        {
            for (var pass = 0; pass < 2; pass++)
            {
                _ = NativeMethods.GetClientRect(_hwnd, out var client);
                SetBar(0, Scale(LogicalWidth, _dpi / 96d), client.Right, ref _scrollX);
                SetBar(1, Scale(LogicalHeight, _dpi / 96d), client.Bottom, ref _scrollY);
            }
            UpdateChildControls();
            UpdateNativeControls();
        }
        finally { _updatingScroll = false; }
    }

    private void SetBar(int bar, int content, int visible, ref int position)
    {
        position = Math.Clamp(position, 0, Math.Max(0, content - visible));
        var info = new NativeMethods.SCROLLINFO { cbSize = (uint)sizeof(NativeMethods.SCROLLINFO), fMask = 7,
            nMax = content - 1, nPage = (uint)Math.Max(1, visible), nPos = position };
        _ = NativeMethods.SetScrollInfo(_hwnd, bar, in info, true);
    }

    private void Scroll(bool horizontal, int command)
    {
        var info = new NativeMethods.SCROLLINFO { cbSize = (uint)sizeof(NativeMethods.SCROLLINFO), fMask = 0x17 };
        _ = NativeMethods.GetScrollInfo(_hwnd, horizontal ? 0 : 1, ref info);
        var position = command switch { 0 => info.nPos - 40, 1 => info.nPos + 40, 2 => info.nPos - (int)info.nPage,
            3 => info.nPos + (int)info.nPage, 4 or 5 => info.nTrackPos, 6 => 0, 7 => info.nMax, _ => info.nPos };
        if (horizontal) _scrollX = position; else _scrollY = position;
        UpdateScrollBars();
        Invalidate();
    }

    private void EnsureFocusVisible(HitTarget target)
    {
        if (_hwnd == nint.Zero || _preview)
            return;
        var item = KeyboardTargets().Find(item => item.Target == target);
        if (item.Target == HitTarget.None)
            return;
        _ = NativeMethods.GetClientRect(_hwnd, out var client);
        var scale = _dpi / 96d;
        _scrollX = Math.Max(Scale(item.Bounds.Right, scale) - client.Right, Math.Min(_scrollX, Scale(item.Bounds.Left, scale)));
        _scrollY = Math.Max(Scale(item.Bounds.Bottom, scale) - client.Bottom, Math.Min(_scrollY, Scale(item.Bounds.Top, scale)));
        UpdateScrollBars();
        Invalidate();
    }

}
