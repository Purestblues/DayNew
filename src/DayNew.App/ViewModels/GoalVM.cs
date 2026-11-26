using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using DayNew.Core;

namespace DayNew.ViewModels;

/// <summary>子目标连线语义：归属拆解（实线）或先后递进（虚线箭头）。</summary>
public enum GoalEdgeKind
{
    Belong = 0,
    Progress = 1
}

/// <summary>连线在目标卡片上的吸附边。</summary>
public enum GoalPortSide
{
    Auto = 0,
    Top = 1,
    Right = 2,
    Bottom = 3,
    Left = 4
}

/// <summary>目标导图中的一个节点（ROOT 代表总目标自身，其余为子目标）。</summary>
public sealed class GoalNodeVM : ObservableObject
{
    public const string RootId = "ROOT";
    public const double DefaultWidth = 156;
    public const double DefaultHeight = 42;
    public const double HeightWithDue = 56;

    private string _id;
    private string _title;
    private bool _isCompleted;
    private DateTime? _due;
    private TimeSpan? _dueTime;
    private double _x;
    private double _y;
    private int _taskCount;
    private int _doneTaskCount;
    private bool _isSelectedInEditor;
    private bool _isLinkSource;
    private bool _isPicked;
    private bool _isHovered;
    private bool _isEditingInline;
    private bool _isEditorHovered;
    private bool _isLinkTargetHighlight;

    public GoalNodeVM(GoalVM owner, string id, string title)
    {
        Owner = owner;
        _id = string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString("N")[..10] : id;
        _title = title ?? "";
    }

    public GoalVM Owner { get; }

    public string Id
    {
        get => _id;
        set { if (!string.IsNullOrWhiteSpace(value)) _id = value; }
    }

    public bool IsRoot => Id == RootId;

    public string Title
    {
        get => IsRoot ? Owner.Title : _title;
        set
        {
            if (IsRoot)
            {
                Owner.Title = value;
                return;
            }

            if (!Set(ref _title, value)) return;
            OnPropertyChanged(nameof(DisplayTitle));
            Owner.NotifyNodeTitleChanged(this);
        }
    }

    public string DisplayTitle
    {
        get
        {
            var raw = Title;
            return string.IsNullOrWhiteSpace(raw) ? (IsRoot ? "未命名目标" : "未命名子目标") : raw;
        }
    }

    public bool IsCompleted
    {
        get => IsRoot ? Owner.IsCompleted : _isCompleted;
        set
        {
            if (IsRoot)
            {
                Owner.SetGoalCompleted(value);
                return;
            }

            if (!Set(ref _isCompleted, value)) return;
            RaiseCompletionAndDueDisplay();
        }
    }

    public DateTime? Due
    {
        get => IsRoot ? Owner.Due : _due;
        set
        {
            var normalized = value?.Date;
            if (IsRoot)
            {
                Owner.Due = normalized;
                return;
            }

            var changed = Set(ref _due, normalized);
            if (normalized is null && _dueTime is not null)
            {
                _dueTime = null;
                OnPropertyChanged(nameof(DueTime));
                OnPropertyChanged(nameof(HasDueTime));
                changed = true;
            }

            if (!changed) return;
            RaiseDueDisplay();
            Owner.UpdateLayoutBoundsAndEdges();
        }
    }

    public TimeSpan? DueTime
    {
        get => IsRoot ? Owner.DueTime : _dueTime;
        set
        {
            if (IsRoot)
            {
                Owner.DueTime = value;
                return;
            }

            if (!Set(ref _dueTime, value)) return;
            OnPropertyChanged(nameof(HasDueTime));
            RaiseDueDisplay();
            Owner.UpdateLayoutBoundsAndEdges();
        }
    }

    public bool HasDue => Due.HasValue;

    public bool HasDueTime => Due.HasValue && DueTime.HasValue;

    public bool IsDueOverdue => !IsCompleted && DeadlineRules.IsOverdue(Due, DueTime);

    public string DueInputText => DeadlineRules.FormatDue(Due, DueTime) ?? "";

    public string DueBadgeText => FormatDueBadge(Due, DueTime, IsCompleted);

    internal static string FormatDueBadge(DateTime? due, TimeSpan? dueTime, bool isCompleted)
    {
        if (due is not { } d) return "";
        var date = d.Date;
        var today = DateTime.Today;

        if (dueTime is not { } dt)
        {
            if (!isCompleted && date < today) return $"逾期 · {date:M月d日}";
            if (date == today) return "今天截止";
            if (date == today.AddDays(1)) return "明天截止";
            return date.Year == today.Year ? $"{date:M月d日}截止" : $"{date:yyyy-MM-dd}";
        }

        var clock = DeadlineRules.FormatClock(dt);
        if (!isCompleted && DeadlineRules.IsOverdue(date, dt))
        {
            return date == today
                ? $"逾期 · 今天 {clock}"
                : $"逾期 · {date:M月d日} {clock}";
        }

        if (date == today) return $"今天 {clock} 截止";
        if (date == today.AddDays(1)) return $"明天 {clock} 截止";
        return date.Year == today.Year ? $"{date:M月d日} {clock}" : $"{date:yyyy-MM-dd} {clock}";
    }

    public double X
    {
        get => _x;
        set => Set(ref _x, value);
    }

    public double Y
    {
        get => _y;
        set => Set(ref _y, value);
    }

    public double Width => DefaultWidth;

    public double Height => HasDue ? HeightWithDue : DefaultHeight;

    public int TaskCount
    {
        get => _taskCount;
        set
        {
            if (!Set(ref _taskCount, value)) return;
            OnPropertyChanged(nameof(HasTasks));
            OnPropertyChanged(nameof(TaskBadgeText));
            OnPropertyChanged(nameof(HoverStatusText));
        }
    }

    public int DoneTaskCount
    {
        get => _doneTaskCount;
        set
        {
            if (!Set(ref _doneTaskCount, value)) return;
            OnPropertyChanged(nameof(TaskBadgeText));
            OnPropertyChanged(nameof(HoverStatusText));
        }
    }

    public bool HasTasks => TaskCount > 0;

    public string TaskBadgeText => TaskCount > 0 ? $"{DoneTaskCount}/{TaskCount}" : "";

    public string HoverStatusText
    {
        get
        {
            var state = IsCompleted ? "已完成" : "进行中";
            var taskPart = TaskCount > 0 ? $"日程 {DoneTaskCount}/{TaskCount}" : "暂无日程";
            return HasDue ? $"{state} · {DueBadgeText} · {taskPart}" : $"{state} · {taskPart}";
        }
    }

    public string CompleteActionText => IsCompleted ? "取消完成" : "标记为完成";

