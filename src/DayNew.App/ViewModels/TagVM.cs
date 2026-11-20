using System.Windows.Media;
using DayNew.Core;

namespace DayNew.ViewModels;

/// <summary>
/// 标签。底层实体之一 —— 全库只有一份，日程里存的是对它的引用（不是字符串）。
/// 所以改名只改这一处，所有用到它的日程立刻跟着变。
/// </summary>
public sealed class TagVM : ObservableObject
{
    private string _name;

    public TagVM(string name, string? id = null)
    {
        Id = string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString("N")[..8] : id;
        _name = name;
    }

    public string Id { get; }

    /// <summary>强调色。标签在卡片上就是一个色点 + 文字，一眼能分堆。</summary>
    public Brush Color { get; set; } = Brushes.Transparent;

    public string Name
    {
        get => _name;
        set => Set(ref _name, value.Trim().TrimStart('#'));
    }

    public override string ToString() => Name;
}
