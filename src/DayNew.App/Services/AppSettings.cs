using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using DayNew.Core;

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

    public bool DailyDigestEnabled { get; set; } = true;

    /// <summary>新建日程/快捷卡片时的默认提醒节点组合；空字符串 = 默认不提醒。</summary>
    public string DefaultRemind { get; set; } = RemindRules.Default;

    /// <summary>yyyy-MM-dd；记录「今日待办摘要」已推送的日期，当日不再重复检测。</summary>
    public string LastDigestDate { get; set; } = "";

    /// <summary>本机设备唯一标识（首次加载自动生成，供多设备同步区分来源，不外泄敏感信息）。</summary>
    public string DeviceId { get; set; } = "";

    public string WebDavUrl { get; set; } = "https://dav.jianguoyun.com/dav/DayNew/";

    public string WebDavUser { get; set; } = "";

    /// <summary>经 Windows DPAPI 当前用户加密后的 Base64 密码串，绝不明文落盘。</summary>
    public string WebDavPasswordProtected { get; set; } = "";

    public string LastWebDavBackupAt { get; set; } = "";

    public bool AutoSyncEnabled { get; set; }

    public string LastWebDavSyncAt { get; set; } = "";

    /// <summary>上次已上传的本机状态指纹，用于避免空跑重复 PUT 上传。</summary>
    public string LastUploadedFingerprint { get; set; } = "";

    /// <summary>各远端设备快照文件（device-*.json.gz）上次拉取时的 ETag 缓存。</summary>
    public Dictionary<string, string> PeerEtags { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>各远端设备（DeviceId）上次成功合并时的最大 HLC 水位线，用于精确判定并发冲突。</summary>
    public Dictionary<string, string> PeerWatermarks { get; set; } = new(StringComparer.Ordinal);

    public const int DefaultMcpPort = 37092;

    public bool McpEnabled { get; set; }

    public int McpPort { get; set; } = DefaultMcpPort;

    /// <summary>经 Windows DPAPI 当前用户加密后的 MCP Bearer Token，绝不明文落盘。</summary>
    public string McpTokenProtected { get; set; } = "";

    public static string SettingsPath => Path.Combine(StoreFile.Folder, "settings.json");

    public string GetWebDavPassword() => UnprotectString(WebDavPasswordProtected);

    public void SetWebDavPassword(string? plain) => WebDavPasswordProtected = ProtectString(plain);

    public string GetMcpToken()
    {
        var token = UnprotectString(McpTokenProtected);
        if (!string.IsNullOrWhiteSpace(token)) return token;
        return RegenerateMcpToken();
    }

    public string RegenerateMcpToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(18);
        var token = "dn_" + Convert.ToHexString(bytes).ToLowerInvariant();
        McpTokenProtected = ProtectString(token);
        Save();
        return token;
    }

    private static string ProtectString(string? plain)
    {
        if (string.IsNullOrEmpty(plain)) return "";
        var plainBytes = Encoding.UTF8.GetBytes(plain);
        var encrypted = ProtectedData.Protect(plainBytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(encrypted);
    }

    private static string UnprotectString(string? protectedBase64)
    {
        if (string.IsNullOrWhiteSpace(protectedBase64)) return "";
        try
        {
            var encrypted = Convert.FromBase64String(protectedBase64);
            var plainBytes = ProtectedData.Unprotect(encrypted, optionalEntropy: null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plainBytes);
        }
        catch
        {
            return "";
        }
    }

    public static AppSettings Load()
    {
        lock (FileLock)
        {
            try
            {
                AppSettings loaded;
                var needSave = false;

                if (!File.Exists(SettingsPath))
                {
                    loaded = new AppSettings();
                    needSave = true;
                }
                else
                {
                    var json = File.ReadAllText(SettingsPath);
                    loaded = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
                }

                loaded.DefaultRemind = loaded.DefaultRemind is null
                    ? RemindRules.Default
                    : RemindRules.Normalize(loaded.DefaultRemind);

                if (string.IsNullOrWhiteSpace(loaded.WebDavUrl))
                    loaded.WebDavUrl = "https://dav.jianguoyun.com/dav/DayNew/";
                loaded.WebDavUser ??= "";
                loaded.WebDavPasswordProtected ??= "";
                loaded.LastWebDavBackupAt ??= "";
                loaded.LastWebDavSyncAt ??= "";
                loaded.LastUploadedFingerprint ??= "";
                loaded.PeerEtags = loaded.PeerEtags is null
                    ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, string>(loaded.PeerEtags, StringComparer.OrdinalIgnoreCase);
                loaded.PeerWatermarks = loaded.PeerWatermarks is null
                    ? new Dictionary<string, string>(StringComparer.Ordinal)
                    : new Dictionary<string, string>(loaded.PeerWatermarks, StringComparer.Ordinal);

                if (loaded.McpPort is < 1024 or > 65535)
                    loaded.McpPort = DefaultMcpPort;
                loaded.McpTokenProtected ??= "";

                if (string.IsNullOrWhiteSpace(loaded.DeviceId))
                {
                    loaded.DeviceId = "win-" + Guid.NewGuid().ToString("N")[..6];
                    needSave = true;
                }

                HlcClock.SetDefaultDeviceId(loaded.DeviceId);

                if (needSave)
                    SaveUnsafe(loaded);

                return loaded;
            }
            catch
            {
                var fallbackId = "win-" + Guid.NewGuid().ToString("N")[..6];
                HlcClock.SetDefaultDeviceId(fallbackId);
                return new AppSettings
                {
                    DeviceId = fallbackId
                };
            }
        }
    }

    public void Save()
    {
        lock (FileLock)
        {
            SaveUnsafe(this);
        }
    }

    private static void SaveUnsafe(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(StoreFile.Folder);
            var tmp = SettingsPath + ".tmp";
            var json = JsonSerializer.Serialize(settings, JsonOptions);
            File.WriteAllText(tmp, json);
            File.Move(tmp, SettingsPath, overwrite: true);
        }
        catch
        {
            // 偏好写盘失败不能影响主流程
        }
    }
}
