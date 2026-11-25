namespace DayNew.Core;

/// <summary>
/// 基础重复规则计算：仅保留「每天 / 每周 / 每月」三档，按日历周期推进。
/// </summary>
public static class RepeatRules
{
    public const string Daily = "每天";
    public const string Weekly = "每周";
    public const string Monthly = "每月";

    public static string Normalize(string? value)
    {
        var trimmed = (value ?? "").Trim();
        return trimmed switch
        {
            Daily or "daily" or "day" => Daily,
            Weekly or "weekly" or "week" => Weekly,
            Monthly or "monthly" or "month" => Monthly,
            _ => ""
        };
    }

    public static bool HasRepeat(string? value) => Normalize(value).Length > 0;

    /// <summary>
    /// 计算下一期日期：
    /// - 未逾期（currentDue >= today）按原 Due + 1 个日历周期；
    /// - 已逾期（currentDue &lt; today）跳过已逝周期，推到以今天为基准的下一期（&gt; today）；
    /// - 无日期（null）以今天为基准推进 1 个周期。
    /// </summary>
    public static DateTime? NextOccurrence(DateTime? currentDue, string? repeat, DateTime? todayOverride = null)
    {
        var rule = Normalize(repeat);
        if (rule.Length == 0) return null;

        var today = (todayOverride ?? DateTime.Today).Date;
        var baseDate = (currentDue ?? today).Date;

        var next = StepOnce(baseDate, rule);
        if (baseDate < today)
        {
            while (next <= today)
                next = StepOnce(next, rule);
        }

        return next;
    }

    private static DateTime StepOnce(DateTime date, string rule) => rule switch
    {
        Daily => date.AddDays(1),
        Weekly => date.AddDays(7),
        Monthly => date.AddMonths(1),
        _ => date
    };
}
