using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using DayNew.Core;
using DayNew.Services;

namespace DayNew.ViewModels;

/// <summary>
/// 主视图模型。原型阶段数据只存在内存里，但档位推导规则与正式版一致：
/// 档位不是字段，是 due 和今天的关系算出来的。
/// </summary>
public sealed class MainViewModel : ObservableObject
{
    private readonly DayStore _store = new();
    private readonly StoreFile _file = new();
    private readonly CompletionLog _history = new();
    private readonly UndoManager _undo = new();
    private readonly DispatcherTimer _autoSyncTimer = new();
    private readonly DispatcherTimer _periodicSyncTimer = new();
    private bool _hasInitialized;
    private bool _hasActiveView = true;
    private bool _uiDirty;
    private bool _timelineDirty;
    private bool _suppressAutoSyncSchedule;
    private McpServerService? _mcpServer;
    private GlobalHotKeyService? _hotKeyService;
    private string _mcpPortText = "";
    private string _mcpTokenCache = "";
    private bool _isMcpTokenRevealed;
    private string _lastOverdueSnapshot = "";

    public event Action<string, bool>? ToastRequested;
    public event Action<TaskVM>? TaskTouched;
    public event Action<int, int>? SummaryChanged;

    public AppSettings Settings { get; } = AppSettings.Load();

    internal DayStore Store => _store;

    internal CompletionLog History => _history;

    internal void NotifyTaskTouched(TaskVM task) => TaskTouched?.Invoke(task);

    public IReadOnlyList<TaskVM> AllSchedules => _all;

    private void Notify(string message, bool highlight = false) => ToastRequested?.Invoke(message, highlight);

    /// <summary>日程的唯一来源：就是 store 里那一个列表，不另存副本。</summary>
    private List<TaskVM> _all => _store.Schedules;

    private NavItemVM _selectedNav;
    private bool _isDockExpanded = true;

    /// <summary>新建 / 编辑日程的弹窗状态。打开时 IsEditorOpen 为 true，界面盖一层浮层。</summary>
    public ScheduleEditorVM Editor { get; }

    /// <summary>快捷日程预设管理弹窗状态。</summary>
    public PresetManagerVM PresetManager { get; }

    /// <summary>目标树结构编辑弹窗状态。</summary>
    public GoalTreeEditorVM GoalTreeEditor { get; }

    /// <summary>目标树多选弹窗状态。</summary>
    public GoalTreePickerVM GoalPicker { get; }

    /// <summary>目标 / 子目标可选截止日期选择器状态。</summary>
    public DeadlinePickerVM DeadlinePicker { get; } = new();

    /// <summary>专注计时状态机。</summary>
    public FocusTimerVM Focus { get; }

    public ObservableCollection<TaskVM> FocusCandidateTasks { get; } = new();

    private bool _isEditorOpen;
    private bool _isSettingsOpen;
    private SettingsTab _selectedSettingsTab = SettingsTab.General;
    private string? _recordingShortcutId;
    private bool _isSearchOpen;
    private string _searchQuery = "";
    private int _selectedSearchIndex = -1;
    private bool _isImportConfirmOpen;
    private bool _isWebDavBusy;
    private bool _isPresetManagerOpen;
    private bool _isTagDetailOpen;
    private bool _isGoalTreeEditorOpen;
    private bool _isGoalPickerOpen;
    private string _webDavPasswordCache = "";
    private DataTransferService.ImportPackagePreview? _pendingImportPackage;
    private string _pendingImportFileName = "";
    private string _pendingImportSummary = "";
    private string _pendingImportCurrentNote = "";
    private TagVM? _selectedTagDetail;
    private PersonVM? _selectedPersonDetail;
    private LocationVM? _selectedLocationDetail;
    private GoalVM? _selectedGoalDetail;
    private GoalNodeVM? _selectedGoalNodeDetail;
    private int _tagDetailTodayIndex;
    private string? _goalTreeOpenSnapshot;

    public bool IsEditorOpen
    {
        get => _isEditorOpen;
        private set => Set(ref _isEditorOpen, value);
    }

    public bool IsSettingsOpen
    {
        get => _isSettingsOpen;
        private set
        {
            if (Set(ref _isSettingsOpen, value) && !value)
                CancelShortcutRecording();
        }
    }

    public SettingsTab SelectedSettingsTab
    {
        get => _selectedSettingsTab;
        set
        {
            if (!Set(ref _selectedSettingsTab, value)) return;
            if (value != SettingsTab.Shortcuts) CancelShortcutRecording();
            OnPropertyChanged(nameof(IsSettingsTabGeneral));
            OnPropertyChanged(nameof(IsSettingsTabShortcuts));
            OnPropertyChanged(nameof(IsSettingsTabSyncAndAi));
        }
    }

    public bool IsSettingsTabGeneral => SelectedSettingsTab == SettingsTab.General;
    public bool IsSettingsTabShortcuts => SelectedSettingsTab == SettingsTab.Shortcuts;
    public bool IsSettingsTabSyncAndAi => SelectedSettingsTab == SettingsTab.SyncAndAi;

    public ObservableCollection<ShortcutItemVM> GlobalShortcutItems { get; } = new();
    public ObservableCollection<ShortcutItemVM> InAppShortcutItems { get; } = new();

    public string? RecordingShortcutId
    {
        get => _recordingShortcutId;
        private set
        {
            if (Set(ref _recordingShortcutId, value))
                OnPropertyChanged(nameof(IsRecordingShortcut));
        }
    }

    public bool IsRecordingShortcut => !string.IsNullOrEmpty(RecordingShortcutId);

