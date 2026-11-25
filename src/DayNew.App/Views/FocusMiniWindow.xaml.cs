using System.ComponentModel;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using DayNew.Core;
using DayNew.ViewModels;

namespace DayNew.Views;

public partial class FocusMiniWindow : Window
{
    private readonly FocusTimerVM _focus;
    private readonly Action _onOpenMain;
    private readonly Action _onHide;
    private readonly DispatcherTimer _collapseTimer = new() { Interval = TimeSpan.FromMilliseconds(280) };
    private bool _positioned;
    private bool _isExpanded;
    private bool _isDragging;

    public FocusMiniWindow(FocusTimerVM focus, Action onOpenMain, Action onHide)
    {
        _focus = focus;
        _onOpenMain = onOpenMain;
        _onHide = onHide;

        InitializeComponent();
        DataContext = _focus;
        _focus.PropertyChanged += OnFocusPropertyChanged;
        _collapseTimer.Tick += OnCollapseTimerTick;

        MouseEnter += OnWindowMouseEnter;
        MouseLeave += OnWindowMouseLeave;
        Deactivated += OnWindowDeactivated;
        IsVisibleChanged += OnWindowIsVisibleChanged;

        Loaded += (_, _) =>
        {
            PositionNearWorkAreaBottomRight();
            UpdateStatusBrushes();
        };
        Closed += (_, _) =>
        {
            _collapseTimer.Stop();
            _collapseTimer.Tick -= OnCollapseTimerTick;
            _focus.PropertyChanged -= OnFocusPropertyChanged;
        };
    }

    public void EnsureDefaultPosition()
    {
        if (!_positioned)
            PositionNearWorkAreaBottomRight();
    }

    private void PositionNearWorkAreaBottomRight()
    {
        if (_positioned) return;
        _positioned = true;

        UpdateLayout();
        var area = SystemParameters.WorkArea;
        if (area.Width <= 0 || area.Height <= 0) return;

        var w = ActualWidth > 0 ? ActualWidth : 210;
        var h = ActualHeight > 0 ? ActualHeight : 68;
        Left = Math.Max(area.Left, area.Right - w - 2);
        Top = Math.Max(area.Top, area.Bottom - h - 2);
    }

    private void OnWindowMouseEnter(object sender, MouseEventArgs e)
    {
        _collapseTimer.Stop();
        SetExpanded(true);
    }

    private void OnWindowMouseLeave(object sender, MouseEventArgs e)
    {
        if (_isDragging) return;
        _collapseTimer.Stop();
        _collapseTimer.Start();
    }

    private void OnWindowDeactivated(object? sender, EventArgs e)
    {
        if (_isDragging || IsMouseOver) return;
        _collapseTimer.Stop();
        _collapseTimer.Start();
    }

