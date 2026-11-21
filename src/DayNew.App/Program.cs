using DayNew.Services;
using Microsoft.Toolkit.Uwp.Notifications;
using Velopack;

namespace DayNew;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // 必须在 WPF Application 和单实例 Mutex 之前运行，
        // 这样 Velopack 的安装/更新/卸载命令行钩子可以静默完成并直接退出。
        VelopackApp.Build()
            .OnBeforeUninstallFastCallback(_ =>
            {
                AutoStartService.SetEnabled(false);
                try { ToastNotificationManagerCompat.Uninstall(); } catch { }
            })
            .Run();

        App.StartupSilent = args.Any(a => string.Equals(a, "--tray", StringComparison.OrdinalIgnoreCase));

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
