using System.Globalization;

namespace DayNew.Core;

/// <summary>
/// 截止日期与可选具体时间点（HH:mm）的统一解析、格式化与分钟级逾期判定规则。
/// 落盘格式复用原有 Due 字符串：无时间点存 "yyyy-MM-dd"，有时间点存 "yyyy-MM-dd HH:mm"。
/// </summary>
public static class DeadlineRules
{
    public static string FormatClock(TimeSpan time)
    {
        var totalMins = Math.Clamp((int)Math.Round(time.TotalMinutes), 0, 23 * 60 + 59);
        return $"{totalMins / 60:00}:{totalMins % 60:00}";
    }

    /// <summary>支持 "9"、"18"、"9:30"、"18:00" 与全角冒号 "18：30"，有效范围 00:00..23:59。</summary>
    public static bool TryParseClock(string? text, out TimeSpan time)
    {
        time = default;
        var trimmed = (text ?? "").Trim().Replace('：', ':');
        if (trimmed.Length == 0) return false;

        if (trimmed.Contains(':'))
        {
            var parts = trimmed.Split(':');
            if (parts.Length != 2) return false;
            if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var hour)) return false;
            if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var minute)) return false;
            if (hour is < 0 or > 23 || minute is < 0 or > 59) return false;
            time = new TimeSpan(hour, minute, 0);
            return true;
        }

        if (int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var wholeHour) &&
            wholeHour is >= 0 and <= 23)
        {
            time = new TimeSpan(wholeHour, 0, 0);
            return true;
        }

        return false;
    }

    public static string? FormatDue(DateTime? due, TimeSpan? dueTime)
    {
        if (due is not { } d) return null;
        return dueTime is { } t
            ? $"{d:yyyy-MM-dd} {FormatClock(t)}"
            : d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    public static (DateTime? Date, TimeSpan? DueTime) ParseDue(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return (null, null);

        var trimmed = raw.Trim();
        var sepIdx = trimmed.IndexOfAny([' ', 'T']);
        if (sepIdx > 0)
        {
            var datePart = trimmed[..sepIdx].Trim();
            var timePart = trimmed[(sepIdx + 1)..].Trim();
            if (DateTime.TryParse(datePart, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateWithTime))
            {
                if (timePart.Length == 0)
                    return (dateWithTime.Date, null);

                if (TryParseClock(timePart, out var parsedTime))
                    return (dateWithTime.Date, parsedTime);

                return (null, null);
            }
        }

        if (DateTime.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateOnly))
            return (dateOnly.Date, null);

        if (DateTime.TryParse(trimmed, out var fallbackDate))
            return (fallbackDate.Date, null);

        return (null, null);
    }

    public static bool IsOverdue(DateTime? due, TimeSpan? dueTime, DateTime? nowOverride = null)
    {
        if (due is not { } d) return false;
        var now = nowOverride ?? DateTime.Now;
        var dueDate = d.Date;
        if (dueDate < now.Date) return true;
        if (dueDate == now.Date && dueTime is { } t)
            return now.TimeOfDay >= t;
        return false;
    }
}
