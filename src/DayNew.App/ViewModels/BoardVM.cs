using System.Collections.ObjectModel;
using DayNew.Core;

namespace DayNew.ViewModels;

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

    public string CountText => Count == 0 ? "" : $"{Count} 条日程";

    public override string ToString() => Tag.Name;
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