    public bool IsSelectedInEditor
    {
        get => _isSelectedInEditor;
        set => Set(ref _isSelectedInEditor, value);
    }

    public bool IsLinkSource
    {
        get => _isLinkSource;
        set => Set(ref _isLinkSource, value);
    }

    public bool IsPicked
    {
        get => _isPicked;
        set => Set(ref _isPicked, value);
    }

    public bool IsHovered
    {
        get => _isHovered;
        set => Set(ref _isHovered, value);
    }

    public bool IsEditingInline
    {
        get => _isEditingInline;
        set => Set(ref _isEditingInline, value);
    }

    public bool IsEditorHovered
    {
        get => _isEditorHovered;
        set => Set(ref _isEditorHovered, value);
    }

    public bool IsLinkTargetHighlight
    {
        get => _isLinkTargetHighlight;
        set => Set(ref _isLinkTargetHighlight, value);
    }

    public Point GetPortPoint(GoalPortSide side) => side switch
    {
        GoalPortSide.Top => new Point(X + Width / 2.0, Y),
        GoalPortSide.Right => new Point(X + Width, Y + Height / 2.0),
        GoalPortSide.Bottom => new Point(X + Width / 2.0, Y + Height),
        _ => new Point(X, Y + Height / 2.0)
    };

    public GoalPortSide NearestPortSide(Point canvasPoint)
    {
        var candidates = new[]
        {
            (Side: GoalPortSide.Top, Pt: GetPortPoint(GoalPortSide.Top)),
            (Side: GoalPortSide.Right, Pt: GetPortPoint(GoalPortSide.Right)),
            (Side: GoalPortSide.Bottom, Pt: GetPortPoint(GoalPortSide.Bottom)),
            (Side: GoalPortSide.Left, Pt: GetPortPoint(GoalPortSide.Left))
        };

        var best = GoalPortSide.Left;
        var bestDist = double.MaxValue;
        foreach (var c in candidates)
        {
            var dist = (c.Pt - canvasPoint).LengthSquared;
            if (dist < bestDist)
            {
                bestDist = dist;
                best = c.Side;
            }
        }
        return best;
    }

    internal void SetCompletedRaw(bool value)
    {
        if (!Set(ref _isCompleted, value, nameof(IsCompleted))) return;
        RaiseCompletionAndDueDisplay();
    }

    internal void NotifyRootSynced()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(DisplayTitle));
        OnPropertyChanged(nameof(IsCompleted));
        RaiseCompletionAndDueDisplay();
        RaiseDueDisplay();
    }

    internal void NotifyRootDueSynced() => RaiseDueDisplay();

    private void RaiseCompletionAndDueDisplay()
    {
        OnPropertyChanged(nameof(CompleteActionText));
        OnPropertyChanged(nameof(IsDueOverdue));
        OnPropertyChanged(nameof(DueBadgeText));
        OnPropertyChanged(nameof(HoverStatusText));
    }

    private void RaiseDueDisplay()
    {
        OnPropertyChanged(nameof(Due));
        OnPropertyChanged(nameof(DueTime));
        OnPropertyChanged(nameof(HasDue));
        OnPropertyChanged(nameof(HasDueTime));
        OnPropertyChanged(nameof(IsDueOverdue));
        OnPropertyChanged(nameof(DueInputText));
        OnPropertyChanged(nameof(DueBadgeText));
        OnPropertyChanged(nameof(Height));
        OnPropertyChanged(nameof(HoverStatusText));
    }

    public override string ToString() => DisplayTitle;
}

/// <summary>导图中的一条有向边。</summary>
public sealed class GoalEdgeVM : ObservableObject
{
    private GoalEdgeKind _kind;
    private GoalPortSide _fromSide;
    private GoalPortSide _toSide;
    private string _label = "";
    private bool _isHovered;
    private bool _isEditingLabel;
    private double _midX;
    private double _midY;
    private Geometry? _lineGeometry;
    private Geometry? _arrowGeometry;

    public GoalEdgeVM(string fromId, string toId, GoalEdgeKind kind)
    {
        FromId = fromId;
        ToId = toId;
        _kind = kind;
    }

    public string FromId { get; }

    public string ToId { get; }

    public GoalEdgeKind Kind
    {
        get => _kind;
        set
        {
            if (!Set(ref _kind, value)) return;
            OnPropertyChanged(nameof(IsProgress));
            OnPropertyChanged(nameof(IsBelong));
        }
    }

    public bool IsProgress => Kind == GoalEdgeKind.Progress;

    public bool IsBelong => Kind == GoalEdgeKind.Belong;

    public GoalPortSide FromSide
    {
        get => _fromSide;
        set => Set(ref _fromSide, value);
    }

    public GoalPortSide ToSide
    {
        get => _toSide;
        set => Set(ref _toSide, value);
    }

    public string Label
    {
        get => _label;
        set
        {
            var next = value ?? "";
            if (!Set(ref _label, next)) return;
            OnPropertyChanged(nameof(HasLabel));
            OnPropertyChanged(nameof(ShowOverlay));
        }
    }

    public bool HasLabel => !string.IsNullOrWhiteSpace(Label);

    public bool IsHovered
    {
        get => _isHovered;
        set
        {
            if (!Set(ref _isHovered, value)) return;
            OnPropertyChanged(nameof(ShowOverlay));
        }
    }

    public bool IsEditingLabel
    {
        get => _isEditingLabel;
        set
        {
            if (!Set(ref _isEditingLabel, value)) return;
            OnPropertyChanged(nameof(ShowOverlay));
        }
    }

    public bool ShowOverlay => IsHovered || IsEditingLabel || HasLabel;

    public double MidX
    {
        get => _midX;
        private set => Set(ref _midX, value);
    }

    public double MidY
    {
        get => _midY;
        private set => Set(ref _midY, value);
    }

    public Geometry? LineGeometry
    {
        get => _lineGeometry;
        private set => Set(ref _lineGeometry, value);
    }

    public Geometry? ArrowGeometry
    {
        get => _arrowGeometry;
        private set => Set(ref _arrowGeometry, value);
    }