    private void OnWindowIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (!IsVisible)
        {
            _collapseTimer.Stop();
            SetExpanded(false, animate: false);
        }
    }

    private void OnCollapseTimerTick(object? sender, EventArgs e)
    {
        _collapseTimer.Stop();
        if (!IsMouseOver && !_isDragging)
            SetExpanded(false);
    }

    private void SetExpanded(bool expanded, bool animate = true)
    {
        if (_isExpanded == expanded) return;
        _isExpanded = expanded;

        var oldW = ActualWidth > 0 ? ActualWidth : (expanded ? 210 : 320);
        var oldH = ActualHeight > 0 ? ActualHeight : (expanded ? 68 : 118);

        var area = SystemParameters.WorkArea;
        var hasArea = area.Width > 0 && area.Height > 0 && !double.IsNaN(Left) && !double.IsNaN(Top);

        double anchorLeft = Left;
        double anchorRight = Left + oldW;
        double anchorCenterX = Left + oldW * 0.5;
        double anchorTop = Top;
        double anchorBottom = Top + oldH;

        var pinModeX = 0; // -1: left, 0: center, 1: right
        var pinBottom = false;
        if (hasArea)
        {
            var leftDist = Math.Max(0, Left - area.Left);
            var rightDist = Math.Max(0, area.Right - (Left + oldW));
            var topDist = Math.Max(0, Top - area.Top);
            var bottomDist = Math.Max(0, area.Bottom - (Top + oldH));

            if (rightDist <= leftDist && rightDist < 140) pinModeX = 1;
            else if (leftDist < 140) pinModeX = -1;
            else pinModeX = 0;

            pinBottom = bottomDist < 140 && bottomDist <= topDist;
        }

        UIElement showTarget = expanded ? ExpandedView : CompactView;
        UIElement hideTarget = expanded ? CompactView : ExpandedView;

        hideTarget.BeginAnimation(OpacityProperty, null);
        hideTarget.Opacity = 1.0;
        hideTarget.Visibility = Visibility.Collapsed;

        showTarget.BeginAnimation(OpacityProperty, null);
        showTarget.Opacity = animate ? 0.72 : 1.0;
        showTarget.Visibility = Visibility.Visible;

        UpdateLayout();

        if (hasArea)
        {
            var newW = ActualWidth > 0 ? ActualWidth : oldW;
            var newH = ActualHeight > 0 ? ActualHeight : oldH;

            var nextLeft = pinModeX switch
            {
                1 => anchorRight - newW,
                -1 => anchorLeft,
                _ => anchorCenterX - newW * 0.5
            };
            var nextTop = pinBottom ? anchorBottom - newH : anchorTop;

            Left = Math.Clamp(nextLeft, area.Left, Math.Max(area.Left, area.Right - newW));
            Top = Math.Clamp(nextTop, area.Top, Math.Max(area.Top, area.Bottom - newH));
        }

        if (animate)
            Motion.AnimateOpacity(showTarget, 1.0, Motion.S, Motion.EaseOut);
    }

    private void OnFocusPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(FocusTimerVM.StatusKind) or nameof(FocusTimerVM.IsOvertime) or nameof(FocusTimerVM.IsPaused))
            UpdateStatusBrushes();
    }

    private void UpdateStatusBrushes()
    {
        var key = _focus.StatusKind switch
        {
            "overtime" => "Danger",
            "paused" => "Warning",
            "idle" => "TextDisabled",
            _ => "Accent"
        };

        if (TryFindResource(key) is Brush b)
        {
            compactStatusDot.Fill = b;
            expandedStatusDot.Fill = b;
            var fillBrush = _focus.IsOvertime && TryFindResource("Danger") is Brush db ? db : b;
            compactProgressFill.Background = fillBrush;
            expandedProgressFill.Background = fillBrush;
        }
    }

    private void Root_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject d)
        {
            var cur = d;
            while (cur is not null)
            {
                if (cur is ButtonBase or TextBoxBase) return;
                cur = VisualTreeHelper.GetParent(cur);
            }
        }

        var prevLeft = Left;
        var prevTop = Top;
        _isDragging = true;
        _collapseTimer.Stop();
        try
        {
            DragMove();
        }
        catch
        {
            // 鼠标瞬间释放时 DragMove 可能抛 InvalidOperationException
        }
        finally
        {
            _isDragging = false;
        }

        if (!_isExpanded && Math.Abs(Left - prevLeft) < 2 && Math.Abs(Top - prevTop) < 2)
        {
            SetExpanded(true);
        }
        else if (!IsMouseOver)
        {
            _collapseTimer.Stop();
            _collapseTimer.Start();
        }
    }

    private void Pause_Click(object sender, RoutedEventArgs e) => _focus.TogglePause();

    private void Extend_Click(object sender, RoutedEventArgs e) => _focus.ExtendMinutes(5);

    private void Complete_Click(object sender, RoutedEventArgs e) => _focus.CompleteCurrent();

    private void StartFree_Click(object sender, RoutedEventArgs e) => _focus.StartFreeFocus();

    private void OpenMain_Click(object sender, RoutedEventArgs e) => _onOpenMain();

    private void Hide_Click(object sender, RoutedEventArgs e) => _onHide();
}
