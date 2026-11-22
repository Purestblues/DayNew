using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using DayNew.Core;
using DayNew.ViewModels;
using Microsoft.Toolkit.Uwp.Notifications;
using Microsoft.Win32;
using Drawing = System.Drawing;

namespace DayNew.Services;

/// <summary>
/// Windows 系统通知推送服务（基于 ToastNotificationManagerCompat）：
///   1. 日程节点提醒：严格按日程勾选的 Remind 节点（pre15/pre5/start/end）推送，带「标为完成」「延后 5/10 分钟」快捷按钮。
///   2. 贪睡延后队列：仅推迟通知本身，不改动日程在时间轴上的起止时间。
///   3. 今日待办摘要：启动（自启或手动）或从睡眠/休眠唤醒时推送一次，受 DailyDigestEnabled 控制且当日不重复。
/// </summary>
public sealed class NotificationService : IDisposable
{
    private sealed record SnoozeEntry(string TaskId, DateTime FireAt, string Title, string Body);

    private readonly AppSettings _settings;
    private readonly Func<IReadOnlyList<TaskVM>> _getSchedules;
    private readonly Action _onOpenWindow;
    private readonly Action<string> _onCompleteTask;

    private readonly DispatcherTimer _timer;
    private readonly HashSet<string> _firedKeys = new();
    private readonly List<SnoozeEntry> _snoozes = new();
    private DateTime _lastScanDate = DateTime.Today;
    private string? _toastIconPath;
    private bool _digestScheduled;
    private bool _disposed;

    public NotificationService(
        AppSettings settings,
        Func<IReadOnlyList<TaskVM>> getSchedules,
        Action onOpenWindow,
        Action<string> onCompleteTask)
    {
        _settings = settings;
        _getSchedules = getSchedules;
        _onOpenWindow = onOpenWindow;
        _onCompleteTask = onCompleteTask;

        ToastNotificationManagerCompat.OnActivated += OnToastActivated;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        _timer.Tick += (_, _) => CheckScheduledReminders();
        _timer.Start();

        // 启动时检查一次今日摘要（稍微延迟，等桌面 Shell 通知中心就绪）
        ScheduleDigestCheck();
    }

