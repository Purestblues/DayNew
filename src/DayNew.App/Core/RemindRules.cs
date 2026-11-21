namespace DayNew.Core;

public readonly record struct RemindTrigger(
    string Node,
    TimeSpan TargetTime,
    string ToastTitle,
    bool IsEnd);

/// <summary>
/// 全 App 唯一的相对提醒规则层：
///   1. 节点：pre15 / pre5 / start / end，按固定顺序逗号拼接落盘（空字符串 = 不提醒）；
///   2. 兼容迁移：旧版 "HH:mm" 字符串自动迁移为默认提醒节点组合；
///   3. 仅对具备起止时刻的日程计算触发时刻。
/// </summary>
public static class RemindRules
{
    public const string Pre15 = "pre15";
    public const string Pre5 = "pre5";
    public const string Start = "start";
    public const string End = "end";
    public const string None = "none";
    public const string Default = "pre5,start";

    private static readonly string[] OrderedNodes = { Pre15, Pre5, Start, End };

    public static string Normalize(string? raw, string legacyFallback = Default)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";

        var trimmed = raw.Trim();
        if (string.Equals(trimmed, None, StringComparison.OrdinalIgnoreCase)) return "";

        var active = ParseValidSet(trimmed);
        if (active.Count > 0)
            return JoinOrdered(active);

        if (IsLegacyClock(trimmed))
            return string.IsNullOrWhiteSpace(legacyFallback) ? "" : JoinOrdered(ParseValidSet(legacyFallback));

        return "";
    }

    public static bool HasAny(string? remind) => !string.IsNullOrEmpty(Normalize(remind));

    public static bool HasNode(string? remind, string node)
    {
        var normalized = Normalize(remind);
        if (normalized.Length == 0 || string.IsNullOrWhiteSpace(node)) return false;

        var target = node.Trim().ToLowerInvariant();
        foreach (var part in normalized.Split(','))
            if (part == target) return true;

        return false;
    }

    public static string ToggleNode(string? current, string node)
    {
        if (string.IsNullOrWhiteSpace(node)) return "";

        var target = node.Trim().ToLowerInvariant();
        if (target == None) return "";

        var active = ParseValidSet(Normalize(current));
        if (Array.IndexOf(OrderedNodes, target) < 0)
            return JoinOrdered(active);

        if (!active.Add(target))
            active.Remove(target);

        return JoinOrdered(active);
    }

    public static string FormatLabel(string? remind)
    {
        var normalized = Normalize(remind);
        if (normalized.Length == 0) return "不提醒";

        var parts = new List<string>(4);
        foreach (var node in normalized.Split(','))
        {
            parts.Add(node switch
            {
                Pre15 => "提前15分",
                Pre5 => "提前5分",
                Start => "开始时",
                End => "结束时",
                _ => node
            });
        }

        return string.Join(" · ", parts);
    }

    public static string FormatTooltip(string? remind, bool hasTime = true)
    {
        if (!hasTime) return "未设具体起止时刻，提醒不触发";

        var normalized = Normalize(remind);
        if (normalized.Length == 0) return "不提醒";

        var parts = new List<string>(4);
        foreach (var node in normalized.Split(','))
        {
            parts.Add(node switch
            {
                Pre15 => "提前 15 分钟",
                Pre5 => "提前 5 分钟",
                Start => "开始时",
                End => "结束时",
                _ => node
            });
        }

        return "提醒：" + string.Join(" · ", parts);
    }

    public static IReadOnlyList<RemindTrigger> GetTriggers(
        string? remind,
        TimeSpan startTime,
        TimeSpan endTime,
        string taskTitle)
    {
        var normalized = Normalize(remind);
        if (normalized.Length == 0) return Array.Empty<RemindTrigger>();

        var result = new List<RemindTrigger>(4);
        foreach (var node in normalized.Split(','))
        {
            switch (node)
            {
                case Pre15 when startTime >= TimeSpan.FromMinutes(15):
                    result.Add(new RemindTrigger(Pre15, startTime - TimeSpan.FromMinutes(15), $"15 分钟后开始 · {taskTitle}", false));
                    break;
                case Pre5 when startTime >= TimeSpan.FromMinutes(5):
                    result.Add(new RemindTrigger(Pre5, startTime - TimeSpan.FromMinutes(5), $"5 分钟后开始 · {taskTitle}", false));
                    break;
                case Start:
                    result.Add(new RemindTrigger(Start, startTime, $"现在开始 · {taskTitle}", false));
                    break;
                case End:
                    result.Add(new RemindTrigger(End, endTime, $"日程已结束 · {taskTitle}", true));
                    break;
            }
        }

        return result;
    }

    private static HashSet<string> ParseValidSet(string raw)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var token in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var lower = token.ToLowerInvariant();
            if (Array.IndexOf(OrderedNodes, lower) >= 0)
                set.Add(lower);
        }

        return set;
    }

    private static string JoinOrdered(HashSet<string> active)
    {
        if (active.Count == 0) return "";

        var ordered = new List<string>(4);
        foreach (var node in OrderedNodes)
            if (active.Contains(node)) ordered.Add(node);

        return string.Join(",", ordered);
    }

    private static bool IsLegacyClock(string text)
        => text.Contains(':') || TimeSpan.TryParse(text, out _);
}
