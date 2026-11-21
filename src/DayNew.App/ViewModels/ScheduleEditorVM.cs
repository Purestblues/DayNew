using System.Collections.ObjectModel;
using System.Windows.Media;
using DayNew.Core;

namespace DayNew.ViewModels;

/// <summary>弹窗里的一枚标签：勾上就挂到日程上。</summary>
public sealed class TagPickVM : ObservableObject
{
    private bool _isSelected;

    public TagPickVM(TagVM tag, bool selected)
    {
        Tag = tag;
        _isSelected = selected;
    }

    /// <summary>当场新建的标签先只活在弹窗里，保存时才写进 store（取消不该留下空标签）。</summary>
    public bool IsProvisional { get; init; }

    public TagVM Tag { get; }

    public string Name => Tag.Name;

    public Brush Color => Tag.Color;

    public bool IsSelected
    {
        get => _isSelected;
        set => Set(ref _isSelected, value);
    }
}

/// <summary>弹窗里的一枚目标：单选。第一节是「无目标」（Goal 为 null）。</summary>
public sealed class GoalPickVM : ObservableObject
{
    private bool _isSelected;

    /// <summary>null = 「无目标」那一枚。</summary>
    public GoalPickVM(GoalVM? goal, bool selected)
    {
        Goal = goal;
        _isSelected = selected;
    }

    public GoalVM? Goal { get; }

    public string Title => Goal?.Title ?? "无目标";

    public Brush Color => Goal?.Color ?? Brushes.Transparent;

    public bool HasGoal => Goal is not null;

    public bool IsSelected
    {
        get => _isSelected;
        set => Set(ref _isSelected, value);
    }
}

/// <summary>
/// 新建 / 编辑日程的弹窗状态。只是草稿：点保存之前一个字段都不写回实体，
/// 所以「取消」天然不会留半条改动。
/// </summary>
public sealed class ScheduleEditorVM : ObservableObject
{
    private readonly MainViewModel _owner;

    public ScheduleEditorVM(MainViewModel owner) => _owner = owner;

    /// <summary>正在编辑的日程；null = 新建。</summary>
    public TaskVM? Task { get; private set; }

    public bool IsNew => Task is null;

    public bool IsEditing => Task is not null;

    public string Header => IsNew ? "新建日程" : "编辑日程";

    public ObservableCollection<TagPickVM> TagPicks { get; } = new();

    public ObservableCollection<GoalPickVM> GoalPicks { get; } = new();

    private string _title = "";
    private string _note = "";
    private string _remind = RemindRules.Default;
    private string _repeat = "";
    private bool _isUrgent;
    private bool _isImportant;
    private DateTime _date = DateTime.Today;
    private string _startText = "09:00";
    private string _endText = "09:30";
    private string _hintText = "";
    private string _errorText = "";
    private string _newTagText = "";

    public string Title
    {
        get => _title;
        set => Set(ref _title, value);
    }

    public string Note
    {
        get => _note;
        set => Set(ref _note, value);
    }

    public string Remind
    {
        get => _remind;
        set
        {
            var normalized = RemindRules.Normalize(value);
            if (Set(ref _remind, normalized)) RaiseRemindProperties();
        }
    }

    public bool IsRemindNone => !RemindRules.HasAny(_remind);

    public bool IsRemindPre15 => RemindRules.HasNode(_remind, RemindRules.Pre15);

    public bool IsRemindPre5 => RemindRules.HasNode(_remind, RemindRules.Pre5);

    public bool IsRemindStart => RemindRules.HasNode(_remind, RemindRules.Start);

    public bool IsRemindEnd => RemindRules.HasNode(_remind, RemindRules.End);

    public void ToggleRemindNode(string node) => Remind = RemindRules.ToggleNode(_remind, node);

    public string Repeat
    {
        get => _repeat;
        set => Set(ref _repeat, value);
    }

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

    public DateTime Date
    {
        get => _date;
        private set
        {
            if (!Set(ref _date, value)) return;
            OnPropertyChanged(nameof(DateText));
            _owner.RefreshEditorHint();
        }
    }

    public string DateText => $"{_date:M月d日} 周{"日一二三四五六"[(int)_date.DayOfWeek]}";

    public string StartText
    {
        get => _startText;
        set { if (Set(ref _startText, value)) _owner.RefreshEditorHint(); }
    }

    public string EndText
    {
        get => _endText;
        set { if (Set(ref _endText, value)) _owner.RefreshEditorHint(); }
    }

    /// <summary>「和谁撞了、会挪到几点」——边打字边算，不用等保存才发现。</summary>
    public string HintText
    {
        get => _hintText;
        private set => Set(ref _hintText, value);
    }

    public string ErrorText
    {
        get => _errorText;
        private set => Set(ref _errorText, value);
    }

    public string NewTagText
    {
        get => _newTagText;
        set => Set(ref _newTagText, value);
    }

