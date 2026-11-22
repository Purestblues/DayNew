using System.Collections.ObjectModel;
using DayNew.Core;

namespace DayNew.ViewModels;

public sealed class CalendarDayVM
{
    public DateTime Date { get; init; }
    public string DayText => Date.Day.ToString();
    public bool IsCurrentMonth { get; init; }
    public bool IsToday { get; init; }
    public bool IsSelected { get; init; }
}

/// <summary>符合 nord-frost-ui 风格的可选截止日期选择器状态（供目标节点与目标卡片共用）。</summary>
public sealed class DeadlinePickerVM : ObservableObject
{
    private bool _isOpen;
    private string _targetTitle = "";
    private DateTime? _currentDate;
    private DateTime _viewMonth = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    private string _manualText = "";
    private string _errorText = "";
    private Action<DateTime?>? _onApply;

    public bool IsOpen
    {
        get => _isOpen;
        private set => Set(ref _isOpen, value);
    }

    public string TargetTitle
    {
        get => _targetTitle;
        private set => Set(ref _targetTitle, value);
    }

    public DateTime? CurrentDate
    {
        get => _currentDate;
        private set
        {
            if (!Set(ref _currentDate, value)) return;
            OnPropertyChanged(nameof(HasCurrentDate));
        }
    }

    public bool HasCurrentDate => CurrentDate.HasValue;

    public string MonthTitle => $"{_viewMonth:yyyy年M月}";

    public string ManualText
    {
        get => _manualText;
        set
        {
            if (Set(ref _manualText, value))
                ErrorText = "";
        }
    }

    public string ErrorText
    {
        get => _errorText;
        private set => Set(ref _errorText, value);
    }

    public ObservableCollection<CalendarDayVM> Days { get; } = new();

    public void Open(string title, DateTime? current, Action<DateTime?> onApply)
    {
        TargetTitle = title;
        CurrentDate = current?.Date;
        _onApply = onApply;
        var anchor = CurrentDate ?? DateTime.Today;
        _viewMonth = new DateTime(anchor.Year, anchor.Month, 1);
        ManualText = CurrentDate?.ToString("yyyy-MM-dd") ?? "";
        ErrorText = "";
        RebuildDays();
        OnPropertyChanged(nameof(MonthTitle));
        IsOpen = true;
    }

    public void Close()
    {
        IsOpen = false;
        _onApply = null;
        ErrorText = "";
    }

    public void StepMonth(int delta)
    {
        _viewMonth = _viewMonth.AddMonths(delta);
        OnPropertyChanged(nameof(MonthTitle));
        RebuildDays();
    }

    public void SelectDate(DateTime? date)
    {
        var normalized = date?.Date;
        var callback = _onApply;
        Close();
        callback?.Invoke(normalized);
    }

    public void PickToday() => SelectDate(DateTime.Today);

    public void PickTomorrow() => SelectDate(DateTime.Today.AddDays(1));

    public void PickEndOfWeek()
    {
        var today = DateTime.Today;
        var offset = (7 - (int)today.DayOfWeek) % 7;
        SelectDate(today.AddDays(offset == 0 ? 7 : offset));
    }

    public void PickNextWeek() => SelectDate(DateTime.Today.AddDays(7));

    public void ClearDate() => SelectDate(null);

    public bool TryApplyManualText()
    {
        var raw = (ManualText ?? "").Trim();
        if (raw.Length == 0)
        {
            SelectDate(null);
            return true;
        }

        if (DateTime.TryParse(raw, out var parsed))
        {
            SelectDate(parsed.Date);
            return true;
        }

        ErrorText = "请按 yyyy-MM-dd 格式输入，如 2026-12-01";
        return false;
    }

    private void RebuildDays()
    {
        Days.Clear();
        var today = DateTime.Today;
        var first = _viewMonth;
        // 周一作为一周第一天
        var lead = ((int)first.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
        var start = first.AddDays(-lead);

        for (var i = 0; i < 42; i++)
        {
            var d = start.AddDays(i);
            Days.Add(new CalendarDayVM
            {
                Date = d,
                IsCurrentMonth = d.Month == _viewMonth.Month && d.Year == _viewMonth.Year,
                IsToday = d == today,
                IsSelected = CurrentDate.HasValue && d == CurrentDate.Value
            });
        }
    }
}