    public void UpdateGeometry(GoalNodeVM from, GoalNodeVM to, IReadOnlyList<GoalNodeVM>? allNodes = null)
    {
        var (fromSide, toSide) = ResolveSides(from, to, FromSide, ToSide);
        var start = from.GetPortPoint(fromSide);
        var end = to.GetPortPoint(toSide);

        var (c1, c2) = BuildAvoidantControls(start, end, fromSide, toSide, from, to, allNodes);

        var lineFigure = new PathFigure(start, new PathSegment[]
        {
            new BezierSegment(c1, c2, end, true)
        }, false);
        var lineGeo = new PathGeometry(new[] { lineFigure });
        lineGeo.Freeze();
        LineGeometry = lineGeo;

        var dir = end - c2;
        if (dir.Length < 0.01) dir = end - start;
        if (dir.Length < 0.01) dir = new Vector(1, 0);
        dir.Normalize();
        var perp = new Vector(-dir.Y, dir.X);

        const double arrowLen = 7.5;
        const double arrowHalfW = 3.8;
        var p1 = end - dir * arrowLen + perp * arrowHalfW;
        var p2 = end - dir * arrowLen - perp * arrowHalfW;

        var arrowFigure = new PathFigure(end, new PathSegment[]
        {
            new LineSegment(p1, true),
            new LineSegment(p2, true)
        }, true);
        var arrowGeo = new PathGeometry(new[] { arrowFigure });
        arrowGeo.Freeze();
        ArrowGeometry = arrowGeo;

        var mid = PickClearMidpoint(start, c1, c2, end, allNodes);
        MidX = mid.X;
        MidY = mid.Y;
    }

    public static (GoalPortSide From, GoalPortSide To) ResolveSides(
        GoalNodeVM from, GoalNodeVM to, GoalPortSide requestedFrom, GoalPortSide requestedTo)
    {
        var fromCenter = new Point(from.X + from.Width / 2.0, from.Y + from.Height / 2.0);
        var toCenter = new Point(to.X + to.Width / 2.0, to.Y + to.Height / 2.0);
        var dx = toCenter.X - fromCenter.X;
        var dy = toCenter.Y - fromCenter.Y;

        var resolvedFrom = requestedFrom;
        var resolvedTo = requestedTo;

        if (resolvedFrom == GoalPortSide.Auto && resolvedTo == GoalPortSide.Auto)
        {
            if (to.X >= from.X + from.Width + 16)
                return (GoalPortSide.Right, GoalPortSide.Left);
            if (from.X >= to.X + to.Width + 16)
                return (GoalPortSide.Left, GoalPortSide.Right);
            if (to.X >= from.X + from.Width * 0.5 && Math.Abs(dy) >= 24)
                return (GoalPortSide.Right, dy >= 0 ? GoalPortSide.Top : GoalPortSide.Bottom);
            if (from.X >= to.X + to.Width * 0.5 && Math.Abs(dy) >= 24)
                return (GoalPortSide.Left, dy >= 0 ? GoalPortSide.Top : GoalPortSide.Bottom);
            if (Math.Abs(dy) >= 24)
                return dy >= 0
                    ? (GoalPortSide.Bottom, GoalPortSide.Top)
                    : (GoalPortSide.Top, GoalPortSide.Bottom);
            return dx >= 0
                ? (GoalPortSide.Right, GoalPortSide.Left)
                : (GoalPortSide.Left, GoalPortSide.Right);
        }

        if (resolvedFrom == GoalPortSide.Auto)
        {
            var targetPt = to.GetPortPoint(resolvedTo);
            resolvedFrom = Math.Abs(targetPt.X - fromCenter.X) >= Math.Abs(targetPt.Y - fromCenter.Y)
                ? (targetPt.X >= fromCenter.X ? GoalPortSide.Right : GoalPortSide.Left)
                : (targetPt.Y >= fromCenter.Y ? GoalPortSide.Bottom : GoalPortSide.Top);
        }

        if (resolvedTo == GoalPortSide.Auto)
        {
            var sourcePt = from.GetPortPoint(resolvedFrom);
            resolvedTo = Math.Abs(toCenter.X - sourcePt.X) >= Math.Abs(toCenter.Y - sourcePt.Y)
                ? (toCenter.X >= sourcePt.X ? GoalPortSide.Left : GoalPortSide.Right)
                : (toCenter.Y >= sourcePt.Y ? GoalPortSide.Top : GoalPortSide.Bottom);
        }

        return (resolvedFrom, resolvedTo);
    }

    public static Vector SideNormal(GoalPortSide side) => side switch
    {
        GoalPortSide.Top => new Vector(0, -1),
        GoalPortSide.Right => new Vector(1, 0),
        GoalPortSide.Bottom => new Vector(0, 1),
        _ => new Vector(-1, 0)
    };

    public static Point EvalBezier(Point p0, Point p1, Point p2, Point p3, double t)
    {
        var u = 1.0 - t;
        var tt = t * t;
        var uu = u * u;
        var uuu = uu * u;
        var ttt = tt * t;
        return new Point(
            uuu * p0.X + 3 * uu * t * p1.X + 3 * u * tt * p2.X + ttt * p3.X,
            uuu * p0.Y + 3 * uu * t * p1.Y + 3 * u * tt * p2.Y + ttt * p3.Y);
    }

    private static (Point C1, Point C2) BuildAvoidantControls(
        Point start, Point end,
        GoalPortSide fromSide, GoalPortSide toSide,
        GoalNodeVM from, GoalNodeVM to,
        IReadOnlyList<GoalNodeVM>? allNodes)
    {
        var outDir = SideNormal(fromSide);
        var inDir = SideNormal(toSide);
        var chord = end - start;
        var dist = chord.Length;

        var opposing = Vector.Multiply(outDir, chord) < 0 || Vector.Multiply(inDir, -chord) < 0;
        var handle = opposing
            ? Math.Clamp(dist * 0.42 + 44.0, 56.0, 175.0)
            : Math.Clamp(dist * 0.46, 36.0, 155.0);

        var baseC1 = start + outDir * handle;
        var baseC2 = end + inDir * handle;

        // 水平近似对齐且反向连接时先给一个轻微弧度防止穿心
        if (opposing && Math.Abs(chord.Y) < 28 && (fromSide is GoalPortSide.Left or GoalPortSide.Right))
        {
            baseC1 = new Point(baseC1.X, baseC1.Y - 48);
            baseC2 = new Point(baseC2.X, baseC2.Y - 48);
        }
        else if (opposing && Math.Abs(chord.X) < 36 && (fromSide is GoalPortSide.Top or GoalPortSide.Bottom))
        {
            baseC1 = new Point(baseC1.X + 52, baseC1.Y);
            baseC2 = new Point(baseC2.X + 52, baseC2.Y);
        }

        if (allNodes is null || allNodes.Count <= 2)
            return (baseC1, baseC2);

        if (CountBezierCollisions(start, baseC1, baseC2, end, from, to, allNodes) == 0)
            return (baseC1, baseC2);

        var chordDir = dist > 1 ? chord / dist : new Vector(1, 0);
        var perp = new Vector(-chordDir.Y, chordDir.X);
        var mostlyHorizontal = Math.Abs(chord.X) >= Math.Abs(chord.Y);

        var bestC1 = baseC1;
        var bestC2 = baseC2;
        var bestHits = CountBezierCollisions(start, baseC1, baseC2, end, from, to, allNodes);
        var bestCost = double.MaxValue;

        // 结合垂直/水平/法线方向探测绕行控制点，彻底绕过中间或相邻节点卡片
        var offsets = new[] { 44.0, -44.0, 62.0, -62.0, 82.0, -82.0, 108.0, -108.0, 140.0, -140.0, 180.0, -180.0 };
        var axes = mostlyHorizontal
            ? new[] { new Vector(0, 1), perp, new Vector(1, 0) }
            : new[] { new Vector(1, 0), perp, new Vector(0, 1) };

        foreach (var axis in axes)
        {
            foreach (var offset in offsets)
            {
                var shift = axis * offset;
                var candC1 = baseC1 + shift;
                var candC2 = baseC2 + shift;
                var hits = CountBezierCollisions(start, candC1, candC2, end, from, to, allNodes);
                var cost = Math.Abs(offset);

                if (hits < bestHits || (hits == bestHits && cost < bestCost))
                {
                    bestHits = hits;
                    bestCost = cost;
                    bestC1 = candC1;
                    bestC2 = candC2;
                    if (bestHits == 0 && axis == axes[0])
                        return (bestC1, bestC2);
                }
            }

            if (bestHits == 0)
                return (bestC1, bestC2);
        }

        return (bestC1, bestC2);
    }

