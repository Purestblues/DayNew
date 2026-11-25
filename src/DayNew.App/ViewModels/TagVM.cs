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
    private Brush _color = Brushes.Transparent;

    public TagVM(string name, string? id = null)
    {
        Id = string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString("N")[..8] : id;
        _name = name;
    }

    public string Id { get; }

    /// <summary>混合逻辑时钟与字段级时间戳（仅同步使用，UI 不展示）。</summary>
    public string Hlc { get; set; } = "";
    public Dictionary<string, string> FieldHlcs { get; set; } = new(StringComparer.Ordinal);

    /// <summary>强调色。标签在卡片上就是一个色点 + 文字，一眼能分堆。</summary>
    public Brush Color
    {
        get => _color;
        set
        {
            if (Set(ref _color, value))
                OnPropertyChanged(nameof(ColorHex));
        }
    }

    public string ColorHex => _color is SolidColorBrush sb
        ? $"#{sb.Color.R:X2}{sb.Color.G:X2}{sb.Color.B:X2}"
        : "";

    public string Name
    {
        get => _name;
        set => Set(ref _name, value.Trim().TrimStart('#'));
    }

    public override string ToString() => Name;
}

/// <summary>
/// 人物。底层实体之一 —— 全库只有一份，日程里存的是对它的引用（不是字符串）。
/// 改名只改这一处，所有关联日程立刻同步变。
/// </summary>
public sealed class PersonVM : ObservableObject
{
    private string _name;

    public PersonVM(string name, string? id = null)
    {
        Id = string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString("N")[..8] : id;
        _name = name.Trim().TrimStart('@');
    }

    public string Id { get; }

    /// <summary>混合逻辑时钟与字段级时间戳（仅同步使用，UI 不展示）。</summary>
    public string Hlc { get; set; } = "";
    public Dictionary<string, string> FieldHlcs { get; set; } = new(StringComparer.Ordinal);

    public string Name
    {
        get => _name;
        set => Set(ref _name, value.Trim().TrimStart('@'));
    }

    public override string ToString() => Name;
}

/// <summary>
/// 地点。底层实体之一 —— 全库只有一份，日程里存的是对它的引用（不是字符串）。
/// 改名只改这一处，所有关联日程立刻同步变。
/// </summary>
public sealed class LocationVM : ObservableObject
{
    private string _name;

    public LocationVM(string name, string? id = null)
    {
        Id = string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString("N")[..8] : id;
        _name = name.Trim();
    }

    public string Id { get; }

    /// <summary>混合逻辑时钟与字段级时间戳（仅同步使用，UI 不展示）。</summary>
    public string Hlc { get; set; } = "";
    public Dictionary<string, string> FieldHlcs { get; set; } = new(StringComparer.Ordinal);

    public string Name
    {
        get => _name;
        set => Set(ref _name, value.Trim());
    }

    public override string ToString() => Name;
}
