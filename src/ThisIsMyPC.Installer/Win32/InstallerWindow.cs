using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using ThisIsMyPC.Installer.Services;
using ThisIsMyPC.Installer.ViewModels;

namespace ThisIsMyPC.Installer.Win32;

internal sealed unsafe partial class InstallerWindow : IDisposable
{
    private const string WindowClassName = "ThisIsMyPC.NativeInstaller";
    private const int LogicalWidth = 600;
    private const int LogicalHeight = 480;
    private const int LicenseEditId = 1001;
    private const int FolderEditId = 1002;
    private const uint WindowStyle = NativeMethods.WS_OVERLAPPED | NativeMethods.WS_CAPTION |
        NativeMethods.WS_SYSMENU | NativeMethods.WS_MINIMIZEBOX | NativeMethods.WS_CLIPCHILDREN;

    private static readonly object ClassLock = new();
    private static bool _classRegistered;

    private readonly InstallerViewModel _viewModel;
    private readonly Win32SynchronizationContext _context = new();
    private GCHandle _selfHandle;
    private nint _hwnd;
    private nint _licenseEdit;
    private nint _folderEdit;
    private nint _bodyFont;
    private nint _monoFont;
    private nint _headingFont;
    private readonly nint _dialogBrush = NativeMethods.CreateSolidBrush(InstallerRenderer.DialogColor);
    private readonly nint _panelBrush = NativeMethods.CreateSolidBrush(InstallerRenderer.PanelColor);
    private int _dpi = 96;
    private bool _updatingFolder;
    private bool _disposed;
    private HitTarget _keyboardFocus;
    internal HitTarget KeyboardFocus => _keyboardFocus;
    internal nint WindowHandle => _hwnd;

    internal InstallerWindow(InstallerViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        _viewModel = viewModel;
    }

    internal int Run(string title = "Install ThisIsMyPC")
    {
        _ = NativeMethods.SetProcessDpiAwarenessContext(NativeMethods.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
        EnsureWindowClass();
        var oleInitialized = NativeMethods.OleInitialize(nint.Zero) >= 0;
        var previousContext = SynchronizationContext.Current;
        try
        {
            var instance = NativeMethods.GetModuleHandle(null);
            _dpi = checked((int)NativeMethods.GetDpiForSystem());
            var scale = _dpi / 96d;
            var bounds = new NativeMethods.RECT(0, 0, Scale(LogicalWidth, scale), Scale(LogicalHeight, scale));
            if (!NativeMethods.AdjustWindowRectExForDpi(ref bounds, WindowStyle, false, 0, (uint)_dpi))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows could not size the installer window.");

            var work = GetWorkArea(nint.Zero);
            var width = Math.Min(bounds.Right - bounds.Left, work.Right - work.Left);
            var height = Math.Min(bounds.Bottom - bounds.Top, work.Bottom - work.Top);
            var x = work.Left + (work.Right - work.Left - width) / 2;
            var y = work.Top + (work.Bottom - work.Top - height) / 2;
            _selfHandle = GCHandle.Alloc(this);
            _hwnd = NativeMethods.CreateWindowEx(
                0,
                WindowClassName,
                title,
                WindowStyle,
                x,
                y,
                width,
                height,
                nint.Zero,
                nint.Zero,
                instance,
                GCHandle.ToIntPtr(_selfHandle));
            if (_hwnd == nint.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows could not create the installer window.");

            ReplaceControlFonts();
            _context.Attach(_hwnd);
            SynchronizationContext.SetSynchronizationContext(_context);
            _viewModel.FolderPicker = new StorageFolderPicker(_hwnd);
            _viewModel.RequestClose = Close;
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            Refresh();
            _ = NativeMethods.ShowWindow(_hwnd, NativeMethods.SW_SHOWNORMAL);
            _ = NativeMethods.UpdateWindow(_hwnd);

            while (true)
            {
                var result = NativeMethods.GetMessage(out var message, nint.Zero, 0, 0);
                if (result == 0)
                    return unchecked((int)message.wParam);
                if (result < 0)
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "The installer message loop stopped.");
                if (message.message == NativeMethods.WM_KEYDOWN &&
                    (message.wParam is NativeMethods.VK_RETURN or NativeMethods.VK_ESCAPE or NativeMethods.VK_TAB ||
                     (message.wParam == NativeMethods.VK_SPACE && message.hwnd != _folderEdit && message.hwnd != _licenseEdit)))
                {
                    HandleKey(unchecked((int)message.wParam), NativeMethods.GetKeyState(NativeMethods.VK_SHIFT) < 0);
                    continue;
                }
                _ = NativeMethods.TranslateMessage(in message);
                _ = NativeMethods.DispatchMessage(in message);
            }
        }
        finally
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _viewModel.RequestClose = null;
            _viewModel.FolderPicker = null;
            SynchronizationContext.SetSynchronizationContext(previousContext);
            if (oleInitialized)
                NativeMethods.OleUninitialize();
        }
    }

