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
    private bool _isUrgent;
    private bool _isImportant;
    private bool _suppressBindingEvents;

    public SchedulePresetVM()
    {
        Tags.CollectionChanged += OnTagsChanged;
        Persons.CollectionChanged += OnPersonsChanged;
        Locations.CollectionChanged += OnLocationsChanged;
        GoalBindings.CollectionChanged += OnGoalBindingsChanged;
    }

    public string Id
    {
        get => _id;
        set { if (!string.IsNullOrWhiteSpace(value)) _id = value; }
    }

    /// <summary>混合逻辑时钟与字段级时间戳（仅同步使用，UI 不展示）。</summary>
    public string Hlc { get; set; } = "";
    public Dictionary<string, string> FieldHlcs { get; set; } = new(StringComparer.Ordinal);

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

    public ObservableCollection<GoalBindingVM> GoalBindings { get; } = new();

    public GoalVM? Goal
    {
        get => GoalBindings.FirstOrDefault()?.Goal;
        set
        {
            if (value is null)
            {
                if (GoalBindings.Count == 0) return;
                SetGoalBindings(Array.Empty<GoalBindingVM>());
                return;
            }

            SetGoalBindings(new[] { new GoalBindingVM(value, null) });
        }
    }

    public bool HasGoal => GoalBindings.Count > 0;

    public void SetGoalBindings(IEnumerable<GoalBindingVM> bindings)
    {
        _suppressBindingEvents = true;
        try
        {
            foreach (var oldGoal in GoalBindings.Select(b => b.Goal).Distinct())
                oldGoal.PropertyChanged -= OnGoalPropertyChanged;

            GoalBindings.Clear();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var b in bindings)
            {
                if (seen.Add(b.TargetKey))
                    GoalBindings.Add(b);
            }

            foreach (var newGoal in GoalBindings.Select(b => b.Goal).Distinct())
                newGoal.PropertyChanged += OnGoalPropertyChanged;
        }
        finally
        {
            _suppressBindingEvents = false;
        }

        OnPropertyChanged(nameof(Goal));
        OnPropertyChanged(nameof(HasGoal));
        RaiseSummary();
    }

    public bool HasGoalRef(GoalVM goal) => GoalBindings.Any(b => ReferenceEquals(b.Goal, goal));

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

    public ObservableCollection<PersonVM> Persons { get; } = new();

    public ObservableCollection<LocationVM> Locations { get; } = new();

    public bool HasDot => Tags.Count > 0 || GoalBindings.Count > 0;

    public Brush DotBrush => Tags.Count > 0
        ? Tags[0].Color
        : GoalBindings.FirstOrDefault()?.Goal.Color ?? Brushes.Transparent;

    public string SummaryText
    {
        get
        {
            var parts = new List<string>(6) { DurationLabel };
            if (Tags.Count > 0) parts.Add(string.Join(" · ", Tags.Select(t => $"#{t.Name}")));
            if (Persons.Count > 0) parts.Add(string.Join(" · ", Persons.Select(p => p.Name)));
            if (Locations.Count > 0) parts.Add(string.Join(" · ", Locations.Select(l => l.Name)));
            if (GoalBindings.Count > 0)
            {
                var chips = TaskVM.BuildGoalChips(GoalBindings, compact: true);
                parts.Add(string.Join(" · ", chips.Select(c => c.DisplayText)));
            }
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

    private void OnPersonsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
            foreach (PersonVM person in e.OldItems) person.PropertyChanged -= OnPersonPropertyChanged;

        if (e.NewItems is not null)
            foreach (PersonVM person in e.NewItems) person.PropertyChanged += OnPersonPropertyChanged;

        RaiseSummary();
    }

    private void OnLocationsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
            foreach (LocationVM location in e.OldItems) location.PropertyChanged -= OnLocationPropertyChanged;

        if (e.NewItems is not null)
            foreach (LocationVM location in e.NewItems) location.PropertyChanged += OnLocationPropertyChanged;

        RaiseSummary();
    }

    private void OnGoalBindingsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_suppressBindingEvents) return;
        OnPropertyChanged(nameof(Goal));
        OnPropertyChanged(nameof(HasGoal));
        RaiseSummary();
    }

    private void OnTagPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TagVM.Name)) RaiseSummary();
    }

    private void OnPersonPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PersonVM.Name)) RaiseSummary();
    }

    private void OnLocationPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LocationVM.Name)) RaiseSummary();
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
    private readonly GoalBindingDraft _goalDraft = new();

    public PresetManagerVM(MainViewModel owner) => _owner = owner;

    public SchedulePresetVM? EditingPreset { get; private set; }

    public bool IsEditing => EditingPreset is not null;

    public string FormHeader => IsEditing ? "编辑快捷卡片" : "新增快捷卡片";

    public string SubmitText => IsEditing ? "保存修改" : "添加卡片";

    public int[] DurationQuickPicks { get; } = { 10, 15, 25, 30, 45, 60 };

    public ObservableCollection<TagPickVM> TagPicks { get; } = new();

    public ObservableCollection<PersonPickVM> PersonPicks { get; } = new();

    public ObservableCollection<LocationPickVM> LocationPicks { get; } = new();

    public ObservableCollection<GoalPickVM> GoalPicks { get; } = new();

    public IReadOnlyList<GoalChipVM> SelectedGoalChips => _goalDraft.BuildPreviewChips();

    public bool HasSelectedGoals => _goalDraft.Bindings.Count > 0;

    private string _title = "";
    private string _durationText = "10";
    private string _remind = RemindRules.Default;
    private string _note = "";
    private bool _isUrgent;
    private bool _isImportant;
    private string _newTagText = "";
    private string _newPersonText = "";
    private string _newLocationText = "";
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
        set
        {
            if (Set(ref _newTagText, value))
                PickFilterHelper.Apply(TagPicks, value, '#');
        }
    }

    public string NewPersonText
    {
        get => _newPersonText;
        set
        {
            if (Set(ref _newPersonText, value))
                PickFilterHelper.Apply(PersonPicks, value, '@');
        }
    }

    public string NewLocationText
    {
        get => _newLocationText;
        set
        {
            if (Set(ref _newLocationText, value))
                PickFilterHelper.Apply(LocationPicks, value);
        }
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
        _newPersonText = "";
        _newLocationText = "";
        _errorText = "";
        BuildPicks(Array.Empty<TagVM>(), Array.Empty<PersonVM>(), Array.Empty<LocationVM>(), Array.Empty<GoalBindingVM>());
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
        _newPersonText = "";
        _newLocationText = "";
        _errorText = "";
        BuildPicks(preset.Tags, preset.Persons, preset.Locations, preset.GoalBindings);
        RaiseAll();
    }

    public void RefreshPicksPreservingSelection()
    {
        var selectedTags = SelectedTagPicks.Select(p => p.Tag).ToList();
        var selectedPersons = SelectedPersonPicks.Select(p => p.Person).ToList();
        var selectedLocations = SelectedLocationPicks.Select(p => p.Location).ToList();
        var currentBindings = _goalDraft.Bindings.ToList();
        BuildPicks(selectedTags, selectedPersons, selectedLocations, currentBindings);
    }

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

    public IEnumerable<PersonPickVM> SelectedPersonPicks => PersonPicks.Where(p => p.IsSelected);

    public IEnumerable<LocationPickVM> SelectedLocationPicks => LocationPicks.Where(p => p.IsSelected);

    public void AddTagPick(TagPickVM pick)
    {
        TagPicks.Add(pick);
        OnPropertyChanged(nameof(TagPicks));
    }

    public void AddPersonPick(PersonPickVM pick)
    {
        PersonPicks.Add(pick);
        OnPropertyChanged(nameof(PersonPicks));
    }

    public void AddLocationPick(LocationPickVM pick)
    {
        LocationPicks.Add(pick);
        OnPropertyChanged(nameof(LocationPicks));
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

    private void BuildPicks(
        IList<TagVM> selectedTags,
        IList<PersonVM> selectedPersons,
        IList<LocationVM> selectedLocations,
        IEnumerable<GoalBindingVM> selectedGoals)
    {
        TagPicks.Clear();
        foreach (var tag in _owner.Tags)
            TagPicks.Add(new TagPickVM(tag, selectedTags.Contains(tag)));

        PersonPicks.Clear();
        foreach (var person in _owner.Persons)
            PersonPicks.Add(new PersonPickVM(person, selectedPersons.Contains(person)));

        LocationPicks.Clear();
        foreach (var location in _owner.Locations)
            LocationPicks.Add(new LocationPickVM(location, selectedLocations.Contains(location)));

        _goalDraft.Reset(selectedGoals, _owner.ActiveGoals, GoalPicks);
    }

    private void RaiseGoalSelectionChanged()
    {
        OnPropertyChanged(nameof(SelectedGoalChips));
        OnPropertyChanged(nameof(HasSelectedGoals));
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
        OnPropertyChanged(nameof(NewPersonText));
        OnPropertyChanged(nameof(NewLocationText));
        OnPropertyChanged(nameof(ErrorText));
        OnPropertyChanged(nameof(TagPicks));
        OnPropertyChanged(nameof(PersonPicks));
        OnPropertyChanged(nameof(LocationPicks));
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
}
