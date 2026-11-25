using System.Collections.ObjectModel;
using DayNew.Core;

namespace DayNew.ViewModels;

public enum TagPageTab
{
    Tags,
    Persons,
    Locations
}

/// <summary>标签页的一行：标签名 + 用了多少次。</summary>
public sealed class TagRowVM
{
    public TagRowVM(TagVM tag, int count)
    {
        Tag = tag;
        Count = count;
    }

    /// <summary>直接拿着实体本身，不复制名字——改名只改一处。</summary>
    public TagVM Tag { get; }

    public int Count { get; }

    public string CountText => Count == 0 ? "暂无关联日程" : $"{Count} 条日程";

    public override string ToString() => Tag.Name;
}

/// <summary>人物卡片：人物实体 + 关联日程数。</summary>
public sealed class PersonRowVM
{
    public PersonRowVM(PersonVM person, int count)
    {
        Person = person;
        Count = count;
    }

    public PersonVM Person { get; }

    public int Count { get; }

    public string CountText => Count == 0 ? "暂无关联日程" : $"{Count} 条日程";

    public override string ToString() => Person.Name;
}

/// <summary>地点卡片：地点实体 + 关联日程数。</summary>
public sealed class LocationRowVM
{
    public LocationRowVM(LocationVM location, int count)
    {
        Location = location;
        Count = count;
    }

    public LocationVM Location { get; }

    public int Count { get; }

    public string CountText => Count == 0 ? "暂无关联日程" : $"{Count} 条日程";

    public override string ToString() => Location.Name;
}

/// <summary>标签详情弹窗里按日划分的一条纵向日期列。</summary>
public sealed class TagDayColumnVM : ObservableObject
{
    public TagDayColumnVM(DateTime date)
    {
        Date = date.Date;
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

    public ObservableCollection<TaskVM> Items { get; } = new();

    public int Count => Items.Count;

    public bool HasItems => Items.Count > 0;

    public override string ToString() => $"{DayName} {DayNumber} ({Count})";
}

/// <summary>艾森豪威尔的一个象限。</summary>
public sealed class QuadrantVM : ObservableObject
{
    private bool _isDropTarget;

    public QuadrantVM(bool urgent, bool important, string title, string note)
    {
        IsUrgent = urgent;
        IsImportant = important;
        Title = title;
        Note = note;
    }

    public bool IsUrgent { get; }

    public bool IsImportant { get; }

    public string Title { get; }

    /// <summary>这一格该怎么用。</summary>
    public string Note { get; }

    public ObservableCollection<TaskVM> Items { get; } = new();

    public int Count => Items.Count;

    public bool IsDropTarget
    {
        get => _isDropTarget;
        set => Set(ref _isDropTarget, value);
    }

    public void RaiseCount() => OnPropertyChanged(nameof(Count));

    public override string ToString() => $"{Title} ({Count})";
}
