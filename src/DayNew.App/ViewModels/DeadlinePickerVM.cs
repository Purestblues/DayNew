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
    private bool _hasTime;
    private string _timeText = "18:00";
    private string _manualText = "";
    private string _errorText = "";
    private Action<DateTime?, TimeSpan?>? _onApply;

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

    public bool HasTime
    {
        get => _hasTime;
        set
        {
            if (!Set(ref _hasTime, value)) return;
            ErrorText = "";
            SyncManualTextFromSelection();
        }
    }

    public string TimeText
    {
        get => _timeText;
        set
        {
            if (!Set(ref _timeText, value)) return;
            ErrorText = "";
            SyncManualTextFromSelection();
        }
    }

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

    public void Open(string title, DateTime? current, TimeSpan? currentTime, Action<DateTime?, TimeSpan?> onApply)
    {
        TargetTitle = title;
        CurrentDate = current?.Date;
        _onApply = onApply;
        var anchor = CurrentDate ?? DateTime.Today;
        _viewMonth = new DateTime(anchor.Year, anchor.Month, 1);
        _hasTime = CurrentDate.HasValue && currentTime.HasValue;
        _timeText = currentTime is { } t ? DeadlineRules.FormatClock(t) : "18:00";
        ManualText = DeadlineRules.FormatDue(CurrentDate, _hasTime ? currentTime : null) ?? "";
        ErrorText = "";
        RebuildDays();
        OnPropertyChanged(nameof(HasTime));
        OnPropertyChanged(nameof(TimeText));
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
        if (date is null)
        {
            var clearCallback = _onApply;
            Close();
            clearCallback?.Invoke(null, null);
            return;
        }

        var normalized = date.Value.Date;
        if (!HasTime)
        {
            var callback = _onApply;
            Close();
            callback?.Invoke(normalized, null);
            return;
        }

        CurrentDate = normalized;
        _viewMonth = new DateTime(normalized.Year, normalized.Month, 1);
        OnPropertyChanged(nameof(MonthTitle));
        SyncManualTextFromSelection();
        RebuildDays();
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
            var clearCallback = _onApply;
            Close();
            clearCallback?.Invoke(null, null);
            return true;
        }

        var (parsedDate, parsedTime) = DeadlineRules.ParseDue(raw);
        if (parsedDate is not { } d)
        {
            ErrorText = "请按 yyyy-MM-dd 或 yyyy-MM-dd HH:mm 格式输入";
            return false;
        }

        TimeSpan? finalTime = null;
        if (parsedTime is { } pt)
        {
            finalTime = pt;
        }
        else if (HasTime)
        {
            if (!DeadlineRules.TryParseClock(TimeText, out var t))
            {
                ErrorText = "时间点格式无效，请写成 18:00 或 9:30";
                return false;
            }
            finalTime = t;
        }

        var callback = _onApply;
        Close();
        callback?.Invoke(d, finalTime);
        return true;
    }

    private void SyncManualTextFromSelection()
    {
        if (CurrentDate is not { } date) return;
        if (!HasTime)
        {
            ManualText = DeadlineRules.FormatDue(date, null) ?? "";
            return;
        }

        ManualText = DeadlineRules.TryParseClock(TimeText, out var t)
            ? (DeadlineRules.FormatDue(date, t) ?? "")
            : date.ToString("yyyy-MM-dd");
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
