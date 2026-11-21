using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace DayNew.Services;

/// <summary>
/// 系统托盘常驻服务：显示托盘图标、动态悬浮提示，以及匹配当前主题的 WPF 右键菜单。
/// </summary>
public sealed class TrayService : IDisposable
{
    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly Drawing.Icon? _icon;
    private readonly ContextMenu _menu;
    private readonly MenuItem _notifyItem;
    private readonly MenuItem _autoStartItem;

    private readonly Action _onOpen;
    private readonly Action _onToggleWindow;
    private readonly Func<bool> _getNotifyEnabled;
    private readonly Action<bool> _setNotifyEnabled;
    private readonly Func<bool> _getAutoStartEnabled;
    private readonly Action<bool> _setAutoStartEnabled;
    private readonly Action _onExit;

    private bool _disposed;

    public TrayService(
        Action onOpen,
        Action onToggleWindow,
        Func<bool> getNotifyEnabled,
        Action<bool> setNotifyEnabled,
        Func<bool> getAutoStartEnabled,
        Action<bool> setAutoStartEnabled,
        Action onExit)
    {
        _onOpen = onOpen;
        _onToggleWindow = onToggleWindow;
        _getNotifyEnabled = getNotifyEnabled;
        _setNotifyEnabled = setNotifyEnabled;
        _getAutoStartEnabled = getAutoStartEnabled;
        _setAutoStartEnabled = setAutoStartEnabled;
        _onExit = onExit;

        var exePath = Environment.ProcessPath;
        _icon = !string.IsNullOrEmpty(exePath) ? Drawing.Icon.ExtractAssociatedIcon(exePath) : null;

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

        _notifyIcon = new Forms.NotifyIcon
        {
            Icon = _icon ?? Drawing.SystemIcons.Application,
            Text = "日新 DayNew",
            Visible = true
        };

        _notifyIcon.MouseClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left)
                Application.Current?.Dispatcher.BeginInvoke(_onToggleWindow);
            else if (e.Button == Forms.MouseButtons.Right)
                Application.Current?.Dispatcher.BeginInvoke(ShowMenu);
        };

        _notifyIcon.MouseDoubleClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left)
                Application.Current?.Dispatcher.BeginInvoke(_onOpen);
        };
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
        _notifyIcon.Text = text;
    }

    private void ShowMenu()
    {
        if (_disposed) return;
        _notifyItem.IsChecked = _getNotifyEnabled();
        _autoStartItem.IsChecked = _getAutoStartEnabled();
        _menu.IsOpen = false;
        _menu.IsOpen = true;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _icon?.Dispose();
    }

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
}
