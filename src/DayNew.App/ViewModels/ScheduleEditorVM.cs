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

    public override string ToString() => Name;
}

/// <summary>弹窗里的一枚总目标胶囊：无子目标可直接多选切换；下设子目标时带标记，点击弹目标树多选。</summary>
public sealed class GoalPickVM : ObservableObject
{
    private bool _isSelected;
    private int _selectedCount;

    /// <summary>null = 「无目标」那一枚。</summary>
    public GoalPickVM(GoalVM? goal, bool selected, int selectedCount = 0)
    {
        Goal = goal;
        _isSelected = selected;
        _selectedCount = selectedCount;
    }

    public GoalVM? Goal { get; }

    public string Title => Goal?.Title ?? "无目标";

    public Brush Color => Goal?.Color ?? Brushes.Transparent;

    public bool HasGoal => Goal is not null;

    public bool HasSubGoals => Goal?.HasSubGoals == true;

    public int SelectedCount
    {
        get => _selectedCount;
        set
        {
            if (!Set(ref _selectedCount, value)) return;
            OnPropertyChanged(nameof(BadgeText));
            OnPropertyChanged(nameof(HasBadge));
        }
    }

    public string BadgeText
    {
        get
        {
            if (!HasSubGoals) return "";
            return SelectedCount > 0 ? $"⑂ {SelectedCount}" : "⑂";
        }
    }

    public bool HasBadge => HasSubGoals;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            _isSelected = value;
            OnPropertyChanged();
        }
    }

    public override string ToString() => HasBadge ? $"{Title} {BadgeText}" : Title;
}

/// <summary>ScheduleEditorVM 与 PresetManagerVM 共用的跨总目标/子目标草稿选择状态。</summary>
public sealed class GoalBindingDraft
{
    public List<GoalBindingVM> Bindings { get; } = new();

    public void Reset(IEnumerable<GoalBindingVM> initial, IEnumerable<GoalVM> activeGoals, ObservableCollection<GoalPickVM> targetPicks)
    {
        Bindings.Clear();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var b in initial)
        {
            if (seen.Add(b.TargetKey))
                Bindings.Add(new GoalBindingVM(b.Goal, b.Node));
        }

        RebuildPicks(activeGoals, targetPicks);
    }

    public void RebuildPicks(IEnumerable<GoalVM> activeGoals, ObservableCollection<GoalPickVM> targetPicks)
    {
        // 移除已被外部删除的目标或子目标
        Bindings.RemoveAll(b => b.Node is not null && !b.Goal.Nodes.Contains(b.Node));

        var pool = new List<GoalVM>(activeGoals);
        foreach (var b in Bindings)
        {
            if (!pool.Contains(b.Goal))
                pool.Add(b.Goal);
        }

        targetPicks.Clear();
        targetPicks.Add(new GoalPickVM(null, Bindings.Count == 0));

        foreach (var entity in pool)
        {
            var count = Bindings.Count(b => ReferenceEquals(b.Goal, entity));
            targetPicks.Add(new GoalPickVM(entity, count > 0, count));
        }
    }

    public void SyncPickStates(ObservableCollection<GoalPickVM> targetPicks)
    {
        foreach (var pick in targetPicks)
        {
            if (pick.Goal is null)
            {
                pick.SelectedCount = 0;
                pick.IsSelected = Bindings.Count == 0;
            }
            else
            {
                var count = Bindings.Count(b => ReferenceEquals(b.Goal, pick.Goal));
                pick.SelectedCount = count;
                pick.IsSelected = count > 0;
            }
        }
    }

    public void ClearAll(ObservableCollection<GoalPickVM> targetPicks)
    {
        Bindings.Clear();
        SyncPickStates(targetPicks);
    }

    public void ToggleDirectGoal(GoalVM goal, ObservableCollection<GoalPickVM> targetPicks)
    {
        var existed = Bindings.RemoveAll(b => ReferenceEquals(b.Goal, goal)) > 0;
        if (!existed)
            Bindings.Add(new GoalBindingVM(goal, null));
        SyncPickStates(targetPicks);
    }

    public void ApplyGoalNodes(GoalVM goal, IEnumerable<GoalNodeVM> nodes, ObservableCollection<GoalPickVM> targetPicks)
    {
        Bindings.RemoveAll(b => ReferenceEquals(b.Goal, goal));
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in nodes)
        {
            var binding = new GoalBindingVM(goal, node.IsRoot ? null : node);
            if (seen.Add(binding.TargetKey))
                Bindings.Add(binding);
        }
        SyncPickStates(targetPicks);
    }

    public IReadOnlyList<GoalChipVM> BuildPreviewChips()
        => TaskVM.BuildGoalChips(Bindings, compact: false);
}

/// <summary>
/// 新建 / 编辑日程的弹窗状态。只是草稿：点保存之前一个字段都不写回实体，
/// 所以「取消」天然不会留半条改动。
/// </summary>
public sealed class ScheduleEditorVM : ObservableObject
{
    private readonly MainViewModel _owner;
    private readonly GoalBindingDraft _goalDraft = new();

    public ScheduleEditorVM(MainViewModel owner) => _owner = owner;

    /// <summary>正在编辑的日程；null = 新建。</summary>
    public TaskVM? Task { get; private set; }

    public bool IsNew => Task is null;

    public bool IsEditing => Task is not null;

    public string Header => IsNew ? "新建日程" : "编辑日程";

    public ObservableCollection<TagPickVM> TagPicks { get; } = new();

    public ObservableCollection<GoalPickVM> GoalPicks { get; } = new();

    public IReadOnlyList<GoalChipVM> SelectedGoalChips => _goalDraft.BuildPreviewChips();

    public bool HasSelectedGoals => _goalDraft.Bindings.Count > 0;

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

        BuildPicks(Array.Empty<TagVM>(), Array.Empty<GoalBindingVM>());
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

        BuildPicks(task.Tags, task.GoalBindings);
        RaiseAll();
        _owner.RefreshEditorHint();
    }

    // ---------------- 选择 ----------------

    public void ClickGoalPick(GoalPickVM? pick)
    {
        if (pick is null) return;

        if (pick.Goal is null)
        {
            _goalDraft.ClearAll(GoalPicks);
            RaiseGoalSelectionChanged();
            return;
        }

        if (pick.Goal.HasSubGoals)
        {
            _owner.OpenGoalPickerForDraft(pick.Goal, _goalDraft.Bindings, nodes =>
            {
                _goalDraft.ApplyGoalNodes(pick.Goal, nodes, GoalPicks);
                RaiseGoalSelectionChanged();
            });
            return;
        }

        _goalDraft.ToggleDirectGoal(pick.Goal, GoalPicks);
        RaiseGoalSelectionChanged();
    }

    public IReadOnlyList<GoalBindingVM> SelectedGoalBindings => _goalDraft.Bindings;

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

    private void BuildPicks(IList<TagVM> selectedTags, IEnumerable<GoalBindingVM> selectedGoals)
    {
        TagPicks.Clear();
        foreach (var tag in _owner.Tags)
            TagPicks.Add(new TagPickVM(tag, selectedTags.Contains(tag)));

        _goalDraft.Reset(selectedGoals, _owner.ActiveGoals, GoalPicks);
    }

    private void RaiseGoalSelectionChanged()
    {
        OnPropertyChanged(nameof(SelectedGoalChips));
        OnPropertyChanged(nameof(HasSelectedGoals));
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
        RaiseGoalSelectionChanged();
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
