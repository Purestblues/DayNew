using System.Windows;
using System.Windows.Media;
using DayNew.Core;

namespace DayNew.ViewModels;

/// <summary>目标树结构编辑弹窗状态：支持就地改名、可选截止日期、四边拖出连线、连线文字说明与删除、自动排版。</summary>
public sealed class GoalTreeEditorVM : ObservableObject
{
    private readonly MainViewModel _owner;
    private GoalVM? _goal;
    private GoalNodeVM? _selectedNode;
    private GoalNodeVM? _linkSourceNode;
    private bool _isLinkingBelong;
    private bool _isLinkingProgress;
    private double _zoom = 1.0;
    private double _panX = 20;
    private double _panY = 20;

    // 四边悬停拖出连线预览与松手类型选择浮层状态
    private bool _isDragConnecting;
    private GoalNodeVM? _dragSourceNode;
    private GoalPortSide _dragSourceSide;
    private GoalNodeVM? _dragTargetHighlightNode;
    private Geometry? _dragConnectGeometry;
    private Geometry? _dragConnectArrowGeometry;

    private bool _isConnectMenuOpen;
    private bool _connectMenuHasTargetNode;
    private double _connectMenuX;
    private double _connectMenuY;
    private GoalNodeVM? _pendingSourceNode;
    private GoalPortSide _pendingSourceSide;
    private GoalNodeVM? _pendingTargetNode;
    private GoalPortSide _pendingTargetSide;
    private Point _pendingDropCanvasPoint;

    public GoalTreeEditorVM(MainViewModel owner) => _owner = owner;

    public GoalVM? Goal
    {
        get => _goal;
        private set
        {
            if (!Set(ref _goal, value)) return;
            OnPropertyChanged(nameof(HeaderTitle));
        }
    }

    public string HeaderTitle => Goal is null ? "编辑目标树" : $"编辑目标树 · {Goal.Title}";

    public GoalNodeVM? SelectedNode
    {
        get => _selectedNode;
        private set
        {
            if (ReferenceEquals(_selectedNode, value)) return;
            if (_selectedNode is not null) _selectedNode.IsSelectedInEditor = false;
            _selectedNode = value;
            if (_selectedNode is not null) _selectedNode.IsSelectedInEditor = true;
            OnPropertyChanged();
            RaiseSelectedNodeProperties();
        }
    }

