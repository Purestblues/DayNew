using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;

namespace DayNew.Services;

/// <summary>
/// 纯 Win32 Shell_NotifyIconW 托盘常驻服务（无 WinForms 依赖）：
/// 显示托盘图标、动态悬浮提示，以及匹配当前主题的 WPF 右键菜单。
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

    private ContextMenu? _menu;
    private MenuItem? _notifyItem;
    private MenuItem? _autoStartItem;

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
                Application.Current?.Dispatcher.BeginInvoke(ShowMenu);
                return true;
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
        _tooltip = text;

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

    private void EnsureMenu()
    {
        if (_menu is not null) return;

        _notifyItem = new MenuItem { Header = "系统通知", IsCheckable = true };
        _notifyItem.Click += (_, _) => _setNotifyEnabled(_notifyItem.IsChecked);

        _autoStartItem = new MenuItem { Header = "开机静默自启", IsCheckable = true };
        _autoStartItem.Click += (_, _) => _setAutoStartEnabled(_autoStartItem.IsChecked);

        var openItem = new MenuItem { Header = "打开日新", FontWeight = FontWeights.SemiBold };
        openItem.Click += (_, _) => _onOpen();

        var exitItem = new MenuItem { Header = "退出日新" };
        exitItem.Click += (_, _) => _onExit();

        _menu = new ContextMenu
        {
            Placement = PlacementMode.MousePoint,
            HasDropShadow = true
        };
        _menu.Items.Add(openItem);
        _menu.Items.Add(new Separator());
        _menu.Items.Add(_notifyItem);
        _menu.Items.Add(_autoStartItem);
        _menu.Items.Add(new Separator());
        _menu.Items.Add(exitItem);

        // 让托盘弹出的 WPF ContextMenu 获取前台焦点，点击菜单外区域即可自动收起
        _menu.Opened += (_, _) =>
        {
            if (PresentationSource.FromVisual(_menu) is HwndSource src)
                SetForegroundWindow(src.Handle);
        };
    }

    private void ShowMenu()
    {
        if (_disposed) return;
        EnsureMenu();
        _notifyItem!.IsChecked = _getNotifyEnabled();
        _autoStartItem!.IsChecked = _getAutoStartEnabled();

        SetForegroundWindow(_hwnd);
        _menu!.IsOpen = false;
        _menu.IsOpen = true;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        var nid = CreateIconData(0);
        Shell_NotifyIconW(NimDelete, ref nid);

        if (_ownsIcon && _hIcon != IntPtr.Zero)
            DestroyIcon(_hIcon);
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
    private static extern bool SetForegroundWindow(IntPtr hWnd);
}
