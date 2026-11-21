using System.Diagnostics;
using Microsoft.Win32;

namespace DayNew.Services;

/// <summary>
/// 通过当前用户注册表 Run 键管理开机自启（无需管理员权限）。
/// 自启时附带 --tray 参数，静默常驻托盘而不弹出主窗口。
/// </summary>
public static class AutoStartService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private const string ValueName = "DayNew";

    public static bool IsEnabled
    {
        get
        {
            try
            {
                using var runKey = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
                if (runKey?.GetValue(ValueName) is not string cmd || string.IsNullOrWhiteSpace(cmd))
                    return false;

                // 若用户在任务管理器「启动」里手动禁用了它，Windows 会写入 StartupApproved（首字节非 0x02/偶数）
                using var approvedKey = Registry.CurrentUser.OpenSubKey(ApprovedKeyPath, writable: false);
                if (approvedKey?.GetValue(ValueName) is byte[] bytes && bytes.Length > 0)
                    return (bytes[0] & 0x01) == 0 && bytes[0] != 0x03;

                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    public static bool SetEnabled(bool enabled)
    {
        try
        {
            if (enabled)
            {
                var exePath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
                if (string.IsNullOrWhiteSpace(exePath)) return false;

                using var runKey = Registry.CurrentUser.CreateSubKey(RunKeyPath);
                runKey?.SetValue(ValueName, $"\"{exePath}\" --tray", RegistryValueKind.String);

                // 清理可能存在的禁用标记，确保设置页打开后一定生效
                using var approvedKey = Registry.CurrentUser.OpenSubKey(ApprovedKeyPath, writable: true);
                approvedKey?.DeleteValue(ValueName, throwOnMissingValue: false);
            }
            else
            {
                using var runKey = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
                runKey?.DeleteValue(ValueName, throwOnMissingValue: false);

                using var approvedKey = Registry.CurrentUser.OpenSubKey(ApprovedKeyPath, writable: true);
                approvedKey?.DeleteValue(ValueName, throwOnMissingValue: false);
            }

            return true;
        }
        catch
        {
            return false;
        }
    }
}
