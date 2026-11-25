using System.Collections.ObjectModel;
using System.Windows.Media;
using DayNew.Core;

namespace DayNew.ViewModels;

/// <summary>视图页的一列 = 一天。</summary>
public sealed class TimelineDayVM : ObservableObject
{
    private Brush _gridBrush;

    public TimelineDayVM(DateTime date, Brush gridBrush)
    {
        Date = date;
        _gridBrush = gridBrush;
    }

    public DateTime Date { get; }

    /// <summary>周几，比如「周一」。</summary>
    public string DayName => Date.DayOfWeek switch
    {
        DayOfWeek.Monday => "周一",
        DayOfWeek.Tuesday => "周二",
        DayOfWeek.Wednesday => "周三",
        DayOfWeek.Thursday => "周四",
        DayOfWeek.Friday => "周五",
        DayOfWeek.Saturday => "周六",
        _ => "周日"
    };

    public string DayNumber => $"{Date.Month}/{Date.Day}";

    public bool IsToday => Date.Date == DateTime.Today;

    public Brush GridBrush
    {
        get => _gridBrush;
        set => Set(ref _gridBrush, value);
    }

    public ObservableCollection<TimelineBlockVM> Blocks { get; } = new();

    public override string ToString() => $"{DayName} {DayNumber}";
}

/// <summary>时间轴上的一个块。支持连续缩放与边缘调时原地刷新。</summary>
public sealed class TimelineBlockVM : ObservableObject
{
    private double _top;
    private double _height;
    private double _left = 5;
    private double _width = 150;
    private string? _previewTimeText;

    public TimelineBlockVM(TaskVM task, double top, double height)
    {
        Task = task;
        _top = top;
        _height = height;
    }

    public TaskVM Task { get; }

    public double Top
    {
        get => _top;
        set => Set(ref _top, value);
    }

    public double Height
    {
        get => _height;
        set
        {
            if (!Set(ref _height, value)) return;
            OnPropertyChanged(nameof(HasRoomForMeta));
            OnPropertyChanged(nameof(HasSideMeta));
            OnPropertyChanged(nameof(HasInlineDots));
            OnPropertyChanged(nameof(IsSingleLine));
            OnPropertyChanged(nameof(IsCompact));
            OnPropertyChanged(nameof(IsSmallBlock));
        }
    }

    /// <summary>重叠避让后的横坐标与宽度（同一时段多条就并排分宽度）。</summary>
    public double Left
    {
        get => _left;
        set => Set(ref _left, value);
    }

    public double Width
    {
        get => _width;
        set
        {
            if (!Set(ref _width, value)) return;
            OnPropertyChanged(nameof(HasSideMeta));
            OnPropertyChanged(nameof(HasInlineDots));
        }
    }

    public string? PreviewTimeText
    {
        get => _previewTimeText;
        set
        {
            if (Set(ref _previewTimeText, value))
                OnPropertyChanged(nameof(TimeText));
        }
    }

    public string Title => string.IsNullOrWhiteSpace(Task.Title) ? "未命名" : Task.Title;

    public string BlockToolTip
    {
        get
        {
            var lines = new List<string>(4);
            if (Task.HasGoal) lines.Add($"目标：{Task.GoalLabel}");
            if (Task.HasPersons) lines.Add($"人物：{Task.PersonsText}");
            if (Task.HasLocations) lines.Add($"地点：{Task.LocationsText}");
            lines.Add("双击编辑");
            return string.Join("\n", lines);
        }
    }

    public bool HasMeta => Task.Tags.Count > 0 || Task.HasGoal || Task.HasPeopleOrLocations;

    /// <summary>块够不够高再写标签和目标；太矮就只写标题和时间，不留半行。</summary>
    public bool HasRoomForMeta => Height >= 60;

    /// <summary>常规高度且宽度足够时，在块右侧并排显示标签和目标。</summary>
    public bool HasSideMeta => !IsSingleLine && Width >= 96 && HasMeta;

    /// <summary>单行矮块且宽度够时，仅展示小色点，避免裁切文字。</summary>
    public bool HasInlineDots => IsSingleLine && Width >= 110 && HasMeta;

    /// <summary>太矮放不下两行时（如 1 时缩放下的 15–45 分钟）改成单行并排，绝不纵向裁切。</summary>
    public bool IsSingleLine => Height < 38;

    /// <summary>略紧凑的两行块（38–48px）收紧上下边距与字号。</summary>
    public bool IsCompact => Height >= 38 && Height < 48;

    /// <summary>极矮块收窄上下边缘拖拽热区，给中间留出移动/双击区域。</summary>
    public bool IsSmallBlock => Height < 26;

    public bool Done => Task.Done;

    public string TimeText => _previewTimeText ?? Task.TimeLabel;

    public override string ToString() => $"{Title} {TimeText}".Trim();
}

/// <summary>左侧刻度（整点或细分刻度）。</summary>
public sealed class TimelineHourVM : ObservableObject
{
    private double _height;
    private bool _isCoveredByNow;

    public TimelineHourVM(int minutes, string text, double height, bool isHour = true)
    {
        Minutes = minutes;
        Text = text;
        _height = height;
        IsHour = isHour;
    }

    public int Minutes { get; }

    public string Text { get; }

    public bool IsHour { get; }

    public double Height
    {
        get => _height;
        set => Set(ref _height, value);
    }

    public bool IsCoveredByNow
    {
        get => _isCoveredByNow;
        set => Set(ref _isCoveredByNow, value);
    }

    public override string ToString() => Text;
}

/// <summary>缩放档位按钮。</summary>
public sealed class ZoomOptionVM : ObservableObject
{
    private bool _isCurrent;

    public ZoomOptionVM(int index, string label)
    {
        Index = index;
        Label = label;
    }

    public int Index { get; }

    public string Label { get; }

    public bool IsCurrent
    {
        get => _isCurrent;
        set => Set(ref _isCurrent, value);
    }

    public override string ToString() => Label;
}