    public bool IsSearchOpen
    {
        get => _isSearchOpen;
        private set => Set(ref _isSearchOpen, value);
    }

    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            if (!Set(ref _searchQuery, value)) return;
            RebuildSearchResults();
        }
    }

    public ObservableCollection<SearchItemVM> SearchResults { get; } = new();

    public bool HasSearchResults => SearchResults.Count > 0;

    public string SearchEmptyHint => string.IsNullOrWhiteSpace(SearchQuery)
        ? "暂无日程、目标或标签"
        : $"没有找到匹配「{SearchQuery.Trim()}」的条目";

    public string SearchShortcutHint
    {
        get
        {
            var g = GetShortcutGesture(ShortcutIds.AppSearch);
            return string.IsNullOrEmpty(g) ? "搜索" : $"搜索 {g}";
        }
    }

    public bool CanUndo => _undo.CanUndo;
    public bool CanRedo => _undo.CanRedo;
    public string UndoTooltip => _undo.NextUndoLabel is { } lbl ? $"撤销：{lbl}（{GetShortcutGesture(ShortcutIds.AppUndo)}）" : "暂无可撤销操作";
    public string RedoTooltip => _undo.NextRedoLabel is { } lbl ? $"重做：{lbl}（{GetShortcutGesture(ShortcutIds.AppRedo)}）" : "暂无可重做操作";

    public bool IsImportConfirmOpen
    {
        get => _isImportConfirmOpen;
        private set => Set(ref _isImportConfirmOpen, value);
    }

    public string PendingImportFileName
    {
        get => _pendingImportFileName;
        private set => Set(ref _pendingImportFileName, value);
    }

    public string PendingImportSummary
    {
        get => _pendingImportSummary;
        private set => Set(ref _pendingImportSummary, value);
    }

    public string PendingImportCurrentNote
    {
        get => _pendingImportCurrentNote;
        private set => Set(ref _pendingImportCurrentNote, value);
    }

    public bool IsPresetManagerOpen
    {
        get => _isPresetManagerOpen;
        private set => Set(ref _isPresetManagerOpen, value);
    }

    public bool IsTagDetailOpen
    {
        get => _isTagDetailOpen;
        private set => Set(ref _isTagDetailOpen, value);
    }

    public bool IsGoalTreeEditorOpen
    {
        get => _isGoalTreeEditorOpen;
        private set => Set(ref _isGoalTreeEditorOpen, value);
    }

    public bool IsGoalPickerOpen
    {
        get => _isGoalPickerOpen;
        private set => Set(ref _isGoalPickerOpen, value);
    }

    public TagVM? SelectedTagDetail
    {
        get => _selectedTagDetail;
        private set
        {
            if (Set(ref _selectedTagDetail, value))
                RaiseDetailHeaderProperties();
        }
    }

    public PersonVM? SelectedPersonDetail
    {
        get => _selectedPersonDetail;
        private set
        {
            if (Set(ref _selectedPersonDetail, value))
                RaiseDetailHeaderProperties();
        }
    }

    public LocationVM? SelectedLocationDetail
    {
        get => _selectedLocationDetail;
        private set
        {
            if (Set(ref _selectedLocationDetail, value))
                RaiseDetailHeaderProperties();
        }
    }

    public GoalVM? SelectedGoalDetail
    {
        get => _selectedGoalDetail;
        private set
        {
            if (Set(ref _selectedGoalDetail, value))
                RaiseDetailHeaderProperties();
        }
    }

    public bool DetailHasColorDot => SelectedGoalDetail is not null || SelectedTagDetail is not null;

    public bool DetailHasIcon => SelectedPersonDetail is not null || SelectedLocationDetail is not null;

    public string DetailIconGlyph => SelectedPersonDetail is not null
        ? "\uE77B"
        : (SelectedLocationDetail is not null ? "\uE707" : "");

    public Brush DetailColor => SelectedGoalDetail?.Color ?? SelectedTagDetail?.Color ?? Brushes.Transparent;

    public string DetailTitle => SelectedGoalDetail is not null
        ? (_selectedGoalNodeDetail is { IsRoot: false } sub
            ? $"{SelectedGoalDetail.Title} › {sub.DisplayTitle}"
            : SelectedGoalDetail.Title)
        : SelectedTagDetail?.Name
          ?? SelectedPersonDetail?.Name
          ?? SelectedLocationDetail?.Name
          ?? "";

    public ObservableCollection<TagDayColumnVM> TagDetailDays { get; } = new();

    public int TagDetailTodayIndex => _tagDetailTodayIndex;

    public string TagDetailTotalText
    {
        get
        {
            if (SelectedGoalDetail is not null)
            {
                if (_selectedGoalNodeDetail is { IsRoot: false } sub && SelectedGoalDetail.Nodes.Contains(sub))
                {
                    var ids = SelectedGoalDetail.CollectSubtreeIds(sub);
                    var subCount = _all.Count(t => t.HasGoalSubtreeRef(SelectedGoalDetail, ids));
                    return subCount == 0 ? "该子目标暂无日程" : $"共 {subCount} 条日程 · {sub.HoverStatusText}";
                }

                var goalCount = _all.Count(t => t.HasGoalRef(SelectedGoalDetail));
                return goalCount == 0 ? "暂无日程" : $"共 {goalCount} 条日程 · {SelectedGoalDetail.ProgressText}";
            }

            if (SelectedTagDetail is not null)
            {
                var count = _all.Count(t => t.Tags.Contains(SelectedTagDetail));
                return count == 0 ? "暂无日程" : $"共 {count} 条日程";
            }

            if (SelectedPersonDetail is not null)
            {
                var count = _all.Count(t => t.Persons.Contains(SelectedPersonDetail));
                return count == 0 ? "暂无日程" : $"共 {count} 条日程";
            }

            if (SelectedLocationDetail is not null)
            {
                var count = _all.Count(t => t.Locations.Contains(SelectedLocationDetail));
                return count == 0 ? "暂无日程" : $"共 {count} 条日程";
            }

            return "";
        }
    }

    private void RaiseDetailHeaderProperties()
    {
        OnPropertyChanged(nameof(DetailColor));
        OnPropertyChanged(nameof(DetailHasColorDot));
        OnPropertyChanged(nameof(DetailHasIcon));
        OnPropertyChanged(nameof(DetailIconGlyph));
        OnPropertyChanged(nameof(DetailTitle));
        OnPropertyChanged(nameof(TagDetailTotalText));
    }

    public bool AutoStartEnabled
    {
        get => AutoStartService.IsEnabled;
        set
        {
            if (AutoStartService.IsEnabled == value) return;
            if (!AutoStartService.SetEnabled(value))
            {
                Notify("写入开机自启设置失败", highlight: true);
                OnPropertyChanged();
                return;
            }
            OnPropertyChanged();
            Notify(value ? "已开启开机静默自启" : "已关闭开机自启");
        }
    }

    public bool CloseToTray
    {
        get => Settings.CloseToTray;
        set
        {
            if (Settings.CloseToTray == value) return;
            Settings.CloseToTray = value;
            Settings.Save();
            OnPropertyChanged();
        }
    }

    public bool NotifyEnabled
    {
        get => Settings.NotifyEnabled;
        set
        {
            if (Settings.NotifyEnabled == value) return;
            Settings.NotifyEnabled = value;
            Settings.Save();
            OnPropertyChanged();
            Notify(value ? "已开启系统通知" : "已暂停系统通知");
        }
    }

    public bool DailyDigestEnabled
    {
        get => Settings.DailyDigestEnabled;
        set
        {
            if (Settings.DailyDigestEnabled == value) return;
            Settings.DailyDigestEnabled = value;
            Settings.Save();
            OnPropertyChanged();
            Notify(value ? "已开启每日待办摘要" : "已关闭每日待办摘要");
        }
    }

    public bool FocusAutoOpenMini
    {
        get => Settings.FocusAutoOpenMini;
        set
        {
            if (Settings.FocusAutoOpenMini == value) return;
            Settings.FocusAutoOpenMini = value;
            Settings.Save();
            OnPropertyChanged();
        }
    }

    public bool FocusKeepMiniOnCloseToTray
    {
        get => Settings.FocusKeepMiniOnCloseToTray;
        set
        {
            if (Settings.FocusKeepMiniOnCloseToTray == value) return;
            Settings.FocusKeepMiniOnCloseToTray = value;
            Settings.Save();
            OnPropertyChanged();
        }
    }

    public bool FocusSoundEnabled
    {
        get => Settings.FocusSoundEnabled;
        set
        {
            if (Settings.FocusSoundEnabled == value) return;
            Settings.FocusSoundEnabled = value;
            Settings.Save();
            OnPropertyChanged();
        }
    }

    public bool IsDefaultRemindNone => !RemindRules.HasAny(Settings.DefaultRemind);

    public bool IsDefaultRemindPre15 => RemindRules.HasNode(Settings.DefaultRemind, RemindRules.Pre15);

    public bool IsDefaultRemindPre5 => RemindRules.HasNode(Settings.DefaultRemind, RemindRules.Pre5);

    public bool IsDefaultRemindStart => RemindRules.HasNode(Settings.DefaultRemind, RemindRules.Start);

    public bool IsDefaultRemindEnd => RemindRules.HasNode(Settings.DefaultRemind, RemindRules.End);

    public void ToggleDefaultRemindNode(string node)
    {
        var next = RemindRules.ToggleNode(Settings.DefaultRemind, node);
        if (Settings.DefaultRemind == next) return;

        Settings.DefaultRemind = next;
        Settings.Save();
        OnPropertyChanged(nameof(IsDefaultRemindNone));
        OnPropertyChanged(nameof(IsDefaultRemindPre15));
        OnPropertyChanged(nameof(IsDefaultRemindPre5));
        OnPropertyChanged(nameof(IsDefaultRemindStart));
        OnPropertyChanged(nameof(IsDefaultRemindEnd));
    }

    public string DataFolderPath => StoreFile.Folder;

    public string AppVersion => "v" + (typeof(MainViewModel).Assembly.GetName().Version?.ToString(3) ?? "0.1.0");

    public string WebDavUrl
    {
        get => Settings.WebDavUrl;
        set
        {
            var next = value ?? "";
            if (Settings.WebDavUrl == next) return;
            Settings.WebDavUrl = next;
            Settings.Save();
            OnPropertyChanged();
        }
    }

    public string WebDavUser
    {
        get => Settings.WebDavUser;
        set
        {
            var next = value ?? "";
            if (Settings.WebDavUser == next) return;
            Settings.WebDavUser = next;
            Settings.Save();
            OnPropertyChanged();
        }
    }

    public string WebDavPassword
    {
        get => _webDavPasswordCache;
        set
        {
            var next = value ?? "";
            if (_webDavPasswordCache == next) return;
            _webDavPasswordCache = next;
            Settings.SetWebDavPassword(next);
            Settings.Save();
            OnPropertyChanged();
        }
    }

    public bool IsWebDavBusy
    {
        get => _isWebDavBusy;
        private set
        {
            if (!Set(ref _isWebDavBusy, value)) return;
            OnPropertyChanged(nameof(IsWebDavIdle));
        }
    }

    public bool IsWebDavIdle => !IsWebDavBusy;

    public bool AutoSyncEnabled
    {
        get => Settings.AutoSyncEnabled;
        set
        {
            if (Settings.AutoSyncEnabled == value) return;
            Settings.AutoSyncEnabled = value;
            Settings.Save();
            OnPropertyChanged();

            if (value)
            {
                _periodicSyncTimer.Start();
                Notify("已开启多设备自动同步");
                ScheduleAutoSync(delaySeconds: 1);
            }
            else
            {
                _autoSyncTimer.Stop();
                _periodicSyncTimer.Stop();
                Notify("已关闭多设备自动同步");
            }
        }
    }

    public string WebDavStatusText
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Settings.LastWebDavSyncAt))
                return $"上次同步：{Settings.LastWebDavSyncAt}";
            if (!string.IsNullOrWhiteSpace(Settings.LastWebDavBackupAt))
                return $"上次云备份：{Settings.LastWebDavBackupAt}";
            return "未同步";
        }
    }

    public async Task SyncWebDavNowAsync(bool silent = false)
    {
        if (IsWebDavBusy) return;
        if (string.IsNullOrWhiteSpace(WebDavUrl) ||
            string.IsNullOrWhiteSpace(WebDavUser) ||
            string.IsNullOrEmpty(WebDavPassword))
        {
            if (!silent) ValidateWebDavInputs();
            return;
        }

        _autoSyncTimer.Stop();
        IsWebDavBusy = true;
        try
        {
            var result = await WebDavService.SyncMultiDeviceAsync(_store, _file, _history, Settings);
            OnPropertyChanged(nameof(WebDavStatusText));

            if (result.StoreChanged)
            {
                _suppressAutoSyncSchedule = true;
                try
                {
                    Rebuild();
                }
                finally
                {
                    _suppressAutoSyncSchedule = false;
                }
            }

            if (!silent || result.ConflictCopiesCount > 0 || result.StoreChanged)
            {
                var msg = result.ConflictCopiesCount > 0
                    ? $"同步完成（拉取 {result.UpdatedCount} 处更新，保留 {result.ConflictCopiesCount} 条冲突副本）"
                    : result.StoreChanged
                        ? $"同步完成（已合并 {result.UpdatedCount} 处远端更新）"
                        : result.UploadedOwn
                            ? "同步完成（已上传本机最新快照）"
                            : "云端与本机已是最新状态";
                Notify(msg, highlight: result.ConflictCopiesCount > 0);
            }
        }
        catch (Exception ex)
        {
            if (!silent)
                Notify($"同步失败：{ex.Message}", highlight: true);
        }
        finally
        {
            IsWebDavBusy = false;
        }
    }

    public void StopBackgroundTimers()
    {
        _autoSyncTimer.Stop();
        _periodicSyncTimer.Stop();
        Focus.Dispose();
    }

    // ---------------- P3: AI 与 MCP 接口 ----------------

    public void AttachMcpServer(McpServerService server)
    {
        _mcpServer = server;
        _mcpServer.StatusChanged += () => OnPropertyChanged(nameof(McpStatusText));
        OnPropertyChanged(nameof(McpStatusText));
    }

    public bool McpEnabled
    {
        get => Settings.McpEnabled;
        set
        {
            if (Settings.McpEnabled == value) return;
            Settings.McpEnabled = value;
            Settings.Save();
            OnPropertyChanged();
            _mcpServer?.StartOrRestart();
            OnPropertyChanged(nameof(McpStatusText));

            if (value)
            {
                if (!string.IsNullOrEmpty(_mcpServer?.LastError))
                    Notify(_mcpServer.LastError, highlight: true);
                else
                    Notify($"已开启 MCP 接口（127.0.0.1:{Settings.McpPort}/mcp）");
            }
            else
            {
                Notify("已关闭 MCP 接口");
            }
        }
    }

    public string McpPortText
    {
        get => _mcpPortText;
        set => Set(ref _mcpPortText, value ?? "");
    }

    public string McpEndpointText => $"http://127.0.0.1:{Settings.McpPort}/mcp";

    public bool IsMcpTokenRevealed
    {
        get => _isMcpTokenRevealed;
        private set
        {
            if (!Set(ref _isMcpTokenRevealed, value)) return;
            OnPropertyChanged(nameof(McpTokenDisplayText));
            OnPropertyChanged(nameof(McpTokenRevealButtonText));
        }
    }

    public string McpTokenRevealButtonText => IsMcpTokenRevealed ? "隐藏" : "显示";

    public string McpTokenDisplayText
    {
        get
        {
            var token = GetMcpTokenPlain();
            return IsMcpTokenRevealed ? token : "dn_" + new string('•', 16);
        }
    }

    public string McpStatusText
    {
        get
        {
            if (!Settings.McpEnabled) return "已关闭";
            if (!string.IsNullOrWhiteSpace(_mcpServer?.LastError)) return _mcpServer.LastError;
            if (_mcpServer is { IsListening: true }) return $"运行中 · :{Settings.McpPort}";
            return "已开启";
        }
    }

    public string GetMcpTokenPlain()
    {
        if (string.IsNullOrEmpty(_mcpTokenCache))
            _mcpTokenCache = Settings.GetMcpToken();
        return _mcpTokenCache;
    }

    public void ApplyMcpPort()
    {
        var raw = (McpPortText ?? "").Trim();
        if (!int.TryParse(raw, out var port) || port is < 1024 or > 65535)
        {
            Notify("端口范围需在 1024–65535 之间", highlight: true);
            McpPortText = Settings.McpPort.ToString();
            return;
        }

        if (Settings.McpPort == port)
        {
            Notify($"当前已监听端口 {port}");
            return;
        }

        Settings.McpPort = port;
        Settings.Save();
        OnPropertyChanged(nameof(McpEndpointText));
        _mcpServer?.StartOrRestart();
        OnPropertyChanged(nameof(McpStatusText));

        if (!string.IsNullOrEmpty(_mcpServer?.LastError))
            Notify(_mcpServer.LastError, highlight: true);
        else
            Notify($"MCP 端口已更新为 {port}");
    }

    public void ToggleMcpTokenReveal() => IsMcpTokenRevealed = !IsMcpTokenRevealed;

    public void RegenerateMcpToken()
    {
        _mcpTokenCache = Settings.RegenerateMcpToken();
        OnPropertyChanged(nameof(McpTokenDisplayText));
        Notify("已重新生成 MCP Bearer Token（旧连接需同步更新）");
    }

    public string BuildMcpJsonConfig()
    {
        var exePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(exePath) || !exePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            exePath = Path.Combine(AppContext.BaseDirectory, "DayNew.exe");

        var payload = new
        {
            mcpServers = new Dictionary<string, object>
            {
                ["daynew"] = new
                {
                    type = "http",
                    url = McpEndpointText,
                    headers = new Dictionary<string, string>
                    {
                        ["Authorization"] = $"Bearer {GetMcpTokenPlain()}"
                    }
                },
                ["daynew-stdio"] = new
                {
                    command = exePath,
                    args = new[] { "--mcp" }
                }
            }
        };

        return System.Text.Json.JsonSerializer.Serialize(payload, StoreFile.JsonOptions);
    }

    public string BuildMcpInstallPrompt()
    {
        var exePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(exePath) || !exePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            exePath = Path.Combine(AppContext.BaseDirectory, "DayNew.exe");

        return $"""
            请帮我把「日新 DayNew」本地 MCP 服务接入到当前客户端中，按「HTTP 优先、STDIO 回退」配置并完成自检：
            1. 首选方式（Streamable HTTP）：
               - 端点：{McpEndpointText}
               - 请求头：Authorization: Bearer {GetMcpTokenPlain()}
               （日新在 Windows 托盘常驻时即可响应；请确保已在日新「设置 → AI 与 MCP 接口」中开启开关）
            2. 回退方式（仅当当前客户端不支持带 Header 的 HTTP MCP 时使用 STDIO 桥接）：
               - 命令：{exePath}
               - 参数：["--mcp"]
            3. 配置完成后请立即调用一次只读工具 `get_overview` 验证连通性与今日待办读取；
            4. 注意安全规则：删除类工具（delete_schedule / delete_tag / delete_goal）必须先向我说明待删明细与影响范围并征得同意，且执行时日新本机仍会弹出置顶确认窗由我当场点击许可。
            """;
    }

    private void ScheduleAutoSync(int delaySeconds = 6)
    {
        if (_suppressAutoSyncSchedule || !Settings.AutoSyncEnabled) return;
        if (string.IsNullOrWhiteSpace(WebDavUrl) ||
            string.IsNullOrWhiteSpace(WebDavUser) ||
            string.IsNullOrEmpty(WebDavPassword))
        {
            return;
        }

        _autoSyncTimer.Stop();
        _autoSyncTimer.Interval = TimeSpan.FromSeconds(Math.Max(1, delaySeconds));
        _autoSyncTimer.Start();
    }

    public async Task TestWebDavConnectionAsync()
    {
        if (IsWebDavBusy) return;
        if (!ValidateWebDavInputs()) return;

        IsWebDavBusy = true;
        try
        {
            var (ok, message) = await WebDavService.TestConnectionAsync(WebDavUrl, WebDavUser, WebDavPassword);
            Notify(message, highlight: !ok);
        }
        catch (Exception ex)
        {
            Notify($"连接失败：{ex.Message}", highlight: true);
        }
        finally
        {
            IsWebDavBusy = false;
        }
    }

    public async Task UploadWebDavBackupAsync()
    {
        if (IsWebDavBusy) return;
        if (!ValidateWebDavInputs()) return;

        IsWebDavBusy = true;
        try
        {
            var backupAt = await WebDavService.UploadBackupZipAsync(
                _store, _file, _history, WebDavUrl, WebDavUser, WebDavPassword);
            Settings.LastWebDavBackupAt = backupAt;
            Settings.Save();
            OnPropertyChanged(nameof(WebDavStatusText));
            Notify($"已上传云端备份（{WebDavService.CloudBackupFileName}）");
        }
        catch (Exception ex)
        {
            Notify($"上传云端备份失败：{ex.Message}", highlight: true);
        }
        finally
        {
            IsWebDavBusy = false;
        }
    }

    public async Task DownloadWebDavBackupAsync()
    {
        if (IsWebDavBusy) return;
        if (!ValidateWebDavInputs()) return;

        IsWebDavBusy = true;
        try
        {
            var localZip = await WebDavService.DownloadBackupZipAsync(WebDavUrl, WebDavUser, WebDavPassword);
            BeginImportData(localZip);
        }
        catch (Exception ex)
        {
            Notify($"拉取云端备份失败：{ex.Message}", highlight: true);
        }
        finally
        {
            IsWebDavBusy = false;
        }
    }

    private bool ValidateWebDavInputs()
    {
        if (string.IsNullOrWhiteSpace(WebDavUrl))
        {
            Notify("请先填写 WebDAV 地址", highlight: true);
            return false;
        }

        if (string.IsNullOrWhiteSpace(WebDavUser) || string.IsNullOrEmpty(WebDavPassword))
        {
            Notify("请先填写账号与密码", highlight: true);
            return false;
        }

        return true;
    }

    public void OpenSettings(SettingsTab? initialTab = null)
    {
        if (initialTab is { } tab) SelectedSettingsTab = tab;
        OnPropertyChanged(nameof(AutoStartEnabled));
        RefreshHotKeyStatuses();
        IsSettingsOpen = true;
    }

    public void CloseSettings() => IsSettingsOpen = false;

    // ---------------- 快捷键管理（系统级全局热键 + 应用内快捷键） ----------------

    public void AttachHotKeyService(GlobalHotKeyService service)
    {
        _hotKeyService = service;
        _hotKeyService.StatusChanged += RefreshHotKeyStatuses;
        _hotKeyService.ApplySettings(Settings);
    }

    private void RebuildShortcutItems()
    {
        GlobalShortcutItems.Clear();
        InAppShortcutItems.Clear();

        foreach (var def in ShortcutCatalog.All)
        {
            var gesture = Settings.GetShortcut(def.Id);
            var status = def.Scope == ShortcutScope.Global
                ? (_hotKeyService?.GetStatus(def.Id) ?? (string.IsNullOrEmpty(gesture) ? HotKeyStatus.Disabled : HotKeyStatus.Registered))
                : HotKeyStatus.Disabled;

            var item = new ShortcutItemVM(def, gesture, status);
            if (def.Scope == ShortcutScope.Global) GlobalShortcutItems.Add(item);
            else InAppShortcutItems.Add(item);
        }
    }

    public void RefreshHotKeyStatuses()
    {
        foreach (var item in GlobalShortcutItems)
        {
            var status = _hotKeyService?.GetStatus(item.Id) ??
                         (string.IsNullOrEmpty(item.Gesture) ? HotKeyStatus.Disabled : HotKeyStatus.Registered);
            item.HotKeyStatus = status;
        }
    }

    public string GetShortcutGesture(string actionId) => Settings.GetShortcut(actionId);

    public bool MatchesShortcut(string actionId, ModifierKeys modifiers, Key key)
        => ShortcutGesture.Matches(Settings.GetShortcut(actionId), modifiers, key);

    public void BeginShortcutRecording(string actionId)
    {
        RecordingShortcutId = string.Equals(RecordingShortcutId, actionId, StringComparison.Ordinal) ? null : actionId;
        SyncShortcutRecordingState();
    }

    public void CancelShortcutRecording()
    {
        if (RecordingShortcutId is null) return;
        RecordingShortcutId = null;
        SyncShortcutRecordingState();
    }

    private void SyncShortcutRecordingState()
    {
        foreach (var item in GlobalShortcutItems)
            item.IsRecording = string.Equals(item.Id, RecordingShortcutId, StringComparison.Ordinal);
        foreach (var item in InAppShortcutItems)
            item.IsRecording = string.Equals(item.Id, RecordingShortcutId, StringComparison.Ordinal);
    }

    public bool CommitRecordedShortcut(ModifierKeys modifiers, Key key)
    {
        if (RecordingShortcutId is not { } actionId) return false;
        var item = GlobalShortcutItems.Concat(InAppShortcutItems).FirstOrDefault(x => x.Id == actionId);
        if (item is null)
        {
            CancelShortcutRecording();
            return false;
        }

        if (key == Key.Escape && modifiers == ModifierKeys.None)
        {
            CancelShortcutRecording();
            return true;
        }

        if ((key == Key.Back || key == Key.Delete) && modifiers == ModifierKeys.None)
        {
            ClearShortcut(actionId);
            return true;
        }

        if (ShortcutGesture.IsModifierOnly(key)) return true;

        if (!ShortcutGesture.IsValidForScope(item.Scope, modifiers, key))
        {
            Notify(item.IsGlobal
                ? "全局热键需包含修饰键（Ctrl/Alt/Shift/Win）或功能键 F1–F12"
                : "应用内快捷键需包含 Ctrl / Alt / Win 组合键或 F1–F12", highlight: true);
            return true;
        }

        var formatted = ShortcutGesture.Format(modifiers, key);
        var siblingList = item.IsGlobal ? GlobalShortcutItems : InAppShortcutItems;
        var conflict = siblingList.FirstOrDefault(other =>
            !string.Equals(other.Id, actionId, StringComparison.Ordinal) &&
            string.Equals(other.Gesture, formatted, StringComparison.OrdinalIgnoreCase));

        if (conflict is not null)
        {
            Notify($"「{formatted}」已被「{conflict.Title}」使用，请先更换或清除原快捷键", highlight: true);
            return true;
        }

        Settings.SetShortcut(actionId, formatted);
        Settings.Save();
        item.Gesture = formatted;
        CancelShortcutRecording();

        if (item.IsGlobal)
        {
            _hotKeyService?.ApplySettings(Settings);
            if (_hotKeyService?.GetStatus(actionId) == HotKeyStatus.ConflictOrFailed)
                Notify($"全局热键「{formatted}」注册失败，可能已被其他软件占用", highlight: true);
            else
                Notify($"已更新全局热键：{item.Title} → {formatted}");
        }
        else
        {
            OnPropertyChanged(nameof(SearchShortcutHint));
            OnPropertyChanged(nameof(UndoTooltip));
            OnPropertyChanged(nameof(RedoTooltip));
            Notify($"已更新快捷键：{item.Title} → {formatted}");
        }

        return true;
    }

    public void ClearShortcut(string actionId)
    {
        var item = GlobalShortcutItems.Concat(InAppShortcutItems).FirstOrDefault(x => x.Id == actionId);
        if (item is null) return;

        Settings.SetShortcut(actionId, "");
        Settings.Save();
        item.Gesture = "";
        CancelShortcutRecording();

        if (item.IsGlobal) _hotKeyService?.ApplySettings(Settings);
        else
        {
            OnPropertyChanged(nameof(SearchShortcutHint));
            OnPropertyChanged(nameof(UndoTooltip));
            OnPropertyChanged(nameof(RedoTooltip));
        }

        Notify($"已禁用快捷键「{item.Title}」");
    }

    public void ResetShortcut(string actionId)
    {
        var item = GlobalShortcutItems.Concat(InAppShortcutItems).FirstOrDefault(x => x.Id == actionId);
        if (item is null) return;

        var siblingList = item.IsGlobal ? GlobalShortcutItems : InAppShortcutItems;
        var conflict = siblingList.FirstOrDefault(other =>
            !string.Equals(other.Id, actionId, StringComparison.Ordinal) &&
            string.Equals(other.Gesture, item.DefaultGesture, StringComparison.OrdinalIgnoreCase));
        if (conflict is not null)
        {
            Notify($"默认键「{item.DefaultGesture}」正被「{conflict.Title}」占用，请先调整后者", highlight: true);
            return;
        }

        Settings.SetShortcut(actionId, item.DefaultGesture);
        Settings.Save();
        item.Gesture = item.DefaultGesture;
        CancelShortcutRecording();

        if (item.IsGlobal)
        {
            _hotKeyService?.ApplySettings(Settings);
            if (_hotKeyService?.GetStatus(actionId) == HotKeyStatus.ConflictOrFailed)
                Notify($"默认全局热键「{item.DefaultGesture}」被系统或其他软件占用", highlight: true);
            else
                Notify($"已恢复默认：{item.Title} → {item.DefaultGesture}");
        }
        else
        {
            OnPropertyChanged(nameof(SearchShortcutHint));
            OnPropertyChanged(nameof(UndoTooltip));
            OnPropertyChanged(nameof(RedoTooltip));
            Notify($"已恢复默认：{item.Title} → {item.DefaultGesture}");
        }
    }

    public void ResetAllShortcuts()
    {
        CancelShortcutRecording();
        Settings.ResetAllShortcuts();
        Settings.Save();
        _hotKeyService?.ApplySettings(Settings);
        RebuildShortcutItems();
        OnPropertyChanged(nameof(SearchShortcutHint));
        OnPropertyChanged(nameof(UndoTooltip));
        OnPropertyChanged(nameof(RedoTooltip));
        Notify("已将全部快捷键恢复为默认配置");
    }

    // ---------------- 全局搜索面板（Command Palette） ----------------

    public void OpenSearch()
    {
        _searchQuery = "";
        OnPropertyChanged(nameof(SearchQuery));
        RebuildSearchResults();
        IsSearchOpen = true;
    }

    public void CloseSearch() => IsSearchOpen = false;

    public void MoveSearchSelection(int delta)
    {
        if (SearchResults.Count == 0)
        {
            _selectedSearchIndex = -1;
            return;
        }

        var next = _selectedSearchIndex < 0
            ? (delta > 0 ? 0 : SearchResults.Count - 1)
            : (_selectedSearchIndex + delta + SearchResults.Count) % SearchResults.Count;

        SelectSearchIndex(next);
    }

    public void SelectSearchItem(SearchItemVM item)
    {
        var idx = SearchResults.IndexOf(item);
        if (idx >= 0) SelectSearchIndex(idx);
    }

    private void SelectSearchIndex(int idx)
    {
        _selectedSearchIndex = idx;
        for (var i = 0; i < SearchResults.Count; i++)
            SearchResults[i].IsSelected = i == idx;
    }

    public bool ActivateCurrentSearchResult()
    {
        if (SearchResults.Count == 0) return false;
        var idx = _selectedSearchIndex >= 0 && _selectedSearchIndex < SearchResults.Count ? _selectedSearchIndex : 0;
        ActivateSearchResult(SearchResults[idx]);
        return true;
    }

    public void ActivateSearchResult(SearchItemVM item)
    {
        IsSearchOpen = false;

        switch (item.Kind)
        {
            case SearchResultKind.Schedule when item.Task is { } task:
                OpenSchedule(task);
                break;

            case SearchResultKind.Goal when item.Goal is { } goal:
                NavigateToGoals();
                OpenGoalDetail(goal);
                break;

            case SearchResultKind.Tag when item.Tag is { } tag:
                SelectedNav = NavItems.First(n => n.Kind == PageKind.Tags);
                SelectedTagPageTab = TagPageTab.Tags;
                OpenTagDetail(tag);
                break;

            case SearchResultKind.Person when item.Person is { } person:
                SelectedNav = NavItems.First(n => n.Kind == PageKind.Tags);
                SelectedTagPageTab = TagPageTab.Persons;
                OpenPersonDetail(person);
                break;

            case SearchResultKind.Location when item.Location is { } loc:
                SelectedNav = NavItems.First(n => n.Kind == PageKind.Tags);
                SelectedTagPageTab = TagPageTab.Locations;
                OpenLocationDetail(loc);
                break;
        }
    }

    private void RebuildSearchResults()
    {
        SearchResults.Clear();
        var raw = (SearchQuery ?? "").Trim();
        var tokens = raw.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (tokens.Length == 0)
        {
            // 空查询展示最常用的待办与活跃目标
            foreach (var task in _all
                         .Where(t => !t.Done)
                         .OrderBy(t => t.Due ?? DateTime.MaxValue)
                         .ThenBy(t => t.StartTime ?? TimeSpan.MaxValue)
                         .ThenBy(t => t.Order)
                         .Take(10))
            {
                SearchResults.Add(CreateScheduleSearchItem(task));
            }

            foreach (var goal in ActiveGoals.Take(4))
            {
                SearchResults.Add(new SearchItemVM
                {
                    Kind = SearchResultKind.Goal,
                    KindBadge = "目标",
                    Title = goal.Title,
                    Subtitle = goal.ProgressText,
                    AccentBrush = goal.Color,
                    Goal = goal
                });
            }
        }
        else
        {
            foreach (var task in _all
                         .Where(t => MatchesAllTokens(tokens,
                             t.Title, t.Note, t.GoalLabel,
                             string.Join(' ', t.Tags.Select(x => x.Name)),
                             string.Join(' ', t.Persons.Select(x => x.Name)),
                             string.Join(' ', t.Locations.Select(x => x.Name))))
                         .OrderBy(t => t.Done)
                         .ThenBy(t => t.Due ?? DateTime.MaxValue)
                         .ThenBy(t => t.StartTime ?? TimeSpan.MaxValue)
                         .Take(20))
            {
                SearchResults.Add(CreateScheduleSearchItem(task));
            }

            foreach (var goal in Goals
                         .Where(g => MatchesAllTokens(tokens, g.Title, string.Join(' ', g.Nodes.Select(n => n.Title))))
                         .Take(8))
            {
                SearchResults.Add(new SearchItemVM
                {
                    Kind = SearchResultKind.Goal,
                    KindBadge = "目标",
                    Title = goal.Title,
                    Subtitle = goal.IsCompleted ? "已完成目标" : goal.ProgressText,
                    AccentBrush = goal.Color,
                    IsDone = goal.IsCompleted,
                    Goal = goal
                });
            }

            foreach (var tag in Tags.Where(t => MatchesAllTokens(tokens, t.Name)).Take(6))
            {
                var count = _all.Count(s => s.Tags.Contains(tag));
                SearchResults.Add(new SearchItemVM
                {
                    Kind = SearchResultKind.Tag,
                    KindBadge = "标签",
                    Title = "#" + tag.Name,
                    Subtitle = count == 0 ? "暂无关联日程" : $"{count} 条日程",
                    AccentBrush = tag.Color,
                    Tag = tag
                });
            }

            foreach (var person in Persons.Where(p => MatchesAllTokens(tokens, p.Name)).Take(6))
            {
                var count = _all.Count(s => s.Persons.Contains(person));
                SearchResults.Add(new SearchItemVM
                {
                    Kind = SearchResultKind.Person,
                    KindBadge = "人物",
                    Title = "@" + person.Name,
                    Subtitle = count == 0 ? "暂无关联日程" : $"{count} 条日程",
                    Person = person
                });
            }

            foreach (var loc in Locations.Where(l => MatchesAllTokens(tokens, l.Name)).Take(6))
            {
                var count = _all.Count(s => s.Locations.Contains(loc));
                SearchResults.Add(new SearchItemVM
                {
                    Kind = SearchResultKind.Location,
                    KindBadge = "地点",
                    Title = loc.Name,
                    Subtitle = count == 0 ? "暂无关联日程" : $"{count} 条日程",
                    Location = loc
                });
            }
        }

        SelectSearchIndex(SearchResults.Count > 0 ? 0 : -1);
        OnPropertyChanged(nameof(HasSearchResults));
        OnPropertyChanged(nameof(SearchEmptyHint));
    }

    private static bool MatchesAllTokens(string[] tokens, params string[] fields)
    {
        foreach (var token in tokens)
        {
            var hit = false;
            foreach (var field in fields)
            {
                if (!string.IsNullOrEmpty(field) &&
                    field.Contains(token, StringComparison.OrdinalIgnoreCase))
                {
                    hit = true;
                    break;
                }
            }
            if (!hit) return false;
        }
        return true;
    }

    private static SearchItemVM CreateScheduleSearchItem(TaskVM task)
    {
        var parts = new List<string>(4);
        if (task.Due is { } due)
        {
            var today = DateTime.Today;
            var dayLabel = due.Date == today
                ? "今天"
                : due.Date == today.AddDays(1)
                    ? "明天"
                    : due.Date == today.AddDays(-1)
                        ? "昨天"
                        : $"{due:M月d日}";
            parts.Add(task.HasTime ? $"{dayLabel} {task.TimeLabel}" : dayLabel);
        }
        else
        {
            parts.Add("不定日期");
        }

        if (task.HasGoal) parts.Add(task.GoalLabel);
        if (task.Tags.Count > 0) parts.Add(string.Join(" ", task.Tags.Take(2).Select(t => "#" + t.Name)));
        if (task.Persons.Count > 0) parts.Add(string.Join(" ", task.Persons.Take(2).Select(p => "@" + p.Name)));

        return new SearchItemVM
        {
            Kind = SearchResultKind.Schedule,
            KindBadge = task.Done ? "已完成" : "日程",
            Title = task.Title,
            Subtitle = string.Join(" · ", parts),
            AccentBrush = task.HasGoal ? task.PrimaryGoalColor : task.Tags.FirstOrDefault()?.Color,
            IsDone = task.Done,
            Task = task
        };
    }

    // ---------------- 快捷新建与导航 ----------------

    public void OpenQuickNewSchedule()
    {
        if (SelectedNav.Kind == PageKind.Future) OpenNewFutureSchedule();
        else OpenNewDeadlineSchedule(DateTime.Today);
    }

    public void NavigateToPageIndex(int oneBasedIndex)
    {
        var idx = oneBasedIndex - 1;
        if (idx >= 0 && idx < NavItems.Count)
            SelectedNav = NavItems[idx];
    }

    // ---------------- 业务撤销 / 重做 ----------------

    public string BeginUndoSnapshot() => UndoManager.CaptureJson(_store);

    public void CommitUndoSnapshot(
        string label,
        string beforeJson,
        IReadOnlyList<CompletionUndoEvent>? completions = null)
    {
        var afterJson = UndoManager.CaptureJson(_store);
        _undo.Push(label, beforeJson, afterJson, completions);
        RaiseUndoStateChanged();
    }

    private void RecordUndo(string label, Action action)
    {
        var before = BeginUndoSnapshot();
        action();
        CommitUndoSnapshot(label, before);
    }

    public bool Undo()
    {
        if (!_undo.TryUndo(_store, _history, out var label))
        {
            Notify("没有可撤销的操作");
            return false;
        }

        ApplyUndoOrRedoRefresh();
        Notify($"已撤销：{label}");
        return true;
    }

    public bool Redo()
    {
        if (!_undo.TryRedo(_store, _history, out var label))
        {
            Notify("没有可重做的操作");
            return false;
        }

        ApplyUndoOrRedoRefresh();
        Notify($"已重做：{label}");
        return true;
    }

    private void ApplyUndoOrRedoRefresh()
    {
        if (IsEditorOpen) CloseEditor();
        if (IsGoalPickerOpen) CloseGoalPicker();
        if (IsGoalTreeEditorOpen)
        {
            DeadlinePicker.Close();
            IsGoalTreeEditorOpen = false;
            GoalTreeEditor.Close();
        }
        if (IsTagDetailOpen) CloseTagDetail();
        if (IsPresetManagerOpen) PresetManager.StartNew();

        Rebuild();
        if (IsSearchOpen) RebuildSearchResults();
        RaiseUndoStateChanged();
    }

    private void RaiseUndoStateChanged()
    {
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
        OnPropertyChanged(nameof(UndoTooltip));
        OnPropertyChanged(nameof(RedoTooltip));
    }

    public void OpenDataFolder()
    {
        try
        {
            Directory.CreateDirectory(StoreFile.Folder);
            Process.Start(new ProcessStartInfo("explorer.exe", StoreFile.Folder) { UseShellExecute = true });
        }
        catch
        {
            Notify("无法打开数据目录", highlight: true);
        }
    }

    public void ExportAllData(string targetZipPath)
    {
        try
        {
            DataTransferService.ExportToZip(_store, _file, _history, targetZipPath);
            Notify($"已导出全部数据至 {Path.GetFileName(targetZipPath)}");
        }
        catch (Exception ex)
        {
            Notify($"导出失败：{ex.Message}", highlight: true);
        }
    }

    public void BeginImportData(string sourcePath)
    {
        try
        {
            var pkg = DataTransferService.InspectPackage(sourcePath);
            _pendingImportPackage = pkg;
            PendingImportFileName = pkg.FileName;
            PendingImportSummary = pkg.HasCompletionsEntry
                ? $"包含 {pkg.ScheduleCount} 条日程、{pkg.GoalCount} 个目标、{pkg.TagCount} 个标签、{pkg.PresetCount} 个快捷卡片，以及 {pkg.CompletionCount} 条完成记录。"
                : $"仅包含 data.json（{pkg.ScheduleCount} 条日程、{pkg.GoalCount} 个目标、{pkg.TagCount} 个标签、{pkg.PresetCount} 个快捷卡片），不含完成记录。";
            PendingImportCurrentNote =
                $"当前库有 {_store.Schedules.Count} 条日程、{_store.Goals.Count} 个目标、{_store.Tags.Count} 个标签。执行前将自动在 backups 目录备份当前全部数据。";
            IsImportConfirmOpen = true;
        }
        catch (Exception ex)
        {
            _pendingImportPackage = null;
            Notify($"无法读取备份：{ex.Message}", highlight: true);
        }
    }

    public void ConfirmImportMerge() => ExecuteImport(merge: true);

    public void ConfirmImportOverwrite() => ExecuteImport(merge: false);

    public void CancelImportConfirm()
    {
        _pendingImportPackage = null;
        IsImportConfirmOpen = false;
    }

    private void ExecuteImport(bool merge)
    {
        if (_pendingImportPackage is not { } pkg)
        {
            IsImportConfirmOpen = false;
            return;
        }

        try
        {
            DeadlinePicker.Close();
            if (IsGoalPickerOpen) CloseGoalPicker();
            if (IsGoalTreeEditorOpen) CloseGoalTreeEditor();
            if (IsEditorOpen) CloseEditor();
            if (IsPresetManagerOpen) ClosePresetManager();
            if (IsTagDetailOpen) CloseTagDetail();
            CollapseAll();

            var result = DataTransferService.ApplyImport(pkg, merge, _store, _file, _history);
            if (!merge && !_store.HasLoadedPresets && _store.Presets.Count == 0)
                SeedDefaultPresets();

            _pendingImportPackage = null;
            IsImportConfirmOpen = false;
            _undo.Clear();
            RaiseUndoStateChanged();
            Rebuild();

            var backupName = Path.GetFileName(result.BackupPath);
            var modeText = merge ? "已按 ID 合并导入" : "已覆盖导入";
            Notify($"{modeText}（原数据已备份为 {backupName}）");
        }
        catch (Exception ex)
        {
            Notify($"导入失败：{ex.Message}", highlight: true);
        }
    }

    public (int TodayPending, int OverdueCount) GetSummaryCounts()
    {
        var today = DateTime.Today;
        var todayPending = 0;
        var overdueCount = 0;

        foreach (var t in _all)
        {
            if (t.Done || t.Due is not { } d) continue;
            if (DeadlineRules.IsOverdue(d, t.DueTime)) overdueCount++;
            else if (d.Date == today) todayPending++;
        }

        return (todayPending, overdueCount);
    }

    public bool CompleteTaskById(string taskId)
    {
        var task = _all.FirstOrDefault(t => t.Id == taskId);
        if (task is null || task.Done) return false;
        ToggleDone(task);
        return true;
    }

    /// <summary>左侧 dock 是否展开（折叠后只留图标）。</summary>
    public bool IsDockExpanded
    {
        get => _isDockExpanded;
        set => Set(ref _isDockExpanded, value);
    }

    public MainViewModel(bool deferUiBuild = false)
    {
        _hasActiveView = !deferUiBuild;
        _webDavPasswordCache = Settings.GetWebDavPassword();
        _mcpPortText = Settings.McpPort.ToString();
        Editor = new ScheduleEditorVM(this);
        PresetManager = new PresetManagerVM(this);
        GoalTreeEditor = new GoalTreeEditorVM(this);
        GoalPicker = new GoalTreePickerVM();

        Lanes = new ObservableCollection<LaneVM>
        {
            new(LaneKind.Overdue, "逾期"),
            new(LaneKind.Today, "今天"),
            new(LaneKind.Tomorrow, "明天"),
            new(LaneKind.ThisWeek, "本周"),
            new(LaneKind.Later, "之后")
        };

        OverviewLanes = new ObservableCollection<LaneVM>(
            Lanes.Where(l => l.Kind != LaneKind.Overdue));

        NavItems = new ObservableCollection<NavItemVM>
        {
            new(PageKind.Focus, "专注", "\uE916",
                "专注计时中心：自动跟随当前日程、自由正计时与悬浮胶囊管理。"),
            new(PageKind.Overview, "概览", "\uE80F",
                "你每天真正会打开的页：一横排档位，拖一下就换个时间做。"),
            new(PageKind.Timeline, "时间轴", "\uE787",
                "纵向是 24 小时，横向是日期，一屏七天。任务有了起止时间就能直接拖进格子；放大时刻度会变细。"),
            new(PageKind.Tags, "标签", "\uE8EC",
                "在这里创建、改名、删除标签。概览页里只负责往卡片上贴，不负责管理。"),
            new(PageKind.Goals, "目标", "\uE7C1",
                "长期目标，比如「学 Python」。任务是它的子项。目标和标签互不干扰：目标回答「为了什么」，标签回答「是什么」。"),
            new(PageKind.Eisenhower, "艾森豪威尔看板", "\uE80A",
                "四象限看板。紧急和重要都靠你手动标记，把卡片拖进哪个格子就改哪个标记。"),
            new(PageKind.Deadlines, "截止日期", "\uE73A",
                "只设了截止日期的事件，以及设了截止日的目标和子目标，按日期分组查看。",
                hasTopDivider: true),
            new(PageKind.Future, "未来事项", "\uEA80",
                "没有明确截止日期、只是将来要做的事，单独收纳在这里，不占「之后」档位。"),
            new(PageKind.Overdue, "逾期", "\uE823",
                "已过截止日还没完成的日程都收在这里，勾选完成、移到今天，或双击细改。")
        };

        _selectedNav = NavItems.First(n => n.Kind == PageKind.Overview);

        ZoomOptions = new ObservableCollection<ZoomOptionVM>();
        for (var i = 0; i < Zooms.Length; i++) ZoomOptions.Add(new ZoomOptionVM(i, Zooms[i].Label));

        Quadrants = new ObservableCollection<QuadrantVM>
        {
            new QuadrantVM(true, true, "紧急 · 重要", "马上自己动手做"),
            new QuadrantVM(false, true, "重要 · 不紧急", "排时间做，最值钱"),
            new QuadrantVM(true, false, "紧急 · 不重要", "能交给别人就别自己做"),
            new QuadrantVM(false, false, "没标记", "不重要也不紧急；还没分类的也在这")
        };

        LoadOrSeed();
        Focus = new FocusTimerVM(
            () => _all,
            () => FocusSoundEnabled,
            ToggleDone,
            (msg, hl) => Notify(msg, hl));
        Focus.SessionStarted += () => OnPropertyChanged(nameof(ShowTopFocusBar));
        Focus.SessionEnded += () => OnPropertyChanged(nameof(ShowTopFocusBar));
        RebuildShortcutItems();
        if (deferUiBuild)
        {
            _uiDirty = true;
            _timelineDirty = true;
        }
        else
        {
            Rebuild();
        }
        _hasInitialized = true;

        _autoSyncTimer.Tick += async (_, _) =>
        {
            _autoSyncTimer.Stop();
            await SyncWebDavNowAsync(silent: true);
        };
        _periodicSyncTimer.Interval = TimeSpan.FromMinutes(5);
        _periodicSyncTimer.Tick += async (_, _) => await SyncWebDavNowAsync(silent: true);

        if (Settings.AutoSyncEnabled)
        {
            _periodicSyncTimer.Start();
            ScheduleAutoSync(delaySeconds: 3);
        }
    }

    public ObservableCollection<LaneVM> Lanes { get; }

    public ObservableCollection<LaneVM> OverviewLanes { get; }

    public LaneVM OverdueLane => LaneOfKind(LaneKind.Overdue);

    public ObservableCollection<NavItemVM> NavItems { get; }

    public ObservableCollection<GoalVM> Goals => _store.Goals;

    public ObservableCollection<GoalVM> ActiveGoals { get; } = new();

    public ObservableCollection<GoalVM> CompletedGoals { get; } = new();

    public bool HasActiveGoals => ActiveGoals.Count > 0;

    public bool HasCompletedGoals => CompletedGoals.Count > 0;

    public ObservableCollection<TagVM> Tags => _store.Tags;

    public ObservableCollection<PersonVM> Persons => _store.Persons;

    public ObservableCollection<LocationVM> Locations => _store.Locations;

    public ObservableCollection<SchedulePresetVM> Presets => _store.Presets;

    public ObservableCollection<TagRowVM> TagRows { get; } = new();

    public ObservableCollection<PersonRowVM> PersonRows { get; } = new();

    public ObservableCollection<LocationRowVM> LocationRows { get; } = new();

    private TagPageTab _selectedTagPageTab = TagPageTab.Tags;
    public TagPageTab SelectedTagPageTab
    {
        get => _selectedTagPageTab;
        set
        {
            if (!Set(ref _selectedTagPageTab, value)) return;
            _tagEntityInput = "";
            OnPropertyChanged(nameof(TagEntityInput));
            OnPropertyChanged(nameof(IsTagTabTags));
            OnPropertyChanged(nameof(IsTagTabPersons));
            OnPropertyChanged(nameof(IsTagTabLocations));
            OnPropertyChanged(nameof(TagPageBlurb));
            OnPropertyChanged(nameof(TagEntityPlaceholder));
            OnPropertyChanged(nameof(TagEntityButtonText));
        }
    }

    public bool IsTagTabTags => SelectedTagPageTab == TagPageTab.Tags;
    public bool IsTagTabPersons => SelectedTagPageTab == TagPageTab.Persons;
    public bool IsTagTabLocations => SelectedTagPageTab == TagPageTab.Locations;

    public string TagPageBlurb => SelectedTagPageTab switch
    {
        TagPageTab.Persons => "人物全库只有一份，改名直接同步到所有关联日程；点击卡片可按人物筛选日程。",
        TagPageTab.Locations => "地点全库只有一份，改名直接同步到所有关联日程；点击卡片可按地点筛选日程。",
        _ => "标签只在这里管理；概览页里只负责往卡片上贴。全库只有一份，改名直接作用到所有用到它的日程。"
    };

    private string _tagEntityInput = "";
    public string TagEntityInput
    {
        get => _tagEntityInput;
        set => Set(ref _tagEntityInput, value);
    }

    public string TagEntityPlaceholder => SelectedTagPageTab switch
    {
        TagPageTab.Persons => "新建人物，回车确认（比如「@张三」或「产品经理」）",
        TagPageTab.Locations => "新建地点，回车确认（比如「会议室A」或「图书馆」）",
        _ => "新建标签，回车确认（比如「#工作」或「阅读」）"
    };

    public string TagEntityButtonText => SelectedTagPageTab switch
    {
        TagPageTab.Persons => "新建人物",
        TagPageTab.Locations => "新建地点",
        _ => "新建标签"
    };

    public ObservableCollection<QuadrantVM> Quadrants { get; } = new();

    private bool _eisenhowerNextThreeDaysOnly = true;
    public bool EisenhowerNextThreeDaysOnly
    {
        get => _eisenhowerNextThreeDaysOnly;
        set
        {
            if (Set(ref _eisenhowerNextThreeDaysOnly, value))
                RebuildQuadrants();
        }
    }

    public ObservableCollection<DeadlineDayGroupVM> DeadlineGroups { get; } = new();

    public ObservableCollection<TaskVM> FutureTasks { get; } = new();

    private int _deadlineCount;
    public int DeadlineCount
    {
        get => _deadlineCount;
        private set => Set(ref _deadlineCount, value);
    }

    public int FutureTaskCount => FutureTasks.Count;

    private string _goalInput = "";
    public string GoalInput
    {
        get => _goalInput;
        set => Set(ref _goalInput, value);
    }

    public NavItemVM SelectedNav
    {
        get => _selectedNav;
        set
        {
            if (value is null) return;
            if (!Set(ref _selectedNav, value)) return;
            OnPropertyChanged(nameof(IsOverview));
            OnPropertyChanged(nameof(IsTimeline));
            OnPropertyChanged(nameof(IsGoals));
            OnPropertyChanged(nameof(IsTags));
            OnPropertyChanged(nameof(IsEisenhower));
            OnPropertyChanged(nameof(IsFocusPage));
            OnPropertyChanged(nameof(ShowTopFocusBar));
            OnPropertyChanged(nameof(IsDeadlines));
            OnPropertyChanged(nameof(IsFuture));
            OnPropertyChanged(nameof(IsOverduePage));
            OnPropertyChanged(nameof(IsPlaceholder));
            OnPropertyChanged(nameof(CurrentTitle));
            OnPropertyChanged(nameof(CurrentBlurb));
            if (value.Kind == PageKind.Timeline && _hasActiveView) EnsureTimelineReady();
        }
    }

    public bool IsOverview => SelectedNav.Kind == PageKind.Overview;

    public bool IsTimeline => SelectedNav.Kind == PageKind.Timeline;

    public bool IsGoals => SelectedNav.Kind == PageKind.Goals;

    public bool IsTags => SelectedNav.Kind == PageKind.Tags;

    public bool IsEisenhower => SelectedNav.Kind == PageKind.Eisenhower;

    public bool IsFocusPage => SelectedNav.Kind == PageKind.Focus;

    public bool ShowTopFocusBar => Focus.IsActive && !IsFocusPage;

    public bool IsDeadlines => SelectedNav.Kind == PageKind.Deadlines;

    public bool IsFuture => SelectedNav.Kind == PageKind.Future;

    public bool IsOverduePage => SelectedNav.Kind == PageKind.Overdue;

    public bool IsPlaceholder => SelectedNav.Kind is not (PageKind.Overview or PageKind.Timeline or PageKind.Tags or PageKind.Goals or PageKind.Eisenhower or PageKind.Focus or PageKind.Deadlines or PageKind.Future or PageKind.Overdue);

    public string CurrentTitle => SelectedNav.Title;

    public string CurrentBlurb => SelectedNav.Blurb;

    public string StatusLeft
    {
        get
        {
            var pending = _all.Count(t => !t.Done);
            var overdue = Lanes.First(l => l.Kind == LaneKind.Overdue).Items.Count;
            return overdue > 0
                ? $"{pending} 条待办 · {overdue} 条逾期"
                : $"{pending} 条待办";
        }
    }

    public string StatusRight => _file.Exists ? "已存本地" : "待首次保存";

    public string DataPath => StoreFile.DataPath;

    public string HistoryPath => CompletionLog.Path;

    /// <summary>立即落盘（关窗口 / 退出时调）。</summary>
    public void FlushSave() => _file.Flush(_store);

    private void LoadOrSeed()
    {
        if (_file.TryLoad(_store))
        {
            if (!_store.HasLoadedPresets) SeedDefaultPresets();
            return;
        }

        Seed();
    }

    // ---------------- 查询 ----------------

    public LaneVM LaneOfKind(LaneKind kind) => Lanes.First(l => l.Kind == kind);

    private static DateTime EndOfWeek()
    {
        var today = DateTime.Today;
        var offset = (7 - (int)today.DayOfWeek) % 7; // 周日 0 => 7 天后归零
        return today.AddDays(offset == 0 ? 7 : offset);
    }

    private static DateTime NextMonday()
    {
        var today = DateTime.Today;
        var offset = ((int)DayOfWeek.Monday - (int)today.DayOfWeek + 7) % 7;
        return today.AddDays(offset == 0 ? 7 : offset);
    }

    internal static LaneKind? LaneFor(TaskVM t)
    {
        // 无日期任务只在「未来事项」页显示，不再进入概览的「之后」档位
        if (t.Due is not { } due) return null;

        var today = DateTime.Today;
        var d = due.Date;

        // 过去日期已完成的日程留作历史（视图时间轴与完成日志可见），不再混入「今天」
        if (d < today) return t.Done ? null : LaneKind.Overdue;
        if (d == today)
        {
            if (!t.Done && DeadlineRules.IsOverdue(d, t.DueTime))
                return LaneKind.Overdue;
            return LaneKind.Today;
        }
        if (d == today.AddDays(1)) return LaneKind.Tomorrow;
        if (d <= EndOfWeek()) return LaneKind.ThisWeek;
        return LaneKind.Later;
    }

    private static DateTime? DueForLane(LaneKind kind)
    {
        var today = DateTime.Today;
        return kind switch
        {
            LaneKind.Overdue => today.AddDays(-1),
            LaneKind.Today => today,
            LaneKind.Tomorrow => today.AddDays(1),
            LaneKind.ThisWeek => EndOfWeek(),
            LaneKind.Later => NextMonday(),
            _ => today
        };
    }

    // ---------------- 重建档位 ----------------

    public void EnsureUiReady()
    {
        _hasActiveView = true;
        if (_uiDirty || _timelineBuiltDate != DateTime.Today ||
            !string.Equals(ComputeTodayTimedOverdueSnapshot(DateTime.Now), _lastOverdueSnapshot, StringComparison.Ordinal))
        {
            _uiDirty = false;
            Rebuild();
        }
        else if (IsTimeline && _timelineDirty)
        {
            EnsureTimelineReady();
        }
    }

    public void EnsureTimelineReady()
    {
        if (!_hasActiveView)
        {
            _timelineDirty = true;
            return;
        }

        if (_timelineDirty || TimelineDays.Count == 0 || _timelineBuiltDate != DateTime.Today)
        {
            _timelineDirty = false;
            RebuildTimeline();
        }
    }

    /// <summary>窗口销毁退回托盘时清空派生 UI 集合与时间轴画刷，只保留底层 _store。</summary>
    public void ReleaseUiCaches()
    {
        _hasActiveView = false;
        _uiDirty = true;
        _timelineDirty = true;

        foreach (var lane in Lanes) lane.Items.Clear();
        ActiveGoals.Clear();
        CompletedGoals.Clear();
        foreach (var goal in Goals) goal.Tasks.Clear();
        TagRows.Clear();
        PersonRows.Clear();
        LocationRows.Clear();
        foreach (var quadrant in Quadrants) quadrant.Items.Clear();
        DeadlineGroups.Clear();
        FutureTasks.Clear();
        FocusCandidateTasks.Clear();
        TagDetailDays.Clear();
        TimelineDays.Clear();
        TimelineHours.Clear();
        GridBrush = Brushes.Transparent;
        OnPropertyChanged(nameof(GridBrush));
    }

    public void Rebuild()
    {
        Focus.SyncWithSchedules();
        if (!_hasActiveView)
        {
            _uiDirty = true;
            _timelineDirty = true;
            _file.MarkDirty(_store);
            if (_hasInitialized) ScheduleAutoSync();
            var (pendingHeadless, overdueHeadless) = GetSummaryCounts();
            SummaryChanged?.Invoke(pendingHeadless, overdueHeadless);
            return;
        }

        _uiDirty = false;
        foreach (var lane in Lanes) lane.Items.Clear();

        foreach (var task in _all.OrderBy(t => t.Order))
        {
            if (LaneFor(task) is { } kind)
                LaneOfKind(kind).Items.Add(task);
        }

        SyncGoalGroups();

        foreach (var goal in Goals)
        {
            goal.Tasks.Clear();
            foreach (var task in _all.Where(t => t.HasGoalRef(goal)).OrderBy(t => t.Order))
                goal.Tasks.Add(task);

            goal.RootNode.TaskCount = goal.Total;
            goal.RootNode.DoneTaskCount = goal.DoneCount;

            foreach (var node in goal.Nodes)
            {
                var nodeTasks = _all.Where(t => t.GoalBindings.Any(b => ReferenceEquals(b.Goal, goal) && ReferenceEquals(b.Node, node))).ToList();
                node.TaskCount = nodeTasks.Count;
                node.DoneTaskCount = nodeTasks.Count(t => t.Done);
            }

            goal.Refresh();
        }

        foreach (var task in _all) task.NotifyGoalDisplayChanged();
        foreach (var preset in _store.Presets) preset.RefreshSummary();

        // 标签 / 人物 / 地点：按使用次数降序排列（0 次也保留列出，方便管理与清理）
        RebuildUsageRows(_store.Tags, _all.SelectMany(t => t.Tags), t => t.Name, TagRows, (t, n) => new TagRowVM(t, n));
        RebuildUsageRows(_store.Persons, _all.SelectMany(t => t.Persons), p => p.Name, PersonRows, (p, n) => new PersonRowVM(p, n));
        RebuildUsageRows(_store.Locations, _all.SelectMany(t => t.Locations), l => l.Name, LocationRows, (l, n) => new LocationRowVM(l, n));

        // 四象限：两个标记决定一格；两个都没打的落「没标记」
        RebuildQuadrants();

        NavItems.First(n => n.Kind == PageKind.Overdue).BadgeCount = OverdueLane.Count;

        RebuildDeadlines();
        RebuildFutureTasks();
        RebuildFocusCandidates();

        OnPropertyChanged(nameof(StatusLeft));
        OnPropertyChanged(nameof(StatusRight));

        _file.MarkDirty(_store);
        if (_hasInitialized) ScheduleAutoSync();

        if (IsTimeline)
        {
            _timelineDirty = false;
            RebuildTimeline();
        }
        else
        {
            _timelineDirty = true;
            if (TimelineDays.Count > 0) TimelineDays.Clear();
            if (TimelineHours.Count > 0) TimelineHours.Clear();
            GridBrush = Brushes.Transparent;
        }

        if (IsTagDetailOpen) RebuildTagDetail();

        var (todayPending, overdueCount) = GetSummaryCounts();
        SummaryChanged?.Invoke(todayPending, overdueCount);
        _lastOverdueSnapshot = ComputeTodayTimedOverdueSnapshot(DateTime.Now);
    }

    private void RebuildQuadrants()
    {
        foreach (var quadrant in Quadrants) quadrant.Items.Clear();
        var today = DateTime.Today;
        var windowEnd = today.AddDays(2);
        foreach (var task in _all.OrderBy(t => t.Order))
        {
            if (task.Done) continue;
            if (_eisenhowerNextThreeDaysOnly)
            {
                if (!task.Due.HasValue) continue;
                var d = task.Due.Value.Date;
                if (d < today || d > windowEnd) continue;
            }
            var quadrant = Quadrants.First(q => q.IsUrgent == task.IsUrgent && q.IsImportant == task.IsImportant);
            quadrant.Items.Add(task);
        }
        foreach (var quadrant in Quadrants) quadrant.RaiseCount();
    }

    private void RebuildDeadlines()
    {
        var byDate = new SortedDictionary<DateTime, DeadlineDayGroupVM>();

        DeadlineDayGroupVM GroupFor(DateTime date)
        {
            var key = date.Date;
            if (!byDate.TryGetValue(key, out var group))
            {
                group = new DeadlineDayGroupVM(key);
                byDate[key] = group;
            }
            return group;
        }

        foreach (var goal in Goals)
        {
            if (goal.IsCompleted) continue;

            if (goal.Due is { } goalDue)
                GroupFor(goalDue).Goals.Add(new DeadlineGoalItemVM(goal, node: null, goalDue, goal.DueTime));

            foreach (var node in goal.Nodes)
            {
                if (!node.IsCompleted && node.Due is { } nodeDue)
                    GroupFor(nodeDue).Goals.Add(new DeadlineGoalItemVM(goal, node, nodeDue, node.DueTime));
            }
        }

        foreach (var task in _all.OrderBy(t => t.DueTime ?? TimeSpan.MaxValue).ThenBy(t => t.Order))
        {
            if (task.Done || task.Due is not { } due || task.HasTime) continue;
            GroupFor(due).Tasks.Add(task);
        }

        DeadlineGroups.Clear();
        var total = 0;
        foreach (var group in byDate.Values)
        {
            if (group.Goals.Count > 1)
            {
                var sortedGoals = group.Goals.OrderBy(g => g.DueTime ?? TimeSpan.MaxValue).ToList();
                group.Goals.Clear();
                foreach (var g in sortedGoals) group.Goals.Add(g);
            }
            DeadlineGroups.Add(group);
            total += group.TotalCount;
        }

        DeadlineCount = total;
    }

    private void RebuildFutureTasks()
    {
        FutureTasks.Clear();
        foreach (var task in _all.Where(t => t.Due is null).OrderBy(t => t.Done).ThenBy(t => t.Order))
            FutureTasks.Add(task);

        OnPropertyChanged(nameof(FutureTaskCount));
    }

    private void RebuildFocusCandidates()
    {
        var today = DateTime.Today;
        var now = DateTime.Now.TimeOfDay;

        var candidates = _all
            .Where(t => !t.Done && t.Due?.Date == today)
            .OrderBy(t =>
            {
                if (t.StartTime is { } s && t.EndTime is { } e && now >= s && now < e) return 0;
                if (t.HasTime) return 1;
                return 2;
            })
            .ThenBy(t => t.StartTime ?? TimeSpan.MaxValue)
            .ThenBy(t => t.Order)
            .ToList();

        FocusCandidateTasks.Clear();
        foreach (var task in candidates)
            FocusCandidateTasks.Add(task);
    }

    public void NavigateToFocusPage()
        => SelectedNav = NavItems.First(n => n.Kind == PageKind.Focus);

    private void SyncGoalGroups()
    {
        var desiredActive = Goals.Where(g => !g.IsCompleted).ToList();
        var desiredCompleted = Goals.Where(g => g.IsCompleted).ToList();

        SyncGoalCollection(ActiveGoals, desiredActive);
        SyncGoalCollection(CompletedGoals, desiredCompleted);
        OnPropertyChanged(nameof(HasActiveGoals));
        OnPropertyChanged(nameof(HasCompletedGoals));
    }

    private static void SyncGoalCollection(ObservableCollection<GoalVM> target, IList<GoalVM> desired)
    {
        if (target.Count == desired.Count && target.SequenceEqual(desired)) return;
        target.Clear();
        foreach (var item in desired) target.Add(item);
    }

    // ---------------- 操作 ----------------

    /// <summary>拖拽落点：跨档位时改 due；同档位内只改顺序，保留原日期。</summary>
    public void MoveTask(TaskVM task, LaneVM target, int index)
    {
        var before = BeginUndoSnapshot();
        var crossLane = LaneFor(task) != target.Kind;
        _all.Remove(task);

        if (crossLane)
        {
            var nextDue = DueForLane(target.Kind);
            task.Due = nextDue;
            if (target.Kind == LaneKind.Today && task.DueTime is { } dt && DateTime.Now.TimeOfDay >= dt)
                task.DueTime = null;
        }

        var ordered = _all.Where(t => LaneFor(t) == target.Kind)
                          .OrderBy(t => t.Order)
                          .ToList();

        index = Math.Clamp(index, 0, ordered.Count);
        ordered.Insert(index, task);

        for (var i = 0; i < ordered.Count; i++) ordered[i].Order = (i + 1) * 1024;

        _all.Add(task);
        Rebuild();
        CommitUndoSnapshot(crossLane ? $"移动「{task.Title}」到{target.Title}" : $"调整「{task.Title}」顺序", before);
        TaskTouched?.Invoke(task);
        if (crossLane) Notify($"已移到「{target.Title}」");
    }

    public void ToggleExpand(TaskVM task)
    {
        var expand = !task.IsExpanded;
        foreach (var t in _all) t.IsExpanded = false;
        task.IsExpanded = expand;
    }

    public void CollapseAll()
    {
        foreach (var t in _all) t.IsExpanded = false;
        foreach (var g in Goals) g.IsExpanded = false;
    }

    public void ToggleDone(TaskVM task)
    {
        var before = BeginUndoSnapshot();
        var rule = RepeatRules.Normalize(task.Repeat);
        if (!task.Done && rule.Length > 0)
        {
            var recordedForDate = task.Due?.Date;
            _history.Append(task.Id, recordedForDate, done: true);

            if (RepeatRules.NextOccurrence(task.Due, rule) is { } nextDue)
            {
                task.Due = nextDue;
                task.Done = false;

                var shiftedHint = "";
                if (task.StartTime is { } start && task.EndTime is { } end)
                {
                    var startMin = (int)start.TotalMinutes;
                    var duration = Math.Max(5, (int)(end - start).TotalMinutes);
                    var freeStart = FindFreeStart(nextDue, startMin, duration, exclude: task);
                    if (freeStart != startMin)
                    {
                        task.StartTime = TimeSpan.FromMinutes(freeStart);
                        task.EndTime = TimeSpan.FromMinutes(freeStart + duration);
                        shiftedHint = $"（时段冲突已顺延至 {task.TimeLabel}）";
                    }
                }

                Rebuild();
                CommitUndoSnapshot($"完成重复日程「{task.Title}」", before, [new CompletionUndoEvent(task.Id, recordedForDate, true)]);
                TaskTouched?.Invoke(task);
                Notify($"已完成本次「{task.Title}」，已推进至 {nextDue:M月d日}{shiftedHint}");
                return;
            }
        }

        var forDate = task.Due?.Date;
        task.Done = !task.Done;

        // 完成记录单独追加一行，不进主文件（重复日程的历史会把它撞爆）
        _history.Append(task.Id, forDate, task.Done);

        Rebuild();
        CommitUndoSnapshot(task.Done ? $"完成「{task.Title}」" : $"恢复待办「{task.Title}」", before, [new CompletionUndoEvent(task.Id, forDate, task.Done)]);
        Notify(task.Done ? $"已完成「{task.Title}」" : $"已恢复待办「{task.Title}」");
    }

    public void Delete(TaskVM task)
    {
        var title = task.Title;
        RecordUndo($"删除「{title}」", () =>
        {
            _store.RemoveSchedule(task);
            Rebuild();
        });
        Notify($"已删除「{title}」");
    }

    /// <summary>右键菜单：只改日子，不动时段。</summary>
    public void MoveToDay(TaskVM task, DateTime date)
    {
        var label = date.Date == DateTime.Today
            ? "今天"
            : date.Date == DateTime.Today.AddDays(1)
                ? "明天"
                : $"{date:M月d日}";

        RecordUndo($"移到{label}「{task.Title}」", () =>
        {
            task.Due = date.Date;
            if (date.Date == DateTime.Today && task.DueTime is { } dt && DateTime.Now.TimeOfDay >= dt)
                task.DueTime = null;
            Rebuild();
        });
        TaskTouched?.Invoke(task);
        Notify($"已移到{label}");
    }

    public void MoveToFuture(TaskVM task)
    {
        RecordUndo($"移到未来事项「{task.Title}」", () =>
        {
            task.Due = null;
            task.DueTime = null;
            task.StartTime = null;
            task.EndTime = null;
            Rebuild();
        });
        TaskTouched?.Invoke(task);
        Notify("已移到「未来事项」");
    }

    public void CycleRepeat(TaskVM task)
    {
        RecordUndo($"切换重复「{task.Title}」", () =>
        {
            task.Repeat = RepeatRules.Normalize(task.Repeat) switch
            {
                "" => RepeatRules.Daily,
                RepeatRules.Daily => RepeatRules.Weekly,
                RepeatRules.Weekly => RepeatRules.Monthly,
                _ => ""
            };
            _file.MarkDirty(_store);
            if (_hasInitialized) ScheduleAutoSync();
        });
    }

    public void ToggleTaskRemindNode(TaskVM task, string node)
    {
        var next = RemindRules.ToggleNode(task.Remind, node);
        if (task.Remind == next) return;

        RecordUndo($"调整提醒「{task.Title}」", () =>
        {
            task.Remind = next;
            _file.MarkDirty(_store);
            if (_hasInitialized) ScheduleAutoSync();
        });
        TaskTouched?.Invoke(task);
    }

    private static void RebuildUsageRows<TEntity, TRow>(
        IEnumerable<TEntity> entities,
        IEnumerable<TEntity> usages,
        Func<TEntity, string> getName,
        ObservableCollection<TRow> target,
        Func<TEntity, int, TRow> createRow)
        where TEntity : notnull
    {
        target.Clear();
        var counts = usages.GroupBy(x => x).ToDictionary(g => g.Key, g => g.Count());
        foreach (var entity in entities
                     .OrderByDescending(e => counts.TryGetValue(e, out var n) ? n : 0)
                     .ThenBy(getName))
        {
            target.Add(createRow(entity, counts.TryGetValue(entity, out var c) ? c : 0));
        }
    }

    public void AddTag(TaskVM task, string tag)
    {
        RecordUndo($"添加标签「{tag}」", () =>
        {
            var entity = _store.GetOrCreateTag(tag);
            if (entity is null) return;
            if (!task.Tags.Contains(entity)) task.Tags.Add(entity);
            Rebuild();
        });
    }

    public void RemoveTag(TaskVM task, TagVM tag)
    {
        RecordUndo($"移除标签「#{tag.Name}」", () =>
        {
            task.Tags.Remove(tag);
            Rebuild();
        });
    }

    public void SetTagColor(TagVM tag, string hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return;
        if (string.Equals(tag.ColorHex, hex, StringComparison.OrdinalIgnoreCase)) return;

        RecordUndo($"更改标签「#{tag.Name}」颜色", () =>
        {
            tag.Color = DayStore.BrushFromHex(hex);
            Rebuild();
        });
    }

    public void AddPerson(TaskVM task, string person)
    {
        RecordUndo($"关联人物「{person}」", () =>
        {
            var entity = _store.GetOrCreatePerson(person);
            if (entity is null) return;
            if (!task.Persons.Contains(entity)) task.Persons.Add(entity);
            Rebuild();
        });
    }

    public void RemovePerson(TaskVM task, PersonVM person)
    {
        RecordUndo($"移除人物「@{person.Name}」", () =>
        {
            task.Persons.Remove(person);
            Rebuild();
        });
    }

    public void AddLocation(TaskVM task, string location)
    {
        RecordUndo($"关联地点「{location}」", () =>
        {
            var entity = _store.GetOrCreateLocation(location);
            if (entity is null) return;
            if (!task.Locations.Contains(entity)) task.Locations.Add(entity);
            Rebuild();
        });
    }

    public void RemoveLocation(TaskVM task, LocationVM location)
    {
        RecordUndo($"移除地点「{location.Name}」", () =>
        {
            task.Locations.Remove(location);
            Rebuild();
        });
    }

    // ---------------- 标签 / 人物 / 地点（页） ----------------

    public void SubmitTagEntityInput()
    {
        var raw = (TagEntityInput ?? "").Trim();
        if (raw.Length == 0) return;

        switch (SelectedTagPageTab)
        {
            case TagPageTab.Persons:
            {
                var name = raw.TrimStart('@').Trim();
                if (name.Length == 0) return;
                if (_store.FindPerson(name) is not null)
                {
                    Notify($"人物「@{name}」已存在");
                    TagEntityInput = "";
                    return;
                }
                RecordUndo($"新建人物「@{name}」", () =>
                {
                    _store.GetOrCreatePerson(name);
                    TagEntityInput = "";
                    Rebuild();
                });
                Notify($"已新建人物「@{name}」");
                break;
            }
            case TagPageTab.Locations:
            {
                var name = raw;
                if (_store.FindLocation(name) is not null)
                {
                    Notify($"地点「{name}」已存在");
                    TagEntityInput = "";
                    return;
                }
                RecordUndo($"新建地点「{name}」", () =>
                {
                    _store.GetOrCreateLocation(name);
                    TagEntityInput = "";
                    Rebuild();
                });
                Notify($"已新建地点「{name}」");
                break;
            }
            default:
            {
                var name = raw.TrimStart('#').Trim();
                if (name.Length == 0) return;
                if (_store.FindTag(name) is not null)
                {
                    Notify($"标签「#{name}」已存在");
                    TagEntityInput = "";
                    return;
                }
                RecordUndo($"新建标签「#{name}」", () =>
                {
                    _store.GetOrCreateTag(name);
                    TagEntityInput = "";
                    Rebuild();
                });
                Notify($"已新建标签「#{name}」");
                break;
            }
        }
    }

    /// <summary>改名只改实体本身；日程里存的是引用，监听已经处理好了。</summary>
    public void RenameTag(TagVM tag, string name)
    {
        var trimmed = (name ?? "").Trim().TrimStart('#');
        if (trimmed.Length == 0 || trimmed == tag.Name) return;

        RecordUndo($"重命名标签「#{trimmed}」", () =>
        {
            tag.Name = trimmed;
            OnPropertyChanged(nameof(TagRows));
            OnEntityRenamed();
        });
    }

    public void DeleteTag(TagVM tag)
    {
        var name = tag.Name;
        RecordUndo($"删除标签「#{name}」", () =>
        {
            _store.RemoveTag(tag);
            Rebuild();
        });
        Notify($"已移除标签「#{name}」");
    }

    public void RenamePerson(PersonVM person, string name)
    {
        var trimmed = (name ?? "").Trim().TrimStart('@');
        if (trimmed.Length == 0 || trimmed == person.Name) return;

        RecordUndo($"重命名人物「@{trimmed}」", () =>
        {
            person.Name = trimmed;
            OnPropertyChanged(nameof(PersonRows));
            OnEntityRenamed();
        });
    }

    public void DeletePerson(PersonVM person)
    {
        var name = person.Name;
        RecordUndo($"删除人物「@{name}」", () =>
        {
            _store.RemovePerson(person);
            Rebuild();
        });
        Notify($"已移除人物「{name}」");
    }

    public void RenameLocation(LocationVM location, string name)
    {
        var trimmed = (name ?? "").Trim();
        if (trimmed.Length == 0 || trimmed == location.Name) return;

        RecordUndo($"重命名地点「{trimmed}」", () =>
        {
            location.Name = trimmed;
            OnPropertyChanged(nameof(LocationRows));
            OnEntityRenamed();
        });
    }

    public void DeleteLocation(LocationVM location)
    {
        var name = location.Name;
        RecordUndo($"删除地点「{name}」", () =>
        {
            _store.RemoveLocation(location);
            Rebuild();
        });
        Notify($"已移除地点「{name}」");
    }

    public void OnEntityRenamed()
    {
        _file.MarkDirty(_store);
        if (_hasInitialized) ScheduleAutoSync();
        if (_hasActiveView && IsTimeline)
        {
            _timelineDirty = false;
            RebuildTimeline();
        }
        else
        {
            _timelineDirty = true;
        }
    }

    public void OpenTagDetail(TagVM tag)
    {
        _selectedGoalNodeDetail = null;
        SelectedGoalDetail = null;
        SelectedPersonDetail = null;
        SelectedLocationDetail = null;
        SelectedTagDetail = tag;
        RebuildTagDetail();
        IsTagDetailOpen = true;
    }

    public void OpenPersonDetail(PersonVM person)
    {
        _selectedGoalNodeDetail = null;
        SelectedGoalDetail = null;
        SelectedTagDetail = null;
        SelectedLocationDetail = null;
        SelectedPersonDetail = person;
        RebuildTagDetail();
        IsTagDetailOpen = true;
    }

    public void OpenLocationDetail(LocationVM location)
    {
        _selectedGoalNodeDetail = null;
        SelectedGoalDetail = null;
        SelectedTagDetail = null;
        SelectedPersonDetail = null;
        SelectedLocationDetail = location;
        RebuildTagDetail();
        IsTagDetailOpen = true;
    }

    public void OpenGoalDetail(GoalVM goal)
    {
        SelectedTagDetail = null;
        SelectedPersonDetail = null;
        SelectedLocationDetail = null;
        _selectedGoalNodeDetail = null;
        SelectedGoalDetail = goal;
        RebuildTagDetail();
        IsTagDetailOpen = true;
    }

    public void OpenGoalNodeDetail(GoalVM goal, GoalNodeVM node)
    {
        SelectedTagDetail = null;
        SelectedPersonDetail = null;
        SelectedLocationDetail = null;
        _selectedGoalNodeDetail = node.IsRoot ? null : node;
        SelectedGoalDetail = goal;
        RebuildTagDetail();
        IsTagDetailOpen = true;
    }

    public void CloseTagDetail() => IsTagDetailOpen = false;

    public void RebuildTagDetail()
    {
        TagDetailDays.Clear();

        List<TaskVM> detailTasks;
        if (SelectedGoalDetail is not null)
        {
            if (!Goals.Contains(SelectedGoalDetail))
            {
                _tagDetailTodayIndex = 0;
                _selectedGoalNodeDetail = null;
                SelectedGoalDetail = null;
                IsTagDetailOpen = false;
                RaiseDetailHeaderProperties();
                return;
            }

            if (_selectedGoalNodeDetail is { IsRoot: false } sub && SelectedGoalDetail.Nodes.Contains(sub))
            {
                var ids = SelectedGoalDetail.CollectSubtreeIds(sub);
                detailTasks = _all.Where(t => t.HasGoalSubtreeRef(SelectedGoalDetail, ids)).ToList();
            }
            else
            {
                _selectedGoalNodeDetail = null;
                detailTasks = _all.Where(t => t.HasGoalRef(SelectedGoalDetail)).ToList();
            }
        }
        else if (SelectedTagDetail is not null && _store.Tags.Contains(SelectedTagDetail))
        {
            detailTasks = _all.Where(t => t.Tags.Contains(SelectedTagDetail)).ToList();
        }
        else if (SelectedPersonDetail is not null && _store.Persons.Contains(SelectedPersonDetail))
        {
            detailTasks = _all.Where(t => t.Persons.Contains(SelectedPersonDetail)).ToList();
        }
        else if (SelectedLocationDetail is not null && _store.Locations.Contains(SelectedLocationDetail))
        {
            detailTasks = _all.Where(t => t.Locations.Contains(SelectedLocationDetail)).ToList();
        }
        else
        {
            _tagDetailTodayIndex = 0;
            _selectedGoalNodeDetail = null;
            SelectedTagDetail = null;
            SelectedPersonDetail = null;
            SelectedLocationDetail = null;
            SelectedGoalDetail = null;
            IsTagDetailOpen = false;
            RaiseDetailHeaderProperties();
            return;
        }

        var baseStart = TimelineStart();
        var baseEnd = baseStart.AddDays(DayCount - 1);

        var minDate = baseStart;
        var maxDate = baseEnd;

        foreach (var task in detailTasks)
        {
            if (task.Due is not { } due) continue;
            var d = due.Date;
            if (d < minDate) minDate = d;
            if (d > maxDate) maxDate = d;
        }

        var today = DateTime.Today;
        var byDate = detailTasks
            .GroupBy(t => t.Due?.Date ?? today)
            .ToDictionary(
                g => g.Key,
                g => g.OrderBy(t => t.StartTime ?? TimeSpan.MaxValue)
                      .ThenBy(t => t.EndTime ?? TimeSpan.MaxValue)
                      .ThenBy(t => t.Order)
                      .ToList());

        _tagDetailTodayIndex = 0;
        var index = 0;

        for (var d = minDate; d <= maxDate; d = d.AddDays(1))
        {
            if (d == today) _tagDetailTodayIndex = index;

            var col = new TagDayColumnVM(d);
            if (byDate.TryGetValue(d, out var list))
                foreach (var task in list) col.Items.Add(task);

            TagDetailDays.Add(col);
            index++;
        }

        RaiseDetailHeaderProperties();
    }

    // ---------------- 四象限 ----------------

    public void ToggleUrgent(TaskVM task)
    {
        RecordUndo($"标记紧急「{task.Title}」", () =>
        {
            task.IsUrgent = !task.IsUrgent;
            Rebuild();
        });
    }

    public void ToggleImportant(TaskVM task)
    {
        RecordUndo($"标记重要「{task.Title}」", () =>
        {
            task.IsImportant = !task.IsImportant;
            Rebuild();
        });
    }

    /// <summary>拖到哪一格就把两个标记设成那格。</summary>
    public void SetQuadrant(TaskVM task, QuadrantVM quadrant)
    {
        if (task.IsUrgent == quadrant.IsUrgent && task.IsImportant == quadrant.IsImportant) return;

        RecordUndo($"移到「{quadrant.Title}」", () =>
        {
            task.IsUrgent = quadrant.IsUrgent;
            task.IsImportant = quadrant.IsImportant;
            Rebuild();
        });
        TaskTouched?.Invoke(task);
        Notify($"已移到「{quadrant.Title}」");
    }

    // ---------------- 时间 ----------------

    /// <summary>概览卡片里手打时间：同样不允许跟别人撞，撞了就落到最近的空档。</summary>
    public void SetTime(TaskVM task, TimeSpan start, TimeSpan end)
    {
        if (end <= start) end = start.Add(TimeSpan.FromMinutes(30));

        // 有时间就必须有日期，否则它在时间轴上没地方放
        var date = task.Due?.Date ?? DateTime.Today;
        var duration = Math.Max(1, (int)(end - start).TotalMinutes);
        var free = FindFreeStart(date, (int)start.TotalMinutes, duration, task);

        var newStart = TimeSpan.FromMinutes(free);
        var newEnd = TimeSpan.FromMinutes(free + duration);
        var changed = task.Due?.Date != date || task.StartTime != newStart || task.EndTime != newEnd;

        RecordUndo($"修改时间「{task.Title}」", () =>
        {
            task.Due = date;
            task.StartTime = newStart;
            task.EndTime = newEnd;
            Rebuild();
        });
        if (changed) TaskTouched?.Invoke(task);

        if (free != (int)start.TotalMinutes)
            Notify($"已避开冲突，调整到 {Clock(free)}–{Clock(free + duration)}", highlight: true);
        else
            Notify($"时间已设为 {Clock(free)}–{Clock(free + duration)}");
    }

    public void ClearTime(TaskVM task)
    {
        RecordUndo($"清除时间「{task.Title}」", () =>
        {
            task.StartTime = null;
            task.EndTime = null;
            Rebuild();
        });
    }

    // ---------------- 视图（时间轴） ----------------

    /// <summary>缩放档：标签、每小时像素、刻度间隔（分钟）。</summary>
    private static readonly (string Label, double PxPerHour, int Interval)[] Zooms =
    {
        ("1 时", 44, 60),
        ("30 分", 88, 30),
        ("15 分", 176, 15),
        ("5 分", 528, 5),
        ("1 分", 1584, 1)
    };

    public const double DayWidth = 160;
    public const int DayCount = 42;
    public const double MinPxPerHour = 28;
    public const double MaxPxPerHour = 1800;
    private const int DaysBeforeToday = 14;

    private int _zoomIndex = 1;
    private double _pxPerHour = 88;
    private int _tickStepMinutes = 30;
    private int _currentMinutes = DateTime.Now.Hour * 60 + DateTime.Now.Minute;
    private DateTime _timelineBuiltDate = DateTime.Today;

    public ObservableCollection<TimelineDayVM> TimelineDays { get; } = new();

    public ObservableCollection<TimelineHourVM> TimelineHours { get; } = new();

    public ObservableCollection<ZoomOptionVM> ZoomOptions { get; }

    public double TimelineWidth => DayWidth * DayCount;

    public double PxPerHourValue => _pxPerHour;

    public int IntervalMinutes => Zooms[_zoomIndex].Interval;

    public double GridHeight => 24 * PxPerHourValue;

    public int TodayIndex => (DateTime.Today - TimelineStart()).Days;

    public int CurrentMinutes => _currentMinutes;

    private const double NowBadgeHeight = 18;
    private const double NowDotSize = 7;

    public double NowLineTop => _currentMinutes / 60.0 * PxPerHourValue;

    public double NowBadgeTop => Math.Clamp(NowLineTop - NowBadgeHeight / 2.0, 0, Math.Max(0, GridHeight - NowBadgeHeight));

    public double NowDotTop => Math.Clamp(NowLineTop - 3.0, 0, Math.Max(0, GridHeight - NowDotSize));

    public string NowTimeText => Clock(_currentMinutes);

    public double NowTodayLeft => TodayIndex * DayWidth;

    public Brush GridBrush { get; private set; } = Brushes.Transparent;

    public void RefreshCurrentTime()
    {
        var now = DateTime.Now;
        var mins = now.Hour * 60 + now.Minute;

        if (now.Date != _timelineBuiltDate)
        {
            _currentMinutes = mins;
            Rebuild();
            OnPropertyChanged(nameof(CurrentMinutes));
            OnPropertyChanged(nameof(NowLineTop));
            OnPropertyChanged(nameof(NowBadgeTop));
            OnPropertyChanged(nameof(NowDotTop));
            OnPropertyChanged(nameof(NowTimeText));
            return;
        }

        if (mins == _currentMinutes) return;

        _currentMinutes = mins;
        UpdateTimelineTickOverlap();
        OnPropertyChanged(nameof(CurrentMinutes));
        OnPropertyChanged(nameof(NowLineTop));
        OnPropertyChanged(nameof(NowBadgeTop));
        OnPropertyChanged(nameof(NowDotTop));
        OnPropertyChanged(nameof(NowTimeText));
        RefreshOverdueIfNeeded();
    }

    private string ComputeTodayTimedOverdueSnapshot(DateTime now)
    {
        var today = now.Date;
        var parts = new List<string>();
        foreach (var t in _all)
        {
            if (!t.Done && t.Due?.Date == today && t.DueTime is { } dt && now.TimeOfDay >= dt)
                parts.Add("T:" + t.Id);
        }
        foreach (var g in Goals)
        {
            if (!g.IsCompleted && g.Due?.Date == today && g.DueTime is { } gt && now.TimeOfDay >= gt)
                parts.Add("G:" + g.Id);
            foreach (var n in g.Nodes)
            {
                if (!n.IsCompleted && n.Due?.Date == today && n.DueTime is { } nt && now.TimeOfDay >= nt)
                    parts.Add("N:" + g.Id + ":" + n.Id);
            }
        }
        parts.Sort(StringComparer.Ordinal);
        return string.Join("|", parts);
    }

    public void RefreshOverdueIfNeeded()
    {
        var now = DateTime.Now;
        var snapshot = ComputeTodayTimedOverdueSnapshot(now);
        if (string.Equals(snapshot, _lastOverdueSnapshot, StringComparison.Ordinal))
            return;

        _lastOverdueSnapshot = snapshot;
        Rebuild();
    }

    /// <summary>时间轴的第一天：本周一往前推两周，保证今天左右都有地方可滚。</summary>
    private static DateTime TimelineStart()
    {
        var today = DateTime.Today;
        var toMonday = ((int)DayOfWeek.Monday - (int)today.DayOfWeek + 7) % 7;
        return today.AddDays(-toMonday - DaysBeforeToday);
    }

    public double PresetPxPerHour(int index)
        => index >= 0 && index < Zooms.Length ? Zooms[index].PxPerHour : PxPerHourValue;

    private static int ZoomIndexForPxPerHour(double pxPerHour) => pxPerHour switch
    {
        < 66 => 0,
        < 132 => 1,
        < 352 => 2,
        < 1000 => 3,
        _ => 4
    };

    public void SetZoom(int index)
    {
        if (index < 0 || index >= Zooms.Length) return;
        SetPxPerHour(Zooms[index].PxPerHour);
    }

    /// <summary>连续缩放 24h 纵轴，并按当前像素密度自动切换刻度粒度与档位高亮（不重建视图树）。</summary>
    public void SetPxPerHour(double pxPerHour)
    {
        var clamped = Math.Clamp(pxPerHour, MinPxPerHour, MaxPxPerHour);
        _pxPerHour = clamped;
        _zoomIndex = ZoomIndexForPxPerHour(clamped);

        if (TimelineDays.Count == 0 || TimelineHours.Count == 0)
        {
            RebuildTimeline();
            return;
        }

        UpdateTimelineScale();
    }

    /// <summary>主题切换后重画网格线（画刷里带颜色，不重建就还是旧主题）。</summary>
    public void RefreshTimelineBrushes()
    {
        if (_hasActiveView && IsTimeline)
        {
            _timelineDirty = false;
            RebuildTimeline();
        }
        else
        {
            _timelineDirty = true;
        }
    }

    private int TickStepMinutes()
    {
        var interval = IntervalMinutes;
        if (interval == 1 && PxPerHourValue < 1140) return 5;
        return Math.Max(1, interval);
    }

    private void UpdateTimelineTickOverlap()
    {
        var badgeTop = NowBadgeTop;
        var badgeBottom = badgeTop + NowBadgeHeight;
        var pxPerHour = PxPerHourValue;

        foreach (var hour in TimelineHours)
        {
            var tickTop = hour.Minutes / 60.0 * pxPerHour;
            var tickTextHeight = hour.IsHour ? 17.0 : 15.0;
            hour.IsCoveredByNow = tickTop < badgeBottom + 1.0 && tickTop + tickTextHeight > badgeTop - 1.0;
        }
    }

    private void RebuildTimelineHours(double pxPerHour, int step)
    {
        _tickStepMinutes = step;
        var slotHeight = pxPerHour * step / 60.0;
        TimelineHours.Clear();
        for (var m = 0; m < 24 * 60; m += step)
            TimelineHours.Add(new TimelineHourVM(m, Clock(m), slotHeight, isHour: m % 60 == 0));

        UpdateTimelineTickOverlap();
    }

    public void RebuildTimeline()
    {
        _timelineBuiltDate = DateTime.Today;
        var pxPerHour = PxPerHourValue;
        var intervalPx = pxPerHour * IntervalMinutes / 60.0;
        GridBrush = CreateGridBrush(intervalPx);

        RebuildTimelineHours(pxPerHour, TickStepMinutes());

        foreach (var option in ZoomOptions) option.IsCurrent = option.Index == _zoomIndex;

        TimelineDays.Clear();
        var start = TimelineStart();

        for (var i = 0; i < DayCount; i++)
        {
            var date = start.AddDays(i);
            var day = new TimelineDayVM(date, GridBrush);

            var blocks = _all
                .Where(t => t.Due is { } due && due.Date == date && t.StartTime is not null)
                .Select(t =>
                {
                    var startMin = t.StartTime!.Value.TotalMinutes;
                    var endMin = t.EndTime?.TotalMinutes ?? startMin + 30;
                    if (endMin <= startMin) endMin = startMin + 30;

                    return new TimelineBlockVM(t,
                        startMin / 60.0 * pxPerHour,
                        Math.Max(22, (endMin - startMin) / 60.0 * pxPerHour));
                })
                .OrderBy(b => b.Top);

            foreach (var block in blocks) day.Blocks.Add(block);
            LayoutOverlaps(day.Blocks);
            TimelineDays.Add(day);
        }

        OnPropertyChanged(nameof(GridHeight));
        OnPropertyChanged(nameof(GridBrush));
        OnPropertyChanged(nameof(PxPerHourValue));
        OnPropertyChanged(nameof(NowLineTop));
        OnPropertyChanged(nameof(NowBadgeTop));
        OnPropertyChanged(nameof(NowDotTop));
        OnPropertyChanged(nameof(NowTodayLeft));
    }

    private void UpdateTimelineScale()
    {
        var pxPerHour = PxPerHourValue;
        var intervalPx = pxPerHour * IntervalMinutes / 60.0;
        GridBrush = CreateGridBrush(intervalPx);

        foreach (var option in ZoomOptions) option.IsCurrent = option.Index == _zoomIndex;

        var step = TickStepMinutes();
        if (step != _tickStepMinutes)
        {
            RebuildTimelineHours(pxPerHour, step);
        }
        else
        {
            var slotHeight = pxPerHour * step / 60.0;
            foreach (var hour in TimelineHours) hour.Height = slotHeight;
            UpdateTimelineTickOverlap();
        }

        foreach (var day in TimelineDays)
        {
            day.GridBrush = GridBrush;
            foreach (var block in day.Blocks)
            {
                var startMin = block.Task.StartTime?.TotalMinutes ?? 0;
                var endMin = block.Task.EndTime?.TotalMinutes ?? startMin + 30;
                if (endMin <= startMin) endMin = startMin + 30;

                block.Top = startMin / 60.0 * pxPerHour;
                block.Height = Math.Max(22, (endMin - startMin) / 60.0 * pxPerHour);
            }

            LayoutOverlaps(day.Blocks);
        }

        OnPropertyChanged(nameof(GridHeight));
        OnPropertyChanged(nameof(GridBrush));
        OnPropertyChanged(nameof(PxPerHourValue));
        OnPropertyChanged(nameof(NowLineTop));
        OnPropertyChanged(nameof(NowBadgeTop));
        OnPropertyChanged(nameof(NowDotTop));
    }

    private static DrawingBrush CreateGridBrush(double intervalPx)
    {
        var lineBrush = (Application.Current?.TryFindResource("BorderSubtle") as Brush) ?? Brushes.Gray;

        const double tileWidth = 32;
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(Brushes.Transparent, null,
            new RectangleGeometry(new Rect(0, 0, tileWidth, intervalPx))));
        group.Children.Add(new GeometryDrawing(null, new Pen(lineBrush, 1),
            new LineGeometry(new Point(0, 0.5), new Point(tileWidth, 0.5))));
        group.Freeze();

        var brush = new DrawingBrush(group)
        {
            TileMode = TileMode.Tile,
            Stretch = Stretch.None,
            Viewbox = new Rect(0, 0, tileWidth, intervalPx),
            ViewboxUnits = BrushMappingMode.Absolute,
            Viewport = new Rect(0, 0, tileWidth, intervalPx),
            ViewportUnits = BrushMappingMode.Absolute
        };
        brush.Freeze();
        return brush;
    }

    // ---------------- 不透叠：人不可能同一时间干两件事 ----------------

    /// <summary>某天已经占住时间段的日程（排除自己）。</summary>
    private IEnumerable<(int Start, int End)> BusyRanges(DateTime date, TaskVM? exclude)
    {
        foreach (var task in _all)
        {
            if (ReferenceEquals(task, exclude)) continue;
            if (task.StartTime is not { } start) continue;
            if (task.Due is not { } due || due.Date != date.Date) continue;

            var from = (int)start.TotalMinutes;
            var to = (int)(task.EndTime?.TotalMinutes ?? from + 30);
            if (to <= from) to = from + 30;

            yield return (from, to);
        }
    }

    private bool IsFree(DateTime date, int start, int duration, TaskVM? exclude)
    {
        var end = start + duration;
        if (start < 0 || end > 24 * 60) return false;

        foreach (var (from, to) in BusyRanges(date, exclude))
            if (start < to && from < end) return false;

        return true;
    }

    /// <summary>
    /// 从想要的位置往两边找最近的空档（步长 = 当前缩放的间隔）。
    /// 整天都排满了就贴着当天最后一段之后放，宁可挤一下也不把日程丢到第二天。
    /// </summary>
    public int FindFreeStart(DateTime date, int desiredStart, int durationMinutes, TaskVM? exclude = null)
    {
        durationMinutes = Math.Clamp(durationMinutes, 1, 24 * 60);
        var latest = 24 * 60 - durationMinutes;
        var desired = Math.Clamp(desiredStart, 0, latest);

        if (IsFree(date, desired, durationMinutes, exclude)) return desired;

        var step = Math.Max(1, IntervalMinutes);
        var maxSteps = 24 * 60 / step + 1;

        for (var k = 1; k <= maxSteps; k++)
        {
            // 同一距离先试往下推：新建的往后排更符合直觉
            var after = desired + k * step;
            if (after <= latest && IsFree(date, after, durationMinutes, exclude)) return after;

            var before = desired - k * step;
            if (before >= 0 && IsFree(date, before, durationMinutes, exclude)) return before;
        }

        var lastEnd = BusyRanges(date, exclude).Select(r => r.End).DefaultIfEmpty(0).Max();
        return Math.Clamp(lastEnd, 0, latest);
    }

    /// <summary>这一段时间跟谁撞了（用于弹窗里的提示）。</summary>
    private string? ConflictTitle(DateTime date, int start, int duration, TaskVM? exclude)
    {
        var end = start + duration;

        foreach (var task in _all)
        {
            if (ReferenceEquals(task, exclude)) continue;
            if (task.StartTime is not { } startTime) continue;
            if (task.Due is not { } due || due.Date != date.Date) continue;

            var from = (int)startTime.TotalMinutes;
            var to = (int)(task.EndTime?.TotalMinutes ?? from + 30);
            if (to <= from) to = from + 30;

            if (start < to && from < end) return task.Title;
        }

        return null;
    }

    /// <summary>拖时间轴上的块：换天 + 换开始时刻，时长不变；撞到别人就直接让到最近的空档。</summary>
    public void MoveBlock(TaskVM task, DateTime date, int startMinutes)
    {
        var duration = DurationMinutes(task);
        var start = FindFreeStart(date, startMinutes, duration, task);

        RecordUndo($"移动日程块「{task.Title}」", () =>
        {
            task.Due = date.Date;
            task.StartTime = TimeSpan.FromMinutes(start);
            task.EndTime = TimeSpan.FromMinutes(start + duration);
            Rebuild();
        });
        TaskTouched?.Invoke(task);

        if (start != startMinutes)
            Notify($"已避开冲突，挪到 {Clock(start)}–{Clock(start + duration)}", highlight: true);
        else
            Notify($"已调整至 {date:M月d日} {Clock(start)}–{Clock(start + duration)}");
    }

    /// <summary>拖日程块上下边缘时的可拉伸安全边界（碰到相邻日程自动停住，不重叠）。</summary>
    public (int MinStart, int MaxEnd) GetResizeBounds(TaskVM task)
    {
        if (task.Due is not { } due || task.StartTime is not { } startTime) return (0, 24 * 60);

        var origStart = (int)startTime.TotalMinutes;
        var origEnd = (int)(task.EndTime?.TotalMinutes ?? origStart + 30);
        if (origEnd <= origStart) origEnd = origStart + 30;

        var minStart = 0;
        var maxEnd = 24 * 60;

        foreach (var (from, to) in BusyRanges(due.Date, task))
        {
            if (to <= origStart) minStart = Math.Max(minStart, to);
            if (from >= origEnd) maxEnd = Math.Min(maxEnd, from);
        }

        return (Math.Min(minStart, origStart), Math.Max(maxEnd, origEnd));
    }

    /// <summary>拖动时间轴日程块的上下边缘后提交新的起止时刻（最少 5 分钟）。</summary>
    public void ResizeBlock(TaskVM task, int startMinutes, int endMinutes)
    {
        startMinutes = Math.Clamp(startMinutes, 0, 24 * 60 - 5);
        endMinutes = Math.Clamp(endMinutes, startMinutes + 5, 24 * 60);

        RecordUndo($"调整时长「{task.Title}」", () =>
        {
            task.StartTime = TimeSpan.FromMinutes(startMinutes);
            task.EndTime = TimeSpan.FromMinutes(endMinutes);
            Rebuild();
        });
        TaskTouched?.Invoke(task);
        Notify($"已调整至 {Clock(startMinutes)}–{Clock(endMinutes)}");
    }

    public static int DurationMinutes(TaskVM task)
    {
        if (task.StartTime is not { } start || task.EndTime is not { } end) return 30;
        var minutes = (int)(end - start).TotalMinutes;
        return minutes <= 0 ? 30 : minutes;
    }

    /// <summary>
    /// 同一时段有多条时并排分宽度，而不是叠在一起互相遮住。
    /// 做法：按开始时间把相互重叠的块归成一组，组内贪心分配到列，宽度 = 列宽 / 列数。
    /// </summary>
    private static void LayoutOverlaps(IReadOnlyList<TimelineBlockVM> blocks)
    {
        const double padLeft = 5;
        const double gap = 2;
        var usable = DayWidth - padLeft * 2;

        var i = 0;
        while (i < blocks.Count)
        {
            // 一个簇：起始时间连续重叠下去的那一段
            var end = blocks[i].Top + blocks[i].Height;
            var j = i + 1;
            while (j < blocks.Count && blocks[j].Top < end - 0.5)
            {
                end = Math.Max(end, blocks[j].Top + blocks[j].Height);
                j++;
            }

            var cluster = blocks.Skip(i).Take(j - i).ToList();
            var columnEnds = new List<double>();
            var columns = new Dictionary<TimelineBlockVM, int>();

            foreach (var block in cluster)
            {
                var placed = false;
                for (var c = 0; c < columnEnds.Count; c++)
                {
                    if (columnEnds[c] > block.Top + 0.5) continue;
                    columnEnds[c] = block.Top + block.Height;
                    columns[block] = c;
                    placed = true;
                    break;
                }

                if (placed) continue;
                columnEnds.Add(block.Top + block.Height);
                columns[block] = columnEnds.Count - 1;
            }

            var count = Math.Max(1, columnEnds.Count);
            var width = Math.Max(24, usable / count - gap);

            foreach (var block in cluster)
            {
                var column = columns[block];
                block.Left = padLeft + column * (usable / count);
                block.Width = width;
            }

            i = j;
        }
    }

    // ---------------- 目标 ----------------

    public void NavigateToGoals()
        => SelectedNav = NavItems.First(n => n.Kind == PageKind.Goals);

    public void NavigateToTimeline()
        => SelectedNav = NavItems.First(n => n.Kind == PageKind.Timeline);

    public void AddGoalFromInput()
    {
        var title = (GoalInput ?? "").Trim();
        if (title.Length == 0) return;

        RecordUndo($"新建目标「{title}」", () =>
        {
            _store.AddGoal(title);
            GoalInput = "";
            Rebuild();
        });
        Notify($"已新建目标「{title}」");
    }

    public void OpenGoalTreeEditor(GoalVM goal)
    {
        _goalTreeOpenSnapshot = BeginUndoSnapshot();
        GoalTreeEditor.Open(goal);
        IsGoalTreeEditorOpen = true;
    }

    public void CloseGoalTreeEditor()
    {
        DeadlinePicker.Close();
        IsGoalTreeEditorOpen = false;
        GoalTreeEditor.Close();
        Rebuild();
        if (_goalTreeOpenSnapshot is { } snap)
        {
            _goalTreeOpenSnapshot = null;
            CommitUndoSnapshot("编辑目标结构", snap);
        }
    }

    public void OpenNodeDeadlinePicker(GoalNodeVM node)
    {
        var label = string.IsNullOrWhiteSpace(node.DisplayTitle) ? "目标" : node.DisplayTitle;
        DeadlinePicker.Open($"「{label}」截止日期", node.Due, node.DueTime, (due, dueTime) => GoalTreeEditor.SetNodeDue(node, due, dueTime));
    }

    public void OpenGoalDeadlinePicker(GoalVM goal)
    {
        var label = string.IsNullOrWhiteSpace(goal.Title) ? "目标" : goal.Title;
        DeadlinePicker.Open($"「{label}」截止日期", goal.Due, goal.DueTime, (due, dueTime) =>
        {
            RecordUndo($"修改目标截止日「{label}」", () =>
            {
                goal.Due = due;
                goal.DueTime = due.HasValue ? dueTime : null;
                OnGoalStructureChanged(rebuildTimelineAndCards: false);
            });
            Notify(due.HasValue ? $"已设截止日期：{goal.DueBadgeText}" : "已清除截止日期");
        });
    }

    public void OnGoalStructureChanged(bool rebuildTimelineAndCards)
    {
        _file.MarkDirty(_store);
        if (_hasInitialized) ScheduleAutoSync();
        RebuildDeadlines();
        if (!rebuildTimelineAndCards) return;

        foreach (var task in _all) task.NotifyGoalDisplayChanged();
        foreach (var preset in _store.Presets) preset.RefreshSummary();
        if (IsTimeline)
        {
            _timelineDirty = false;
            RebuildTimeline();
        }
        else
        {
            _timelineDirty = true;
        }
        if (IsTagDetailOpen) RebuildTagDetail();
        if (IsPresetManagerOpen) PresetManager.RefreshPicksPreservingSelection();
    }

    public void RemoveGoalSubNode(GoalVM goal, GoalNodeVM node)
    {
        var label = node.DisplayTitle;
        RecordUndo($"删除子目标「{label}」", () =>
        {
            foreach (var task in _all)
            {
                if (task.GoalBindings.Any(b => ReferenceEquals(b.Goal, goal) && ReferenceEquals(b.Node, node)))
                    task.SetGoalBindings(task.GoalBindings.Where(b => !(ReferenceEquals(b.Goal, goal) && ReferenceEquals(b.Node, node))));
            }

            foreach (var preset in _store.Presets)
            {
                if (preset.GoalBindings.Any(b => ReferenceEquals(b.Goal, goal) && ReferenceEquals(b.Node, node)))
                    preset.SetGoalBindings(preset.GoalBindings.Where(b => !(ReferenceEquals(b.Goal, goal) && ReferenceEquals(b.Node, node))));
            }

            goal.RemoveSubGoal(node);
            Rebuild();
        });
        Notify($"已删除子目标「{label}」");
    }

    public void ToggleGoalNodeCompleted(GoalVM goal, GoalNodeVM node)
    {
        var next = !node.IsCompleted;
        RecordUndo(next ? $"完成子目标「{node.DisplayTitle}」" : $"恢复子目标「{node.DisplayTitle}」", () =>
        {
            goal.SetNodeCompleted(node, next);
            Rebuild();
        });
        Notify(next ? $"已完成「{node.DisplayTitle}」" : $"已恢复「{node.DisplayTitle}」");
    }

    public void ToggleGoalCompleted(GoalVM goal)
    {
        var next = !goal.IsCompleted;
        RecordUndo(next ? $"完成目标「{goal.Title}」" : $"恢复目标「{goal.Title}」", () =>
        {
            goal.SetGoalCompleted(next);
            Rebuild();
        });
        Notify(next ? $"目标「{goal.Title}」已完成，已移入下方区域" : $"已恢复目标「{goal.Title}」");
    }

    public void OpenGoalPickerForDraft(
        GoalVM goal,
        IEnumerable<GoalBindingVM> existingBindings,
        Action<IReadOnlyList<GoalNodeVM>> onConfirm)
    {
        GoalPicker.Open(goal, existingBindings, onConfirm);
        IsGoalPickerOpen = true;
    }

    public void ConfirmGoalPicker()
    {
        IsGoalPickerOpen = false;
        GoalPicker.Confirm();
    }

    public void CloseGoalPicker()
    {
        IsGoalPickerOpen = false;
        GoalPicker.Close();
    }

    internal void NotifyFromChild(string message, bool highlight = false) => Notify(message, highlight);

    // ---------------- 日程编辑弹窗 ----------------

    /// <summary>时间轴上拖出一段 → 弹窗新建（标题、标签、目标、起止时间都在里面改）。</summary>
    public void OpenNewSchedule(DateTime date, int startMinutes, int endMinutes)
    {
        if (endMinutes <= startMinutes) endMinutes = startMinutes + 30;

        Editor.OpenNew(date, startMinutes, endMinutes);
        IsEditorOpen = true;
    }

    /// <summary>截止日期页点击「新建」→ 弹窗新建仅设截止日期、不占具体时段的日程。</summary>
    public void OpenNewDeadlineSchedule(DateTime? date = null)
    {
        Editor.OpenNewDeadline(date ?? DateTime.Today);
        IsEditorOpen = true;
    }

    /// <summary>未来事项页点击「新建」→ 弹窗新建无日期的未来事项。</summary>
    public void OpenNewFutureSchedule()
    {
        Editor.OpenNewFuture();
        IsEditorOpen = true;
    }

    /// <summary>自由专注结束 → 打开日程编辑窗，保存后自动写入时间轴并标记完成。</summary>
    public void OpenScheduleFromFreeFocus(string title, DateTime date, int startMinutes, int endMinutes)
    {
        Editor.OpenFromFreeFocus(title, date, startMinutes, endMinutes);
        IsEditorOpen = true;
    }

    public void OpenSchedule(TaskVM task)
    {
        Editor.OpenExisting(task);
        IsEditorOpen = true;
    }

    public void CloseEditor() => IsEditorOpen = false;

    /// <summary>边打字边说清楚：这段时间跟谁撞了、保存时会挪到几点。</summary>
    public void RefreshEditorHint()
    {
        if (Editor.HasDue && Editor.HasDueTime && !Editor.TryReadDueTime(out _))
            Editor.SetError("截止时间点写成 18:00 这样");
        else
            Editor.SetError("");

        if (!Editor.HasDue || !Editor.HasTime)
        {
            Editor.SetHint("");
            return;
        }

        if (!Editor.TryReadTime(out var start, out var end))
        {
            Editor.SetHint("时间写成 9:30 这样");
            return;
        }

        var duration = Math.Max(1, end - start);
        var free = FindFreeStart(Editor.Date, start, duration, Editor.Task);

        if (free == start)
        {
            Editor.SetHint("");
            return;
        }

        var conflict = ConflictTitle(Editor.Date, start, duration, Editor.Task);
        var window = $"{Clock(free)}–{Clock(free + duration)}";

        Editor.SetHint(conflict is null
            ? $"这段时间排不下，保存时会挪到 {window}"
            : $"和「{conflict}」撞了，保存时会挪到 {window}");
    }

    public void SaveEditor()
    {
        var start = 0;
        var duration = 0;
        var free = 0;
        TimeSpan? dueTime = null;

        if (Editor.HasDue && Editor.HasDueTime)
        {
            if (!Editor.TryReadDueTime(out var parsedDueTime))
            {
                Editor.SetError("截止时间点看不懂，写成 18:00 这样");
                return;
            }

            dueTime = parsedDueTime;
        }

        if (Editor.HasDue && Editor.HasTime)
        {
            if (!Editor.TryReadTime(out start, out var end))
            {
                Editor.SetError("时间看不懂，写成 9:30 这样");
                return;
            }

            duration = Math.Max(1, end - start);
            free = FindFreeStart(Editor.Date, start, duration, Editor.Task);
        }

        var title = (Editor.Title ?? "").Trim();
        if (title.Length == 0) title = "未命名";

        var isNew = Editor.Task is null;
        var markDone = Editor.MarkDoneOnSave;
        TaskVM task = null!;
        var before = BeginUndoSnapshot();
        task = Editor.Task ?? _store.AddSchedule();

        task.Title = title;
        task.Note = Editor.Note ?? "";
        task.Remind = Editor.Remind;
        task.Repeat = Editor.Repeat == ScheduleEditorVM.NoRepeat ? "" : Editor.Repeat;
        task.IsUrgent = Editor.IsUrgent;
        task.IsImportant = Editor.IsImportant;
        task.SetGoalBindings(Editor.SelectedGoalBindings);

        if (!Editor.HasDue)
        {
            task.Due = null;
            task.DueTime = null;
            task.StartTime = null;
            task.EndTime = null;
        }
        else if (!Editor.HasTime)
        {
            task.Due = Editor.Date.Date;
            task.DueTime = dueTime;
            task.StartTime = null;
            task.EndTime = null;
        }
        else
        {
            task.Due = Editor.Date.Date;
            task.DueTime = dueTime;
            task.StartTime = TimeSpan.FromMinutes(free);
            task.EndTime = TimeSpan.FromMinutes(free + duration);
        }

        if (markDone)
            task.Done = true;

        task.Tags.Clear();
        foreach (var tag in ResolveSelectedTags(Editor.SelectedTagPicks)) task.Tags.Add(tag);

        task.Persons.Clear();
        foreach (var person in ResolveSelectedPersons(Editor.SelectedPersonPicks)) task.Persons.Add(person);

        task.Locations.Clear();
        foreach (var location in ResolveSelectedLocations(Editor.SelectedLocationPicks)) task.Locations.Add(location);

        if (markDone)
            _history.Append(task.Id, task.Due?.Date, done: true);

        IsEditorOpen = false;
        Rebuild();
        CommitUndoSnapshot(
            markDone ? $"记录并完成「{title}」" : isNew ? $"新建「{title}」" : $"编辑「{title}」",
            before,
            markDone ? [new CompletionUndoEvent(task.Id, task.Due?.Date, true)] : null);
        TaskTouched?.Invoke(task);

        if (Editor.HasDue && Editor.HasTime && free != start)
            Notify(markDone ? $"已避开冲突，保存并完成至 {Clock(free)}–{Clock(free + duration)}" : $"已避开冲突，保存至 {Clock(free)}–{Clock(free + duration)}", highlight: true);
        else
            Notify(markDone ? $"已保存并完成「{title}」" : isNew ? $"已创建「{title}」" : $"已保存「{title}」");
    }

    public void DeleteEdited()
    {
        if (Editor.Task is not { } task) return;

        IsEditorOpen = false;
        Delete(task);
    }

    /// <summary>弹窗里当场建的标签/人物/地点先只是草稿，保存时才真正进 store。</summary>
    public void AddEditorTag(string name)
        => AddProvisionalOrExistingTag(Editor.TagPicks, Editor.AddTagPick, name);

    public void AddPresetManagerTag(string name)
        => AddProvisionalOrExistingTag(PresetManager.TagPicks, PresetManager.AddTagPick, name);

    public void AddEditorPerson(string name)
        => AddProvisionalOrExistingPerson(Editor.PersonPicks, Editor.AddPersonPick, name);

    public void AddPresetManagerPerson(string name)
        => AddProvisionalOrExistingPerson(PresetManager.PersonPicks, PresetManager.AddPersonPick, name);

    public void AddEditorLocation(string name)
        => AddProvisionalOrExistingLocation(Editor.LocationPicks, Editor.AddLocationPick, name);

    public void AddPresetManagerLocation(string name)
        => AddProvisionalOrExistingLocation(PresetManager.LocationPicks, PresetManager.AddLocationPick, name);

    private void AddProvisionalOrExistingTag(
        ObservableCollection<TagPickVM> picks,
        Action<TagPickVM> addPick,
        string name)
        => AddProvisionalOrExistingPick(
            picks,
            addPick,
            name,
            '#',
            _store.FindTag,
            n => new TagVM(n) { Color = _store.PickLeastUsedTagColor() },
            (e, sel, prov) => new TagPickVM(e, sel) { IsProvisional = prov });

    private void AddProvisionalOrExistingPerson(
        ObservableCollection<PersonPickVM> picks,
        Action<PersonPickVM> addPick,
        string name)
        => AddProvisionalOrExistingPick(
            picks,
            addPick,
            name,
            '@',
            _store.FindPerson,
            n => new PersonVM(n),
            (e, sel, prov) => new PersonPickVM(e, sel) { IsProvisional = prov });

    private void AddProvisionalOrExistingLocation(
        ObservableCollection<LocationPickVM> picks,
        Action<LocationPickVM> addPick,
        string name)
        => AddProvisionalOrExistingPick(
            picks,
            addPick,
            name,
            null,
            _store.FindLocation,
            n => new LocationVM(n),
            (e, sel, prov) => new LocationPickVM(e, sel) { IsProvisional = prov });

    private static void AddProvisionalOrExistingPick<TEntity, TPick>(
        ObservableCollection<TPick> picks,
        Action<TPick> addPick,
        string rawName,
        char? stripLeading,
        Func<string, TEntity?> findInStore,
        Func<string, TEntity> createDraft,
        Func<TEntity, bool, bool, TPick> createPick)
        where TPick : class, IFilterablePick
    {
        var trimmed = (rawName ?? "").Trim();
        if (stripLeading is { } ch) trimmed = trimmed.TrimStart(ch);
        if (trimmed.Length == 0) return;

        var existing = picks.FirstOrDefault(
            p => string.Equals(p.Name, trimmed, StringComparison.OrdinalIgnoreCase));

        if (existing is not null)
        {
            existing.IsSelected = true;
            return;
        }

        var entity = findInStore(trimmed);
        if (entity is not null)
        {
            addPick(createPick(entity, true, false));
            return;
        }

        addPick(createPick(createDraft(trimmed), true, true));
    }

    private List<TagVM> ResolveSelectedTags(IEnumerable<TagPickVM> selectedPicks)
        => ResolveSelectedEntities(selectedPicks, p => p.IsProvisional, p => p.Tag, n => _store.GetOrCreateTag(n), p => p.Name);

    private List<PersonVM> ResolveSelectedPersons(IEnumerable<PersonPickVM> selectedPicks)
        => ResolveSelectedEntities(selectedPicks, p => p.IsProvisional, p => p.Person, _store.GetOrCreatePerson, p => p.Name);

    private List<LocationVM> ResolveSelectedLocations(IEnumerable<LocationPickVM> selectedPicks)
        => ResolveSelectedEntities(selectedPicks, p => p.IsProvisional, p => p.Location, _store.GetOrCreateLocation, p => p.Name);

    private static List<TEntity> ResolveSelectedEntities<TPick, TEntity>(
        IEnumerable<TPick> selectedPicks,
        Func<TPick, bool> isProvisional,
        Func<TPick, TEntity> getEntity,
        Func<string, TEntity?> getOrCreate,
        Func<TPick, string> getName)
        where TEntity : class
    {
        var result = new List<TEntity>();
        foreach (var pick in selectedPicks)
        {
            var entity = isProvisional(pick) ? getOrCreate(getName(pick)) : getEntity(pick);
            if (entity is not null && !result.Contains(entity)) result.Add(entity);
        }
        return result;
    }

    // ---------------- 快捷日程预设 ----------------

    public void OpenPresetManager()
    {
        PresetManager.StartNew();
        IsPresetManagerOpen = true;
    }

    public void ClosePresetManager() => IsPresetManagerOpen = false;

    public void StartEditPreset(SchedulePresetVM preset) => PresetManager.StartEdit(preset);

    public void CancelEditPreset() => PresetManager.StartNew();

    public void SavePreset()
    {
        var title = (PresetManager.Title ?? "").Trim();
        if (title.Length == 0)
        {
            PresetManager.SetError("写个卡片名称，比如「休息」");
            return;
        }

        if (!PresetManager.TryReadDuration(out var duration))
        {
            PresetManager.SetError("时长请填 5–1440 之间的分钟数");
            return;
        }

        var isNew = PresetManager.EditingPreset is null;
        RecordUndo(isNew ? $"添加快捷卡片「{title}」" : $"保存快捷卡片「{title}」", () =>
        {
            var preset = PresetManager.EditingPreset ?? _store.AddPreset();

            preset.Title = title;
            preset.DurationMinutes = duration;
            preset.Remind = PresetManager.Remind;
            preset.Note = PresetManager.Note ?? "";
            preset.IsUrgent = PresetManager.IsUrgent;
            preset.IsImportant = PresetManager.IsImportant;
            preset.SetGoalBindings(PresetManager.SelectedGoalBindings);

            preset.Tags.Clear();
            foreach (var tag in ResolveSelectedTags(PresetManager.SelectedTagPicks))
                preset.Tags.Add(tag);

            preset.Persons.Clear();
            foreach (var person in ResolveSelectedPersons(PresetManager.SelectedPersonPicks))
                preset.Persons.Add(person);

            preset.Locations.Clear();
            foreach (var location in ResolveSelectedLocations(PresetManager.SelectedLocationPicks))
                preset.Locations.Add(location);

            Rebuild();
            PresetManager.StartNew();
        });
        Notify(isNew ? $"已添加快捷卡片「{title}」" : $"已保存快捷卡片「{title}」");
    }

    public void DeletePreset(SchedulePresetVM preset)
    {
        var label = preset.DisplayTitle;
        if (ReferenceEquals(PresetManager.EditingPreset, preset))
            PresetManager.StartNew();

        RecordUndo($"删除快捷卡片「{label}」", () =>
        {
            _store.RemovePreset(preset);
            _file.MarkDirty(_store);
            if (_hasInitialized) ScheduleAutoSync();
        });
        Notify($"已删除快捷卡片「{label}」");
    }

    /// <summary>把快捷卡片拖到时间轴松手：直接落盘生成一条日程（撞到别人自动让开）。</summary>
    public void CreateScheduleFromPreset(SchedulePresetVM preset, DateTime date, int desiredStartMinutes)
    {
        var duration = Math.Clamp(preset.DurationMinutes, 5, 24 * 60);
        var free = FindFreeStart(date, desiredStartMinutes, duration);
        var title = string.IsNullOrWhiteSpace(preset.Title) ? "未命名" : preset.Title.Trim();

        TaskVM task = null!;
        RecordUndo($"从快捷卡片创建「{title}」", () =>
        {
            task = _store.AddSchedule(s =>
            {
                s.Title = title;
                s.Note = preset.Note;
                s.Remind = preset.Remind;
                s.Due = date.Date;
                s.StartTime = TimeSpan.FromMinutes(free);
                s.EndTime = TimeSpan.FromMinutes(free + duration);
                s.IsUrgent = preset.IsUrgent;
                s.IsImportant = preset.IsImportant;
                s.SetGoalBindings(preset.GoalBindings.Select(b => new GoalBindingVM(b.Goal, b.Node)));
            });

            foreach (var tag in preset.Tags)
                if (!task.Tags.Contains(tag)) task.Tags.Add(tag);

            foreach (var person in preset.Persons)
                if (!task.Persons.Contains(person)) task.Persons.Add(person);

            foreach (var location in preset.Locations)
                if (!task.Locations.Contains(location)) task.Locations.Add(location);

            Rebuild();
        });
        TaskTouched?.Invoke(task);

        if (free != desiredStartMinutes)
            Notify($"已避开冲突，添加「{title}」至 {Clock(free)}–{Clock(free + duration)}", highlight: true);
        else
            Notify($"已添加「{title}」 {Clock(free)}–{Clock(free + duration)}");
    }

    /// <summary>「9」「9:30」「09:30」都认；读不出来返回 false。</summary>
    public static bool TryParseClock(string text, out int minutes)
    {
        minutes = 0;

        var trimmed = (text ?? "").Trim();
        if (trimmed.Length == 0) return false;

        if (trimmed.Contains(':'))
        {
            if (!TimeSpan.TryParse(trimmed, out var span)) return false;
            if (span < TimeSpan.Zero || span >= TimeSpan.FromDays(1)) return false;

            minutes = (int)span.TotalMinutes;
            return true;
        }

        if (int.TryParse(trimmed, out var hour) && hour is >= 0 and < 24)
        {
            minutes = hour * 60;
            return true;
        }

        return false;
    }

    private static string Clock(int minutes)
    {
        minutes = Math.Clamp(minutes, 0, 24 * 60);
        return $"{minutes / 60:00}:{minutes % 60:00}";
    }

    private GoalVM CreateGoal(string title) => _store.AddGoal(title);

    public void DeleteGoal(GoalVM goal)
    {
        if (IsGoalTreeEditorOpen && ReferenceEquals(GoalTreeEditor.Goal, goal))
            CloseGoalTreeEditor();
        if (IsGoalPickerOpen && ReferenceEquals(GoalPicker.Goal, goal))
            CloseGoalPicker();

        var title = goal.Title;
        RecordUndo($"删除目标「{title}」", () =>
        {
            foreach (var task in _all.Where(t => t.HasGoalRef(goal)))
                task.SetGoalBindings(task.GoalBindings.Where(b => !ReferenceEquals(b.Goal, goal)));
            foreach (var preset in _store.Presets.Where(p => p.HasGoalRef(goal)))
                preset.SetGoalBindings(preset.GoalBindings.Where(b => !ReferenceEquals(b.Goal, goal)));

            _store.RemoveGoal(goal);
            Rebuild();
        });
        Notify($"已删除目标「{title}」");
    }

    /// <summary>把任务挂到目标下。拖到有子目标的目标卡上时弹出目标树选择。</summary>
    public void AssignGoal(TaskVM task, GoalVM goal)
    {
        if (goal.HasSubGoals)
        {
            OpenGoalPickerForDraft(goal, task.GoalBindings, nodes =>
            {
                RecordUndo($"关联目标「{goal.Title}」", () =>
                {
                    var next = task.GoalBindings
                        .Where(b => !ReferenceEquals(b.Goal, goal))
                        .Concat(nodes.Select(n => new GoalBindingVM(goal, n.IsRoot ? null : n)))
                        .ToList();
                    task.SetGoalBindings(next);
                    Rebuild();
                });
                TaskTouched?.Invoke(task);
                Notify(nodes.Count > 0
                    ? $"已关联「{goal.Title}」下的 {nodes.Count} 项目标"
                    : $"已取消「{goal.Title}」关联");
            });
            return;
        }

        if (task.HasGoalRef(goal))
        {
            Notify($"已在目标「{goal.Title}」中");
            return;
        }

        RecordUndo($"关联目标「{goal.Title}」", () =>
        {
            var updated = task.GoalBindings.ToList();
            updated.Add(new GoalBindingVM(goal, null));
            task.SetGoalBindings(updated);
            Rebuild();
        });
        TaskTouched?.Invoke(task);
        Notify($"已关联目标「{goal.Title}」");
    }

    public void ClearGoal(TaskVM task)
    {
        if (!task.HasGoal) return;
        RecordUndo($"取消目标关联「{task.Title}」", () =>
        {
            task.SetGoalBindings(Array.Empty<GoalBindingVM>());
            Rebuild();
        });
    }

    public void ToggleGoalExpand(GoalVM goal)
    {
        var expand = !goal.IsExpanded;
        foreach (var g in Goals) g.IsExpanded = false;
        goal.IsExpanded = expand;
    }

    // ---------------- 演示数据 ----------------

    private void Seed()
    {
        var today = DateTime.Today;

        var paper = CreateGoal("发小论文");
        var lit = paper.AddChildSubGoal(paper.RootNode, "写文献综述", GoalEdgeKind.Belong);
        var iv = paper.AddChildSubGoal(paper.RootNode, "梳理自变量", GoalEdgeKind.Belong);
        var dv = paper.AddChildSubGoal(paper.RootNode, "找出因变量", GoalEdgeKind.Belong);
        var model = paper.AddChildSubGoal(iv, "理清研究模型", GoalEdgeKind.Progress);
        paper.TryAddOrUpdateEdge(dv.Id, model.Id, GoalEdgeKind.Progress, out _);
        var expDesign = paper.AddChildSubGoal(lit, "设计实验", GoalEdgeKind.Progress);
        paper.TryAddOrUpdateEdge(model.Id, expDesign.Id, GoalEdgeKind.Progress, out _);
        var expRun = paper.AddChildSubGoal(expDesign, "进行实验", GoalEdgeKind.Progress);
        paper.AddChildSubGoal(expRun, "数据回收分析", GoalEdgeKind.Progress);
        paper.AutoLayout();

        var python = CreateGoal("学 Python");
        var pyBase = python.AddChildSubGoal(python.RootNode, "语法与模块", GoalEdgeKind.Belong);
        var pyCrawl = python.AddChildSubGoal(pyBase, "爬虫实战", GoalEdgeKind.Progress);
        python.AutoLayout();

        var tidy = CreateGoal("生活整理");

        TaskVM Add(string title, DateTime? due, string note = "", string[]? tags = null,
                 string repeat = "", string remind = RemindRules.Default, bool done = false, GoalVM? goal = null,
                 string? time = null, GoalBindingVM[]? bindings = null)
        {
            var t = _store.AddSchedule(s =>
            {
                s.Title = title;
                s.Note = note;
                s.Due = due;
                s.Repeat = repeat;
                s.Remind = remind;
                s.Done = done;
                if (bindings is { Length: > 0 })
                    s.SetGoalBindings(bindings);
                else
                    s.Goal = goal;
            });

            if (time is not null)
            {
                var parts = time.Split('-');
                t.StartTime = TimeSpan.Parse(parts[0]);
                t.EndTime = TimeSpan.Parse(parts[1]);
            }

            foreach (var tag in tags ?? Array.Empty<string>())
                t.Tags.Add(_store.GetOrCreateTag(tag));

            return t;
        }

        Add("交电费", today.AddDays(-1), "户号记在手机备忘录里（在发票右上角）", new[] { "生活" }, time: "19:00-19:15");
        Add("写方案", today, "先把档位的边界定死，再动代码", new[] { "工作" }, time: "09:00-10:30");
        Add("梳理变量与模型", today, "核对核心自变量、因变量与理论模型假设", new[] { "科研" }, time: "14:00-15:30",
            bindings: new[]
            {
                new GoalBindingVM(paper, iv),
                new GoalBindingVM(paper, dv),
                new GoalBindingVM(paper, model)
            });
        Add("跑步", today, "", new[] { "习惯" }, repeat: "每天", remind: RemindRules.Default, time: "19:30-20:10");
        Add("回邮件", today, "", new[] { "工作" }, time: "11:00-11:40");
        Add("吃药", today, "", new[] { "生活" }, repeat: "每天", done: true, time: "08:00-08:10");
        Add("体检预约", today.AddDays(1), "上午空腹", new[] { "生活" }, time: "08:30-09:30");
        Add("整理书房", EndOfWeek(), "", new[] { "生活" }, goal: tidy, time: "14:00-16:00");
        Add("年报", today.AddDays(40), "", new[] { "工作" }, remind: "", time: "10:00-11:30");
        Add("学 Rust", today.AddDays(3), "先把所有权搞明白", new[] { "学习" }, remind: "", time: "20:00-21:00");
        Add("读官方教程前四章", today.AddDays(2), "", new[] { "学习" }, remind: "", time: "20:00-21:30",
            bindings: new[] { new GoalBindingVM(python, pyBase) });
        Add("写一个爬虫小脚本", today.AddDays(10), "", new[] { "学习" }, remind: "", time: "20:00-21:00",
            bindings: new[] { new GoalBindingVM(python, pyCrawl) });
        Add("换手机", today.AddDays(5), "", new[] { "生活" }, remind: "", goal: tidy, time: "15:00-15:30");

        SeedDefaultPresets();
    }

    private void SeedDefaultPresets()
    {
        void AddPreset(string title, int durationMinutes)
        {
            _store.AddPreset(p =>
            {
                p.Title = title;
                p.DurationMinutes = durationMinutes;
                p.Remind = RemindRules.Default;
            });
        }

        AddPreset("休息", 10);
        AddPreset("专注", 25);
        AddPreset("整理", 15);
    }
}
