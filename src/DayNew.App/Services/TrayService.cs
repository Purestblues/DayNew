using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace DayNew.Services;

/// <summary>
/// 纯 Win32 Shell_NotifyIconW 托盘常驻服务（无 WinForms 依赖）：
/// 显示托盘图标、动态悬浮提示，以及匹配当前主题的独立激活态 WPF 托盘菜单窗口。
/// </summary>
public sealed class TrayService : IDisposable
{
    public const uint WmTrayCallback = 0x8001; // WM_APP + 1

    private const uint TrayIconId = 1;
    private const uint NimAdd = 0x00000000;
    private const uint NimModify = 0x00000001;
    private const uint NimDelete = 0x00000002;
    private const uint NifMessage = 0x00000001;
    private const uint NifIcon = 0x00000002;
    private const uint NifTip = 0x00000004;

    private const uint WmLButtonUp = 0x0202;
    private const uint WmLButtonDblClk = 0x0203;
    private const uint WmRButtonUp = 0x0205;
    private const uint WmContextMenu = 0x007B;

    private readonly IntPtr _hwnd;
    private readonly IntPtr _hIcon;
    private readonly bool _ownsIcon;
    private readonly uint _wmTaskbarCreated;
    private string _tooltip = "日新 DayNew";
    private string _summaryTooltip = "日新 · 今日无待办";
    private string? _focusTooltipOverride;

    private TrayMenuWindow? _menuWindow;
    private long _lastMenuTick;

    private readonly Action _onOpen;
    private readonly Action _onToggleWindow;
    private readonly Func<bool> _getNotifyEnabled;
    private readonly Action<bool> _setNotifyEnabled;
    private readonly Func<bool> _getAutoStartEnabled;
    private readonly Action<bool> _setAutoStartEnabled;
    private readonly Action _onExit;

    private bool _disposed;

    public TrayService(
        IntPtr messageWindowHandle,
        Action onOpen,
        Action onToggleWindow,
        Func<bool> getNotifyEnabled,
        Action<bool> setNotifyEnabled,
        Func<bool> getAutoStartEnabled,
        Action<bool> setAutoStartEnabled,
        Action onExit)
    {
        _hwnd = messageWindowHandle;
        _onOpen = onOpen;
        _onToggleWindow = onToggleWindow;
        _getNotifyEnabled = getNotifyEnabled;
        _setNotifyEnabled = setNotifyEnabled;
        _getAutoStartEnabled = getAutoStartEnabled;
        _setAutoStartEnabled = setAutoStartEnabled;
        _onExit = onExit;

        _wmTaskbarCreated = RegisterWindowMessage("TaskbarCreated");

        var exePath = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(exePath) && ExtractIconExW(exePath, 0, IntPtr.Zero, out var smallIcon, 1) > 0 && smallIcon != IntPtr.Zero)
        {
            _hIcon = smallIcon;
            _ownsIcon = true;
        }
        else
        {
            _hIcon = LoadIconW(IntPtr.Zero, (IntPtr)32512); // IDI_APPLICATION
            _ownsIcon = false;
        }