    public string SelectedNodeTitle
    {
        get => SelectedNode?.Title ?? "";
        set
        {
            if (SelectedNode is null) return;
            if (SelectedNode.Title == value) return;
            SelectedNode.Title = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HeaderTitle));
            _owner.OnGoalStructureChanged(rebuildTimelineAndCards: true);
        }
    }

    public bool CanEditSelected => SelectedNode is not null;

    public bool CanAddSibling => SelectedNode is { IsRoot: false };

    public bool CanDeleteSelected => SelectedNode is { IsRoot: false };

    public string SelectedKindHint => SelectedNode is null
        ? "未选中节点"
        : SelectedNode.IsRoot ? "总目标（根节点）" : "子目标节点";

    public string SiblingButtonToolTip => CanAddSibling
        ? "与当前子目标共享同一父节点，新增并列子目标"
        : "当前选中「总目标（根节点）」，已是最高层，无法添加并列子目标；请先选中一个子目标或点击「+ 归属子目标」";

    public string DeleteButtonToolTip => CanDeleteSelected
        ? "删除当前选中的子目标（其下游节点会自动回挂）"
        : "总目标（根节点）不可在此删除；如需删除整个目标，请在目标卡片右下角点击删除";

    public bool SelectedNodeHasDue => SelectedNode?.HasDue ?? false;

    public bool SelectedNodeDueIsOverdue => SelectedNode?.IsDueOverdue ?? false;

    public string SelectedNodeDueText => SelectedNode is { HasDue: true }
        ? SelectedNode.DueBadgeText
        : "设截止日期";

    public bool IsLinkingBelong
    {
        get => _isLinkingBelong;
        private set
        {
            if (!Set(ref _isLinkingBelong, value)) return;
            OnPropertyChanged(nameof(IsLinking));
            OnPropertyChanged(nameof(LinkHint));
        }
    }

    public bool IsLinkingProgress
    {
        get => _isLinkingProgress;
        private set
        {
            if (!Set(ref _isLinkingProgress, value)) return;
            OnPropertyChanged(nameof(IsLinking));
            OnPropertyChanged(nameof(LinkHint));
        }
    }

    public bool IsLinking => IsLinkingBelong || IsLinkingProgress;

    public bool IsDragConnecting
    {
        get => _isDragConnecting;
        private set
        {
            if (!Set(ref _isDragConnecting, value)) return;
            OnPropertyChanged(nameof(LinkHint));
        }
    }

    public Geometry? DragConnectGeometry
    {
        get => _dragConnectGeometry;
        private set => Set(ref _dragConnectGeometry, value);
    }

    public Geometry? DragConnectArrowGeometry
    {
        get => _dragConnectArrowGeometry;
        private set => Set(ref _dragConnectArrowGeometry, value);
    }

    public bool IsConnectMenuOpen
    {
        get => _isConnectMenuOpen;
        private set => Set(ref _isConnectMenuOpen, value);
    }

    public bool ConnectMenuHasTargetNode
    {
        get => _connectMenuHasTargetNode;
        private set
        {
            if (!Set(ref _connectMenuHasTargetNode, value)) return;
            OnPropertyChanged(nameof(ConnectMenuHeader));
        }
    }

    public double ConnectMenuX
    {
        get => _connectMenuX;
        private set => Set(ref _connectMenuX, value);
    }

    public double ConnectMenuY
    {
        get => _connectMenuY;
        private set => Set(ref _connectMenuY, value);
    }

    public string ConnectMenuHeader => ConnectMenuHasTargetNode
        ? $"连接到「{_pendingTargetNode?.DisplayTitle}」"
        : "在落点处新建并连接";

    public string LinkHint
    {
        get
        {
            if (IsDragConnecting)
                return "拖向另一目标卡片松手可选择「归属 / 递进」连线，拖到空白处松手可就地新建子目标";
            if (IsLinking)
                return $"从「{_linkSourceNode?.DisplayTitle}」{(IsLinkingBelong ? "建立归属连线" : "建立递进连线")}：请点击画布上另一个目标子目标";
            return "单击选中 · 双击卡片改名 · 悬停卡片四边圆点拖出连线 · 悬停连线可加说明或删除 · 滚轮缩放 · 右键/空格拖画布";
        }
    }

    public double Zoom
    {
        get => _zoom;
        private set
        {
            var clamped = Math.Clamp(value, GoalVM.MinZoom, GoalVM.MaxZoom);
            if (Set(ref _zoom, clamped)) OnPropertyChanged(nameof(ZoomLabel));
        }
    }

    public string ZoomLabel => $"{(int)Math.Round(Zoom * 100)}%";

    public double PanX
    {
        get => _panX;
        set => Set(ref _panX, value);
    }

    public double PanY
    {
        get => _panY;
        set => Set(ref _panY, value);
    }

    public void Open(GoalVM goal)
    {
        ClearLinkMode();
        CancelPortDrag();
        CancelConnectMenu();
        Goal = goal;
        ClearAllInlineStates(goal);
        SelectNodeOnly(goal.RootNode);
        ResetViewport();
    }

    public void Close()
    {
        if (Goal is not null) ClearAllInlineStates(Goal);
        ClearLinkMode();
        CancelPortDrag();
        CancelConnectMenu();
        SelectNodeOnly(null);
        Goal = null;
    }

    private static void ClearAllInlineStates(GoalVM goal)
    {
        foreach (var n in goal.AllCanvasNodes)
        {
            n.IsEditingInline = false;
            n.IsEditorHovered = false;
            n.IsLinkTargetHighlight = false;
        }
        foreach (var e in goal.Edges)
        {
            e.IsHovered = false;
            e.IsEditingLabel = false;
        }
    }

    public void ResetViewport()
    {
        if (Goal is null)
        {
            Zoom = 1.0;
            PanX = 20;
            PanY = 20;
            return;
        }

        const double availW = 900;
        const double availH = 390;
        var fitX = availW / Math.Max(1, Goal.CanvasWidth);
        var fitY = availH / Math.Max(1, Goal.CanvasHeight);
        Zoom = Math.Clamp(Math.Min(1.0, Math.Min(fitX, fitY)), 0.5, 1.0);
        PanX = Math.Max(16, (availW - Goal.CanvasWidth * Zoom) / 2.0);
        PanY = Math.Max(16, (availH - Goal.CanvasHeight * Zoom) / 2.0);
    }

    public void ZoomAt(double factor, Point viewportPos)
    {
        CancelConnectMenu();
        var oldZoom = Zoom;
        var newZoom = Math.Clamp(oldZoom * factor, GoalVM.MinZoom, GoalVM.MaxZoom);
        if (Math.Abs(newZoom - oldZoom) < 0.0001) return;

        var cx = (viewportPos.X - PanX) / oldZoom;
        var cy = (viewportPos.Y - PanY) / oldZoom;
        Zoom = newZoom;
        PanX = viewportPos.X - cx * newZoom;
        PanY = viewportPos.Y - cy * newZoom;
    }

    public void SelectNodeOnly(GoalNodeVM? node)
    {
        SelectedNode = node;
    }

    public void HandleNodeClick(GoalNodeVM node)
    {
        if (Goal is null) return;
        CancelConnectMenu();

        if (IsLinking && _linkSourceNode is not null)
        {
            if (ReferenceEquals(_linkSourceNode, node))
            {
                ClearLinkMode();
                return;
            }

            var kind = IsLinkingProgress ? GoalEdgeKind.Progress : GoalEdgeKind.Belong;
            var ok = Goal.TryAddOrUpdateEdge(_linkSourceNode.Id, node.Id, kind, out var msg, toggleIfSame: true);
            _owner.NotifyFromChild(msg, highlight: !ok);
            if (ok)
            {
                ClearLinkMode();
                SelectNodeOnly(node);
                _owner.OnGoalStructureChanged(rebuildTimelineAndCards: true);
            }
            return;
        }

        SelectNodeOnly(node);
    }

    public GoalNodeVM? AddChild(GoalEdgeKind kind)
    {
        if (Goal is null) return null;

        CancelConnectMenu();
        ClearLinkMode();
        FinishAnyInlineNodeEdit();

        var parent = SelectedNode ?? Goal.RootNode;
        var defaultTitle = kind == GoalEdgeKind.Progress ? "递进子目标" : "新子目标";
        var created = Goal.AddChildSubGoal(parent, defaultTitle, kind);
        SelectNodeOnly(created);
        created.IsEditingInline = true;
        _owner.OnGoalStructureChanged(rebuildTimelineAndCards: true);
        return created;
    }

    public GoalNodeVM? AddSibling()
    {
        if (Goal is null || SelectedNode is null || SelectedNode.IsRoot) return null;

        CancelConnectMenu();
        ClearLinkMode();
        FinishAnyInlineNodeEdit();

        var created = Goal.AddSiblingSubGoal(SelectedNode, "并列子目标");
        if (created is not null)
        {
            SelectNodeOnly(created);
            created.IsEditingInline = true;
            _owner.OnGoalStructureChanged(rebuildTimelineAndCards: true);
        }
        return created;
    }

    public void StartInlineNodeEdit(GoalNodeVM node)
    {
        if (Goal is null) return;
        CancelConnectMenu();
        FinishAnyInlineNodeEdit(except: node);
        SelectNodeOnly(node);
        node.IsEditingInline = true;
    }

    public void CommitInlineNodeEdit(GoalNodeVM node)
    {
        if (!node.IsEditingInline) return;
        node.IsEditingInline = false;

        if (!node.IsRoot && string.IsNullOrWhiteSpace(node.Title))
            node.Title = "未命名子目标";

        OnPropertyChanged(nameof(SelectedNodeTitle));
        OnPropertyChanged(nameof(HeaderTitle));
        _owner.OnGoalStructureChanged(rebuildTimelineAndCards: true);
    }

    public void FinishAnyInlineNodeEdit(GoalNodeVM? except = null)
    {
        if (Goal is null) return;
        foreach (var n in Goal.AllCanvasNodes)
        {
            if (!ReferenceEquals(n, except) && n.IsEditingInline)
                CommitInlineNodeEdit(n);
        }
    }

    public void SetNodeDue(GoalNodeVM node, DateTime? due, TimeSpan? dueTime = null)
    {
        node.Due = due?.Date;
        node.DueTime = due.HasValue ? dueTime : null;
        if (ReferenceEquals(node, SelectedNode))
            RaiseSelectedNodeDueProperties();
        _owner.OnGoalStructureChanged(rebuildTimelineAndCards: false);
        _owner.NotifyFromChild(due.HasValue ? $"已设截止日期：{node.DueBadgeText}" : "已清除截止日期");
    }

    public void ToggleLinkMode(GoalEdgeKind kind)
    {
        if (Goal is null) return;
        CancelConnectMenu();
        var wantBelong = kind == GoalEdgeKind.Belong;
        var wantProgress = kind == GoalEdgeKind.Progress;

        if ((wantBelong && IsLinkingBelong) || (wantProgress && IsLinkingProgress))
        {
            ClearLinkMode();
            return;
        }

        ClearLinkMode();
        _linkSourceNode = SelectedNode ?? Goal.RootNode;
        _linkSourceNode.IsLinkSource = true;
        IsLinkingBelong = wantBelong;
        IsLinkingProgress = wantProgress;
    }

    public void ClearLinkMode()
    {
        if (_linkSourceNode is not null)
        {
            _linkSourceNode.IsLinkSource = false;
            _linkSourceNode = null;
        }

        IsLinkingBelong = false;
        IsLinkingProgress = false;
    }

    // ---------------- draw.io 风格四边悬停拖拽连线 ----------------

    public void BeginPortDrag(GoalNodeVM sourceNode, GoalPortSide sourceSide, Point canvasPoint)
    {
        if (Goal is null) return;
        ClearLinkMode();
        CancelConnectMenu();
        FinishAnyInlineNodeEdit();

        _dragSourceNode = sourceNode;
        _dragSourceSide = sourceSide;
        sourceNode.IsLinkSource = true;
        IsDragConnecting = true;
        UpdatePortDrag(canvasPoint, null, GoalPortSide.Auto);
    }

    public void UpdatePortDrag(Point canvasPoint, GoalNodeVM? snapTarget, GoalPortSide snapSide)
    {
        if (!IsDragConnecting || _dragSourceNode is null) return;

        if (!ReferenceEquals(_dragTargetHighlightNode, snapTarget))
        {
            if (_dragTargetHighlightNode is not null) _dragTargetHighlightNode.IsLinkTargetHighlight = false;
            _dragTargetHighlightNode = snapTarget;
            if (_dragTargetHighlightNode is not null) _dragTargetHighlightNode.IsLinkTargetHighlight = true;
        }

        var start = _dragSourceNode.GetPortPoint(_dragSourceSide);
        Point end;
        Vector inDir;
        if (snapTarget is not null && !ReferenceEquals(snapTarget, _dragSourceNode))
        {
            var resolvedSide = snapSide == GoalPortSide.Auto
                ? snapTarget.NearestPortSide(canvasPoint)
                : snapSide;
            end = snapTarget.GetPortPoint(resolvedSide);
            inDir = GoalEdgeVM.SideNormal(resolvedSide);
        }
        else
        {
            end = canvasPoint;
            var back = start - end;
            if (back.Length > 1) back.Normalize();
            else back = new Vector(-1, 0);
            inDir = back;
        }

        var outDir = GoalEdgeVM.SideNormal(_dragSourceSide);
        var dist = (end - start).Length;
        var handle = Math.Clamp(dist * 0.45, 28.0, 140.0);
        var c1 = start + outDir * handle;
        var c2 = end + inDir * handle;

        var fig = new PathFigure(start, new PathSegment[] { new BezierSegment(c1, c2, end, true) }, false);
        var geo = new PathGeometry(new[] { fig });
        geo.Freeze();
        DragConnectGeometry = geo;

        var dir = end - c2;
        if (dir.Length < 0.01) dir = end - start;
        if (dir.Length < 0.01) dir = new Vector(1, 0);
        dir.Normalize();
        var perp = new Vector(-dir.Y, dir.X);
        var p1 = end - dir * 8 + perp * 4;
        var p2 = end - dir * 8 - perp * 4;
        var arrowFig = new PathFigure(end, new PathSegment[] { new LineSegment(p1, true), new LineSegment(p2, true) }, true);
        var arrowGeo = new PathGeometry(new[] { arrowFig });
        arrowGeo.Freeze();
        DragConnectArrowGeometry = arrowGeo;
    }

    public void CompletePortDrag(
        Point canvasPoint,
        Point viewportPoint,
        double viewportWidth,
        double viewportHeight,
        GoalNodeVM? targetNode,
        GoalPortSide targetSide)
    {
        var source = _dragSourceNode;
        var sourceSide = _dragSourceSide;
        CancelPortDrag();

        if (Goal is null || source is null) return;

        var startPt = source.GetPortPoint(sourceSide);
        if ((canvasPoint - startPt).Length < 10 && (targetNode is null || ReferenceEquals(targetNode, source)))
        {
            _owner.NotifyFromChild("请按住边缘圆点拖向另一张卡片或空白处松手");
            return;
        }

        if (targetNode is not null && ReferenceEquals(targetNode, source))
            return;

        if (targetNode is not null && targetNode.IsRoot)
        {
            _owner.NotifyFromChild("不能把连线指回总目标", highlight: true);
            return;
        }

        _pendingSourceNode = source;
        _pendingSourceSide = sourceSide;
        _pendingTargetNode = targetNode;
        _pendingTargetSide = targetNode is not null
            ? (targetSide == GoalPortSide.Auto ? targetNode.NearestPortSide(canvasPoint) : targetSide)
            : GoalPortSide.Auto;
        _pendingDropCanvasPoint = canvasPoint;

        ConnectMenuHasTargetNode = targetNode is not null;
        const double menuW = 196;
        const double menuH = 104;
        ConnectMenuX = Math.Clamp(viewportPoint.X + 8, 8, Math.Max(8, viewportWidth - menuW - 8));
        ConnectMenuY = Math.Clamp(viewportPoint.Y + 8, 8, Math.Max(8, viewportHeight - menuH - 8));
        OnPropertyChanged(nameof(ConnectMenuHeader));
        IsConnectMenuOpen = true;
    }

    public void CancelPortDrag()
    {
        if (_dragSourceNode is not null)
        {
            _dragSourceNode.IsLinkSource = false;
            _dragSourceNode = null;
        }
        if (_dragTargetHighlightNode is not null)
        {
            _dragTargetHighlightNode.IsLinkTargetHighlight = false;
            _dragTargetHighlightNode = null;
        }
        IsDragConnecting = false;
        DragConnectGeometry = null;
        DragConnectArrowGeometry = null;
    }

    public GoalNodeVM? ConfirmConnectMenu(GoalEdgeKind kind)
    {
        var goal = Goal;
        var source = _pendingSourceNode;
        var sourceSide = _pendingSourceSide;
        var target = _pendingTargetNode;
        var targetSide = _pendingTargetSide;
        var dropPt = _pendingDropCanvasPoint;

        CancelConnectMenu();
        if (goal is null || source is null) return null;

        if (target is not null)
        {
            var ok = goal.TryAddOrUpdateEdge(source.Id, target.Id, kind, out var msg, sourceSide, targetSide, toggleIfSame: false);
            _owner.NotifyFromChild(msg, highlight: !ok);
            if (ok)
            {
                SelectNodeOnly(target);
                _owner.OnGoalStructureChanged(rebuildTimelineAndCards: true);
            }
            return null;
        }

        // 拖到空白处松手：就在落点处新建子目标并连线，同时进入就地改名
        var defaultTitle = kind == GoalEdgeKind.Progress ? "递进子目标" : "新子目标";
        var pos = new Point(
            dropPt.X - GoalNodeVM.DefaultWidth / 2.0,
            dropPt.Y - GoalNodeVM.DefaultHeight / 2.0);
        var created = goal.AddChildSubGoal(source, defaultTitle, kind, pos, sourceSide, GoalPortSide.Auto);
        var (sx, sy) = goal.NormalizeMinBounds();
        if (sx > 0 || sy > 0)
        {
            PanX -= sx * Zoom;
            PanY -= sy * Zoom;
        }

        SelectNodeOnly(created);
        created.IsEditingInline = true;
        _owner.OnGoalStructureChanged(rebuildTimelineAndCards: true);
        return created;
    }

    public void CancelConnectMenu()
    {
        IsConnectMenuOpen = false;
        _pendingSourceNode = null;
        _pendingTargetNode = null;
    }

    // ---------------- 连线文字说明与删除 ----------------

    public void StartEdgeLabelEdit(GoalEdgeVM edge)
    {
        if (Goal is null) return;
        CancelConnectMenu();
        foreach (var e in Goal.Edges)
        {
            if (!ReferenceEquals(e, edge)) e.IsEditingLabel = false;
        }
        edge.IsHovered = false;
        edge.IsEditingLabel = true;
    }

    public void CommitEdgeLabelEdit(GoalEdgeVM edge)
    {
        if (!edge.IsEditingLabel) return;
        edge.Label = (edge.Label ?? "").Trim();
        edge.IsEditingLabel = false;
        _owner.OnGoalStructureChanged(rebuildTimelineAndCards: false);
    }

    public void RemoveEdge(GoalEdgeVM edge)
    {
        if (Goal is null) return;
        CancelConnectMenu();
        if (Goal.RemoveEdge(edge, out var msg))
        {
            _owner.NotifyFromChild(msg);
            _owner.OnGoalStructureChanged(rebuildTimelineAndCards: true);
        }
    }

    public void DeleteSelected()
    {
        if (Goal is null || SelectedNode is null || SelectedNode.IsRoot) return;
        ClearLinkMode();
        CancelConnectMenu();

        var toDelete = SelectedNode;
        SelectNodeOnly(Goal.RootNode);
        _owner.RemoveGoalSubNode(Goal, toDelete);
    }

    public void AutoLayout()
    {
        if (Goal is null) return;
        CancelConnectMenu();
        Goal.AutoLayout();
        _owner.OnGoalStructureChanged(rebuildTimelineAndCards: false);
    }

    public void CommitNodeDrag()
    {
        if (Goal is null) return;
        var (sx, sy) = Goal.NormalizeMinBounds();
        if (sx > 0 || sy > 0)
        {
            PanX -= sx * Zoom;
            PanY -= sy * Zoom;
        }
        _owner.OnGoalStructureChanged(rebuildTimelineAndCards: false);
    }

    private void RaiseSelectedNodeProperties()
    {
        OnPropertyChanged(nameof(SelectedNodeTitle));
        OnPropertyChanged(nameof(CanEditSelected));
        OnPropertyChanged(nameof(CanAddSibling));
        OnPropertyChanged(nameof(CanDeleteSelected));
        OnPropertyChanged(nameof(SelectedKindHint));
        OnPropertyChanged(nameof(SiblingButtonToolTip));
        OnPropertyChanged(nameof(DeleteButtonToolTip));
        RaiseSelectedNodeDueProperties();
    }

    private void RaiseSelectedNodeDueProperties()
    {
        OnPropertyChanged(nameof(SelectedNodeHasDue));
        OnPropertyChanged(nameof(SelectedNodeDueIsOverdue));
        OnPropertyChanged(nameof(SelectedNodeDueText));
    }
}

