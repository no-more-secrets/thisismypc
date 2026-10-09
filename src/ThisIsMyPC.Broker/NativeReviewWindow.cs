using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using ThisIsMyPC.Core.Changes;

namespace ThisIsMyPC.Broker;

// The elevated checkpoint uses Win32 and GDI only. All displayed text comes from a validated policy.
internal static unsafe partial class NativeReviewWindow
{
    internal enum Decision { Failed, Cancelled, Discarded, Apply }

    private const string ClassName = "ThisIsMyPCBrokerReview";
    private const uint WsCaption = 0x00C00000;
    private const uint WsSysMenu = 0x00080000;
    private const uint WsChild = 0x40000000;
    private const uint WsVisible = 0x10000000;
    private const uint WsTabStop = 0x00010000;
    private const uint WsVScroll = 0x00200000;
    private const uint BsOwnerDraw = 0x0000000B;
    private const uint WmCreate = 0x0001;
    private const uint WmDestroy = 0x0002;
    private const uint WmSize = 0x0005;
    private const uint WmPaint = 0x000F;
    private const uint WmClose = 0x0010;
    private const uint WmEraseBackground = 0x0014;
    private const uint WmDrawItem = 0x002B;
    private const uint WmPrintClient = 0x0318;
    private const uint WmCommand = 0x0111;
    private const uint WmVScroll = 0x0115;
    private const uint WmMouseWheel = 0x020A;
    private const uint DtLeft = 0;
    private const uint DtRight = 2;
    private const uint DtVCenter = 4;
    private const uint DtWordBreak = 0x10;
    private const uint DtSingleLine = 0x20;
    private const uint DtCalcRect = 0x400;
    private const uint DtNoPrefix = 0x800;
    private const uint SifRange = 1;
    private const uint SifPage = 2;
    private const uint SifPos = 4;
    private const uint SifTrackPos = 0x10;
    private const int ApplyButton = 1001;
    private const int DiscardButton = 1002;
    private const int HeaderHeight = 47;
    private const int FooterHeight = 59;
    private const int NoteHeight = 38;
    private const int CardGap = 4;

    // These are the dark theme values in App/Styles/Theme.axaml.
    private const uint Raised = 0x382424;
    private const uint Outline = 0x5A3F3F;
    private const uint Overlay = 0x503838;
    private const uint White = 0xFFFFFF;
    private const uint Secondary = 0xD0CACA;
    private const uint Tertiary = 0xA5A0A0;
    private const uint Accent = 0xD98D5B;
    private const uint AccentOutline = 0xE3A67F;
    private const uint SuccessMuted = 0x2E4A2D;
    private const uint DangerMuted = 0x20204A;
    private const uint WarningMuted = 0x1E3A4A;

    private static ReviewState? _current;

