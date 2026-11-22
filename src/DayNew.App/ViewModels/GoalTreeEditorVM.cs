using System.Windows;
using DayNew.Core;

namespace DayNew.ViewModels;

/// <summary>目标树结构编辑弹窗状态：支持新增归属/并列/递进子目标、连线共有子目标、改名、删除与自动排版。</summary>
public sealed class GoalTreeEditorVM : ObservableObject
{
    private readonly MainViewModel _owner;
    private GoalVM? _goal;
    private GoalNodeVM? _selectedNode;
    private GoalNodeVM? _linkSourceNode;
    private bool _isLinkingBelong;
    private bool _isLinkingProgress;
    private string _newNodeTitle = "";
    private double _zoom = 1.0;
    private double _panX = 20;
    private double _panY = 20;

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
            OnPropertyChanged(nameof(SelectedNodeTitle));
            OnPropertyChanged(nameof(CanEditSelected));
            OnPropertyChanged(nameof(CanAddSibling));
            OnPropertyChanged(nameof(CanDeleteSelected));
            OnPropertyChanged(nameof(SelectedKindHint));
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

    public string NewNodeTitle
    {
        get => _newNodeTitle;
        set => Set(ref _newNodeTitle, value);
    }

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

    public string LinkHint => IsLinking
        ? $"从「{_linkSourceNode?.DisplayTitle}」{(IsLinkingBelong ? "建立归属连线" : "建立递进连线")}：请点击画布上另一个目标子目标（再次点击已有连线可断开）"
        : "单击选中节点 · 拖拽节点微调位置 · 滚轮缩放 · 右键/空格拖拽画布";

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
        Goal = goal;
        NewNodeTitle = "";
        SelectNodeOnly(goal.RootNode);
        ResetViewport();
    }

    public void Close()
    {
        ClearLinkMode();
        SelectNodeOnly(null);
        Goal = null;
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
        const double availH = 410;
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

    public void SelectNodeOnly(GoalNodeVM? node)
    {
        SelectedNode = node;
    }

    public void HandleNodeClick(GoalNodeVM node)
    {
        if (Goal is null) return;

        if (IsLinking && _linkSourceNode is not null)
        {
            if (ReferenceEquals(_linkSourceNode, node))
            {
                ClearLinkMode();
                return;
            }

            var kind = IsLinkingProgress ? GoalEdgeKind.Progress : GoalEdgeKind.Belong;
            var ok = Goal.TryAddOrUpdateEdge(_linkSourceNode.Id, node.Id, kind, out var msg);
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

    public GoalNodeVM? AddChild(GoalEdgeKind kind, out bool shouldFocusTitle)
    {
        shouldFocusTitle = false;
        if (Goal is null) return null;

        ClearLinkMode();
        var parent = SelectedNode ?? Goal.RootNode;
        var raw = (NewNodeTitle ?? "").Trim();
        if (raw.Length == 0)
        {
            raw = kind == GoalEdgeKind.Progress ? "递进子目标" : "新子目标";
            shouldFocusTitle = true;
        }

        var created = Goal.AddChildSubGoal(parent, raw, kind);
        NewNodeTitle = "";
        SelectNodeOnly(created);
        _owner.OnGoalStructureChanged(rebuildTimelineAndCards: true);
        return created;
    }

    public GoalNodeVM? AddSibling(out bool shouldFocusTitle)
    {
        shouldFocusTitle = false;
        if (Goal is null || SelectedNode is null || SelectedNode.IsRoot) return null;

        ClearLinkMode();
        var raw = (NewNodeTitle ?? "").Trim();
        if (raw.Length == 0)
        {
            raw = "并列子目标";
            shouldFocusTitle = true;
        }

        var created = Goal.AddSiblingSubGoal(SelectedNode, raw);
        NewNodeTitle = "";
        if (created is not null)
        {
            SelectNodeOnly(created);
            _owner.OnGoalStructureChanged(rebuildTimelineAndCards: true);
        }
        return created;
    }

    public void ToggleLinkMode(GoalEdgeKind kind)
    {
        if (Goal is null) return;
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

    public void DeleteSelected()
    {
        if (Goal is null || SelectedNode is null || SelectedNode.IsRoot) return;
        ClearLinkMode();

        var toDelete = SelectedNode;
        SelectNodeOnly(Goal.RootNode);
        _owner.RemoveGoalSubNode(Goal, toDelete);
    }

    public void AutoLayout()
    {
        if (Goal is null) return;
        Goal.AutoLayout();
        _owner.OnGoalStructureChanged(rebuildTimelineAndCards: false);
    }

    public void CommitNodeDrag()
    {
        if (Goal is null) return;
        Goal.UpdateLayoutBoundsAndEdges();
        _owner.OnGoalStructureChanged(rebuildTimelineAndCards: false);
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
