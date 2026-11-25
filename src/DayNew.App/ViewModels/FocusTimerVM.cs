using System.Windows.Threading;
using DayNew.Core;
using DayNew.Services;

namespace DayNew.ViewModels;

public enum FocusMode
{
    None,
    AutoSchedule,
    ManualCountdown,
    ManualStopwatch,
    FreeStopwatch
}

/// <summary>
/// 专注计时状态机（纯内存态，不落盘历史）：
/// 1. 空闲时自动跟随当前时间所在的今日时段日程（StartTime <= now < EndTime）；
/// 2. 支持任意日程手动倒计时 / 正计时，以及无绑定的自由专注正计时；
/// 3. 自由专注结束时抛出事件，由主窗口拉起日程编辑窗并在保存后标为已完成。
/// </summary>
public sealed class FocusTimerVM : ObservableObject, IDisposable
{
    private readonly Func<IReadOnlyList<TaskVM>> _getSchedules;
    private readonly Func<bool> _isSoundEnabled;
    private readonly Action<TaskVM> _onCompleteTask;
    private readonly Action<string, bool> _onNotify;
    private readonly DispatcherTimer _timer;

    private FocusMode _mode = FocusMode.None;
    private TaskVM? _currentTask;
    private bool _isPaused;
    private bool _isOvertime;
    private string _freeDraftTitle = "";
    private DateTime _sessionStartedAt;
    private DateTime _lastTickAt;
    private TimeSpan _plannedDuration;
    private TimeSpan _remaining;
    private TimeSpan _elapsed;
    private TimeSpan _overtime;
    private bool _overtimeNotified;
    private (string TaskId, DateTime Date, TimeSpan Start, TimeSpan End)? _dismissedAutoKey;
    private bool _disposed;

    public FocusTimerVM(
        Func<IReadOnlyList<TaskVM>> getSchedules,
        Func<bool> isSoundEnabled,
        Action<TaskVM> onCompleteTask,
        Action<string, bool> onNotify)
    {
        _getSchedules = getSchedules;
        _isSoundEnabled = isSoundEnabled;
        _onCompleteTask = onCompleteTask;
        _onNotify = onNotify;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += OnTimerTick;
        _timer.Start();

        TryAutoFollow(DateTime.Now, playSound: false);
    }

    public event Action? SessionStarted;
    public event Action? SessionEnded;
    public event Action<string, string?>? CountdownFinished;
    public event Action<string, DateTime, int, int>? FreeFocusCompleted;
    public event Action? Ticked;

    public FocusMode Mode => _mode;

    public bool IsActive => _mode != FocusMode.None;

    public bool IsIdle => _mode == FocusMode.None;

    public bool IsPaused => _isPaused;

    public bool IsOvertime => _isOvertime;

    public bool IsAutoFollow => _mode == FocusMode.AutoSchedule;

    public bool IsFreeMode => _mode == FocusMode.FreeStopwatch;

    public bool IsCountdown => _mode is FocusMode.AutoSchedule or FocusMode.ManualCountdown;

    public bool IsStopwatch => _mode is FocusMode.ManualStopwatch or FocusMode.FreeStopwatch;

    public TaskVM? CurrentTask => _currentTask;

    public string FreeDraftTitle
    {
        get => _freeDraftTitle;
        set
        {
            var next = value ?? "";
            if (!Set(ref _freeDraftTitle, next)) return;
            if (_mode == FocusMode.FreeStopwatch)
            {
                OnPropertyChanged(nameof(DisplayTitle));
                OnPropertyChanged(nameof(TrayStatusText));
            }
        }
    }

    public string DisplayTitle
    {
        get
        {
            if (_mode == FocusMode.None) return "当前无进行中专注";
            if (_mode == FocusMode.FreeStopwatch)
                return string.IsNullOrWhiteSpace(_freeDraftTitle) ? "自由专注" : _freeDraftTitle.Trim();
            if (_currentTask is not null)
                return string.IsNullOrWhiteSpace(_currentTask.Title) ? "未命名日程" : _currentTask.Title.Trim();
            return "专注中";
        }
    }

    public string ModeLabel => _mode switch
    {
        FocusMode.AutoSchedule => _isOvertime ? "日程已到时" : "自动跟随日程",
        FocusMode.ManualCountdown => _isOvertime ? "倒计时已结束" : "倒计时专注",
        FocusMode.ManualStopwatch => "正计时专注",
        FocusMode.FreeStopwatch => "自由专注 · 正计时",
        _ => "空闲"
    };

    public string TimeText
    {
        get
        {
            if (_mode == FocusMode.None) return "00:00";
            if (IsCountdown)
                return _isOvertime ? "+" + FormatDuration(_overtime) : FormatDuration(_remaining);
            return FormatDuration(_elapsed);
        }
    }

