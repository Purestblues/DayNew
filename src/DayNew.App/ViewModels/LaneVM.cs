using System.Collections.ObjectModel;
using DayNew.Core;

namespace DayNew.ViewModels;

public enum LaneKind
{
    Overdue,
    Today,
    Tomorrow,
    ThisWeek,
    Later
}

public sealed class LaneVM : ObservableObject
{
    private bool _isDropTarget;

    public LaneVM(LaneKind kind, string title)
    {
        Kind = kind;
        Title = title;
        Items.CollectionChanged += (_, _) => OnPropertyChanged(nameof(Count));
    }

    public LaneKind Kind { get; }

    public string Title { get; }

    public bool IsOverdue => Kind == LaneKind.Overdue;

    public ObservableCollection<TaskVM> Items { get; } = new();

    public int Count => Items.Count;

    public bool IsDropTarget
    {
        get => _isDropTarget;
        set => Set(ref _isDropTarget, value);
    }

    public override string ToString() => $"{Title} ({Count})";
}
