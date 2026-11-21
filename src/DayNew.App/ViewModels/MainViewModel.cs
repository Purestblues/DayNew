using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using DayNew.Core;
using DayNew.Services;

namespace DayNew.ViewModels;

/// <summary>
/// 主视图模型。原型阶段数据只存在内存里，但档位推导规则与正式版一致：
/// 档位不是字段，是 due 和今天的关系算出来的。
/// </summary>
public sealed class MainViewModel : ObservableObject
{
    private readonly DayStore _store = new();
    private readonly StoreFile _file = new();
    private readonly CompletionLog _history = new();

    public event Action<string, bool>? ToastRequested;
    public event Action<TaskVM>? TaskTouched;
    public event Action<int, int>? SummaryChanged;

    public AppSettings Settings { get; } = AppSettings.Load();

    public IReadOnlyList<TaskVM> AllSchedules => _all;

    private void Notify(string message, bool highlight = false) => ToastRequested?.Invoke(message, highlight);

    /// <summary>日程的唯一来源：就是 store 里那一个列表，不另存副本。</summary>
    private List<TaskVM> _all => _store.Schedules;

    private NavItemVM _selectedNav;
    private bool _isDockExpanded = true;

    /// <summary>新建 / 编辑日程的弹窗状态。打开时 IsEditorOpen 为 true，界面盖一层浮层。</summary>
    public ScheduleEditorVM Editor { get; }

    /// <summary>快捷日程预设管理弹窗状态。</summary>
    public PresetManagerVM PresetManager { get; }

    private bool _isEditorOpen;
    private bool _isSettingsOpen;
    private bool _isPresetManagerOpen;
    private bool _isTagDetailOpen;
    private TagVM? _selectedTagDetail;
    private GoalVM? _selectedGoalDetail;
    private int _tagDetailTodayIndex;

    public bool IsEditorOpen
    {
        get => _isEditorOpen;
        private set => Set(ref _isEditorOpen, value);
    }

    public bool IsSettingsOpen
    {
        get => _isSettingsOpen;
        private set => Set(ref _isSettingsOpen, value);
    }

    public bool IsPresetManagerOpen
    {
        get => _isPresetManagerOpen;
        private set => Set(ref _isPresetManagerOpen, value);
    }

    public bool IsTagDetailOpen
    {
        get => _isTagDetailOpen;
        private set => Set(ref _isTagDetailOpen, value);
    }

    public TagVM? SelectedTagDetail
    {
        get => _selectedTagDetail;
        private set
        {
            if (Set(ref _selectedTagDetail, value))
                RaiseDetailHeaderProperties();
        }
    }

    public GoalVM? SelectedGoalDetail
    {
        get => _selectedGoalDetail;
        private set
        {
            if (Set(ref _selectedGoalDetail, value))
                RaiseDetailHeaderProperties();
        }
    }

    public Brush DetailColor => SelectedGoalDetail?.Color ?? SelectedTagDetail?.Color ?? Brushes.Transparent;

    public string DetailTitle => SelectedGoalDetail?.Title ?? SelectedTagDetail?.Name ?? "";

    public ObservableCollection<TagDayColumnVM> TagDetailDays { get; } = new();

    public int TagDetailTodayIndex => _tagDetailTodayIndex;

    public string TagDetailTotalText
    {
        get
        {
            if (SelectedGoalDetail is not null)
            {
                var goalCount = _all.Count(t => ReferenceEquals(t.Goal, SelectedGoalDetail));
                return goalCount == 0 ? "暂无日程" : $"共 {goalCount} 条日程 · {SelectedGoalDetail.ProgressText}";
            }

            if (SelectedTagDetail is null) return "";
            var count = _all.Count(t => t.Tags.Contains(SelectedTagDetail));
            return count == 0 ? "暂无日程" : $"共 {count} 条日程";
        }
    }

    private void RaiseDetailHeaderProperties()
    {
        OnPropertyChanged(nameof(DetailColor));
        OnPropertyChanged(nameof(DetailTitle));
        OnPropertyChanged(nameof(TagDetailTotalText));
    }

    public bool AutoStartEnabled
    {
        get => AutoStartService.IsEnabled;
        set
        {
            if (AutoStartService.IsEnabled == value) return;
            if (!AutoStartService.SetEnabled(value))
            {
                Notify("写入开机自启设置失败", highlight: true);
                OnPropertyChanged();
                return;
            }
            OnPropertyChanged();
            Notify(value ? "已开启开机静默自启" : "已关闭开机自启");
        }
    }

    public bool CloseToTray
    {
        get => Settings.CloseToTray;
        set
        {
            if (Settings.CloseToTray == value) return;
            Settings.CloseToTray = value;
            Settings.Save();
            OnPropertyChanged();
        }
    }

    public bool NotifyEnabled
    {
        get => Settings.NotifyEnabled;
        set
        {
            if (Settings.NotifyEnabled == value) return;
            Settings.NotifyEnabled = value;
            Settings.Save();
            OnPropertyChanged();
            Notify(value ? "已开启系统通知" : "已暂停系统通知");
        }
    }

    public bool DailyDigestEnabled
    {
        get => Settings.DailyDigestEnabled;
        set
        {
            if (Settings.DailyDigestEnabled == value) return;
            Settings.DailyDigestEnabled = value;
            Settings.Save();
            OnPropertyChanged();
            Notify(value ? "已开启每日待办摘要" : "已关闭每日待办摘要");
        }
    }

    public bool IsDefaultRemindNone => !RemindRules.HasAny(Settings.DefaultRemind);

    public bool IsDefaultRemindPre15 => RemindRules.HasNode(Settings.DefaultRemind, RemindRules.Pre15);

    public bool IsDefaultRemindPre5 => RemindRules.HasNode(Settings.DefaultRemind, RemindRules.Pre5);

    public bool IsDefaultRemindStart => RemindRules.HasNode(Settings.DefaultRemind, RemindRules.Start);

    public bool IsDefaultRemindEnd => RemindRules.HasNode(Settings.DefaultRemind, RemindRules.End);

    public void ToggleDefaultRemindNode(string node)
    {
        var next = RemindRules.ToggleNode(Settings.DefaultRemind, node);
        if (Settings.DefaultRemind == next) return;

        Settings.DefaultRemind = next;
        Settings.Save();
        OnPropertyChanged(nameof(IsDefaultRemindNone));
        OnPropertyChanged(nameof(IsDefaultRemindPre15));
        OnPropertyChanged(nameof(IsDefaultRemindPre5));
        OnPropertyChanged(nameof(IsDefaultRemindStart));
        OnPropertyChanged(nameof(IsDefaultRemindEnd));
    }

    public string DataFolderPath => StoreFile.Folder;

    public string AppVersion => "v" + (typeof(MainViewModel).Assembly.GetName().Version?.ToString(3) ?? "0.1.0");

    public void OpenSettings()
    {
        OnPropertyChanged(nameof(AutoStartEnabled));
        IsSettingsOpen = true;
    }

