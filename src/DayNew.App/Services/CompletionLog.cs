using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace DayNew.Services;

/// <summary>
/// 完成记录。只追加、永不重写：一行一次事件。
///
/// 为什么不塞进主文件：重复日程如果每次完成都写一条完整日程，
/// 5 个每天的习惯十年就是 1.8 万条，主文件会被历史撑爆。
/// 这里一行只有「哪条、属于哪天、什么时候做的、是勾上还是取消」，约 120 字节。
///
/// 取消勾选也记一行（done=false），按时间顺序读最后一条就知道当前状态——
/// 这样误点一下再取消，历史仍然是诚实的。
/// </summary>
public sealed class CompletionLog
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static string Path => System.IO.Path.Combine(StoreFile.Folder, "completions.ndjson");

    /// <summary>追加一行。失败就算了，不能因为记历史把程序带崩。</summary>
    public void Append(string scheduleId, DateTime? forDate, bool done)
    {
        try
        {
            Directory.CreateDirectory(StoreFile.Folder);

            var entry = new CompletionEntry
            {
                Id = scheduleId,
                For = forDate?.ToString("yyyy-MM-dd"),
                At = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                Done = done
            };

            File.AppendAllText(Path, JsonSerializer.Serialize(entry, JsonOptions) + Environment.NewLine);
        }
        catch
        {
            // 记不下来不影响主流程
        }
    }

    /// <summary>读出所有事件（以后做统计用）。</summary>
    public IReadOnlyList<CompletionEntry> ReadAll()
    {
        if (!File.Exists(Path)) return Array.Empty<CompletionEntry>();

        var list = new List<CompletionEntry>();
        foreach (var line in File.ReadLines(Path))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            try
            {
                var entry = JsonSerializer.Deserialize<CompletionEntry>(line, JsonOptions);
                if (entry is not null) list.Add(entry);
            }
            catch
            {
                // 坏行跳过，不影响其它记录
            }
        }

        return list;
    }

    public sealed class CompletionEntry
    {
        public string Id { get; set; } = "";
        public string? For { get; set; }
        public string At { get; set; } = "";
        public bool Done { get; set; }
    }
}