        AddTrayIcon();
    }

    public bool HandleWindowMessage(uint msg, IntPtr lParam)
    {
        if (_disposed) return false;

        if (_wmTaskbarCreated != 0 && msg == _wmTaskbarCreated)
        {
            AddTrayIcon();
            return true;
        }

        if (msg != WmTrayCallback) return false;

        var mouseMsg = unchecked((uint)(lParam.ToInt64() & 0xFFFF));
        switch (mouseMsg)
        {
            case WmLButtonUp:
                Application.Current?.Dispatcher.BeginInvoke(_onToggleWindow);
                return true;
            case WmLButtonDblClk:
                Application.Current?.Dispatcher.BeginInvoke(_onOpen);
                return true;
            case WmRButtonUp:
            case WmContextMenu:
            {
                // Win11 下 Explorer 可能紧接着派发 WM_RBUTTONUP 与 WM_CONTEXTMENU，短时间去重防止重复闪开
                var now = Environment.TickCount64;
                if (now - _lastMenuTick < 150) return true;
                _lastMenuTick = now;

                GetCursorPos(out var cursorPt);
                Application.Current?.Dispatcher.BeginInvoke(() => ShowMenu(cursorPt));
                return true;
            }
            default:
                return false;
        }
    }

    public void UpdateTooltip(int todayPending, int overdueCount)
    {
        if (_disposed) return;

        var text = (todayPending, overdueCount) switch
        {
            (0, 0) => "日新 · 今日无待办",
            (0, > 0) => $"日新 · 今日无新任务，{overdueCount} 项逾期",
            (> 0, 0) => $"日新 · 今日 {todayPending} 项待办",
            _ => $"日新 · 今日 {todayPending} 项待办，{overdueCount} 项逾期"
        };

        // Win32 NOTIFYICONDATA.szTip 上限 127 字符（老系统 63），保持简洁
        if (text.Length > 60) text = text[..60];
        _summaryTooltip = text;
        ApplyCurrentTooltip();
    }

    public void UpdateFocusOverride(string? focusText)
    {
        if (_disposed) return;

        var next = string.IsNullOrWhiteSpace(focusText) ? null : focusText.Trim();
        if (next is { Length: > 60 }) next = next[..60];
        if (string.Equals(_focusTooltipOverride, next, StringComparison.Ordinal)) return;

        _focusTooltipOverride = next;
        ApplyCurrentTooltip();
    }

    private void ApplyCurrentTooltip()
    {
        var desired = _focusTooltipOverride ?? _summaryTooltip;
        if (desired.Length > 60) desired = desired[..60];
        if (string.Equals(_tooltip, desired, StringComparison.Ordinal)) return;

        _tooltip = desired;
        var nid = CreateIconData(NifTip);
        Shell_NotifyIconW(NimModify, ref nid);
    }

    private void AddTrayIcon()
    {
        var nid = CreateIconData(NifMessage | NifIcon | NifTip);
        Shell_NotifyIconW(NimAdd, ref nid);
    }

    private NotifyIconData CreateIconData(uint flags) => new()
    {
        cbSize = (uint)Marshal.SizeOf<NotifyIconData>(),
        hWnd = _hwnd,
        uID = TrayIconId,
        uFlags = flags,
        uCallbackMessage = WmTrayCallback,
        hIcon = _hIcon,
        szTip = _tooltip
    };

    private void ShowMenu(POINT cursorPt)
    {
        if (_disposed) return;

        CloseActiveMenu();

        var menu = new TrayMenuWindow(
            notifyChecked: _getNotifyEnabled(),
            autoStartChecked: _getAutoStartEnabled(),
            onOpen: _onOpen,
            onToggleNotify: next => _setNotifyEnabled(next),
            onToggleAutoStart: next => _setAutoStartEnabled(next),
            onExit: _onExit);

        _menuWindow = menu;
        menu.Closed += (_, _) =>
        {
            if (ReferenceEquals(_menuWindow, menu))
                _menuWindow = null;
        };

        menu.ShowAtScreenPoint(cursorPt);
    }

    private void CloseActiveMenu()
    {
        if (_menuWindow is not { } existing) return;
        _menuWindow = null;
        existing.Dismiss();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        CloseActiveMenu();

        var nid = CreateIconData(0);
        Shell_NotifyIconW(NimDelete, ref nid);

        if (_ownsIcon && _hIcon != IntPtr.Zero)
            DestroyIcon(_hIcon);
    }

    /// <summary>
    /// 独立可激活 WPF 托盘菜单窗口：
    /// 解决无主窗体模式下 WPF ContextMenu/Popup 因 WS_EX_NOACTIVATE 与跨进程鼠标捕获
    /// 被 Win11 托盘/隐藏图标浮层收回而半秒内闪退的问题。
    /// </summary>
    private sealed class TrayMenuWindow : Window
    {
        private const double ShadowPad = 10;
        private bool _closing;
        private bool _armed;

        public TrayMenuWindow(
            bool notifyChecked,
            bool autoStartChecked,
            Action onOpen,
            Action<bool> onToggleNotify,
            Action<bool> onToggleAutoStart,
            Action onExit)
        {
            Title = "DayNew.TrayMenu";
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Topmost = true;
            SizeToContent = SizeToContent.WidthAndHeight;
            SnapsToDevicePixels = true;
            UseLayoutRounding = true;
            Focusable = true;

            var stack = new StackPanel { MinWidth = 156 };
            stack.Children.Add(CreateMenuItem("打开日新", isSemiBold: true, isChecked: false, () =>
            {
                Dismiss();
                onOpen();
            }));
            stack.Children.Add(CreateSeparator());
            stack.Children.Add(CreateMenuItem("系统通知", isSemiBold: false, isChecked: notifyChecked, () =>
            {
                Dismiss();
                onToggleNotify(!notifyChecked);
            }));
            stack.Children.Add(CreateMenuItem("开机静默自启", isSemiBold: false, isChecked: autoStartChecked, () =>
            {
                Dismiss();
                onToggleAutoStart(!autoStartChecked);
            }));
            stack.Children.Add(CreateSeparator());
            stack.Children.Add(CreateMenuItem("退出日新", isSemiBold: false, isChecked: false, () =>
            {
                Dismiss();
                onExit();
            }));

            var card = new Border
            {
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(4),
                Child = stack,
                Effect = new DropShadowEffect
                {
                    BlurRadius = 16,
                    ShadowDepth = 3,
                    Direction = 270,
                    Opacity = 0.22,
                    Color = Colors.Black
                }
            };
            card.SetResourceReference(Border.BackgroundProperty, "Surface4");
            card.SetResourceReference(Border.BorderBrushProperty, "BorderStrong");

            var root = new Border
            {
                Padding = new Thickness(ShadowPad),
                Background = Brushes.Transparent,
                Child = card
            };
            Content = root;

            Deactivated += (_, _) =>
            {
                if (_armed) Dismiss();
            };
            PreviewKeyDown += (_, e) =>
            {
                if (e.Key == Key.Escape)
                {
                    e.Handled = true;
                    Dismiss();
                }
            };
        }

        public void ShowAtScreenPoint(POINT cursorPt)
        {
            // 先落在光标所在屏幕内测出实际尺寸与 DPI，再精确定位显示，避免跨屏 DPI 跳变与首帧闪烁
            Opacity = 0;
            var (scaleX, scaleY) = GetMonitorDpiScale(cursorPt);
            Left = cursorPt.X / scaleX;
            Top = cursorPt.Y / scaleY;
            Show();
            UpdateLayout();

            PositionNearCursor(cursorPt);
            Opacity = 1;

            var hwnd = new WindowInteropHelper(this).Handle;
            ForceForeground(hwnd);
            Activate();
            Focus();
            _armed = true;
        }

        private static (double ScaleX, double ScaleY) GetMonitorDpiScale(POINT cursorPt)
        {
            try
            {
                var hMon = MonitorFromPoint(cursorPt, MonitorDefaultToNearest);
                if (hMon != IntPtr.Zero && GetDpiForMonitor(hMon, 0, out var dpiX, out var dpiY) == 0 && dpiX > 0 && dpiY > 0)
                    return (dpiX / 96.0, dpiY / 96.0);
            }
            catch
            {
                // 旧版系统无 shcore.dll 时回退 1.0
            }
            return (1.0, 1.0);
        }

        public void Dismiss()
        {
            if (_closing) return;
            _closing = true;
            try
            {
                Close();
            }
            catch
            {
                // 忽略已关闭状态
            }
        }

        private void PositionNearCursor(POINT cursorPt)
        {
            var src = PresentationSource.FromVisual(this);
            var fromDevice = src?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;

            var cursorDip = fromDevice.Transform(new Point(cursorPt.X, cursorPt.Y));
            var workAreaDip = GetWorkAreaDip(cursorPt, fromDevice);

            var menuW = Math.Max(ActualWidth - ShadowPad * 2, 160);
            var menuH = Math.Max(ActualHeight - ShadowPad * 2, 120);

            var cardLeft = cursorDip.X + menuW <= workAreaDip.Right - 4
                ? cursorDip.X
                : cursorDip.X - menuW;
            cardLeft = Math.Clamp(cardLeft, workAreaDip.Left + 4, Math.Max(workAreaDip.Left + 4, workAreaDip.Right - menuW - 4));

            var cardTop = cursorDip.Y - menuH >= workAreaDip.Top + 4
                ? cursorDip.Y - menuH
                : cursorDip.Y;
            cardTop = Math.Clamp(cardTop, workAreaDip.Top + 4, Math.Max(workAreaDip.Top + 4, workAreaDip.Bottom - menuH - 4));

            Left = cardLeft - ShadowPad;
            Top = cardTop - ShadowPad;
        }

        private static Rect GetWorkAreaDip(POINT cursorPt, Matrix fromDevice)
        {
            var hMon = MonitorFromPoint(cursorPt, MonitorDefaultToNearest);
            var mi = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
            if (hMon != IntPtr.Zero && GetMonitorInfoW(hMon, ref mi))
            {
                var tl = fromDevice.Transform(new Point(mi.rcWork.Left, mi.rcWork.Top));
                var br = fromDevice.Transform(new Point(mi.rcWork.Right, mi.rcWork.Bottom));
                return new Rect(tl, br);
            }

            return SystemParameters.WorkArea;
        }

        private static Border CreateMenuItem(string text, bool isSemiBold, bool isChecked, Action onClick)
        {
            var checkGlyph = new TextBlock
            {
                Text = "\uE73E",
                FontSize = 10.5,
                VerticalAlignment = VerticalAlignment.Center,
                Visibility = isChecked ? Visibility.Visible : Visibility.Collapsed
            };
            if (Application.Current?.TryFindResource("IconFont") is FontFamily iconFont)
                checkGlyph.FontFamily = iconFont;
            checkGlyph.SetResourceReference(TextBlock.ForegroundProperty, "Accent");

            var label = new TextBlock
            {
                Text = text,
                FontSize = 12.5,
                FontWeight = isSemiBold ? FontWeights.SemiBold : FontWeights.Medium,
                VerticalAlignment = VerticalAlignment.Center
            };
            if (Application.Current?.TryFindResource("AppFont") is FontFamily appFont)
                label.FontFamily = appFont;
            label.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimary");

            var grid = new Grid { Margin = new Thickness(10, 6, 12, 6) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(checkGlyph, 0);
            Grid.SetColumn(label, 1);
            grid.Children.Add(checkGlyph);
            grid.Children.Add(label);

            var row = new Border
            {
                Background = Brushes.Transparent,
                CornerRadius = new CornerRadius(6),
                Margin = new Thickness(0, 1, 0, 1),
                Cursor = Cursors.Hand,
                Child = grid
            };

            row.MouseEnter += (_, _) => row.SetResourceReference(Border.BackgroundProperty, "HoverOverlay");
            row.MouseLeave += (_, _) => row.Background = Brushes.Transparent;
            row.MouseLeftButtonDown += (_, e) =>
            {
                e.Handled = true;
                row.SetResourceReference(Border.BackgroundProperty, "PressedOverlay");
            };
            row.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                onClick();
            };

            return row;
        }

        private static Border CreateSeparator()
        {
            var sep = new Border
            {
                Height = 1,
                Margin = new Thickness(8, 4, 8, 4)
            };
            sep.SetResourceReference(Border.BackgroundProperty, "BorderSubtle");
            return sep;
        }

        private static void ForceForeground(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return;
            if (SetForegroundWindow(hwnd) && GetForegroundWindow() == hwnd) return;

            var fgHwnd = GetForegroundWindow();
            var fgTid = fgHwnd != IntPtr.Zero ? GetWindowThreadProcessId(fgHwnd, out _) : 0;
            var curTid = GetCurrentThreadId();

            var attached = false;
            try
            {
                if (fgTid != 0 && fgTid != curTid)
                    attached = AttachThreadInput(fgTid, curTid, true);

                SetForegroundWindow(hwnd);
                BringWindowToTop(hwnd);
                SetFocus(hwnd);
            }
            finally
            {
                if (attached)
                    AttachThreadInput(fgTid, curTid, false);
            }
        }
    }

    private const uint MonitorDefaultToNearest = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MONITORINFO
    {
        public uint cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public uint uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIconW(uint dwMessage, ref NotifyIconData lpData);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconExW(string lpszFile, int nIconIndex, IntPtr phiconLarge, out IntPtr phiconSmall, uint nIcons);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadIconW(IntPtr hInstance, IntPtr lpIconName);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint RegisterWindowMessage(string lpString);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfoW(IntPtr hMonitor, ref MONITORINFO lpmi);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool BringWindowToTop(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr SetFocus(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);
}
