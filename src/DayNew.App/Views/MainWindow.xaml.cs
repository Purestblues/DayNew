using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using DayNew.Core;
using DayNew.Services;
using DayNew.ViewModels;

namespace DayNew.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly Action _onHideToTray;
    private readonly Action _onExitApp;
    private readonly Action _onTestNotify;
    private readonly Rect? _savedBounds;
    private readonly WindowState? _savedState;
    private bool _allowExit;
    private bool _closingToTray;
    private bool _hasCentered;
    private bool _cleanedUp;
    private bool _syncingPasswordBox;

    private Point _pressPoint;
    private TaskVM? _pressedTask;
    private Border? _pressedCard;
    private Border? _pressedTagCard;
    private TagRowVM? _pressedTagRow;
    private Border? _pressedPersonCard;
    private PersonRowVM? _pressedPersonRow;
    private Border? _pressedLocationCard;
    private LocationRowVM? _pressedLocationRow;
    private Border? _pressedGoalPageCard;
    private GoalVM? _pressedGoalPageItem;
    private GoalVM? _pressedGoal;
    private TimelineBlockVM? _pressedBlock;
    private FrameworkElement? _pressedBlockElement;
    private bool _draggingBlock;
    private Border? _blockGhost;
    private int _blockDay;
    private int _blockMinutes;
    private bool _timelineScrolled;

    // 快捷日程预设卡片拖到时间轴直接落盘建日程
    private SchedulePresetVM? _pressedPreset;
    private FrameworkElement? _pressedPresetElement;
    private bool _draggingPreset;
    private Border? _presetGhost;
    private bool _presetGhostPlaced;
    private bool _presetGhostInBody;
    private int _presetDay;
    private int _presetMinutes;

    // 在时间轴空白处拖一段（或单击红线附近） = 新建日程
    private bool _creating;
    private int _createDay;
    private int _createAnchorMinutes;
    private bool _createAnchorSnappedToNow;
    private Border? _createGhost;
    private const double RedLineSnapPx = 10.0;
    private bool _dragging;
    private Border? _ghost;
    private LaneVM? _hoverLane;
    private GoalVM? _hoverGoal;
    private QuadrantVM? _hoverQuadrant;

    // 拖拽时给其它卡片开缝（手机桌面拖图标那种手感）：元素 → 要挪到多少像素
    private ItemsControl? _gapItems;
    private readonly Dictionary<FrameworkElement, double> _gapTargets = new();

    /// <summary>双击命中的那条日程：按下时记下，松手时开弹窗。</summary>
    private TaskVM? _pendingOpen;

    private readonly DispatcherTimer _toastTimer = new() { Interval = TimeSpan.FromMilliseconds(1800) };
    private readonly DispatcherTimer _nowLineTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    private int _toastSeq;
    private bool _blockGhostPlaced;

    private readonly DispatcherTimer _expandTimer = new() { Interval = TimeSpan.FromMilliseconds(220) };
    private TaskVM? _pendingExpandTask;
    private bool _clickedPageBlank;

    private EventHandler? _scrollAnimHandler;
    private DateTime _scrollAnimStart;
    private double _scrollFromH, _scrollToH, _scrollFromV, _scrollToV;

    private EventHandler? _tagDetailScrollAnimHandler;
    private DateTime _tagDetailScrollAnimStart;
    private double _tagDetailScrollFromH, _tagDetailScrollToH;

    // 视图平滑缩放（Alt+滚轮 / 档位按钮）
    private EventHandler? _zoomAnimHandler;
    private DateTime _zoomAnimLastTick;
    private double _zoomTargetPx;
    private double _zoomAnchorHours;
    private double _zoomAnchorViewportY;

    // 视图画布自由拖动（右键拖动 / 空格+左键拖动）
    private bool _spacePanKeyDown;
    private bool _panning;
    private bool _panWithRightButton;
    private Point _panStartPoint;
    private double _panStartH, _panStartV;

    // 视图日程块上下边缘调整时间（5 分钟为单位）
    private TimelineBlockVM? _resizeCandidateBlock;
    private bool _resizeTopEdge;
    private bool _resizingBlock;
    private int _resizeOrigStartMin, _resizeOrigEndMin;
    private int _resizeMinBound, _resizeMaxBound;
    private int _resizeCurrentStartMin, _resizeCurrentEndMin;
    private Border? _resizePill;

    // 目标导图画布交互（卡片内画布 / 结构编辑弹窗 / 目标树多选弹窗共用一套平移状态）
    private enum GoalCanvasTarget { None, Card, TreeEditor, Picker }
    private GoalCanvasTarget _goalPanTarget;
    private GoalVM? _goalPanCard;
    private bool _goalPanRightButton;
    private Point _goalPanStartPoint;
    private double _goalPanStartX, _goalPanStartY;

    // 目标卡片节点悬停信息防闪烁延时隐藏
    private readonly DispatcherTimer _goalHoverHideTimer = new() { Interval = TimeSpan.FromMilliseconds(140) };
    private GoalVM? _hoveredGoalCard;
    private bool _hoveringGoalPopover;

    // 目标树结构编辑器节点拖动 / 点击选择 / 四边拖拽连线 / 连线悬停防闪烁
    private GoalNodeVM? _pressedEditorNode;
    private bool _draggingEditorNode;
    private double _editorNodeStartX, _editorNodeStartY;
    private bool _draggingEditorPort;
    private readonly DispatcherTimer _edgeHoverHideTimer = new() { Interval = TimeSpan.FromMilliseconds(140) };
    private GoalEdgeVM? _hoveredEditorEdge;
    private bool _hoveringEdgeHit;
    private bool _hoveringEdgeOverlay;

    // 目标页大卡片内节点自由拖动
    private GoalNodeVM? _pressedCardGoalNode;
    private bool _draggingCardGoalNode;
    private double _cardNodeStartX, _cardNodeStartY;

    // 目标树多选弹窗节点点击
    private GoalNodeVM? _pressedPickerNode;

    public MainWindow(
        MainViewModel vm,
        Action onHideToTray,
        Action onExitApp,
        Action onTestNotify,
        Rect? savedBounds = null,
        WindowState? savedState = null)
    {
        _vm = vm;
        _onHideToTray = onHideToTray;
        _onExitApp = onExitApp;
        _onTestNotify = onTestNotify;
        _savedBounds = savedBounds;
        _savedState = savedState;

        InitializeComponent();
        DataContext = _vm;
        EnsurePageTemplateLoaded(_vm.SelectedNav.Kind);
        ApplyThemeIcon();
        UpdateMaxIcon();

        PreviewMouseLeftButtonDown += OnPreviewDown;
        PreviewMouseMove += OnPreviewMove;
        PreviewMouseLeftButtonUp += OnPreviewUp;
        PreviewMouseRightButtonDown += OnPreviewRightDown;
        PreviewMouseRightButtonUp += OnPreviewRightUp;
        PreviewKeyDown += OnPreviewKeyDown;
        PreviewKeyUp += OnPreviewKeyUp;
        Deactivated += OnWindowDeactivated;
        LostMouseCapture += OnWindowLostMouseCapture;
        StateChanged += OnWindowStateChangedEvent;
        Closing += OnWindowClosing;
        Closed += OnWindowClosed;

        // 不依赖 WindowStartupLocation：在多屏 / 高 DPI 下它会把窗口丢到屏幕外；首开居中或恢复上次位置
        Loaded += OnWindowLoaded;

        _vm.PropertyChanged += OnViewModelPropertyChanged;
        _vm.DeadlinePicker.PropertyChanged += OnDeadlinePickerPropertyChanged;
        _vm.ToastRequested += ShowToast;
        _vm.TaskTouched += OnTaskTouched;
        _toastTimer.Tick += OnToastTimerTick;
        _expandTimer.Tick += OnExpandTimerTick;
        _goalHoverHideTimer.Tick += OnGoalHoverHideTimerTick;
        _edgeHoverHideTimer.Tick += OnEdgeHoverHideTimerTick;
        _nowLineTimer.Tick += OnNowLineTimerTick;
        _nowLineTimer.Start();
        _vm.RefreshCurrentTime();

        NavHost.SelectionChanged += OnNavHostSelectionChanged;
    }

    private void OnWindowDeactivated(object? sender, EventArgs e) => CancelActiveInteractions();

    private void OnWindowLostMouseCapture(object? sender, MouseEventArgs e)
    {
        if (_dragging || _draggingBlock || _draggingPreset || _panning || _resizingBlock || _goalPanTarget != GoalCanvasTarget.None || _draggingEditorNode || _draggingEditorPort || _draggingCardGoalNode)
            CancelActiveInteractions();
    }

    private void OnWindowStateChangedEvent(object? sender, EventArgs e) => OnWindowStateChanged();

    private void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (_hasCentered) return;
        _hasCentered = true;
        if (_savedBounds is { } b && b.Width > 0 && b.Height > 0)
        {
            var area = SystemParameters.WorkArea;
            Width = Math.Min(b.Width, Math.Max(MinWidth, area.Width));
            Height = Math.Min(b.Height, Math.Max(MinHeight, area.Height));
            Left = Math.Clamp(b.Left, area.Left, Math.Max(area.Left, area.Right - Width));
            Top = Math.Clamp(b.Top, area.Top, Math.Max(area.Top, area.Bottom - Height));
            if (_savedState == WindowState.Maximized)
                WindowState = WindowState.Maximized;
        }
        else
        {
            CenterOnScreen();
        }

        if (_vm.IsTimeline && !_timelineScrolled)
        {
            _timelineScrolled = true;
            Dispatcher.BeginInvoke(new Action(ScrollToToday), DispatcherPriority.Loaded);
        }
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        DetachWindowResources();
        if (_allowExit) _onExitApp();
    }

    private void OnToastTimerTick(object? sender, EventArgs e) => HideToast();
    private void OnExpandTimerTick(object? sender, EventArgs e) => CommitPendingExpand();

    private void OnGoalHoverHideTimerTick(object? sender, EventArgs e)
    {
        _goalHoverHideTimer.Stop();
        if (!_hoveringGoalPopover)
        {
            _hoveredGoalCard?.ClearHoverNode();
            _hoveredGoalCard = null;
        }
    }

    private void OnEdgeHoverHideTimerTick(object? sender, EventArgs e)
    {
        _edgeHoverHideTimer.Stop();
        if (!_hoveringEdgeHit && !_hoveringEdgeOverlay && _hoveredEditorEdge is not null && !_hoveredEditorEdge.IsEditingLabel)
        {
            _hoveredEditorEdge.IsHovered = false;
            _hoveredEditorEdge = null;
        }
    }

    private void OnNowLineTimerTick(object? sender, EventArgs e) => _vm.RefreshCurrentTime();

    private void OnNavHostSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        EnsurePageTemplateLoaded(_vm.SelectedNav.Kind);
        CancelPendingExpand();
        AnimateActivePage();

        if (!_vm.IsTimeline || _timelineScrolled) return;
        _timelineScrolled = true;
        Dispatcher.BeginInvoke(new Action(ScrollToToday), DispatcherPriority.Loaded);
    }

    private void EnsurePageTemplateLoaded(PageKind kind)
    {
        (ContentControl host, string templateKey) = kind switch
        {
            PageKind.Timeline => (TimelinePageHost, "TimelinePageTemplate"),
            PageKind.Goals => (GoalsPageHost, "GoalsPageTemplate"),
            PageKind.Tags => (TagsPageHost, "TagsPageTemplate"),
            PageKind.Eisenhower => (EisenhowerPageHost, "EisenhowerPageTemplate"),
            PageKind.Deadlines => (DeadlinesPageHost, "DeadlinesPageTemplate"),
            PageKind.Future => (FuturePageHost, "FuturePageTemplate"),
            PageKind.Overdue => (OverduePageHost, "OverduePageTemplate"),
            _ => default
        };

        if (host is null || string.IsNullOrEmpty(templateKey)) return;
        var desired = (ControlTemplate)FindResource(templateKey);
        if (ReferenceEquals(host.Template, desired)) return;
        host.Template = desired;
        host.ApplyTemplate();
    }

    private static T? FindTemplateChild<T>(ContentControl host, string name) where T : class
        => host.Template?.FindName(name, host) as T;

    private ScrollViewer? BodyScroll => FindTemplateChild<ScrollViewer>(TimelinePageHost, "BodyScroll");
    private ScrollViewer? HeaderScroll => FindTemplateChild<ScrollViewer>(TimelinePageHost, "HeaderScroll");
    private ScrollViewer? GutterScroll => FindTemplateChild<ScrollViewer>(TimelinePageHost, "GutterScroll");
    private ItemsControl? DaysHost => FindTemplateChild<ItemsControl>(TimelinePageHost, "DaysHost");
    private ScrollViewer? PresetsScroll => FindTemplateChild<ScrollViewer>(TimelinePageHost, "PresetsScroll");
    private ItemsControl? QuadrantsHost => FindTemplateChild<ItemsControl>(EisenhowerPageHost, "QuadrantsHost");
    private ItemsControl? DeadlinesHost => FindTemplateChild<ItemsControl>(DeadlinesPageHost, "DeadlinesHost");
    private ItemsControl? FutureHost => FindTemplateChild<ItemsControl>(FuturePageHost, "FutureHost");
    private ItemsControl? OverdueHost => FindTemplateChild<ItemsControl>(OverduePageHost, "OverdueHost");
    private ScrollViewer? TagDetailScroll => FindTemplateChild<ScrollViewer>(TagDetailBodyHost, "TagDetailScroll");
    private ItemsControl? TagDetailDaysHost => FindTemplateChild<ItemsControl>(TagDetailBodyHost, "TagDetailDaysHost");
    private TextBox? EditorTitle => FindTemplateChild<TextBox>(EditorBodyHost, "EditorTitle");
    private PasswordBox? WebDavPasswordBox => FindTemplateChild<PasswordBox>(SettingsBodyHost, "WebDavPasswordBox");
    private TextBox? PresetTitleBox => FindTemplateChild<TextBox>(PresetManagerBodyHost, "PresetTitleBox");
    private Border? TreeEditorViewport => FindTemplateChild<Border>(GoalTreeEditorBodyHost, "TreeEditorViewport");
    private Border? TreeEditorConnectMenu => FindTemplateChild<Border>(GoalTreeEditorBodyHost, "TreeEditorConnectMenu");
    private Border? GoalPickerViewport => FindTemplateChild<Border>(GoalPickerBodyHost, "GoalPickerViewport");

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.IsEditorOpen))
            AnimateModalVisibility(EditorLayer, EditorScrim, EditorDialog, EditorScale, EditorShift,
                EditorBodyHost, "EditorBodyTemplate",
                _vm.IsEditorOpen, () => _vm.IsEditorOpen);
        else if (e.PropertyName == nameof(MainViewModel.IsSettingsOpen))
        {
            AnimateModalVisibility(SettingsLayer, SettingsScrim, SettingsDialog, SettingsScale, SettingsShift,
                SettingsBodyHost, "SettingsBodyTemplate",
                _vm.IsSettingsOpen, () => _vm.IsSettingsOpen);
            if (_vm.IsSettingsOpen) SyncWebDavPasswordBox();
        }
        else if (e.PropertyName == nameof(MainViewModel.IsImportConfirmOpen))
            AnimateModalVisibility(ImportConfirmLayer, ImportConfirmScrim, ImportConfirmDialog, ImportConfirmScale, ImportConfirmShift,
                ImportConfirmBodyHost, "ImportConfirmBodyTemplate",
                _vm.IsImportConfirmOpen, () => _vm.IsImportConfirmOpen);
        else if (e.PropertyName == nameof(MainViewModel.IsPresetManagerOpen))
            AnimateModalVisibility(PresetManagerLayer, PresetManagerScrim, PresetManagerDialog, PresetManagerScale, PresetManagerShift,
                PresetManagerBodyHost, "PresetManagerBodyTemplate",
                _vm.IsPresetManagerOpen, () => _vm.IsPresetManagerOpen);
        else if (e.PropertyName == nameof(MainViewModel.IsTagDetailOpen))
            AnimateModalVisibility(TagDetailLayer, TagDetailScrim, TagDetailDialog, TagDetailScale, TagDetailShift,
                TagDetailBodyHost, "TagDetailBodyTemplate",
                _vm.IsTagDetailOpen, () => _vm.IsTagDetailOpen);
        else if (e.PropertyName == nameof(MainViewModel.IsGoalTreeEditorOpen))
            AnimateModalVisibility(GoalTreeEditorLayer, GoalTreeEditorScrim, GoalTreeEditorDialog, GoalTreeEditorScale, GoalTreeEditorShift,
                GoalTreeEditorBodyHost, "GoalTreeEditorBodyTemplate",
                _vm.IsGoalTreeEditorOpen, () => _vm.IsGoalTreeEditorOpen);
        else if (e.PropertyName == nameof(MainViewModel.IsGoalPickerOpen))
            AnimateModalVisibility(GoalPickerLayer, GoalPickerScrim, GoalPickerDialog, GoalPickerScale, GoalPickerShift,
                GoalPickerBodyHost, "GoalPickerBodyTemplate",
                _vm.IsGoalPickerOpen, () => _vm.IsGoalPickerOpen);
    }

    private void OnDeadlinePickerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DeadlinePickerVM.IsOpen))
            AnimateModalVisibility(DeadlinePickerLayer, DeadlinePickerScrim, DeadlinePickerDialog, DeadlinePickerScale, DeadlinePickerShift,
                DeadlinePickerBodyHost, "DeadlinePickerBodyTemplate",
                _vm.DeadlinePicker.IsOpen, () => _vm.DeadlinePicker.IsOpen);
    }

    // ==================== 标题栏 ====================

    // 最大化时 Windows 会把无边框窗口往屏幕外多长一圈（这里实测每边 6 DIP），
    // 结果就是最底下的横向滚动条和状态栏被切掉。自己接管最大化尺寸：正好贴住工作区。
    private const int WmGetMinMaxInfo = 0x0024;
    private const int WmSysCommand = 0x0112;
    private const int ScKeyMenu = 0xF100;
    private const int WmMouseHWheel = 0x020E;
    private const int MonitorDefaultToNearest = 2;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(WindowHook);
    }

    private IntPtr WindowHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == App.WmShowMainWindow)
        {
            ShowFromTray();
            handled = true;
            return IntPtr.Zero;
        }

        if (msg == WmSysCommand && (wParam.ToInt64() & 0xFFF0) == ScKeyMenu)
        {
            handled = true;
            return IntPtr.Zero;
        }

        if (msg == WmMouseHWheel)
        {
            var delta = unchecked((short)((wParam.ToInt64() >> 16) & 0xFFFF));
            if (delta != 0 && HandleHorizontalWheel(delta))
            {
                handled = true;
                return IntPtr.Zero;
            }
            return IntPtr.Zero;
        }

        if (msg != WmGetMinMaxInfo) return IntPtr.Zero;

        var monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
        if (monitor == IntPtr.Zero) return IntPtr.Zero;

        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info)) return IntPtr.Zero;

        var work = info.Work;
        var screen = info.Monitor;

        var mmi = Marshal.PtrToStructure<MinMaxInfo>(lParam);
        mmi.MaxPosition.X = work.Left - screen.Left;
        mmi.MaxPosition.Y = work.Top - screen.Top;
        mmi.MaxSize.X = work.Right - work.Left;
        mmi.MaxSize.Y = work.Bottom - work.Top;
        Marshal.StructureToPtr(mmi, lParam, true);

        return IntPtr.Zero;
    }

    private bool HandleHorizontalWheel(int delta)
    {
        if (_vm.DeadlinePicker.IsOpen || _vm.IsGoalPickerOpen || _vm.IsGoalTreeEditorOpen || _vm.IsEditorOpen || _vm.IsImportConfirmOpen || _vm.IsSettingsOpen || _vm.IsPresetManagerOpen) return false;

        var pos = Mouse.GetPosition(this);
        ScrollViewer? target = null;

        if (VisualTreeHelper.HitTest(this, pos)?.VisualHit is DependencyObject hit)
        {
            var d = hit;
            while (d is not null)
            {
                if (d is ScrollViewer sv && sv.ScrollableWidth > 0)
                {
                    target = sv;
                    break;
                }
                d = VisualTreeHelper.GetParent(d);
            }
        }

        if (target is null && _vm.IsTagDetailOpen && IsInsideElement(pos, TagDetailDialog) && TagDetailScroll is { ScrollableWidth: > 0 } tds)
            target = tds;
        else if (target is null && _vm.IsTimeline && IsInsideElement(pos, PageTimeline) && BodyScroll is { ScrollableWidth: > 0 } bs)
            target = bs;

        if (target is null) return false;

        if (ReferenceEquals(target, BodyScroll)) StopScrollAnimation();
        else if (ReferenceEquals(target, TagDetailScroll)) StopTagDetailScrollAnimation();
        target.ScrollToHorizontalOffset(Math.Clamp(target.HorizontalOffset + delta * 0.35, 0, target.ScrollableWidth));
        return true;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, int flags);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [DllImport("user32.dll")]
    private static extern bool InvalidateRect(IntPtr hWnd, IntPtr lpRect, bool bErase);

    [StructLayout(LayoutKind.Sequential)]
    private struct WinPoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WinRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public WinPoint Reserved;
        public WinPoint MaxSize;
        public WinPoint MaxPosition;
        public WinPoint MinTrackSize;
        public WinPoint MaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public WinRect Monitor;
        public WinRect Work;
        public uint Flags;
    }

    private void Theme_Click(object sender, RoutedEventArgs e)
    {
        ThemeService.Toggle();
        ApplyThemeIcon();
        _vm.RefreshTimelineBrushes();
    }

    private void ApplyThemeIcon() => ThemeButton.Content = ThemeService.IsDark ? "\uE706" : "\uE708";

    private void CenterOnScreen()
    {
        var area = SystemParameters.WorkArea;
        if (area.Width <= 0 || area.Height <= 0) return;

        Left = Math.Max(area.Left, area.Left + (area.Width - ActualWidth) / 2);
        Top = Math.Max(area.Top, area.Top + (area.Height - ActualHeight) / 2);
    }

    private void OnWindowStateChanged()
    {
        UpdateMaxIcon();
        InvalidateMeasure();
        InvalidateVisual();
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;

        InvalidateRect(hwnd, IntPtr.Zero, false);
        Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() =>
        {
            InvalidateVisual();
            InvalidateRect(hwnd, IntPtr.Zero, false);
        }));
    }

    private void UpdateMaxIcon()
        => MaxButton.Content = WindowState == WindowState.Maximized ? "\uE923" : "\uE922";

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Maximize_Click(object sender, RoutedEventArgs e)
        => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    // ==================== 托盘 / 窗口显隐 / 设置 ====================

    private void OnWindowClosing(object? sender, CancelEventArgs e)
    {
        _vm.FlushSave();

        if (_closingToTray)
        {
            e.Cancel = false;
            return;
        }

        if (!_allowExit && _vm.CloseToTray)
        {
            PrepareHideToTray();
            _closingToTray = true;
            e.Cancel = false;
            return;
        }

        _allowExit = true;
    }

    public void ShowFromTray()
    {
        if (!IsVisible) Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;

        Activate();
        if (!IsActive)
        {
            Topmost = true;
            Topmost = false;
            Focus();
        }
    }

    public void CloseToTray()
    {
        if (_closingToTray || _allowExit) return;
        PrepareHideToTray();
        _closingToTray = true;
        Close();
    }

    public void ForceCloseForExit()
    {
        _allowExit = true;
        Close();
    }

    private void PrepareHideToTray()
    {
        CancelActiveInteractions();
        _vm.DeadlinePicker.Close();
        _vm.CloseGoalPicker();
        _vm.CloseGoalTreeEditor();
        _vm.CancelImportConfirm();
        _vm.CloseSettings();
        _vm.ClosePresetManager();
        _vm.CloseTagDetail();
        _vm.FlushSave();
    }

    private void DetachWindowResources()
    {
        if (_cleanedUp) return;
        _cleanedUp = true;

        _nowLineTimer.Stop();
        _toastTimer.Stop();
        _expandTimer.Stop();
        _goalHoverHideTimer.Stop();
        _edgeHoverHideTimer.Stop();

        StopScrollAnimation();
        StopTagDetailScrollAnimation();
        StopZoomAnimation();

        _vm.PropertyChanged -= OnViewModelPropertyChanged;
        _vm.DeadlinePicker.PropertyChanged -= OnDeadlinePickerPropertyChanged;
        _vm.ToastRequested -= ShowToast;
        _vm.TaskTouched -= OnTaskTouched;

        _toastTimer.Tick -= OnToastTimerTick;
        _expandTimer.Tick -= OnExpandTimerTick;
        _goalHoverHideTimer.Tick -= OnGoalHoverHideTimerTick;
        _edgeHoverHideTimer.Tick -= OnEdgeHoverHideTimerTick;
        _nowLineTimer.Tick -= OnNowLineTimerTick;

        NavHost.SelectionChanged -= OnNavHostSelectionChanged;

        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero)
            HwndSource.FromHwnd(hwnd)?.RemoveHook(WindowHook);

        DataContext = null;
    }

    private void SettingsOpen_Click(object sender, RoutedEventArgs e) => _vm.OpenSettings();

    private void SettingsClose_Click(object sender, RoutedEventArgs e) => _vm.CloseSettings();

    private void SettingsScrim_Click(object sender, MouseButtonEventArgs e) => _vm.CloseSettings();

    private void OpenDataFolder_Click(object sender, RoutedEventArgs e) => _vm.OpenDataFolder();

    private void TestNotify_Click(object sender, RoutedEventArgs e) => _onTestNotify();

    private void SettingsDefaultRemind_Click(object sender, RoutedEventArgs e)
        => HandleRemindChipClick(sender, _vm.ToggleDefaultRemindNode);

    private void ExportData_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "导出全部数据",
            DefaultExt = ".daynew.zip",
            AddExtension = true,
            Filter = "日新备份包 (*.daynew.zip)|*.daynew.zip|ZIP 压缩包 (*.zip)|*.zip",
            FileName = $"DayNew-备份-{DateTime.Now:yyyyMMdd}.daynew.zip"
        };

        if (dialog.ShowDialog(this) == true)
            _vm.ExportAllData(dialog.FileName);
    }

    private void ImportData_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择要导入的备份包或数据文件",
            Filter = "日新备份包或数据文件 (*.daynew.zip;*.zip;*.json)|*.daynew.zip;*.zip;*.json|所有文件 (*.*)|*.*"
        };

        if (dialog.ShowDialog(this) == true)
            _vm.BeginImportData(dialog.FileName);
    }

    private void ImportConfirmScrim_Click(object sender, MouseButtonEventArgs e) => _vm.CancelImportConfirm();

    private void ImportConfirmCancel_Click(object sender, RoutedEventArgs e) => _vm.CancelImportConfirm();

    private void ImportConfirmMerge_Click(object sender, RoutedEventArgs e) => _vm.ConfirmImportMerge();

    private void ImportConfirmOverwrite_Click(object sender, RoutedEventArgs e) => _vm.ConfirmImportOverwrite();

    private void SyncWebDavPasswordBox()
    {
        if (WebDavPasswordBox is not { } box || box.Password == _vm.WebDavPassword) return;
        _syncingPasswordBox = true;
        try
        {
            box.Password = _vm.WebDavPassword;
        }
        finally
        {
            _syncingPasswordBox = false;
        }
    }

    private void WebDavPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (_syncingPasswordBox || WebDavPasswordBox is not { } box) return;
        _vm.WebDavPassword = box.Password;
    }

    private async void WebDavTest_Click(object sender, RoutedEventArgs e) => await _vm.TestWebDavConnectionAsync();

    private async void WebDavUpload_Click(object sender, RoutedEventArgs e) => await _vm.UploadWebDavBackupAsync();

    private async void WebDavRestore_Click(object sender, RoutedEventArgs e) => await _vm.DownloadWebDavBackupAsync();

    private async void WebDavSyncNow_Click(object sender, RoutedEventArgs e) => await _vm.SyncWebDavNowAsync();

    private void McpApplyPort_Click(object sender, RoutedEventArgs e) => _vm.ApplyMcpPort();

    private void McpToggleReveal_Click(object sender, RoutedEventArgs e) => _vm.ToggleMcpTokenReveal();

    private void McpCopyToken_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(_vm.GetMcpTokenPlain());
            ShowToast("已复制 MCP Bearer Token", highlight: false);
        }
        catch
        {
            ShowToast("复制失败，请手动复制", highlight: true);
        }
    }

    private void McpRegenerateToken_Click(object sender, RoutedEventArgs e) => _vm.RegenerateMcpToken();

    private void McpCopyJson_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(_vm.BuildMcpJsonConfig());
            ShowToast("已复制 MCP JSON 配置片段", highlight: false);
        }
        catch
        {
            ShowToast("复制失败，请手动复制", highlight: true);
        }
    }

    private void McpCopyPrompt_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(_vm.BuildMcpInstallPrompt());
            ShowToast("已复制安装提示词（HTTP 优先、STDIO 回退）", highlight: false);
        }
        catch
        {
            ShowToast("复制失败，请手动复制", highlight: true);
        }
    }

    // ==================== 卡片内操作 ====================

    private void Check_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is TaskVM task) _vm.ToggleDone(task);
    }

    private void Repeat_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is TaskVM task) _vm.CycleRepeat(task);
    }

    private void TaskRemindChip_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: TaskVM task, Tag: string node })
        {
            _vm.ToggleTaskRemindNode(task, node);
            SyncToggleBinding(sender);
        }
    }

    private static void HandleRemindChipClick(object sender, Action<string> toggleNode)
    {
        if (sender is not FrameworkElement { Tag: string node }) return;
        toggleNode(node);
        SyncToggleBinding(sender);
    }

    private static void SyncToggleBinding(object sender)
    {
        if (sender is ToggleButton toggle)
            BindingOperations.GetBindingExpression(toggle, ToggleButton.IsCheckedProperty)?.UpdateTarget();
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is TaskVM task) _vm.Delete(task);
    }

    private void TagRemove_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: TagVM tag } fe) return;
        if (FindDataContext<TaskVM>(fe) is { } task) _vm.RemoveTag(task, tag);
    }

    private void TagInput_KeyDown(object sender, KeyEventArgs e)
        => HandleTaskChipInput(sender, e, _vm.AddTag);

    private void PersonRemove_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: PersonVM person } fe) return;
        if (FindDataContext<TaskVM>(fe) is { } task) _vm.RemovePerson(task, person);
    }

    private void PersonInput_KeyDown(object sender, KeyEventArgs e)
        => HandleTaskChipInput(sender, e, _vm.AddPerson);

    private void LocationRemove_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: LocationVM location } fe) return;
        if (FindDataContext<TaskVM>(fe) is { } task) _vm.RemoveLocation(task, location);
    }

    private void LocationInput_KeyDown(object sender, KeyEventArgs e)
        => HandleTaskChipInput(sender, e, _vm.AddLocation);

    private static void HandleTaskChipInput(object sender, KeyEventArgs e, Action<TaskVM, string> addAction)
    {
        if (sender is not TextBox box || e.Key != Key.Enter) return;

        var task = FindDataContext<TaskVM>(box);
        if (task is not null) addAction(task, box.Text);
        box.Text = "";
        e.Handled = true;
    }

    private void TitleBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Enter or Key.Escape)
        {
            _vm.CollapseAll();
            e.Handled = true;
        }
    }

    // ==================== 目标 ====================

    // ==================== 侧边栏 / 右键菜单 ====================

    private void DockToggle_Click(object sender, RoutedEventArgs e)
    {
        _vm.IsDockExpanded = !_vm.IsDockExpanded;

        DockBorder.BeginAnimation(WidthProperty,
            new DoubleAnimation(_vm.IsDockExpanded ? 196 : 56, TimeSpan.FromMilliseconds(200))
            {
                EasingFunction = Motion.EaseOut
            });

        DockToggleRotate.BeginAnimation(RotateTransform.AngleProperty,
            new DoubleAnimation(_vm.IsDockExpanded ? 0 : 180, Motion.S)
            {
                EasingFunction = Motion.EaseOut
            });
    }

    private static TaskVM? MenuTask(object sender)
    {
        if (sender is MenuItem { DataContext: TaskVM task }) return task;

        // ContextMenu 的 DataContext 是从 PlacementTarget 继承的，拿不到时退一步再找
        return sender is MenuItem { Parent: ContextMenu { PlacementTarget: FrameworkElement fe } }
            ? fe.DataContext as TaskVM
            : null;
    }

    private void MenuToggleDone_Click(object sender, RoutedEventArgs e)
    {
        if (MenuTask(sender) is { } task) _vm.ToggleDone(task);
    }

    private void MenuMoveToday_Click(object sender, RoutedEventArgs e)
    {
        if (MenuTask(sender) is { } task) _vm.MoveToDay(task, DateTime.Today);
    }

    private void MenuMoveTomorrow_Click(object sender, RoutedEventArgs e)
    {
        if (MenuTask(sender) is { } task) _vm.MoveToDay(task, DateTime.Today.AddDays(1));
    }

    private void MenuMoveFuture_Click(object sender, RoutedEventArgs e)
    {
        if (MenuTask(sender) is { } task) _vm.MoveToFuture(task);
    }

    private void AddDeadline_Click(object sender, RoutedEventArgs e)
    {
        _vm.CollapseAll();
        _vm.OpenNewDeadlineSchedule();
        FocusEditorTitle();
    }

    private void AddFuture_Click(object sender, RoutedEventArgs e)
    {
        _vm.CollapseAll();
        _vm.OpenNewFutureSchedule();
        FocusEditorTitle();
    }

    private void DeadlineGoal_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: DeadlineGoalItemVM item }) return;
        _vm.OpenGoalTreeEditor(item.Goal);
        if (item.Node is { } node)
            _vm.GoalTreeEditor.SelectNodeOnly(node);
    }

    private void MenuDelete_Click(object sender, RoutedEventArgs e)
    {
        if (MenuTask(sender) is { } task) _vm.Delete(task);
    }

    private void GoalInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        _vm.AddGoalFromInput();
        e.Handled = true;
    }

    private void DeleteGoal_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: GoalVM goal }) _vm.DeleteGoal(goal);
    }

    private void OpenGoalDetail_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: GoalVM goal }) OpenGoalDetailModal(goal);
    }

    private void OpenGoalTreeEditor_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: GoalVM goal }) _vm.OpenGoalTreeEditor(goal);
    }

    private void ResetGoalCanvas_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: GoalVM goal }) goal.ResetViewport();
    }

    private void ToggleGoalComplete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: GoalVM goal }) _vm.ToggleGoalCompleted(goal);
    }

    private void GoalCardSetDue_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: GoalVM goal }) _vm.OpenGoalDeadlinePicker(goal);
    }

    private void GoalCardCanvas_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not FrameworkElement viewport || viewport.DataContext is not GoalVM goal) return;
        e.Handled = true;
        goal.ClearHoverNode();
        var factor = Math.Pow(1.14, e.Delta / 120.0);
        goal.ZoomAt(factor, e.GetPosition(viewport));
    }

    private void GoalCardNode_MouseEnter(object sender, MouseEventArgs e)
    {
        if (_goalPanTarget != GoalCanvasTarget.None || _dragging || _draggingCardGoalNode) return;
        if (sender is not FrameworkElement { DataContext: GoalNodeVM node } el) return;
        if (FindAncestorTag(el, "goalcanvas") is not FrameworkElement viewport) return;

        _goalHoverHideTimer.Stop();
        _hoveringGoalPopover = false;

        if (_hoveredGoalCard is not null && !ReferenceEquals(_hoveredGoalCard, node.Owner))
            _hoveredGoalCard.ClearHoverNode();

        _hoveredGoalCard = node.Owner;
        node.Owner.SetHoverNode(node, viewport.ActualWidth, viewport.ActualHeight);
    }

    private void GoalCardNode_MouseLeave(object sender, MouseEventArgs e) => _goalHoverHideTimer.Start();

    private void GoalCardHoverPopover_MouseEnter(object sender, MouseEventArgs e)
    {
        _hoveringGoalPopover = true;
        _goalHoverHideTimer.Stop();
    }

    private void GoalCardHoverPopover_MouseLeave(object sender, MouseEventArgs e)
    {
        _hoveringGoalPopover = false;
        _goalHoverHideTimer.Start();
    }

    private void GoalCardCanvas_MouseLeave(object sender, MouseEventArgs e)
    {
        if (!_hoveringGoalPopover) _goalHoverHideTimer.Start();
    }

    private void GoalCardNodeToggleComplete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: GoalNodeVM node })
            _vm.ToggleGoalNodeCompleted(node.Owner, node);
    }

    private void GoalCardNodeDetail_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: GoalNodeVM node }) return;
        node.Owner.ClearHoverNode();
        OpenDetailModal(() => _vm.OpenGoalNodeDetail(node.Owner, node));
    }

    private void GoalCardNodeSetDue_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: GoalNodeVM node }) return;
        node.Owner.ClearHoverNode();
        _vm.OpenNodeDeadlinePicker(node);
    }

    // ==================== 目标树结构编辑弹窗 ====================

    private void GoalTreeEditorClose_Click(object sender, RoutedEventArgs e) => _vm.CloseGoalTreeEditor();

    private void GoalTreeEditorScrim_Click(object sender, MouseButtonEventArgs e) => _vm.CloseGoalTreeEditor();

    private void TreeEditorDeadline_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.GoalTreeEditor.SelectedNode is { } node)
            _vm.OpenNodeDeadlinePicker(node);
    }

    private void TreeEditorClearDeadline_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.GoalTreeEditor.SelectedNode is { } node)
            _vm.GoalTreeEditor.SetNodeDue(node, null);
    }

    private void TreeEditorAddBelong_Click(object sender, RoutedEventArgs e)
        => _vm.GoalTreeEditor.AddChild(GoalEdgeKind.Belong);

    private void TreeEditorAddSibling_Click(object sender, RoutedEventArgs e)
        => _vm.GoalTreeEditor.AddSibling();

    private void TreeEditorAddProgress_Click(object sender, RoutedEventArgs e)
        => _vm.GoalTreeEditor.AddChild(GoalEdgeKind.Progress);

    private void TreeEditorLinkBelong_Click(object sender, RoutedEventArgs e)
        => _vm.GoalTreeEditor.ToggleLinkMode(GoalEdgeKind.Belong);

    private void TreeEditorLinkProgress_Click(object sender, RoutedEventArgs e)
        => _vm.GoalTreeEditor.ToggleLinkMode(GoalEdgeKind.Progress);

    private void TreeEditorAutoLayout_Click(object sender, RoutedEventArgs e)
        => _vm.GoalTreeEditor.AutoLayout();

    private void TreeEditorDeleteNode_Click(object sender, RoutedEventArgs e)
        => _vm.GoalTreeEditor.DeleteSelected();

    private void TreeEditorResetView_Click(object sender, RoutedEventArgs e)
        => _vm.GoalTreeEditor.ResetViewport();

    private void TreeEditorConnectBelong_Click(object sender, RoutedEventArgs e)
        => _vm.GoalTreeEditor.ConfirmConnectMenu(GoalEdgeKind.Belong);

    private void TreeEditorConnectProgress_Click(object sender, RoutedEventArgs e)
        => _vm.GoalTreeEditor.ConfirmConnectMenu(GoalEdgeKind.Progress);

    private void TreeEditorViewport_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        e.Handled = true;
        var factor = Math.Pow(1.14, e.Delta / 120.0);
        _vm.GoalTreeEditor.ZoomAt(factor, e.GetPosition(TreeEditorViewport));
    }

    private void TreeEditorNode_MouseEnter(object sender, MouseEventArgs e)
    {
        if (_draggingEditorPort || _draggingEditorNode || _goalPanTarget != GoalCanvasTarget.None) return;
        if (sender is FrameworkElement { DataContext: GoalNodeVM node })
            node.IsEditorHovered = true;
    }

    private void TreeEditorNode_MouseLeave(object sender, MouseEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: GoalNodeVM node })
            node.IsEditorHovered = false;
    }

    private void TreeEditorInlineBox_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is TextBox { IsVisible: true } box)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                box.Focus();
                box.SelectAll();
            }), DispatcherPriority.Input);
        }
    }

    private void TreeEditorInlineBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox { DataContext: GoalNodeVM node }) return;
        if (e.Key is Key.Enter or Key.Escape)
        {
            _vm.GoalTreeEditor.CommitInlineNodeEdit(node);
            Keyboard.ClearFocus();
            e.Handled = true;
        }
    }

    private void TreeEditorInlineBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox { DataContext: GoalNodeVM node })
            _vm.GoalTreeEditor.CommitInlineNodeEdit(node);
    }

    private void TreeEdgeHit_MouseEnter(object sender, MouseEventArgs e)
    {
        if (_draggingEditorPort || _draggingEditorNode || _goalPanTarget != GoalCanvasTarget.None) return;
        if (sender is not FrameworkElement { DataContext: GoalEdgeVM edge }) return;

        _edgeHoverHideTimer.Stop();
        _hoveringEdgeHit = true;

        if (_hoveredEditorEdge is not null && !ReferenceEquals(_hoveredEditorEdge, edge))
            _hoveredEditorEdge.IsHovered = false;

        _hoveredEditorEdge = edge;
        edge.IsHovered = true;
    }

    private void TreeEdgeHit_MouseLeave(object sender, MouseEventArgs e)
    {
        _hoveringEdgeHit = false;
        _edgeHoverHideTimer.Start();
    }

    private void TreeEdgeOverlay_MouseEnter(object sender, MouseEventArgs e)
    {
        if (_draggingEditorPort || _draggingEditorNode || _goalPanTarget != GoalCanvasTarget.None) return;
        if (sender is not FrameworkElement { DataContext: GoalEdgeVM edge }) return;

        _edgeHoverHideTimer.Stop();
        _hoveringEdgeOverlay = true;

        if (_hoveredEditorEdge is not null && !ReferenceEquals(_hoveredEditorEdge, edge))
            _hoveredEditorEdge.IsHovered = false;

        _hoveredEditorEdge = edge;
        edge.IsHovered = true;
    }

    private void TreeEdgeOverlay_MouseLeave(object sender, MouseEventArgs e)
    {
        _hoveringEdgeOverlay = false;
        _edgeHoverHideTimer.Start();
    }

    private void TreeEdgeAddNote_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: GoalEdgeVM edge }) return;
        _hoveringEdgeHit = false;
        _hoveringEdgeOverlay = false;
        _hoveredEditorEdge = null;
        _vm.GoalTreeEditor.StartEdgeLabelEdit(edge);
    }

    private void TreeEdgeDelete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: GoalEdgeVM edge }) return;
        _hoveringEdgeHit = false;
        _hoveringEdgeOverlay = false;
        _hoveredEditorEdge = null;
        _vm.GoalTreeEditor.RemoveEdge(edge);
    }

    private void TreeEdgeLabelBox_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is TextBox { IsVisible: true } box)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                box.Focus();
                box.SelectAll();
            }), DispatcherPriority.Input);
        }
    }

    private void TreeEdgeLabelBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox { DataContext: GoalEdgeVM edge }) return;
        if (e.Key is Key.Enter or Key.Escape)
        {
            _vm.GoalTreeEditor.CommitEdgeLabelEdit(edge);
            Keyboard.ClearFocus();
            e.Handled = true;
        }
    }

    private void TreeEdgeLabelBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox { DataContext: GoalEdgeVM edge })
            _vm.GoalTreeEditor.CommitEdgeLabelEdit(edge);
    }

    // ==================== 目标截止日期选择浮层 ====================

    private void DeadlinePickerClose_Click(object sender, RoutedEventArgs e) => _vm.DeadlinePicker.Close();

    private void DeadlinePickerScrim_Click(object sender, MouseButtonEventArgs e) => _vm.DeadlinePicker.Close();

    private void DeadlineToday_Click(object sender, RoutedEventArgs e) => _vm.DeadlinePicker.PickToday();

    private void DeadlineTomorrow_Click(object sender, RoutedEventArgs e) => _vm.DeadlinePicker.PickTomorrow();

    private void DeadlineEndOfWeek_Click(object sender, RoutedEventArgs e) => _vm.DeadlinePicker.PickEndOfWeek();

    private void DeadlineNextWeek_Click(object sender, RoutedEventArgs e) => _vm.DeadlinePicker.PickNextWeek();

    private void DeadlineClear_Click(object sender, RoutedEventArgs e) => _vm.DeadlinePicker.ClearDate();

    private void DeadlinePrevMonth_Click(object sender, RoutedEventArgs e) => _vm.DeadlinePicker.StepMonth(-1);

    private void DeadlineNextMonth_Click(object sender, RoutedEventArgs e) => _vm.DeadlinePicker.StepMonth(1);

    private void DeadlineDay_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: CalendarDayVM day })
            _vm.DeadlinePicker.SelectDate(day.Date);
    }

    private void DeadlineApplyManual_Click(object sender, RoutedEventArgs e)
        => _vm.DeadlinePicker.TryApplyManualText();

    private void DeadlineManualBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        _vm.DeadlinePicker.TryApplyManualText();
        e.Handled = true;
    }

    // ==================== 目标树多选弹窗 ====================

    private void GoalPickerConfirm_Click(object sender, RoutedEventArgs e) => _vm.ConfirmGoalPicker();

    private void GoalPickerCancel_Click(object sender, RoutedEventArgs e) => _vm.CloseGoalPicker();

    private void GoalPickerScrim_Click(object sender, MouseButtonEventArgs e) => _vm.CloseGoalPicker();

    private void GoalPickerClear_Click(object sender, RoutedEventArgs e) => _vm.GoalPicker.ClearPicks();

    private void GoalPickerResetView_Click(object sender, RoutedEventArgs e) => _vm.GoalPicker.ResetViewport();

    private void GoalPickerViewport_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        e.Handled = true;
        var factor = Math.Pow(1.14, e.Delta / 120.0);
        _vm.GoalPicker.ZoomAt(factor, e.GetPosition(GoalPickerViewport));
    }

    // ==================== 标签 / 四象限 ====================

    private void Urgent_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is TaskVM task) _vm.ToggleUrgent(task);
    }

    private void Important_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is TaskVM task) _vm.ToggleImportant(task);
    }

    private void TagPageTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton { Tag: string raw } btn)
        {
            if (Enum.TryParse<TagPageTab>(raw, out var tab))
                _vm.SelectedTagPageTab = tab;
            SyncToggleBinding(btn);
        }
    }

    private void EntityNameBox_LostFocus(object sender, RoutedEventArgs e)
        => _vm.OnEntityRenamed();

    private void EntityNameBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Enter or Key.Escape)
        {
            Keyboard.ClearFocus();
            e.Handled = true;
        }
    }

    private void DeleteTagRow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: TagVM tag }) _vm.DeleteTag(tag);
    }

    private void DeletePersonRow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: PersonVM person }) _vm.DeletePerson(person);
    }

    private void DeleteLocationRow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: LocationVM location }) _vm.DeleteLocation(location);
    }

    // ==================== 标签 / 人物 / 地点 / 目标详情按日时间流弹窗 ====================

    private void OpenTagDetailModal(TagVM tag) => OpenDetailModal(() => _vm.OpenTagDetail(tag));

    private void OpenPersonDetailModal(PersonVM person) => OpenDetailModal(() => _vm.OpenPersonDetail(person));

    private void OpenLocationDetailModal(LocationVM location) => OpenDetailModal(() => _vm.OpenLocationDetail(location));

    private void OpenGoalDetailModal(GoalVM goal) => OpenDetailModal(() => _vm.OpenGoalDetail(goal));

    private void OpenDetailModal(Action openAction)
    {
        openAction();
        Dispatcher.BeginInvoke(new Action(() => ScrollTagDetailToToday(animate: false)), DispatcherPriority.Loaded);
    }

    private void TagDetailClose_Click(object sender, RoutedEventArgs e)
    {
        StopTagDetailScrollAnimation();
        _vm.CloseTagDetail();
    }

    private void TagDetailScrim_Click(object sender, MouseButtonEventArgs e)
    {
        StopTagDetailScrollAnimation();
        _vm.CloseTagDetail();
    }

    private void TagDetailToday_Click(object sender, RoutedEventArgs e) => ScrollTagDetailToToday(animate: true);

    private void TagDetailScroll_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (!_vm.IsTagDetailOpen || _vm.IsEditorOpen || TagDetailScroll is not { } scroll) return;

        StopTagDetailScrollAnimation();
        e.Handled = true;
        scroll.ScrollToHorizontalOffset(
            Math.Clamp(scroll.HorizontalOffset - e.Delta * 0.45, 0, scroll.ScrollableWidth));
    }

    private void ScrollTagDetailToToday(bool animate)
    {
        StopTagDetailScrollAnimation();
        if (_vm.TagDetailDays.Count == 0 || TagDetailScroll is not { } scroll) return;

        scroll.UpdateLayout();

        const double columnWidth = 188;
        var targetH = Math.Clamp((_vm.TagDetailTodayIndex - 1) * columnWidth, 0, scroll.ScrollableWidth);

        if (!animate)
        {
            scroll.ScrollToHorizontalOffset(targetH);
            return;
        }

        _tagDetailScrollFromH = scroll.HorizontalOffset;
        _tagDetailScrollToH = targetH;
        if (Math.Abs(_tagDetailScrollToH - _tagDetailScrollFromH) < 1) return;

        _tagDetailScrollAnimStart = DateTime.UtcNow;
        _tagDetailScrollAnimHandler = OnTagDetailScrollAnimationFrame;
        CompositionTarget.Rendering += _tagDetailScrollAnimHandler;
    }

    private void OnTagDetailScrollAnimationFrame(object? sender, EventArgs e)
    {
        if (TagDetailScroll is not { } scroll)
        {
            StopTagDetailScrollAnimation();
            return;
        }

        var elapsed = (DateTime.UtcNow - _tagDetailScrollAnimStart).TotalMilliseconds;
        var t = Math.Clamp(elapsed / Motion.M.TotalMilliseconds, 0, 1);
        var eased = Motion.EaseOut.Ease(t);

        scroll.ScrollToHorizontalOffset(
            _tagDetailScrollFromH + (_tagDetailScrollToH - _tagDetailScrollFromH) * eased);

        if (t >= 1) StopTagDetailScrollAnimation();
    }

    private void StopTagDetailScrollAnimation()
    {
        if (_tagDetailScrollAnimHandler is null) return;
        CompositionTarget.Rendering -= _tagDetailScrollAnimHandler;
        _tagDetailScrollAnimHandler = null;
    }

    // ==================== 视图（时间轴） ====================

    private void Zoom_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: int index } || BodyScroll is not { } body) return;

        StopScrollAnimation();

        var targetPx = _vm.PresetPxPerHour(index);
        var centerY = body.ViewportHeight * 0.5;
        StartSmoothZoom(targetPx, centerY);
    }

    private void PageTimeline_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_vm.IsEditorOpen || _vm.IsImportConfirmOpen || _vm.IsSettingsOpen || _vm.IsPresetManagerOpen) return;
        if (BodyScroll is not { } body) return;

        StopScrollAnimation();

        var mods = Keyboard.Modifiers;
        // Windows 精密触控板双指捏合发送 Ctrl+MouseWheel，同时保留 Alt+MouseWheel 缩放
        if ((mods & (ModifierKeys.Control | ModifierKeys.Alt)) != 0)
        {
            e.Handled = true;
            var factor = Math.Pow(1.18, e.Delta / 120.0);
            var basePx = _zoomAnimHandler is not null ? _zoomTargetPx : _vm.PxPerHourValue;
            var anchorY = Math.Clamp(e.GetPosition(body).Y, 0, Math.Max(1, body.ViewportHeight));
            StartSmoothZoom(basePx * factor, anchorY);
            return;
        }

        // 工具条里的快捷卡片区如果横向溢出，优先横向滚它自己
        if (PresetsScroll is { } presets && IsInsideElement(e.GetPosition(this), presets) && presets.ScrollableWidth > 0)
        {
            e.Handled = true;
            presets.ScrollToHorizontalOffset(
                Math.Clamp(presets.HorizontalOffset - e.Delta * 0.35, 0, presets.ScrollableWidth));
            return;
        }

        if ((mods & ModifierKeys.Shift) != 0)
        {
            e.Handled = true;
            body.ScrollToHorizontalOffset(
                Math.Clamp(body.HorizontalOffset - e.Delta * 0.45, 0, body.ScrollableWidth));
            return;
        }

        // 接管普通滚轮 / 触控板双指滑动：WPF ScrollViewer 默认每条消息固定跳 48px，会导致触控板飞出去；
        // 这里按实际 e.Delta 等比例滚动并降敏，轻滑就跟手。
        e.Handled = true;
        body.ScrollToVerticalOffset(
            Math.Clamp(body.VerticalOffset - e.Delta * 0.35, 0, body.ScrollableHeight));
    }

    private void StartSmoothZoom(double targetPx, double anchorViewportY)
    {
        if (BodyScroll is not { } body) return;
        _zoomTargetPx = Math.Clamp(targetPx, MainViewModel.MinPxPerHour, MainViewModel.MaxPxPerHour);
        _zoomAnchorViewportY = Math.Clamp(anchorViewportY, 0, Math.Max(1, body.ViewportHeight));

        var currentPx = _vm.PxPerHourValue;
        _zoomAnchorHours = currentPx > 0 ? (body.VerticalOffset + _zoomAnchorViewportY) / currentPx : 0;

        if (_zoomAnimHandler is null)
        {
            _zoomAnimLastTick = DateTime.UtcNow;
            _zoomAnimHandler = OnZoomAnimationFrame;
            CompositionTarget.Rendering += _zoomAnimHandler;
        }
    }

    private void OnZoomAnimationFrame(object? sender, EventArgs e)
    {
        if (BodyScroll is not { } body)
        {
            StopZoomAnimation();
            return;
        }

        var now = DateTime.UtcNow;
        var dt = Math.Clamp((now - _zoomAnimLastTick).TotalSeconds, 0.001, 0.05);
        _zoomAnimLastTick = now;

        var current = _vm.PxPerHourValue;
        var diff = _zoomTargetPx - current;
        var done = Math.Abs(diff) < 0.35;
        var nextPx = done ? _zoomTargetPx : current + diff * (1.0 - Math.Exp(-dt * 28.0));

        _vm.SetPxPerHour(nextPx);
        body.UpdateLayout();

        var offset = Math.Clamp(_zoomAnchorHours * nextPx - _zoomAnchorViewportY, 0, body.ScrollableHeight);
        body.ScrollToVerticalOffset(offset);

        if (done) StopZoomAnimation();
    }

    private void StopZoomAnimation()
    {
        if (_zoomAnimHandler is null) return;
        CompositionTarget.Rendering -= _zoomAnimHandler;
        _zoomAnimHandler = null;
    }

    private void Today_Click(object sender, RoutedEventArgs e) => SmoothScrollToToday();

    private (double Horizontal, double Vertical) GetTodayNowScrollOffset()
    {
        _vm.RefreshCurrentTime();
        var h = Math.Max(0, (_vm.TodayIndex - 1) * MainViewModel.DayWidth);
        var maxV = Math.Max(0, _vm.GridHeight - (BodyScroll?.ViewportHeight ?? 0));
        var v = Math.Clamp(_vm.NowLineTop - 44, 0, maxV);
        return (h, v);
    }

    private void ScrollToToday()
    {
        StopZoomAnimation();
        StopScrollAnimation();
        if (BodyScroll is not { } body) return;
        var (h, v) = GetTodayNowScrollOffset();
        body.ScrollToHorizontalOffset(h);
        body.ScrollToVerticalOffset(v);
    }

    private void SmoothScrollToToday()
    {
        StopZoomAnimation();
        StopScrollAnimation();
        if (BodyScroll is not { } body) return;

        var (h, v) = GetTodayNowScrollOffset();
        _scrollFromH = body.HorizontalOffset;
        _scrollToH = h;
        _scrollFromV = body.VerticalOffset;
        _scrollToV = v;

        if (Math.Abs(_scrollToH - _scrollFromH) < 1 && Math.Abs(_scrollToV - _scrollFromV) < 1) return;

        _scrollAnimStart = DateTime.UtcNow;
        _scrollAnimHandler = OnScrollAnimationFrame;
        CompositionTarget.Rendering += _scrollAnimHandler;
    }

    private void OnScrollAnimationFrame(object? sender, EventArgs e)
    {
        if (BodyScroll is not { } body)
        {
            StopScrollAnimation();
            return;
        }

        var elapsed = (DateTime.UtcNow - _scrollAnimStart).TotalMilliseconds;
        var t = Math.Clamp(elapsed / Motion.M.TotalMilliseconds, 0, 1);
        var eased = Motion.EaseOut.Ease(t);

        body.ScrollToHorizontalOffset(_scrollFromH + (_scrollToH - _scrollFromH) * eased);
        body.ScrollToVerticalOffset(_scrollFromV + (_scrollToV - _scrollFromV) * eased);

        if (t >= 1) StopScrollAnimation();
    }

    private void StopScrollAnimation()
    {
        if (_scrollAnimHandler is null) return;
        CompositionTarget.Rendering -= _scrollAnimHandler;
        _scrollAnimHandler = null;
    }

    private void BodyScroll_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (BodyScroll is not { } body) return;
        HeaderScroll?.ScrollToHorizontalOffset(body.HorizontalOffset);
        GutterScroll?.ScrollToVerticalOffset(body.VerticalOffset);
    }

    // ==================== 时间是手打字 ====================

    private void TimeBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        CommitTime(sender as TextBox);
        e.Handled = true;
    }

    private void TimeBox_LostFocus(object sender, RoutedEventArgs e) => CommitTime(sender as TextBox);

    private void CommitTime(TextBox? box)
    {
        if (box?.DataContext is not TaskVM task) return;

        var text = (box.Text ?? "").Trim();
        if (text.Length == 0)
        {
            _vm.ClearTime(task);
            return;
        }

        if (TryParseRange(text, out var start, out var end))
        {
            _vm.SetTime(task, start, end);
            box.Text = task.TimeLabel;
        }
        else
        {
            box.Text = task.TimeLabel;
        }
    }

    private static bool TryParseRange(string text, out TimeSpan start, out TimeSpan end)
    {
        start = default;
        end = default;

        var parts = text.Split(new[] { '-', '–', '—', '~', ' ', '到' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || !TryParseClock(parts[0], out start)) return false;

        if (parts.Length == 1)
        {
            end = start.Add(TimeSpan.FromMinutes(30));
            return true;
        }

        if (!TryParseClock(parts[1], out end)) return false;
        if (end <= start) end = start.Add(TimeSpan.FromMinutes(30));
        return true;
    }

    private static bool TryParseClock(string text, out TimeSpan value)
    {
        value = default;
        text = text.Trim();

        if (text.Contains(':'))
            return TimeSpan.TryParse(text, out value) && value >= TimeSpan.Zero && value < TimeSpan.FromDays(1);

        if (int.TryParse(text, out var hour) && hour is >= 0 and < 24)
        {
            value = TimeSpan.FromHours(hour);
            return true;
        }

        return false;
    }

    // ==================== 窗口级键盘 ====================

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        // 拦截单独的 Alt 键，防止 Alt+滚轮缩放松开后激活 Win32 系统菜单模式而吞掉后续滚轮事件
        if (e.Key == Key.System && e.SystemKey is Key.LeftAlt or Key.RightAlt)
        {
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape)
        {
            if (_vm.DeadlinePicker.IsOpen)
            {
                _vm.DeadlinePicker.Close();
                e.Handled = true;
                return;
            }

            if (_vm.IsGoalPickerOpen)
            {
                _vm.CloseGoalPicker();
                e.Handled = true;
                return;
            }

            if (_vm.IsGoalTreeEditorOpen)
            {
                if (_draggingEditorPort)
                {
                    _draggingEditorPort = false;
                    _vm.GoalTreeEditor.CancelPortDrag();
                    if (IsMouseCaptured) ReleaseMouseCapture();
                    Mouse.Capture(null);
                }
                else if (_vm.GoalTreeEditor.IsConnectMenuOpen)
                {
                    _vm.GoalTreeEditor.CancelConnectMenu();
                }
                else if (_vm.GoalTreeEditor.IsLinking)
                {
                    _vm.GoalTreeEditor.ClearLinkMode();
                }
                else
                {
                    _vm.CloseGoalTreeEditor();
                }
                e.Handled = true;
                return;
            }

            if (_vm.IsImportConfirmOpen)
            {
                _vm.CancelImportConfirm();
                e.Handled = true;
                return;
            }

            if (_vm.IsSettingsOpen)
            {
                _vm.CloseSettings();
                e.Handled = true;
                return;
            }

            if (_vm.IsPresetManagerOpen)
            {
                _vm.ClosePresetManager();
                e.Handled = true;
                return;
            }

            if (_vm.IsEditorOpen)
            {
                _vm.CloseEditor();
                e.Handled = true;
                return;
            }

            if (_vm.IsTagDetailOpen)
            {
                StopTagDetailScrollAnimation();
                _vm.CloseTagDetail();
                e.Handled = true;
                return;
            }

            if (_panning || _goalPanTarget != GoalCanvasTarget.None || _resizingBlock || _dragging || _draggingBlock || _draggingPreset || _creating || _draggingEditorNode || _draggingEditorPort || _draggingCardGoalNode)
            {
                CancelActiveInteractions();
                e.Handled = true;
                return;
            }

            _vm.CollapseAll();
            return;
        }

        var modalBlocksGlobal = _vm.DeadlinePicker.IsOpen || _vm.IsEditorOpen || _vm.IsImportConfirmOpen || _vm.IsSettingsOpen || _vm.IsPresetManagerOpen || _vm.IsTagDetailOpen;
        var canSpacePan = (!_vm.DeadlinePicker.IsOpen && (_vm.IsGoalPickerOpen || _vm.IsGoalTreeEditorOpen || (!modalBlocksGlobal && (_vm.IsTimeline || _vm.IsGoals))))
                          && Keyboard.FocusedElement is not TextBoxBase;

        if (e.Key == Key.Space && canSpacePan)
        {
            if (!_spacePanKeyDown)
            {
                _spacePanKeyDown = true;
                UpdatePanCursor();
            }
            e.Handled = true;
            return;
        }

        // 弹窗开着就别再响应全局快捷键
        if (modalBlocksGlobal || _vm.IsGoalPickerOpen || _vm.IsGoalTreeEditorOpen) return;

        if (e.Key == Key.N && Keyboard.Modifiers == ModifierKeys.Control)
        {
            // 日程只能在视图页建，所以 Ctrl+N 把人送到那里
            _vm.NavigateToTimeline();
            Dispatcher.BeginInvoke(new Action(ScrollToToday), DispatcherPriority.Loaded);
            e.Handled = true;
        }
    }

    private void OnPreviewKeyUp(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.System && e.SystemKey is Key.LeftAlt or Key.RightAlt)
        {
            e.Handled = true;
            return;
        }

        if (e.Key != Key.Space) return;

        _spacePanKeyDown = false;
        if (!_panning && _goalPanTarget == GoalCanvasTarget.None) UpdatePanCursor();
    }

    // ==================== 视图 / 目标导图画布自由拖动（右键 / 空格+左键） ====================

    private void OnPreviewRightDown(object sender, MouseButtonEventArgs e)
    {
        if (_vm.DeadlinePicker.IsOpen) return;

        var p = e.GetPosition(this);
        var src = e.OriginalSource as DependencyObject;

        if (_vm.IsGoalPickerOpen && IsInsideElement(p, GoalPickerViewport))
        {
            BeginGoalCanvasPan(GoalCanvasTarget.Picker, null, p, rightButton: true);
            e.Handled = true;
            return;
        }

        if (_vm.IsGoalTreeEditorOpen && IsInsideElement(p, TreeEditorViewport))
        {
            _vm.GoalTreeEditor.CancelConnectMenu();
            BeginGoalCanvasPan(GoalCanvasTarget.TreeEditor, null, p, rightButton: true);
            e.Handled = true;
            return;
        }

        if (_vm.IsEditorOpen || _vm.IsImportConfirmOpen || _vm.IsSettingsOpen || _vm.IsPresetManagerOpen || _vm.IsTagDetailOpen || _vm.IsGoalPickerOpen || _vm.IsGoalTreeEditorOpen)
            return;

        if (_vm.IsGoals && FindAncestorTag(src, "goalcanvas") is FrameworkElement { DataContext: GoalVM goalCard } && !IsInteractive(src))
        {
            BeginGoalCanvasPan(GoalCanvasTarget.Card, goalCard, p, rightButton: true);
            e.Handled = true;
            return;
        }

        if (!_vm.IsTimeline) return;
        if (IsInteractive(src)) return;
        if (!IsInsideBody(p)) return;

        BeginPan(p, rightButton: true);
        e.Handled = true;
    }

    private void OnPreviewRightUp(object sender, MouseButtonEventArgs e)
    {
        if (_goalPanTarget != GoalCanvasTarget.None && _goalPanRightButton)
        {
            EndGoalCanvasPan();
            e.Handled = true;
            return;
        }

        if (!_panning || !_panWithRightButton) return;

        EndPan();
        e.Handled = true;
    }

    private void BeginGoalCanvasPan(GoalCanvasTarget target, GoalVM? card, Point p, bool rightButton)
    {
        _goalPanTarget = target;
        _goalPanCard = card;
        _goalPanRightButton = rightButton;
        _goalPanStartPoint = p;

        switch (target)
        {
            case GoalCanvasTarget.Card when card is not null:
                card.ClearHoverNode();
                _goalPanStartX = card.ViewPanX;
                _goalPanStartY = card.ViewPanY;
                break;
            case GoalCanvasTarget.TreeEditor:
                _goalPanStartX = _vm.GoalTreeEditor.PanX;
                _goalPanStartY = _vm.GoalTreeEditor.PanY;
                break;
            case GoalCanvasTarget.Picker:
                _goalPanStartX = _vm.GoalPicker.PanX;
                _goalPanStartY = _vm.GoalPicker.PanY;
                break;
        }

        Mouse.OverrideCursor = Cursors.SizeAll;
        Mouse.Capture(this, CaptureMode.SubTree);
    }

    private void UpdateGoalCanvasPan(Point p)
    {
        var dx = p.X - _goalPanStartPoint.X;
        var dy = p.Y - _goalPanStartPoint.Y;

        switch (_goalPanTarget)
        {
            case GoalCanvasTarget.Card when _goalPanCard is not null:
                _goalPanCard.ViewPanX = _goalPanStartX + dx;
                _goalPanCard.ViewPanY = _goalPanStartY + dy;
                break;
            case GoalCanvasTarget.TreeEditor:
                _vm.GoalTreeEditor.PanX = _goalPanStartX + dx;
                _vm.GoalTreeEditor.PanY = _goalPanStartY + dy;
                break;
            case GoalCanvasTarget.Picker:
                _vm.GoalPicker.PanX = _goalPanStartX + dx;
                _vm.GoalPicker.PanY = _goalPanStartY + dy;
                break;
        }
    }

    private void EndGoalCanvasPan()
    {
        if (_goalPanTarget == GoalCanvasTarget.None) return;

        _goalPanTarget = GoalCanvasTarget.None;
        _goalPanCard = null;
        UpdatePanCursor();
        if (IsMouseCaptured) ReleaseMouseCapture();
        Mouse.Capture(null);
    }

    private void BeginPan(Point p, bool rightButton)
    {
        if (BodyScroll is not { } body) return;
        StopZoomAnimation();
        StopScrollAnimation();
        _panning = true;
        _panWithRightButton = rightButton;
        _panStartPoint = p;
        _panStartH = body.HorizontalOffset;
        _panStartV = body.VerticalOffset;
        UpdatePanCursor();
        Mouse.Capture(this, CaptureMode.SubTree);
    }

    private void UpdatePan(Point p)
    {
        if (BodyScroll is not { } body) return;
        var dx = p.X - _panStartPoint.X;
        var dy = p.Y - _panStartPoint.Y;
        body.ScrollToHorizontalOffset(Math.Clamp(_panStartH - dx, 0, body.ScrollableWidth));
        body.ScrollToVerticalOffset(Math.Clamp(_panStartV - dy, 0, body.ScrollableHeight));
    }

    private void EndPan()
    {
        if (!_panning) return;

        _panning = false;
        UpdatePanCursor();
        if (IsMouseCaptured) ReleaseMouseCapture();
        Mouse.Capture(null);
    }

    private void UpdatePanCursor()
    {
        var body = BodyScroll;
        if (_panning || _goalPanTarget != GoalCanvasTarget.None)
        {
            if (_panning && body is not null)
            {
                body.Cursor = Cursors.SizeAll;
                body.ForceCursor = true;
            }
            Mouse.OverrideCursor = Cursors.SizeAll;
        }
        else if (_spacePanKeyDown && (_vm.IsGoalPickerOpen || _vm.IsGoalTreeEditorOpen || _vm.IsGoals || (_vm.IsTimeline && !_vm.IsEditorOpen)))
        {
            Mouse.OverrideCursor = Cursors.SizeAll;
            if (_vm.IsTimeline && body is not null)
            {
                body.Cursor = Cursors.SizeAll;
                body.ForceCursor = true;
            }
        }
        else
        {
            Mouse.OverrideCursor = null;
            if (body is not null)
            {
                body.ForceCursor = false;
                body.ClearValue(CursorProperty);
            }
        }
    }

    // ==================== 拖拽 ====================

    private void OnPreviewDown(object sender, MouseButtonEventArgs e)
    {
        _pressedGoal = null;
        _pressedGoalPageCard = null;
        _pressedGoalPageItem = null;
        _pressedBlock = null;
        _pressedPreset = null;
        _pressedPresetElement = null;
        _pressedTagCard = null;
        _pressedTagRow = null;
        _pressedPersonCard = null;
        _pressedPersonRow = null;
        _pressedLocationCard = null;
        _pressedLocationRow = null;
        _resizeCandidateBlock = null;
        _pendingOpen = null;
        _clickedPageBlank = false;
        _pressedEditorNode = null;
        _draggingEditorNode = false;
        _draggingEditorPort = false;
        _pressedCardGoalNode = null;
        _draggingCardGoalNode = false;
        _pressedPickerNode = null;

        if (_vm.DeadlinePicker.IsOpen) return;

        var src = e.OriginalSource as DependencyObject;
        var p = e.GetPosition(this);

        if (_vm.IsGoalPickerOpen)
        {
            if (IsInteractive(src)) return;

            if (_spacePanKeyDown && IsInsideElement(p, GoalPickerViewport))
            {
                BeginGoalCanvasPan(GoalCanvasTarget.Picker, null, p, rightButton: false);
                e.Handled = true;
                return;
            }

            if (FindAncestorTag(src, "goalpickernode") is FrameworkElement { DataContext: GoalNodeVM pickerNode })
            {
                _pressedPickerNode = pickerNode;
                _pressPoint = p;
            }
            return;
        }

        if (_vm.IsGoalTreeEditorOpen)
        {
            if (_vm.GoalTreeEditor.IsConnectMenuOpen && !IsInsideElement(p, TreeEditorConnectMenu))
                _vm.GoalTreeEditor.CancelConnectMenu();

            if (IsInteractive(src)) return;

            // 从节点四边悬停圆点按住拖出连线
            if (FindAncestorTag(src, "nodeport") is FrameworkElement { DataContext: GoalNodeVM portNode } portEl)
            {
                _vm.GoalTreeEditor.FinishAnyInlineNodeEdit();
                CommitAnyEdgeLabelEdit();
                Keyboard.ClearFocus();

                _draggingEditorPort = true;
                _pressPoint = p;
                var portSide = ParsePortSide(portEl.Uid);
                _vm.GoalTreeEditor.BeginPortDrag(portNode, portSide, ToTreeEditorCanvasPoint(p));
                Mouse.Capture(this, CaptureMode.SubTree);
                e.Handled = true;
                return;
            }

            // 点击画布区域时提交就地编辑并让出文本框焦点，确保随后按空格可平移画布
            if (IsInsideElement(p, TreeEditorViewport))
            {
                _vm.GoalTreeEditor.FinishAnyInlineNodeEdit();
                CommitAnyEdgeLabelEdit();
                Keyboard.ClearFocus();
            }

            if (_spacePanKeyDown && IsInsideElement(p, TreeEditorViewport))
            {
                BeginGoalCanvasPan(GoalCanvasTarget.TreeEditor, null, p, rightButton: false);
                e.Handled = true;
                return;
            }

            if (FindAncestorTag(src, "treeeditornode") is FrameworkElement { DataContext: GoalNodeVM editorNode })
            {
                if (e.ClickCount >= 2 && !_vm.GoalTreeEditor.IsLinking)
                {
                    _vm.GoalTreeEditor.StartInlineNodeEdit(editorNode);
                    e.Handled = true;
                    return;
                }

                _pressedEditorNode = editorNode;
                _pressPoint = p;
                _editorNodeStartX = editorNode.X;
                _editorNodeStartY = editorNode.Y;
            }
            return;
        }

        // 弹窗开着时，底下的东西一概不接鼠标
        if (_vm.IsEditorOpen || _vm.IsImportConfirmOpen || _vm.IsSettingsOpen || _vm.IsPresetManagerOpen) return;

        if (IsInteractive(src))
        {
            _pressedTask = null;
            _pressedCard = null;
            return;
        }

        if (_vm.IsTagDetailOpen)
        {
            _pressedTask = null;
            _pressedCard = null;
            var tagTaskCard = FindAncestorTag(src, "tagtaskcard") as Border;
            if (tagTaskCard?.DataContext is TaskVM tagTask)
            {
                _pressedCard = tagTaskCard;
                Motion.AnimateScale(tagTaskCard, 0.985, Motion.Xs);
                if (e.ClickCount >= 2) _pendingOpen = tagTask;
            }
            return;
        }

        _pressPoint = p;

        // 空格 + 左键拖动目标卡片内画布
        if (_spacePanKeyDown && _vm.IsGoals && FindAncestorTag(src, "goalcanvas") is FrameworkElement { DataContext: GoalVM goalCard })
        {
            BeginGoalCanvasPan(GoalCanvasTarget.Card, goalCard, _pressPoint, rightButton: false);
            e.Handled = true;
            return;
        }

        // 目标页大卡片内的目标/子目标节点：按住可自由拖拽位置
        if (_vm.IsGoals && FindAncestorTag(src, "goalcardnode") is FrameworkElement { DataContext: GoalNodeVM cardGoalNode })
        {
            _pressedCardGoalNode = cardGoalNode;
            _cardNodeStartX = cardGoalNode.X;
            _cardNodeStartY = cardGoalNode.Y;
            return;
        }

        // 空格 + 左键拖动视图画布：优先于建日程 / 拖块
        if (_spacePanKeyDown && _vm.IsTimeline && IsInsideBody(_pressPoint))
        {
            _pressedTask = null;
            _pressedCard = null;
            BeginPan(_pressPoint, rightButton: false);
            e.Handled = true;
            return;
        }

        _pressedCard = FindAncestorTag(src, "card") as Border
                       ?? FindAncestorTag(src, "boardrow") as Border;
        _pressedTask = _pressedCard?.DataContext as TaskVM;
        if (_pressedCard is not null) Motion.AnimateScale(_pressedCard, 0.985, Motion.Xs);

        if (_pressedTask is null)
        {
            var presetElement = FindAncestorTag(src, "preset") as FrameworkElement;
            _pressedPreset = presetElement?.DataContext as SchedulePresetVM;
            _pressedPresetElement = presetElement;
            if (_pressedPresetElement is not null)
                Motion.AnimateScale(_pressedPresetElement, 0.97, Motion.Xs);

            if (_pressedPreset is null)
                _pressedGoal = (FindAncestorTag(src, "goal") as FrameworkElement)?.DataContext as GoalVM;

            if (_pressedPreset is null && _pressedGoal is null && FindAncestorTag(src, "goalcanvas") is null)
            {
                var goalPageCard = FindAncestorTag(src, "goalpagecard") as Border;
                if (goalPageCard?.DataContext is GoalVM goalItem && !goalItem.HasSubGoals)
                {
                    _pressedGoalPageCard = goalPageCard;
                    _pressedGoalPageItem = goalItem;
                    Motion.AnimateScale(goalPageCard, 0.985, Motion.Xs);
                }
            }

            if (_pressedPreset is null && _pressedGoal is null && _pressedGoalPageItem is null)
            {
                if (FindAncestorTag(src, "tagcard") is Border { DataContext: TagRowVM tagRow } tagCard)
                {
                    _pressedTagCard = tagCard;
                    _pressedTagRow = tagRow;
                    Motion.AnimateScale(tagCard, 0.985, Motion.Xs);
                }
                else if (FindAncestorTag(src, "personcard") is Border { DataContext: PersonRowVM personRow } personCard)
                {
                    _pressedPersonCard = personCard;
                    _pressedPersonRow = personRow;
                    Motion.AnimateScale(personCard, 0.985, Motion.Xs);
                }
                else if (FindAncestorTag(src, "locationcard") is Border { DataContext: LocationRowVM locationRow } locationCard)
                {
                    _pressedLocationCard = locationCard;
                    _pressedLocationRow = locationRow;
                    Motion.AnimateScale(locationCard, 0.985, Motion.Xs);
                }
            }

            if (_pressedPreset is null && _pressedGoal is null && _pressedGoalPageItem is null &&
                _pressedTagRow is null && _pressedPersonRow is null && _pressedLocationRow is null)
            {
                var topEdge = FindAncestorTag(src, "blockTopEdge") as FrameworkElement;
                var bottomEdge = topEdge is null ? FindAncestorTag(src, "blockBottomEdge") as FrameworkElement : null;
                var blockElement = FindAncestorTag(src, "block") as FrameworkElement;
                _pressedBlock = blockElement?.DataContext as TimelineBlockVM;
                _pressedBlockElement = blockElement;

                if (_pressedBlock is not null && (topEdge is not null || bottomEdge is not null))
                {
                    _resizeCandidateBlock = _pressedBlock;
                    _resizeTopEdge = topEdge is not null;
                }
                else if (_pressedBlockElement is not null)
                {
                    Motion.AnimateScale(_pressedBlockElement, 0.98, Motion.Xs);
                }
            }

            // 空白处按下：在时间轴网格内进入「点击或拖一段建日程」状态，否则记下页面空白点击用于收起展开卡片
            if (_pressedPreset is null && _pressedGoal is null && _pressedGoalPageItem is null &&
                _pressedTagRow is null && _pressedPersonRow is null && _pressedLocationRow is null &&
                _pressedBlock is null)
            {
                var day = DayIndexAt(_pressPoint);
                if (day >= 0)
                {
                    _creating = true;
                    _createDay = day;
                    _createAnchorSnappedToNow = IsNearRedLine(_pressPoint);
                    _createAnchorMinutes = _createAnchorSnappedToNow
                        ? Math.Clamp(_vm.CurrentMinutes, 0, 24 * 60)
                        : MinutesAt(_pressPoint, floor: true);
                }
                else if (IsInsideElement(_pressPoint, PageHost))
                {
                    _clickedPageBlank = true;
                }
            }
        }

        // 双击：按下时就把要编辑的那条记下来（包含仍在双击等待窗内的第二次点击）
        if (e.ClickCount >= 2 || (_pressedTask is not null && ReferenceEquals(_pressedTask, _pendingExpandTask)))
        {
            CancelPendingExpand();
            _pendingOpen = _pressedTask ?? _pressedBlock?.Task;
        }
    }

    private void OnPreviewMove(object sender, MouseEventArgs e)
    {
        if (_goalPanTarget != GoalCanvasTarget.None)
        {
            var pressed = _goalPanRightButton
                ? e.RightButton == MouseButtonState.Pressed
                : e.LeftButton == MouseButtonState.Pressed;
            if (!pressed)
            {
                EndGoalCanvasPan();
                return;
            }

            UpdateGoalCanvasPan(e.GetPosition(this));
            return;
        }

        if (_draggingEditorPort)
        {
            if (e.LeftButton != MouseButtonState.Pressed)
            {
                _draggingEditorPort = false;
                _vm.GoalTreeEditor.CancelPortDrag();
                if (IsMouseCaptured) ReleaseMouseCapture();
                Mouse.Capture(null);
                return;
            }

            var canvasPt = ToTreeEditorCanvasPoint(e.GetPosition(this));
            var (snapNode, snapSide) = HitTreeEditorTargetNode(canvasPt, exclude: null);
            _vm.GoalTreeEditor.UpdatePortDrag(canvasPt, snapNode, snapSide);
            return;
        }

        if (_pressedCardGoalNode is not null)
        {
            if (e.LeftButton != MouseButtonState.Pressed)
            {
                if (_draggingCardGoalNode)
                {
                    _draggingCardGoalNode = false;
                    if (IsMouseCaptured) ReleaseMouseCapture();
                    Mouse.Capture(null);
                    CommitCardGoalNodeDrag(_pressedCardGoalNode);
                }
                _pressedCardGoalNode = null;
                return;
            }

            var gp = e.GetPosition(this);
            if (!_draggingCardGoalNode)
            {
                if (Math.Abs(gp.X - _pressPoint.X) < 5 && Math.Abs(gp.Y - _pressPoint.Y) < 5)
                    return;

                _draggingCardGoalNode = true;
                _pressedCardGoalNode.Owner.ClearHoverNode();
                Mouse.Capture(this, CaptureMode.SubTree);
            }

            var cardZoom = Math.Max(0.1, _pressedCardGoalNode.Owner.ViewZoom);
            _pressedCardGoalNode.X = _cardNodeStartX + (gp.X - _pressPoint.X) / cardZoom;
            _pressedCardGoalNode.Y = _cardNodeStartY + (gp.Y - _pressPoint.Y) / cardZoom;
            _pressedCardGoalNode.Owner.RefreshEdgeGeometries();
            return;
        }

        if (_pressedEditorNode is not null)
        {
            if (e.LeftButton != MouseButtonState.Pressed)
            {
                if (_draggingEditorNode)
                {
                    _draggingEditorNode = false;
                    if (IsMouseCaptured) ReleaseMouseCapture();
                    Mouse.Capture(null);
                    _vm.GoalTreeEditor.CommitNodeDrag();
                }
                _pressedEditorNode = null;
                return;
            }

            var ep = e.GetPosition(this);
            if (!_draggingEditorNode)
            {
                if (_vm.GoalTreeEditor.IsLinking || (Math.Abs(ep.X - _pressPoint.X) < 5 && Math.Abs(ep.Y - _pressPoint.Y) < 5))
                    return;

                _draggingEditorNode = true;
                Mouse.Capture(this, CaptureMode.SubTree);
            }

            var zoom = Math.Max(0.1, _vm.GoalTreeEditor.Zoom);
            _pressedEditorNode.X = _editorNodeStartX + (ep.X - _pressPoint.X) / zoom;
            _pressedEditorNode.Y = _editorNodeStartY + (ep.Y - _pressPoint.Y) / zoom;
            _vm.GoalTreeEditor.Goal?.RefreshEdgeGeometries();
            return;
        }

        if (_panning)
        {
            var pressed = _panWithRightButton
                ? e.RightButton == MouseButtonState.Pressed
                : e.LeftButton == MouseButtonState.Pressed;
            if (!pressed)
            {
                EndPan();
                return;
            }

            UpdatePan(e.GetPosition(this));
            return;
        }

        if (_resizingBlock)
        {
            if (e.LeftButton != MouseButtonState.Pressed)
            {
                EndBlockResize(restoreVisual: true);
                return;
            }

            UpdateBlockResize(e.GetPosition(this));
            return;
        }

        if (_resizeCandidateBlock is not null)
        {
            if (e.LeftButton != MouseButtonState.Pressed) return;

            var rp = e.GetPosition(this);
            if (Math.Abs(rp.Y - _pressPoint.Y) < 4 && Math.Abs(rp.X - _pressPoint.X) < 4) return;

            BeginBlockResize(rp);
            return;
        }

        if (_draggingPreset)
        {
            if (e.LeftButton != MouseButtonState.Pressed)
            {
                EndPresetDrag();
                return;
            }

            UpdatePresetDrag(e.GetPosition(this));
            return;
        }

        if (_draggingBlock)
        {
            if (e.LeftButton != MouseButtonState.Pressed)
            {
                EndBlockDrag();
                return;
            }

            UpdateBlockDrag(e.GetPosition(this));
            return;
        }

        if (_dragging)
        {
            if (e.LeftButton != MouseButtonState.Pressed)
            {
                CancelDrag();
                return;
            }
            UpdateDrag(e.GetPosition(this));
            return;
        }

        if (_creating)
        {
            if (e.LeftButton != MouseButtonState.Pressed)
            {
                FinishCreate(e.GetPosition(this));
                return;
            }

            var cp = e.GetPosition(this);
            if (_createGhost is null && Math.Abs(cp.Y - _pressPoint.Y) < 4 && Math.Abs(cp.X - _pressPoint.X) < 4)
                return;

            UpdateCreateGhost(cp);
            return;
        }

        if (_pressedPreset is not null && _pressedPresetElement is not null)
        {
            if (e.LeftButton != MouseButtonState.Pressed) return;

            var pp = e.GetPosition(this);
            if (Math.Abs(pp.X - _pressPoint.X) < 6 && Math.Abs(pp.Y - _pressPoint.Y) < 6) return;

            BeginPresetDrag(pp);
            return;
        }

        if (_pressedBlock is not null && _pressedBlockElement is not null)
        {
            if (e.LeftButton != MouseButtonState.Pressed) return;

            var bp = e.GetPosition(this);
            if (Math.Abs(bp.X - _pressPoint.X) < 6 && Math.Abs(bp.Y - _pressPoint.Y) < 6) return;

            BeginBlockDrag(bp);
            return;
        }

        if (_pressedTask is null || e.LeftButton != MouseButtonState.Pressed) return;

        var p = e.GetPosition(this);
        if (Math.Abs(p.X - _pressPoint.X) < 6 && Math.Abs(p.Y - _pressPoint.Y) < 6) return;

        BeginDrag(p);    }

    private void OnPreviewUp(object sender, MouseButtonEventArgs e)
    {
        if (_goalPanTarget != GoalCanvasTarget.None && !_goalPanRightButton)
        {
            EndGoalCanvasPan();
            e.Handled = true;
            return;
        }

        if (_pressedPickerNode is not null)
        {
            var pNode = _pressedPickerNode;
            _pressedPickerNode = null;
            var upPos = e.GetPosition(this);
            if (Math.Abs(upPos.X - _pressPoint.X) < 6 && Math.Abs(upPos.Y - _pressPoint.Y) < 6)
                _vm.GoalPicker.TogglePick(pNode);
            e.Handled = true;
            return;
        }

        if (_draggingEditorPort)
        {
            _draggingEditorPort = false;
            if (IsMouseCaptured) ReleaseMouseCapture();
            Mouse.Capture(null);

            var winPt = e.GetPosition(this);
            var canvasPt = ToTreeEditorCanvasPoint(winPt);
            var vp = TreeEditorViewport;
            var viewportPt = vp is not null ? e.GetPosition(vp) : winPt;
            var (targetNode, targetSide) = HitTreeEditorTargetNode(canvasPt, exclude: null);
            _vm.GoalTreeEditor.CompletePortDrag(
                canvasPt,
                viewportPt,
                vp?.ActualWidth ?? 0,
                vp?.ActualHeight ?? 0,
                targetNode,
                targetSide);
            e.Handled = true;
            return;
        }

        if (_pressedCardGoalNode is not null)
        {
            var cNode = _pressedCardGoalNode;
            _pressedCardGoalNode = null;
            if (_draggingCardGoalNode)
            {
                _draggingCardGoalNode = false;
                if (IsMouseCaptured) ReleaseMouseCapture();
                Mouse.Capture(null);
                CommitCardGoalNodeDrag(cNode);
            }
            e.Handled = true;
            return;
        }

        if (_pressedEditorNode is not null)
        {
            var eNode = _pressedEditorNode;
            _pressedEditorNode = null;
            if (_draggingEditorNode)
            {
                _draggingEditorNode = false;
                if (IsMouseCaptured) ReleaseMouseCapture();
                Mouse.Capture(null);
                _vm.GoalTreeEditor.CommitNodeDrag();
            }
            else
            {
                _vm.GoalTreeEditor.HandleNodeClick(eNode);
            }
            e.Handled = true;
            return;
        }

        if (_panning && !_panWithRightButton)
        {
            EndPan();
            e.Handled = true;
            return;
        }

        if (_resizingBlock)
        {
            var task = _resizeCandidateBlock?.Task;
            var start = _resizeCurrentStartMin;
            var end = _resizeCurrentEndMin;
            var changed = start != _resizeOrigStartMin || end != _resizeOrigEndMin;

            EndBlockResize(restoreVisual: !changed);

            if (changed && task is not null)
                _vm.ResizeBlock(task, start, end);

            e.Handled = true;
            return;
        }

        if (_creating)
        {
            FinishCreate(e.GetPosition(this));
            e.Handled = true;
            return;
        }

        if (_draggingPreset)
        {
            var p = e.GetPosition(this);
            var inside = IsInsideBody(p);
            var preset = _pressedPreset;
            var day = _presetDay;
            var minutes = _presetMinutes;

            EndPresetDrag();

            if (inside && preset is not null && day >= 0 && day < _vm.TimelineDays.Count)
                _vm.CreateScheduleFromPreset(preset, _vm.TimelineDays[day].Date, minutes);

            e.Handled = true;
            return;
        }

        if (_draggingBlock)
        {
            var p = e.GetPosition(this);
            var inside = IsInsideBody(p);
            var task = _pressedBlock?.Task;
            var day = _blockDay;
            var minutes = _blockMinutes;

            EndBlockDrag();

            if (inside && task is not null && day >= 0 && day < _vm.TimelineDays.Count)
                _vm.MoveBlock(task, _vm.TimelineDays[day].Date, minutes);

            e.Handled = true;
            return;
        }

        if (_dragging)
        {
            var p = e.GetPosition(this);
            var goal = HitGoal(p);
            var lane = goal is null ? HitLane(p) : null;
            var quadrant = goal is null && lane is null ? HitQuadrant(p) : null;

            if (goal is not null && _pressedTask is not null)
            {
                var target = _pressedTask;
                EndDrag();
                _vm.AssignGoal(target, goal);
            }
            else if (quadrant is not null && _pressedTask is not null)
            {
                var target = _pressedTask;
                EndDrag();
                _vm.SetQuadrant(target, quadrant);
            }
            else if (lane is not null && _pressedTask is not null)
            {
                var index = InsertIndex(lane, p);
                var task = _pressedTask;
                EndDrag();
                _vm.MoveTask(task, lane, index);
            }
            else
            {
                EndDrag();
            }

            e.Handled = true;
            return;
        }

        var open = _pendingOpen;
        _pendingOpen = null;
        var clickedBlank = _clickedPageBlank;
        _clickedPageBlank = false;

        if (open is not null)
        {
            // 双击：时间轴上的块、概览/逾期里的卡片、展开态卡片空白处都走同一个弹窗
            OpenEditor(open);
        }
        else if (_pressedTask is not null)
        {
            // 已展开卡片内单击空白区域不收起，以便继续双击；未展开则等双击窗口后展开
            if (Equals(_pressedCard?.Tag, "card") && !_pressedTask.IsExpanded)
                SchedulePendingExpand(_pressedTask);
        }
        else if (_pressedPreset is not null)
        {
            ShowToast("按住快捷卡片拖到下方时间轴即可添加", highlight: false);
        }
        else if (_pressedGoal is not null)
        {
            _vm.ToggleGoalExpand(_pressedGoal);
        }
        else if (_pressedGoalPageItem is not null)
        {
            if (IsCardTap(e, _pressedGoalPageCard, "goalpagecard"))
                OpenGoalDetailModal(_pressedGoalPageItem);
        }
        else if (_pressedTagRow is not null)
        {
            if (IsCardTap(e, _pressedTagCard, "tagcard"))
                OpenTagDetailModal(_pressedTagRow.Tag);
        }
        else if (_pressedPersonRow is not null)
        {
            if (IsCardTap(e, _pressedPersonCard, "personcard"))
                OpenPersonDetailModal(_pressedPersonRow.Person);
        }
        else if (_pressedLocationRow is not null)
        {
            if (IsCardTap(e, _pressedLocationCard, "locationcard"))
                OpenLocationDetailModal(_pressedLocationRow.Location);
        }
        else if (clickedBlank)
        {
            _vm.CollapseAll();
        }

        ResetPressedVisuals();

        _pressedTask = null;
        _pressedCard = null;
        _pressedTagCard = null;
        _pressedTagRow = null;
        _pressedPersonCard = null;
        _pressedPersonRow = null;
        _pressedLocationCard = null;
        _pressedLocationRow = null;
        _pressedPersonCard = null;
        _pressedPersonRow = null;
        _pressedLocationCard = null;
        _pressedLocationRow = null;
        _pressedGoalPageCard = null;
        _pressedGoalPageItem = null;
        _pressedPreset = null;
        _pressedPresetElement = null;
        _pressedGoal = null;
        _pressedBlock = null;
        _resizeCandidateBlock = null;
        _pressedBlockElement = null;
    }

    private bool IsCardTap(MouseButtonEventArgs e, Border? card, string tag)
    {
        if (card is null) return false;
        var releaseSrc = e.OriginalSource as DependencyObject;
        return !IsInteractive(releaseSrc) &&
               (ReferenceEquals(FindAncestorTag(releaseSrc, tag), card) ||
                IsInsideElement(e.GetPosition(this), card));
    }

    private void SchedulePendingExpand(TaskVM task)
    {
        _expandTimer.Stop();
        _pendingExpandTask = task;
        _expandTimer.Start();
    }

    private void CommitPendingExpand()
    {
        _expandTimer.Stop();
        var task = _pendingExpandTask;
        _pendingExpandTask = null;
        if (task is not null && !_vm.IsEditorOpen) _vm.ToggleExpand(task);
    }

    private void CancelPendingExpand()
    {
        _expandTimer.Stop();
        _pendingExpandTask = null;
    }

    private void BeginDrag(Point p)
    {
        if (_pressedTask is null || _pressedCard is null) return;
        if (!LanesHost.IsVisible && QuadrantsHost is not { IsVisible: true }) return;

        CancelPendingExpand();
        _dragging = true;
        Motion.AnimateScale(_pressedCard, 1.0, Motion.Xs);
        Motion.AnimateOpacity(_pressedCard, 0.3, Motion.Xs);

        var ghost = new Border
        {
            Width = Math.Max(_pressedCard.ActualWidth, 80),
            Height = Math.Max(_pressedCard.ActualHeight, 34),
            Background = (Brush)FindResource("Surface2"),
            BorderBrush = (Brush)FindResource("Accent"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10, 8, 10, 8),
            Opacity = 0,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new ScaleTransform(0.98, 0.98),
            Effect = Motion.CreateFloatingShadow(),
            IsHitTestVisible = false,
            Child = new TextBlock
            {
                Text = _pressedTask.Title,
                Foreground = (Brush)FindResource("TextPrimary"),
                FontFamily = (FontFamily)FindResource("AppFont"),
                FontSize = 13.5,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap
            }
        };

        _ghost = ghost;
        DragLayer.Children.Add(ghost);
        Motion.AnimateOpacity(ghost, 0.96, Motion.Xs);
        Motion.AnimateScale(ghost, 1.02, TimeSpan.FromMilliseconds(120));
        Mouse.Capture(this, CaptureMode.SubTree);
        UpdateDrag(p);
    }

    private void UpdateDrag(Point p)
    {
        if (_ghost is null) return;

        Canvas.SetLeft(_ghost, p.X - 24);
        Canvas.SetTop(_ghost, p.Y - 16);

        var goal = HitGoal(p);
        var lane = goal is null ? HitLane(p) : null;
        var quadrant = goal is null && lane is null ? HitQuadrant(p) : null;

        if (!ReferenceEquals(goal, _hoverGoal))
        {
            if (_hoverGoal is not null) _hoverGoal.IsDropTarget = false;
            _hoverGoal = goal;
            if (_hoverGoal is not null) _hoverGoal.IsDropTarget = true;
        }

        if (!ReferenceEquals(quadrant, _hoverQuadrant))
        {
            if (_hoverQuadrant is not null) _hoverQuadrant.IsDropTarget = false;
            _hoverQuadrant = quadrant;
            if (_hoverQuadrant is not null) _hoverQuadrant.IsDropTarget = true;
        }

        if (goal is not null || quadrant is not null)
        {
            if (_hoverLane is not null)
            {
                _hoverLane.IsDropTarget = false;
                _hoverLane = null;
            }

            ClearGaps();
            return;
        }

        if (!ReferenceEquals(lane, _hoverLane))
        {
            if (_hoverLane is not null) _hoverLane.IsDropTarget = false;
            _hoverLane = lane;
            if (_hoverLane is not null) _hoverLane.IsDropTarget = true;
        }

        UpdateGaps(lane, p);
    }

    private void EndDrag()
    {
        ResetAllShifts();

        if (_ghost is not null)
        {
            DragLayer.Children.Remove(_ghost);
            _ghost = null;
        }

        if (_pressedCard is not null) Motion.AnimateOpacity(_pressedCard, 1, Motion.S);

        if (_hoverLane is not null)
        {
            _hoverLane.IsDropTarget = false;
            _hoverLane = null;
        }

        if (_hoverGoal is not null)
        {
            _hoverGoal.IsDropTarget = false;
            _hoverGoal = null;
        }

        if (_hoverQuadrant is not null)
        {
            _hoverQuadrant.IsDropTarget = false;
            _hoverQuadrant = null;
        }

        _dragging = false;
        _pressedTask = null;
        _pressedCard = null;
        _pressedGoal = null;
        _pressedBlock = null;
        _pendingOpen = null;

        if (IsMouseCaptured) ReleaseMouseCapture();
        Mouse.Capture(null);
    }

    private void CancelDrag() => EndDrag();

    // ==================== 在时间轴上拖出一段，就建一条日程 ====================

    private int DayIndexAt(Point p)
    {
        if (DaysHost is not { IsVisible: true } daysHost || !IsInsideBody(p)) return -1;

        for (var i = 0; i < _vm.TimelineDays.Count; i++)
        {
            if (daysHost.ItemContainerGenerator.ContainerFromIndex(i) is not FrameworkElement el) continue;
            if (!el.IsVisible) continue;

            var topLeft = el.TranslatePoint(new Point(0, 0), this);
            if (new Rect(topLeft, el.RenderSize).Contains(p)) return i;
        }

        return -1;
    }

    private bool IsNearRedLine(Point p)
    {
        if (!IsInsideBody(p) || BodyScroll is not { } body) return false;
        var origin = body.TranslatePoint(new Point(0, 0), this);
        var contentY = p.Y - origin.Y + body.VerticalOffset;
        return Math.Abs(contentY - _vm.NowLineTop) <= RedLineSnapPx;
    }

    private int SnapMinutesToGridOrRedLine(Point p, bool floor)
        => IsNearRedLine(p)
            ? Math.Clamp(_vm.CurrentMinutes, 0, 24 * 60)
            : MinutesAt(p, floor);

    /// <summary>把窗口坐标换算成「第几分钟」，按当前缩放间隔取整。</summary>
    private int MinutesAt(Point p, bool floor)
    {
        if (BodyScroll is not { } body) return 0;
        var origin = body.TranslatePoint(new Point(0, 0), this);
        var contentY = p.Y - origin.Y + body.VerticalOffset;
        var minutes = contentY / _vm.PxPerHourValue * 60.0;

        var interval = Math.Max(1, _vm.IntervalMinutes);
        // 起点向下取整，终点四舍五入：刚好拖到刻度线就是整段时间，不会莫名多半小时
        var snapped = floor ? Math.Floor(minutes / interval) : Math.Round(minutes / interval);

        return Math.Clamp((int)(snapped * interval), 0, 24 * 60);
    }

    private (int Start, int End) CreateRange(Point p)
    {
        var interval = Math.Max(1, _vm.IntervalMinutes);
        var minStep = interval == 1 ? 1 : 5;
        var a = _createAnchorMinutes;
        var b = SnapMinutesToGridOrRedLine(p, floor: false);

        int start;
        int end;
        if (b > a)
        {
            start = a;
            end = Math.Max(a + minStep, b);
        }
        else if (b < a)
        {
            start = b;
            end = Math.Max(b + minStep, a);
        }
        else
        {
            var fallback = _createAnchorSnappedToNow ? Math.Min(interval, 30) : interval;
            start = Math.Clamp(a, 0, 24 * 60 - minStep);
            end = start + fallback;
        }

        start = Math.Clamp(start, 0, 24 * 60 - minStep);
        end = Math.Clamp(end, start + minStep, 24 * 60);

        return (start, end);
    }

    private void UpdateCreateGhost(Point p)
    {
        if (BodyScroll is not { } body) return;
        var (start, end) = CreateRange(p);
        var origin = body.TranslatePoint(new Point(0, 0), this);

        var height = (end - start) / 60.0 * _vm.PxPerHourValue;

        if (_createGhost is null)
        {
            _createGhost = new Border
            {
                Width = MainViewModel.DayWidth - 10,
                Background = (Brush)FindResource("AccentTint"),
                BorderBrush = (Brush)FindResource("Accent"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 3, 6, 3),
                IsHitTestVisible = false,
                Child = new TextBlock
                {
                    FontFamily = (FontFamily)FindResource("AppFont"),
                    FontSize = 11.5,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = (Brush)FindResource("Accent")
                }
            };
            DragLayer.Children.Add(_createGhost);
        }

        if (_createGhost.Child is TextBlock label) label.Text = $"{Clock(start)}–{Clock(end)}";
        _createGhost.Height = Math.Max(16, height);

        Canvas.SetLeft(_createGhost, origin.X + _createDay * MainViewModel.DayWidth + 5 - body.HorizontalOffset);
        Canvas.SetTop(_createGhost, origin.Y + start / 60.0 * _vm.PxPerHourValue - body.VerticalOffset);
    }

    private void FinishCreate(Point p)
    {
        var hadGhost = _createGhost is not null;

        if (_createGhost is not null)
        {
            DragLayer.Children.Remove(_createGhost);
            _createGhost = null;
        }

        var day = _createDay;
        var snappedNow = _createAnchorSnappedToNow;
        _creating = false;
        _createAnchorSnappedToNow = false;

        if (day < 0 || day >= _vm.TimelineDays.Count) return;

        // 单击空白格：红线附近从当前时刻起（30分），其余空白格按点击刻度起建日程
        if (!hadGhost)
        {
            if (snappedNow)
            {
                var nowStart = Math.Clamp(_vm.CurrentMinutes, 0, 24 * 60 - 5);
                var nowEnd = Math.Min(24 * 60, nowStart + 30);
                OpenNewEditor(_vm.TimelineDays[day].Date, nowStart, nowEnd);
                return;
            }

            var clickDuration = _vm.IntervalMinutes is 15 or 30 or 60 ? _vm.IntervalMinutes : 30;
            var clickStart = Math.Clamp(_createAnchorMinutes, 0, 24 * 60 - clickDuration);
            var clickEnd = Math.Min(24 * 60, clickStart + clickDuration);
            OpenNewEditor(_vm.TimelineDays[day].Date, clickStart, clickEnd);
            return;
        }

        var (start, end) = CreateRange(p);
        OpenNewEditor(_vm.TimelineDays[day].Date, start, end);
    }

    // ==================== 日程编辑弹窗 ====================

    private void OpenEditor(TaskVM task)
    {
        // 卡片里的就地编辑器与弹窗同时开着会让人分心，先收起来
        _vm.CollapseAll();
        _vm.OpenSchedule(task);
        FocusEditorTitle();
    }

    private void OpenNewEditor(DateTime date, int startMinutes, int endMinutes)
    {
        _vm.OpenNewSchedule(date, startMinutes, endMinutes);
        FocusEditorTitle();
    }

    private void FocusEditorTitle()
        => Dispatcher.BeginInvoke(new Action(() =>
        {
            EditorTitle?.Focus();
            EditorTitle?.SelectAll();
        }), DispatcherPriority.Input);

    private void EditorSave_Click(object sender, RoutedEventArgs e) => _vm.SaveEditor();

    private void EditorCancel_Click(object sender, RoutedEventArgs e) => _vm.CloseEditor();

    private void EditorScrim_Click(object sender, MouseButtonEventArgs e) => _vm.CloseEditor();

    private void EditorDelete_Click(object sender, RoutedEventArgs e) => _vm.DeleteEdited();

    private void EditorPrevDay_Click(object sender, RoutedEventArgs e) => _vm.Editor.StepDay(-1);

    private void EditorNextDay_Click(object sender, RoutedEventArgs e) => _vm.Editor.StepDay(1);

    private void EditorRepeat_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string repeat }) _vm.Editor.Repeat = repeat;
    }

    private void EditorRemind_Click(object sender, RoutedEventArgs e)
        => HandleRemindChipClick(sender, _vm.Editor.ToggleRemindNode);

    private void EditorGoal_Click(object sender, RoutedEventArgs e)
        => HandleGoalPickClick(sender, _vm.Editor.ClickGoalPick);

    private void EditorNewTag_KeyDown(object sender, KeyEventArgs e)
        => HandleModalChipInput(sender, e, _vm.AddEditorTag, () => _vm.Editor.NewTagText = "");

    private void EditorNewPerson_KeyDown(object sender, KeyEventArgs e)
        => HandleModalChipInput(sender, e, _vm.AddEditorPerson, () => _vm.Editor.NewPersonText = "");

    private void EditorNewLocation_KeyDown(object sender, KeyEventArgs e)
        => HandleModalChipInput(sender, e, _vm.AddEditorLocation, () => _vm.Editor.NewLocationText = "");

    private static void HandleModalChipInput(
        object sender,
        KeyEventArgs e,
        Action<string> addAction,
        Action clearVmText)
    {
        if (e.Key != Key.Enter || sender is not TextBox box) return;

        addAction(box.Text);
        clearVmText();
        box.Clear();
        e.Handled = true;
    }

    /// <summary>弹窗里回车 = 保存；备注或新建胶囊框里回车不直接提交表单。</summary>
    private void Editor_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        if (Keyboard.FocusedElement is TextBox tb &&
            (tb.AcceptsReturn || tb.Name is "EditorNewTag" or "EditorNewPerson" or "EditorNewLocation"))
            return;

        _vm.SaveEditor();
        e.Handled = true;
    }

    private static string Clock(int minutes) => $"{minutes / 60:00}:{minutes % 60:00}";

    // ==================== 快捷日程预设管理弹窗 ====================

    private void ManagePresets_Click(object sender, RoutedEventArgs e)
    {
        _vm.OpenPresetManager();
        Dispatcher.BeginInvoke(new Action(() =>
        {
            PresetTitleBox?.Focus();
            PresetTitleBox?.SelectAll();
        }), DispatcherPriority.Input);
    }

    private void PresetManagerClose_Click(object sender, RoutedEventArgs e) => _vm.ClosePresetManager();

    private void PresetManagerScrim_Click(object sender, MouseButtonEventArgs e) => _vm.ClosePresetManager();

    private void PresetManager_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        if (Keyboard.FocusedElement is TextBox tb &&
            tb.Name is "PresetNewTag" or "PresetNewPerson" or "PresetNewLocation")
            return;
        _vm.SavePreset();
        e.Handled = true;
    }

    private void PresetCancelEdit_Click(object sender, RoutedEventArgs e) => _vm.CancelEditPreset();

    private void PresetDurationChip_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: int minutes }) _vm.PresetManager.SetDuration(minutes);
    }

    private void PresetGoal_Click(object sender, RoutedEventArgs e)
        => HandleGoalPickClick(sender, _vm.PresetManager.ClickGoalPick);

    private static void HandleGoalPickClick(object sender, Action<GoalPickVM> onPick)
    {
        if (sender is not ToggleButton btn || btn.DataContext is not GoalPickVM pick) return;
        onPick(pick);
        // ToggleButton.OnToggle 会在触发 Click 前本地改写 IsChecked，需同步回数据源状态
        btn.GetBindingExpression(ToggleButton.IsCheckedProperty)?.UpdateTarget();
    }

    private void PresetRemind_Click(object sender, RoutedEventArgs e)
        => HandleRemindChipClick(sender, _vm.PresetManager.ToggleRemindNode);

    private void PresetNewTag_KeyDown(object sender, KeyEventArgs e)
        => HandleModalChipInput(sender, e, _vm.AddPresetManagerTag, () => _vm.PresetManager.NewTagText = "");

    private void PresetNewPerson_KeyDown(object sender, KeyEventArgs e)
        => HandleModalChipInput(sender, e, _vm.AddPresetManagerPerson, () => _vm.PresetManager.NewPersonText = "");

    private void PresetNewLocation_KeyDown(object sender, KeyEventArgs e)
        => HandleModalChipInput(sender, e, _vm.AddPresetManagerLocation, () => _vm.PresetManager.NewLocationText = "");

    private void PresetSave_Click(object sender, RoutedEventArgs e) => _vm.SavePreset();

    private void PresetEdit_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: SchedulePresetVM preset })
        {
            _vm.StartEditPreset(preset);
            Dispatcher.BeginInvoke(new Action(() =>
            {
                PresetTitleBox?.Focus();
                PresetTitleBox?.SelectAll();
            }), DispatcherPriority.Input);
        }
    }

    private void PresetDelete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: SchedulePresetVM preset }) _vm.DeletePreset(preset);
    }

    // ==================== 快捷卡片拖到时间轴 ====================

    private void BeginPresetDrag(Point p)
    {
        if (_pressedPreset is null || _pressedPresetElement is null) return;

        _draggingPreset = true;
        _presetGhostPlaced = false;
        _presetGhostInBody = false;
        Motion.AnimateScale(_pressedPresetElement, 1.0, Motion.Xs);
        Motion.AnimateOpacity(_pressedPresetElement, 0.35, Motion.Xs);

        var ghost = new Border
        {
            Background = (Brush)FindResource("Surface2"),
            BorderBrush = (Brush)FindResource("Accent"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(9, 4, 9, 4),
            Opacity = 0,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new ScaleTransform(0.98, 0.98),
            Effect = Motion.CreateFloatingShadow(),
            IsHitTestVisible = false,
            Child = new TextBlock
            {
                Text = $"{_pressedPreset.DisplayTitle} · {_pressedPreset.DurationLabel}",
                Foreground = (Brush)FindResource("TextPrimary"),
                FontFamily = (FontFamily)FindResource("AppFont"),
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis
            }
        };

        _presetGhost = ghost;
        DragLayer.Children.Add(ghost);
        Motion.AnimateOpacity(ghost, 0.96, Motion.Xs);
        Motion.AnimateScale(ghost, 1.02, TimeSpan.FromMilliseconds(120));
        Mouse.Capture(this, CaptureMode.SubTree);
        UpdatePresetDrag(p);
    }

    private void UpdatePresetDrag(Point p)
    {
        if (_presetGhost is null || _pressedPreset is null) return;

        var label = _presetGhost.Child as TextBlock;

        if (!IsInsideBody(p))
        {
            if (_presetGhostInBody)
            {
                _presetGhostInBody = false;
                _presetGhostPlaced = false;
                _presetGhost.BeginAnimation(Canvas.LeftProperty, null);
                _presetGhost.BeginAnimation(Canvas.TopProperty, null);
                _presetGhost.Width = double.NaN;
                _presetGhost.Height = double.NaN;
                _presetGhost.CornerRadius = new CornerRadius(6);
                _presetGhost.Padding = new Thickness(9, 4, 9, 4);
                _presetGhost.Background = (Brush)FindResource("Surface2");
                if (label is not null)
                    label.Text = $"{_pressedPreset.DisplayTitle} · {_pressedPreset.DurationLabel}";
            }

            Canvas.SetLeft(_presetGhost, p.X - 24);
            Canvas.SetTop(_presetGhost, p.Y - 14);
            return;
        }

        var duration = Math.Clamp(_pressedPreset.DurationMinutes, 5, 24 * 60);
        var (day, minutes, left, top) = SnapSlotAt(p, duration);
        var height = Math.Max(20, duration / 60.0 * _vm.PxPerHourValue);

        if (!_presetGhostInBody)
        {
            _presetGhostInBody = true;
            _presetGhost.Width = MainViewModel.DayWidth - 10;
            _presetGhost.Height = height;
            _presetGhost.CornerRadius = new CornerRadius(4);
            _presetGhost.Padding = height < 28 ? new Thickness(6, 1, 6, 1) : new Thickness(6, 3, 6, 3);
            _presetGhost.Background = (Brush)FindResource("AccentTint");
        }

        if (label is not null)
            label.Text = $"{_pressedPreset.DisplayTitle}  {Clock(minutes)}–{Clock(minutes + duration)}";

        if (!_presetGhostPlaced)
        {
            _presetGhostPlaced = true;
            _presetDay = day;
            _presetMinutes = minutes;
            _presetGhost.BeginAnimation(Canvas.LeftProperty, null);
            _presetGhost.BeginAnimation(Canvas.TopProperty, null);
            Canvas.SetLeft(_presetGhost, left);
            Canvas.SetTop(_presetGhost, top);
            return;
        }

        if (_presetDay == day && _presetMinutes == minutes) return;

        _presetDay = day;
        _presetMinutes = minutes;

        var snap = TimeSpan.FromMilliseconds(75);
        _presetGhost.BeginAnimation(Canvas.LeftProperty, new DoubleAnimation(left, snap) { EasingFunction = Motion.EaseOut });
        _presetGhost.BeginAnimation(Canvas.TopProperty, new DoubleAnimation(top, snap) { EasingFunction = Motion.EaseOut });
    }

    private void EndPresetDrag()
    {
        if (_presetGhost is not null)
        {
            DragLayer.Children.Remove(_presetGhost);
            _presetGhost = null;
        }

        if (_pressedPresetElement is not null) Motion.AnimateOpacity(_pressedPresetElement, 1, Motion.S);

        _presetGhostPlaced = false;
        _presetGhostInBody = false;
        _draggingPreset = false;
        _pressedPreset = null;
        _pressedPresetElement = null;

        if (IsMouseCaptured) ReleaseMouseCapture();
        Mouse.Capture(null);
    }

    // ==================== 时间轴拖块 ====================

    private void BeginBlockDrag(Point p)
    {
        if (_pressedBlock is null || _pressedBlockElement is null) return;

        _draggingBlock = true;
        _blockGhostPlaced = false;
        Motion.AnimateScale(_pressedBlockElement, 1.0, Motion.Xs);
        Motion.AnimateOpacity(_pressedBlockElement, 0.3, Motion.Xs);

        var height = Math.Max(22, MainViewModel.DurationMinutes(_pressedBlock.Task) / 60.0 * _vm.PxPerHourValue);

        var ghost = new Border
        {
            Width = MainViewModel.DayWidth - 10,
            Height = height,
            Background = (Brush)FindResource("Surface2"),
            BorderBrush = (Brush)FindResource("Accent"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = height < 28 ? new Thickness(6, 1, 6, 1) : new Thickness(6, 3, 6, 3),
            Opacity = 0,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new ScaleTransform(0.98, 0.98),
            Effect = Motion.CreateFloatingShadow(),
            IsHitTestVisible = false,
            Child = new TextBlock
            {
                Text = _pressedBlock.Title,
                Foreground = (Brush)FindResource("TextPrimary"),
                FontFamily = (FontFamily)FindResource("AppFont"),
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis
            }
        };

        _blockGhost = ghost;
        DragLayer.Children.Add(ghost);
        Motion.AnimateOpacity(ghost, 0.96, Motion.Xs);
        Motion.AnimateScale(ghost, 1.02, TimeSpan.FromMilliseconds(120));
        Mouse.Capture(this, CaptureMode.SubTree);
        UpdateBlockDrag(p);
    }

    private (int Day, int Minutes, double Left, double Top) SnapSlotAt(Point p, int duration, TaskVM? exclude = null)
    {
        if (BodyScroll is not { } body) return (0, 0, p.X, p.Y);
        var origin = body.TranslatePoint(new Point(0, 0), this);
        var contentX = p.X - origin.X + body.HorizontalOffset;
        var contentY = p.Y - origin.Y + body.VerticalOffset;

        var day = (int)Math.Floor(contentX / MainViewModel.DayWidth);
        day = Math.Clamp(day, 0, Math.Max(0, _vm.TimelineDays.Count - 1));

        var interval = Math.Max(1, _vm.IntervalMinutes);
        var minutes = IsNearRedLine(p)
            ? _vm.CurrentMinutes
            : (int)Math.Round(contentY / _vm.PxPerHourValue * 60.0 / interval) * interval;

        minutes = Math.Clamp(minutes, 0, Math.Max(0, 24 * 60 - duration));

        // 幽灵直接吸到最近的空档：看到的就是松手后落的位置，不会先重叠再跳
        if (day < _vm.TimelineDays.Count)
            minutes = _vm.FindFreeStart(_vm.TimelineDays[day].Date, minutes, duration, exclude);

        var left = origin.X + day * MainViewModel.DayWidth + 5 - body.HorizontalOffset;
        var top = origin.Y + minutes / 60.0 * _vm.PxPerHourValue - body.VerticalOffset;
        return (day, minutes, left, top);
    }

    private void UpdateBlockDrag(Point p)
    {
        if (_blockGhost is null || _pressedBlock is null) return;

        var duration = MainViewModel.DurationMinutes(_pressedBlock.Task);
        var (day, minutes, left, top) = SnapSlotAt(p, duration, _pressedBlock.Task);

        if (!_blockGhostPlaced)
        {
            _blockGhostPlaced = true;
            _blockDay = day;
            _blockMinutes = minutes;
            Canvas.SetLeft(_blockGhost, left);
            Canvas.SetTop(_blockGhost, top);
            return;
        }

        if (_blockDay == day && _blockMinutes == minutes) return;

        _blockDay = day;
        _blockMinutes = minutes;

        var snap = TimeSpan.FromMilliseconds(75);
        _blockGhost.BeginAnimation(Canvas.LeftProperty, new DoubleAnimation(left, snap) { EasingFunction = Motion.EaseOut });
        _blockGhost.BeginAnimation(Canvas.TopProperty, new DoubleAnimation(top, snap) { EasingFunction = Motion.EaseOut });
    }

    private void EndBlockDrag()
    {
        if (_blockGhost is not null)
        {
            DragLayer.Children.Remove(_blockGhost);
            _blockGhost = null;
        }

        if (_pressedBlockElement is not null) Motion.AnimateOpacity(_pressedBlockElement, 1, Motion.S);

        _blockGhostPlaced = false;
        _draggingBlock = false;
        _pressedBlock = null;
        _pressedBlockElement = null;

        if (IsMouseCaptured) ReleaseMouseCapture();
        Mouse.Capture(null);
    }

    // ==================== 时间轴块边缘调整时间（5 分钟为单位） ====================

    private double RawMinutesAt(Point p)
    {
        if (BodyScroll is not { } body) return 0;
        var origin = body.TranslatePoint(new Point(0, 0), this);
        var contentY = p.Y - origin.Y + body.VerticalOffset;
        return contentY / _vm.PxPerHourValue * 60.0;
    }

    private void BeginBlockResize(Point p)
    {
        if (_resizeCandidateBlock is null) return;

        var task = _resizeCandidateBlock.Task;
        if (task.StartTime is not { } startTime) return;

        _pendingOpen = null;
        _resizingBlock = true;
        _resizeOrigStartMin = (int)startTime.TotalMinutes;
        _resizeOrigEndMin = (int)(task.EndTime?.TotalMinutes ?? _resizeOrigStartMin + 30);
        if (_resizeOrigEndMin <= _resizeOrigStartMin) _resizeOrigEndMin = _resizeOrigStartMin + 30;

        (_resizeMinBound, _resizeMaxBound) = _vm.GetResizeBounds(task);
        _resizeCurrentStartMin = _resizeOrigStartMin;
        _resizeCurrentEndMin = _resizeOrigEndMin;

        _resizePill = new Border
        {
            Background = (Brush)FindResource("Surface4"),
            BorderBrush = (Brush)FindResource("Accent"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8, 3, 8, 3),
            Effect = Motion.CreateFloatingShadow(10, 3, 0.2),
            IsHitTestVisible = false,
            Child = new TextBlock
            {
                FontFamily = (FontFamily)FindResource("AppFont"),
                FontSize = 11.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)FindResource("TextPrimary")
            }
        };
        DragLayer.Children.Add(_resizePill);

        Mouse.OverrideCursor = Cursors.SizeNS;
        Mouse.Capture(this, CaptureMode.SubTree);
        UpdateBlockResize(p);
    }

    private void UpdateBlockResize(Point p)
    {
        if (_resizeCandidateBlock is null) return;

        var rawMinutes = RawMinutesAt(p);
        var snapped = IsNearRedLine(p)
            ? _vm.CurrentMinutes
            : (int)Math.Round(rawMinutes / 5.0) * 5;

        int newStart;
        int newEnd;
        if (_resizeTopEdge)
        {
            newStart = Math.Clamp(snapped, _resizeMinBound, _resizeOrigEndMin - 5);
            newEnd = _resizeOrigEndMin;
        }
        else
        {
            newStart = _resizeOrigStartMin;
            newEnd = Math.Clamp(snapped, _resizeOrigStartMin + 5, _resizeMaxBound);
        }

        _resizeCurrentStartMin = newStart;
        _resizeCurrentEndMin = newEnd;

        var pxPerHour = _vm.PxPerHourValue;
        _resizeCandidateBlock.Top = newStart / 60.0 * pxPerHour;
        _resizeCandidateBlock.Height = Math.Max(22, (newEnd - newStart) / 60.0 * pxPerHour);
        _resizeCandidateBlock.PreviewTimeText = $"{Clock(newStart)}–{Clock(newEnd)}";

        if (_resizePill is not null)
        {
            if (_resizePill.Child is TextBlock label)
                label.Text = $"{Clock(newStart)}–{Clock(newEnd)} · {FormatDuration(newEnd - newStart)}";

            _resizePill.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var edgeY = (_resizeTopEdge ? newStart : newEnd) / 60.0 * pxPerHour;
            var origin = BodyScroll?.TranslatePoint(new Point(0, 0), this) ?? default;
            var blockScreenY = origin.Y + edgeY - (BodyScroll?.VerticalOffset ?? 0);
            var pillY = _resizeTopEdge
                ? blockScreenY - _resizePill.DesiredSize.Height - 6
                : blockScreenY + 6;
            pillY = Math.Clamp(pillY, 44, Math.Max(44, ActualHeight - 48));
            var pillX = Math.Clamp(p.X + 14, 16, Math.Max(16, ActualWidth - _resizePill.DesiredSize.Width - 16));

            Canvas.SetLeft(_resizePill, pillX);
            Canvas.SetTop(_resizePill, pillY);
        }
    }

    private void EndBlockResize(bool restoreVisual)
    {
        if (_resizeCandidateBlock is not null)
        {
            _resizeCandidateBlock.PreviewTimeText = null;
            if (restoreVisual)
            {
                var pxPerHour = _vm.PxPerHourValue;
                _resizeCandidateBlock.Top = _resizeOrigStartMin / 60.0 * pxPerHour;
                _resizeCandidateBlock.Height = Math.Max(22, (_resizeOrigEndMin - _resizeOrigStartMin) / 60.0 * pxPerHour);
            }
        }

        if (_resizePill is not null)
        {
            DragLayer.Children.Remove(_resizePill);
            _resizePill = null;
        }

        _resizingBlock = false;
        _resizeCandidateBlock = null;
        _pressedBlock = null;
        _pressedBlockElement = null;

        Mouse.OverrideCursor = null;
        if (IsMouseCaptured) ReleaseMouseCapture();
        Mouse.Capture(null);
    }

    private static string FormatDuration(int minutes)
    {
        var h = minutes / 60;
        var m = minutes % 60;
        if (h == 0) return $"{m}分";
        return m == 0 ? $"{h}小时" : $"{h}小时{m}分";
    }

    private bool IsInsideBody(Point p)
    {
        if (BodyScroll is not { } body || body.ActualWidth <= 0 || body.ActualHeight <= 0) return false;

        var topLeft = body.TranslatePoint(new Point(0, 0), this);
        return new Rect(topLeft, body.RenderSize).Contains(p);
    }

    // ==================== 命中测试 ====================

    private LaneVM? HitLane(Point p)
    {
        if (!LanesHost.IsVisible) return null;

        foreach (var lane in _vm.OverviewLanes)
        {
            if (LanesHost.ItemContainerGenerator.ContainerFromItem(lane) is not FrameworkElement el) continue;
            if (!el.IsVisible) continue;

            var topLeft = el.TranslatePoint(new Point(0, 0), this);
            var rect = new Rect(topLeft, el.RenderSize);
            if (rect.Contains(p)) return lane;
        }

        return null;
    }

    private GoalVM? HitGoal(Point p)
    {
        if (!GoalsHost.IsVisible) return null;

        foreach (var goal in _vm.ActiveGoals)
        {
            if (GoalsHost.ItemContainerGenerator.ContainerFromItem(goal) is not FrameworkElement el) continue;
            if (!el.IsVisible) continue;

            var topLeft = el.TranslatePoint(new Point(0, 0), this);
            var rect = new Rect(topLeft, el.RenderSize);
            if (rect.Contains(p)) return goal;
        }

        return null;
    }

    private QuadrantVM? HitQuadrant(Point p)
    {
        if (QuadrantsHost is not { IsVisible: true } qHost) return null;

        foreach (var quadrant in _vm.Quadrants)
        {
            if (qHost.ItemContainerGenerator.ContainerFromItem(quadrant) is not FrameworkElement el) continue;
            if (!el.IsVisible) continue;

            var topLeft = el.TranslatePoint(new Point(0, 0), this);
            var rect = new Rect(topLeft, el.RenderSize);
            if (rect.Contains(p)) return quadrant;
        }

        return null;
    }

    private ItemsControl? LaneItemsControl(LaneVM lane)
    {
        if (!LanesHost.IsVisible) return null;
        if (LanesHost.ItemContainerGenerator.ContainerFromItem(lane) is not FrameworkElement laneEl) return null;

        return FindDescendant<ItemsControl>(laneEl, ic => ReferenceEquals(ic.ItemsSource, lane.Items));
    }

    /// <summary>
    /// 落点在「去掉被拖那张之后」的第几位。不排自己：否则同一档位内向下拖会差一位。
    /// 比较的是布局位置（减掉开缝时的位移），否则卡片一让位就把落点算翻了。
    /// </summary>
    private int InsertIndex(LaneVM lane, Point p)
    {
        var itemsControl = LaneItemsControl(lane);
        if (itemsControl is null) return Math.Max(0, lane.Items.Count - 1);

        var index = 0;

        for (var i = 0; i < lane.Items.Count; i++)
        {
            var item = lane.Items[i];
            var isDragged = ReferenceEquals(item, _pressedTask);

            if (itemsControl.ItemContainerGenerator.ContainerFromIndex(i) is FrameworkElement container)
            {
                var shift = _gapTargets.TryGetValue(container, out var offset) ? offset : 0;
                var topLeft = container.TranslatePoint(new Point(0, 0), this);
                var center = topLeft.Y - shift + container.ActualHeight / 2;

                if (p.Y < center) return index;
            }

            if (!isDragged) index++;
        }

        return index;
    }

    // ==================== 拖拽开缝（手机桌面拖图标那种手感） ====================

    /// <summary>
    /// 目标位置之后的卡片滑开一个卡高，让出一个空位。
    /// 同一档位内是「先抽出、再插入」：往下的拖动会让中间那些卡片上滑补齐，反之亦然。
    /// </summary>
    private void UpdateGaps(LaneVM? lane, Point p)
    {
        var itemsControl = lane is null ? null : LaneItemsControl(lane);

        if (itemsControl is null || _pressedTask is null || _pressedCard is null)
        {
            ClearGaps();
            return;
        }

        if (!ReferenceEquals(itemsControl, _gapItems))
        {
            ClearGaps();
            _gapItems = itemsControl;
        }

        var step = Math.Max(8, _pressedCard.ActualHeight + 8); // CardBorder 的下边距是 8
        var index = InsertIndex(lane!, p);
        var source = lane!.Items.IndexOf(_pressedTask);
        var index2 = 0;

        for (var i = 0; i < lane.Items.Count; i++)
        {
            var item = lane.Items[i];
            var isDragged = ReferenceEquals(item, _pressedTask);
            var container = itemsControl.ItemContainerGenerator.ContainerFromIndex(i) as FrameworkElement;

            var target = 0d;

            if (!isDragged)
            {
                if (source < 0)
                    target = index2 >= index ? step : 0;                       // 跨档位：往下推开
                else if (index > source)
                    target = index2 >= source && index2 < index ? -step : 0;   // 往下拖：中间的往上补
                else if (index < source)
                    target = index2 >= index && index2 < source ? step : 0;    // 往上拖：中间的往下让

                index2++;
            }

            if (container is not null) ShiftTo(container, target);
        }
    }

    private void ShiftTo(FrameworkElement element, double target)
    {
        if (_gapTargets.TryGetValue(element, out var current) && Math.Abs(current - target) < 0.5) return;

        _gapTargets[element] = target;

        if (element.RenderTransform is not TranslateTransform transform)
        {
            transform = new TranslateTransform();
            element.RenderTransform = transform;
        }

        transform.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(target, TimeSpan.FromMilliseconds(120))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });
    }

    /// <summary>松手 / 离开档位：把所有位移清掉。重建列表后也要再清一遍，防止容器复用把偏移带过去。</summary>
    private void ClearGaps()
    {
        foreach (var element in _gapTargets.Keys)
        {
            if (element.RenderTransform is not TranslateTransform transform) continue;

            transform.BeginAnimation(TranslateTransform.YProperty, null);
            transform.Y = 0;
        }

        _gapTargets.Clear();
        _gapItems = null;
    }

    private void ResetAllShifts()
    {
        ClearGaps();

        foreach (var lane in _vm.OverviewLanes)
        {
            var itemsControl = LaneItemsControl(lane);
            if (itemsControl is null) continue;

            for (var i = 0; i < lane.Items.Count; i++)
            {
                if (itemsControl.ItemContainerGenerator.ContainerFromIndex(i) is not FrameworkElement container) continue;
                if (container.RenderTransform is not TranslateTransform transform) continue;

                transform.BeginAnimation(TranslateTransform.YProperty, null);
                transform.Y = 0;
            }
        }
    }

    // ==================== 视觉树工具 ====================

    private Point ToTreeEditorCanvasPoint(Point windowPoint)
    {
        if (TreeEditorViewport is not { } vp) return windowPoint;
        var vpPt = TranslatePoint(windowPoint, vp);
        var zoom = Math.Max(0.1, _vm.GoalTreeEditor.Zoom);
        return new Point(
            (vpPt.X - _vm.GoalTreeEditor.PanX) / zoom,
            (vpPt.Y - _vm.GoalTreeEditor.PanY) / zoom);
    }

    private static GoalPortSide ParsePortSide(string? uid) => (uid ?? "").Trim() switch
    {
        "Top" => GoalPortSide.Top,
        "Right" => GoalPortSide.Right,
        "Bottom" => GoalPortSide.Bottom,
        "Left" => GoalPortSide.Left,
        _ => GoalPortSide.Auto
    };

    private (GoalNodeVM? Node, GoalPortSide Side) HitTreeEditorTargetNode(Point canvasPoint, GoalNodeVM? exclude)
    {
        var goal = _vm.GoalTreeEditor.Goal;
        if (goal is null) return (null, GoalPortSide.Auto);

        foreach (var node in goal.AllCanvasNodes)
        {
            if (node.IsRoot || ReferenceEquals(node, exclude)) continue;
            var hitRect = new Rect(node.X - 14, node.Y - 14, node.Width + 28, node.Height + 28);
            if (hitRect.Contains(canvasPoint))
                return (node, node.NearestPortSide(canvasPoint));
        }

        return (null, GoalPortSide.Auto);
    }

    private void CommitAnyEdgeLabelEdit()
    {
        var goal = _vm.GoalTreeEditor.Goal;
        if (goal is null) return;
        foreach (var edge in goal.Edges)
        {
            if (edge.IsEditingLabel)
                _vm.GoalTreeEditor.CommitEdgeLabelEdit(edge);
        }
    }

    private void CommitCardGoalNodeDrag(GoalNodeVM node)
    {
        var goal = node.Owner;
        var (sx, sy) = goal.NormalizeMinBounds();
        if (sx > 0 || sy > 0)
        {
            goal.ViewPanX -= sx * goal.ViewZoom;
            goal.ViewPanY -= sy * goal.ViewZoom;
        }
        _vm.OnGoalStructureChanged(rebuildTimelineAndCards: false);
    }

    private static bool IsInteractive(DependencyObject? source)
    {
        var d = source;
        while (d is not null)
        {
            if (d is ButtonBase or TextBoxBase or Thumb or ListBoxItem) return true;

            // 滚动条上的任何一个位置都不该变成「在空白处按住」——
            // 否则拖滚动条会被当成拖时间段，凭空建出一条日程。
            if (d is ScrollBar) return true;

            d = VisualTreeHelper.GetParent(d);
        }

        return false;
    }

    private bool IsInsideElement(Point p, FrameworkElement? element)
    {
        if (element is null || !element.IsVisible || element.ActualWidth <= 0 || element.ActualHeight <= 0) return false;
        var topLeft = element.TranslatePoint(new Point(0, 0), this);
        return new Rect(topLeft, element.RenderSize).Contains(p);
    }

    private void CancelActiveInteractions()
    {
        CancelPendingExpand();
        StopZoomAnimation();
        StopTagDetailScrollAnimation();
        _spacePanKeyDown = false;
        if (_goalPanTarget != GoalCanvasTarget.None) EndGoalCanvasPan();
        if (_draggingEditorPort)
        {
            _draggingEditorPort = false;
            _vm.GoalTreeEditor.CancelPortDrag();
            if (IsMouseCaptured) ReleaseMouseCapture();
            Mouse.Capture(null);
        }
        if (_draggingCardGoalNode)
        {
            _draggingCardGoalNode = false;
            _pressedCardGoalNode = null;
            if (IsMouseCaptured) ReleaseMouseCapture();
            Mouse.Capture(null);
        }
        else
        {
            _pressedCardGoalNode = null;
        }
        if (_draggingEditorNode)
        {
            _draggingEditorNode = false;
            _pressedEditorNode = null;
            if (IsMouseCaptured) ReleaseMouseCapture();
            Mouse.Capture(null);
        }
        _pressedPickerNode = null;
        if (_panning) EndPan();
        else UpdatePanCursor();
        if (_resizingBlock) EndBlockResize(restoreVisual: true);
        _resizeCandidateBlock = null;
        if (_dragging) CancelDrag();
        if (_draggingPreset) EndPresetDrag();
        if (_draggingBlock) EndBlockDrag();
        if (_creating)
        {
            if (_createGhost is not null)
            {
                DragLayer.Children.Remove(_createGhost);
                _createGhost = null;
            }
            _creating = false;
            _createAnchorSnappedToNow = false;
        }

        ResetPressedVisuals();
    }

    private void ResetPressedVisuals()
    {
        if (_pressedCard is not null) Motion.AnimateScale(_pressedCard, 1.0, Motion.S);
        if (_pressedTagCard is not null) Motion.AnimateScale(_pressedTagCard, 1.0, Motion.S);
        if (_pressedPersonCard is not null) Motion.AnimateScale(_pressedPersonCard, 1.0, Motion.S);
        if (_pressedLocationCard is not null) Motion.AnimateScale(_pressedLocationCard, 1.0, Motion.S);
        if (_pressedGoalPageCard is not null) Motion.AnimateScale(_pressedGoalPageCard, 1.0, Motion.S);
        if (_pressedPresetElement is not null) Motion.AnimateScale(_pressedPresetElement, 1.0, Motion.S);
        if (_pressedBlockElement is not null) Motion.AnimateScale(_pressedBlockElement, 1.0, Motion.S);
    }

    // ==================== 页面 / 弹窗 / 提示动效 ====================

    private void AnimateActivePage()
    {
        FrameworkElement? page = _vm.SelectedNav.Kind switch
        {
            PageKind.Overview => PageOverview,
            PageKind.Timeline => PageTimeline,
            PageKind.Goals => PageGoals,
            PageKind.Tags => PageTags,
            PageKind.Eisenhower => PageEisenhower,
            PageKind.Deadlines => PageDeadlines,
            PageKind.Future => PageFuture,
            PageKind.Overdue => PageOverdue,
            _ => PagePlaceholder
        };

        if (page is null) return;

        page.BeginAnimation(OpacityProperty, null);
        page.Opacity = 0;

        if (page.RenderTransform is TranslateTransform shift)
        {
            shift.BeginAnimation(TranslateTransform.YProperty, null);
            shift.Y = 8;
            Motion.AnimateOffsetY(shift, 0, Motion.M);
        }

        Motion.AnimateOpacity(page, 1, Motion.M);
    }

    private void AnimateModalVisibility(
        Grid layer,
        Border scrim,
        Border dialog,
        ScaleTransform scale,
        TranslateTransform shift,
        ContentControl bodyHost,
        string templateKey,
        bool open,
        Func<bool> isStillOpen)
    {
        if (open)
        {
            var desired = (ControlTemplate)FindResource(templateKey);
            if (!ReferenceEquals(bodyHost.Template, desired))
            {
                bodyHost.Template = desired;
                bodyHost.ApplyTemplate();
            }

            layer.Visibility = Visibility.Visible;
            layer.IsHitTestVisible = true;

            scrim.BeginAnimation(OpacityProperty, null);
            dialog.BeginAnimation(OpacityProperty, null);
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            shift.BeginAnimation(TranslateTransform.YProperty, null);

            scrim.Opacity = 0;
            dialog.Opacity = 0;
            scale.ScaleX = 0.97;
            scale.ScaleY = 0.97;
            shift.Y = 10;

            Motion.AnimateOpacity(scrim, 1, TimeSpan.FromMilliseconds(180));
            Motion.AnimateOpacity(dialog, 1, Motion.M);
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, Motion.M) { EasingFunction = Motion.EaseOut });
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, Motion.M) { EasingFunction = Motion.EaseOut });
            Motion.AnimateOffsetY(shift, 0, Motion.M);
        }
        else
        {
            layer.IsHitTestVisible = false;

            Motion.AnimateOpacity(scrim, 0, Motion.S, Motion.EaseIn);
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.98, Motion.S) { EasingFunction = Motion.EaseIn });
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.98, Motion.S) { EasingFunction = Motion.EaseIn });
            Motion.AnimateOffsetY(shift, 6, Motion.S, Motion.EaseIn);
            Motion.AnimateOpacity(dialog, 0, Motion.S, Motion.EaseIn, () =>
            {
                if (!isStillOpen())
                {
                    layer.Visibility = Visibility.Collapsed;
                    bodyHost.Template = null;
                }
            });
        }
    }

    private void ShowToast(string message, bool highlight)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => ShowToast(message, highlight));
            return;
        }

        _toastSeq++;
        _toastTimer.Stop();

        ToastText.Text = message;
        ToastDot.SetResourceReference(Shape.FillProperty, highlight ? "Warning" : "Accent");
        ToastPill.Visibility = Visibility.Visible;

        ToastPill.BeginAnimation(OpacityProperty, null);
        ToastShift.BeginAnimation(TranslateTransform.YProperty, null);
        ToastPill.Opacity = 0;
        ToastShift.Y = 8;

        Motion.AnimateOpacity(ToastPill, 1, Motion.S);
        Motion.AnimateOffsetY(ToastShift, 0, Motion.S);
        _toastTimer.Start();
    }

    private void HideToast()
    {
        _toastTimer.Stop();
        var seq = _toastSeq;

        Motion.AnimateOffsetY(ToastShift, 4, TimeSpan.FromMilliseconds(180), Motion.EaseIn);
        Motion.AnimateOpacity(ToastPill, 0, TimeSpan.FromMilliseconds(180), Motion.EaseIn, () =>
        {
            if (seq == _toastSeq) ToastPill.Visibility = Visibility.Collapsed;
        });
    }

    private void OnTaskTouched(TaskVM task)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            FrameworkElement? target = null;

            if (_vm.IsTagDetailOpen)
                target = FindDescendant<Border>(TagDetailDaysHost, b => Equals(b.Tag, "tagtaskcard") && ReferenceEquals(b.DataContext, task));
            else if (_vm.IsOverview)
                target = FindDescendant<Border>(LanesHost, b => Equals(b.Tag, "card") && ReferenceEquals(b.DataContext, task));
            else if (_vm.IsDeadlines)
                target = FindDescendant<Border>(DeadlinesHost, b => Equals(b.Tag, "card") && ReferenceEquals(b.DataContext, task));
            else if (_vm.IsFuture)
                target = FindDescendant<Border>(FutureHost, b => Equals(b.Tag, "card") && ReferenceEquals(b.DataContext, task));
            else if (_vm.IsOverduePage)
                target = FindDescendant<Border>(OverdueHost, b => Equals(b.Tag, "card") && ReferenceEquals(b.DataContext, task));
            else if (_vm.IsEisenhower)
                target = FindDescendant<Border>(QuadrantsHost, b => Equals(b.Tag, "boardrow") && ReferenceEquals(b.DataContext, task));
            else if (_vm.IsTimeline)
                target = FindDescendant<Border>(DaysHost, b => Equals(b.Tag, "block") &&
                    b.DataContext is TimelineBlockVM blk && ReferenceEquals(blk.Task, task));

            if (target is null) return;

            var scale = Motion.EnsureScale(target);
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            scale.ScaleX = 0.96;
            scale.ScaleY = 0.96;
            Motion.AnimateScale(target, 1.0, TimeSpan.FromMilliseconds(180));
        }), DispatcherPriority.Loaded);
    }

    private static DependencyObject? FindAncestorTag(DependencyObject? source, string tag)
    {
        var d = source;
        while (d is not null)
        {
            if (d is FrameworkElement fe && Equals(fe.Tag, tag)) return d;
            d = VisualTreeHelper.GetParent(d);
        }

        return null;
    }

    private static T? FindDataContext<T>(DependencyObject? source) where T : class
    {
        var d = source;
        while (d is not null)
        {
            if (d is FrameworkElement { DataContext: T match }) return match;
            d = VisualTreeHelper.GetParent(d);
        }

        return null;
    }

    private static T? FindDescendant<T>(DependencyObject? root, Func<T, bool>? match = null) where T : DependencyObject
    {
        if (root is null) return null;
        var queue = new Queue<DependencyObject>();
        queue.Enqueue(root);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            var count = VisualTreeHelper.GetChildrenCount(current);
            for (var i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(current, i);
                if (child is T typed && (match is null || match(typed))) return typed;
                queue.Enqueue(child);
            }
        }

        return null;
    }
}