    private static int CountBezierCollisions(
        Point start, Point c1, Point c2, Point end,
        GoalNodeVM from, GoalNodeVM to,
        IReadOnlyList<GoalNodeVM> nodes)
    {
        var hits = 0;
        const int samples = 22;
        for (var i = 1; i < samples; i++)
        {
            var t = (double)i / samples;
            var pt = EvalBezier(start, c1, c2, end, t);

            foreach (var node in nodes)
            {
                if (ReferenceEquals(node, from) || ReferenceEquals(node, to))
                {
                    if (t is < 0.06 or > 0.94) continue;
                    var selfRect = new Rect(node.X + 4, node.Y + 4, Math.Max(4, node.Width - 8), Math.Max(4, node.Height - 8));
                    if (selfRect.Contains(pt)) { hits++; break; }
                }
                else
                {
                    var padRect = new Rect(node.X - 10, node.Y - 10, node.Width + 20, node.Height + 20);
                    if (padRect.Contains(pt)) { hits++; break; }
                }
            }
        }
        return hits;
    }

    private static Point PickClearMidpoint(Point start, Point c1, Point c2, Point end, IReadOnlyList<GoalNodeVM>? nodes)
    {
        var tCandidates = new[] { 0.5, 0.42, 0.58, 0.34, 0.66 };
        var fallback = EvalBezier(start, c1, c2, end, 0.5);
        if (nodes is null || nodes.Count == 0) return fallback;

        foreach (var t in tCandidates)
        {
            var pt = EvalBezier(start, c1, c2, end, t);
            var blocked = nodes.Any(n => new Rect(n.X - 18, n.Y - 14, n.Width + 36, n.Height + 28).Contains(pt));
            if (!blocked) return pt;
        }

        return fallback;
    }
}

/// <summary>日程或快捷预设到目标/子目标的绑定记录。</summary>
public sealed class GoalBindingVM
{
    public GoalBindingVM(GoalVM goal, GoalNodeVM? node)
    {
        Goal = goal;
        Node = node is { IsRoot: true } ? null : node;
    }

    public GoalVM Goal { get; }

    /// <summary>null 表示直接挂在总目标本身（ROOT）上。</summary>
    public GoalNodeVM? Node { get; }

    public string GoalId => Goal.Id;

    public string? NodeId => Node?.Id;

    public string TargetKey => Node is null ? Goal.Id : $"{Goal.Id}:{Node.Id}";

    public bool Matches(GoalVM goal, GoalNodeVM? node)
    {
        var normalized = node is { IsRoot: true } ? null : node;
        return ReferenceEquals(Goal, goal) && ReferenceEquals(Node, normalized);
    }
}

/// <summary>日程卡片上用于渲染的目标胶囊（仅含总目标或直属子目标两个层级）。</summary>
public sealed class GoalChipVM
{
    public required Brush Color { get; init; }
    public required string FullText { get; init; }
    public required string DisplayText { get; init; }
    public required bool IsRoot { get; init; }
    public required string ToolTipText { get; init; }

    public static string TruncateMiddle(string? text, int maxChars = 6)
    {
        var raw = (text ?? "").Trim();
        if (raw.Length <= maxChars) return raw;
        if (maxChars <= 3) return raw[..maxChars];

        var head = Math.Max(1, (maxChars - 2) / 2);
        var tail = Math.Max(1, maxChars - 2 - head);
        if (head + tail >= raw.Length) return raw;
        return $"{raw[..head]}…{raw[^tail..]}";
    }

    public override string ToString() => DisplayText;
}

/// <summary>远景目标：可单独作为简单目标，也可下设子目标有向图（DAG）。</summary>
public sealed class GoalVM : ObservableObject
{
    public const double MinZoom = 0.35;
    public const double MaxZoom = 2.20;

    private string _id = Guid.NewGuid().ToString("N")[..10];
    private string _title = "";
    private bool _isCompleted;
    private DateTime? _due;
    private TimeSpan? _dueTime;
    private bool _isExpanded;
    private bool _isDropTarget;
    private int _maxDepth;
    private double _canvasWidth = 420;
    private double _canvasHeight = 150;
    private double _viewZoom = 1.0;
    private double _viewPanX;
    private double _viewPanY;
    private GoalNodeVM? _hoveredNode;
    private double _hoverAnchorX;
    private double _hoverAnchorY;

    public GoalVM()
    {
        RootNode = new GoalNodeVM(this, GoalNodeVM.RootId, "");
        AllCanvasNodes.Add(RootNode);
    }

    /// <summary>稳定标识，日程靠它认目标。</summary>
    public string Id
    {
        get => _id;
        set { if (!string.IsNullOrWhiteSpace(value)) _id = value; }
    }

    /// <summary>混合逻辑时钟与字段级时间戳（仅同步使用，UI 不展示）。</summary>
    public string Hlc { get; set; } = "";
    public Dictionary<string, string> FieldHlcs { get; set; } = new(StringComparer.Ordinal);

    public string Title
    {
        get => _title;
        set
        {
            if (!Set(ref _title, value)) return;
            RootNode.NotifyRootSynced();
            Refresh();
        }
    }

