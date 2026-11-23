using System.Collections.ObjectModel;
using DayNew.Core;
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

    public ObservableCollection<PersonVM> Persons { get; } = new();

    public ObservableCollection<LocationVM> Locations { get; } = new();

    public ObservableCollection<SchedulePresetVM> Presets { get; } = new();

    /// <summary>墓碑集合（Del=true），仅用于多端同步传播删除语义，不进入 UI 活跃列表。</summary>
    public List<ScheduleDto> DeletedSchedules { get; } = new();
    public List<GoalDto> DeletedGoals { get; } = new();
    public List<TagDto> DeletedTags { get; } = new();
    public List<PersonDto> DeletedPersons { get; } = new();
    public List<LocationDto> DeletedLocations { get; } = new();
    public List<PresetDto> DeletedPresets { get; } = new();

    /// <summary>上次落盘/加载的字段基线，用于精确检测哪些字段发生了实质变化再推进字段 HLC。</summary>
    internal Dictionary<string, ScheduleDto> BaselineSchedules { get; } = new(StringComparer.Ordinal);
    internal Dictionary<string, GoalDto> BaselineGoals { get; } = new(StringComparer.Ordinal);
    internal Dictionary<string, TagDto> BaselineTags { get; } = new(StringComparer.Ordinal);
    internal Dictionary<string, PersonDto> BaselinePersons { get; } = new(StringComparer.Ordinal);
    internal Dictionary<string, LocationDto> BaselineLocations { get; } = new(StringComparer.Ordinal);
    internal Dictionary<string, PresetDto> BaselinePresets { get; } = new(StringComparer.Ordinal);

    /// <summary>加载旧版 data.json 时未包含 presets 字段，由上层决定是否补默认预设。</summary>
    public bool HasLoadedPresets { get; set; }

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

    /// <summary>删标签：先从所有日程和快捷预设上摘掉，再删实体并记录同步墓碑。</summary>
    public void RemoveTag(TagVM tag)
    {
        foreach (var schedule in Schedules) schedule.Tags.Remove(tag);
        foreach (var preset in Presets) preset.Tags.Remove(tag);
        if (Tags.Remove(tag))
        {
            var dto = StoreFile.BuildRawTagDto(tag);
            dto.Del = true;
            dto.Hlc = HlcClock.Tick();
            UpsertTombstone(DeletedTags, dto, t => t.Id);
            BaselineTags.Remove(tag.Id);
        }
    }

    // ---------------- 人物 ----------------

    public PersonVM? FindPerson(string name)
    {
        var trimmed = (name ?? "").Trim().TrimStart('@');
        if (trimmed.Length == 0) return null;

        return Persons.FirstOrDefault(p => string.Equals(p.Name, trimmed, StringComparison.OrdinalIgnoreCase));
    }

    public PersonVM? GetOrCreatePerson(string name)
    {
        var trimmed = (name ?? "").Trim().TrimStart('@');
        if (trimmed.Length == 0) return null;

        var existing = FindPerson(trimmed);
        if (existing is not null) return existing;

        var person = new PersonVM(trimmed);
        Persons.Add(person);
        return person;
    }

    /// <summary>删人物：先从所有日程和快捷预设上摘掉，再删实体并记录同步墓碑。</summary>
    public void RemovePerson(PersonVM person)
    {
        foreach (var schedule in Schedules) schedule.Persons.Remove(person);
        foreach (var preset in Presets) preset.Persons.Remove(person);
        if (Persons.Remove(person))
        {
            var dto = StoreFile.BuildRawPersonDto(person);
            dto.Del = true;
            dto.Hlc = HlcClock.Tick();
            UpsertTombstone(DeletedPersons, dto, p => p.Id);
            BaselinePersons.Remove(person.Id);
        }
    }

    // ---------------- 地点 ----------------

    public LocationVM? FindLocation(string name)
    {
        var trimmed = (name ?? "").Trim();
        if (trimmed.Length == 0) return null;

        return Locations.FirstOrDefault(l => string.Equals(l.Name, trimmed, StringComparison.OrdinalIgnoreCase));
    }

    public LocationVM? GetOrCreateLocation(string name)
    {
        var trimmed = (name ?? "").Trim();
        if (trimmed.Length == 0) return null;

        var existing = FindLocation(trimmed);
        if (existing is not null) return existing;

        var location = new LocationVM(trimmed);
        Locations.Add(location);
        return location;
    }

    /// <summary>删地点：先从所有日程和快捷预设上摘掉，再删实体并记录同步墓碑。</summary>
    public void RemoveLocation(LocationVM location)
    {
        foreach (var schedule in Schedules) schedule.Locations.Remove(location);
        foreach (var preset in Presets) preset.Locations.Remove(location);
        if (Locations.Remove(location))
        {
            var dto = StoreFile.BuildRawLocationDto(location);
            dto.Del = true;
            dto.Hlc = HlcClock.Tick();
            UpsertTombstone(DeletedLocations, dto, l => l.Id);
            BaselineLocations.Remove(location.Id);
        }
    }

    // ---------------- 目标 ----------------

    public GoalVM AddGoal(string title)
    {
        var goal = new GoalVM { Title = title, Color = NextGoalColor() };
        goal.AutoLayout();
        Goals.Add(goal);
        return goal;
    }

    public void RemoveGoal(GoalVM goal)
    {
        if (Goals.Remove(goal))
        {
            var dto = StoreFile.BuildRawGoalDto(goal);
            dto.Del = true;
            dto.Hlc = HlcClock.Tick();
            UpsertTombstone(DeletedGoals, dto, g => g.Id);
            BaselineGoals.Remove(goal.Id);
        }
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

    public void RemoveSchedule(TaskVM schedule)
    {
        if (Schedules.Remove(schedule))
        {
            var dto = StoreFile.BuildRawScheduleDto(schedule);
            dto.Del = true;
            dto.Hlc = HlcClock.Tick();
            UpsertTombstone(DeletedSchedules, dto, s => s.Id);
            BaselineSchedules.Remove(schedule.Id);
        }
    }

    /// <summary>把顺序值重新压紧成 1024、2048……方便后续插入。</summary>
    public void Reindex(IEnumerable<TaskVM> ordered)
    {
        var i = 0;
        foreach (var schedule in ordered) schedule.Order = ++i * 1024;
        _scheduleOrder = Math.Max(_scheduleOrder, i);
    }

    // ---------------- 快捷预设 ----------------

    public SchedulePresetVM AddPreset(Action<SchedulePresetVM>? configure = null)
    {
        var preset = new SchedulePresetVM();
        configure?.Invoke(preset);
        Presets.Add(preset);
        return preset;
    }

    public void RemovePreset(SchedulePresetVM preset)
    {
        if (Presets.Remove(preset))
        {
            var dto = StoreFile.BuildRawPresetDto(preset);
            dto.Del = true;
            dto.Hlc = HlcClock.Tick();
            UpsertTombstone(DeletedPresets, dto, p => p.Id);
            BaselinePresets.Remove(preset.Id);
        }
    }

    internal static void UpsertTombstone<T>(List<T> tombstones, T item, Func<T, string> getId)
    {
        var id = getId(item);
        if (string.IsNullOrWhiteSpace(id)) return;
        for (var i = 0; i < tombstones.Count; i++)
        {
            if (string.Equals(getId(tombstones[i]), id, StringComparison.Ordinal))
            {
                tombstones[i] = item;
                return;
            }
        }
        tombstones.Add(item);
    }
}