    public void CloseSettings() => IsSettingsOpen = false;

    public void OpenDataFolder()
    {
        try
        {
            Directory.CreateDirectory(StoreFile.Folder);
            Process.Start(new ProcessStartInfo("explorer.exe", StoreFile.Folder) { UseShellExecute = true });
        }
        catch
        {
            Notify("无法打开数据目录", highlight: true);
        }
    }

    public (int TodayPending, int OverdueCount) GetSummaryCounts()
    {
        var today = DateTime.Today;
        var todayPending = _all.Count(t => !t.Done && t.Due?.Date == today);
        return (todayPending, OverdueLane.Count);
    }

    public bool CompleteTaskById(string taskId)
    {
        var task = _all.FirstOrDefault(t => t.Id == taskId);
        if (task is null || task.Done) return false;
        ToggleDone(task);
        return true;
    }

    /// <summary>左侧 dock 是否展开（折叠后只留图标）。</summary>
    public bool IsDockExpanded
    {
        get => _isDockExpanded;
        set => Set(ref _isDockExpanded, value);
    }

    public MainViewModel()
    {
        Editor = new ScheduleEditorVM(this);
        PresetManager = new PresetManagerVM(this);

        Lanes = new ObservableCollection<LaneVM>
        {
            new(LaneKind.Overdue, "逾期"),
            new(LaneKind.Today, "今天"),
            new(LaneKind.Tomorrow, "明天"),
            new(LaneKind.ThisWeek, "本周"),
            new(LaneKind.Later, "之后")
        };

        OverviewLanes = new ObservableCollection<LaneVM>(
            Lanes.Where(l => l.Kind != LaneKind.Overdue));

        NavItems = new ObservableCollection<NavItemVM>
        {
            new(PageKind.Overview, "概览", "\uE80F",
                "你每天真正会打开的页：一横排档位，拖一下就换个时间做。"),
            new(PageKind.Timeline, "视图", "\uE787",
                "纵向是 24 小时，横向是日期，一屏七天。任务有了起止时间就能直接拖进格子；放大时刻度会变细。"),
            new(PageKind.Tags, "标签", "\uE8EC",
                "在这里创建、改名、删除标签。概览页里只负责往卡片上贴，不负责管理。"),
            new(PageKind.Goals, "目标", "\uE7C1",
                "长期目标，比如「学 Python」。任务是它的子项。目标和标签互不干扰：目标回答「为了什么」，标签回答「是什么」。"),
            new(PageKind.Eisenhower, "艾森豪威尔看板", "\uE80A",
                "四象限看板。紧急和重要都靠你手动标记，把卡片拖进哪个格子就改哪个标记。"),
            new(PageKind.Overdue, "逾期", "\uE823",
                "已过截止日还没完成的日程都收在这里，勾选完成、移到今天，或双击细改。")
        };

        _selectedNav = NavItems[0];

        ZoomOptions = new ObservableCollection<ZoomOptionVM>();
        for (var i = 0; i < Zooms.Length; i++) ZoomOptions.Add(new ZoomOptionVM(i, Zooms[i].Label));

        Quadrants = new ObservableCollection<QuadrantVM>
        {
            new QuadrantVM(true, true, "紧急 · 重要", "马上自己动手做"),
            new QuadrantVM(false, true, "重要 · 不紧急", "排时间做，最值钱"),
            new QuadrantVM(true, false, "紧急 · 不重要", "能交给别人就别自己做"),
            new QuadrantVM(false, false, "没标记", "不重要也不紧急；还没分类的也在这")
        };

        LoadOrSeed();
        Rebuild();
    }

    public ObservableCollection<LaneVM> Lanes { get; }

    public ObservableCollection<LaneVM> OverviewLanes { get; }

    public LaneVM OverdueLane => LaneOfKind(LaneKind.Overdue);

    public ObservableCollection<NavItemVM> NavItems { get; }

    public ObservableCollection<GoalVM> Goals => _store.Goals;

    public ObservableCollection<TagVM> Tags => _store.Tags;

    public ObservableCollection<SchedulePresetVM> Presets => _store.Presets;

    public ObservableCollection<TagRowVM> TagRows { get; } = new();

    public ObservableCollection<QuadrantVM> Quadrants { get; } = new();

    private string _goalInput = "";
    public string GoalInput
    {
        get => _goalInput;
        set => Set(ref _goalInput, value);
    }

    public NavItemVM SelectedNav
    {
        get => _selectedNav;
        set
        {
            if (value is null) return;
            if (!Set(ref _selectedNav, value)) return;
            OnPropertyChanged(nameof(IsOverview));
            OnPropertyChanged(nameof(IsTimeline));
            OnPropertyChanged(nameof(IsGoals));
            OnPropertyChanged(nameof(IsTags));
            OnPropertyChanged(nameof(IsEisenhower));
            OnPropertyChanged(nameof(IsOverduePage));
            OnPropertyChanged(nameof(IsPlaceholder));
            OnPropertyChanged(nameof(CurrentTitle));
            OnPropertyChanged(nameof(CurrentBlurb));
        }
    }

    public bool IsOverview => SelectedNav.Kind == PageKind.Overview;

    public bool IsTimeline => SelectedNav.Kind == PageKind.Timeline;

    public bool IsGoals => SelectedNav.Kind == PageKind.Goals;

    public bool IsTags => SelectedNav.Kind == PageKind.Tags;

    public bool IsEisenhower => SelectedNav.Kind == PageKind.Eisenhower;

    public bool IsOverduePage => SelectedNav.Kind == PageKind.Overdue;

    public bool IsPlaceholder => SelectedNav.Kind is not (PageKind.Overview or PageKind.Timeline or PageKind.Tags or PageKind.Goals or PageKind.Eisenhower or PageKind.Overdue);

    public string CurrentTitle => SelectedNav.Title;

    public string CurrentBlurb => SelectedNav.Blurb;

    public string StatusLeft
    {
        get
        {
            var pending = _all.Count(t => !t.Done);
            var overdue = Lanes.First(l => l.Kind == LaneKind.Overdue).Items.Count;
            return overdue > 0
                ? $"{pending} 条待办 · {overdue} 条逾期"
                : $"{pending} 条待办";
        }
    }

    public string StatusRight => _file.Exists ? "已存本地" : "待首次保存";

    public string DataPath => StoreFile.DataPath;

    public string HistoryPath => CompletionLog.Path;

    /// <summary>立即落盘（关窗口 / 退出时调）。</summary>
    public void FlushSave() => _file.Flush(_store);

    private void LoadOrSeed()
    {
        if (_file.TryLoad(_store))
        {
            if (!_store.HasLoadedPresets) SeedDefaultPresets();
            return;
        }

        Seed();
    }

    // ---------------- 查询 ----------------

    public LaneVM LaneOfKind(LaneKind kind) => Lanes.First(l => l.Kind == kind);

    private static DateTime EndOfWeek()
    {
        var today = DateTime.Today;
        var offset = (7 - (int)today.DayOfWeek) % 7; // 周日 0 => 7 天后归零
        return today.AddDays(offset == 0 ? 7 : offset);
    }