    public string SubStatusText
    {
        get
        {
            if (_mode == FocusMode.None) return "时段日程到点自动计时，或随时开启自由专注";
            if (_isPaused) return "已暂停";
            if (_isOvertime) return $"已超时 {FormatDuration(_overtime)}";
            if (_mode == FocusMode.AutoSchedule && _currentTask is { StartTime: { } s, EndTime: { } e })
                return $"{FmtClock(s)} – {FmtClock(e)}";
            if (_mode == FocusMode.ManualCountdown && _currentTask is not null)
                return $"目标 {Math.Max(1, (int)Math.Round(_plannedDuration.TotalMinutes))} 分钟";
            return $"开始于 {_sessionStartedAt:HH:mm}";
        }
    }

    public double Progress
    {
        get
        {
            if (!IsCountdown || _plannedDuration.TotalSeconds <= 0) return 0;
            if (_isOvertime) return 1;
            var done = 1.0 - (_remaining.TotalSeconds / _plannedDuration.TotalSeconds);
            return Math.Clamp(done, 0, 1);
        }
    }

    public bool ShowProgress => IsCountdown;

    public bool CanExtend => IsCountdown;

    public string PauseButtonText => _isPaused ? "继续" : "暂停";

    public string PauseButtonIcon => _isPaused ? "\uE768" : "\uE769";

    public string CompleteButtonText => IsFreeMode ? "结束并存为日程" : "标为完成";

    public string StopButtonText => IsAutoFollow ? "跳过" : "结束";

    public string StatusKind => !IsActive
        ? "idle"
        : _isOvertime
            ? "overtime"
            : _isPaused
                ? "paused"
                : "active";

    public string? TrayStatusText
    {
        get
        {
            if (!IsActive) return null;
            var title = DisplayTitle;
            if (title.Length > 18) title = title[..18] + "…";
            var prefix = _isPaused ? "专注已暂停" : _isOvertime ? "专注超时" : "专注中";
            return $"{prefix} · {title} · {TimeText}";
        }
    }

    public void StartTaskFocus(TaskVM task, int? customMinutes = null, bool stopwatch = false)
    {
        if (_disposed) return;

        var now = DateTime.Now;
        _currentTask = task;
        _isPaused = false;
        _isOvertime = false;
        _overtimeNotified = false;
        _overtime = TimeSpan.Zero;
        _elapsed = TimeSpan.Zero;
        _sessionStartedAt = now;
        _lastTickAt = now;
        _dismissedAutoKey = null;

        if (stopwatch)
        {
            _mode = FocusMode.ManualStopwatch;
            _plannedDuration = TimeSpan.Zero;
            _remaining = TimeSpan.Zero;
        }
        else
        {
            _mode = FocusMode.ManualCountdown;
            var duration = ResolveCountdownDuration(task, customMinutes, now);
            _plannedDuration = duration;
            _remaining = duration;
        }

        if (_isSoundEnabled()) FocusSoundService.PlayStart();
        RaiseAll();
        SessionStarted?.Invoke();
    }

    public void StartFreeFocus(string? title = null)
    {
        if (_disposed) return;

        var now = DateTime.Now;
        _mode = FocusMode.FreeStopwatch;
        _currentTask = null;
        if (title is not null) _freeDraftTitle = title.Trim();
        _isPaused = false;
        _isOvertime = false;
        _overtimeNotified = false;
        _overtime = TimeSpan.Zero;
        _elapsed = TimeSpan.Zero;
        _plannedDuration = TimeSpan.Zero;
        _remaining = TimeSpan.Zero;
        _sessionStartedAt = now;
        _lastTickAt = now;

        if (_isSoundEnabled()) FocusSoundService.PlayStart();
        RaiseAll();
        SessionStarted?.Invoke();
    }

    public void TogglePause()
    {
        if (!IsActive) return;

        _isPaused = !_isPaused;
        _lastTickAt = DateTime.Now;
        RaiseAll();
        Ticked?.Invoke();
    }

    public void ExtendMinutes(int minutes = 5)
    {
        if (!IsActive || !IsCountdown || minutes <= 0) return;

        var delta = TimeSpan.FromMinutes(minutes);
        if (_isOvertime)
        {
            _isOvertime = false;
            _overtime = TimeSpan.Zero;
            _overtimeNotified = false;
            _remaining = delta;
            _plannedDuration += delta;
        }
        else
        {
            _remaining += delta;
            _plannedDuration += delta;
        }

        _isPaused = false;
        _lastTickAt = DateTime.Now;
        RaiseAll();
        Ticked?.Invoke();
        _onNotify($"专注已延长 {minutes} 分钟", false);
    }