/// <summary>选择目标树节点的弹窗状态（供日程编辑/快捷卡片/概览拖拽复用）。</summary>
public sealed class GoalTreePickerVM : ObservableObject
{
    private GoalVM? _goal;
    private Action<IReadOnlyList<GoalNodeVM>>? _onConfirm;
    private double _zoom = 1.0;
    private double _panX = 20;
    private double _panY = 20;

    public GoalVM? Goal
    {
        get => _goal;
        private set
        {
            if (!Set(ref _goal, value)) return;
            OnPropertyChanged(nameof(HeaderTitle));
        }
    }

    public string HeaderTitle => Goal is null ? "选择所属目标" : $"选择「{Goal.Title}」归属目标（可多选）";

    public int PickedCount => Goal?.AllCanvasNodes.Count(n => n.IsPicked) ?? 0;

    public string SummaryText => PickedCount == 0
        ? "点击导图上的子目标或总目标进行多选（滚轮缩放 · 右键/空格拖拽）"
        : $"已选 {PickedCount} 项（可跨分支多选）";

    public double Zoom
    {
        get => _zoom;
        private set
        {
            var clamped = Math.Clamp(value, GoalVM.MinZoom, GoalVM.MaxZoom);
            if (Set(ref _zoom, clamped)) OnPropertyChanged(nameof(ZoomLabel));
        }
    }