    private static DateTime NextMonday()
    {
        var today = DateTime.Today;
        var offset = ((int)DayOfWeek.Monday - (int)today.DayOfWeek + 7) % 7;
        return today.AddDays(offset == 0 ? 7 : offset);
    }

    private static LaneKind LaneFor(TaskVM t)
    {
        // 无日期任务并入「之后」的末尾（排序值最大时自然沉到最后）
        if (t.Due is not { } due) return LaneKind.Later;

        var today = DateTime.Today;
        var d = due.Date;

        if (d < today) return t.Done ? LaneKind.Today : LaneKind.Overdue;
        if (d == today) return LaneKind.Today;
        if (d == today.AddDays(1)) return LaneKind.Tomorrow;
        if (d <= EndOfWeek()) return LaneKind.ThisWeek;
        return LaneKind.Later;
    }

    private static DateTime? DueForLane(LaneKind kind)
    {
        var today = DateTime.Today;
        return kind switch
        {
            LaneKind.Overdue => today.AddDays(-1),
            LaneKind.Today => today,
            LaneKind.Tomorrow => today.AddDays(1),
            LaneKind.ThisWeek => EndOfWeek(),
            LaneKind.Later => NextMonday(),
            _ => today
        };
    }

    // ---------------- 重建档位 ----------------

    public void Rebuild()
    {
        foreach (var lane in Lanes) lane.Items.Clear();

        foreach (var task in _all.OrderBy(t => t.Order))
            LaneOfKind(LaneFor(task)).Items.Add(task);

        foreach (var goal in Goals)
        {
            goal.Tasks.Clear();
            foreach (var task in _all.Where(t => ReferenceEquals(t.Goal, goal)).OrderBy(t => t.Order))
                goal.Tasks.Add(task);
            goal.Refresh();
        }

        // 标签：按用得多少排（已经在 store 里的标签为 0 次的也列出来，方便管理和清理）
        TagRows.Clear();
        var usage = _all.SelectMany(t => t.Tags)
                        .GroupBy(t => t)
                        .ToDictionary(g => g.Key, g => g.Count());

        foreach (var tag in _store.Tags
                                .OrderByDescending(t => usage.TryGetValue(t, out var n) ? n : 0)
                                .ThenBy(t => t.Name))
            TagRows.Add(new TagRowVM(tag, usage.TryGetValue(tag, out var count) ? count : 0));

        // 四象限：两个标记决定一格；两个都没打的落「没标记」
        foreach (var quadrant in Quadrants) quadrant.Items.Clear();
        foreach (var task in _all.OrderBy(t => t.Order))
        {
            var quadrant = Quadrants.First(q => q.IsUrgent == task.IsUrgent && q.IsImportant == task.IsImportant);
            quadrant.Items.Add(task);
        }
        foreach (var quadrant in Quadrants) quadrant.RaiseCount();

        NavItems.First(n => n.Kind == PageKind.Overdue).BadgeCount = OverdueLane.Count;

        OnPropertyChanged(nameof(StatusLeft));
        OnPropertyChanged(nameof(StatusRight));

        _file.MarkDirty(_store);

        RebuildTimeline();
        if (IsTagDetailOpen) RebuildTagDetail();

        var (todayPending, overdueCount) = GetSummaryCounts();
        SummaryChanged?.Invoke(todayPending, overdueCount);
    }

    // ---------------- 操作 ----------------

    /// <summary>拖拽落点：跨档位时改 due；同档位内只改顺序，保留原日期。</summary>
    public void MoveTask(TaskVM task, LaneVM target, int index)
    {
        var crossLane = LaneFor(task) != target.Kind;
        _all.Remove(task);

        if (crossLane)
            task.Due = DueForLane(target.Kind);

        var ordered = _all.Where(t => LaneFor(t) == target.Kind)
                          .OrderBy(t => t.Order)
                          .ToList();

        index = Math.Clamp(index, 0, ordered.Count);
        ordered.Insert(index, task);

        for (var i = 0; i < ordered.Count; i++) ordered[i].Order = (i + 1) * 1024;

        _all.Add(task);
        Rebuild();
        TaskTouched?.Invoke(task);
        if (crossLane) Notify($"已移到「{target.Title}」");
    }

    public void ToggleExpand(TaskVM task)
    {
        var expand = !task.IsExpanded;
        foreach (var t in _all) t.IsExpanded = false;
        task.IsExpanded = expand;
    }

    public void CollapseAll()
    {
        foreach (var t in _all) t.IsExpanded = false;
        foreach (var g in Goals) g.IsExpanded = false;
    }

    public void ToggleDone(TaskVM task)
    {
        task.Done = !task.Done;

        // 完成记录单独追加一行，不进主文件（重复日程的历史会把它撞爆）
        _history.Append(task.Id, task.Due?.Date, task.Done);

        Rebuild();
        Notify(task.Done ? $"已完成「{task.Title}」" : $"已恢复待办「{task.Title}」");
    }

    public void Delete(TaskVM task)
    {
        _all.Remove(task);
        Rebuild();
        Notify($"已删除「{task.Title}」");
    }

    /// <summary>右键菜单：只改日子，不动时段。</summary>
    public void MoveToDay(TaskVM task, DateTime date)
    {
        task.Due = date.Date;
        Rebuild();
        TaskTouched?.Invoke(task);

        var label = date.Date == DateTime.Today
            ? "今天"
            : date.Date == DateTime.Today.AddDays(1)
                ? "明天"
                : $"{date:M月d日}";
        Notify($"已移到{label}");
    }

    public void CycleRepeat(TaskVM task)
    {
        task.Repeat = task.Repeat switch
        {
            "" => "每天",
            "每天" => "每周",
            "每周" => "每月",
            _ => ""
        };
    }

    public void ToggleTaskRemindNode(TaskVM task, string node)
    {
        var next = RemindRules.ToggleNode(task.Remind, node);
        if (task.Remind == next) return;

        task.Remind = next;
        _file.MarkDirty(_store);
        TaskTouched?.Invoke(task);
    }

    public void AddTag(TaskVM task, string tag)
    {
        var entity = _store.GetOrCreateTag(tag);
        if (entity is null) return;
        if (!task.Tags.Contains(entity)) task.Tags.Add(entity);
        Rebuild();
    }

    public void RemoveTag(TaskVM task, TagVM tag)
    {
        task.Tags.Remove(tag);
        Rebuild();
    }

    // ---------------- 标签（页） ----------------

    /// <summary>改名只改实体本身；日程里存的是引用，监听已经处理好了。</summary>
    public void RenameTag(TagVM tag, string name)
    {
        var trimmed = (name ?? "").Trim().TrimStart('#');
        if (trimmed.Length == 0) return;

        tag.Name = trimmed;
        OnPropertyChanged(nameof(TagRows));
    }