    public void CompleteCurrent()
    {
        if (!IsActive) return;

        if (_mode == FocusMode.FreeStopwatch)
        {
            var endAt = DateTime.Now;
            var title = string.IsNullOrWhiteSpace(_freeDraftTitle) ? "" : _freeDraftTitle.Trim();
            var date = _sessionStartedAt.Date;
            var startMinutes = Math.Clamp((int)_sessionStartedAt.TimeOfDay.TotalMinutes, 0, 24 * 60 - 2);
            var endMinutes = endAt.Date == date
                ? (int)Math.Ceiling(endAt.TimeOfDay.TotalMinutes)
                : 24 * 60 - 1;
            endMinutes = Math.Clamp(Math.Max(startMinutes + 1, endMinutes), startMinutes + 1, 24 * 60 - 1);

            if (_isSoundEnabled()) FocusSoundService.PlayComplete();

            _freeDraftTitle = "";
            ClearSessionState(checkAutoFollow: false);
            FreeFocusCompleted?.Invoke(title, date, startMinutes, endMinutes);
            return;
        }

        var task = _currentTask;
        if (_isSoundEnabled()) FocusSoundService.PlayComplete();

        ClearSessionState(checkAutoFollow: false);
        if (task is not null && !task.Done)
            _onCompleteTask(task);

        TryAutoFollow(DateTime.Now, playSound: false);
    }

    public void StopOrSkip()
    {
        if (!IsActive) return;

        if (_mode == FocusMode.AutoSchedule && _currentTask is { Due: { } due, StartTime: { } s, EndTime: { } e })
        {
            _dismissedAutoKey = (_currentTask.Id, due.Date, s, e);
            ClearSessionState(checkAutoFollow: false);
            _onNotify($"已跳过「{(_currentTask.Title.Length > 0 ? _currentTask.Title : "该日程")}」本次自动专注", false);
            return;
        }

        ClearSessionState(checkAutoFollow: true);
    }

    public void SyncWithSchedules()
    {
        if (_disposed) return;

        var now = DateTime.Now;
        if (_currentTask is not null)
        {
            var stillExists = _getSchedules().Contains(_currentTask);
            if (!stillExists || _currentTask.Done)
            {
                ClearSessionState(checkAutoFollow: true);
                return;
            }

            if (_mode == FocusMode.AutoSchedule)
            {
                if (_currentTask.Due?.Date != now.Date
                    || _currentTask.StartTime is not { } start
                    || _currentTask.EndTime is not { } end
                    || now.TimeOfDay < start)
                {
                    ClearSessionState(checkAutoFollow: true);
                    return;
                }

                _plannedDuration = end > start ? end - start : TimeSpan.FromMinutes(1);
                var left = end - now.TimeOfDay;
                if (left > TimeSpan.Zero)
                {
                    _isOvertime = false;
                    _overtime = TimeSpan.Zero;
                    _overtimeNotified = false;
                    _remaining = left;
                }
                else
                {
                    _isOvertime = true;
                    _remaining = TimeSpan.Zero;
                    _overtime = -left;
                }
                RaiseAll();
                Ticked?.Invoke();
                return;
            }
        }

        if (_mode == FocusMode.None)
            TryAutoFollow(now, playSound: false);
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        if (_disposed) return;

        var now = DateTime.Now;
        if (_mode == FocusMode.None)
        {
            TryAutoFollow(now, playSound: true);
            return;
        }

        if (_isPaused)
        {
            _lastTickAt = now;
            return;
        }

        var dt = now - _lastTickAt;
        if (dt <= TimeSpan.Zero) dt = TimeSpan.FromSeconds(1);
        if (dt > TimeSpan.FromSeconds(30)) dt = TimeSpan.FromSeconds(1);
        _lastTickAt = now;

        if (_mode == FocusMode.AutoSchedule && _currentTask is { StartTime: { } s, EndTime: { } end, Due: { } due } && due.Date == now.Date)
        {
            var left = end - now.TimeOfDay;
            if (left > TimeSpan.Zero)
            {
                _remaining = left;
            }
            else
            {
                _remaining = TimeSpan.Zero;
                if (!_isOvertime)
                {
                    _isOvertime = true;
                    _overtime = -left;
                    TriggerCountdownFinished();
                }
                else
                {
                    _overtime += dt;
                }
            }
        }
        else if (IsCountdown)
        {
            if (_isOvertime)
            {
                _overtime += dt;
            }
            else
            {
                _remaining -= dt;
                if (_remaining <= TimeSpan.Zero)
                {
                    _overtime = -_remaining;
                    _remaining = TimeSpan.Zero;
                    _isOvertime = true;
                    TriggerCountdownFinished();
                }
            }
        }
        else
        {
            _elapsed += dt;
        }

        RaiseTimeProperties();
        Ticked?.Invoke();
    }

