using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace DayNew.Views;

/// <summary>
/// MCP 删除类工具的独立轻量置顶即时确认窗：
/// 即便主窗口缩小在系统托盘内，也能立即弹出提醒用户核对待删对象与影响范围。
/// </summary>
public partial class McpDeleteConfirmWindow : Window
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private int _remainingSeconds;

    public bool Approved { get; private set; }

    public McpDeleteConfirmWindow(
        string targetKind,
        string targetTitle,
        string impactDetail,
        int timeoutSeconds = 60)
    {
        InitializeComponent();

        _remainingSeconds = Math.Max(5, timeoutSeconds);
        HeaderTitleText.Text = $"AI 请求删除{targetKind}";
        TargetTitleText.Text = string.IsNullOrWhiteSpace(targetTitle) ? "（无名称）" : targetTitle;
        ImpactDetailText.Text = impactDetail;
        CountdownText.Text = $"{_remainingSeconds}s";

        Loaded += (_, _) =>
        {
            CenterOnWorkArea();
            Activate();
        };

        Closed += (_, _) => _timer.Stop();

        _timer.Tick += (_, _) =>
        {
            _remainingSeconds--;
            if (_remainingSeconds <= 0)
            {
                Approved = false;
                _timer.Stop();
                Close();
                return;
            }

            CountdownText.Text = $"{_remainingSeconds}s";
        };
        _timer.Start();
    }

    private void CenterOnWorkArea()
    {
        var area = SystemParameters.WorkArea;
        if (area.Width <= 0 || area.Height <= 0) return;

        Left = Math.Max(area.Left, area.Left + (area.Width - ActualWidth) / 2);
        Top = Math.Max(area.Top, area.Top + (area.Height - ActualHeight) / 2);
    }

    private void Approve_Click(object sender, RoutedEventArgs e)
    {
        Approved = true;
        Close();
    }

    private void Deny_Click(object sender, RoutedEventArgs e)
    {
        Approved = false;
        Close();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        Approved = false;
        e.Handled = true;
        Close();
    }
}