    public void DeleteTag(TagVM tag)
    {
        _store.RemoveTag(tag);
        Rebuild();
        Notify($"已移除标签「#{tag.Name}」");
    }

    public void OpenTagDetail(TagVM tag)
    {
        SelectedGoalDetail = null;
        SelectedTagDetail = tag;
        RebuildTagDetail();
        IsTagDetailOpen = true;
    }

    public void OpenGoalDetail(GoalVM goal)
    {
        SelectedTagDetail = null;
        SelectedGoalDetail = goal;
        RebuildTagDetail();
        IsTagDetailOpen = true;
    }

    public void CloseTagDetail() => IsTagDetailOpen = false;

    public void RebuildTagDetail()
    {
        TagDetailDays.Clear();

        List<TaskVM> detailTasks;
        if (SelectedGoalDetail is not null)
        {
            if (!Goals.Contains(SelectedGoalDetail))
            {
                _tagDetailTodayIndex = 0;
                SelectedGoalDetail = null;
                IsTagDetailOpen = false;
                RaiseDetailHeaderProperties();
                return;
            }

            detailTasks = _all.Where(t => ReferenceEquals(t.Goal, SelectedGoalDetail)).ToList();
        }
        else if (SelectedTagDetail is not null && _store.Tags.Contains(SelectedTagDetail))
        {
            detailTasks = _all.Where(t => t.Tags.Contains(SelectedTagDetail)).ToList();
        }
        else
        {
            _tagDetailTodayIndex = 0;
            SelectedTagDetail = null;
            SelectedGoalDetail = null;
            IsTagDetailOpen = false;
            RaiseDetailHeaderProperties();
            return;
        }

        var baseStart = TimelineStart();
        var baseEnd = baseStart.AddDays(DayCount - 1);

        var minDate = baseStart;
        var maxDate = baseEnd;

        foreach (var task in detailTasks)
        {
            if (task.Due is not { } due) continue;
            var d = due.Date;
            if (d < minDate) minDate = d;
            if (d > maxDate) maxDate = d;
        }

        var today = DateTime.Today;
        var byDate = detailTasks
            .GroupBy(t => t.Due?.Date ?? today)
            .ToDictionary(
                g => g.Key,
                g => g.OrderBy(t => t.StartTime ?? TimeSpan.MaxValue)
                      .ThenBy(t => t.EndTime ?? TimeSpan.MaxValue)
                      .ThenBy(t => t.Order)
                      .ToList());

        _tagDetailTodayIndex = 0;
        var index = 0;

        for (var d = minDate; d <= maxDate; d = d.AddDays(1))
        {
            if (d == today) _tagDetailTodayIndex = index;

            var col = new TagDayColumnVM(d);
            if (byDate.TryGetValue(d, out var list))
                foreach (var task in list) col.Items.Add(task);

            TagDetailDays.Add(col);
            index++;
        }

        RaiseDetailHeaderProperties();
    }

    // ---------------- 四象限 ----------------

    public void ToggleUrgent(TaskVM task)
    {
        task.IsUrgent = !task.IsUrgent;
        Rebuild();
    }

    public void ToggleImportant(TaskVM task)
    {
        task.IsImportant = !task.IsImportant;
        Rebuild();
    }

    /// <summary>拖到哪一格就把两个标记设成那格。</summary>
    public void SetQuadrant(TaskVM task, QuadrantVM quadrant)
    {
        if (task.IsUrgent == quadrant.IsUrgent && task.IsImportant == quadrant.IsImportant) return;

        task.IsUrgent = quadrant.IsUrgent;
        task.IsImportant = quadrant.IsImportant;
        Rebuild();
        TaskTouched?.Invoke(task);
        Notify($"已移到「{quadrant.Title}」");
    }

    // ---------------- 时间 ----------------

    /// <summary>概览卡片里手打时间：同样不允许跟别人撞，撞了就落到最近的空档。</summary>
    public void SetTime(TaskVM task, TimeSpan start, TimeSpan end)
    {
        if (end <= start) end = start.Add(TimeSpan.FromMinutes(30));

        // 有时间就必须有日期，否则它在时间轴上没地方放
        var date = task.Due?.Date ?? DateTime.Today;
        var duration = Math.Max(1, (int)(end - start).TotalMinutes);
        var free = FindFreeStart(date, (int)start.TotalMinutes, duration, task);

        var newStart = TimeSpan.FromMinutes(free);
        var newEnd = TimeSpan.FromMinutes(free + duration);
        var changed = task.Due?.Date != date || task.StartTime != newStart || task.EndTime != newEnd;

        task.Due = date;
        task.StartTime = newStart;
        task.EndTime = newEnd;
        Rebuild();
        if (changed) TaskTouched?.Invoke(task);

        if (free != (int)start.TotalMinutes)
            Notify($"已避开冲突，调整到 {Clock(free)}–{Clock(free + duration)}", highlight: true);
        else
            Notify($"时间已设为 {Clock(free)}–{Clock(free + duration)}");
    }

    public void ClearTime(TaskVM task)
    {
        task.StartTime = null;
        task.EndTime = null;
        Rebuild();
    }

    // ---------------- 视图（时间轴） ----------------

    /// <summary>缩放档：标签、每小时像素、刻度间隔（分钟）。</summary>
    private static readonly (string Label, double PxPerHour, int Interval)[] Zooms =
    {
        ("1 时", 44, 60),
        ("30 分", 88, 30),
        ("15 分", 176, 15),
        ("5 分", 528, 5),
        ("1 分", 1584, 1)
    };

    public const double DayWidth = 160;
    public const int DayCount = 42;
    public const double MinPxPerHour = 28;
    public const double MaxPxPerHour = 1800;
    private const int DaysBeforeToday = 14;

    private int _zoomIndex = 1;
    private double _pxPerHour = 88;
    private int _tickStepMinutes = 30;
    private int _currentMinutes = DateTime.Now.Hour * 60 + DateTime.Now.Minute;
    private DateTime _timelineBuiltDate = DateTime.Today;

    public ObservableCollection<TimelineDayVM> TimelineDays { get; } = new();

    public ObservableCollection<TimelineHourVM> TimelineHours { get; } = new();

    public ObservableCollection<ZoomOptionVM> ZoomOptions { get; }

    public double TimelineWidth => DayWidth * DayCount;

    public double PxPerHourValue => _pxPerHour;

    public int IntervalMinutes => Zooms[_zoomIndex].Interval;

    public double GridHeight => 24 * PxPerHourValue;

    public int TodayIndex => (DateTime.Today - TimelineStart()).Days;

    public int CurrentMinutes => _currentMinutes;

    public double NowLineTop => _currentMinutes / 60.0 * PxPerHourValue;

    public string NowTimeText => Clock(_currentMinutes);

    public double NowTodayLeft => TodayIndex * DayWidth;

    public Brush GridBrush { get; private set; } = Brushes.Transparent;

