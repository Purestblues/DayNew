using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace DayNew.Services;

/// <summary>
/// 应用级偏好与推送状态，单独存放在 %LOCALAPPDATA%\DayNew\settings.json，
/// 不污染业务数据 data.json 的文件契约。
/// </summary>
public sealed class AppSettings
{
    private static readonly object FileLock = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public bool CloseToTray { get; set; } = true;

    public bool NotifyEnabled { get; set; } = true;

    /// <summary>yyyy-MM-dd；记录「今日待办摘要」已推送的日期，当日不再重复检测。</summary>
    public string LastDigestDate { get; set; } = "";

    public static string SettingsPath => Path.Combine(StoreFile.Folder, "settings.json");

    public static AppSettings Load()
    {
        lock (FileLock)
        {
            try
            {
                if (!File.Exists(SettingsPath)) return new AppSettings();
                var json = File.ReadAllText(SettingsPath);
                return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
            }
            catch
            {
                return new AppSettings();
            }
        }
    }

    public void Save()
    {
        lock (FileLock)
        {
            try
            {
                Directory.CreateDirectory(StoreFile.Folder);
                var tmp = SettingsPath + ".tmp";
                var json = JsonSerializer.Serialize(this, JsonOptions);
                File.WriteAllText(tmp, json);
                File.Move(tmp, SettingsPath, overwrite: true);
            }
            catch
            {
                // 偏好写盘失败不能影响主流程
            }
        }
    }
}
