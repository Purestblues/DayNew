using System.Collections.ObjectModel;
using DayNew.ViewModels;

namespace DayNew.Services;

/// <summary>
/// 全库唯一的存储。底层只有三类实体：
///   日程 Schedule（TaskVM）、目标 Goal（GoalVM）、标签 Tag（TagVM）。
///
/// 约定：**任何页面都只读这里，不复制实体**。界面上的档位列、目标列、时间轴、
/// 四象限、标签页里出现的都是同一批对象引用，改一处所有地方同步。
/// 不要在这三个集合之外再维护一份"任务列表"。
/// </summary>
public sealed class DayStore
{
    public List<TaskVM> Schedules { get; } = new();

    public ObservableCollection<GoalVM> Goals { get; } = new();

    public ObservableCollection<TagVM> Tags { get; } = new();

    private int _scheduleOrder;
    private int _goalColorIndex;
    private int _tagColorIndex;

    // ---------------- 标签 ----------------

    public TagVM? FindTag(string name)
    {
        var trimmed = (name ?? "").Trim().TrimStart('#');
        if (trimmed.Length == 0) return null;

        return Tags.FirstOrDefault(t => string.Equals(t.Name, trimmed, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>按名字取标签，没有就建一个。快速录入里的 #标签 走这里。</summary>
    public TagVM GetOrCreateTag(string name)
    {
        var existing = FindTag(name);
        if (existing is not null) return existing;

        var tag = new TagVM(name) { Color = NextTagColor() };
        Tags.Add(tag);
        return tag;
    }

    /// <summary>删标签：先从所有日程上摘掉，再删实体。</summary>
    public void RemoveTag(TagVM tag)
    {
        foreach (var schedule in Schedules) schedule.Tags.Remove(tag);
        Tags.Remove(tag);
    }

    // ---------------- 目标 ----------------

    public GoalVM AddGoal(string title)
    {
        var goal = new GoalVM { Title = title, Color = NextGoalColor() };
        Goals.Add(goal);
        return goal;
    }

    /// <summary>目标配色，取自 Nord Frost 的分类色板；与主题无关，深浅底都看得清。</summary>
    private static readonly string[] GoalPalette =
    {
        "#527CE6", "#C8834B", "#4D968E", "#B85D82", "#8DA101", "#7E6BC4",
        "#BF616A", "#5E81AC", "#D08770", "#9D7298", "#689D6A", "#4C566A"
    };

    private System.Windows.Media.SolidColorBrush NextGoalColor()
    {
        var hex = GoalPalette[_goalColorIndex++ % GoalPalette.Length];
        return BrushFromHex(hex);
    }

    /// <summary>标签配色：从目标配色的后半段开始取，减少跟目标撞色。</summary>
    private System.Windows.Media.SolidColorBrush NextTagColor() => TagPaletteColor(_tagColorIndex++);

    public static System.Windows.Media.SolidColorBrush TagPaletteColor(int index)
    {
        var offset = GoalPalette.Length / 2;
        return BrushFromHex(GoalPalette[(offset + index) % GoalPalette.Length]);
    }

    public static System.Windows.Media.SolidColorBrush BrushFromHex(string hex)
    {
        try
        {
            var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex)!;
            var brush = new System.Windows.Media.SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }
        catch
        {
            var fallback = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0x52, 0x7C, 0xE6));
            fallback.Freeze();
            return fallback;
        }
    }

    // ---------------- 日程 ----------------

    public TaskVM AddSchedule(Action<TaskVM>? configure = null)
    {
        var schedule = new TaskVM { Order = (++_scheduleOrder) * 1024 };
        configure?.Invoke(schedule);
        Schedules.Add(schedule);
        return schedule;
    }

    public void RemoveSchedule(TaskVM schedule) => Schedules.Remove(schedule);

    /// <summary>把顺序值重新压紧成 1024、2048……方便后续插入。</summary>
    public void Reindex(IEnumerable<TaskVM> ordered)
    {
        var i = 0;
        foreach (var schedule in ordered) schedule.Order = ++i * 1024;
        _scheduleOrder = Math.Max(_scheduleOrder, i);
    }
}
