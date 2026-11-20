using System.Collections.ObjectModel;
using System.Windows.Media;
using DayNew.Core;

namespace DayNew.ViewModels;

/// <summary>远景目标：任务的容器。任务最多属于一个目标，与标签互不干扰。</summary>
public sealed class GoalVM : ObservableObject
{
    /// <summary>稳定标识，日程靠它认目标。</summary>
    public string Id
    {
        get => _id;
        set { if (!string.IsNullOrWhiteSpace(value)) _id = value; }
    }

    private string _id = Guid.NewGuid().ToString("N")[..10];

    private string _title = "";
    private bool _isExpanded;
    private bool _isDropTarget;

    public string Title
    {
        get => _title;
        set
        {
            if (!Set(ref _title, value)) return;
            Refresh();
        }
    }

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

    /// <summary>由 MainViewModel.Rebuild 填充，不手工维护。</summary>
    public ObservableCollection<TaskVM> Tasks { get; } = new();

    public int Total => Tasks.Count;

    public int DoneCount => Tasks.Count(t => t.Done);

    public string ProgressText => Total == 0 ? "还没有任务" : $"{DoneCount}/{Total} 完成";

    public double Progress => Total == 0 ? 0 : (double)DoneCount / Total;

    public void Refresh()
    {
        OnPropertyChanged(nameof(Total));
        OnPropertyChanged(nameof(DoneCount));
        OnPropertyChanged(nameof(ProgressText));
        OnPropertyChanged(nameof(Progress));
    }

    public override string ToString() => string.IsNullOrWhiteSpace(Title) ? "未命名目标" : Title;
}
