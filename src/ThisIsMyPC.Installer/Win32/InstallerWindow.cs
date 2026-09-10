using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using ThisIsMyPC.Installer.Services;
using ThisIsMyPC.Installer.ViewModels;

namespace ThisIsMyPC.Installer.Win32;

internal sealed unsafe class InstallerWindow : IDisposable
{
    private const string WindowClassName = "ThisIsMyPC.NativeInstaller";
    private const int LogicalWidth = 720;
    private const int LogicalHeight = 640;
    private const int LicenseEditId = 1001;
    private const int FolderEditId = 1002;
    private const nuint ProgressTimerId = 1;
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
    private nint _surfaceBrush;
    private nint _bodyFont;
    private nint _monoFont;
    private int _dpi = 96;
    private bool _updatingFolder;
    private bool _disposed;
    private HitTarget _hover;
    private int _progressFrame;

    internal InstallerWindow(InstallerViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        _viewModel = viewModel;
    }

    internal int Run()
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

            var width = bounds.Right - bounds.Left;
            var height = bounds.Bottom - bounds.Top;
            var x = Math.Max(0, (NativeMethods.GetSystemMetrics(0) - width) / 2);
            var y = Math.Max(0, (NativeMethods.GetSystemMetrics(1) - height) / 2);
            _selfHandle = GCHandle.Alloc(this);
            _hwnd = NativeMethods.CreateWindowEx(
                0,
                WindowClassName,
                "Install ThisIsMyPC",
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
                    message.wParam is NativeMethods.VK_RETURN or NativeMethods.VK_ESCAPE)
                {
                    HandleKey(unchecked((int)message.wParam));
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

    internal static byte[] RenderPreviewBgra(InstallerViewModel viewModel, int width = LogicalWidth, int height = LogicalHeight)
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
            InstallerRenderer.Draw(dc, viewModel, width, height, width / (double)LogicalWidth, HitTarget.None, 0, drawEditors: true);
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
            case NativeMethods.WM_PAINT:
                Paint(hwnd);
                return nint.Zero;
            case NativeMethods.WM_COMMAND:
                HandleCommand(wParam, lParam);
                return nint.Zero;
            case NativeMethods.WM_LBUTTONUP:
                HandleClick(SignedLowWord(lParam), SignedHighWord(lParam));
                return nint.Zero;
            case NativeMethods.WM_MOUSEMOVE:
                HandleMouseMove(SignedLowWord(lParam), SignedHighWord(lParam));
                return nint.Zero;
            case NativeMethods.WM_MOUSELEAVE:
                _hover = HitTarget.None;
                Invalidate();
                return nint.Zero;
            case NativeMethods.WM_DPICHANGED:
                ApplyDpiChange(wParam, lParam);
                return nint.Zero;
            case NativeMethods.WM_KEYDOWN:
                HandleKey(unchecked((int)wParam));
                return nint.Zero;
            case NativeMethods.WM_TIMER:
                if (wParam == ProgressTimerId)
                {
                    _progressFrame = (_progressFrame + 1) % 24;
                    Invalidate();
                }
                return nint.Zero;
            case NativeMethods.WM_CTLCOLOREDIT:
            case NativeMethods.WM_CTLCOLORSTATIC:
                if (lParam == _licenseEdit || lParam == _folderEdit)
                {
                    _ = NativeMethods.SetTextColor((nint)wParam, InstallerRenderer.TextColor);
                    _ = NativeMethods.SetBkColor((nint)wParam, InstallerRenderer.FieldColor);
                    return _surfaceBrush;
                }
                break;
            case NativeMethods.WM_APP_CALLBACK:
                _context.Drain();
                return nint.Zero;
            case NativeMethods.WM_CLOSE:
                if (_viewModel.CanCancel || _viewModel.IsFinished)
                    _ = NativeMethods.DestroyWindow(hwnd);
                return nint.Zero;
            case NativeMethods.WM_DESTROY:
                _ = NativeMethods.KillTimer(hwnd, ProgressTimerId);
                _hwnd = nint.Zero;
                NativeMethods.PostQuitMessage(0);
                return nint.Zero;
        }

