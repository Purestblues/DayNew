namespace DayNew.Services;

/// <summary>
/// 落盘用的数据结构。和实体分开写，因为：
///   1. 实体里有 Brush、INotifyPropertyChanged 这些跟存储无关的东西；
///   2. 文件格式是长期契约，不能因为重构 ViewModel 就变。
/// 时间一律存字符串（yyyy-MM-dd / HH:mm），人可以直接看、直接改。
/// </summary>
public sealed class StoreDto
{
    public int Version { get; set; } = 1;

    public List<GoalDto> Goals { get; set; } = new();

    public List<TagDto> Tags { get; set; } = new();

    public List<ScheduleDto> Schedules { get; set; } = new();
}

public sealed class GoalDto
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Color { get; set; } = "#527CE6";
}

public sealed class TagDto
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Color { get; set; } = "";
}

public sealed class ScheduleDto
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Note { get; set; } = "";

    /// <summary>yyyy-MM-dd；空 = 没有定日期。</summary>
    public string? Due { get; set; }

    /// <summary>HH:mm；空 = 不占时间段。</summary>
    public string? Start { get; set; }

    public string? End { get; set; }

    public string Repeat { get; set; } = "";
    public string Remind { get; set; } = "";
    public bool Done { get; set; }
    public bool Urgent { get; set; }
    public bool Important { get; set; }

    public string? GoalId { get; set; }

    public List<string> TagIds { get; set; } = new();

    public int Order { get; set; }
}
