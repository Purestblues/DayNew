using System.ComponentModel;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using DayNew.ViewModels;

namespace DayNew.Views;

public partial class FocusMiniWindow : Window
{
    private readonly FocusTimerVM _focus;
    private readonly Action _onOpenMain;
    private readonly Action _onHide;
    private bool _positioned;

    public FocusMiniWindow(FocusTimerVM focus, Action onOpenMain, Action onHide)
    {
        _focus = focus;
        _onOpenMain = onOpenMain;
        _onHide = onHide;

        InitializeComponent();
        DataContext = _focus;
        _focus.PropertyChanged += OnFocusPropertyChanged;
        Loaded += (_, _) =>
        {
            PositionNearWorkAreaBottomRight();
            UpdateStatusBrushes();
        };
        Closed += (_, _) => _focus.PropertyChanged -= OnFocusPropertyChanged;
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

        var w = ActualWidth > 0 ? ActualWidth : 310;
        var h = ActualHeight > 0 ? ActualHeight : 88;
        Left = Math.Max(area.Left, area.Right - w - 18);
        Top = Math.Max(area.Top, area.Bottom - h - 18);
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
            statusDot.Fill = b;
            progressFill.Background = _focus.IsOvertime && TryFindResource("Danger") is Brush db ? db : b;
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

        try
        {
            DragMove();
        }
        catch
        {
            // 鼠标瞬间释放时 DragMove 可能抛 InvalidOperationException
        }
    }

    private void Pause_Click(object sender, RoutedEventArgs e) => _focus.TogglePause();

    private void Extend_Click(object sender, RoutedEventArgs e) => _focus.ExtendMinutes(5);

    private void Complete_Click(object sender, RoutedEventArgs e) => _focus.CompleteCurrent();

    private void StartFree_Click(object sender, RoutedEventArgs e) => _focus.StartFreeFocus();

    private void OpenMain_Click(object sender, RoutedEventArgs e) => _onOpenMain();

    private void Hide_Click(object sender, RoutedEventArgs e) => _onHide();
}
