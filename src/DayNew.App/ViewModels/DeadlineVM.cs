using System.Collections.ObjectModel;
using System.Windows.Media;
using DayNew.Core;

namespace DayNew.ViewModels;

/// <summary>截止日期页里展示的一条目标或子目标。</summary>
public sealed class DeadlineGoalItemVM
{
    public DeadlineGoalItemVM(GoalVM goal, GoalNodeVM? node, DateTime due, TimeSpan? dueTime = null)
    {
        Goal = goal;
        Node = node;
        Due = due.Date;
        DueTime = dueTime;
    }

    public GoalVM Goal { get; }

    /// <summary>null 表示总目标本身；非 null 表示具体子目标节点。</summary>
    public GoalNodeVM? Node { get; }

    public DateTime Due { get; }

    public TimeSpan? DueTime { get; }

    public bool HasDueTime => DueTime.HasValue;

    public string DueTimeText => DueTime is { } dt ? $"{DeadlineRules.FormatClock(dt)} 截止" : "";

    public string Title => Node is null
        ? (string.IsNullOrWhiteSpace(Goal.Title) ? "未命名目标" : Goal.Title)
        : Node.DisplayTitle;

    public string SubLabel => Node is null
        ? "目标"
        : $"目标 · {(string.IsNullOrWhiteSpace(Goal.Title) ? "未命名目标" : Goal.Title)}";

    public Brush Color => Goal.Color;

    public bool IsOverdue => DeadlineRules.IsOverdue(Due, DueTime);

    public string DueBadgeText => GoalNodeVM.FormatDueBadge(Due, DueTime, isCompleted: false);

    public string ProgressText
    {
        get
        {
            if (Node is null)
            {
                if (Goal.Total > 0) return $"{Goal.DoneCount}/{Goal.Total}";
                if (Goal.HasSubGoals) return $"{Goal.DoneSubGoalCount}/{Goal.Nodes.Count} 子目标";
                return "";
            }

            return Node.TaskCount > 0 ? $"{Node.DoneTaskCount}/{Node.TaskCount}" : "";
        }
    }

    public bool HasProgress => !string.IsNullOrEmpty(ProgressText);
}

/// <summary>截止日期页里按天分的一组：可能同时包含仅设截止日的日程与设了截止日的目标/子目标。</summary>
public sealed class DeadlineDayGroupVM : ObservableObject
{
    private static readonly string[] WeekNames = { "日", "一", "二", "三", "四", "五", "六" };

    public DeadlineDayGroupVM(DateTime date)
    {
        Date = date.Date;
    }

    public DateTime Date { get; }

    public bool IsOverdue => Date < DateTime.Today;

    public bool IsToday => Date == DateTime.Today;

    public string HeaderText
    {
        get
        {
            var today = DateTime.Today;
            var diff = (Date - today).Days;
            var wk = WeekNames[(int)Date.DayOfWeek];
            return diff switch
            {
                0 => $"今天 · {Date:M月d日} 周{wk}",
                1 => $"明天 · {Date:M月d日} 周{wk}",
                -1 => $"昨天 · {Date:M月d日} 周{wk} · 已逾期",
                < -1 => $"{Date:M月d日} 周{wk} · 已逾期",
                _ => $"{Date:M月d日} 周{wk}"
            };
        }
    }

    public ObservableCollection<TaskVM> Tasks { get; } = new();

    public ObservableCollection<DeadlineGoalItemVM> Goals { get; } = new();

    public bool HasTasks => Tasks.Count > 0;

    public bool HasGoals => Goals.Count > 0;

    public int TotalCount => Tasks.Count + Goals.Count;
}