    private void TriggerCountdownFinished()
    {
        if (_overtimeNotified) return;
        _overtimeNotified = true;

        if (_isSoundEnabled()) FocusSoundService.PlayComplete();
        CountdownFinished?.Invoke(DisplayTitle, _currentTask?.Id);
    }

    private void TryAutoFollow(DateTime now, bool playSound)
    {
        var today = now.Date;
        var tod = now.TimeOfDay;

        TaskVM? activeTask = null;
        foreach (var t in _getSchedules())
        {
            if (t.Done || t.Due?.Date != today) continue;
            if (t.StartTime is not { } start || t.EndTime is not { } end || end <= start) continue;
            if (tod < start || tod >= end) continue;

            if (_dismissedAutoKey is { } d
                && d.TaskId == t.Id
                && d.Date == today
                && d.Start == start
                && d.End == end)
                continue;

            activeTask = t;
            break;
        }

        if (activeTask is null) return;

        var s = activeTask.StartTime!.Value;
        var e = activeTask.EndTime!.Value;

        _mode = FocusMode.AutoSchedule;
        _currentTask = activeTask;
        _isPaused = false;
        _isOvertime = false;
        _overtimeNotified = false;
        _overtime = TimeSpan.Zero;
        _elapsed = TimeSpan.Zero;
        _plannedDuration = e - s;
        _remaining = e - tod;
        _sessionStartedAt = today + s;
        _lastTickAt = now;

        if (playSound && _isSoundEnabled()) FocusSoundService.PlayStart();
        RaiseAll();
        SessionStarted?.Invoke();
    }

    private void ClearSessionState(bool checkAutoFollow)
    {
        _mode = FocusMode.None;
        _currentTask = null;
        _isPaused = false;
        _isOvertime = false;
        _overtimeNotified = false;
        _overtime = TimeSpan.Zero;
        _elapsed = TimeSpan.Zero;
        _plannedDuration = TimeSpan.Zero;
        _remaining = TimeSpan.Zero;

        RaiseAll();
        SessionEnded?.Invoke();

        if (checkAutoFollow)
            TryAutoFollow(DateTime.Now, playSound: false);
    }

    private static TimeSpan ResolveCountdownDuration(TaskVM task, int? customMinutes, DateTime now)
    {
        if (customMinutes is > 0)
            return TimeSpan.FromMinutes(customMinutes.Value);

        if (task.StartTime is { } start && task.EndTime is { } end && end > start)
        {
            if (task.Due?.Date == now.Date && now.TimeOfDay >= start && now.TimeOfDay < end)
                return end - now.TimeOfDay;
            return end - start;
        }

        return TimeSpan.FromMinutes(25);
    }

    private void RaiseAll()
    {
        OnPropertyChanged(nameof(Mode));
        OnPropertyChanged(nameof(IsActive));
        OnPropertyChanged(nameof(IsIdle));
        OnPropertyChanged(nameof(IsPaused));
        OnPropertyChanged(nameof(IsOvertime));
        OnPropertyChanged(nameof(IsAutoFollow));
        OnPropertyChanged(nameof(IsFreeMode));
        OnPropertyChanged(nameof(IsCountdown));
        OnPropertyChanged(nameof(IsStopwatch));
        OnPropertyChanged(nameof(CurrentTask));
        OnPropertyChanged(nameof(FreeDraftTitle));
        OnPropertyChanged(nameof(DisplayTitle));
        OnPropertyChanged(nameof(ModeLabel));
        OnPropertyChanged(nameof(ShowProgress));
        OnPropertyChanged(nameof(CanExtend));
        OnPropertyChanged(nameof(PauseButtonText));
        OnPropertyChanged(nameof(PauseButtonIcon));
        OnPropertyChanged(nameof(CompleteButtonText));
        OnPropertyChanged(nameof(StopButtonText));
        OnPropertyChanged(nameof(StatusKind));
        RaiseTimeProperties();
    }

    private void RaiseTimeProperties()
    {
        OnPropertyChanged(nameof(TimeText));
        OnPropertyChanged(nameof(SubStatusText));
        OnPropertyChanged(nameof(Progress));
        OnPropertyChanged(nameof(TrayStatusText));
    }

    private static string FormatDuration(TimeSpan span)
    {
        if (span < TimeSpan.Zero) span = TimeSpan.Zero;
        var totalHours = (int)span.TotalHours;
        return totalHours > 0
            ? $"{totalHours:00}:{span.Minutes:00}:{span.Seconds:00}"
            : $"{span.Minutes:00}:{span.Seconds:00}";
    }

    private static string FmtClock(TimeSpan t) => $"{(int)t.TotalHours:00}:{t.Minutes:00}";

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer.Stop();
        _timer.Tick -= OnTimerTick;
    }
}
