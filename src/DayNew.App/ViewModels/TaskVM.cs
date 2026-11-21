using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using DayNew.Core;

namespace DayNew.ViewModels;

public sealed class TaskVM : ObservableObject
{
    /// <summary>稳定标识。现在只用来落盘，以后同步/合并靠它认“同一条”。</summary>
    public string Id
    {
        get => _id;
        set { if (!string.IsNullOrWhiteSpace(value)) _id = value; }
    }

    private string _id = Guid.NewGuid().ToString("N")[..10];

    private string _title = "";
    private string _note = "";
    private bool _done;
    private bool _isExpanded;
    private DateTime? _due;
    private string _remind = "";
    private string _repeat = "";
    private GoalVM? _goal;
    private bool _isUrgent;
    private bool _isImportant;
    private TimeSpan? _startTime;
    private TimeSpan? _endTime;

    public TaskVM()
    {
        Tags.CollectionChanged += OnTagsChanged;
    }

    public string Title
    {
        get => _title;
        set { if (Set(ref _title, value)) RaiseMeta(); }
    }

    public string Note
    {
        get => _note;
        set => Set(ref _note, value);
    }

    public bool Done
    {
        get => _done;
        set { if (Set(ref _done, value)) RaiseMeta(); }
    }

    public bool IsExpanded
    {
        get => _isExpanded;
        set => Set(ref _isExpanded, value);
    }

    /// <summary>为空 = 「某天」档。档位本身不存字段，由它和今天的关系算出来。</summary>
    public DateTime? Due
    {
        get => _due;
        set { if (Set(ref _due, value)) RaiseMeta(); }
    }

    /// <summary>相对起止时刻的提醒节点组合（逗号分隔），空 = 不提醒。</summary>
    public string Remind
    {
        get => _remind;
        set
        {
            var normalized = RemindRules.Normalize(value);
            if (Set(ref _remind, normalized)) RaiseMeta();
        }
    }

    /// <summary>原型阶段只是显示文本：每天 / 每周 / 每月。</summary>
    public string Repeat
    {
        get => _repeat;
        set { if (Set(ref _repeat, value)) { RaiseMeta(); OnPropertyChanged(nameof(RepeatLabel)); } }
    }

    /// <summary>所属远景目标，最多一个；空 = 不挂目标。</summary>
    public GoalVM? Goal
    {
        get => _goal;
        set
        {
            if (Set(ref _goal, value))
            {
                OnPropertyChanged(nameof(GoalLabel));
                OnPropertyChanged(nameof(HasGoal));
                RaiseMeta();
            }
        }
    }

    public bool HasGoal => Goal is not null;

    public string GoalLabel => Goal is null ? "无目标" : Goal.Title;

    /// <summary>可选。填了就能落到视图页的时间轴上；只用时刻，日期由 Due 决定。</summary>
    public TimeSpan? StartTime
    {
        get => _startTime;
        set
        {
            if (!Set(ref _startTime, value)) return;
            OnPropertyChanged(nameof(HasTime));
            OnPropertyChanged(nameof(TimeLabel));
            RaiseMeta();
        }
    }

    public TimeSpan? EndTime
    {
        get => _endTime;
        set
        {
            if (!Set(ref _endTime, value)) return;
            OnPropertyChanged(nameof(HasTime));
            OnPropertyChanged(nameof(TimeLabel));
            RaiseMeta();
        }
    }

    public bool HasTime => StartTime is not null && EndTime is not null;

    public string TimeLabel => HasTime
        ? $"{Fmt(StartTime!.Value)}–{Fmt(EndTime!.Value)}"
        : "";

    private static string Fmt(TimeSpan t) => $"{(int)t.TotalHours:00}:{t.Minutes:00}";

    /// <summary>艾森豪威尔的两个轴——手动标，不从日期/目标推导。</summary>
    public bool IsUrgent
    {
        get => _isUrgent;
        set => Set(ref _isUrgent, value);
    }

    public bool IsImportant
    {
        get => _isImportant;
        set => Set(ref _isImportant, value);
    }

    public int Order { get; set; }

    public ObservableCollection<TagVM> Tags { get; } = new();

    public string RepeatLabel => Repeat switch
    {
        "" => "不重复",
        _ => Repeat
    };

    public bool HasRemind => RemindRules.HasAny(_remind);

    public bool ShowRemindBell => HasTime && HasRemind;

    public bool IsRemindNone => !HasRemind;

    public bool IsRemindPre15 => RemindRules.HasNode(_remind, RemindRules.Pre15);

    public bool IsRemindPre5 => RemindRules.HasNode(_remind, RemindRules.Pre5);

    public bool IsRemindStart => RemindRules.HasNode(_remind, RemindRules.Start);

    public bool IsRemindEnd => RemindRules.HasNode(_remind, RemindRules.End);

    public string RemindLabel => RemindRules.FormatLabel(_remind);

    public string RemindTooltip => RemindRules.FormatTooltip(_remind, HasTime);

    public string MetaText
    {
        get
        {
            var parts = new List<string>(4);
            if (HasTime) parts.Add(TimeLabel);
            var date = DateLabel();
            if (date.Length > 0) parts.Add(date);
            if (!string.IsNullOrEmpty(Repeat)) parts.Add(Repeat);
            if (Tags.Count > 0) parts.Add(string.Join(" · ", Tags.Select(t => t.Name)));
            return string.Join("  ·  ", parts);
        }
    }

    /// <summary>日期尽量用相对词，少占位置；今天/明天由档位本身表达，不再重复写。</summary>
    private string DateLabel()
    {
        if (Due is not { } due) return "不定日期";

        var today = DateTime.Today;
        var diff = (due.Date - today).Days;

        return diff switch
        {
            0 or 1 => "",
            -1 => "昨天",
            -2 => "前天",
            < -2 => due.ToString("M月d日"),
            <= 6 => "周" + "日一二三四五六"[(int)due.DayOfWeek],
            _ => due.ToString("M月d日")
        };
    }

    private void OnTagsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
            foreach (TagVM tag in e.OldItems) tag.PropertyChanged -= OnTagPropertyChanged;

        if (e.NewItems is not null)
            foreach (TagVM tag in e.NewItems) tag.PropertyChanged += OnTagPropertyChanged;

        RaiseMeta();
    }

    /// <summary>标签改名不需要遍历任务：日程里存的是引用，监听到就够了。</summary>
    private void OnTagPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TagVM.Name)) RaiseMeta();
    }

    private void RaiseMeta()
    {
        OnPropertyChanged(nameof(MetaText));
        OnPropertyChanged(nameof(RepeatLabel));
        RaiseRemindProperties();
    }

    private void RaiseRemindProperties()
    {
        OnPropertyChanged(nameof(HasRemind));
        OnPropertyChanged(nameof(ShowRemindBell));
        OnPropertyChanged(nameof(IsRemindNone));
        OnPropertyChanged(nameof(IsRemindPre15));
        OnPropertyChanged(nameof(IsRemindPre5));
        OnPropertyChanged(nameof(IsRemindStart));
        OnPropertyChanged(nameof(IsRemindEnd));
        OnPropertyChanged(nameof(RemindLabel));
        OnPropertyChanged(nameof(RemindTooltip));
    }

    /// <summary>外部改了会影响卡片文字的东西（比如目标改名）时手动刷一下。</summary>
    public void NotifyMetaChanged() => RaiseMeta();

    public override string ToString() => string.IsNullOrWhiteSpace(Title) ? "未命名" : Title;
}