    public void RefreshCurrentTime()
    {
        var now = DateTime.Now;
        var mins = now.Hour * 60 + now.Minute;

        if (now.Date != _timelineBuiltDate)
        {
            _currentMinutes = mins;
            Rebuild();
            OnPropertyChanged(nameof(CurrentMinutes));
            OnPropertyChanged(nameof(NowLineTop));
            OnPropertyChanged(nameof(NowTimeText));
            return;
        }

        if (mins == _currentMinutes) return;

        _currentMinutes = mins;
        OnPropertyChanged(nameof(CurrentMinutes));
        OnPropertyChanged(nameof(NowLineTop));
        OnPropertyChanged(nameof(NowTimeText));
    }

    /// <summary>时间轴的第一天：本周一往前推两周，保证今天左右都有地方可滚。</summary>
    private static DateTime TimelineStart()
    {
        var today = DateTime.Today;
        var toMonday = ((int)DayOfWeek.Monday - (int)today.DayOfWeek + 7) % 7;
        return today.AddDays(-toMonday - DaysBeforeToday);
    }

    public double PresetPxPerHour(int index)
        => index >= 0 && index < Zooms.Length ? Zooms[index].PxPerHour : PxPerHourValue;

    private static int ZoomIndexForPxPerHour(double pxPerHour) => pxPerHour switch
    {
        < 66 => 0,
        < 132 => 1,
        < 352 => 2,
        < 1000 => 3,
        _ => 4
    };

    public void SetZoom(int index)
    {
        if (index < 0 || index >= Zooms.Length) return;
        SetPxPerHour(Zooms[index].PxPerHour);
    }

    /// <summary>连续缩放 24h 纵轴，并按当前像素密度自动切换刻度粒度与档位高亮（不重建视图树）。</summary>
    public void SetPxPerHour(double pxPerHour)
    {
        var clamped = Math.Clamp(pxPerHour, MinPxPerHour, MaxPxPerHour);
        _pxPerHour = clamped;
        _zoomIndex = ZoomIndexForPxPerHour(clamped);

        if (TimelineDays.Count == 0 || TimelineHours.Count == 0)
        {
            RebuildTimeline();
            return;
        }

        UpdateTimelineScale();
    }

    /// <summary>主题切换后重画网格线（画刷里带颜色，不重建就还是旧主题）。</summary>
    public void RefreshTimelineBrushes() => RebuildTimeline();

    private int TickStepMinutes()
    {
        var interval = IntervalMinutes;
        if (interval == 1 && PxPerHourValue < 1140) return 5;
        return Math.Max(1, interval);
    }

    private void RebuildTimelineHours(double pxPerHour, int step)
    {
        _tickStepMinutes = step;
        var slotHeight = pxPerHour * step / 60.0;
        TimelineHours.Clear();
        for (var m = 0; m < 24 * 60; m += step)
            TimelineHours.Add(new TimelineHourVM(Clock(m), slotHeight, isHour: m % 60 == 0));
    }

    public void RebuildTimeline()
    {
        _timelineBuiltDate = DateTime.Today;
        var pxPerHour = PxPerHourValue;
        var intervalPx = pxPerHour * IntervalMinutes / 60.0;
        GridBrush = CreateGridBrush(intervalPx);

        RebuildTimelineHours(pxPerHour, TickStepMinutes());

        foreach (var option in ZoomOptions) option.IsCurrent = option.Index == _zoomIndex;

        TimelineDays.Clear();
        var start = TimelineStart();

        for (var i = 0; i < DayCount; i++)
        {
            var date = start.AddDays(i);
            var day = new TimelineDayVM(date, GridBrush);

            var blocks = _all
                .Where(t => t.Due is { } due && due.Date == date && t.StartTime is not null)
                .Select(t =>
                {
                    var startMin = t.StartTime!.Value.TotalMinutes;
                    var endMin = t.EndTime?.TotalMinutes ?? startMin + 30;
                    if (endMin <= startMin) endMin = startMin + 30;

                    return new TimelineBlockVM(t,
                        startMin / 60.0 * pxPerHour,
                        Math.Max(22, (endMin - startMin) / 60.0 * pxPerHour));
                })
                .OrderBy(b => b.Top);

            foreach (var block in blocks) day.Blocks.Add(block);
            LayoutOverlaps(day.Blocks);
            TimelineDays.Add(day);
        }

        OnPropertyChanged(nameof(GridHeight));
        OnPropertyChanged(nameof(GridBrush));
        OnPropertyChanged(nameof(PxPerHourValue));
        OnPropertyChanged(nameof(NowLineTop));
        OnPropertyChanged(nameof(NowTodayLeft));
    }

    private void UpdateTimelineScale()
    {
        var pxPerHour = PxPerHourValue;
        var intervalPx = pxPerHour * IntervalMinutes / 60.0;
        GridBrush = CreateGridBrush(intervalPx);

        foreach (var option in ZoomOptions) option.IsCurrent = option.Index == _zoomIndex;

        var step = TickStepMinutes();
        if (step != _tickStepMinutes)
        {
            RebuildTimelineHours(pxPerHour, step);
        }
        else
        {
            var slotHeight = pxPerHour * step / 60.0;
            foreach (var hour in TimelineHours) hour.Height = slotHeight;
        }

        foreach (var day in TimelineDays)
        {
            day.GridBrush = GridBrush;
            foreach (var block in day.Blocks)
            {
                var startMin = block.Task.StartTime?.TotalMinutes ?? 0;
                var endMin = block.Task.EndTime?.TotalMinutes ?? startMin + 30;
                if (endMin <= startMin) endMin = startMin + 30;

                block.Top = startMin / 60.0 * pxPerHour;
                block.Height = Math.Max(22, (endMin - startMin) / 60.0 * pxPerHour);
            }

            LayoutOverlaps(day.Blocks);
        }

        OnPropertyChanged(nameof(GridHeight));
        OnPropertyChanged(nameof(GridBrush));
        OnPropertyChanged(nameof(PxPerHourValue));
        OnPropertyChanged(nameof(NowLineTop));
    }

    private static DrawingBrush CreateGridBrush(double intervalPx)
    {
        var lineBrush = (Application.Current?.TryFindResource("BorderSubtle") as Brush) ?? Brushes.Gray;

        const double tileWidth = 32;
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(Brushes.Transparent, null,
            new RectangleGeometry(new Rect(0, 0, tileWidth, intervalPx))));
        group.Children.Add(new GeometryDrawing(null, new Pen(lineBrush, 1),
            new LineGeometry(new Point(0, 0.5), new Point(tileWidth, 0.5))));
        group.Freeze();

