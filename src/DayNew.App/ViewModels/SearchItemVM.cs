using System.Windows.Media;
using DayNew.Core;

namespace DayNew.ViewModels;

public enum SearchResultKind
{
    Schedule,
    Goal,
    Tag,
    Person,
    Location
}

/// <summary>居中搜索浮层（Command Palette）的单个结果条目。</summary>
public sealed class SearchItemVM : ObservableObject
{
    private bool _isSelected;

    public required SearchResultKind Kind { get; init; }
    public required string KindBadge { get; init; }
    public required string Title { get; init; }
    public string Subtitle { get; init; } = "";
    public Brush? AccentBrush { get; init; }
    public bool HasAccent => AccentBrush is not null;
    public bool IsDone { get; init; }

    public TaskVM? Task { get; init; }
    public GoalVM? Goal { get; init; }
    public TagVM? Tag { get; init; }
    public PersonVM? Person { get; init; }
    public LocationVM? Location { get; init; }

    public bool IsSelected
    {
        get => _isSelected;
        set => Set(ref _isSelected, value);
    }
}
