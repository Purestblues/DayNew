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

    /// <summary>为 null 表示老版本文件，加载时需补默认预设；空列表表示用户主动清空。</summary>
    public List<PresetDto>? Presets { get; set; }
}

public sealed class GoalDto
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Color { get; set; } = "#527CE6";
    public bool IsCompleted { get; set; }
    /// <summary>yyyy-MM-dd；空 = 未设截止日期。</summary>
    public string? Due { get; set; }
    public double? RootX { get; set; }
    public double? RootY { get; set; }
    public List<GoalNodeDto> Nodes { get; set; } = new();
    public List<GoalEdgeDto> Edges { get; set; } = new();
    public string Hlc { get; set; } = "";
    public bool Del { get; set; }
    public Dictionary<string, string>? FieldHlcs { get; set; }
}

public sealed class GoalNodeDto
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public bool IsCompleted { get; set; }
    /// <summary>yyyy-MM-dd；空 = 未设截止日期。</summary>
    public string? Due { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
}

public sealed class GoalEdgeDto
{
    public string FromId { get; set; } = "";
    public string ToId { get; set; } = "";
    public string Kind { get; set; } = "belong";
    public string? Label { get; set; }
    public string? FromSide { get; set; }
    public string? ToSide { get; set; }
}

public sealed class GoalRefDto
{
    public string GoalId { get; set; } = "";
    public string? NodeId { get; set; }
}

public sealed class TagDto
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Color { get; set; } = "";
    public string Hlc { get; set; } = "";
    public bool Del { get; set; }
    public Dictionary<string, string>? FieldHlcs { get; set; }
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

    public List<GoalRefDto> GoalRefs { get; set; } = new();

    public List<string> TagIds { get; set; } = new();

    public int Order { get; set; }

    public string Hlc { get; set; } = "";

    public bool Del { get; set; }

    public Dictionary<string, string>? FieldHlcs { get; set; }
}

public sealed class PresetDto
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public int DurationMinutes { get; set; } = 10;
    public string? Remind { get; set; }
    public string Note { get; set; } = "";
    public bool Urgent { get; set; }
    public bool Important { get; set; }
    public string? GoalId { get; set; }
    public List<GoalRefDto> GoalRefs { get; set; } = new();
    public List<string> TagIds { get; set; } = new();
    public string Hlc { get; set; } = "";
    public bool Del { get; set; }
    public Dictionary<string, string>? FieldHlcs { get; set; }
}

/// <summary>每设备上传到 WebDAV 的全量 gzip 快照契约（device-&lt;deviceId&gt;.json.gz）。</summary>
public sealed class DeviceSnapshotDto
{
    public int Version { get; set; } = 1;
    public string DeviceId { get; set; } = "";
    public string SnapshotHlc { get; set; } = "";
    public StoreDto Store { get; set; } = new();
    public List<CompletionLog.CompletionEntry> Completions { get; set; } = new();
}