    /// <summary>「不重复」用一个内部值 none，实体里还是空字符串。</summary>
    public const string NoRepeat = "none";

    public void StepDay(int days) => Date = Date.AddDays(days);

    // ---------------- 打开 ----------------

    public void OpenNew(DateTime date, int startMinutes, int endMinutes)
    {
        Task = null;
        _title = "";
        _note = "";
        _remind = RemindRules.Normalize(_owner.Settings.DefaultRemind);
        _repeat = NoRepeat;
        _isUrgent = false;
        _isImportant = false;
        _newTagText = "";
        _hintText = "";
        _errorText = "";
        _date = date.Date;
        _startText = Clock(startMinutes);
        _endText = Clock(endMinutes);

        BuildPicks(Array.Empty<TagVM>(), null);
        RaiseAll();
        _owner.RefreshEditorHint();
    }

    public void OpenExisting(TaskVM task)
    {
        Task = task;
        _title = task.Title;
        _note = task.Note;
        _remind = RemindRules.Normalize(task.Remind);
        _repeat = string.IsNullOrEmpty(task.Repeat) ? NoRepeat : task.Repeat;
        _isUrgent = task.IsUrgent;
        _isImportant = task.IsImportant;
        _newTagText = "";
        _hintText = "";
        _errorText = "";
        _date = task.Due?.Date ?? DateTime.Today;

        var start = (int)(task.StartTime?.TotalMinutes ?? 9 * 60);
        var end = (int)(task.EndTime?.TotalMinutes ?? start + 30);
        if (end <= start) end = start + 30;
        _startText = Clock(start);
        _endText = Clock(end);

        BuildPicks(task.Tags, task.Goal);
        RaiseAll();
        _owner.RefreshEditorHint();
    }

    // ---------------- 选择 ----------------

    public void SelectGoal(GoalPickVM? pick)
    {
        foreach (var option in GoalPicks) option.IsSelected = ReferenceEquals(option, pick);
    }

    public GoalVM? SelectedGoal => GoalPicks.FirstOrDefault(g => g.IsSelected)?.Goal;

    public IEnumerable<TagPickVM> SelectedTagPicks => TagPicks.Where(p => p.IsSelected);

    public void AddTagPick(TagPickVM pick)
    {
        TagPicks.Add(pick);
        OnPropertyChanged(nameof(TagPicks));
    }

    public void SetHint(string text) => HintText = text;

    public void SetError(string text) => ErrorText = text;

    /// <summary>把「9」「9:30」「09:30」读成分钟数；读不出来返回 false。</summary>
    public bool TryReadTime(out int start, out int end)
    {
        start = end = 0;

        if (!MainViewModel.TryParseClock(StartText, out start)) return false;
        if (!MainViewModel.TryParseClock(EndText, out end)) return false;

        if (end <= start) end = Math.Min(24 * 60, start + 30);
        if (end <= start) start = Math.Max(0, end - 30);

        return end > start;
    }

    // ---------------- 内部 ----------------

    private void BuildPicks(IList<TagVM> selected, GoalVM? goal)
    {
        TagPicks.Clear();
        foreach (var tag in _owner.Tags)
            TagPicks.Add(new TagPickVM(tag, selected.Contains(tag)));

        GoalPicks.Clear();
        GoalPicks.Add(new GoalPickVM(null, goal is null));
        foreach (var entity in _owner.Goals)
            GoalPicks.Add(new GoalPickVM(entity, ReferenceEquals(entity, goal)));
    }

    private void RaiseAll()
    {
        OnPropertyChanged(nameof(Task));
        OnPropertyChanged(nameof(IsNew));
        OnPropertyChanged(nameof(IsEditing));
        OnPropertyChanged(nameof(Header));
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Note));
        OnPropertyChanged(nameof(Remind));
        RaiseRemindProperties();
        OnPropertyChanged(nameof(Repeat));
        OnPropertyChanged(nameof(IsUrgent));
        OnPropertyChanged(nameof(IsImportant));
        OnPropertyChanged(nameof(Date));
        OnPropertyChanged(nameof(DateText));
        OnPropertyChanged(nameof(StartText));
        OnPropertyChanged(nameof(EndText));
        OnPropertyChanged(nameof(HintText));
        OnPropertyChanged(nameof(ErrorText));
        OnPropertyChanged(nameof(NewTagText));
        OnPropertyChanged(nameof(TagPicks));
        OnPropertyChanged(nameof(GoalPicks));
    }

    private void RaiseRemindProperties()
    {
        OnPropertyChanged(nameof(IsRemindNone));
        OnPropertyChanged(nameof(IsRemindPre15));
        OnPropertyChanged(nameof(IsRemindPre5));
        OnPropertyChanged(nameof(IsRemindStart));
        OnPropertyChanged(nameof(IsRemindEnd));
    }

    private static string Clock(int minutes)
    {
        minutes = Math.Clamp(minutes, 0, 24 * 60);
        return $"{minutes / 60:00}:{minutes % 60:00}";
    }
}
