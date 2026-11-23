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
    private static readonly object FileLock = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public static string Path => System.IO.Path.Combine(StoreFile.Folder, "completions.ndjson");

    /// <summary>追加一行。失败就算了，不能因为记历史把程序带崩。</summary>
    public void Append(string scheduleId, DateTime? forDate, bool done)
    {
        try
        {
            lock (FileLock)
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
        }
        catch
        {
            // 记不下来不影响主流程
        }
    }

    /// <summary>读出所有事件（以后做统计用）。</summary>
    public IReadOnlyList<CompletionEntry> ReadAll()
    {
        lock (FileLock)
        {
            return ReadAllUnsafe();
        }
    }

    public static IReadOnlyList<CompletionEntry> ParseLines(string ndjsonText)
    {
        if (string.IsNullOrWhiteSpace(ndjsonText)) return Array.Empty<CompletionEntry>();

        var list = new List<CompletionEntry>();
        using var reader = new StringReader(ndjsonText);
        while (reader.ReadLine() is { } line)
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

    public static string FormatLines(IEnumerable<CompletionEntry> entries)
    {
        var lines = entries.Select(e => JsonSerializer.Serialize(e, JsonOptions)).ToList();
        return lines.Count == 0 ? "" : string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }

    /// <summary>覆盖导入时原子替换全部完成记录。</summary>
    public void ReplaceAll(IEnumerable<CompletionEntry> entries)
    {
        lock (FileLock)
        {
            WriteAllUnsafe(entries);
        }
    }

    /// <summary>合并导入时按事件签名去重追加，并保持按时间顺序排列，返回新增条数。</summary>
    public int MergeEntries(IEnumerable<CompletionEntry> incoming)
    {
        lock (FileLock)
        {
            var current = ReadAllUnsafe();
            var seen = new HashSet<(string Id, string For, string At, bool Done)>();
            var combined = new List<CompletionEntry>(current.Count);

            foreach (var item in current)
            {
                seen.Add(EntryKey(item));
                combined.Add(item);
            }

            var added = 0;
            foreach (var item in incoming)
            {
                if (seen.Add(EntryKey(item)))
                {
                    combined.Add(item);
                    added++;
                }
            }

            if (added == 0) return 0;

            var ordered = combined.OrderBy(e => e.At ?? "", StringComparer.Ordinal).ToList();
            WriteAllUnsafe(ordered);
            return added;
        }
    }

    private static (string Id, string For, string At, bool Done) EntryKey(CompletionEntry entry)
        => (entry.Id ?? "", entry.For ?? "", entry.At ?? "", entry.Done);

    private static List<CompletionEntry> ReadAllUnsafe()
    {
        if (!File.Exists(Path)) return new List<CompletionEntry>();

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

    private static void WriteAllUnsafe(IEnumerable<CompletionEntry> entries)
    {
        Directory.CreateDirectory(StoreFile.Folder);
        var tmp = Path + ".tmp";
        File.WriteAllText(tmp, FormatLines(entries));
        File.Move(tmp, Path, overwrite: true);
    }

    public sealed class CompletionEntry
    {
        public string Id { get; set; } = "";
        public string? For { get; set; }
        public string At { get; set; } = "";
        public bool Done { get; set; }
    }
}
