using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows.Media;
using DayNew.Core;

namespace DayNew.ViewModels;

/// <summary>
/// 快捷日程预设：放在视图工具条上，拖到时间轴格子松手即直接落成一条日程。
/// </summary>
public sealed class SchedulePresetVM : ObservableObject
{
    private string _id = Guid.NewGuid().ToString("N")[..10];
    private string _title = "";
    private int _durationMinutes = 10;
    private string _remind = RemindRules.Default;
    private string _note = "";
    private GoalVM? _goal;
    private bool _isUrgent;
    private bool _isImportant;

    public SchedulePresetVM()
    {
        Tags.CollectionChanged += OnTagsChanged;
    }

    public string Id
    {
        get => _id;
        set { if (!string.IsNullOrWhiteSpace(value)) _id = value; }
    }

    public string Title
    {
        get => _title;
        set
        {
            if (!Set(ref _title, value)) return;
            OnPropertyChanged(nameof(DisplayTitle));
            RaiseSummary();
        }
    }

    public string DisplayTitle => string.IsNullOrWhiteSpace(Title) ? "未命名预设" : Title;

    public int DurationMinutes
    {
        get => _durationMinutes;
        set
        {
            var clamped = Math.Clamp(value, 5, 24 * 60);
            if (!Set(ref _durationMinutes, clamped)) return;
            OnPropertyChanged(nameof(DurationLabel));
            RaiseSummary();
        }
    }

    public string DurationLabel => FormatDuration(DurationMinutes);

    public string Remind
    {
        get => _remind;
        set
        {
            var normalized = RemindRules.Normalize(value);
            if (!Set(ref _remind, normalized)) return;
            OnPropertyChanged(nameof(HasRemind));
            OnPropertyChanged(nameof(RemindLabel));
            OnPropertyChanged(nameof(RemindTooltip));
            RaiseSummary();
        }
    }

    public bool HasRemind => RemindRules.HasAny(_remind);

    public string RemindLabel => RemindRules.FormatLabel(_remind);

    public string RemindTooltip => RemindRules.FormatTooltip(_remind, hasTime: true);

    public string Note
    {
        get => _note;
        set => Set(ref _note, value);
    }

    public GoalVM? Goal
    {
        get => _goal;
        set
        {
            if (ReferenceEquals(_goal, value)) return;
            if (_goal is not null) _goal.PropertyChanged -= OnGoalPropertyChanged;
            _goal = value;
            if (_goal is not null) _goal.PropertyChanged += OnGoalPropertyChanged;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasGoal));
            RaiseSummary();
        }
    }

    public bool HasGoal => Goal is not null;

    public bool IsUrgent
    {
        get => _isUrgent;
        set { if (Set(ref _isUrgent, value)) RaiseSummary(); }
    }

    public bool IsImportant
    {
        get => _isImportant;
        set { if (Set(ref _isImportant, value)) RaiseSummary(); }
    }

    public ObservableCollection<TagVM> Tags { get; } = new();

    public bool HasDot => Tags.Count > 0 || Goal is not null;

    public Brush DotBrush => Tags.Count > 0
        ? Tags[0].Color
        : Goal?.Color ?? Brushes.Transparent;

    public string SummaryText
    {
        get
        {
            var parts = new List<string>(4) { DurationLabel };
            if (Tags.Count > 0) parts.Add(string.Join(" · ", Tags.Select(t => $"#{t.Name}")));
            if (Goal is not null) parts.Add(Goal.Title);
            if (IsUrgent && IsImportant) parts.Add("紧急·重要");
            else if (IsUrgent) parts.Add("紧急");
            else if (IsImportant) parts.Add("重要");
            return string.Join("  ·  ", parts);
        }
    }

    public void RefreshSummary() => RaiseSummary();

    public static string FormatDuration(int minutes)
    {
        var h = minutes / 60;
        var m = minutes % 60;
        if (h == 0) return $"{m}分";
        return m == 0 ? $"{h}小时" : $"{h}小时{m}分";
    }

    private void OnTagsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
            foreach (TagVM tag in e.OldItems) tag.PropertyChanged -= OnTagPropertyChanged;

        if (e.NewItems is not null)
            foreach (TagVM tag in e.NewItems) tag.PropertyChanged += OnTagPropertyChanged;

        RaiseSummary();
    }

    private void OnTagPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TagVM.Name)) RaiseSummary();
    }

    private void OnGoalPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(GoalVM.Title)) RaiseSummary();
    }

    private void RaiseSummary()
    {
        OnPropertyChanged(nameof(HasDot));
        OnPropertyChanged(nameof(DotBrush));
        OnPropertyChanged(nameof(SummaryText));
    }

    public override string ToString() => $"{DisplayTitle} {DurationLabel}";
}