    internal static byte[] RenderPreviewBgra(InstallerViewModel viewModel, int width = LogicalWidth, int height = LogicalHeight,
        HitTarget keyboardFocus = HitTarget.None)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width));

        var info = new NativeMethods.BITMAPINFO
        {
            bmiHeader = new NativeMethods.BITMAPINFOHEADER
            {
                biSize = (uint)sizeof(NativeMethods.BITMAPINFOHEADER),
                biWidth = width,
                biHeight = -height,
                biPlanes = 1,
                biBitCount = 32,
                biCompression = NativeMethods.BI_RGB,
            },
        };
        var dc = NativeMethods.CreateCompatibleDC(nint.Zero);
        if (dc == nint.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error());
        var bitmap = NativeMethods.CreateDIBSection(dc, in info, NativeMethods.DIB_RGB_COLORS, out var bits, nint.Zero, 0);
        if (bitmap == nint.Zero || bits == nint.Zero)
        {
            _ = NativeMethods.DeleteDC(dc);
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        var previous = NativeMethods.SelectObject(dc, bitmap);
        try
        {
            using var preview = new InstallerWindow(viewModel) { _keyboardFocus = keyboardFocus, _preview = true };
            preview.CreatePreview(width, height);
            InstallerRenderer.Draw(dc, width, height, width / (double)LogicalWidth);
            preview.PrintControls(dc);
            preview.DrawKeyboardFocus(dc);
            var pixels = new byte[checked(width * height * 4)];
            Marshal.Copy(bits, pixels, 0, pixels.Length);
            for (var index = 3; index < pixels.Length; index += 4)
                pixels[index] = byte.MaxValue;
            return pixels;
        }
        finally
        {
            _ = NativeMethods.SelectObject(dc, previous);
            _ = NativeMethods.DeleteObject(bitmap);
            _ = NativeMethods.DeleteDC(dc);
        }
    }

    private static void EnsureWindowClass()
    {
        lock (ClassLock)
        {
            if (_classRegistered)
                return;
            var className = Marshal.StringToHGlobalUni(WindowClassName);
            try
            {
                var instance = NativeMethods.GetModuleHandle(null);
                var applicationIcon = NativeMethods.LoadIcon(instance, (nint)NativeMethods.IDI_APPLICATION);
                if (applicationIcon == nint.Zero)
                    applicationIcon = NativeMethods.LoadIcon(nint.Zero, (nint)NativeMethods.IDI_APPLICATION);
                var windowClass = new NativeMethods.WNDCLASSEXW
                {
                    cbSize = (uint)sizeof(NativeMethods.WNDCLASSEXW),
                    lpfnWndProc = (nint)(delegate* unmanaged[Stdcall]<nint, uint, nuint, nint, nint>)&WindowProcedure,
                    hInstance = instance,
                    hCursor = NativeMethods.LoadCursor(nint.Zero, (nint)NativeMethods.IDC_ARROW),
                    hIcon = applicationIcon,
                    hIconSm = applicationIcon,
                    hbrBackground = nint.Zero,
                    lpszClassName = className,
                };
                if (NativeMethods.RegisterClassEx(ref windowClass) == 0)
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows could not register the installer window.");
                _classRegistered = true;
            }
            finally
            {
                Marshal.FreeHGlobal(className);
            }
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static nint WindowProcedure(nint hwnd, uint message, nuint wParam, nint lParam)
    {
        try
        {
            if (message == NativeMethods.WM_NCCREATE)
            {
                var create = (NativeMethods.CREATESTRUCTW*)lParam;
                _ = NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWLP_USERDATA, create->lpCreateParams);
            }

            var pointer = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWLP_USERDATA);
            if (pointer != nint.Zero && GCHandle.FromIntPtr(pointer).Target is InstallerWindow window)
                return window.HandleMessage(hwnd, message, wParam, lParam);
        }
        catch (Exception ex)
        {
            _ = NativeMethods.MessageBox(hwnd, "The installer window stopped.\n\n" + ex.Message, "ThisIsMyPC installer", 0x10);
            _ = NativeMethods.DestroyWindow(hwnd);
            return nint.Zero;
        }

        return NativeMethods.DefWindowProc(hwnd, message, wParam, lParam);
    }

    private nint HandleMessage(nint hwnd, uint message, nuint wParam, nint lParam)
    {
        switch (message)
        {
            case NativeMethods.WM_CREATE:
                CreateChildControls(hwnd);
                return nint.Zero;
            case NativeMethods.WM_SIZE:
                if (_hwnd != nint.Zero && !_preview)
                    UpdateScrollBars();
                return nint.Zero;
            case NativeMethods.WM_VSCROLL:
            case NativeMethods.WM_HSCROLL:
                Scroll(message == NativeMethods.WM_HSCROLL, unchecked((int)(wParam & 0xffff)));
                return nint.Zero;
            case NativeMethods.WM_MOUSEWHEEL:
                Scroll(false, SignedHighWord((nint)wParam) > 0 ? 0 : 1);
                return nint.Zero;
            case NativeMethods.WM_PAINT:
                Paint(hwnd);
                return nint.Zero;
            case NativeMethods.WM_COMMAND:
                HandleCommand(wParam, lParam);
                return nint.Zero;
            case NativeMethods.WM_LBUTTONUP:
                HandleClick(SignedLowWord(lParam), SignedHighWord(lParam));
                return nint.Zero;
            case NativeMethods.WM_DPICHANGED:
                ApplyDpiChange(wParam, lParam);
                return nint.Zero;
            case NativeMethods.WM_KEYDOWN:
                HandleKey(unchecked((int)wParam));
                return nint.Zero;
            case NativeMethods.WM_CTLCOLORSTATIC:
            case NativeMethods.WM_CTLCOLORBTN:
                if (lParam == _licenseEdit)
                {
                    _ = NativeMethods.SetTextColor((nint)wParam, NativeMethods.GetSysColor(8));
                    _ = NativeMethods.SetBkColor((nint)wParam, NativeMethods.GetSysColor(5));
                    return NativeMethods.GetSysColorBrush(5);
                }
                if (lParam == _folderEdit)
                    break;
                var header = _nativeControls.Values.Any(control => control.Handle == lParam && control.Spec.Id is 3020 or 3021);
                _ = NativeMethods.SetTextColor((nint)wParam, InstallerRenderer.TextColor);
                _ = NativeMethods.SetBkColor((nint)wParam, header ? InstallerRenderer.DialogColor : InstallerRenderer.PanelColor);
                return header ? _dialogBrush : _panelBrush;
            case NativeMethods.WM_APP_CALLBACK:
                _context.Drain();
                return nint.Zero;
            case NativeMethods.WM_CLOSE:
                if (_viewModel.CanCancel || _viewModel.IsFinished)
                    _ = NativeMethods.DestroyWindow(hwnd);
                return nint.Zero;
            case NativeMethods.WM_DESTROY:
                _hwnd = nint.Zero;
                if (!_preview)
                    NativeMethods.PostQuitMessage(0);
                return nint.Zero;
        }

        return NativeMethods.DefWindowProc(hwnd, message, wParam, lParam);
    }

    private void CreateChildControls(nint hwnd)
    {
        _dpi = checked((int)NativeMethods.GetDpiForWindow(hwnd));
        var scale = _dpi / 96d;
        var commonControls = new NativeMethods.INITCOMMONCONTROLSEX { Size = 8, Classes = 0x20 };
        if (!NativeMethods.InitCommonControlsEx(in commonControls))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        _bodyFont = NativeMethods.CreateFont(-Scale(14, scale), 0, 0, 0, NativeMethods.FW_NORMAL, 0, 0, 0,
            NativeMethods.DEFAULT_CHARSET, 0, 0, NativeMethods.CLEARTYPE_QUALITY, 0, "Segoe UI");
        _monoFont = NativeMethods.CreateFont(-Scale(13, scale), 0, 0, 0, NativeMethods.FW_NORMAL, 0, 0, 0,
            NativeMethods.DEFAULT_CHARSET, 0, 0, NativeMethods.CLEARTYPE_QUALITY, 0, "Consolas");
        var instance = NativeMethods.GetModuleHandle(null);
        _licenseEdit = NativeMethods.CreateWindowEx(
            NativeMethods.WS_EX_CLIENTEDGE,
            "EDIT",
            _viewModel.LicenseText.ReplaceLineEndings("\r\n"),
            NativeMethods.WS_CHILD | NativeMethods.WS_VSCROLL | NativeMethods.WS_HSCROLL |
                NativeMethods.ES_LEFT | NativeMethods.ES_MULTILINE | NativeMethods.ES_AUTOVSCROLL |
                NativeMethods.ES_AUTOHSCROLL | NativeMethods.ES_READONLY | NativeMethods.WS_TABSTOP,
            0, 0, 0, 0, hwnd, (nint)LicenseEditId, instance, nint.Zero);
        _folderEdit = NativeMethods.CreateWindowEx(
            NativeMethods.WS_EX_CLIENTEDGE,
            "EDIT",
            _viewModel.InstallFolder,
            NativeMethods.WS_CHILD | NativeMethods.ES_LEFT | NativeMethods.ES_AUTOHSCROLL | NativeMethods.WS_TABSTOP,
            0, 0, 0, 0, hwnd, (nint)FolderEditId, instance, nint.Zero);
        if (_licenseEdit == nint.Zero || _folderEdit == nint.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows could not create the installer fields.");
        _ = NativeMethods.SendMessage(_licenseEdit, NativeMethods.WM_SETFONT, (nuint)_monoFont, (nint)1);
        _ = NativeMethods.SendMessage(_folderEdit, NativeMethods.WM_SETFONT, (nuint)_bodyFont, (nint)1);
        _ = NativeMethods.SendMessage(_folderEdit, NativeMethods.EM_SETMARGINS,
            NativeMethods.EC_LEFTMARGIN | NativeMethods.EC_RIGHTMARGIN, (nint)((6 << 16) | 6));

    }

    private void Paint(nint hwnd)
    {
        var dc = NativeMethods.BeginPaint(hwnd, out var paint);
        try
        {
            _ = NativeMethods.GetClientRect(hwnd, out var bounds);
            var width = Scale(LogicalWidth, _dpi / 96d);
            var height = Scale(LogicalHeight, _dpi / 96d);
            _ = NativeMethods.SetViewportOrgEx(dc, -_scrollX, -_scrollY, nint.Zero);
            InstallerRenderer.Draw(dc, width, height, _dpi / 96d);
            DrawKeyboardFocus(dc);
        }
        finally
        {
            _ = NativeMethods.EndPaint(hwnd, in paint);
        }
    }

    private void ApplyDpiChange(nuint wParam, nint lParam)
    {
        _dpi = checked((int)(wParam & 0xffff));
        var suggested = (NativeMethods.RECT*)lParam;
        var work = GetWorkArea(*suggested);
        _ = NativeMethods.SetWindowPos(
            _hwnd,
            nint.Zero,
            Math.Clamp(suggested->Left, work.Left, Math.Max(work.Left, work.Right - Math.Min(suggested->Right - suggested->Left, work.Right - work.Left))),
            Math.Clamp(suggested->Top, work.Top, Math.Max(work.Top, work.Bottom - Math.Min(suggested->Bottom - suggested->Top, work.Bottom - work.Top))),
            Math.Min(suggested->Right - suggested->Left, work.Right - work.Left),
            Math.Min(suggested->Bottom - suggested->Top, work.Bottom - work.Top),
            NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
        ReplaceControlFonts();
        UpdateScrollBars();
        Refresh();
    }

    private void ReplaceControlFonts()
    {
        var scale = _dpi / 96d;
        var oldBodyFont = _bodyFont;
        var oldMonoFont = _monoFont;
        var oldHeadingFont = _headingFont;
        var bodyFont = NativeMethods.CreateFont(-Scale(14, scale), 0, 0, 0, NativeMethods.FW_NORMAL, 0, 0, 0,
            NativeMethods.DEFAULT_CHARSET, 0, 0, NativeMethods.CLEARTYPE_QUALITY, 0, "Segoe UI");
        var monoFont = NativeMethods.CreateFont(-Scale(13, scale), 0, 0, 0, NativeMethods.FW_NORMAL, 0, 0, 0,
            NativeMethods.DEFAULT_CHARSET, 0, 0, NativeMethods.CLEARTYPE_QUALITY, 0, "Consolas");
        if (bodyFont == nint.Zero || monoFont == nint.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows could not create the installer fonts.");
        _headingFont = NativeMethods.CreateFont(-Scale(14, scale), 0, 0, 0, NativeMethods.FW_BOLD, 0, 0, 0,
            NativeMethods.DEFAULT_CHARSET, 0, 0, NativeMethods.CLEARTYPE_QUALITY, 0, "Segoe UI");
        _bodyFont = bodyFont;
        _monoFont = monoFont;
        _ = NativeMethods.SendMessage(_licenseEdit, NativeMethods.WM_SETFONT, (nuint)_monoFont, (nint)1);
        _ = NativeMethods.SendMessage(_folderEdit, NativeMethods.WM_SETFONT, (nuint)_bodyFont, (nint)1);
        if (oldHeadingFont != nint.Zero)
            _ = NativeMethods.DeleteObject(oldHeadingFont);
        if (oldBodyFont != nint.Zero)
            _ = NativeMethods.DeleteObject(oldBodyFont);
        if (oldMonoFont != nint.Zero)
            _ = NativeMethods.DeleteObject(oldMonoFont);
    }

    private void HandleCommand(nuint wParam, nint lParam)
    {
        var id = unchecked((int)(wParam & 0xffff));
        var notification = unchecked((uint)((wParam >> 16) & 0xffff));
        if (notification == 0 && _nativeControls.TryGetValue(id, out var control) && control.Handle == lParam && control.Spec.Target != HitTarget.None)
        {
            Activate(control.Spec.Target);
            return;
        }
        if (id == FolderEditId && notification == NativeMethods.EN_CHANGE && lParam == _folderEdit && !_updatingFolder)
            _viewModel.InstallFolder = ReadWindowText(_folderEdit);
    }

    private void HandleClick(int physicalX, int physicalY)
        => HandleLogicalClick(Logical(physicalX + _scrollX), Logical(physicalY + _scrollY));

    internal void HandleLogicalClick(int x, int y)
    {
        var target = HitTest(x, y);
        _keyboardFocus = HitTarget.None;
        if (_hwnd != nint.Zero)
            _ = NativeMethods.SetFocus(_hwnd);
        switch (target)
        {
            case HitTarget.Uninstall:
                _viewModel.UninstallMode = !_viewModel.UninstallMode;
                break;
            case HitTarget.LicenseAccepted:
                _viewModel.LicenseAccepted = !_viewModel.LicenseAccepted;
                break;
            case HitTarget.StartMenu:
                _viewModel.StartMenuShortcut = !_viewModel.StartMenuShortcut;
                break;
            case HitTarget.Desktop:
                _viewModel.DesktopShortcut = !_viewModel.DesktopShortcut;
                break;
            case HitTarget.StartWithWindows:
                _viewModel.StartWithWindows = !_viewModel.StartWithWindows;
                break;
            case HitTarget.CheckForUpdates:
                _viewModel.CheckForUpdates = !_viewModel.CheckForUpdates;
                break;
            case HitTarget.Launch:
                _viewModel.LaunchWhenDone = !_viewModel.LaunchWhenDone;
                break;
            case HitTarget.Browse:
                _viewModel.BrowseCommand.Execute(null);
                break;
            case HitTarget.Back when _viewModel.BackCommand.CanExecute(null):
                _viewModel.BackCommand.Execute(null);
                break;
            case HitTarget.Primary when _viewModel.PrimaryCommand.CanExecute(null):
                SyncFolderText();
                _viewModel.PrimaryCommand.Execute(null);
                break;
            case HitTarget.Cancel when _viewModel.CancelCommand.CanExecute(null):
                _viewModel.CancelCommand.Execute(null);
                break;
        }
        Refresh();
    }

    internal void HandleKey(int key, bool reverse = false)
    {
        var targets = KeyboardTargets();
        var nativeFocus = _hwnd == nint.Zero ? nint.Zero : NativeMethods.GetFocus();
        var focusedControl = _nativeControls.Values.FirstOrDefault(control => control.Handle == nativeFocus);
        if (focusedControl is not null && focusedControl.Spec.Target != HitTarget.None)
            _keyboardFocus = focusedControl.Spec.Target;
        if (nativeFocus != nint.Zero && nativeFocus == _folderEdit)
            _keyboardFocus = HitTarget.FolderEdit;
        else if (nativeFocus != nint.Zero && nativeFocus == _licenseEdit)
            _keyboardFocus = HitTarget.LicenseEdit;
        if (key == NativeMethods.VK_TAB)
        {
            var focused = _hwnd == nint.Zero ? nint.Zero : NativeMethods.GetFocus();
            var current = focused != nint.Zero && focused == _folderEdit ? HitTarget.FolderEdit :
                focused != nint.Zero && focused == _licenseEdit ? HitTarget.LicenseEdit : _keyboardFocus;
            var index = targets.FindIndex(item => item.Target == current);
            if (targets.Count == 0)
                return;
            index = index < 0 ? (reverse ? targets.Count - 1 : 0) :
                (index + (reverse ? targets.Count - 1 : 1)) % targets.Count;
            _keyboardFocus = targets[index].Target;
            if (_hwnd != nint.Zero)
                _ = NativeMethods.SetFocus(_keyboardFocus == HitTarget.FolderEdit ? _folderEdit :
                    _keyboardFocus == HitTarget.LicenseEdit ? _licenseEdit : ControlHandle(_keyboardFocus));
            EnsureFocusVisible(_keyboardFocus);
            Invalidate();
            return;
        }
        if ((key is NativeMethods.VK_SPACE or NativeMethods.VK_RETURN) &&
            _keyboardFocus is not (HitTarget.None or HitTarget.FolderEdit or HitTarget.LicenseEdit))
        {
            var item = targets.Find(item => item.Target == _keyboardFocus);
            if (item.Target != HitTarget.None)
            {
                var previousStep = _viewModel.Step;
                HandleLogicalClick((item.Bounds.Left + item.Bounds.Right) / 2, (item.Bounds.Top + item.Bounds.Bottom) / 2);
                if (_viewModel.Step == previousStep)
                {
                    _keyboardFocus = item.Target;
                    if (_hwnd != nint.Zero)
                        _ = NativeMethods.SetFocus(ControlHandle(item.Target));
                }
                Invalidate();
            }
            return;
        }
        if (key == NativeMethods.VK_RETURN && _viewModel.PrimaryCommand.CanExecute(null))
        {
            SyncFolderText();
            _viewModel.PrimaryCommand.Execute(null);
            Refresh();
        }
        else if (key == NativeMethods.VK_ESCAPE && _viewModel.CancelCommand.CanExecute(null))
        {
            _viewModel.CancelCommand.Execute(null);
        }
    }

    private List<(HitTarget Target, UiRect Bounds)> KeyboardTargets()
    {
        var result = DescribeControls()
            .Where(control => control.Target != HitTarget.None && control.Enabled)
            .Select(control => (control.Target, control.Bounds)).ToList();
        if (_viewModel.IsLicense)
            result.Insert(0, (HitTarget.LicenseEdit, UiRect.FromEdges(28, 130, 572, 376)));
        if (_viewModel.IsOptions && _viewModel.CanChooseFolder)
            result.Insert(0, (HitTarget.FolderEdit, UiRect.FromEdges(28, 105, 472, 133)));
        return result;
    }

    private void DrawKeyboardFocus(nint dc)
    {
        var item = KeyboardTargets().Find(item => item.Target == _keyboardFocus);
        if (item.Target is HitTarget.None or HitTarget.FolderEdit or HitTarget.LicenseEdit)
            return;
        var scale = _dpi / 96d;
        var bounds = new NativeMethods.RECT(Scale(item.Bounds.Left + 2, scale), Scale(item.Bounds.Top + 2, scale),
            Scale(item.Bounds.Right - 2, scale), Scale(item.Bounds.Bottom - 2, scale));
        _ = NativeMethods.DrawFocusRect(dc, in bounds);
    }

    private HitTarget HitTest(int x, int y)
        => KeyboardTargets().FirstOrDefault(item => item.Bounds.Contains(x, y)).Target;

    private void Refresh()
    {
        if (_hwnd == nint.Zero)
            return;
        UpdateChildControls();
        UpdateNativeControls();
        if (!_preview)
            UpdateScrollBars();
        Invalidate();
    }

    private void UpdateChildControls()
    {
        var scale = _dpi / 96d;
        var showLicense = _viewModel.IsLicense;
        var showFolder = _viewModel.IsOptions;
        _ = NativeMethods.ShowWindow(_licenseEdit, showLicense ? NativeMethods.SW_SHOWNA : NativeMethods.SW_HIDE);
        _ = NativeMethods.ShowWindow(_folderEdit, showFolder ? NativeMethods.SW_SHOWNA : NativeMethods.SW_HIDE);
        if (showLicense)
            _ = NativeMethods.MoveWindow(_licenseEdit, Scale(28, scale) - _scrollX, Scale(130, scale) - _scrollY, Scale(544, scale), Scale(246, scale), true);
        if (showFolder)
        {
            _ = NativeMethods.MoveWindow(_folderEdit, Scale(28, scale) - _scrollX, Scale(105, scale) - _scrollY, Scale(444, scale), Scale(28, scale), true);
            _ = NativeMethods.EnableWindow(_folderEdit, _viewModel.CanChooseFolder);
            var current = ReadWindowText(_folderEdit);
            if (!string.Equals(current, _viewModel.InstallFolder, StringComparison.Ordinal))
            {
                _updatingFolder = true;
                try
                {
                    _ = NativeMethods.SetWindowText(_folderEdit, _viewModel.InstallFolder);
                }
                finally
                {
                    _updatingFolder = false;
                }
            }
        }
    }

    private void SyncFolderText()
    {
        if (_viewModel.IsOptions && _folderEdit != nint.Zero)
            _viewModel.InstallFolder = ReadWindowText(_folderEdit);
    }

    private static string ReadWindowText(nint hwnd)
    {
        var length = NativeMethods.GetWindowTextLength(hwnd);
        var buffer = new char[length + 1];
        var read = NativeMethods.GetWindowText(hwnd, buffer, buffer.Length);
        return new string(buffer, 0, Math.Max(0, read));
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        => _context.Post(static state => ((InstallerWindow)state!).Refresh(), this);

    private void Close()
    {
        if (_hwnd != nint.Zero)
            _ = NativeMethods.DestroyWindow(_hwnd);
    }

    private void Invalidate()
    {
        if (_hwnd != nint.Zero)
            _ = NativeMethods.InvalidateRect(_hwnd, nint.Zero, false);
    }

    private int Logical(int physical) => (int)Math.Round(physical * 96d / _dpi);
    private static int OptionsShortcutsY(InstallerViewModel viewModel)
    {
        var detailY = 140;
        if (!viewModel.CanChooseFolder)
            detailY += 24;
        if (viewModel.HasFolderError || viewModel.HasFolderWarning)
            detailY += 28;
        return Math.Max(148, detailY + 8);
    }
    private static int Scale(int logical, double scale) => (int)Math.Round(logical * scale);
    private static int SignedLowWord(nint value) => unchecked((short)((long)value & 0xffff));
    private static int SignedHighWord(nint value) => unchecked((short)(((long)value >> 16) & 0xffff));

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        if (_hwnd != nint.Zero)
            _ = NativeMethods.DestroyWindow(_hwnd);
        if (_headingFont != nint.Zero)
            _ = NativeMethods.DeleteObject(_headingFont);
        _ = NativeMethods.DeleteObject(_dialogBrush);
        _ = NativeMethods.DeleteObject(_panelBrush);
        if (_bodyFont != nint.Zero)
            _ = NativeMethods.DeleteObject(_bodyFont);
        if (_monoFont != nint.Zero)
            _ = NativeMethods.DeleteObject(_monoFont);
        if (_selfHandle.IsAllocated)
            _selfHandle.Free();
    }

    internal enum HitTarget
    {
        None,
        Uninstall,
        LicenseAccepted,
        StartMenu,
        Desktop,
        StartWithWindows,
        CheckForUpdates,
        Launch,
        Browse,
        Back,
        Primary,
        Cancel,
        LicenseEdit,
        FolderEdit,
    }

    private readonly record struct UiRect(int Left, int Top, int Right, int Bottom)
    {
        internal static UiRect FromEdges(int left, int top, int right, int bottom) => new(left, top, right, bottom);
        internal bool Contains(int x, int y) => x >= Left && x < Right && y >= Top && y < Bottom;
    }

    private sealed class Win32SynchronizationContext : SynchronizationContext
    {
        private readonly ConcurrentQueue<(SendOrPostCallback Callback, object? State)> _queue = new();
        private nint _hwnd;

        internal void Attach(nint hwnd) => _hwnd = hwnd;

        public override void Post(SendOrPostCallback d, object? state)
        {
            ArgumentNullException.ThrowIfNull(d);
            _queue.Enqueue((d, state));
            if (_hwnd != nint.Zero)
                _ = NativeMethods.PostMessage(_hwnd, NativeMethods.WM_APP_CALLBACK, 0, nint.Zero);
        }

        internal void Drain()
        {
            while (_queue.TryDequeue(out var work))
                work.Callback(work.State);
        }
    }
}

internal static class InstallerRenderer
{
    // COLORREF uses BGR. Pale blue surfaces keep native themed controls legible.
    internal const uint DialogColor = 0xF8E9DF;
    internal const uint PanelColor = 0xFCF3ED;
    internal const uint TextColor = 0x663B19;

    internal static void Draw(nint dc, int width, int height, double scale)
    {
        void Fill(NativeMethods.RECT bounds, uint color)
        {
            var brush = NativeMethods.CreateSolidBrush(color);
            try { _ = NativeMethods.FillRect(dc, in bounds, brush); }
            finally { _ = NativeMethods.DeleteObject(brush); }
        }
        int Px(int value) => (int)Math.Round(value * scale);
        Fill(new(0, 0, width, height), DialogColor);
        Fill(new(Px(12), Px(72), Px(588), Px(424)), 0xCCA98C);
        Fill(new(Px(12) + 1, Px(72) + 1, Px(588) - 1, Px(424) - 1), PanelColor);
    }
}