    internal static Decision Show(BrokerRequestPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        var pages = policy.BuildConfirmationPages();
        if (pages.Count == 0 || pages.Sum(page => page.Length) > 1_000_000 || _current is not null)
            return Decision.Failed;

        var state = new ReviewState(policy.BuildReviewCards(), policy.HasBatchOperations);
        if (state.Cards.Count == 0)
            return Decision.Failed;
        _current = state;
        var className = Marshal.StringToHGlobalUni(ClassName);
        try
        {
            var windowClass = new WindowClass
            {
                Size = (uint)sizeof(WindowClass),
                WindowProc = (nint)(delegate* unmanaged[Stdcall]<nint, uint, nuint, nint, nint>)&WindowProc,
                Instance = GetModuleHandleW(null),
                Cursor = LoadCursorW(0, (nint)32512),
                Background = GetSysColorBrush(15),
                ClassName = className,
            };
            if (RegisterClassExW(ref windowClass) == 0)
                return Decision.Failed;
            try
            {
                var width = Math.Min(480, Math.Max(320, GetSystemMetrics(0) - 40));
                var height = Math.Min(540, Math.Max(330, GetSystemMetrics(1) - 40));
                var x = Math.Max(0, (GetSystemMetrics(0) - width) / 2);
                var y = Math.Max(0, (GetSystemMetrics(1) - height) / 2);
                var window = CreateWindowExW(0, ClassName, "Review Pending Changes - ThisIsMyPC",
                    WsCaption | WsSysMenu | WsVScroll, x, y, width, height, 0, 0, windowClass.Instance, 0);
                if (window == 0)
                    return Decision.Failed;

                state.Window = window;
                state.TitleFont = CreateFont(-17, 600);
                state.CardFont = CreateFont(-15, 600);
                state.DetailFont = CreateFont(-13, 400);
                if (state.TitleFont == 0 || state.CardFont == 0 || state.DetailFont == 0
                    || !GetClientRect(window, out var client))
                {
                    state.Result = Decision.Failed;
                    _ = DestroyWindow(window);
                    return state.Result;
                }

                state.ClientWidth = client.Right;
                state.ClientHeight = client.Bottom;
                var footerTop = state.ClientHeight - FooterHeight;
                state.Discard = CreateWindowExW(0, "BUTTON", state.Batch ? "Discard All" : "Cancel",
                    WsChild | WsVisible | WsTabStop | BsOwnerDraw,
                    16, footerTop + 11, 112, 36, window, (nint)DiscardButton, windowClass.Instance, 0);
                state.Apply = CreateWindowExW(0, "BUTTON", state.Batch ? "Apply All" : "Confirm",
                    WsChild | WsVisible | WsTabStop | BsOwnerDraw,
                    state.ClientWidth - 128, footerTop + 11, 112, 36,
                    window, (nint)ApplyButton, windowClass.Instance, 0);
                if (state.Discard == 0 || state.Apply == 0)
                {
                    state.Result = Decision.Failed;
                    _ = DestroyWindow(window);
                    return state.Result;
                }

                var dark = 1;
                _ = DwmSetWindowAttribute(window, 20, in dark, sizeof(int));
                _ = SetWindowTheme(window, "DarkMode_Explorer", null);
                try { Layout(state); }
                catch
                {
                    state.Result = Decision.Failed;
                    _ = DestroyWindow(window);
                    return state.Result;
                }
                _ = ShowWindow(window, 5);
                _ = UpdateWindow(window);
                _ = SetForegroundWindow(window);
                _ = SetFocus(state.Discard);

                state.LoopStarted = true;
                while (GetMessageW(out var message, 0, 0, 0) > 0)
                {
                    if (IsDialogMessageW(window, ref message))
                        continue;
                    _ = TranslateMessage(in message);
                    _ = DispatchMessageW(in message);
                }
                return state.Result;
            }
            finally
            {
                if (state.TitleFont != 0) _ = DeleteObject(state.TitleFont);
                if (state.CardFont != 0) _ = DeleteObject(state.CardFont);
                if (state.DetailFont != 0) _ = DeleteObject(state.DetailFont);
                _ = UnregisterClassW(ClassName, windowClass.Instance);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(className);
            _current = null;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static nint WindowProc(nint window, uint message, nuint wParam, nint lParam)
    {
        try
        {
            return HandleMessage(window, message, wParam, lParam);
        }
        catch (Exception ex)
        {
            try { NLog.LogManager.GetLogger("ThisIsMyPC.Broker.NativeReviewWindow").Error(ex, "Native review failed"); }
            catch { /* A logger failure must not escape the native callback. */ }
            if (_current is { } state)
                state.Result = Decision.Failed;
            if (message != WmDestroy)
                _ = DestroyWindow(window);
            else
                PostQuitMessage(0);
            return 0;
        }
    }

    private static nint HandleMessage(nint window, uint message, nuint wParam, nint lParam)
    {
        if (message == WmCreate)
            return 0;
        var state = _current;
        if (state is null)
            return DefWindowProcW(window, message, wParam, lParam);
        if (message == WmCommand && ((wParam >> 16) & 0xffff) == 0
            && (wParam & 0xffff) is ApplyButton or DiscardButton)
        {
            state.Result = (wParam & 0xffff) == ApplyButton ? Decision.Apply : Decision.Discarded;
            _ = DestroyWindow(window);
            return 0;
        }
        if (message == WmCommand && ((wParam >> 16) & 0xffff) == 0 && (wParam & 0xffff) == 2)
        {
            _ = DestroyWindow(window);
            return 0;
        }
        if (message == WmDrawItem && lParam != 0)
        {
            var item = (DrawItem*)lParam;
            if (item->ControlId is ApplyButton or DiscardButton)
            {
                DrawButton(state, item);
                return 1;
            }
        }
        if (message == WmEraseBackground)
            return 1;
        if (message == WmPaint)
        {
            var hdc = BeginPaint(window, out var paint);
            if (hdc != 0)
            {
                try { DrawFrame(state, hdc); }
                finally { _ = EndPaint(window, in paint); }
            }
            return 0;
        }
        if (message == WmPrintClient)
        {
            if (wParam != 0)
                DrawFrame(state, (nint)wParam);
            return 0;
        }
        if (message == WmSize && state.Window != 0)
        {
            if (GetClientRect(window, out var client))
            {
                state.ClientWidth = client.Right;
                state.ClientHeight = client.Bottom;
                Layout(state);
            }
            return 0;
        }
        if (message == WmVScroll)
        {
            Scroll(state, (int)(wParam & 0xffff));
            return 0;
        }
        if (message == WmMouseWheel)
        {
            var delta = (short)((wParam >> 16) & 0xffff);
            SetScroll(state, state.ScrollOffset - delta / 120 * 54);
            return 0;
        }
        if (message == WmClose)
        {
            _ = DestroyWindow(window);
            return 0;
        }
        if (message == WmDestroy)
        {
            if (state.LoopStarted)
                PostQuitMessage(0);
            return 0;
        }
        return DefWindowProcW(window, message, wParam, lParam);
    }

    private static void Layout(ReviewState state)
    {
        var hdc = GetDC(state.Window);
        if (hdc == 0)
            throw new InvalidOperationException("Could not create the review layout.");
        try
        {
            var textWidth = Math.Max(100, state.ClientWidth - 64);
            var y = 0;
            state.Layouts.Clear();
            foreach (var card in state.Cards)
            {
                var titleText = Wrap(hdc, state.CardFont, card.Title, textWidth);
                var titleHeight = Measure(hdc, state.CardFont, titleText, textWidth);
                var lines = new int[card.Lines.Count];
                var lineText = new string[card.Lines.Count];
                var height = 17 + titleHeight;
                for (var index = 0; index < lines.Length; index++)
                {
                    lineText[index] = Wrap(hdc, state.DetailFont, card.Lines[index], textWidth);
                    lines[index] = Measure(hdc, state.DetailFont, lineText[index], textWidth);
                    height = checked(height + lines[index] + 3);
                }
                height = checked(height + 7);
                state.Layouts.Add(new CardLayout(y, height, titleHeight, titleText, lines, lineText));
                y = checked(y + height + CardGap);
            }
            state.ContentHeight = y;
            UpdateScroll(state);
        }
        finally { _ = ReleaseDC(state.Window, hdc); }
    }

    private static int Measure(nint hdc, nint font, string text, int width)
    {
        var old = SelectObject(hdc, font);
        try
        {
            if (text.Length == 0)
                return 16;
            var rect = new Rect { Right = width };
            if (DrawTextW(hdc, text, text.Length, ref rect, DtLeft | DtWordBreak | DtCalcRect | DtNoPrefix) == 0)
                throw new InvalidOperationException("Could not measure review text.");
            return Math.Max(16, rect.Bottom);
        }
        finally { _ = SelectObject(hdc, old); }
    }

    private static string Wrap(nint hdc, nint font, string value, int width)
    {
        var old = SelectObject(hdc, font);
        try
        {
            var text = new StringBuilder(value.Length + 16);
            var offset = 0;
            while (offset < value.Length)
            {
                var remaining = value[offset..];
                if (!GetTextExtentExPointW(hdc, remaining, remaining.Length, Math.Max(32, width - 4),
                        out var fit, 0, out _))
                    throw new InvalidOperationException("Could not measure review text.");
                if (fit >= remaining.Length)
                {
                    text.Append(remaining);
                    break;
                }
                fit = Math.Max(1, fit);
                var breakAt = remaining.LastIndexOf(' ', fit - 1, fit);
                if (breakAt > fit / 2)
                    fit = breakAt + 1;
                text.Append(remaining.AsSpan(0, fit).TrimEnd());
                text.Append("\r\n");
                offset += fit;
                while (offset < value.Length && value[offset] == ' ')
                    offset++;
            }
            return text.ToString();
        }
        finally { _ = SelectObject(hdc, old); }
    }

    private static void DrawFrame(ReviewState state, nint hdc)
    {
        var width = state.ClientWidth;
        var height = state.ClientHeight;
        Fill(hdc, new Rect { Right = width, Bottom = height }, Raised);
        Fill(hdc, new Rect { Top = HeaderHeight - 1, Right = width, Bottom = HeaderHeight }, Outline);
        Fill(hdc, new Rect { Top = height - FooterHeight, Right = width, Bottom = height - FooterHeight + 1 }, Outline);

        var compact = width < 420;
        var countLeft = compact ? width - 52 : width - 125;
        Text(hdc, state.TitleFont, White, "Review Pending Changes",
            new Rect { Left = 16, Top = 12, Right = countLeft - 8, Bottom = 35 }, DtSingleLine | DtNoPrefix);
        var count = compact ? $"{state.Cards.Count}"
            : !state.Batch ? state.Cards.Count == 1 ? "1 operation" : $"{state.Cards.Count} operations"
            : state.Cards.Count == 1 ? "1 change" : $"{state.Cards.Count} changes";
        Text(hdc, state.DetailFont, Tertiary, count,
            new Rect { Left = countLeft, Top = 15, Right = width - 16, Bottom = 34 }, DtSingleLine | DtRight | DtNoPrefix);
        Text(hdc, state.DetailFont, Secondary, "Approve only if every target matches your selected changes.",
            new Rect { Left = 16, Top = HeaderHeight + 7, Right = width - 24, Bottom = HeaderHeight + NoteHeight },
            DtWordBreak | DtNoPrefix);

        var listTop = HeaderHeight + NoteHeight;
        var listBottom = height - FooterHeight;
        var saved = SaveDC(hdc);
        if (saved == 0)
            throw new InvalidOperationException("Could not save the review canvas.");
        bool restored;
        try
        {
            if (IntersectClipRect(hdc, 0, listTop, width, listBottom) == 0)
                throw new InvalidOperationException("Could not clip review cards.");
            for (var index = 0; index < state.Layouts.Count; index++)
            {
                var layout = state.Layouts[index];
                var top = listTop + layout.Top - state.ScrollOffset;
                if (top + layout.Height < listTop || top >= listBottom)
                    continue;
                DrawCard(hdc, state, state.Cards[index], layout, top);
            }
        }
        finally { restored = RestoreDC(hdc, saved); }
        if (!restored)
            throw new InvalidOperationException("Could not restore the review canvas.");
    }

    private static void DrawCard(nint hdc, ReviewState state, BrokerReviewCard card, CardLayout layout, int top)
    {
        var left = 8;
        var right = state.ClientWidth - 24;
        var color = card.Category switch
        {
            ChangeCategory.Enable or ChangeCategory.Create => SuccessMuted,
            ChangeCategory.Disable or ChangeCategory.Delete => DangerMuted,
            ChangeCategory.Modify => WarningMuted,
            _ => Raised,
        };
        RoundFill(hdc, new Rect { Left = left, Top = top, Right = right, Bottom = top + layout.Height },
            color, card.Category is null ? Outline : color, 5);
        var x = left + 12;
        var textRight = right - 12;
        var y = top + 8;
        Text(hdc, state.CardFont, White, layout.TitleText,
            new Rect { Left = x, Top = y, Right = textRight, Bottom = y + layout.TitleHeight }, DtWordBreak | DtNoPrefix);
        y += layout.TitleHeight + 5;
        for (var index = 0; index < card.Lines.Count; index++)
        {
            var colorForLine = index == 1 && card.Category is not null ? White : index == 0 ? Secondary : Tertiary;
            Text(hdc, state.DetailFont, colorForLine, layout.LineText[index],
                new Rect { Left = x, Top = y, Right = textRight, Bottom = y + layout.LineHeights[index] },
                DtWordBreak | DtNoPrefix);
            y += layout.LineHeights[index] + 3;
        }
    }

    private static void DrawButton(ReviewState state, DrawItem* item)
    {
        var apply = item->ControlId == ApplyButton;
        var selected = (item->ItemState & 1) != 0;
        var background = apply ? selected ? 0xA8683F : Accent : selected ? 0x6A4C4C : Overlay;
        Fill(item->Hdc, item->Bounds, Raised);
        RoundFill(item->Hdc, item->Bounds, background, apply ? AccentOutline : Outline, 4);
        Text(item->Hdc, state.DetailFont, apply ? White : Secondary,
            apply ? state.Batch ? "Apply All" : "Confirm" : state.Batch ? "Discard All" : "Cancel",
            item->Bounds, DtSingleLine | DtVCenter | DtNoPrefix | 1);
        if ((item->ItemState & 0x10) != 0)
        {
            var focus = item->Bounds;
            focus.Left += 3;
            focus.Top += 3;
            focus.Right -= 3;
            focus.Bottom -= 3;
            _ = DrawFocusRect(item->Hdc, in focus);
        }
    }

    private static void Scroll(ReviewState state, int command)
    {
        var viewport = Math.Max(1, state.ClientHeight - FooterHeight - HeaderHeight - NoteHeight);
        var next = command switch
        {
            0 => state.ScrollOffset - 30,
            1 => state.ScrollOffset + 30,
            2 => state.ScrollOffset - viewport,
            3 => state.ScrollOffset + viewport,
            4 or 5 => TrackPosition(state),
            6 => 0,
            7 => state.ContentHeight,
            _ => state.ScrollOffset,
        };
        SetScroll(state, next);
    }

    private static int TrackPosition(ReviewState state)
    {
        var info = new ScrollInfo { Size = (uint)sizeof(ScrollInfo), Mask = SifTrackPos };
        return GetScrollInfo(state.Window, 1, ref info) ? info.TrackPosition : state.ScrollOffset;
    }

    private static void SetScroll(ReviewState state, int position)
    {
        var viewport = Math.Max(1, state.ClientHeight - FooterHeight - HeaderHeight - NoteHeight);
        var clamped = Math.Clamp(position, 0, Math.Max(0, state.ContentHeight - viewport));
        if (clamped == state.ScrollOffset)
            return;
        state.ScrollOffset = clamped;
        UpdateScroll(state);
        _ = InvalidateRect(state.Window, 0, false);
    }

    private static void UpdateScroll(ReviewState state)
    {
        var viewport = Math.Max(1, state.ClientHeight - FooterHeight - HeaderHeight - NoteHeight);
        var info = new ScrollInfo
        {
            Size = (uint)sizeof(ScrollInfo),
            Mask = SifRange | SifPage | SifPos,
            Minimum = 0,
            Maximum = Math.Max(0, state.ContentHeight - 1),
            Page = (uint)viewport,
            Position = state.ScrollOffset,
        };
        _ = SetScrollInfo(state.Window, 1, in info, true);
    }

    private static void Fill(nint hdc, Rect rect, uint color)
    {
        var brush = CreateSolidBrush(color);
        if (brush == 0)
            throw new InvalidOperationException("Could not draw the review surface.");
        try
        {
            if (FillRect(hdc, in rect, brush) == 0)
                throw new InvalidOperationException("Could not draw the review surface.");
        }
        finally { _ = DeleteObject(brush); }
    }

    private static void RoundFill(nint hdc, Rect rect, uint fill, uint stroke, int radius)
    {
        var brush = CreateSolidBrush(fill);
        var pen = CreatePen(0, 1, stroke);
        if (brush == 0 || pen == 0)
        {
            if (brush != 0) _ = DeleteObject(brush);
            if (pen != 0) _ = DeleteObject(pen);
            throw new InvalidOperationException("Could not draw a review card.");
        }
        var oldBrush = SelectObject(hdc, brush);
        var oldPen = SelectObject(hdc, pen);
        var drawn = RoundRect(hdc, rect.Left, rect.Top, rect.Right, rect.Bottom, radius * 2, radius * 2);
        _ = SelectObject(hdc, oldPen);
        _ = SelectObject(hdc, oldBrush);
        _ = DeleteObject(pen);
        _ = DeleteObject(brush);
        if (!drawn)
            throw new InvalidOperationException("Could not draw a review card.");
    }

    private static void Text(nint hdc, nint font, uint color, string value, Rect rect, uint flags)
    {
        var oldFont = SelectObject(hdc, font);
        var oldColor = SetTextColor(hdc, color);
        var oldMode = SetBkMode(hdc, 1);
        var drawn = DrawTextW(hdc, value, value.Length, ref rect, flags);
        _ = SetBkMode(hdc, oldMode);
        _ = SetTextColor(hdc, oldColor);
        _ = SelectObject(hdc, oldFont);
        if (drawn == 0 && value.Length > 0)
            throw new InvalidOperationException("Could not draw review text.");
    }

    private static nint CreateFont(int height, int weight) => CreateFontW(height, 0, 0, 0, weight,
        0, 0, 0, 1, 0, 0, 5, 0, "Segoe UI");

    private sealed class ReviewState(IReadOnlyList<BrokerReviewCard> cards, bool batch)
    {
        internal Decision Result = Decision.Cancelled;
        internal readonly IReadOnlyList<BrokerReviewCard> Cards = cards;
        internal readonly List<CardLayout> Layouts = [];
        internal readonly bool Batch = batch;
        internal nint Window;
        internal nint Discard;
        internal nint Apply;
        internal nint TitleFont;
        internal nint CardFont;
        internal nint DetailFont;
        internal int ClientWidth;
        internal int ClientHeight;
        internal int ContentHeight;
        internal int ScrollOffset;
        internal bool LoopStarted;
    }

    private sealed record CardLayout(int Top, int Height, int TitleHeight, string TitleText,
        int[] LineHeights, string[] LineText);

    [StructLayout(LayoutKind.Sequential)]
    private struct TextSize { internal int Width; internal int Height; }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowClass
    {
        internal uint Size;
        internal uint Style;
        internal nint WindowProc;
        internal int ClassExtra;
        internal int WindowExtra;
        internal nint Instance;
        internal nint Icon;
        internal nint Cursor;
        internal nint Background;
        internal nint MenuName;
        internal nint ClassName;
        internal nint SmallIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point { internal int X; internal int Y; }
    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { internal int Left; internal int Top; internal int Right; internal int Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct Message
    {
        internal nint Window;
        internal uint Id;
        internal nuint WParam;
        internal nint LParam;
        internal uint Time;
        internal Point Location;
        internal uint Private;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct PaintStruct
    {
        internal nint Hdc;
        internal int Erase;
        internal Rect Paint;
        internal int Restore;
        internal int IncUpdate;
        internal fixed byte Reserved[32];
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct DrawItem
    {
        internal uint ControlType;
        internal uint ControlId;
        internal uint ItemId;
        internal uint ItemAction;
        internal uint ItemState;
        internal nint Window;
        internal nint Hdc;
        internal Rect Bounds;
        internal nuint Data;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct ScrollInfo
    {
        internal uint Size;
        internal uint Mask;
        internal int Minimum;
        internal int Maximum;
        internal uint Page;
        internal int Position;
        internal int TrackPosition;
    }

    [LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleW", StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint GetModuleHandleW(string? moduleName);
    [LibraryImport("user32.dll", EntryPoint = "RegisterClassExW", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial ushort RegisterClassExW(ref WindowClass windowClass);
    [LibraryImport("user32.dll", EntryPoint = "UnregisterClassW", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial bool UnregisterClassW(string className, nint instance);
    [LibraryImport("user32.dll", EntryPoint = "CreateWindowExW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint CreateWindowExW(uint exStyle, string className, string title, uint style,
        int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);
    [LibraryImport("user32.dll", EntryPoint = "DefWindowProcW")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint DefWindowProcW(nint window, uint message, nuint wParam, nint lParam);
    [LibraryImport("user32.dll", EntryPoint = "DestroyWindow")]
    [return: MarshalAs(UnmanagedType.Bool)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial bool DestroyWindow(nint window);
    [LibraryImport("user32.dll", EntryPoint = "GetMessageW")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int GetMessageW(out Message message, nint window, uint min, uint max);
    [LibraryImport("user32.dll", EntryPoint = "IsDialogMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial bool IsDialogMessageW(nint window, ref Message message);
    [LibraryImport("user32.dll", EntryPoint = "TranslateMessage")]
    [return: MarshalAs(UnmanagedType.Bool)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial bool TranslateMessage(in Message message);
    [LibraryImport("user32.dll", EntryPoint = "DispatchMessageW")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint DispatchMessageW(in Message message);
    [LibraryImport("user32.dll", EntryPoint = "PostQuitMessage")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial void PostQuitMessage(int exitCode);
    [LibraryImport("user32.dll", EntryPoint = "ShowWindow")]
    [return: MarshalAs(UnmanagedType.Bool)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial bool ShowWindow(nint window, int command);
    [LibraryImport("user32.dll", EntryPoint = "UpdateWindow")]
    [return: MarshalAs(UnmanagedType.Bool)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial bool UpdateWindow(nint window);
    [LibraryImport("user32.dll", EntryPoint = "SetFocus")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint SetFocus(nint window);
    [LibraryImport("user32.dll", EntryPoint = "GetSystemMetrics")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int GetSystemMetrics(int index);
    [LibraryImport("user32.dll", EntryPoint = "LoadCursorW")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint LoadCursorW(nint instance, nint cursor);
    [LibraryImport("user32.dll", EntryPoint = "GetSysColorBrush")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint GetSysColorBrush(int index);
    [LibraryImport("user32.dll", EntryPoint = "GetClientRect")]
    [return: MarshalAs(UnmanagedType.Bool)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial bool GetClientRect(nint window, out Rect rectangle);
    [LibraryImport("user32.dll", EntryPoint = "SetForegroundWindow")]
    [return: MarshalAs(UnmanagedType.Bool)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial bool SetForegroundWindow(nint window);
    [LibraryImport("user32.dll", EntryPoint = "BeginPaint")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint BeginPaint(nint window, out PaintStruct paint);
    [LibraryImport("user32.dll", EntryPoint = "EndPaint")]
    [return: MarshalAs(UnmanagedType.Bool)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial bool EndPaint(nint window, in PaintStruct paint);
    [LibraryImport("user32.dll", EntryPoint = "GetDC")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint GetDC(nint window);
    [LibraryImport("user32.dll", EntryPoint = "ReleaseDC")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int ReleaseDC(nint window, nint hdc);
    [LibraryImport("user32.dll", EntryPoint = "DrawTextW", StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int DrawTextW(nint hdc, string text, int length, ref Rect rect, uint format);
    [LibraryImport("gdi32.dll", EntryPoint = "GetTextExtentExPointW", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial bool GetTextExtentExPointW(nint hdc, string text, int count, int maxExtent,
        out int fit, nint advances, out TextSize size);
    [LibraryImport("user32.dll", EntryPoint = "FillRect")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int FillRect(nint hdc, in Rect rect, nint brush);
    [LibraryImport("user32.dll", EntryPoint = "DrawFocusRect")]
    [return: MarshalAs(UnmanagedType.Bool)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial bool DrawFocusRect(nint hdc, in Rect rect);
    [LibraryImport("user32.dll", EntryPoint = "InvalidateRect")]
    [return: MarshalAs(UnmanagedType.Bool)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial bool InvalidateRect(nint window, nint rect, [MarshalAs(UnmanagedType.Bool)] bool erase);
    [LibraryImport("user32.dll", EntryPoint = "SetScrollInfo")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int SetScrollInfo(nint window, int bar, in ScrollInfo info, [MarshalAs(UnmanagedType.Bool)] bool redraw);
    [LibraryImport("user32.dll", EntryPoint = "GetScrollInfo")]
    [return: MarshalAs(UnmanagedType.Bool)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial bool GetScrollInfo(nint window, int bar, ref ScrollInfo info);
    [LibraryImport("gdi32.dll", EntryPoint = "CreateFontW", StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint CreateFontW(int height, int width, int escapement, int orientation, int weight,
        uint italic, uint underline, uint strikeOut, uint charSet, uint outputPrecision, uint clipPrecision,
        uint quality, uint pitchAndFamily, string faceName);
    [LibraryImport("gdi32.dll", EntryPoint = "CreateSolidBrush")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint CreateSolidBrush(uint color);
    [LibraryImport("gdi32.dll", EntryPoint = "CreatePen")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint CreatePen(int style, int width, uint color);
    [LibraryImport("gdi32.dll", EntryPoint = "RoundRect")]
    [return: MarshalAs(UnmanagedType.Bool)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial bool RoundRect(nint hdc, int left, int top, int right, int bottom, int width, int height);
    [LibraryImport("gdi32.dll", EntryPoint = "SelectObject")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint SelectObject(nint hdc, nint objectHandle);
    [LibraryImport("gdi32.dll", EntryPoint = "DeleteObject")]
    [return: MarshalAs(UnmanagedType.Bool)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial bool DeleteObject(nint objectHandle);
    [LibraryImport("gdi32.dll", EntryPoint = "SetTextColor")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial uint SetTextColor(nint hdc, uint color);
    [LibraryImport("gdi32.dll", EntryPoint = "SetBkMode")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int SetBkMode(nint hdc, int mode);
    [LibraryImport("gdi32.dll", EntryPoint = "SaveDC")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int SaveDC(nint hdc);
    [LibraryImport("gdi32.dll", EntryPoint = "RestoreDC")]
    [return: MarshalAs(UnmanagedType.Bool)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial bool RestoreDC(nint hdc, int state);
    [LibraryImport("gdi32.dll", EntryPoint = "IntersectClipRect")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int IntersectClipRect(nint hdc, int left, int top, int right, int bottom);
    [LibraryImport("dwmapi.dll", EntryPoint = "DwmSetWindowAttribute")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int DwmSetWindowAttribute(nint window, uint attribute, in int value, uint size);
    [LibraryImport("uxtheme.dll", EntryPoint = "SetWindowTheme", StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int SetWindowTheme(nint window, string? subAppName, string? subIdList);
}
