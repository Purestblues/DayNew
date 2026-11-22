using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
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

    public static bool StartupSilent { get; set; }

    public static readonly uint WmShowMainWindow = RegisterWindowMessage("DayNew.ShowMainWindow");

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += (_, args) => LogError("DispatcherUnhandledException", args.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex) LogError("UnhandledException", ex);
        };

        _instanceMutex = new Mutex(initiallyOwned: true, MutexName, out var isFirstInstance);

        if (!isFirstInstance)
        {
            if (!StartupSilent) ActivateExistingWindow();
            Shutdown();
            return;
        }

        try
        {
            ThemeService.ApplyInitial();
            var window = new MainWindow();
            MainWindow = window;

            if (StartupSilent)
            {
                // 只建 HWND 并挂载消息钩子，不弹窗，确保后台静默常驻且可被单实例再次唤醒
                new WindowInteropHelper(window).EnsureHandle();
            }
            else
            {
                window.Show();
            }
        }
        catch (Exception ex)
        {
            LogError("OnStartup", ex);
            Shutdown(1);
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
        if (MainWindow is MainWindow mw)
            mw.CleanupOnExit();
        else
            (MainWindow?.DataContext as MainViewModel)?.FlushSave();

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

    /// <summary>已经开着（含隐藏在托盘里）就把那个窗口拉到前面，而不是再开一个。</summary>
    private static void ActivateExistingWindow()
    {
        var handle = FindWindow(null, WindowTitle);
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
}