/// <summary>快捷日程预设管理弹窗的表单与编辑态。</summary>
public sealed class PresetManagerVM : ObservableObject
{
    private readonly MainViewModel _owner;

    public PresetManagerVM(MainViewModel owner) => _owner = owner;

    public SchedulePresetVM? EditingPreset { get; private set; }

    public bool IsEditing => EditingPreset is not null;

    public string FormHeader => IsEditing ? "编辑快捷卡片" : "新增快捷卡片";

    public string SubmitText => IsEditing ? "保存修改" : "添加卡片";

    public int[] DurationQuickPicks { get; } = { 10, 15, 25, 30, 45, 60 };

    public ObservableCollection<TagPickVM> TagPicks { get; } = new();

    public ObservableCollection<GoalPickVM> GoalPicks { get; } = new();

    private string _title = "";
    private string _durationText = "10";
    private string _remind = RemindRules.Default;
    private string _note = "";
    private bool _isUrgent;
    private bool _isImportant;
    private string _newTagText = "";
    private string _errorText = "";

    public string Title
    {
        get => _title;
        set
        {
            if (Set(ref _title, value)) SetError("");
        }
    }

    public string DurationText
    {
        get => _durationText;
        set
        {
            if (Set(ref _durationText, value)) SetError("");
        }
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

    public string Note
    {
        get => _note;
        set => Set(ref _note, value);
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

    public string NewTagText
    {
        get => _newTagText;
        set => Set(ref _newTagText, value);
    }

    public string ErrorText
    {
        get => _errorText;
        private set => Set(ref _errorText, value);
    }

    public void StartNew()
    {
        EditingPreset = null;
        _title = "";
        _durationText = "10";
        _remind = RemindRules.Normalize(_owner.Settings.DefaultRemind);
        _note = "";
        _isUrgent = false;
        _isImportant = false;
        _newTagText = "";
        _errorText = "";
        BuildPicks(Array.Empty<TagVM>(), null);
        RaiseAll();
    }

    public void StartEdit(SchedulePresetVM preset)
    {
        EditingPreset = preset;
        _title = preset.Title;
        _durationText = preset.DurationMinutes.ToString();
        _remind = RemindRules.Normalize(preset.Remind);
        _note = preset.Note;
        _isUrgent = preset.IsUrgent;
        _isImportant = preset.IsImportant;
        _newTagText = "";
        _errorText = "";
        BuildPicks(preset.Tags, preset.Goal);
        RaiseAll();
    }

    public void RefreshPicksPreservingSelection()
    {
        var selectedTags = SelectedTagPicks.Select(p => p.Tag).ToList();
        var selectedGoal = SelectedGoal;
        BuildPicks(selectedTags, selectedGoal);
    }

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

    public void SetDuration(int minutes) => DurationText = Math.Clamp(minutes, 5, 24 * 60).ToString();

    public void SetError(string text) => ErrorText = text;

    public bool TryReadDuration(out int minutes)
    {
        minutes = 0;
        var text = (DurationText ?? "").Trim();
        if (!int.TryParse(text, out var value)) return false;
        if (value < 5 || value > 24 * 60) return false;
        minutes = value;
        return true;
    }

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
        OnPropertyChanged(nameof(EditingPreset));
        OnPropertyChanged(nameof(IsEditing));
        OnPropertyChanged(nameof(FormHeader));
        OnPropertyChanged(nameof(SubmitText));
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(DurationText));
        OnPropertyChanged(nameof(Remind));
        RaiseRemindProperties();
        OnPropertyChanged(nameof(Note));
        OnPropertyChanged(nameof(IsUrgent));
        OnPropertyChanged(nameof(IsImportant));
        OnPropertyChanged(nameof(NewTagText));
        OnPropertyChanged(nameof(ErrorText));
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
}
