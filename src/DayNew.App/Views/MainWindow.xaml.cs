using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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
    private readonly MainViewModel _vm = new();
    private readonly TrayService _tray;
    private readonly NotificationService _notifications;
    private bool _allowExit;
    private bool _hasCentered;
    private bool _cleanedUp;

    private Point _pressPoint;
    private TaskVM? _pressedTask;
    private Border? _pressedCard;
    private GoalVM? _pressedGoal;
    private TimelineBlockVM? _pressedBlock;
    private FrameworkElement? _pressedBlockElement;
    private bool _draggingBlock;
    private Border? _blockGhost;
    private int _blockDay;
    private int _blockMinutes;
    private bool _timelineScrolled;

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

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _vm;
        ApplyThemeIcon();
        UpdateMaxIcon();

        _tray = new TrayService(
            onOpen: ShowFromTray,
            onToggleWindow: ToggleTrayWindow,
            getNotifyEnabled: () => _vm.NotifyEnabled,
            setNotifyEnabled: v => _vm.NotifyEnabled = v,
            getAutoStartEnabled: () => _vm.AutoStartEnabled,
            setAutoStartEnabled: v => _vm.AutoStartEnabled = v,
            onExit: ExitApplication);

        _notifications = new NotificationService(
            _vm.Settings,
            () => _vm.AllSchedules,
            onOpenWindow: ShowFromTray,
            onCompleteTask: id =>
            {
                if (_vm.CompleteTaskById(id)) _vm.FlushSave();
            });

        var (todayPending, overdueCount) = _vm.GetSummaryCounts();
        _tray.UpdateTooltip(todayPending, overdueCount);

        PreviewMouseLeftButtonDown += OnPreviewDown;
        PreviewMouseMove += OnPreviewMove;
        PreviewMouseLeftButtonUp += OnPreviewUp;
        PreviewMouseRightButtonDown += OnPreviewRightDown;
        PreviewMouseRightButtonUp += OnPreviewRightUp;
        PreviewKeyDown += OnPreviewKeyDown;
        PreviewKeyUp += OnPreviewKeyUp;
        Deactivated += (_, _) => CancelActiveInteractions();
        LostMouseCapture += (_, _) =>
        {
            if (_dragging || _draggingBlock || _panning || _resizingBlock) CancelActiveInteractions();
        };
        StateChanged += (_, _) => UpdateMaxIcon();
        Closing += OnWindowClosing;
        Closed += (_, _) =>
        {
            if (_allowExit) Application.Current.Shutdown();
        };

        // 不依赖 WindowStartupLocation：在多屏 / 高 DPI 下它会把窗口丢到屏幕外；只首开时居中一次
        Loaded += (_, _) =>
        {
            if (_hasCentered) return;
            _hasCentered = true;
            CenterOnScreen();
        };

        _vm.PropertyChanged += OnViewModelPropertyChanged;
        _vm.ToastRequested += ShowToast;
        _vm.TaskTouched += OnTaskTouched;
        _vm.SummaryChanged += _tray.UpdateTooltip;
        _toastTimer.Tick += (_, _) => HideToast();
        _expandTimer.Tick += (_, _) => CommitPendingExpand();
        _nowLineTimer.Tick += (_, _) => _vm.RefreshCurrentTime();
        _nowLineTimer.Start();

        NavHost.SelectionChanged += (_, _) =>
        {
            CancelPendingExpand();
            AnimateActivePage();

            if (!_vm.IsTimeline || _timelineScrolled) return;
            _timelineScrolled = true;
            Dispatcher.BeginInvoke(new Action(ScrollToToday), DispatcherPriority.Loaded);
        };
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.IsEditorOpen))
            AnimateModalVisibility(EditorLayer, EditorScrim, EditorDialog, EditorScale, EditorShift,
                _vm.IsEditorOpen, () => _vm.IsEditorOpen);
        else if (e.PropertyName == nameof(MainViewModel.IsSettingsOpen))
            AnimateModalVisibility(SettingsLayer, SettingsScrim, SettingsDialog, SettingsScale, SettingsShift,
                _vm.IsSettingsOpen, () => _vm.IsSettingsOpen);
    }

    // ==================== 标题栏 ====================

    // 最大化时 Windows 会把无边框窗口往屏幕外多长一圈（这里实测每边 6 DIP），
    // 结果就是最底下的横向滚动条和状态栏被切掉。自己接管最大化尺寸：正好贴住工作区。
    private const int WmGetMinMaxInfo = 0x0024;
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
        handled = true;

        return IntPtr.Zero;
    }

    private bool HandleHorizontalWheel(int delta)
    {
        if (_vm.IsEditorOpen || _vm.IsSettingsOpen) return false;

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

        if (target is null && _vm.IsTimeline && IsInsideElement(pos, PageTimeline) && BodyScroll.ScrollableWidth > 0)
            target = BodyScroll;

        if (target is null) return false;

        if (ReferenceEquals(target, BodyScroll)) StopScrollAnimation();
        target.ScrollToHorizontalOffset(Math.Clamp(target.HorizontalOffset + delta * 0.6, 0, target.ScrollableWidth));
        return true;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, int flags);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

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

        if (!_allowExit && _vm.CloseToTray)
        {
            e.Cancel = true;
            HideToTray();
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

    private void HideToTray()
    {
        CancelActiveInteractions();
        _vm.CloseSettings();
        _vm.FlushSave();
        Hide();
    }

    private void ToggleTrayWindow()
    {
        if (IsVisible && WindowState != WindowState.Minimized && IsActive)
            HideToTray();
        else
            ShowFromTray();
    }

    private void ExitApplication()
    {
        _allowExit = true;
        Application.Current.Shutdown();
    }

    public void CleanupOnExit()
    {
        if (_cleanedUp) return;
        _cleanedUp = true;
        _nowLineTimer.Stop();
        _vm.FlushSave();
        _notifications.Dispose();
        _tray.Dispose();
    }

    private void SettingsOpen_Click(object sender, RoutedEventArgs e) => _vm.OpenSettings();

    private void SettingsClose_Click(object sender, RoutedEventArgs e) => _vm.CloseSettings();

    private void SettingsScrim_Click(object sender, MouseButtonEventArgs e) => _vm.CloseSettings();

    private void OpenDataFolder_Click(object sender, RoutedEventArgs e) => _vm.OpenDataFolder();

    private void TestNotify_Click(object sender, RoutedEventArgs e) => _notifications.SendTestNotification();

    // ==================== 卡片内操作 ====================

    private void Check_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is TaskVM task) _vm.ToggleDone(task);
    }

    private void Repeat_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is TaskVM task) _vm.CycleRepeat(task);
    }

    private void Remind_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is TaskVM task) _vm.ToggleRemind(task);
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is TaskVM task) _vm.Delete(task);
    }

    private void TagRemove_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe) return;
        if (fe.Tag is not TagVM tag) return;
        var task = FindDataContext<TaskVM>(fe);
        if (task is not null) _vm.RemoveTag(task, tag);
    }

    private void TagInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox box) return;
        if (e.Key != Key.Enter) return;

        var task = FindDataContext<TaskVM>(box);
        if (task is not null) _vm.AddTag(task, box.Text);
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

    // ==================== 标签 / 四象限 ====================

    private void Urgent_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is TaskVM task) _vm.ToggleUrgent(task);
    }

    private void Important_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is TaskVM task) _vm.ToggleImportant(task);
    }

    private void DeleteTagRow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: TagVM tag }) _vm.DeleteTag(tag);
    }

    // ==================== 视图（时间轴） ====================

    private void Zoom_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: int index }) return;

        StopScrollAnimation();

        var targetPx = _vm.PresetPxPerHour(index);
        var centerY = BodyScroll.ViewportHeight * 0.5;
        StartSmoothZoom(targetPx, centerY);
    }

    private void BodyScroll_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        StopScrollAnimation();

        var mods = Keyboard.Modifiers;
        if ((mods & ModifierKeys.Alt) != 0)
        {
            e.Handled = true;
            var factor = Math.Pow(1.16, e.Delta / 120.0);
            var basePx = _zoomAnimHandler is not null ? _zoomTargetPx : _vm.PxPerHourValue;
            StartSmoothZoom(basePx * factor, e.GetPosition(BodyScroll).Y);
            return;
        }

        if ((mods & ModifierKeys.Shift) != 0)
        {
            e.Handled = true;
            BodyScroll.ScrollToHorizontalOffset(
                Math.Clamp(BodyScroll.HorizontalOffset - e.Delta * 0.75, 0, BodyScroll.ScrollableWidth));
            return;
        }

        if ((mods & ModifierKeys.Control) != 0)
        {
            e.Handled = true;
            BodyScroll.ScrollToVerticalOffset(
                Math.Clamp(BodyScroll.VerticalOffset - e.Delta * 0.75, 0, BodyScroll.ScrollableHeight));
        }
    }

    private void StartSmoothZoom(double targetPx, double anchorViewportY)
    {
        _zoomTargetPx = Math.Clamp(targetPx, MainViewModel.MinPxPerHour, MainViewModel.MaxPxPerHour);
        _zoomAnchorViewportY = Math.Clamp(anchorViewportY, 0, Math.Max(1, BodyScroll.ViewportHeight));

        var currentPx = _vm.PxPerHourValue;
        _zoomAnchorHours = currentPx > 0 ? (BodyScroll.VerticalOffset + _zoomAnchorViewportY) / currentPx : 0;

        if (_zoomAnimHandler is null)
        {
            _zoomAnimLastTick = DateTime.UtcNow;
            _zoomAnimHandler = OnZoomAnimationFrame;
            CompositionTarget.Rendering += _zoomAnimHandler;
        }
    }

    private void OnZoomAnimationFrame(object? sender, EventArgs e)
    {
        var now = DateTime.UtcNow;
        var dt = Math.Clamp((now - _zoomAnimLastTick).TotalSeconds, 0.001, 0.05);
        _zoomAnimLastTick = now;

        var current = _vm.PxPerHourValue;
        var diff = _zoomTargetPx - current;
        var done = Math.Abs(diff) < 0.35;
        var nextPx = done ? _zoomTargetPx : current + diff * (1.0 - Math.Exp(-dt * 28.0));

        _vm.SetPxPerHour(nextPx);
        BodyScroll.UpdateLayout();

        var offset = Math.Clamp(_zoomAnchorHours * nextPx - _zoomAnchorViewportY, 0, BodyScroll.ScrollableHeight);
        BodyScroll.ScrollToVerticalOffset(offset);

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
        var maxV = Math.Max(0, _vm.GridHeight - BodyScroll.ViewportHeight);
        var v = Math.Clamp(_vm.NowLineTop - 44, 0, maxV);
        return (h, v);
    }

    private void ScrollToToday()
    {
        StopZoomAnimation();
        StopScrollAnimation();
        var (h, v) = GetTodayNowScrollOffset();
        BodyScroll.ScrollToHorizontalOffset(h);
        BodyScroll.ScrollToVerticalOffset(v);
    }

    private void SmoothScrollToToday()
    {
        StopZoomAnimation();
        StopScrollAnimation();

        var (h, v) = GetTodayNowScrollOffset();
        _scrollFromH = BodyScroll.HorizontalOffset;
        _scrollToH = h;
        _scrollFromV = BodyScroll.VerticalOffset;
        _scrollToV = v;

        if (Math.Abs(_scrollToH - _scrollFromH) < 1 && Math.Abs(_scrollToV - _scrollFromV) < 1) return;

        _scrollAnimStart = DateTime.UtcNow;
        _scrollAnimHandler = OnScrollAnimationFrame;
        CompositionTarget.Rendering += _scrollAnimHandler;
    }

    private void OnScrollAnimationFrame(object? sender, EventArgs e)
    {
        var elapsed = (DateTime.UtcNow - _scrollAnimStart).TotalMilliseconds;
        var t = Math.Clamp(elapsed / Motion.M.TotalMilliseconds, 0, 1);
        var eased = Motion.EaseOut.Ease(t);

        BodyScroll.ScrollToHorizontalOffset(_scrollFromH + (_scrollToH - _scrollFromH) * eased);
        BodyScroll.ScrollToVerticalOffset(_scrollFromV + (_scrollToV - _scrollFromV) * eased);

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
        HeaderScroll.ScrollToHorizontalOffset(BodyScroll.HorizontalOffset);
        GutterScroll.ScrollToVerticalOffset(BodyScroll.VerticalOffset);
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
        if (e.Key == Key.Escape)
        {
            if (_vm.IsSettingsOpen)
            {
                _vm.CloseSettings();
                e.Handled = true;
                return;
            }

            if (_vm.IsEditorOpen)
            {
                _vm.CloseEditor();
                e.Handled = true;
                return;
            }

            if (_panning || _resizingBlock || _dragging || _draggingBlock || _creating)
            {
                CancelActiveInteractions();
                e.Handled = true;
                return;
            }

            _vm.CollapseAll();
            return;
        }

        // 弹窗开着就别再响应全局快捷键
        if (_vm.IsEditorOpen || _vm.IsSettingsOpen) return;

        if (e.Key == Key.Space && _vm.IsTimeline && Keyboard.FocusedElement is not TextBoxBase)
        {
            if (!_spacePanKeyDown)
            {
                _spacePanKeyDown = true;
                UpdatePanCursor();
            }
            e.Handled = true;
            return;
        }

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
        if (e.Key != Key.Space) return;

        _spacePanKeyDown = false;
        if (!_panning) UpdatePanCursor();
    }

    // ==================== 视图画布自由拖动（右键 / 空格+左键） ====================

    private void OnPreviewRightDown(object sender, MouseButtonEventArgs e)
    {
        if (!_vm.IsTimeline || _vm.IsEditorOpen) return;
        if (IsInteractive(e.OriginalSource as DependencyObject)) return;

        var p = e.GetPosition(this);
        if (!IsInsideBody(p)) return;

        BeginPan(p, rightButton: true);
        e.Handled = true;
    }

    private void OnPreviewRightUp(object sender, MouseButtonEventArgs e)
    {
        if (!_panning || !_panWithRightButton) return;

        EndPan();
        e.Handled = true;
    }

    private void BeginPan(Point p, bool rightButton)
    {
        StopZoomAnimation();
        StopScrollAnimation();
        _panning = true;
        _panWithRightButton = rightButton;
        _panStartPoint = p;
        _panStartH = BodyScroll.HorizontalOffset;
        _panStartV = BodyScroll.VerticalOffset;
        UpdatePanCursor();
        Mouse.Capture(this, CaptureMode.SubTree);
    }

    private void UpdatePan(Point p)
    {
        var dx = p.X - _panStartPoint.X;
        var dy = p.Y - _panStartPoint.Y;
        BodyScroll.ScrollToHorizontalOffset(Math.Clamp(_panStartH - dx, 0, BodyScroll.ScrollableWidth));
        BodyScroll.ScrollToVerticalOffset(Math.Clamp(_panStartV - dy, 0, BodyScroll.ScrollableHeight));
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
        if (_panning)
        {
            BodyScroll.Cursor = Cursors.SizeAll;
            BodyScroll.ForceCursor = true;
            Mouse.OverrideCursor = Cursors.SizeAll;
        }
        else if (_spacePanKeyDown && _vm.IsTimeline && !_vm.IsEditorOpen)
        {
            Mouse.OverrideCursor = null;
            BodyScroll.Cursor = Cursors.SizeAll;
            BodyScroll.ForceCursor = true;
        }
        else
        {
            Mouse.OverrideCursor = null;
            BodyScroll.ForceCursor = false;
            BodyScroll.ClearValue(CursorProperty);
        }
    }

    // ==================== 拖拽 ====================

    private void OnPreviewDown(object sender, MouseButtonEventArgs e)
    {
        _pressedGoal = null;
        _pressedBlock = null;
        _resizeCandidateBlock = null;
        _pendingOpen = null;
        _clickedPageBlank = false;

        // 弹窗开着时，底下的东西一概不接鼠标
        if (_vm.IsEditorOpen) return;

        var src = e.OriginalSource as DependencyObject;
        if (IsInteractive(src))
        {
            _pressedTask = null;
            _pressedCard = null;
            return;
        }

        _pressPoint = e.GetPosition(this);

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
            _pressedGoal = (FindAncestorTag(src, "goal") as FrameworkElement)?.DataContext as GoalVM;

            if (_pressedGoal is null)
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

            // 空白处按下：在时间轴网格内进入「拖一段建日程」状态，否则记下页面空白点击用于收起展开卡片
            if (_pressedGoal is null && _pressedBlock is null)
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
        else if (_pressedGoal is not null)
        {
            _vm.ToggleGoalExpand(_pressedGoal);
        }
        else if (clickedBlank)
        {
            _vm.CollapseAll();
        }

        if (_pressedCard is not null) Motion.AnimateScale(_pressedCard, 1.0, Motion.S);
        if (_pressedBlockElement is not null) Motion.AnimateScale(_pressedBlockElement, 1.0, Motion.S);

        _pressedTask = null;
        _pressedCard = null;
        _pressedGoal = null;
        _pressedBlock = null;
        _resizeCandidateBlock = null;
        _pressedBlockElement = null;
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
        if (!LanesHost.IsVisible && !QuadrantsHost.IsVisible) return;

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
        if (!DaysHost.IsVisible || !IsInsideBody(p)) return -1;

        for (var i = 0; i < _vm.TimelineDays.Count; i++)
        {
            if (DaysHost.ItemContainerGenerator.ContainerFromIndex(i) is not FrameworkElement el) continue;
            if (!el.IsVisible) continue;

            var topLeft = el.TranslatePoint(new Point(0, 0), this);
            if (new Rect(topLeft, el.RenderSize).Contains(p)) return i;
        }

        return -1;
    }

    private bool IsNearRedLine(Point p)
    {
        if (!IsInsideBody(p)) return false;
        var origin = BodyScroll.TranslatePoint(new Point(0, 0), this);
        var contentY = p.Y - origin.Y + BodyScroll.VerticalOffset;
        return Math.Abs(contentY - _vm.NowLineTop) <= RedLineSnapPx;
    }

    private int SnapMinutesToGridOrRedLine(Point p, bool floor)
        => IsNearRedLine(p)
            ? Math.Clamp(_vm.CurrentMinutes, 0, 24 * 60)
            : MinutesAt(p, floor);

    /// <summary>把窗口坐标换算成「第几分钟」，按当前缩放间隔取整。</summary>
    private int MinutesAt(Point p, bool floor)
    {
        var origin = BodyScroll.TranslatePoint(new Point(0, 0), this);
        var contentY = p.Y - origin.Y + BodyScroll.VerticalOffset;
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
        var (start, end) = CreateRange(p);
        var origin = BodyScroll.TranslatePoint(new Point(0, 0), this);

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

        Canvas.SetLeft(_createGhost, origin.X + _createDay * MainViewModel.DayWidth + 5 - BodyScroll.HorizontalOffset);
        Canvas.SetTop(_createGhost, origin.Y + start / 60.0 * _vm.PxPerHourValue - BodyScroll.VerticalOffset);
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

        // 仅在红线附近单击时直接从当前时刻起新建（默认 30 分钟）；其余空白单击不误触
        if (!hadGhost)
        {
            if (!snappedNow) return;
            var nowStart = Math.Clamp(_vm.CurrentMinutes, 0, 24 * 60 - 5);
            var nowEnd = Math.Min(24 * 60, nowStart + 30);
            OpenNewEditor(_vm.TimelineDays[day].Date, nowStart, nowEnd);
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
            EditorTitle.Focus();
            EditorTitle.SelectAll();
        }), DispatcherPriority.Input);

    private void EditorSave_Click(object sender, RoutedEventArgs e) => _vm.SaveEditor();

    private void EditorCancel_Click(object sender, RoutedEventArgs e) => _vm.CloseEditor();

    private void EditorDelete_Click(object sender, RoutedEventArgs e) => _vm.DeleteEdited();

    private void EditorPrevDay_Click(object sender, RoutedEventArgs e) => _vm.Editor.StepDay(-1);

    private void EditorNextDay_Click(object sender, RoutedEventArgs e) => _vm.Editor.StepDay(1);

    private void EditorRepeat_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string repeat }) _vm.Editor.Repeat = repeat;
    }

    private void EditorGoal_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is GoalPickVM pick) _vm.Editor.SelectGoal(pick);
    }

    private void EditorNewTag_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || sender is not TextBox box) return;

        _vm.AddEditorTag(box.Text);
        _vm.Editor.NewTagText = "";
        box.Clear();
        e.Handled = true;
    }

    /// <summary>弹窗里回车 = 保存；备注里回车还是换行。</summary>
    private void Editor_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        if (Keyboard.FocusedElement is TextBox { AcceptsReturn: true }) return;

        _vm.SaveEditor();
        e.Handled = true;
    }

    private static string Clock(int minutes) => $"{minutes / 60:00}:{minutes % 60:00}";

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

    private void UpdateBlockDrag(Point p)
    {
        if (_blockGhost is null || _pressedBlock is null) return;

        var origin = BodyScroll.TranslatePoint(new Point(0, 0), this);
        var contentX = p.X - origin.X + BodyScroll.HorizontalOffset;
        var contentY = p.Y - origin.Y + BodyScroll.VerticalOffset;

        var day = (int)Math.Floor(contentX / MainViewModel.DayWidth);
        day = Math.Clamp(day, 0, Math.Max(0, _vm.TimelineDays.Count - 1));

        var interval = Math.Max(1, _vm.IntervalMinutes);
        var minutes = IsNearRedLine(p)
            ? _vm.CurrentMinutes
            : (int)Math.Round(contentY / _vm.PxPerHourValue * 60.0 / interval) * interval;

        var duration = MainViewModel.DurationMinutes(_pressedBlock.Task);
        minutes = Math.Clamp(minutes, 0, Math.Max(0, 24 * 60 - duration));

        // 幽灵直接吸到最近的空档：看到的就是松手后落的位置，不会先重叠再跳
        if (day < _vm.TimelineDays.Count)
            minutes = _vm.FindFreeStart(_vm.TimelineDays[day].Date, minutes, duration, _pressedBlock.Task);

        var left = origin.X + day * MainViewModel.DayWidth + 5 - BodyScroll.HorizontalOffset;
        var top = origin.Y + minutes / 60.0 * _vm.PxPerHourValue - BodyScroll.VerticalOffset;

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
        var origin = BodyScroll.TranslatePoint(new Point(0, 0), this);
        var contentY = p.Y - origin.Y + BodyScroll.VerticalOffset;
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
            var origin = BodyScroll.TranslatePoint(new Point(0, 0), this);
            var blockScreenY = origin.Y + edgeY - BodyScroll.VerticalOffset;
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
        if (BodyScroll.ActualWidth <= 0 || BodyScroll.ActualHeight <= 0) return false;

        var topLeft = BodyScroll.TranslatePoint(new Point(0, 0), this);
        return new Rect(topLeft, BodyScroll.RenderSize).Contains(p);
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

        foreach (var goal in _vm.Goals)
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
        if (!QuadrantsHost.IsVisible) return null;

        foreach (var quadrant in _vm.Quadrants)
        {
            if (QuadrantsHost.ItemContainerGenerator.ContainerFromItem(quadrant) is not FrameworkElement el) continue;
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

    private bool IsInsideElement(Point p, FrameworkElement element)
    {
        if (!element.IsVisible || element.ActualWidth <= 0 || element.ActualHeight <= 0) return false;
        var topLeft = element.TranslatePoint(new Point(0, 0), this);
        return new Rect(topLeft, element.RenderSize).Contains(p);
    }

    private void CancelActiveInteractions()
    {
        CancelPendingExpand();
        StopZoomAnimation();
        _spacePanKeyDown = false;
        if (_panning) EndPan();
        else UpdatePanCursor();
        if (_resizingBlock) EndBlockResize(restoreVisual: true);
        _resizeCandidateBlock = null;
        if (_dragging) CancelDrag();
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

    private static void AnimateModalVisibility(
        Grid layer,
        Border scrim,
        Border dialog,
        ScaleTransform scale,
        TranslateTransform shift,
        bool open,
        Func<bool> isStillOpen)
    {
        if (open)
        {
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
                if (!isStillOpen()) layer.Visibility = Visibility.Collapsed;
            });
        }
    }

    private void ShowToast(string message, bool highlight)
    {
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

            if (_vm.IsOverview)
                target = FindDescendant<Border>(LanesHost, b => Equals(b.Tag, "card") && ReferenceEquals(b.DataContext, task));
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

    private static T? FindDescendant<T>(DependencyObject root, Func<T, bool>? match = null) where T : DependencyObject
    {
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
