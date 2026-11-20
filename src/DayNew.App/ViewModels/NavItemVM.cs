using DayNew.Core;

namespace DayNew.ViewModels;

public enum PageKind
{
    Overview,
    Timeline,
    Tags,
    Goals,
    Eisenhower,
    Overdue
}

/// <summary>左侧 dock 的一项目。Blurb 是这一页「将来要做什么」的说明，页面没做时显示它。</summary>
public sealed class NavItemVM : ObservableObject
{
    private int _badgeCount;

    public NavItemVM(PageKind kind, string title, string icon, string blurb)
    {
        Kind = kind;
        Title = title;
        Icon = icon;
        Blurb = blurb;
    }

    public PageKind Kind { get; }

    public string Title { get; }

    /// <summary>Segoe MDL2 Assets 字形码。</summary>
    public string Icon { get; }

    public string Blurb { get; }

    public int BadgeCount
    {
        get => _badgeCount;
        set
        {
            if (!Set(ref _badgeCount, value)) return;
            OnPropertyChanged(nameof(HasBadge));
        }
    }

    public bool HasBadge => _badgeCount > 0;

    public override string ToString() => HasBadge ? $"{Title} ({BadgeCount})" : Title;
}