    public string ZoomLabel => $"{(int)Math.Round(Zoom * 100)}%";

    public double PanX
    {
        get => _panX;
        set => Set(ref _panX, value);
    }

    public double PanY
    {
        get => _panY;
        set => Set(ref _panY, value);
    }

    public void Open(GoalVM goal, IEnumerable<GoalBindingVM> existingBindings, Action<IReadOnlyList<GoalNodeVM>> onConfirm)
    {
        ClearNodePickFlags();
        Goal = goal;
        _onConfirm = onConfirm;

        foreach (var binding in existingBindings)
        {
            if (!ReferenceEquals(binding.Goal, goal)) continue;
            if (binding.Node is null)
                goal.RootNode.IsPicked = true;
            else
                binding.Node.IsPicked = true;
        }

        ResetViewport();
        RaisePickSummary();
    }

    public void ResetViewport()
    {
        if (Goal is null)
        {
            Zoom = 1.0;
            PanX = 20;
            PanY = 20;
            return;
        }

        const double availW = 740;
        const double availH = 380;
        var fitX = availW / Math.Max(1, Goal.CanvasWidth);
        var fitY = availH / Math.Max(1, Goal.CanvasHeight);
        Zoom = Math.Clamp(Math.Min(1.0, Math.Min(fitX, fitY)), 0.5, 1.0);
        PanX = Math.Max(16, (availW - Goal.CanvasWidth * Zoom) / 2.0);
        PanY = Math.Max(16, (availH - Goal.CanvasHeight * Zoom) / 2.0);
    }