    public bool IsCompleted
    {
        get => _isCompleted;
        private set
        {
            if (!Set(ref _isCompleted, value)) return;
            RootNode.NotifyRootSynced();
            Refresh();
        }
    }

    public DateTime? Due
    {
        get => _due;
        set
        {
            var normalized = value?.Date;
            var changed = Set(ref _due, normalized);
            if (normalized is null && _dueTime is not null)
            {
                _dueTime = null;
                OnPropertyChanged(nameof(DueTime));
                OnPropertyChanged(nameof(HasDueTime));
                changed = true;
            }

            if (!changed) return;
            RootNode.NotifyRootDueSynced();
            OnPropertyChanged(nameof(HasDue));
            OnPropertyChanged(nameof(IsDueOverdue));
            OnPropertyChanged(nameof(DueBadgeText));
            UpdateLayoutBoundsAndEdges();
            Refresh();
        }
    }

    public TimeSpan? DueTime
    {
        get => _dueTime;
        set
        {
            if (!Set(ref _dueTime, value)) return;
            RootNode.NotifyRootDueSynced();
            OnPropertyChanged(nameof(HasDueTime));
            OnPropertyChanged(nameof(IsDueOverdue));
            OnPropertyChanged(nameof(DueBadgeText));
            UpdateLayoutBoundsAndEdges();
            Refresh();
        }
    }

    public bool HasDue => Due.HasValue;

    public bool HasDueTime => Due.HasValue && DueTime.HasValue;

    public bool IsDueOverdue => !IsCompleted && DeadlineRules.IsOverdue(Due, DueTime);

    public string DueBadgeText => GoalNodeVM.FormatDueBadge(Due, DueTime, IsCompleted);

    /// <summary>每个目标一个固定颜色，任务卡片标题前的小圆点用它 —— 比写字省地方。</summary>
    public Brush Color { get; set; } = Brushes.Transparent;

    public bool IsExpanded
    {
        get => _isExpanded;
        set => Set(ref _isExpanded, value);
    }

    public bool IsDropTarget
    {
        get => _isDropTarget;
        set => Set(ref _isDropTarget, value);
    }

    public GoalNodeVM RootNode { get; }

    /// <summary>全部子目标节点（不含 ROOT）。</summary>
    public ObservableCollection<GoalNodeVM> Nodes { get; } = new();

    /// <summary>画布渲染节点（ROOT + 全部子目标）。</summary>
    public ObservableCollection<GoalNodeVM> AllCanvasNodes { get; } = new();

    /// <summary>节点间的归属/递进有向边。</summary>
    public ObservableCollection<GoalEdgeVM> Edges { get; } = new();

    public bool HasSubGoals => Nodes.Count > 0;

    public int MaxDepth
    {
        get => _maxDepth;
        private set => Set(ref _maxDepth, value);
    }

    public double CanvasWidth
    {
        get => _canvasWidth;
        private set => Set(ref _canvasWidth, value);
    }

    public double CanvasHeight
    {
        get => _canvasHeight;
        private set => Set(ref _canvasHeight, value);
    }

    /// <summary>目标页 Bento 网格跨列数：无子目标 1 列，简单树 2 列，复杂树 3 列。</summary>
    public int CardColumnSpan
    {
        get
        {
            if (!HasSubGoals) return 1;
            return Nodes.Count <= 4 && MaxDepth <= 2 ? 2 : 3;
        }
    }

    public double CardMaxWidth => CardColumnSpan switch
    {
        1 => 420,
        2 => 620,
        _ => 860
    };

    public double CardHeight
    {
        get
        {
            if (!HasSubGoals) return 148;
            return CardColumnSpan == 2
                ? Math.Clamp(CanvasHeight + 92, 232, 340)
                : Math.Clamp(CanvasHeight + 92, 286, 420);
        }
    }

    public double ViewZoom
    {
        get => _viewZoom;
        set
        {
            var clamped = Math.Clamp(value, MinZoom, MaxZoom);
            if (Set(ref _viewZoom, clamped))
                OnPropertyChanged(nameof(ZoomLabel));
        }
    }

    public string ZoomLabel => $"{(int)Math.Round(ViewZoom * 100)}%";

    public double ViewPanX
    {
        get => _viewPanX;
        set => Set(ref _viewPanX, value);
    }

    public double ViewPanY
    {
        get => _viewPanY;
        set => Set(ref _viewPanY, value);
    }