        return NativeMethods.DefWindowProc(hwnd, message, wParam, lParam);
    }

    private void CreateChildControls(nint hwnd)
    {
        _dpi = checked((int)NativeMethods.GetDpiForWindow(hwnd));
        var scale = _dpi / 96d;
        _surfaceBrush = NativeMethods.CreateSolidBrush(InstallerRenderer.FieldColor);
        _bodyFont = NativeMethods.CreateFont(-Scale(14, scale), 0, 0, 0, NativeMethods.FW_NORMAL, 0, 0, 0,
            NativeMethods.DEFAULT_CHARSET, 0, 0, NativeMethods.CLEARTYPE_QUALITY, 0, "Segoe UI");
        _monoFont = NativeMethods.CreateFont(-Scale(13, scale), 0, 0, 0, NativeMethods.FW_NORMAL, 0, 0, 0,
            NativeMethods.DEFAULT_CHARSET, 0, 0, NativeMethods.CLEARTYPE_QUALITY, 0, "Consolas");
        var instance = NativeMethods.GetModuleHandle(null);
        _licenseEdit = NativeMethods.CreateWindowEx(
            NativeMethods.WS_EX_CLIENTEDGE,
            "EDIT",
            _viewModel.LicenseText,
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
        _ = NativeMethods.SetWindowTheme(_licenseEdit, "DarkMode_Explorer", null);
        _ = NativeMethods.SetWindowTheme(_folderEdit, "DarkMode_Explorer", null);
        const uint darkModeAttribute = 20;
        var enabled = 1;
        _ = NativeMethods.DwmSetWindowAttribute(hwnd, darkModeAttribute, in enabled, sizeof(int));
    }

    private void Paint(nint hwnd)
    {
        var dc = NativeMethods.BeginPaint(hwnd, out var paint);
        try
        {
            _ = NativeMethods.GetClientRect(hwnd, out var bounds);
            var width = bounds.Right - bounds.Left;
            var height = bounds.Bottom - bounds.Top;
            InstallerRenderer.Draw(dc, _viewModel, width, height, _dpi / 96d, _hover, _progressFrame, drawEditors: false);
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
        _ = NativeMethods.SetWindowPos(
            _hwnd,
            nint.Zero,
            suggested->Left,
            suggested->Top,
            suggested->Right - suggested->Left,
            suggested->Bottom - suggested->Top,
            NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
        ReplaceControlFonts();
        Refresh();
    }

    private void ReplaceControlFonts()
    {
        var scale = _dpi / 96d;
        var oldBodyFont = _bodyFont;
        var oldMonoFont = _monoFont;
        var bodyFont = NativeMethods.CreateFont(-Scale(14, scale), 0, 0, 0, NativeMethods.FW_NORMAL, 0, 0, 0,
            NativeMethods.DEFAULT_CHARSET, 0, 0, NativeMethods.CLEARTYPE_QUALITY, 0, "Segoe UI");
        var monoFont = NativeMethods.CreateFont(-Scale(13, scale), 0, 0, 0, NativeMethods.FW_NORMAL, 0, 0, 0,
            NativeMethods.DEFAULT_CHARSET, 0, 0, NativeMethods.CLEARTYPE_QUALITY, 0, "Consolas");
        if (bodyFont == nint.Zero || monoFont == nint.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows could not create the installer fonts.");
        _bodyFont = bodyFont;
        _monoFont = monoFont;
        _ = NativeMethods.SendMessage(_licenseEdit, NativeMethods.WM_SETFONT, (nuint)_monoFont, (nint)1);
        _ = NativeMethods.SendMessage(_folderEdit, NativeMethods.WM_SETFONT, (nuint)_bodyFont, (nint)1);
        if (oldBodyFont != nint.Zero)
            _ = NativeMethods.DeleteObject(oldBodyFont);
        if (oldMonoFont != nint.Zero)
            _ = NativeMethods.DeleteObject(oldMonoFont);
    }

    private void HandleCommand(nuint wParam, nint lParam)
    {
        var id = unchecked((int)(wParam & 0xffff));
        var notification = unchecked((uint)((wParam >> 16) & 0xffff));
        if (id == FolderEditId && notification == NativeMethods.EN_CHANGE && lParam == _folderEdit && !_updatingFolder)
            _viewModel.InstallFolder = ReadWindowText(_folderEdit);
    }

    private void HandleClick(int physicalX, int physicalY)
        => HandleLogicalClick(Logical(physicalX), Logical(physicalY));

    internal void HandleLogicalClick(int x, int y)
    {
        var target = HitTest(x, y);
        switch (target)
        {
            case HitTarget.WelcomeTab:
                _viewModel.TabIndex = InstallerViewModel.WelcomeTab;
                break;
            case HitTarget.LicenseTab:
                _viewModel.TabIndex = InstallerViewModel.LicenseTab;
                break;
            case HitTarget.OptionsTab:
                _viewModel.TabIndex = InstallerViewModel.OptionsTab;
                break;
            case HitTarget.RemoveTab:
                _viewModel.TabIndex = InstallerViewModel.RemoveTab;
                break;
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

    private void HandleMouseMove(int physicalX, int physicalY)
    {
        var target = HitTest(Logical(physicalX), Logical(physicalY));
        if (target == _hover)
            return;
        _hover = target;
        var track = new NativeMethods.TRACKMOUSEEVENT
        {
            cbSize = (uint)sizeof(NativeMethods.TRACKMOUSEEVENT),
            dwFlags = NativeMethods.TME_LEAVE,
            hwndTrack = _hwnd,
        };
        _ = NativeMethods.TrackMouseEvent(ref track);
        Invalidate();
    }

    private void HandleKey(int key)
    {
        if (key == NativeMethods.VK_RETURN && _viewModel.PrimaryCommand.CanExecute(null))
        {
            SyncFolderText();
            _viewModel.PrimaryCommand.Execute(null);
        }
        else if (key == NativeMethods.VK_ESCAPE && _viewModel.CancelCommand.CanExecute(null))
        {
            _viewModel.CancelCommand.Execute(null);
        }
    }

    private HitTarget HitTest(int x, int y)
    {
        if (_viewModel.ShowInstallTabs)
        {
            if (UiRect.FromEdges(38, 85, 177, 114).Contains(x, y) && _viewModel.CanOpenWelcome)
                return HitTarget.WelcomeTab;
            if (UiRect.FromEdges(207, 85, 334, 114).Contains(x, y) && _viewModel.CanOpenLicense)
                return HitTarget.LicenseTab;
            if (UiRect.FromEdges(363, 85, 491, 114).Contains(x, y) && _viewModel.CanOpenOptions)
                return HitTarget.OptionsTab;
        }
        else
        {
            if (UiRect.FromEdges(38, 85, 328, 114).Contains(x, y) && _viewModel.CanOpenWelcome)
                return HitTarget.WelcomeTab;
            if (UiRect.FromEdges(359, 85, 641, 114).Contains(x, y) && _viewModel.IsInRemoveTab)
                return HitTarget.RemoveTab;
        }

        if (_viewModel.IsWelcome && _viewModel.IsInstalled && UiRect.FromEdges(486, 265, 663, 306).Contains(x, y))
            return HitTarget.Uninstall;
        if (_viewModel.IsLicense && UiRect.FromEdges(56, 514, 650, 543).Contains(x, y))
            return HitTarget.LicenseAccepted;
        if (_viewModel.IsOptions)
        {
            var shortcutsY = OptionsShortcutsY(_viewModel);
            var behaviorY = shortcutsY + 98;
            if (UiRect.FromEdges(585, 177, 664, 213).Contains(x, y) && _viewModel.CanChooseFolder)
                return HitTarget.Browse;
            if (UiRect.FromEdges(56, shortcutsY + 19, 450, shortcutsY + 47).Contains(x, y))
                return HitTarget.StartMenu;
            if (UiRect.FromEdges(56, shortcutsY + 48, 450, shortcutsY + 76).Contains(x, y))
                return HitTarget.Desktop;
            if (UiRect.FromEdges(56, behaviorY + 19, 450, behaviorY + 47).Contains(x, y))
                return HitTarget.StartWithWindows;
            if (UiRect.FromEdges(56, behaviorY + 48, 450, behaviorY + 76).Contains(x, y))
                return HitTarget.CheckForUpdates;
        }
        var launchY = _viewModel.RebootRequired ? 232 : 203;
        if (_viewModel.IsDone && _viewModel.DoneInstalled && UiRect.FromEdges(56, launchY - 5, 450, launchY + 24).Contains(x, y))
            return HitTarget.Launch;
        if (_viewModel.CanGoBack && UiRect.FromEdges(358, 583, 454, 621).Contains(x, y))
            return HitTarget.Back;
        var primaryLeft = !_viewModel.CanGoBack && !_viewModel.CanCancel ? 592 : 464;
        if (UiRect.FromEdges(primaryLeft, 583, primaryLeft + 96, 621).Contains(x, y) && _viewModel.CanGoPrimary)
            return HitTarget.Primary;
        if (_viewModel.CanCancel && UiRect.FromEdges(592, 583, 688, 621).Contains(x, y))
            return HitTarget.Cancel;
        return HitTarget.None;
    }

    private void Refresh()
    {
        if (_hwnd == nint.Zero)
            return;
        UpdateChildControls();
        if (_viewModel.IsBusy)
            _ = NativeMethods.SetTimer(_hwnd, ProgressTimerId, 80, nint.Zero);
        else
            _ = NativeMethods.KillTimer(_hwnd, ProgressTimerId);
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
            _ = NativeMethods.MoveWindow(_licenseEdit, Scale(57, scale), Scale(200, scale), Scale(606, scale), Scale(308, scale), true);
        if (showFolder)
        {
            _ = NativeMethods.MoveWindow(_folderEdit, Scale(57, scale), Scale(178, scale), Scale(521, scale), Scale(33, scale), true);
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
        var detailY = 218;
        if (!viewModel.CanChooseFolder)
            detailY += 24;
        if (viewModel.HasFolderError || viewModel.HasFolderWarning)
            detailY += 28;
        return Math.Max(238, detailY + 10);
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
        if (_bodyFont != nint.Zero)
            _ = NativeMethods.DeleteObject(_bodyFont);
        if (_monoFont != nint.Zero)
            _ = NativeMethods.DeleteObject(_monoFont);
        if (_surfaceBrush != nint.Zero)
            _ = NativeMethods.DeleteObject(_surfaceBrush);
        if (_selfHandle.IsAllocated)
            _selfHandle.Free();
    }

    internal enum HitTarget
    {
        None,
        WelcomeTab,
        LicenseTab,
        OptionsTab,
        RemoveTab,
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
    internal static readonly uint BackgroundColor = Rgb(23, 23, 43);
    internal static readonly uint CardColor = Rgb(34, 34, 56);
    internal static readonly uint FieldColor = Rgb(45, 45, 71);
    internal static readonly uint OutlineColor = Rgb(66, 66, 96);
    internal static readonly uint TextColor = Rgb(255, 255, 255);
    internal static readonly uint SecondaryTextColor = Rgb(218, 218, 232);
    internal static readonly uint MutedTextColor = Rgb(166, 166, 194);
    internal static readonly uint AccentColor = Rgb(91, 145, 220);
    internal static readonly uint AccentHoverColor = Rgb(74, 128, 203);
    internal static readonly uint DangerColor = Rgb(255, 81, 85);
    internal static readonly uint WarningColor = Rgb(246, 184, 83);

    internal static void Draw(nint dc, InstallerViewModel viewModel, int width, int height, double scale,
        InstallerWindow.HitTarget hover, int progressFrame, bool drawEditors)
    {
        using var objects = new GdiObjects(scale);
        Fill(dc, new NativeMethods.RECT(0, 0, width, height), objects.BackgroundBrush);
        _ = NativeMethods.SetBkMode(dc, NativeMethods.TRANSPARENT);

        Text(dc, objects.TitleFont, TextColor, "ThisIsMyPC", Rect(32, 18, 300, 50, scale), NativeMethods.DT_LEFT | NativeMethods.DT_SINGLELINE);
        Dot(dc, objects, 75, 29, Rgb(255, 165, 67), scale);
        Dot(dc, objects, 116, 29, DangerColor, scale);
        Text(dc, objects.CaptionFont, MutedTextColor, $"Installer Version {InstallerViewModel.AppVersion}",
            Rect(32, 53, 320, 72, scale), NativeMethods.DT_LEFT | NativeMethods.DT_SINGLELINE);

        DrawTabs(dc, viewModel, objects, scale);
        Rounded(dc, objects.CardBrush, objects.OutlinePen, Rect(32, 126, 688, 563, scale), 7, scale);

        if (viewModel.IsWelcome)
            DrawWelcome(dc, viewModel, objects, scale);
        else if (viewModel.IsLicense)
            DrawLicense(dc, viewModel, objects, scale, drawEditors);
        else if (viewModel.IsOptions)
            DrawOptions(dc, viewModel, objects, scale, hover, drawEditors);
        else if (viewModel.IsInstalling || viewModel.IsUninstalling)
            DrawBusy(dc, viewModel, objects, scale, progressFrame);
        else if (viewModel.IsConfirmUninstall)
            DrawRemove(dc, objects, scale);
        else if (viewModel.IsDone)
            DrawDone(dc, viewModel, objects, scale);

        DrawFooter(dc, viewModel, objects, scale, hover);
    }

    private static void DrawTabs(nint dc, InstallerViewModel viewModel, GdiObjects objects, double scale)
    {
        Rounded(dc, objects.BackgroundBrush, objects.OutlinePen, Rect(32, 78, 672, 120, scale), 7, scale);
        if (viewModel.ShowInstallTabs)
        {
            DrawTab(dc, objects, "Welcome", Rect(38, 85, 177, 114, scale), viewModel.TabIndex == InstallerViewModel.WelcomeTab, viewModel.CanOpenWelcome);
            DrawTab(dc, objects, "License", Rect(207, 85, 334, 114, scale), viewModel.TabIndex == InstallerViewModel.LicenseTab, viewModel.CanOpenLicense);
            DrawTab(dc, objects, "Options", Rect(363, 85, 491, 114, scale), viewModel.TabIndex == InstallerViewModel.OptionsTab, viewModel.CanOpenOptions);
            DrawTab(dc, objects, "Install", Rect(521, 85, 641, 114, scale), viewModel.TabIndex == InstallerViewModel.InstallTab, viewModel.IsInInstallTab);
        }
        else
        {
            DrawTab(dc, objects, "Welcome", Rect(38, 85, 328, 114, scale), viewModel.TabIndex == InstallerViewModel.WelcomeTab, viewModel.CanOpenWelcome);
            DrawTab(dc, objects, "Remove", Rect(359, 85, 641, 114, scale), viewModel.TabIndex == InstallerViewModel.RemoveTab, viewModel.IsInRemoveTab);
        }
    }

    private static void DrawTab(nint dc, GdiObjects objects, string label, NativeMethods.RECT bounds, bool selected, bool enabled)
    {
        Rounded(dc, selected ? objects.CardBrush : objects.BackgroundBrush, objects.OutlinePen, bounds, 5, objects.Scale);
        Text(dc, objects.BodyFont, enabled || selected ? TextColor : MutedTextColor, label, bounds,
            NativeMethods.DT_CENTER | NativeMethods.DT_VCENTER | NativeMethods.DT_SINGLELINE | NativeMethods.DT_NOPREFIX);
    }

    private static void DrawWelcome(nint dc, InstallerViewModel viewModel, GdiObjects objects, double scale)
    {
        Text(dc, objects.LeadBoldFont, TextColor, "Welcome to the installer for ThisIsMyPC.",
            Rect(57, 155, 650, 183, scale), NativeMethods.DT_LEFT | NativeMethods.DT_SINGLELINE);
        Text(dc, objects.LeadFont, SecondaryTextColor,
            "This program will be installed for everybody who uses this PC. This installer has administrator permissions.",
            Rect(57, 195, 650, 240, scale), NativeMethods.DT_LEFT | NativeMethods.DT_WORDBREAK);
        if (viewModel.IsInstalled)
        {
            Rounded(dc, objects.FieldBrush, objects.OutlinePen, Rect(57, 253, 663, 316, scale), 6, scale);
            Text(dc, objects.BodyFont, SecondaryTextColor, viewModel.InstalledSummary,
                Rect(72, 269, 476, 304, scale), NativeMethods.DT_LEFT | NativeMethods.DT_WORDBREAK);
            CheckBox(dc, objects, Rect(488, 276, 506, 294, scale), viewModel.UninstallMode, "Uninstall ThisIsMyPC",
                Rect(514, 268, 650, 303, scale));
        }
        if (viewModel.ShowWelcomeHint)
            Text(dc, objects.LeadFont, TextColor, viewModel.WelcomeHint,
                Rect(390, 516, 663, 540, scale), NativeMethods.DT_RIGHT | NativeMethods.DT_SINGLELINE);
    }

    private static void DrawLicense(nint dc, InstallerViewModel viewModel, GdiObjects objects, double scale, bool drawEditor)
    {
        Text(dc, objects.BodyFont, SecondaryTextColor,
            "ThisIsMyPC is free software under the GNU General Public License, version 3. You may use it, share it, and change it under these terms.",
            Rect(57, 153, 663, 190, scale), NativeMethods.DT_LEFT | NativeMethods.DT_WORDBREAK);
        if (drawEditor)
        {
            Rounded(dc, objects.FieldBrush, objects.OutlinePen, Rect(57, 200, 663, 508, scale), 4, scale);
            Text(dc, objects.MonoFont, TextColor, viewModel.LicenseText,
                Rect(68, 212, 651, 497, scale), NativeMethods.DT_LEFT | NativeMethods.DT_WORDBREAK | NativeMethods.DT_NOPREFIX);
        }
        CheckBox(dc, objects, Rect(57, 519, 75, 537, scale), viewModel.LicenseAccepted,
            "I accept the terms of the GNU General Public License, version 3", Rect(83, 514, 650, 542, scale));
    }

    private static void DrawOptions(nint dc, InstallerViewModel viewModel, GdiObjects objects, double scale,
        InstallerWindow.HitTarget hover, bool drawEditor)
    {
        Text(dc, objects.LabelFont, TextColor, "Install folder", Rect(57, 151, 300, 174, scale), NativeMethods.DT_LEFT | NativeMethods.DT_SINGLELINE);
        if (drawEditor)
        {
            Rounded(dc, objects.FieldBrush, objects.OutlinePen, Rect(57, 178, 578, 211, scale), 4, scale);
            Text(dc, objects.BodyFont, TextColor, viewModel.InstallFolder, Rect(68, 178, 567, 211, scale),
                NativeMethods.DT_LEFT | NativeMethods.DT_VCENTER | NativeMethods.DT_SINGLELINE | NativeMethods.DT_END_ELLIPSIS);
        }
        DrawButton(dc, objects, "Browse...", Rect(586, 178, 663, 211, scale), true,
            hover == InstallerWindow.HitTarget.Browse, ButtonKind.Neutral);

        var detailY = 218;
        if (!viewModel.CanChooseFolder)
        {
            Text(dc, objects.CaptionFont, MutedTextColor, "Updates go into the folder the app is already in.",
                Rect(57, detailY, 663, detailY + 20, scale), NativeMethods.DT_LEFT | NativeMethods.DT_SINGLELINE);
            detailY += 24;
        }
        if (viewModel.HasFolderError)
        {
            Text(dc, objects.CaptionFont, DangerColor, viewModel.FolderError!, Rect(57, detailY, 663, detailY + 35, scale),
                NativeMethods.DT_LEFT | NativeMethods.DT_WORDBREAK);
            detailY += 28;
        }
        else if (viewModel.HasFolderWarning)
        {
            Text(dc, objects.CaptionFont, WarningColor, viewModel.FolderWarning!, Rect(57, detailY, 663, detailY + 35, scale),
                NativeMethods.DT_LEFT | NativeMethods.DT_WORDBREAK);
            detailY += 28;
        }

        var shortcutsY = Math.Max(238, detailY + 10);
        Text(dc, objects.LabelFont, TextColor, "Shortcuts", Rect(57, shortcutsY, 300, shortcutsY + 22, scale), NativeMethods.DT_LEFT | NativeMethods.DT_SINGLELINE);
        CheckBox(dc, objects, Rect(57, shortcutsY + 24, 75, shortcutsY + 42, scale), viewModel.StartMenuShortcut,
            "Add ThisIsMyPC to the Start menu", Rect(83, shortcutsY + 19, 500, shortcutsY + 47, scale));
        CheckBox(dc, objects, Rect(57, shortcutsY + 53, 75, shortcutsY + 71, scale), viewModel.DesktopShortcut,
            "Add a shortcut on the Desktop", Rect(83, shortcutsY + 48, 500, shortcutsY + 76, scale));

        var behaviorY = shortcutsY + 98;
        Text(dc, objects.LabelFont, TextColor, "Behavior", Rect(57, behaviorY, 300, behaviorY + 22, scale), NativeMethods.DT_LEFT | NativeMethods.DT_SINGLELINE);
        CheckBox(dc, objects, Rect(57, behaviorY + 24, 75, behaviorY + 42, scale), viewModel.StartWithWindows,
            "Start with Windows, in the tray", Rect(83, behaviorY + 19, 500, behaviorY + 47, scale));
        CheckBox(dc, objects, Rect(57, behaviorY + 53, 75, behaviorY + 71, scale), viewModel.CheckForUpdates,
            "Check for updates automatically", Rect(83, behaviorY + 48, 500, behaviorY + 76, scale));
        Text(dc, objects.CaptionFont, MutedTextColor, "Both can be changed later in Settings inside the app.",
            Rect(85, behaviorY + 78, 600, behaviorY + 100, scale), NativeMethods.DT_LEFT | NativeMethods.DT_SINGLELINE);
    }

    private static void DrawBusy(nint dc, InstallerViewModel viewModel, GdiObjects objects, double scale, int progressFrame)
    {
        Text(dc, objects.BodyFont, SecondaryTextColor, viewModel.StatusText,
            Rect(180, 303, 540, 328, scale), NativeMethods.DT_CENTER | NativeMethods.DT_SINGLELINE);
        Rounded(dc, objects.FieldBrush, objects.OutlinePen, Rect(180, 338, 540, 344, scale), 3, scale);
        var start = 180 + (progressFrame * 12) % 310;
        Fill(dc, Rect(start, 338, Math.Min(start + 50, 540), 344, scale), objects.AccentBrush);
        if (viewModel.IsInstalling)
            Text(dc, objects.CaptionFont, MutedTextColor, "This takes under a minute.", Rect(180, 360, 540, 382, scale), NativeMethods.DT_CENTER | NativeMethods.DT_SINGLELINE);
    }

    private static void DrawRemove(nint dc, GdiObjects objects, double scale)
    {
        Text(dc, objects.BodyFont, SecondaryTextColor,
            "Remove ThisIsMyPC from this PC? The app, its shortcuts, and its entry in Installed apps go away.",
            Rect(57, 153, 663, 184, scale), NativeMethods.DT_LEFT | NativeMethods.DT_WORDBREAK);
        Text(dc, objects.BodyFont, SecondaryTextColor,
            "Your settings and change history stay in the ProgramData folder, so a later install picks them up. Changes you applied to Windows stay as they are; undo them in the app first if you want them reverted.",
            Rect(57, 187, 663, 239, scale), NativeMethods.DT_LEFT | NativeMethods.DT_WORDBREAK);
        Text(dc, objects.CaptionFont, MutedTextColor, "Click Remove to continue, or Back to keep it.",
            Rect(57, 257, 663, 280, scale), NativeMethods.DT_LEFT | NativeMethods.DT_SINGLELINE);
    }

    private static void DrawDone(nint dc, InstallerViewModel viewModel, GdiObjects objects, double scale)
    {
        if (viewModel.Failed)
        {
            Text(dc, objects.BodyFont, DangerColor, viewModel.ErrorText!, Rect(57, 153, 663, 235, scale), NativeMethods.DT_LEFT | NativeMethods.DT_WORDBREAK);
            if (viewModel.HasLogPath)
                Text(dc, objects.CaptionFont, MutedTextColor, "Details were saved to " + viewModel.LogPath,
                    Rect(57, 250, 663, 300, scale), NativeMethods.DT_LEFT | NativeMethods.DT_WORDBREAK);
            return;
        }
        if (viewModel.Removed)
        {
            Text(dc, objects.BodyFont, SecondaryTextColor,
                "ThisIsMyPC was removed. Your settings and change history are still in the ProgramData folder in case you install it again.",
                Rect(57, 153, 663, 205, scale), NativeMethods.DT_LEFT | NativeMethods.DT_WORDBREAK);
            return;
        }

        Text(dc, objects.BodyFont, SecondaryTextColor,
            "ThisIsMyPC is installed. Windows asks for permission only when a change needs administrator access.",
            Rect(57, 153, 663, 191, scale), NativeMethods.DT_LEFT | NativeMethods.DT_WORDBREAK);
        if (viewModel.RebootRequired)
            Text(dc, objects.CaptionFont, WarningColor, "Windows asked for a restart to finish. Restart when convenient.",
                Rect(57, 190, 663, 220, scale), NativeMethods.DT_LEFT | NativeMethods.DT_WORDBREAK);
        var launchY = viewModel.RebootRequired ? 232 : 203;
        CheckBox(dc, objects, Rect(57, launchY, 75, launchY + 18, scale), viewModel.LaunchWhenDone,
            "Open ThisIsMyPC when I click Finish", Rect(83, launchY - 5, 500, launchY + 23, scale));
    }

    private static void DrawFooter(nint dc, InstallerViewModel viewModel, GdiObjects objects, double scale, InstallerWindow.HitTarget hover)
    {
        if (viewModel.CanGoBack)
            DrawButton(dc, objects, "< Back", Rect(358, 583, 454, 621, scale), true,
                hover == InstallerWindow.HitTarget.Back, ButtonKind.Neutral);
        var primaryLeft = !viewModel.CanGoBack && !viewModel.CanCancel ? 592 : 464;
        DrawButton(dc, objects, viewModel.PrimaryButtonText.Trim(), Rect(primaryLeft, 583, primaryLeft + 96, 621, scale), viewModel.CanGoPrimary,
            hover == InstallerWindow.HitTarget.Primary, ButtonKind.Primary);
        if (viewModel.CanCancel)
            DrawButton(dc, objects, "Cancel", Rect(592, 583, 688, 621, scale), true,
                hover == InstallerWindow.HitTarget.Cancel, ButtonKind.Danger);
    }

    private static void DrawButton(nint dc, GdiObjects objects, string text, NativeMethods.RECT bounds, bool enabled, bool hover, ButtonKind kind)
    {
        var brush = kind switch
        {
            ButtonKind.Primary when enabled && hover => objects.AccentHoverBrush,
            ButtonKind.Primary when enabled => objects.AccentBrush,
            ButtonKind.Danger when hover => objects.DangerHoverBrush,
            ButtonKind.Danger => objects.DangerBrush,
            ButtonKind.Neutral when hover => objects.OutlineBrush,
            _ => objects.FieldBrush,
        };
        Rounded(dc, brush, objects.OutlinePen, bounds, 5, objects.Scale);
        Text(dc, objects.LabelFont, enabled ? TextColor : MutedTextColor, text, bounds,
            NativeMethods.DT_CENTER | NativeMethods.DT_VCENTER | NativeMethods.DT_SINGLELINE | NativeMethods.DT_NOPREFIX);
    }

    private static void CheckBox(nint dc, GdiObjects objects, NativeMethods.RECT box, bool isChecked, string label, NativeMethods.RECT labelBounds)
    {
        Rounded(dc, isChecked ? objects.AccentBrush : objects.FieldBrush, objects.OutlinePen, box, 4, objects.Scale);
        if (isChecked)
        {
            var oldPen = NativeMethods.SelectObject(dc, objects.WhitePen);
            _ = NativeMethods.MoveToEx(dc, box.Left + Scale(4, objects.Scale), box.Top + Scale(9, objects.Scale), nint.Zero);
            _ = NativeMethods.LineTo(dc, box.Left + Scale(8, objects.Scale), box.Top + Scale(13, objects.Scale));
            _ = NativeMethods.LineTo(dc, box.Left + Scale(15, objects.Scale), box.Top + Scale(5, objects.Scale));
            _ = NativeMethods.SelectObject(dc, oldPen);
        }
        Text(dc, objects.BodyFont, TextColor, label, labelBounds,
            NativeMethods.DT_LEFT | NativeMethods.DT_VCENTER | NativeMethods.DT_SINGLELINE | NativeMethods.DT_NOPREFIX);
    }

    private static void Dot(nint dc, GdiObjects objects, int x, int y, uint color, double scale)
    {
        var brush = NativeMethods.CreateSolidBrush(color);
        var oldBrush = NativeMethods.SelectObject(dc, brush);
        var oldPen = NativeMethods.SelectObject(dc, objects.NoOutlinePen);
        _ = NativeMethods.Ellipse(dc, Scale(x, scale), Scale(y, scale), Scale(x + 4, scale), Scale(y + 4, scale));
        _ = NativeMethods.SelectObject(dc, oldPen);
        _ = NativeMethods.SelectObject(dc, oldBrush);
        _ = NativeMethods.DeleteObject(brush);
    }

    private static void Rounded(nint dc, nint brush, nint pen, NativeMethods.RECT bounds, int radius, double scale)
    {
        var oldBrush = NativeMethods.SelectObject(dc, brush);
        var oldPen = NativeMethods.SelectObject(dc, pen);
        _ = NativeMethods.RoundRect(dc, bounds.Left, bounds.Top, bounds.Right, bounds.Bottom, Scale(radius * 2, scale), Scale(radius * 2, scale));
        _ = NativeMethods.SelectObject(dc, oldPen);
        _ = NativeMethods.SelectObject(dc, oldBrush);
    }

    private static void Fill(nint dc, NativeMethods.RECT bounds, nint brush) => _ = NativeMethods.FillRect(dc, in bounds, brush);

    private static void Text(nint dc, nint font, uint color, string text, NativeMethods.RECT bounds, uint format)
    {
        var oldFont = NativeMethods.SelectObject(dc, font);
        _ = NativeMethods.SetTextColor(dc, color);
        _ = NativeMethods.DrawText(dc, text, text.Length, ref bounds, format | NativeMethods.DT_NOPREFIX);
        _ = NativeMethods.SelectObject(dc, oldFont);
    }

    private static NativeMethods.RECT Rect(int left, int top, int right, int bottom, double scale)
        => new(Scale(left, scale), Scale(top, scale), Scale(right, scale), Scale(bottom, scale));

    private static int Scale(int logical, double scale) => (int)Math.Round(logical * scale);
    private static uint Rgb(byte red, byte green, byte blue) => red | ((uint)green << 8) | ((uint)blue << 16);

    private enum ButtonKind
    {
        Neutral,
        Primary,
        Danger,
    }

    private sealed class GdiObjects : IDisposable
    {
        internal GdiObjects(double scale)
        {
            Scale = scale;
            BackgroundBrush = NativeMethods.CreateSolidBrush(BackgroundColor);
            CardBrush = NativeMethods.CreateSolidBrush(CardColor);
            FieldBrush = NativeMethods.CreateSolidBrush(FieldColor);
            OutlineBrush = NativeMethods.CreateSolidBrush(OutlineColor);
            AccentBrush = NativeMethods.CreateSolidBrush(AccentColor);
            AccentHoverBrush = NativeMethods.CreateSolidBrush(AccentHoverColor);
            DangerBrush = NativeMethods.CreateSolidBrush(DangerColor);
            DangerHoverBrush = NativeMethods.CreateSolidBrush(Rgb(223, 66, 70));
            OutlinePen = NativeMethods.CreatePen(NativeMethods.PS_SOLID, 1, OutlineColor);
            WhitePen = NativeMethods.CreatePen(NativeMethods.PS_SOLID, Math.Max(1, Scale(2, scale)), TextColor);
            NoOutlinePen = NativeMethods.CreatePen(NativeMethods.PS_SOLID, 1, BackgroundColor);
            TitleFont = Font(28, NativeMethods.FW_BOLD, "Segoe UI");
            LeadBoldFont = Font(20, NativeMethods.FW_SEMIBOLD, "Segoe UI");
            LeadFont = Font(16, NativeMethods.FW_NORMAL, "Segoe UI");
            BodyFont = Font(14, NativeMethods.FW_NORMAL, "Segoe UI");
            LabelFont = Font(13, NativeMethods.FW_SEMIBOLD, "Segoe UI");
            CaptionFont = Font(13, NativeMethods.FW_NORMAL, "Segoe UI");
            MonoFont = Font(13, NativeMethods.FW_NORMAL, "Consolas");
        }

        internal double Scale { get; }
        internal nint BackgroundBrush { get; }
        internal nint CardBrush { get; }
        internal nint FieldBrush { get; }
        internal nint OutlineBrush { get; }
        internal nint AccentBrush { get; }
        internal nint AccentHoverBrush { get; }
        internal nint DangerBrush { get; }
        internal nint DangerHoverBrush { get; }
        internal nint OutlinePen { get; }
        internal nint WhitePen { get; }
        internal nint NoOutlinePen { get; }
        internal nint TitleFont { get; }
        internal nint LeadBoldFont { get; }
        internal nint LeadFont { get; }
        internal nint BodyFont { get; }
        internal nint LabelFont { get; }
        internal nint CaptionFont { get; }
        internal nint MonoFont { get; }

        private nint Font(int size, int weight, string face)
            => NativeMethods.CreateFont(-Scale(size, Scale), 0, 0, 0, weight, 0, 0, 0,
                NativeMethods.DEFAULT_CHARSET, 0, 0, NativeMethods.CLEARTYPE_QUALITY, 0, face);

        public void Dispose()
        {
            foreach (var value in new[]
                     {
                         BackgroundBrush, CardBrush, FieldBrush, OutlineBrush, AccentBrush, AccentHoverBrush,
                         DangerBrush, DangerHoverBrush, OutlinePen, WhitePen, NoOutlinePen, TitleFont,
                         LeadBoldFont, LeadFont, BodyFont, LabelFont, CaptionFont, MonoFont,
                     })
                _ = NativeMethods.DeleteObject(value);
        }
    }
}