    public void ZoomAt(double factor, Point viewportPos)
    {
        var oldZoom = Zoom;
        var newZoom = Math.Clamp(oldZoom * factor, GoalVM.MinZoom, GoalVM.MaxZoom);
        if (Math.Abs(newZoom - oldZoom) < 0.0001) return;

        var cx = (viewportPos.X - PanX) / oldZoom;
        var cy = (viewportPos.Y - PanY) / oldZoom;
        Zoom = newZoom;
        PanX = viewportPos.X - cx * newZoom;
        PanY = viewportPos.Y - cy * newZoom;
    }

    public void TogglePick(GoalNodeVM node)
    {
        node.IsPicked = !node.IsPicked;
        RaisePickSummary();
    }

    public void ClearPicks()
    {
        ClearNodePickFlags();
        RaisePickSummary();
    }

    public void Confirm()
    {
        if (Goal is null) return;
        var picked = Goal.AllCanvasNodes.Where(n => n.IsPicked).ToList();
        var callback = _onConfirm;
        Close();
        callback?.Invoke(picked);
    }

    public void Close()
    {
        ClearNodePickFlags();
        _onConfirm = null;
        Goal = null;
        RaisePickSummary();
    }

    private void ClearNodePickFlags()
    {
        if (Goal is null) return;
        foreach (var node in Goal.AllCanvasNodes)
            node.IsPicked = false;
    }

    private void RaisePickSummary()
    {
        OnPropertyChanged(nameof(PickedCount));
        OnPropertyChanged(nameof(SummaryText));
    }
}