    public void ScheduleDigestCheck()
    {
        if (_disposed || !_settings.NotifyEnabled || !_settings.DailyDigestEnabled) return;
        if (_settings.LastDigestDate == DateTime.Today.ToString("yyyy-MM-dd")) return;
        if (_digestScheduled) return;

        _digestScheduled = true;
        _ = Task.Delay(2500).ContinueWith(_ =>
        {
            Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                _digestScheduled = false;
                TryPushDailyDigest();
            });
        });
    }

    public void TryPushDailyDigest()
    {
        if (_disposed || !_settings.NotifyEnabled || !_settings.DailyDigestEnabled) return;

        var today = DateTime.Today;
        var todayStr = today.ToString("yyyy-MM-dd");

        // 当日推送过后，今日内便不再做今日待办通知推送的检测
        if (_settings.LastDigestDate == todayStr) return;

        _settings.LastDigestDate = todayStr;
        _settings.Save();

        var all = _getSchedules();
        var todayTasks = all
            .Where(t => !t.Done && t.Due?.Date == today)
            .OrderBy(t => t.StartTime ?? TimeSpan.MaxValue)
            .ThenBy(t => t.Order)
            .ToList();

        var overdueCount = all.Count(t => !t.Done && t.Due is { } d && d.Date < today);

        string title;
        string body;

        if (todayTasks.Count == 0)
        {
            title = "今日待办";
            body = overdueCount > 0
                ? $"今日无任务，享受生活吧！（另有 {overdueCount} 项逾期未完成）"
                : "今日无任务，享受生活吧！";
        }
        else
        {
            title = overdueCount > 0
                ? $"今日待办 {todayTasks.Count} 项 · {overdueCount} 项逾期"
                : $"今日待办 · {todayTasks.Count} 项";

            var preview = todayTasks
                .Take(4)
                .Select(t => t.StartTime is { } st ? $"{Fmt(st)} {TitleOf(t)}" : TitleOf(t));
            body = string.Join(" · ", preview);
            if (todayTasks.Count > 4) body += $" 等 {todayTasks.Count} 项";
        }

        ShowToast(
            title: title,
            body: body,
            tag: $"digest-{todayStr}",
            taskId: null,
            includeCompleteAction: false);
    }

    public void SendTestNotification()
    {
        ShowToast(
            title: "日新通知已就绪",
            body: "将按各日程配置的节点（提前 15/5 分钟、开始、结束）准时提醒，并支持标为完成或延后。",
            tag: "test",
            taskId: null,
            includeCompleteAction: false);
    }

    /// <summary>当日程时间或提醒配置改变时，清理该任务尚未过期的已触发记录；已完成则清理贪睡队列。</summary>
    public void ResetTaskFires(TaskVM task)
    {
        if (_disposed) return;

        if (task.Done)
        {
            _snoozes.RemoveAll(s => s.TaskId == task.Id);
            return;
        }

        var today = DateTime.Today;
        var now = DateTime.Now.TimeOfDay;
        var prefix = task.Id + "|";

        var currentTriggers = new HashSet<(string Node, int TargetMinutes)>();
        if (task.Due?.Date == today && task.StartTime is { } st && task.EndTime is { } et && task.HasRemind)
        {
            foreach (var trigger in RemindRules.GetTriggers(task.Remind, st, et, TitleOf(task)))
                currentTriggers.Add((trigger.Node, (int)trigger.TargetTime.TotalMinutes));
        }

        _firedKeys.RemoveWhere(key =>
        {
            if (!key.StartsWith(prefix, StringComparison.Ordinal)) return false;

            var parts = key.Split('|');
            if (parts.Length != 4) return true;

            if (!DateTime.TryParseExact(parts[1], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var keyDate)
                || keyDate != today)
                return true;

            if (!int.TryParse(parts[3], out var targetMins)) return true;

            if (TimeSpan.FromMinutes(targetMins) > now) return true;

            return !currentTriggers.Contains((parts[2], targetMins));
        });
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (_disposed || e.Mode != PowerModes.Resume) return;
        if (!_settings.NotifyEnabled || !_settings.DailyDigestEnabled) return;

        // 今日已推送过则不再做检测
        if (_settings.LastDigestDate == DateTime.Today.ToString("yyyy-MM-dd")) return;

        Application.Current?.Dispatcher.BeginInvoke(ScheduleDigestCheck);
    }

    private void CheckScheduledReminders()
    {
        if (_disposed || !_settings.NotifyEnabled) return;

        var nowDateTime = DateTime.Now;
        var today = nowDateTime.Date;
        if (today != _lastScanDate)
        {
            _lastScanDate = today;
            _firedKeys.Clear();
        }

        var all = _getSchedules();

        // 1. 处理到点的贪睡提醒
        for (var i = _snoozes.Count - 1; i >= 0; i--)
        {
            var entry = _snoozes[i];
            if (nowDateTime < entry.FireAt) continue;

            _snoozes.RemoveAt(i);
            var targetTask = all.FirstOrDefault(t => t.Id == entry.TaskId);
            if (targetTask is null || targetTask.Done) continue;

            ShowToast(
                title: entry.Title,
                body: entry.Body,
                tag: $"snz-{targetTask.Id}",
                taskId: targetTask.Id,
                includeCompleteAction: true);
        }

        // 2. 扫描今日各日程的相对提醒节点
        var now = nowDateTime.TimeOfDay;
        foreach (var task in all)
        {
            if (task.Done || task.Due?.Date != today) continue;
            if (!task.HasTime || !task.HasRemind) continue;

            var st = task.StartTime!.Value;
            var et = task.EndTime!.Value;
            var range = $"{Fmt(st)} – {Fmt(et)}";

            foreach (var trigger in RemindRules.GetTriggers(task.Remind, st, et, TitleOf(task)))
            {
                var primary = trigger.IsEnd ? $"{range} · 记得确认完成" : range;
                TryFire(
                    task,
                    today,
                    now,
                    trigger.TargetTime,
                    trigger.Node,
                    trigger.ToastTitle,
                    BuildBody(task, primary));
            }
        }
    }

    private void TryFire(
        TaskVM task,
        DateTime today,
        TimeSpan now,
        TimeSpan target,
        string stage,
        string title,
        string body)
    {
        if (now < target || now >= target + TimeSpan.FromMinutes(2)) return;

        var key = $"{task.Id}|{today:yyyy-MM-dd}|{stage}|{(int)target.TotalMinutes}";
        if (!_firedKeys.Add(key)) return;

        ShowToast(
            title: title,
            body: body,
            tag: $"{task.Id}-{stage}",
            taskId: task.Id,
            includeCompleteAction: true);
    }

    private void ShowToast(string title, string body, string tag, string? taskId, bool includeCompleteAction)
    {
        try
        {
            var builder = new ToastContentBuilder()
                .AddArgument("action", "open")
                .AddText(title)
                .AddText(body);

            if (!string.IsNullOrEmpty(taskId))
                builder.AddArgument("taskId", taskId);

            var iconPath = EnsureToastIconPath();
            if (!string.IsNullOrEmpty(iconPath))
                builder.AddAppLogoOverride(new Uri(iconPath), ToastGenericAppLogoCrop.Default);

            if (includeCompleteAction && !string.IsNullOrEmpty(taskId))
            {
                builder.AddButton(new ToastButton()
                    .SetContent("标为完成")
                    .AddArgument("action", "complete")
                    .AddArgument("taskId", taskId)
                    .SetBackgroundActivation());

                builder.AddButton(new ToastButton()
                    .SetContent("延后 5 分钟")
                    .AddArgument("action", "snooze")
                    .AddArgument("minutes", "5")
                    .AddArgument("taskId", taskId)
                    .SetBackgroundActivation());

                builder.AddButton(new ToastButton()
                    .SetContent("延后 10 分钟")
                    .AddArgument("action", "snooze")
                    .AddArgument("minutes", "10")
                    .AddArgument("taskId", taskId)
                    .SetBackgroundActivation());
            }

            builder.Show(toast =>
            {
                toast.Tag = tag.Length > 16 ? tag[..16] : tag;
                toast.Group = "daynew";
            });
        }
        catch
        {
            // 通知中心异常或系统关闭通知时静默降级，不打崩主程序
        }
    }

    private void OnToastActivated(ToastNotificationActivatedEventArgsCompat e)
    {
        try
        {
            var args = ToastArguments.Parse(e.Argument);
            var action = args.TryGetValue("action", out var a) ? a : "open";

            if (action == "complete" && args.TryGetValue("taskId", out var taskId) && !string.IsNullOrEmpty(taskId))
            {
                Application.Current?.Dispatcher.BeginInvoke(() => _onCompleteTask(taskId));
                return;
            }

            if (action == "snooze"
                && args.TryGetValue("taskId", out var snoozeTaskId)
                && !string.IsNullOrEmpty(snoozeTaskId)
                && args.TryGetValue("minutes", out var minsStr)
                && int.TryParse(minsStr, out var mins)
                && mins > 0)
            {
                Application.Current?.Dispatcher.BeginInvoke(() => SnoozeTask(snoozeTaskId, mins));
                return;
            }

            Application.Current?.Dispatcher.BeginInvoke(_onOpenWindow);
        }
        catch
        {
            Application.Current?.Dispatcher.BeginInvoke(_onOpenWindow);
        }
    }

    private void SnoozeTask(string taskId, int minutes)
    {
        if (_disposed) return;

        var task = _getSchedules().FirstOrDefault(t => t.Id == taskId);
        if (task is null || task.Done) return;

        var primary = task.HasTime
            ? $"{Fmt(task.StartTime!.Value)} – {Fmt(task.EndTime!.Value)} · 已延后 {minutes} 分钟"
            : $"已延后 {minutes} 分钟";

        _snoozes.RemoveAll(s => s.TaskId == taskId);
        _snoozes.Add(new SnoozeEntry(
            taskId,
            DateTime.Now.AddMinutes(minutes),
            $"延后提醒 · {TitleOf(task)}",
            BuildBody(task, primary)));
    }

    private string? EnsureToastIconPath()
    {
        if (_toastIconPath is not null && File.Exists(_toastIconPath)) return _toastIconPath;

        try
        {
            Directory.CreateDirectory(StoreFile.Folder);
            var path = Path.Combine(StoreFile.Folder, "toast-icon.png");
            if (!File.Exists(path))
            {
                var exePath = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(exePath))
                {
                    using var icon = Drawing.Icon.ExtractAssociatedIcon(exePath);
                    if (icon is not null)
                    {
                        using var bmp = icon.ToBitmap();
                        bmp.Save(path, ImageFormat.Png);
                    }
                }
            }

            if (File.Exists(path))
            {
                _toastIconPath = path;
                return path;
            }
        }
        catch
        {
            // 图标提取失败不影响纯文本通知
        }

        return null;
    }

    private static string TitleOf(TaskVM task)
        => string.IsNullOrWhiteSpace(task.Title) ? "未命名日程" : task.Title.Trim();

    private static string BuildBody(TaskVM task, string primary)
    {
        var parts = new List<string>(2) { primary };
        if (task.HasGoal)
            parts.Add(task.GoalLabel);
        else if (!string.IsNullOrWhiteSpace(task.Note))
            parts.Add(task.Note.Replace('\r', ' ').Replace('\n', ' ').Trim());
        return string.Join(" · ", parts);
    }

    private static string Fmt(TimeSpan t) => $"{(int)t.TotalHours:00}:{t.Minutes:00}";

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer.Stop();
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        ToastNotificationManagerCompat.OnActivated -= OnToastActivated;
    }
}
