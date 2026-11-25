using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using DayNew.Core;
using DayNew.Services;
using DayNew.ViewModels;
using DayNew.Views;

namespace DayNew;

public partial class App : Application
{
    /// <summary>
    /// 单实例保护。两个进程同时开着会各自写 data.json，后写的把先写的盖掉——
    /// 这正是"数据会丢"最容易发生的方式，比断电常见得多。
    /// </summary>
    private Mutex? _instanceMutex;

    private const string MutexName = @"Local\DayNew.SingleInstance";
    private const string WindowTitle = "日新 DayNew";
    private const string WakeupWindowTitle = "DayNew.WakeupHost";

    private static string ResolveMutexName()
    {
        var customDir = Environment.GetEnvironmentVariable("DAYNEW_DATA_DIR");
        if (string.IsNullOrWhiteSpace(customDir)) return MutexName;
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(customDir.Trim().ToLowerInvariant())))[..8];
        return $"{MutexName}.{hash}";
    }

    private static string ResolveWakeupTitle()
    {
        var customDir = Environment.GetEnvironmentVariable("DAYNEW_DATA_DIR");
        if (string.IsNullOrWhiteSpace(customDir)) return WakeupWindowTitle;
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(customDir.Trim().ToLowerInvariant())))[..8];
        return $"{WakeupWindowTitle}.{hash}";
    }

    public static bool StartupSilent { get; set; }

    public static readonly uint WmShowMainWindow = RegisterWindowMessage("DayNew.ShowMainWindow");

    private MainViewModel? _vm;
    private Win32MessageWindow? _wakeupWindow;
    private GlobalHotKeyService? _hotKeys;
    private TrayService? _tray;
    private NotificationService? _notifications;
    private McpServerService? _mcpServer;
    private FocusMiniWindow? _focusMini;
    private DispatcherTimer? _trimTimer;
    private Rect? _savedWindowBounds;
    private WindowState? _savedWindowState;
    private bool _exiting;
    private bool _cleanedUp;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += (_, args) => LogError("DispatcherUnhandledException", args.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex) LogError("UnhandledException", ex);
        };

        _instanceMutex = new Mutex(initiallyOwned: true, ResolveMutexName(), out var isFirstInstance);

        if (!isFirstInstance)
        {
            if (!StartupSilent) ActivateExistingWindow();
            Shutdown();
            return;
        }

        try
        {
            ThemeService.ApplyInitial();

            _vm = new MainViewModel(deferUiBuild: StartupSilent);

            _wakeupWindow = new Win32MessageWindow(ResolveWakeupTitle(), HandleWakeupMessage);

            _hotKeys = new GlobalHotKeyService(_wakeupWindow.Handle, OnGlobalHotKeyTriggered);
            _vm.AttachHotKeyService(_hotKeys);

            _tray = new TrayService(
                _wakeupWindow.Handle,
                onOpen: ShowMainWindow,
                onToggleWindow: ToggleMainWindow,
                getNotifyEnabled: () => _vm.NotifyEnabled,
                setNotifyEnabled: v => _vm.NotifyEnabled = v,
                getAutoStartEnabled: () => _vm.AutoStartEnabled,
                setAutoStartEnabled: v => _vm.AutoStartEnabled = v,
                onExit: ExitApplication);

            _notifications = new NotificationService(
                _vm.Settings,
                () => _vm.AllSchedules,
                onOpenWindow: ShowMainWindow,
                onCompleteTask: id =>
                {
                    if (_vm.CompleteTaskById(id)) _vm.FlushSave();
                },
                onExtendFocus: mins => _vm.Focus.ExtendMinutes(mins));

            _vm.TaskTouched += _notifications.ResetTaskFires;
            _vm.SummaryChanged += _tray.UpdateTooltip;

            _vm.Focus.SessionStarted += () =>
            {
                _tray?.UpdateFocusOverride(_vm.Focus.TrayStatusText);
                if (_vm.FocusAutoOpenMini) ShowFocusMini();
            };
            _vm.Focus.SessionEnded += () => _tray?.UpdateFocusOverride(null);
            _vm.Focus.Ticked += () => _tray?.UpdateFocusOverride(_vm.Focus.TrayStatusText);
            _vm.Focus.CountdownFinished += (title, taskId) => _notifications?.ShowFocusFinishedNotification(title, taskId);
            _vm.Focus.FreeFocusCompleted += (title, date, startMin, endMin) =>
            {
                ShowMainWindow();
                _vm.OpenScheduleFromFreeFocus(title, date, startMin, endMin);
                (MainWindow as MainWindow)?.FocusEditorTitleFromExternal();
            };

            _mcpServer = new McpServerService(_vm, Dispatcher);
            _vm.AttachMcpServer(_mcpServer);
            _mcpServer.StartOrRestart();

            var (todayPending, overdueCount) = _vm.GetSummaryCounts();
            _tray.UpdateTooltip(todayPending, overdueCount);
            if (_vm.Focus.IsActive)
                _tray.UpdateFocusOverride(_vm.Focus.TrayStatusText);

            _trimTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
            _trimTimer.Tick += (_, _) =>
            {
                _trimTimer.Stop();
                if (MainWindow is null && !_exiting)
                    TrimMemoryInBackground();
            };

            if (StartupSilent)
            {
                // 无头托盘静默常驻：不创建 MainWindow，也不触发 WPF 渲染管线，只做启动后一次内存修剪
                _trimTimer.Start();
            }
            else
            {
                ShowMainWindow();
            }
        }
        catch (Exception ex)
        {
            LogError("OnStartup", ex);
            Shutdown(1);
        }
    }

    private IntPtr? HandleWakeupMessage(uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WmShowMainWindow)
        {
            Dispatcher.BeginInvoke(ShowMainWindow);
            return IntPtr.Zero;
        }

        if (msg == GlobalHotKeyService.WmHotKey && _hotKeys?.HandleWindowMessage(msg, wParam) == true)
            return IntPtr.Zero;

        if (_tray?.HandleWindowMessage(msg, lParam) == true)
            return IntPtr.Zero;

        return null;
    }

    private void OnGlobalHotKeyTriggered(string actionId)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (_exiting || _vm is null) return;

            switch (actionId)
            {
                case ShortcutIds.GlobalToggleWindow:
                    ToggleMainWindow();
                    break;
                case ShortcutIds.GlobalOpenSearch:
                    ShowMainWindow();
                    (MainWindow as MainWindow)?.OpenSearchFromExternal();
                    break;
                case ShortcutIds.GlobalNewSchedule:
                    ShowMainWindow();
                    (MainWindow as MainWindow)?.OpenQuickNewFromExternal();
                    break;
                case ShortcutIds.GlobalToggleFocusMini:
                    ToggleFocusMini();
                    break;
            }
        });
    }

    public void ShowFocusMini()
    {
        if (_exiting || _vm is null) return;

        if (_focusMini is null)
        {
            _focusMini = new FocusMiniWindow(
                _vm.Focus,
                onOpenMain: ShowMainWindow,
                onHide: HideFocusMini);
            _focusMini.Closed += (_, _) => _focusMini = null;
        }

        _focusMini.EnsureDefaultPosition();
        if (!_focusMini.IsVisible) _focusMini.Show();
    }

    public void HideFocusMini()
    {
        if (_focusMini is { IsVisible: true })
            _focusMini.Hide();
    }

    public void ToggleFocusMini()
    {
        if (_focusMini is { IsVisible: true }) HideFocusMini();
        else ShowFocusMini();
    }

    public void ShowMainWindow()
    {
        if (_exiting || _vm is null || _notifications is null) return;

        _trimTimer?.Stop();
        _vm.EnsureUiReady();

        if (MainWindow is MainWindow existing)
        {
            existing.ShowFromTray();
            return;
        }

        var mw = new MainWindow(
            _vm,
            onHideToTray: HideMainWindowToTray,
            onExitApp: ExitApplication,
            onTestNotify: () => _notifications.SendTestNotification(),
            onToggleFocusMini: ToggleFocusMini,
            savedBounds: _savedWindowBounds,
            savedState: _savedWindowState);

        MainWindow = mw;
        mw.Closed += (_, _) => OnMainWindowClosed(mw);
        mw.Show();
        mw.Activate();
    }

    private void ToggleMainWindow()
    {
        if (MainWindow is MainWindow mw && mw.IsVisible && mw.WindowState != WindowState.Minimized && mw.IsActive)
            HideMainWindowToTray();
        else
            ShowMainWindow();
    }

    private void HideMainWindowToTray()
    {
        if (MainWindow is not MainWindow mw) return;
        CaptureWindowBounds(mw);
        mw.CloseToTray();
    }

    private void OnMainWindowClosed(MainWindow mw)
    {
        CaptureWindowBounds(mw);
        if (ReferenceEquals(MainWindow, mw))
            MainWindow = null;

        if (_exiting) return;

        if (_vm is { FocusKeepMiniOnCloseToTray: false })
            HideFocusMini();

        _vm?.ReleaseUiCaches();
        _trimTimer?.Stop();
        _trimTimer?.Start();
    }

    private void CaptureWindowBounds(MainWindow mw)
    {
        var bounds = mw.WindowState == WindowState.Normal
            ? new Rect(mw.Left, mw.Top, mw.Width, mw.Height)
            : mw.RestoreBounds;

        if (bounds.Width > 0 && bounds.Height > 0 && !double.IsNaN(bounds.Left) && !double.IsNaN(bounds.Top))
            _savedWindowBounds = bounds;

        _savedWindowState = mw.WindowState == WindowState.Minimized
            ? WindowState.Normal
            : mw.WindowState;
    }

    private void ExitApplication()
    {
        if (_exiting) return;
        _exiting = true;
        _trimTimer?.Stop();

        _focusMini?.Close();
        _focusMini = null;

        if (MainWindow is MainWindow mw)
            mw.ForceCloseForExit();

        Shutdown();
    }

    private static void TrimMemoryInBackground()
    {
        try
        {
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
            SetProcessWorkingSetSizeEx(GetCurrentProcess(), new IntPtr(-1), new IntPtr(-1), 0);
        }
        catch
        {
            // 内存修剪失败不影响后台常驻
        }
    }

    private static void LogError(string source, Exception ex)
    {
        try
        {
            Directory.CreateDirectory(StoreFile.Folder);
            File.WriteAllText(
                Path.Combine(StoreFile.Folder, "startup-error.txt"),
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{source}]\n{ex}\n");
        }
        catch
        {
            // 忽略日志写失败
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (!_cleanedUp)
        {
            _cleanedUp = true;
            _exiting = true;
            _trimTimer?.Stop();

            if (_vm is not null)
            {
                if (_notifications is not null) _vm.TaskTouched -= _notifications.ResetTaskFires;
                if (_tray is not null) _vm.SummaryChanged -= _tray.UpdateTooltip;
                _vm.StopBackgroundTimers();
                _vm.FlushSave();
            }

            _mcpServer?.Dispose();
            _hotKeys?.Dispose();
            _notifications?.Dispose();
            _tray?.Dispose();
            _wakeupWindow?.Dispose();
        }

        try
        {
            _instanceMutex?.ReleaseMutex();
        }
        catch
        {
            // 没拿到所有权就退出，忽略
        }

        _instanceMutex?.Dispose();
        base.OnExit(e);
    }

    /// <summary>已经开着（含无头驻留在托盘里）就把窗口拉到前面，而不是再开一个。</summary>
    private static void ActivateExistingWindow()
    {
        var handle = FindWindow(null, ResolveWakeupTitle());
        if (handle == IntPtr.Zero) handle = FindWindow(null, WindowTitle);
        if (handle == IntPtr.Zero) return;

        GetWindowThreadProcessId(handle, out var pid);
        if (pid != 0) AllowSetForegroundWindow(pid);

        PostMessage(handle, WmShowMainWindow, IntPtr.Zero, IntPtr.Zero);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint RegisterWindowMessage(string lpString);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? className, string windowName);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(uint dwProcessId);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetProcessWorkingSetSizeEx(
        IntPtr hProcess,
        IntPtr dwMinimumWorkingSetSize,
        IntPtr dwMaximumWorkingSetSize,
        uint flags);
}
