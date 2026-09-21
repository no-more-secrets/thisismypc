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
    /// <summary>Page area below the title bar. Page coordinates start under the title bar.</summary>
    private const int LogicalHeight = 480;
    internal const int TitleBarHeight = InstallerRenderer.TitleBarHeight;
    private const int ClientHeight = LogicalHeight + TitleBarHeight;
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
    private readonly nint _headerBrush = NativeMethods.CreateSolidBrush(InstallerRenderer.HeaderColor);
    private readonly nint _dialogBrush = NativeMethods.CreateSolidBrush(InstallerRenderer.DialogColor);
    private readonly nint _panelBrush = NativeMethods.CreateSolidBrush(InstallerRenderer.PanelColor);
    private int _dpi = 96;
    private bool _updatingFolder;
    private bool _disposed;
    private HitTarget _keyboardFocus;
    private string _caption = "Install ThisIsMyPC";
    private CaptionButton _hoverButton;
    private bool _trackingMouse;
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
        _caption = title;
        EnsureWindowClass();
        var oleInitialized = NativeMethods.OleInitialize(nint.Zero) >= 0;
        var previousContext = SynchronizationContext.Current;
        try
        {
            var instance = NativeMethods.GetModuleHandle(null);
            _dpi = checked((int)NativeMethods.GetDpiForSystem());
            var scale = _dpi / 96d;
            // The window draws its own frame: WM_NCCALCSIZE hands the whole rectangle to the client area.
            var work = GetWorkArea(nint.Zero);
            var width = Math.Min(Scale(LogicalWidth, scale), work.Right - work.Left);
            var height = Math.Min(Scale(ClientHeight, scale), work.Bottom - work.Top);
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

            var corners = NativeMethods.DWMWCP_ROUND;
            _ = NativeMethods.DwmSetWindowAttribute(_hwnd, NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE, in corners, sizeof(int));
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
            var scale = width / (double)LogicalWidth;
            _ = NativeMethods.SetViewportOrgEx(dc, 0, Scale(TitleBarHeight, scale), nint.Zero);
            InstallerRenderer.Draw(dc, width, Scale(LogicalHeight, scale), scale, viewModel);
            preview.PrintControls(dc);
            preview.DrawKeyboardFocus(dc);
            _ = NativeMethods.SetViewportOrgEx(dc, 0, 0, nint.Zero);
            preview.DrawTitleBar(dc, width);
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
            case NativeMethods.WM_NCCALCSIZE:
                // No stock frame: the client area covers the window and the title bar is drawn here.
                if (wParam != 0)
                    return nint.Zero;
                break;
            case NativeMethods.WM_NCHITTEST:
                return HitTestFrame(hwnd, message, wParam, lParam);
            case NativeMethods.WM_NCACTIVATE:
                // -1 keeps DefWindowProc from repainting a non-client area this window does not have.
                return NativeMethods.DefWindowProc(hwnd, message, wParam, -1);
            case NativeMethods.WM_MOUSEMOVE:
                TrackHover(SignedLowWord(lParam), SignedHighWord(lParam));
                return nint.Zero;
            case NativeMethods.WM_MOUSELEAVE:
                _trackingMouse = false;
                SetHover(CaptionButton.None);
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
            case 0x14: // WM_ERASEBKGND.
            case 0x318: // WM_PRINTCLIENT: themed buttons request the surface behind their transparent edges.
                var backgroundDc = (nint)wParam;
                var saved = NativeMethods.SaveDC(backgroundDc);
                try
                {
                    // Keep the origin Windows mapped from the child into parent coordinates.
                    _ = NativeMethods.OffsetViewportOrgEx(backgroundDc, -_scrollX, Scale(TitleBarHeight, _dpi / 96d) - _scrollY, 0);
                    InstallerRenderer.Draw(backgroundDc, Scale(LogicalWidth, _dpi / 96d),
                        Scale(LogicalHeight, _dpi / 96d), _dpi / 96d, _viewModel);
                }
                finally { _ = NativeMethods.RestoreDC(backgroundDc, saved); }
                return 1;
            case NativeMethods.WM_COMMAND:
                HandleCommand(wParam, lParam);
                return nint.Zero;
            case NativeMethods.WM_LBUTTONUP:
                var button = CaptionButtonAt(SignedLowWord(lParam), SignedHighWord(lParam));
                if (button == CaptionButton.Close)
                    _ = NativeMethods.PostMessage(hwnd, NativeMethods.WM_CLOSE, 0, nint.Zero);
                else if (button == CaptionButton.Minimize)
                    _ = NativeMethods.ShowWindow(hwnd, NativeMethods.SW_MINIMIZE);
                else
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
                var owner = _nativeControls.Values.FirstOrDefault(control => control.Handle == lParam);
                var surface = owner is null ? InstallerRenderer.PanelColor :
                    owner.Spec.Id is 3020 or 3021 ? InstallerRenderer.HeaderColor :
                    owner.Spec.Bounds.Top >= InstallerRenderer.FooterTop ? InstallerRenderer.DialogColor : InstallerRenderer.PanelColor;
                _ = NativeMethods.SetTextColor((nint)wParam, owner?.Spec.Id == 3021 ? InstallerRenderer.SubtitleColor : InstallerRenderer.TextColor);
                _ = NativeMethods.SetBkColor((nint)wParam, surface);
                return surface == InstallerRenderer.HeaderColor ? _headerBrush :
                    surface == InstallerRenderer.DialogColor ? _dialogBrush : _panelBrush;
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
        _monoFont = NativeMethods.CreateFont(-Scale(MonoFontSize, scale), 0, 0, 0, NativeMethods.FW_NORMAL, 0, 0, 0,
            NativeMethods.DEFAULT_CHARSET, 0, 0, NativeMethods.CLEARTYPE_QUALITY, 0, "Consolas");
        var instance = NativeMethods.GetModuleHandle(null);
        // The license wraps instead of scrolling sideways; the renderer outlines both fields.
        _licenseEdit = NativeMethods.CreateWindowEx(
            0,
            "EDIT",
            _viewModel.LicenseText.ReplaceLineEndings("\r\n"),
            NativeMethods.WS_CHILD | NativeMethods.WS_VSCROLL |
                NativeMethods.ES_LEFT | NativeMethods.ES_MULTILINE | NativeMethods.ES_AUTOVSCROLL |
                NativeMethods.ES_READONLY | NativeMethods.WS_TABSTOP,
            0, 0, 0, 0, hwnd, (nint)LicenseEditId, instance, nint.Zero);
        _folderEdit = NativeMethods.CreateWindowEx(
            0,
            "EDIT",
            _viewModel.InstallFolder,
            NativeMethods.WS_CHILD | NativeMethods.ES_LEFT | NativeMethods.ES_AUTOHSCROLL | NativeMethods.WS_TABSTOP,
            0, 0, 0, 0, hwnd, (nint)FolderEditId, instance, nint.Zero);
        if (_licenseEdit == nint.Zero || _folderEdit == nint.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows could not create the installer fields.");
        ApplyEditFonts();
    }

    // The license keeps exactly one blank column before its text so its 78-column lines still fit unwrapped.
    private void ApplyEditFonts()
    {
        var scale = _dpi / 96d;
        _ = NativeMethods.SendMessage(_licenseEdit, NativeMethods.WM_SETFONT, (nuint)_monoFont, (nint)1);
        _ = NativeMethods.SendMessage(_folderEdit, NativeMethods.WM_SETFONT, (nuint)_bodyFont, (nint)1);
        var inset = Scale(6, scale);
        _ = NativeMethods.SendMessage(_folderEdit, NativeMethods.EM_SETMARGINS,
            NativeMethods.EC_LEFTMARGIN | NativeMethods.EC_RIGHTMARGIN, (nint)((inset << 16) | inset));
        _ = NativeMethods.SendMessage(_licenseEdit, NativeMethods.EM_SETMARGINS,
            NativeMethods.EC_LEFTMARGIN | NativeMethods.EC_RIGHTMARGIN, (nint)MonoColumnWidth());
    }

    private int MonoColumnWidth()
    {
        var dc = NativeMethods.CreateCompatibleDC(nint.Zero);
        if (dc == nint.Zero)
            return Scale(MonoFontSize / 2, _dpi / 96d);
        try
        {
            var previous = NativeMethods.SelectObject(dc, _monoFont);
            var measured = NativeMethods.GetTextExtentPoint32(dc, "M", 1, out var size);
            _ = NativeMethods.SelectObject(dc, previous);
            return measured && size.X > 0 ? size.X : Scale(MonoFontSize / 2, _dpi / 96d);
        }
        finally { _ = NativeMethods.DeleteDC(dc); }
    }

    private void Paint(nint hwnd)
    {
        var dc = NativeMethods.BeginPaint(hwnd, out var paint);
        try
        {
            _ = NativeMethods.GetClientRect(hwnd, out var bounds);
            var width = Scale(LogicalWidth, _dpi / 96d);
            var height = Scale(LogicalHeight, _dpi / 96d);
            _ = NativeMethods.SetViewportOrgEx(dc, -_scrollX, Scale(TitleBarHeight, _dpi / 96d) - _scrollY, nint.Zero);
            InstallerRenderer.Draw(dc, width, height, _dpi / 96d, _viewModel);
            DrawKeyboardFocus(dc);
            // The title bar stays put while the page scrolls under it.
            _ = NativeMethods.SetViewportOrgEx(dc, 0, 0, nint.Zero);
            DrawTitleBar(dc, bounds.Right);
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
        var monoFont = NativeMethods.CreateFont(-Scale(MonoFontSize, scale), 0, 0, 0, NativeMethods.FW_NORMAL, 0, 0, 0,
            NativeMethods.DEFAULT_CHARSET, 0, 0, NativeMethods.CLEARTYPE_QUALITY, 0, "Consolas");
        if (bodyFont == nint.Zero || monoFont == nint.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows could not create the installer fonts.");
        _headingFont = NativeMethods.CreateFont(-Scale(20, scale), 0, 0, 0, NativeMethods.FW_SEMIBOLD, 0, 0, 0,
            NativeMethods.DEFAULT_CHARSET, 0, 0, NativeMethods.CLEARTYPE_QUALITY, 0, "Segoe UI");
        _bodyFont = bodyFont;
        _monoFont = monoFont;
        ApplyEditFonts();
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
        => HandleLogicalClick(Logical(physicalX + _scrollX), Logical(physicalY + _scrollY - Scale(TitleBarHeight, _dpi / 96d)));

    private nint HitTestFrame(nint hwnd, uint message, nuint wParam, nint lParam)
    {
        var hit = NativeMethods.DefWindowProc(hwnd, message, wParam, lParam);
        if (hit != NativeMethods.HTCLIENT || !NativeMethods.GetWindowRect(hwnd, out var window))
            return hit;
        // Client and window origins coincide: the frame is zero-sized and scrollbars sit right and bottom.
        var x = SignedLowWord(lParam) - window.Left;
        var y = SignedHighWord(lParam) - window.Top;
        return y >= 0 && y < Scale(TitleBarHeight, _dpi / 96d) && CaptionButtonAt(x, y) == CaptionButton.None
            ? NativeMethods.HTCAPTION
            : NativeMethods.HTCLIENT;
    }

    private CaptionButton CaptionButtonAt(int physicalX, int physicalY)
    {
        var scale = _dpi / 96d;
        if (physicalY < 0 || physicalY >= Scale(TitleBarHeight, scale) || _hwnd == nint.Zero ||
            !NativeMethods.GetClientRect(_hwnd, out var client))
            return CaptionButton.None;
        var buttonWidth = Scale(InstallerRenderer.CaptionButtonWidth, scale);
        if (physicalX >= client.Right - buttonWidth && physicalX < client.Right)
            return CaptionButton.Close;
        if (physicalX >= client.Right - 2 * buttonWidth && physicalX < client.Right - buttonWidth)
            return CaptionButton.Minimize;
        return CaptionButton.None;
    }

    private void TrackHover(int physicalX, int physicalY)
    {
        if (!_trackingMouse && _hwnd != nint.Zero)
        {
            var track = new NativeMethods.TRACKMOUSEEVENT
            {
                cbSize = (uint)sizeof(NativeMethods.TRACKMOUSEEVENT),
                dwFlags = NativeMethods.TME_LEAVE,
                hwndTrack = _hwnd,
            };
            _trackingMouse = NativeMethods.TrackMouseEvent(ref track);
        }
        SetHover(CaptionButtonAt(physicalX, physicalY));
    }

    private void SetHover(CaptionButton button)
    {
        if (_hoverButton == button)
            return;
        _hoverButton = button;
        Invalidate();
    }

    private bool CloseEnabled => _viewModel.CanCancel || _viewModel.IsFinished;

    private void DrawTitleBar(nint dc, int clientWidth)
        => InstallerRenderer.DrawTitleBar(dc, clientWidth, _dpi / 96d, _bodyFont, _caption, _hoverButton, CloseEnabled);

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
        {
            var top = Scale(InstallerRenderer.LicenseTop + TitleBarHeight, scale) - _scrollY;
            _ = NativeMethods.MoveWindow(_licenseEdit, Scale(28, scale) - _scrollX, top, Scale(544, scale), Scale(InstallerRenderer.LicenseHeight, scale), true);
            ClipToPage(_licenseEdit, top, Scale(InstallerRenderer.LicenseHeight, scale));
        }
        if (showFolder)
        {
            var top = Scale(InstallerRenderer.FolderTop + TitleBarHeight, scale) - _scrollY;
            _ = NativeMethods.MoveWindow(_folderEdit, Scale(28, scale) - _scrollX, top, Scale(444, scale), Scale(InstallerRenderer.FolderHeight, scale), true);
            ClipToPage(_folderEdit, top, Scale(InstallerRenderer.FolderHeight, scale));
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

    private const int MonoFontSize = 12;
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
        _ = NativeMethods.DeleteObject(_headerBrush);
        _ = NativeMethods.DeleteObject(_dialogBrush);
        _ = NativeMethods.DeleteObject(_panelBrush);
        if (_bodyFont != nint.Zero)
            _ = NativeMethods.DeleteObject(_bodyFont);
        if (_monoFont != nint.Zero)
            _ = NativeMethods.DeleteObject(_monoFont);
        if (_selfHandle.IsAllocated)
            _selfHandle.Free();
    }

    internal enum CaptionButton
    {
        None,
        Minimize,
        Close,
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

internal static unsafe class InstallerRenderer
{
    // COLORREF uses BGR. A white header band, a pale body, and a deeper footer keep native themed controls legible.
    internal const uint HeaderColor = 0xFFFFFF;
    internal const uint PanelColor = 0xFCF3ED;
    internal const uint DialogColor = 0xF8E9DF;
    internal const uint LineColor = 0xE6D6C8;
    internal const uint FieldBorderColor = 0xC8B0A0;
    internal const uint TextColor = 0x663B19;
    internal const uint SubtitleColor = 0x856B5A;
    internal const uint DisabledGlyphColor = 0xB8B8B8;
    internal const uint CloseHoverColor = 0x1C2BC4;
    internal const uint MinimizeHoverColor = 0xE9E9E9;
    internal const int TitleBarHeight = 32;
    internal const int CaptionButtonWidth = 46;
    internal const int HeaderHeight = 80;
    internal const int FooterTop = 424;
    internal const int AppIconLeft = 28;
    internal const int AppIconTop = 92;
    internal const int AppIconSize = 80;
    internal const int LicenseTop = 130;
    internal const int LicenseHeight = 246;
    internal const int FolderTop = 105;
    internal const int FolderHeight = 28;
    private const int WordmarkTop = 16;
    private const int WordmarkHeight = 48;
    private const int WordmarkRight = 572;
    private const string InstallerFileName = "ThisIsMyPC-Installer.exe";

    private static readonly object CacheLock = new();
    private static readonly Dictionary<int, nint> Icons = new();
    private static nint _iconModule;
    private static bool _iconModuleResolved;
    private static byte[]? _wordmarkPixels;
    private static bool _wordmarkLoaded;
    private static int _wordmarkWidth;
    private static int _wordmarkHeight;

    /// <summary>Draws the page chrome in page coordinates: the caller's viewport origin sits under the title bar.</summary>
    internal static void Draw(nint dc, int width, int height, double scale, InstallerViewModel viewModel)
    {
        int Px(int value) => (int)Math.Round(value * scale);
        var line = Math.Max(1, Px(1));
        var headerBottom = Px(HeaderHeight);
        var footerTop = Px(FooterTop);
        Fill(dc, new(0, 0, width, headerBottom), HeaderColor);
        Fill(dc, new(0, headerBottom, width, headerBottom + line), LineColor);
        Fill(dc, new(0, headerBottom + line, width, footerTop), PanelColor);
        Fill(dc, new(0, footerTop, width, footerTop + line), LineColor);
        Fill(dc, new(0, footerTop + line, width, height), DialogColor);
        DrawWordmark(dc, Px(WordmarkRight), Px(WordmarkTop), Px(WordmarkHeight));
        if (viewModel.IsWelcome || (viewModel.IsDone && !viewModel.Failed && !viewModel.Removed))
            DrawAppIcon(dc, Px(AppIconLeft), Px(AppIconTop), Px(AppIconSize));
        if (viewModel.IsLicense)
            Outline(dc, new(Px(28), Px(LicenseTop), Px(28 + 544), Px(LicenseTop + LicenseHeight)), line);
        if (viewModel.IsOptions)
            Outline(dc, new(Px(28), Px(FolderTop), Px(28 + 444), Px(FolderTop + FolderHeight)), line);
    }

    /// <summary>Draws the title bar in client coordinates: icon, caption, and the minimize and close buttons.</summary>
    internal static void DrawTitleBar(nint dc, int clientWidth, double scale, nint font, string caption,
        InstallerWindow.CaptionButton hover, bool closeEnabled)
    {
        int Px(int value) => (int)Math.Round(value * scale);
        var height = Px(TitleBarHeight);
        var buttonWidth = Px(CaptionButtonWidth);
        Fill(dc, new(0, 0, clientWidth, height), HeaderColor);
        DrawAppIcon(dc, Px(12), Px(8), Px(16));
        var textBounds = new NativeMethods.RECT(Px(36), 0, Math.Max(Px(36), clientWidth - 2 * buttonWidth - Px(8)), height);
        var previousFont = NativeMethods.SelectObject(dc, font);
        _ = NativeMethods.SetBkMode(dc, NativeMethods.TRANSPARENT);
        _ = NativeMethods.SetTextColor(dc, TextColor);
        _ = NativeMethods.DrawText(dc, caption, -1, ref textBounds,
            NativeMethods.DT_LEFT | NativeMethods.DT_VCENTER | NativeMethods.DT_SINGLELINE | NativeMethods.DT_NOPREFIX | NativeMethods.DT_END_ELLIPSIS);
        _ = NativeMethods.SelectObject(dc, previousFont);

        var close = new NativeMethods.RECT(clientWidth - buttonWidth, 0, clientWidth, height);
        var minimize = new NativeMethods.RECT(clientWidth - 2 * buttonWidth, 0, clientWidth - buttonWidth, height);
        var closeHot = closeEnabled && hover == InstallerWindow.CaptionButton.Close;
        if (closeHot)
            Fill(dc, close, CloseHoverColor);
        if (hover == InstallerWindow.CaptionButton.Minimize)
            Fill(dc, minimize, MinimizeHoverColor);
        var reach = Px(5);
        var pen = NativeMethods.CreatePen(NativeMethods.PS_SOLID, Math.Max(1, Px(1)), closeHot ? HeaderColor : closeEnabled ? TextColor : DisabledGlyphColor);
        var previousPen = NativeMethods.SelectObject(dc, pen);
        try
        {
            var cx = (close.Left + close.Right) / 2;
            var cy = height / 2;
            _ = NativeMethods.MoveToEx(dc, cx - reach, cy - reach, nint.Zero);
            _ = NativeMethods.LineTo(dc, cx + reach + 1, cy + reach + 1);
            _ = NativeMethods.MoveToEx(dc, cx + reach, cy - reach, nint.Zero);
            _ = NativeMethods.LineTo(dc, cx - reach - 1, cy + reach + 1);
            _ = NativeMethods.SelectObject(dc, previousPen);
            _ = NativeMethods.DeleteObject(pen);
            pen = NativeMethods.CreatePen(NativeMethods.PS_SOLID, Math.Max(1, Px(1)), TextColor);
            _ = NativeMethods.SelectObject(dc, pen);
            cx = (minimize.Left + minimize.Right) / 2;
            _ = NativeMethods.MoveToEx(dc, cx - reach, cy, nint.Zero);
            _ = NativeMethods.LineTo(dc, cx + reach + 1, cy);
        }
        finally
        {
            _ = NativeMethods.SelectObject(dc, previousPen);
            _ = NativeMethods.DeleteObject(pen);
        }
    }

    private static void Fill(nint dc, NativeMethods.RECT bounds, uint color)
    {
        var brush = NativeMethods.CreateSolidBrush(color);
        try { _ = NativeMethods.FillRect(dc, in bounds, brush); }
        finally { _ = NativeMethods.DeleteObject(brush); }
    }

    // A one-line frame just outside a field, so the field itself needs no client edge.
    private static void Outline(nint dc, NativeMethods.RECT field, int line)
    {
        Fill(dc, new(field.Left - line, field.Top - line, field.Right + line, field.Top), FieldBorderColor);
        Fill(dc, new(field.Left - line, field.Bottom, field.Right + line, field.Bottom + line), FieldBorderColor);
        Fill(dc, new(field.Left - line, field.Top, field.Left, field.Bottom), FieldBorderColor);
        Fill(dc, new(field.Right, field.Top, field.Right + line, field.Bottom), FieldBorderColor);
    }

    // The wordmark ships as an 8-bit coverage mask, tinted over the header at draw time.
    private static void DrawWordmark(nint dc, int right, int top, int height)
    {
        var pixels = WordmarkPixels();
        if (pixels is null || height <= 0)
            return;
        var width = (int)Math.Round(height * (double)_wordmarkWidth / _wordmarkHeight);
        var info = new NativeMethods.BITMAPINFO
        {
            bmiHeader = new NativeMethods.BITMAPINFOHEADER
            {
                biSize = (uint)sizeof(NativeMethods.BITMAPINFOHEADER),
                biWidth = _wordmarkWidth,
                biHeight = -_wordmarkHeight,
                biPlanes = 1,
                biBitCount = 32,
                biCompression = NativeMethods.BI_RGB,
            },
        };
        _ = NativeMethods.SetStretchBltMode(dc, NativeMethods.HALFTONE);
        _ = NativeMethods.SetBrushOrgEx(dc, 0, 0, nint.Zero);
        fixed (byte* bits = pixels)
            _ = NativeMethods.StretchDIBits(dc, right - width, top, width, height, 0, 0, _wordmarkWidth, _wordmarkHeight,
                bits, in info, NativeMethods.DIB_RGB_COLORS, NativeMethods.SRCCOPY);
    }

    private static byte[]? WordmarkPixels()
    {
        lock (CacheLock)
        {
            if (_wordmarkLoaded)
                return _wordmarkPixels;
            _wordmarkLoaded = true;
            try
            {
                using var stream = typeof(InstallerRenderer).Assembly.GetManifestResourceStream("wordmark.alpha");
                if (stream is null)
                    return null;
                Span<byte> header = stackalloc byte[8];
                stream.ReadExactly(header);
                var width = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(header);
                var height = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(header[4..]);
                if (width is <= 0 or > 4096 || height is <= 0 or > 4096)
                    return null;
                var alpha = new byte[width * height];
                using (var deflate = new System.IO.Compression.DeflateStream(stream, System.IO.Compression.CompressionMode.Decompress))
                    deflate.ReadExactly(alpha);
                var pixels = new byte[alpha.Length * 4];
                for (var index = 0; index < alpha.Length; index++)
                {
                    var coverage = alpha[index];
                    pixels[index * 4] = Blend(HeaderColor, TextColor, 16, coverage);
                    pixels[index * 4 + 1] = Blend(HeaderColor, TextColor, 8, coverage);
                    pixels[index * 4 + 2] = Blend(HeaderColor, TextColor, 0, coverage);
                    pixels[index * 4 + 3] = byte.MaxValue;
                }
                _wordmarkWidth = width;
                _wordmarkHeight = height;
                _wordmarkPixels = pixels;
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException)
            {
                // A damaged resource costs the wordmark, never the installer.
            }
            return _wordmarkPixels;
        }
    }

    private static byte Blend(uint background, uint tint, int shift, byte coverage)
    {
        var from = (int)((background >> shift) & 0xFF);
        var to = (int)((tint >> shift) & 0xFF);
        return (byte)(from + (to - from) * coverage / 255);
    }

    private static void DrawAppIcon(nint dc, int left, int top, int size)
    {
        var icon = AppIcon(size);
        if (icon != nint.Zero)
            _ = NativeMethods.DrawIconEx(dc, left, top, icon, size, size, 0, nint.Zero, NativeMethods.DI_NORMAL);
    }

    private static nint AppIcon(int size)
    {
        lock (CacheLock)
        {
            if (Icons.TryGetValue(size, out var cached))
                return cached;
            var module = IconModule();
            var icon = nint.Zero;
            if (module != nint.Zero)
            {
                // The smooth scaler lives in comctl32 v6 only; a host without that activation context gets the plain loader.
                try
                {
                    if (NativeMethods.LoadIconWithScaleDown(module, NativeMethods.IDI_APPLICATION, size, size, out icon) < 0)
                        icon = nint.Zero;
                }
                catch (Exception exception) when (exception is EntryPointNotFoundException or DllNotFoundException)
                {
                    icon = nint.Zero;
                }
                if (icon == nint.Zero)
                    icon = NativeMethods.LoadImage(module, NativeMethods.IDI_APPLICATION, NativeMethods.IMAGE_ICON, size, size, 0);
            }
            Icons[size] = icon;
            return icon;
        }
    }

    // The icon is this executable's own resource. Test and preview hosts run the IL assembly under
    // another process, so they read the resource from the assembly file instead of the host module.
    private static nint IconModule()
    {
        if (_iconModuleResolved)
            return _iconModule;
        _iconModuleResolved = true;
        var process = Environment.ProcessPath;
        if (process is not null && string.Equals(Path.GetFileName(process), InstallerFileName, StringComparison.OrdinalIgnoreCase))
            _iconModule = NativeMethods.GetModuleHandle(null);
        else
        {
            var assembly = Path.Combine(AppContext.BaseDirectory, InstallerFileName);
            if (File.Exists(assembly))
                _iconModule = NativeMethods.LoadLibraryEx(assembly, nint.Zero,
                    NativeMethods.LOAD_LIBRARY_AS_DATAFILE | NativeMethods.LOAD_LIBRARY_AS_IMAGE_RESOURCE);
        }
        return _iconModule;
    }
}