        var brush = new DrawingBrush(group)
        {
            TileMode = TileMode.Tile,
            Stretch = Stretch.None,
            Viewbox = new Rect(0, 0, tileWidth, intervalPx),
            ViewboxUnits = BrushMappingMode.Absolute,
            Viewport = new Rect(0, 0, tileWidth, intervalPx),
            ViewportUnits = BrushMappingMode.Absolute
        };
        brush.Freeze();
        return brush;
    }

    // ---------------- 不透叠：人不可能同一时间干两件事 ----------------

    /// <summary>某天已经占住时间段的日程（排除自己）。</summary>
    private IEnumerable<(int Start, int End)> BusyRanges(DateTime date, TaskVM? exclude)
    {
        foreach (var task in _all)
        {
            if (ReferenceEquals(task, exclude)) continue;
            if (task.StartTime is not { } start) continue;
            if (task.Due is not { } due || due.Date != date.Date) continue;

            var from = (int)start.TotalMinutes;
            var to = (int)(task.EndTime?.TotalMinutes ?? from + 30);
            if (to <= from) to = from + 30;

            yield return (from, to);
        }
    }

    private bool IsFree(DateTime date, int start, int duration, TaskVM? exclude)
    {
        var end = start + duration;
        if (start < 0 || end > 24 * 60) return false;

        foreach (var (from, to) in BusyRanges(date, exclude))
            if (start < to && from < end) return false;

        return true;
    }

    /// <summary>
    /// 从想要的位置往两边找最近的空档（步长 = 当前缩放的间隔）。
    /// 整天都排满了就贴着当天最后一段之后放，宁可挤一下也不把日程丢到第二天。
    /// </summary>
    public int FindFreeStart(DateTime date, int desiredStart, int durationMinutes, TaskVM? exclude = null)
    {
        durationMinutes = Math.Clamp(durationMinutes, 1, 24 * 60);
        var latest = 24 * 60 - durationMinutes;
        var desired = Math.Clamp(desiredStart, 0, latest);

        if (IsFree(date, desired, durationMinutes, exclude)) return desired;

        var step = Math.Max(1, IntervalMinutes);
        var maxSteps = 24 * 60 / step + 1;

        for (var k = 1; k <= maxSteps; k++)
        {
            // 同一距离先试往下推：新建的往后排更符合直觉
            var after = desired + k * step;
            if (after <= latest && IsFree(date, after, durationMinutes, exclude)) return after;

            var before = desired - k * step;
            if (before >= 0 && IsFree(date, before, durationMinutes, exclude)) return before;
        }

        var lastEnd = BusyRanges(date, exclude).Select(r => r.End).DefaultIfEmpty(0).Max();
        return Math.Clamp(lastEnd, 0, latest);
    }

    /// <summary>这一段时间跟谁撞了（用于弹窗里的提示）。</summary>
    private string? ConflictTitle(DateTime date, int start, int duration, TaskVM? exclude)
    {
        var end = start + duration;

        foreach (var task in _all)
        {
            if (ReferenceEquals(task, exclude)) continue;
            if (task.StartTime is not { } startTime) continue;
            if (task.Due is not { } due || due.Date != date.Date) continue;

            var from = (int)startTime.TotalMinutes;
            var to = (int)(task.EndTime?.TotalMinutes ?? from + 30);
            if (to <= from) to = from + 30;

            if (start < to && from < end) return task.Title;
        }

        return null;
    }

    /// <summary>拖时间轴上的块：换天 + 换开始时刻，时长不变；撞到别人就直接让到最近的空档。</summary>
    public void MoveBlock(TaskVM task, DateTime date, int startMinutes)
    {
        var duration = DurationMinutes(task);
        var start = FindFreeStart(date, startMinutes, duration, task);

        task.Due = date.Date;
        task.StartTime = TimeSpan.FromMinutes(start);
        task.EndTime = TimeSpan.FromMinutes(start + duration);
        Rebuild();
        TaskTouched?.Invoke(task);

        if (start != startMinutes)
            Notify($"已避开冲突，挪到 {Clock(start)}–{Clock(start + duration)}", highlight: true);
        else
            Notify($"已调整至 {date:M月d日} {Clock(start)}–{Clock(start + duration)}");
    }

    /// <summary>拖日程块上下边缘时的可拉伸安全边界（碰到相邻日程自动停住，不重叠）。</summary>
    public (int MinStart, int MaxEnd) GetResizeBounds(TaskVM task)
    {
        if (task.Due is not { } due || task.StartTime is not { } startTime) return (0, 24 * 60);

        var origStart = (int)startTime.TotalMinutes;
        var origEnd = (int)(task.EndTime?.TotalMinutes ?? origStart + 30);
        if (origEnd <= origStart) origEnd = origStart + 30;

        var minStart = 0;
        var maxEnd = 24 * 60;

        foreach (var (from, to) in BusyRanges(due.Date, task))
        {
            if (to <= origStart) minStart = Math.Max(minStart, to);
            if (from >= origEnd) maxEnd = Math.Min(maxEnd, from);
        }

        return (Math.Min(minStart, origStart), Math.Max(maxEnd, origEnd));
    }

    /// <summary>拖动时间轴日程块的上下边缘后提交新的起止时刻（最少 5 分钟）。</summary>
    public void ResizeBlock(TaskVM task, int startMinutes, int endMinutes)
    {
        startMinutes = Math.Clamp(startMinutes, 0, 24 * 60 - 5);
        endMinutes = Math.Clamp(endMinutes, startMinutes + 5, 24 * 60);

        task.StartTime = TimeSpan.FromMinutes(startMinutes);
        task.EndTime = TimeSpan.FromMinutes(endMinutes);
        Rebuild();
        TaskTouched?.Invoke(task);
        Notify($"已调整至 {Clock(startMinutes)}–{Clock(endMinutes)}");
    }

    public static int DurationMinutes(TaskVM task)
    {
        if (task.StartTime is not { } start || task.EndTime is not { } end) return 30;
        var minutes = (int)(end - start).TotalMinutes;
        return minutes <= 0 ? 30 : minutes;
    }

    /// <summary>
    /// 同一时段有多条时并排分宽度，而不是叠在一起互相遮住。
    /// 做法：按开始时间把相互重叠的块归成一组，组内贪心分配到列，宽度 = 列宽 / 列数。
    /// </summary>
    private static void LayoutOverlaps(IReadOnlyList<TimelineBlockVM> blocks)
    {
        const double padLeft = 5;
        const double gap = 2;
        var usable = DayWidth - padLeft * 2;

        var i = 0;
        while (i < blocks.Count)
        {
            // 一个簇：起始时间连续重叠下去的那一段
            var end = blocks[i].Top + blocks[i].Height;
            var j = i + 1;
            while (j < blocks.Count && blocks[j].Top < end - 0.5)
            {
                end = Math.Max(end, blocks[j].Top + blocks[j].Height);
                j++;
            }

            var cluster = blocks.Skip(i).Take(j - i).ToList();
            var columnEnds = new List<double>();
            var columns = new Dictionary<TimelineBlockVM, int>();

            foreach (var block in cluster)
            {
                var placed = false;
                for (var c = 0; c < columnEnds.Count; c++)
                {
                    if (columnEnds[c] > block.Top + 0.5) continue;
                    columnEnds[c] = block.Top + block.Height;
                    columns[block] = c;
                    placed = true;
                    break;
                }

                if (placed) continue;
                columnEnds.Add(block.Top + block.Height);
                columns[block] = columnEnds.Count - 1;
            }

            var count = Math.Max(1, columnEnds.Count);
            var width = Math.Max(24, usable / count - gap);

            foreach (var block in cluster)
            {
                var column = columns[block];
                block.Left = padLeft + column * (usable / count);
                block.Width = width;
            }

            i = j;
        }
    }

    // ---------------- 目标 ----------------

    public void NavigateToGoals()
        => SelectedNav = NavItems.First(n => n.Kind == PageKind.Goals);

    public void NavigateToTimeline()
        => SelectedNav = NavItems.First(n => n.Kind == PageKind.Timeline);

    public void AddGoalFromInput()
    {
        var title = (GoalInput ?? "").Trim();
        if (title.Length == 0) return;

        _store.AddGoal(title);
        GoalInput = "";
        Notify($"已新建目标「{title}」");
    }

    // ---------------- 日程编辑弹窗 ----------------

    /// <summary>时间轴上拖出一段 → 弹窗新建（标题、标签、目标、起止时间都在里面改）。</summary>
    public void OpenNewSchedule(DateTime date, int startMinutes, int endMinutes)
    {
        if (endMinutes <= startMinutes) endMinutes = startMinutes + 30;

        Editor.OpenNew(date, startMinutes, endMinutes);
        IsEditorOpen = true;
    }

    public void OpenSchedule(TaskVM task)
    {
        Editor.OpenExisting(task);
        IsEditorOpen = true;
    }

    public void CloseEditor() => IsEditorOpen = false;

    /// <summary>边打字边说清楚：这段时间跟谁撞了、保存时会挪到几点。</summary>
    public void RefreshEditorHint()
    {
        Editor.SetError("");

        if (!Editor.TryReadTime(out var start, out var end))
        {
            Editor.SetHint("时间写成 9:30 这样");
            return;
        }

        var duration = Math.Max(1, end - start);
        var free = FindFreeStart(Editor.Date, start, duration, Editor.Task);

        if (free == start)
        {
            Editor.SetHint("");
            return;
        }

        var conflict = ConflictTitle(Editor.Date, start, duration, Editor.Task);
        var window = $"{Clock(free)}–{Clock(free + duration)}";

        Editor.SetHint(conflict is null
            ? $"这段时间排不下，保存时会挪到 {window}"
            : $"和「{conflict}」撞了，保存时会挪到 {window}");
    }

    public void SaveEditor()
    {
        if (!Editor.TryReadTime(out var start, out var end))
        {
            Editor.SetError("时间看不懂，写成 9:30 这样");
            return;
        }

        var duration = Math.Max(1, end - start);
        var free = FindFreeStart(Editor.Date, start, duration, Editor.Task);

        var title = (Editor.Title ?? "").Trim();
        if (title.Length == 0) title = "未命名";

        var isNew = Editor.Task is null;
        var task = Editor.Task ?? _store.AddSchedule();

        task.Title = title;
        task.Note = Editor.Note ?? "";
        task.Remind = Editor.Remind;
        task.Repeat = Editor.Repeat == ScheduleEditorVM.NoRepeat ? "" : Editor.Repeat;
        task.IsUrgent = Editor.IsUrgent;
        task.IsImportant = Editor.IsImportant;
        task.Goal = Editor.SelectedGoal;
        task.Due = Editor.Date.Date;
        task.StartTime = TimeSpan.FromMinutes(free);
        task.EndTime = TimeSpan.FromMinutes(free + duration);

        task.Tags.Clear();
        foreach (var tag in ResolveSelectedTags(Editor.SelectedTagPicks)) task.Tags.Add(tag);

        IsEditorOpen = false;
        Rebuild();
        TaskTouched?.Invoke(task);

        if (free != start)
            Notify($"已避开冲突，保存至 {Clock(free)}–{Clock(free + duration)}", highlight: true);
        else
            Notify(isNew ? $"已创建「{title}」" : $"已保存「{title}」");
    }

    public void DeleteEdited()
    {
        if (Editor.Task is not { } task) return;

        IsEditorOpen = false;
        Delete(task);
    }

    /// <summary>弹窗里当场建的标签先只是草稿，保存时才真正进 store。</summary>
    public void AddEditorTag(string name)
        => AddProvisionalOrExistingTag(Editor.TagPicks, Editor.AddTagPick, name);

    public void AddPresetManagerTag(string name)
        => AddProvisionalOrExistingTag(PresetManager.TagPicks, PresetManager.AddTagPick, name);

    private void AddProvisionalOrExistingTag(
        ObservableCollection<TagPickVM> picks,
        Action<TagPickVM> addPick,
        string name)
    {
        var trimmed = (name ?? "").Trim().TrimStart('#');
        if (trimmed.Length == 0) return;

        var existing = picks.FirstOrDefault(
            p => string.Equals(p.Name, trimmed, StringComparison.OrdinalIgnoreCase));

        if (existing is not null)
        {
            existing.IsSelected = true;
            return;
        }

        var entity = _store.FindTag(trimmed);
        if (entity is not null)
        {
            addPick(new TagPickVM(entity, true));
            return;
        }

        var draft = new TagVM(trimmed) { Color = DayStore.TagPaletteColor(_store.Tags.Count) };
        addPick(new TagPickVM(draft, true) { IsProvisional = true });
    }

    private List<TagVM> ResolveSelectedTags(IEnumerable<TagPickVM> selectedPicks)
    {
        var result = new List<TagVM>();

        foreach (var pick in selectedPicks)
        {
            var tag = pick.IsProvisional ? _store.GetOrCreateTag(pick.Name) : pick.Tag;
            if (!result.Contains(tag)) result.Add(tag);
        }

        return result;
    }

    // ---------------- 快捷日程预设 ----------------

    public void OpenPresetManager()
    {
        PresetManager.StartNew();
        IsPresetManagerOpen = true;
    }

    public void ClosePresetManager() => IsPresetManagerOpen = false;

    public void StartEditPreset(SchedulePresetVM preset) => PresetManager.StartEdit(preset);

    public void CancelEditPreset() => PresetManager.StartNew();

    public void SavePreset()
    {
        var title = (PresetManager.Title ?? "").Trim();
        if (title.Length == 0)
        {
            PresetManager.SetError("写个卡片名称，比如「休息」");
            return;
        }

        if (!PresetManager.TryReadDuration(out var duration))
        {
            PresetManager.SetError("时长请填 5–1440 之间的分钟数");
            return;
        }

        var isNew = PresetManager.EditingPreset is null;
        var preset = PresetManager.EditingPreset ?? _store.AddPreset();

        preset.Title = title;
        preset.DurationMinutes = duration;
        preset.Remind = PresetManager.Remind;
        preset.Note = PresetManager.Note ?? "";
        preset.IsUrgent = PresetManager.IsUrgent;
        preset.IsImportant = PresetManager.IsImportant;
        preset.Goal = PresetManager.SelectedGoal;

        preset.Tags.Clear();
        foreach (var tag in ResolveSelectedTags(PresetManager.SelectedTagPicks))
            preset.Tags.Add(tag);

        Rebuild();
        PresetManager.StartNew();
        Notify(isNew ? $"已添加快捷卡片「{title}」" : $"已保存快捷卡片「{title}」");
    }

    public void DeletePreset(SchedulePresetVM preset)
    {
        if (ReferenceEquals(PresetManager.EditingPreset, preset))
            PresetManager.StartNew();

        _store.RemovePreset(preset);
        _file.MarkDirty(_store);
        Notify($"已删除快捷卡片「{preset.DisplayTitle}」");
    }

    /// <summary>把快捷卡片拖到时间轴松手：直接落盘生成一条日程（撞到别人自动让开）。</summary>
    public void CreateScheduleFromPreset(SchedulePresetVM preset, DateTime date, int desiredStartMinutes)
    {
        var duration = Math.Clamp(preset.DurationMinutes, 5, 24 * 60);
        var free = FindFreeStart(date, desiredStartMinutes, duration);
        var title = string.IsNullOrWhiteSpace(preset.Title) ? "未命名" : preset.Title.Trim();

        var task = _store.AddSchedule(s =>
        {
            s.Title = title;
            s.Note = preset.Note;
            s.Remind = preset.Remind;
            s.Due = date.Date;
            s.StartTime = TimeSpan.FromMinutes(free);
            s.EndTime = TimeSpan.FromMinutes(free + duration);
            s.IsUrgent = preset.IsUrgent;
            s.IsImportant = preset.IsImportant;
            s.Goal = preset.Goal;
        });

        foreach (var tag in preset.Tags)
            if (!task.Tags.Contains(tag)) task.Tags.Add(tag);

        Rebuild();
        TaskTouched?.Invoke(task);

        if (free != desiredStartMinutes)
            Notify($"已避开冲突，添加「{title}」至 {Clock(free)}–{Clock(free + duration)}", highlight: true);
        else
            Notify($"已添加「{title}」 {Clock(free)}–{Clock(free + duration)}");
    }

    /// <summary>「9」「9:30」「09:30」都认；读不出来返回 false。</summary>
    public static bool TryParseClock(string text, out int minutes)
    {
        minutes = 0;

        var trimmed = (text ?? "").Trim();
        if (trimmed.Length == 0) return false;

        if (trimmed.Contains(':'))
        {
            if (!TimeSpan.TryParse(trimmed, out var span)) return false;
            if (span < TimeSpan.Zero || span >= TimeSpan.FromDays(1)) return false;

            minutes = (int)span.TotalMinutes;
            return true;
        }

        if (int.TryParse(trimmed, out var hour) && hour is >= 0 and < 24)
        {
            minutes = hour * 60;
            return true;
        }

        return false;
    }

    private static string Clock(int minutes)
    {
        minutes = Math.Clamp(minutes, 0, 24 * 60);
        return $"{minutes / 60:00}:{minutes % 60:00}";
    }

    private GoalVM CreateGoal(string title) => _store.AddGoal(title);

    public void DeleteGoal(GoalVM goal)
    {
        foreach (var task in _all.Where(t => ReferenceEquals(t.Goal, goal))) task.Goal = null;
        foreach (var preset in _store.Presets.Where(p => ReferenceEquals(p.Goal, goal))) preset.Goal = null;
        Goals.Remove(goal);
        Rebuild();
        Notify($"已删除目标「{goal.Title}」");
    }

    /// <summary>把任务挂到目标下。拖到目标卡片上就是走这里。</summary>
    public void AssignGoal(TaskVM task, GoalVM goal)
    {
        if (ReferenceEquals(task.Goal, goal)) return;
        task.Goal = goal;
        Rebuild();
        TaskTouched?.Invoke(task);
        Notify($"已关联目标「{goal.Title}」");
    }

    public void ClearGoal(TaskVM task)
    {
        if (task.Goal is null) return;
        task.Goal = null;
        Rebuild();
    }

    public void ToggleGoalExpand(GoalVM goal)
    {
        var expand = !goal.IsExpanded;
        foreach (var g in Goals) g.IsExpanded = false;
        goal.IsExpanded = expand;
    }

    // ---------------- 演示数据 ----------------

    private void Seed()
    {
        var today = DateTime.Today;

        var python = CreateGoal("学 Python");
        var tidy = CreateGoal("生活整理");

        void Add(string title, DateTime? due, string note = "", string[]? tags = null,
                 string repeat = "", string remind = RemindRules.Default, bool done = false, GoalVM? goal = null,
                 string? time = null)
        {
            var t = _store.AddSchedule(s =>
            {
                s.Title = title;
                s.Note = note;
                s.Due = due;
                s.Repeat = repeat;
                s.Remind = remind;
                s.Done = done;
                s.Goal = goal;
            });

            if (time is not null)
            {
                var parts = time.Split('-');
                t.StartTime = TimeSpan.Parse(parts[0]);
                t.EndTime = TimeSpan.Parse(parts[1]);
            }

            foreach (var tag in tags ?? Array.Empty<string>())
                t.Tags.Add(_store.GetOrCreateTag(tag));
        }

        Add("交电费", today.AddDays(-1), "户号记在手机备忘录里（在发票右上角）", new[] { "生活" }, time: "19:00-19:15");
        Add("写方案", today, "先把档位的边界定死，再动代码", new[] { "工作" }, time: "09:00-10:30");
        Add("跑步", today, "", new[] { "习惯" }, repeat: "每天", remind: RemindRules.Default, time: "19:30-20:10");
        Add("回邮件", today, "", new[] { "工作" }, time: "11:00-11:40");
        Add("吃药", today, "", new[] { "生活" }, repeat: "每天", done: true, time: "08:00-08:10");
        Add("体检预约", today.AddDays(1), "上午空腹", new[] { "生活" }, time: "08:30-09:30");
        Add("整理书房", EndOfWeek(), "", new[] { "生活" }, goal: tidy, time: "14:00-16:00");
        Add("年报", today.AddDays(40), "", new[] { "工作" }, remind: "", time: "10:00-11:30");
        Add("学 Rust", today.AddDays(3), "先把所有权搞明白", new[] { "学习" }, remind: "", time: "20:00-21:00");
        Add("读官方教程前四章", today.AddDays(2), "", new[] { "学习" }, remind: "", goal: python, time: "20:00-21:30");
        Add("写一个爬虫小脚本", today.AddDays(10), "", new[] { "学习" }, remind: "", goal: python, time: "20:00-21:00");
        Add("换手机", today.AddDays(5), "", new[] { "生活" }, remind: "", goal: tidy, time: "15:00-15:30");

        SeedDefaultPresets();
    }

    private void SeedDefaultPresets()
    {
        void AddPreset(string title, int durationMinutes)
        {
            _store.AddPreset(p =>
            {
                p.Title = title;
                p.DurationMinutes = durationMinutes;
                p.Remind = RemindRules.Default;
            });
        }

        AddPreset("休息", 10);
        AddPreset("专注", 25);
        AddPreset("整理", 15);
    }
}
