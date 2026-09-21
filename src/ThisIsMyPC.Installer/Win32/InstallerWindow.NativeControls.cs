using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using ThisIsMyPC.Installer.ViewModels;

namespace ThisIsMyPC.Installer.Win32;

internal sealed unsafe partial class InstallerWindow
{
    private readonly Dictionary<int, NativeControl> _nativeControls = new();
    private nint _cardBrush;
    private nint _backgroundBrush;
    private bool _preview;
    private bool _updatingScroll;
    private int _scrollX;
    private int _scrollY;

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
        Button(HitTarget.WelcomeTab, "Welcome", 38, 85, vm.ShowInstallTabs ? 177 : 328, 114, vm.CanOpenWelcome, vm.IsWelcome);
        if (vm.ShowInstallTabs)
        {
            Button(HitTarget.LicenseTab, "License", 207, 85, 334, 114, vm.CanOpenLicense, vm.IsLicense);
            Button(HitTarget.OptionsTab, "Options", 363, 85, 491, 114, vm.CanOpenOptions, vm.IsOptions);
            Label(3010, vm.IsInInstallTab ? vm.StepCaption : "Install", 521, 85, 641, 114);
        }
        else
            Label(3010, vm.IsInRemoveTab ? vm.StepCaption : "Remove", 359, 85, 641, 114);
        if (vm.IsWelcome)
        {
            Label(3000, "Welcome to the installer for ThisIsMyPC.", 57, 155, 663, 185);
            Label(3001, "This program will be installed for everybody who uses this PC. This installer has administrator permissions.", 57, 195, 663, 240);
            if (vm.IsInstalled)
            {
                Label(3002, vm.InstalledSummary, 57, 260, 663, 365);
                Button(HitTarget.Uninstall, "Uninstall ThisIsMyPC", 57, 380, 663, 412, check: vm.UninstallMode);
            }
            if (vm.ShowWelcomeHint)
                Label(3003, vm.WelcomeHint, 350, 516, 663, 542);
        }
        else if (vm.IsLicense)
        {
            Label(3000, "GNU General Public License, version 3. You may use, share, and change ThisIsMyPC under these terms.", 57, 153, 663, 190);
            Button(HitTarget.LicenseAccepted, "I accept the terms of the GNU General Public License, version 3", 57, 514, 663, 543, check: vm.LicenseAccepted);
        }
        else if (vm.IsOptions)
        {
            Label(3000, "Install folder", 57, 151, 578, 174);
            Button(HitTarget.Browse, "Browse...", 586, 178, 663, 211, vm.CanChooseFolder);
            var detail = string.Join("\r\n", new[] { vm.CanChooseFolder ? null : "Updates go into the folder the app is already in.", vm.FolderError ?? vm.FolderWarning }.Where(text => text is not null));
            Label(3001, detail, 57, 218, 663, OptionsShortcutsY(vm) - 4);
            var y = OptionsShortcutsY(vm);
            Label(3002, "Shortcuts", 57, y, 663, y + 22);
            Button(HitTarget.StartMenu, "Add ThisIsMyPC to the Start menu", 57, y + 19, 663, y + 47, check: vm.StartMenuShortcut);
            Button(HitTarget.Desktop, "Add a shortcut on the Desktop", 57, y + 48, 663, y + 76, check: vm.DesktopShortcut);
            Label(3003, "Behavior", 57, y + 98, 663, y + 120);
            Button(HitTarget.StartWithWindows, "Start with Windows, in the tray", 57, y + 117, 663, y + 145, check: vm.StartWithWindows);
            Button(HitTarget.CheckForUpdates, "Check for updates automatically", 57, y + 146, 663, y + 174, check: vm.CheckForUpdates);
            Label(3004, "Both can be changed later in Settings inside the app.", 83, y + 180, 663, y + 207);
        }
        else if (vm.IsBusy)
            Label(3000, vm.StatusText, 180, 303, 600, 330);
        else if (vm.IsConfirmUninstall)
        {
            Label(3000, "Remove ThisIsMyPC from this PC? The app, its shortcuts, and its entry in Installed apps go away.", 57, 153, 663, 195);
            Label(3001, "Your settings and change history stay on this PC for a later install. Changes you applied to Windows stay as they are. Undo them in the app first if you want them reverted.", 57, 205, 663, 290);
            Label(3002, "Choose Remove to continue, or Back to keep it.", 57, 307, 663, 335);
        }
        else if (vm.IsDone)
        {
            if (vm.Failed)
            {
                Label(3000, vm.ErrorText!, 57, 153, 663, 320);
                if (vm.HasLogPath)
                    Label(3001, "Details were saved to " + vm.LogPath, 57, 340, 663, 450);
            }
            else if (vm.Removed)
                Label(3000, "ThisIsMyPC was removed. Your settings and change history stay on this PC in case you install it again.", 57, 153, 663, 230);
            else
            {
                Label(3000, "ThisIsMyPC is installed. Windows asks for permission only when a change needs administrator access.", 57, 153, 663, 191);
                if (vm.RebootRequired)
                    Label(3001, "Windows asked for a restart to finish. Restart when convenient.", 57, 195, 663, 225);
                var y = vm.RebootRequired ? 232 : 203;
                Button(HitTarget.Launch, "Open ThisIsMyPC when I choose Finish", 57, y - 5, 663, y + 24, check: vm.LaunchWhenDone);
            }
        }
        if (vm.CanGoBack)
            Button(HitTarget.Back, "< Back", 358, 583, 454, 621);
        var left = !vm.CanGoBack && !vm.CanCancel ? 592 : 464;
        Button(HitTarget.Primary, vm.PrimaryButtonText.Trim(), left, 583, left + 96, 621, vm.CanGoPrimary);
        if (vm.CanCancel)
            Button(HitTarget.Cancel, "Cancel", 592, 583, 688, 621);
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
                        (spec.Target is HitTarget.WelcomeTab or HitTarget.LicenseTab or HitTarget.OptionsTab
                            ? 0x1002u : spec.Checked.HasValue ? 2u : 0u); // BS_PUSHLIKE | BS_CHECKBOX preserves selection without radio focus activation.
                else
                    style |= spec.Id == 3010 ? 0x281u : 0x80u; // SS_NOPREFIX; tab status also centers horizontally and vertically.
                var handle = NativeMethods.CreateWindowEx(0, spec.Target == HitTarget.None ? "STATIC" : "BUTTON", spec.Text,
                    style, 0, 0, 0, 0, _hwnd, (nint)spec.Id, NativeMethods.GetModuleHandle(null), 0);
                if (handle == nint.Zero)
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows could not create an installer control.");
                control = new(handle, spec);
                _nativeControls.Add(spec.Id, control);
                if (spec.Target != HitTarget.None && !NativeMethods.SetWindowSubclass(handle,
                    (nint)(delegate* unmanaged[Stdcall]<nint, uint, nuint, nint, nuint, nuint, nint>)&ButtonProcedure,
                    1, (nuint)GCHandle.ToIntPtr(_selfHandle)))
                    throw new Win32Exception("Windows could not style an installer control.");
            }
            if (control.Spec.Text != spec.Text)
                _ = NativeMethods.SetWindowText(control.Handle, spec.Text);
            _nativeControls[spec.Id] = control with { Spec = spec };
            _ = NativeMethods.SendMessage(control.Handle, NativeMethods.WM_SETFONT, (nuint)_bodyFont, 1);
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
            Print(_licenseEdit, 57, 200);
        if (_viewModel.IsOptions)
            Print(_folderEdit, 57, 178);
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

    private static bool UsesHighContrast()
    {
        var contrast = new NativeMethods.HIGHCONTRAST { cbSize = (uint)sizeof(NativeMethods.HIGHCONTRAST) };
        return NativeMethods.GetHighContrast(0x42, contrast.cbSize, ref contrast, 0) && (contrast.dwFlags & 1) != 0;
    }

    // Only drawing is replaced. Windows retains button input, focus, accessibility, and check state.
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static nint ButtonProcedure(nint hwnd, uint message, nuint wParam, nint lParam, nuint id, nuint data)
    {
        try
        {
            return HandleButtonMessage(hwnd, message, wParam, lParam, id, data);
        }
#pragma warning disable CA1031 // Reverse P/Invoke must never propagate a managed exception into Windows.
        catch (Exception)
        {
            return NativeMethods.DefSubclassProc(hwnd, message, wParam, lParam);
        }
#pragma warning restore CA1031
    }

    private static nint HandleButtonMessage(nint hwnd, uint message, nuint wParam, nint lParam, nuint id, nuint data)
    {
        if (message == 0x82) // WM_NCDESTROY occurs before the parent's GCHandle is released.
        {
            _ = NativeMethods.RemoveWindowSubclass(hwnd,
                (nint)(delegate* unmanaged[Stdcall]<nint, uint, nuint, nint, nuint, nuint, nint>)&ButtonProcedure, id);
            return NativeMethods.DefSubclassProc(hwnd, message, wParam, lParam);
        }
        if (message is NativeMethods.WM_PAINT or NativeMethods.WM_PRINT or 0x318 or 0x14 && !UsesHighContrast())
        {
            if (message == 0x14) // WM_ERASEBKGND. The paint covers the whole control.
                return 1;
            if (GCHandle.FromIntPtr((nint)data).Target is InstallerWindow window)
            {
                var control = window._nativeControls.Values.FirstOrDefault(item => item.Handle == hwnd);
                if (control is not null)
                {
                    var paint = default(NativeMethods.PAINTSTRUCT);
                    var dc = message == NativeMethods.WM_PAINT ? NativeMethods.BeginPaint(hwnd, out paint) : (nint)wParam;
                    try
                    {
                        _ = NativeMethods.GetClientRect(hwnd, out var rect);
                        var state = unchecked((uint)NativeMethods.SendMessage(hwnd, 0xF2, 0, 0)); // BM_GETSTATE.
                        InstallerRenderer.DrawNativeButton(dc, control.Spec.Text, rect, window._dpi / 96d,
                            control.Spec.Target, control.Spec.Enabled, control.Spec.Checked, (state & 0x204) != 0,
                            (state & 8) != 0);
                    }
                    finally
                    {
                        if (message == NativeMethods.WM_PAINT)
                            _ = NativeMethods.EndPaint(hwnd, in paint);
                    }
                    return 0;
                }
            }
        }
        return NativeMethods.DefSubclassProc(hwnd, message, wParam, lParam);
    }
}