    public GoalNodeVM? HoveredNode
    {
        get => _hoveredNode;
        private set
        {
            if (ReferenceEquals(_hoveredNode, value)) return;
            if (_hoveredNode is not null) _hoveredNode.IsHovered = false;
            _hoveredNode = value;
            if (_hoveredNode is not null) _hoveredNode.IsHovered = true;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasHoveredNode));
        }
    }

    public bool HasHoveredNode => HoveredNode is not null;

    public double HoverAnchorX
    {
        get => _hoverAnchorX;
        private set => Set(ref _hoverAnchorX, value);
    }

    public double HoverAnchorY
    {
        get => _hoverAnchorY;
        private set => Set(ref _hoverAnchorY, value);
    }

    public void SetHoverNode(GoalNodeVM? node, double viewportWidth, double viewportHeight)
    {
        if (node is null)
        {
            HoveredNode = null;
            return;
        }

        const double popW = 216;
        const double popH = 86;

        var nodeLeft = node.X * ViewZoom + ViewPanX;
        var nodeTop = node.Y * ViewZoom + ViewPanY;
        var nodeW = node.Width * ViewZoom;
        var nodeH = node.Height * ViewZoom;

        var left = Math.Clamp(nodeLeft + (nodeW - popW) / 2.0, 6, Math.Max(6, viewportWidth - popW - 6));
        var belowY = nodeTop + nodeH + 4;
        var aboveY = nodeTop - popH - 4;
        double top;

        if (belowY + popH <= viewportHeight - 6)
        {
            top = belowY;
        }
        else if (aboveY >= 6)
        {
            top = aboveY;
        }
        else
        {
            // 当视口较矮（如中间行节点上下均放不下浮层）时，优先贴节点右侧或左侧避免底部裁切
            var rightX = nodeLeft + nodeW + 8;
            var leftX = nodeLeft - popW - 8;
            if (rightX + popW <= viewportWidth - 6)
                left = rightX;
            else if (leftX >= 6)
                left = leftX;

            top = Math.Clamp(nodeTop + (nodeH - popH) / 2.0, 6, Math.Max(6, viewportHeight - popH - 6));
        }

        HoverAnchorX = left;
        HoverAnchorY = top;
        HoveredNode = node;
    }

    public void ClearHoverNode() => HoveredNode = null;

    public void ResetViewport()
    {
        ViewZoom = 1.0;
        ViewPanX = 0;
        ViewPanY = 0;
        ClearHoverNode();
    }

    public void ZoomAt(double factor, Point viewportPos)
    {
        var oldZoom = ViewZoom;
        var newZoom = Math.Clamp(oldZoom * factor, MinZoom, MaxZoom);
        if (Math.Abs(newZoom - oldZoom) < 0.0001) return;

        var canvasX = (viewportPos.X - ViewPanX) / oldZoom;
        var canvasY = (viewportPos.Y - ViewPanY) / oldZoom;

        ViewZoom = newZoom;
        ViewPanX = viewportPos.X - canvasX * newZoom;
        ViewPanY = viewportPos.Y - canvasY * newZoom;
    }

    /// <summary>由 MainViewModel.Rebuild 填充，不手工维护。</summary>
    public ObservableCollection<TaskVM> Tasks { get; } = new();

    public int Total => Tasks.Count;

    public int DoneCount => Tasks.Count(t => t.Done);

    public int DoneSubGoalCount => Nodes.Count(n => n.IsCompleted);

    public string ProgressText
    {
        get
        {
            if (HasSubGoals)
            {
                var baseText = $"子目标 {DoneSubGoalCount}/{Nodes.Count} 完成";
                return Total > 0 ? $"{baseText} · 日程 {DoneCount}/{Total}" : baseText;
            }

            if (IsCompleted) return Total == 0 ? "已完成" : $"已完成 · {DoneCount}/{Total} 日程";
            return Total == 0 ? "还没有任务" : $"{DoneCount}/{Total} 完成";
        }
    }

    public double Progress
    {
        get
        {
            if (IsCompleted) return 1.0;
            if (HasSubGoals) return Nodes.Count == 0 ? 0 : (double)DoneSubGoalCount / Nodes.Count;
            return Total == 0 ? 0 : (double)DoneCount / Total;
        }
    }

    public GoalNodeVM? FindNode(string? id)
    {
        if (string.IsNullOrWhiteSpace(id) || id == GoalNodeVM.RootId) return RootNode;
        return Nodes.FirstOrDefault(n => n.Id == id);
    }

    /// <summary>收集 node 自身及所有下游可达子目标的 Id。</summary>
    public HashSet<string> CollectSubtreeIds(GoalNodeVM node)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        if (node.IsRoot)
        {
            ids.Add(GoalNodeVM.RootId);
            foreach (var n in Nodes) ids.Add(n.Id);
            return ids;
        }

        var queue = new Queue<string>();
        ids.Add(node.Id);
        queue.Enqueue(node.Id);

        while (queue.Count > 0)
        {
            var cur = queue.Dequeue();
            foreach (var edge in Edges)
            {
                if (edge.FromId == cur && ids.Add(edge.ToId))
                    queue.Enqueue(edge.ToId);
            }
        }

        return ids;
    }

    /// <summary>添加归属或递进下级子目标（增量找空位，不打乱已有节点位置）。</summary>
    public GoalNodeVM AddChildSubGoal(
        GoalNodeVM parent,
        string title,
        GoalEdgeKind kind,
        Point? explicitPos = null,
        GoalPortSide fromSide = GoalPortSide.Auto,
        GoalPortSide toSide = GoalPortSide.Auto)
    {
        var clean = string.IsNullOrWhiteSpace(title) ? "新子目标" : title.Trim();
        var node = new GoalNodeVM(this, Guid.NewGuid().ToString("N")[..10], clean);
        var pos = explicitPos ?? FindFreeChildPosition(parent);
        node.X = pos.X;
        node.Y = pos.Y;

        Nodes.Add(node);
        AllCanvasNodes.Add(node);
        Edges.Add(new GoalEdgeVM(parent.Id, node.Id, kind)
        {
            FromSide = fromSide,
            ToSide = toSide
        });

        // 新增未完成子目标时，取消总目标完成态避免结构变更后仍卡在已完成区
        if (_isCompleted) _isCompleted = false;

        UpdateLayoutBoundsAndEdges();
        Refresh();
        return node;
    }

    /// <summary>添加与当前节点并列的同级子目标（增量找空位，不打乱已有节点位置）。</summary>
    public GoalNodeVM? AddSiblingSubGoal(GoalNodeVM sibling, string title)
    {
        if (sibling.IsRoot) return null;

        var incoming = Edges.FirstOrDefault(e => e.ToId == sibling.Id);
        var parent = FindNode(incoming?.FromId) ?? RootNode;
        var kind = incoming?.Kind ?? GoalEdgeKind.Belong;
        var clean = string.IsNullOrWhiteSpace(title) ? "并列子目标" : title.Trim();
        var pos = FindFreeSlotNear(sibling.X, sibling.Y + sibling.Height + 18, stepY: 60);
        return AddChildSubGoal(parent, clean, kind, pos, incoming?.FromSide ?? GoalPortSide.Auto, incoming?.ToSide ?? GoalPortSide.Auto);
    }

    private Point FindFreeChildPosition(GoalNodeVM parent)
    {
        var baseX = parent.X + GoalNodeVM.DefaultWidth + 46;
        var existingChildren = Edges
            .Where(e => e.FromId == parent.Id)
            .Select(e => FindNode(e.ToId))
            .Where(n => n is not null)
            .ToList();

        var startY = existingChildren.Count > 0
            ? existingChildren.Max(c => c!.Y + c!.Height) + 18
            : parent.Y;

        return FindFreeSlotNear(baseX, Math.Max(18, startY), stepY: 60);
    }

    private Point FindFreeSlotNear(double preferredX, double preferredY, double stepY)
    {
        var x = Math.Max(16, preferredX);
        var y = Math.Max(16, preferredY);
        const double w = GoalNodeVM.DefaultWidth;
        const double h = GoalNodeVM.DefaultHeight;

        for (var i = 0; i < 40; i++)
        {
            var candY = y + i * stepY;
            var candRect = new Rect(x - 10, candY - 10, w + 20, h + 20);
            if (!AllCanvasNodes.Any(n => candRect.IntersectsWith(new Rect(n.X, n.Y, n.Width, n.Height))))
                return new Point(x, candY);
        }

        return new Point(x, y);
    }

    public bool WouldCreateCycle(string fromId, string toId)
    {
        if (fromId == toId) return true;
        if (toId == GoalNodeVM.RootId) return true;

        var visited = new HashSet<string>(StringComparer.Ordinal) { toId };
        var queue = new Queue<string>();
        queue.Enqueue(toId);

        while (queue.Count > 0)
        {
            var cur = queue.Dequeue();
            foreach (var edge in Edges)
            {
                if (edge.FromId != cur) continue;
                if (edge.ToId == fromId) return true;
                if (visited.Add(edge.ToId)) queue.Enqueue(edge.ToId);
            }
        }

        return false;
    }

    public bool TryAddOrUpdateEdge(
        string fromId,
        string toId,
        GoalEdgeKind kind,
        out string message,
        GoalPortSide fromSide = GoalPortSide.Auto,
        GoalPortSide toSide = GoalPortSide.Auto,
        bool toggleIfSame = false)
    {
        if (toId == GoalNodeVM.RootId)
        {
            message = "不能把连线指回总目标";
            return false;
        }

        if (fromId == toId)
        {
            message = "不能把节点连向自己";
            return false;
        }

        var existing = Edges.FirstOrDefault(e => e.FromId == fromId && e.ToId == toId);
        if (existing is not null)
        {
            var sideUpdated = false;
            if (fromSide != GoalPortSide.Auto && existing.FromSide != fromSide)
            {
                existing.FromSide = fromSide;
                sideUpdated = true;
            }
            if (toSide != GoalPortSide.Auto && existing.ToSide != toSide)
            {
                existing.ToSide = toSide;
                sideUpdated = true;
            }

            if (existing.Kind != kind || sideUpdated)
            {
                existing.Kind = kind;
                RefreshEdgeGeometries();
                message = existing.Kind == GoalEdgeKind.Belong ? "已更新为归属连线" : "已更新为递进连线";
                return true;
            }

            if (!toggleIfSame)
            {
                message = "这两个节点之间已有该连线（悬停连线可删除或加说明）";
                return false;
            }

            Edges.Remove(existing);
            UpdateLayoutBoundsAndEdges();
            message = "已断开该连线";
            return true;
        }

        if (WouldCreateCycle(fromId, toId))
        {
            message = "这样连接会形成闭环，无法建立";
            return false;
        }

        Edges.Add(new GoalEdgeVM(fromId, toId, kind)
        {
            FromSide = fromSide,
            ToSide = toSide
        });
        UpdateLayoutBoundsAndEdges();
        message = kind == GoalEdgeKind.Belong ? "已建立归属连线" : "已建立递进连线";
        return true;
    }

    public bool RemoveEdge(GoalEdgeVM edge, out string message)
    {
        if (!Edges.Remove(edge))
        {
            message = "未找到该连线";
            return false;
        }

        UpdateLayoutBoundsAndEdges();
        Refresh();
        message = "已删除连线";
        return true;
    }

    public bool RemoveSubGoal(GoalNodeVM node)
    {
        if (node.IsRoot) return false;

        var firstParentId = Edges.FirstOrDefault(e => e.ToId == node.Id)?.FromId ?? GoalNodeVM.RootId;
        var childEdges = Edges.Where(e => e.FromId == node.Id).ToList();

        for (var i = Edges.Count - 1; i >= 0; i--)
        {
            var e = Edges[i];
            if (e.FromId == node.Id || e.ToId == node.Id)
                Edges.RemoveAt(i);
        }

        Nodes.Remove(node);
        AllCanvasNodes.Remove(node);

        // 若被删节点的子节点失去全部上游，帮它们挂回上游父节点或总目标，防止成孤岛
        foreach (var childEdge in childEdges)
        {
            if (Nodes.All(n => n.Id != childEdge.ToId)) continue;
            var stillHasParent = Edges.Any(e => e.ToId == childEdge.ToId);
            if (!stillHasParent)
            {
                var fallbackParent = FindNode(firstParentId) is { } p && !ReferenceEquals(p, node)
                    ? p.Id
                    : GoalNodeVM.RootId;
                Edges.Add(new GoalEdgeVM(fallbackParent, childEdge.ToId, childEdge.Kind));
            }
        }

        UpdateLayoutBoundsAndEdges();
        Refresh();
        return true;
    }

    /// <summary>
    /// 标记某个节点完成状态：
    /// 标完成时，自动把下设「独有子目标」以及「所有其他上游父节点均已完成」的共有子目标一并标完成；
    /// 仍有未完成上游父节点的共有子目标不受影响。
    /// </summary>
    public void SetNodeCompleted(GoalNodeVM node, bool completed)
    {
        if (node.IsRoot)
        {
            SetGoalCompleted(completed);
            return;
        }

        node.SetCompletedRaw(completed);

        if (completed)
        {
            var queue = new Queue<GoalNodeVM>();
            queue.Enqueue(node);

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                var childIds = Edges
                    .Where(e => e.FromId == current.Id)
                    .Select(e => e.ToId)
                    .Distinct();

                foreach (var childId in childIds)
                {
                    var child = Nodes.FirstOrDefault(n => n.Id == childId);
                    if (child is null || child.IsCompleted) continue;

                    // 排除 ROOT：总目标需手动完成归档，不作为阻塞子目标自动完成的未完成父节点
                    var upstreamSubParents = Edges
                        .Where(e => e.ToId == childId && e.FromId != GoalNodeVM.RootId)
                        .Select(e => Nodes.FirstOrDefault(n => n.Id == e.FromId))
                        .Where(p => p is not null)
                        .ToList();

                    if (upstreamSubParents.Count > 0 && upstreamSubParents.All(p => p!.IsCompleted))
                    {
                        child.SetCompletedRaw(true);
                        queue.Enqueue(child);
                    }
                }
            }
        }
        else if (_isCompleted)
        {
            _isCompleted = false;
            OnPropertyChanged(nameof(IsCompleted));
            RootNode.NotifyRootSynced();
        }

        Refresh();
    }

    /// <summary>总目标完成归档或恢复：标完成时整棵树所有子目标一并完成。</summary>
    public void SetGoalCompleted(bool completed)
    {
        if (completed)
        {
            foreach (var n in Nodes) n.SetCompletedRaw(true);
        }

        IsCompleted = completed;
    }

    /// <summary>按 DAG 最长路径做自动分层布局，保证根节点在最左、下游逐层向右展开。</summary>
    public void AutoLayout()
    {
        EnsureValidEdges();

        var layerById = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            [GoalNodeVM.RootId] = 0
        };

        foreach (var node in Nodes)
            layerById[node.Id] = 1;

        var maxIters = Nodes.Count + 2;
        for (var iter = 0; iter < maxIters; iter++)
        {
            var changed = false;
            foreach (var edge in Edges)
            {
                if (!layerById.TryGetValue(edge.FromId, out var fromLayer)) continue;
                if (!layerById.TryGetValue(edge.ToId, out var toLayer)) continue;

                var candidate = fromLayer + 1;
                if (candidate > toLayer && candidate <= Nodes.Count + 1)
                {
                    layerById[edge.ToId] = candidate;
                    changed = true;
                }
            }
            if (!changed) break;
        }

        var maxLayer = layerById.Values.DefaultIfEmpty(0).Max();
        var columns = new List<GoalNodeVM>[maxLayer + 1];
        for (var i = 0; i <= maxLayer; i++) columns[i] = new List<GoalNodeVM>();

        columns[0].Add(RootNode);
        for (var layer = 1; layer <= maxLayer; layer++)
        {
            var nodesInLayer = Nodes.Where(n => layerById[n.Id] == layer).ToList();
            if (layer == 1)
            {
                columns[layer].AddRange(nodesInLayer);
            }
            else
            {
                // 按父节点在上一层的顺序排序，减少交叉线
                var orderMap = new Dictionary<string, double>(StringComparer.Ordinal);
                for (var prev = 0; prev < layer; prev++)
                {
                    for (var idx = 0; idx < columns[prev].Count; idx++)
                        orderMap[columns[prev][idx].Id] = idx;
                }

                columns[layer].AddRange(nodesInLayer.OrderBy(n =>
                {
                    var parentScores = Edges
                        .Where(e => e.ToId == n.Id && orderMap.ContainsKey(e.FromId))
                        .Select(e => orderMap[e.FromId])
                        .ToList();
                    return parentScores.Count > 0 ? parentScores.Average() : double.MaxValue;
                }));
            }
        }

        const double padX = 20;
        const double padY = 26;
        const double colStep = 198;
        const double rowStep = 66;

        var maxRows = Math.Max(1, columns.Max(c => c.Count));
        var totalHeight = (maxRows - 1) * rowStep + GoalNodeVM.HeightWithDue;

        for (var layer = 0; layer <= maxLayer; layer++)
        {
            var col = columns[layer];
            if (col.Count == 0) continue;

            var colHeight = (col.Count - 1) * rowStep + GoalNodeVM.HeightWithDue;
            var startY = padY + (totalHeight - colHeight) / 2.0;
            var x = padX + layer * colStep;

            for (var i = 0; i < col.Count; i++)
            {
                col[i].X = x;
                col[i].Y = startY + i * rowStep;
            }
        }

        UpdateLayoutBoundsAndEdges(maxLayer);
    }

    public (double ShiftX, double ShiftY) NormalizeMinBounds()
    {
        if (AllCanvasNodes.Count == 0) return (0, 0);

        var minX = AllCanvasNodes.Min(n => n.X);
        var minY = AllCanvasNodes.Min(n => n.Y);
        var shiftX = minX < 16 ? 16 - minX : 0;
        var shiftY = minY < 16 ? 16 - minY : 0;

        if (shiftX > 0 || shiftY > 0)
        {
            foreach (var node in AllCanvasNodes)
            {
                node.X += shiftX;
                node.Y += shiftY;
            }
        }

        UpdateLayoutBoundsAndEdges();
        return (shiftX, shiftY);
    }

    public void UpdateLayoutBoundsAndEdges(int? computedMaxLayer = null)
    {
        RefreshEdgeGeometries();

        var maxRight = AllCanvasNodes.Max(n => n.X + n.Width);
        var maxBottom = AllCanvasNodes.Max(n => n.Y + n.Height);

        foreach (var edge in Edges)
        {
            if (edge.LineGeometry is null) continue;
            var b = edge.LineGeometry.Bounds;
            if (!b.IsEmpty)
            {
                maxRight = Math.Max(maxRight, b.Right);
                maxBottom = Math.Max(maxBottom, b.Bottom);
            }
        }

        CanvasWidth = Math.Max(420, maxRight + 80);
        CanvasHeight = Math.Max(150, maxBottom + 36);
        MaxDepth = computedMaxLayer ?? EstimateDepth();

        RaiseLayoutProperties();
    }

    public void RefreshEdgeGeometries()
    {
        foreach (var edge in Edges)
        {
            var from = FindNode(edge.FromId);
            var to = FindNode(edge.ToId);
            if (from is not null && to is not null)
                edge.UpdateGeometry(from, to, AllCanvasNodes);
        }
    }

    private int EstimateDepth()
    {
        if (Nodes.Count == 0) return 0;
        var layerById = new Dictionary<string, int>(StringComparer.Ordinal) { [GoalNodeVM.RootId] = 0 };
        foreach (var n in Nodes) layerById[n.Id] = 1;

        for (var i = 0; i < Nodes.Count + 1; i++)
        {
            var changed = false;
            foreach (var e in Edges)
            {
                if (!layerById.TryGetValue(e.FromId, out var fl) || !layerById.TryGetValue(e.ToId, out var tl)) continue;
                if (fl + 1 > tl && fl + 1 <= Nodes.Count + 1)
                {
                    layerById[e.ToId] = fl + 1;
                    changed = true;
                }
            }
            if (!changed) break;
        }

        return layerById.Values.DefaultIfEmpty(0).Max();
    }

    private void EnsureValidEdges()
    {
        var validIds = new HashSet<string>(Nodes.Select(n => n.Id), StringComparer.Ordinal)
        {
            GoalNodeVM.RootId
        };

        for (var i = Edges.Count - 1; i >= 0; i--)
        {
            var e = Edges[i];
            if (!validIds.Contains(e.FromId) || !validIds.Contains(e.ToId) || e.ToId == GoalNodeVM.RootId || e.FromId == e.ToId)
                Edges.RemoveAt(i);
        }
    }

    private void RaiseLayoutProperties()
    {
        OnPropertyChanged(nameof(HasSubGoals));
        OnPropertyChanged(nameof(CardColumnSpan));
        OnPropertyChanged(nameof(CardMaxWidth));
        OnPropertyChanged(nameof(CardHeight));
    }

    internal void NotifyNodeTitleChanged(GoalNodeVM node)
    {
        // 触发外层重建时会同步刷 TaskVM.CompactGoalChips，这里刷新自身进度视图即可
        Refresh();
    }

    public void Refresh()
    {
        OnPropertyChanged(nameof(Total));
        OnPropertyChanged(nameof(DoneCount));
        OnPropertyChanged(nameof(DoneSubGoalCount));
        OnPropertyChanged(nameof(ProgressText));
        OnPropertyChanged(nameof(Progress));
        OnPropertyChanged(nameof(HasSubGoals));
        OnPropertyChanged(nameof(CardColumnSpan));
        OnPropertyChanged(nameof(CardMaxWidth));
        OnPropertyChanged(nameof(CardHeight));
    }

    public override string ToString() => string.IsNullOrWhiteSpace(Title) ? "未命名目标" : Title;
}
