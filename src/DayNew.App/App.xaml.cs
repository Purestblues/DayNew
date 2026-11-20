using System.Runtime.InteropServices;
using System.Windows;
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

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _instanceMutex = new Mutex(initiallyOwned: true, MutexName, out var isFirstInstance);

        if (!isFirstInstance)
        {
            ActivateExistingWindow();
            Shutdown();
            return;
        }

        ThemeService.ApplyInitial();
        var window = new MainWindow();
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // 别把最后一次改动丢在内存里
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

    /// <summary>已经开着就把那个窗口拉到前面，而不是再开一个。</summary>
    private static void ActivateExistingWindow()
    {
        var handle = FindWindow(null, WindowTitle);
        if (handle == IntPtr.Zero) return;

        if (IsIconic(handle)) ShowWindow(handle, SwRestore);
        SetForegroundWindow(handle);
    }

    private const int SwRestore = 9;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? className, string windowName);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr handle);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr handle, int command);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr handle);
}
