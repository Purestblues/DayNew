using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows.Threading;
using DayNew.Core;
using DayNew.ViewModels;
using DayNew.Views;

namespace DayNew.Services;

/// <summary>
/// 主进程内嵌 Streamable HTTP MCP 服务端（127.0.0.1 /mcp）：
///   - 默认关闭，启用时使用 DPAPI 保存的 Bearer Token 常数时间校验；
///   - 所有工具调用切回 WPF UI Dispatcher 执行（单写者模型，不与界面抢 data.json）；
///   - 删除类工具标 destructiveHint=true 并强走独立置顶即时确认窗，拒绝或 60s 超时一律拦截。
/// </summary>
public sealed class McpServerService : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private const string ServerInstructions =
        "你是连接在「日新 DayNew」本地桌面应用内的 MCP 工具。必须遵守以下原则：\n" +
        "1. 日新以截止日期(Due, yyyy-MM-dd)推导档位（逾期/今天/明天/本周/之后），无日期任务属于「未来事项」。\n" +
        "2. 同一天内有具体起止时间(startTime/endTime)的日程禁止重叠，create_schedule 与 update_schedule 会自动避让到最近空档。\n" +
        "3. 日程/标签/目标的新增与修改可直接执行；完成状态只能通过 set_done 切换（completions.ndjson 只追加，不改写或删除历史）。\n" +
        "4. 所有删除工具（delete_schedule、delete_tag、delete_goal）均标有 destructiveHint=true：调用前必须先向用户说明待删对象及影响范围并征得同意；且应用本地会再次弹出置顶确认窗由用户当场许可，拒绝或超时即失败。\n" +
        "5. 导入覆盖、WebDAV 凭证/密码、MCP Token 等敏感操作对 MCP 屏蔽。";

    private readonly MainViewModel _vm;
    private readonly Dispatcher _dispatcher;
    private readonly object _lifecycleLock = new();

    private HttpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _listenTask;
    private int _activePort;
    private bool _disposed;

    public bool IsListening { get; private set; }
    public string LastError { get; private set; } = "";

    public event Action? StatusChanged;

    /// <summary>可注入用于自动化测试的删除确认钩子；为空时默认在 UI 线程弹出独立置顶确认窗。</summary>
    public Func<string, string, string, Task<bool>>? ConfirmDeleteAsync { get; set; }

    public McpServerService(MainViewModel vm, Dispatcher dispatcher)
    {
        _vm = vm;
        _dispatcher = dispatcher;
    }

    public void StartOrRestart()
    {
        lock (_lifecycleLock)
        {
            if (_disposed) return;

            if (!_vm.Settings.McpEnabled)
            {
                StopInternal();
                LastError = "";
                StatusChanged?.Invoke();
                return;
            }

            var desiredPort = _vm.Settings.McpPort is >= 1024 and <= 65535
                ? _vm.Settings.McpPort
                : AppSettings.DefaultMcpPort;

            if (IsListening && _activePort == desiredPort && _listener is { IsListening: true })
                return;

            StopInternal();

            try
            {
                _ = _vm.Settings.GetMcpToken();
                var listener = new HttpListener();
                listener.Prefixes.Add($"http://127.0.0.1:{desiredPort}/");
                listener.Start();

                var cts = new CancellationTokenSource();
                _listener = listener;
                _cts = cts;
                _activePort = desiredPort;
                IsListening = true;
                LastError = "";
                _listenTask = Task.Run(() => AcceptLoopAsync(listener, cts.Token));
            }
            catch (Exception ex)
            {
                IsListening = false;
                LastError = $"端口 {desiredPort} 启动失败：{ex.Message}";
                StopInternal();
            }
        }

        StatusChanged?.Invoke();
    }

    public void Stop()
    {
        lock (_lifecycleLock)
        {
            StopInternal();
            LastError = "";
        }
        StatusChanged?.Invoke();
    }

    public void Dispose()
    {
        lock (_lifecycleLock)
        {
            if (_disposed) return;
            _disposed = true;
            StopInternal();
        }
    }

    private void StopInternal()
    {
        IsListening = false;
        _activePort = 0;

        try { _cts?.Cancel(); } catch { }
        try { _listener?.Stop(); } catch { }
        try { _listener?.Close(); } catch { }
        try { _listenTask?.Wait(300); } catch { }

        _cts?.Dispose();
        _cts = null;
        _listener = null;
        _listenTask = null;
    }

    private async Task AcceptLoopAsync(HttpListener listener, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            HttpListenerContext ctx;
            try
            {
                ctx = await listener.GetContextAsync().ConfigureAwait(false);
            }
            catch
            {
                break;
            }

            _ = Task.Run(() => HandleContextAsync(ctx, ct), ct);
        }
    }

    private async Task HandleContextAsync(HttpListenerContext ctx, CancellationToken ct)
    {
        var req = ctx.Request;
        var resp = ctx.Response;
        try
        {
            var path = req.Url?.AbsolutePath ?? "/";
            var method = req.HttpMethod.ToUpperInvariant();
            var auth = req.Headers["Authorization"];
            var body = "";

            if (method == "POST")
            {
                using var reader = new StreamReader(req.InputStream, req.ContentEncoding ?? Encoding.UTF8);
                body = await reader.ReadToEndAsync(ct).ConfigureAwait(false);
            }

            var (status, respBody) = await ProcessHttpRequestAsync(method, path, auth, body).ConfigureAwait(false);
            resp.StatusCode = status;
            if (!string.IsNullOrEmpty(respBody))
            {
                var bytes = Encoding.UTF8.GetBytes(respBody);
                resp.ContentType = "application/json; charset=utf-8";
                resp.ContentLength64 = bytes.Length;
                await resp.OutputStream.WriteAsync(bytes, ct).ConfigureAwait(false);
            }
        }
        catch
        {
            try { resp.StatusCode = 500; } catch { }
        }
        finally
        {
            try { resp.Close(); } catch { }
        }
    }

    internal async Task<(int StatusCode, string Body)> ProcessHttpRequestAsync(
        string method,
        string path,
        string? authorizationHeader,
        string requestBody)
    {
        if (!string.Equals(path, "/mcp", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(path, "/mcp/", StringComparison.OrdinalIgnoreCase))
        {
            return (404, JsonError(null, -32004, "Not Found"));
        }

        if (!_vm.Settings.McpEnabled)
            return (403, JsonError(null, -32003, "日新 MCP 接口未开启，请先在设置页启用"));

        if (!IsAuthorized(authorizationHeader))
            return (401, JsonError(null, -32001, "Bearer Token 无效或缺失"));

        if (string.Equals(method, "DELETE", StringComparison.OrdinalIgnoreCase))
            return (200, "{}");

        if (!string.Equals(method, "POST", StringComparison.OrdinalIgnoreCase))
            return (405, JsonError(null, -32600, "仅支持 POST /mcp"));

        if (string.IsNullOrWhiteSpace(requestBody))
            return (400, JsonError(null, -32700, "请求体不能为空"));

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(requestBody);
        }
        catch
        {
            return (400, JsonError(null, -32700, "无效的 JSON 请求"));
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return (400, JsonError(null, -32600, "JSON-RPC 请求必须为对象"));

            var hasId = root.TryGetProperty("id", out var idEl) && idEl.ValueKind != JsonValueKind.Null;
            JsonNode? idNode = hasId ? JsonNode.Parse(idEl.GetRawText()) : null;
            var rpcMethod = root.TryGetProperty("method", out var mEl) && mEl.ValueKind == JsonValueKind.String
                ? mEl.GetString() ?? ""
                : "";

            JsonElement paramsEl = root.TryGetProperty("params", out var pEl) ? pEl : default;

            if (!hasId)
            {
                // JSON-RPC Notification（如 notifications/initialized）
                return (202, "");
            }

            if (string.IsNullOrWhiteSpace(rpcMethod))
                return (400, JsonError(idNode, -32600, "缺少 method 字段"));

            try
            {
                object? result = rpcMethod switch
                {
                    "initialize" => HandleInitialize(paramsEl),
                    "ping" => new { },
                    "tools/list" => HandleToolsList(),
                    "tools/call" => await HandleToolsCallAsync(paramsEl).ConfigureAwait(false),
                    _ => null
                };

                if (result is null && rpcMethod is not ("initialize" or "ping" or "tools/list" or "tools/call"))
                    return (200, JsonError(idNode, -32601, $"不支持的方法：{rpcMethod}"));

                return (200, JsonSuccess(idNode, result!));
            }
            catch (McpProtocolException pex)
            {
                return (200, JsonError(idNode, pex.Code, pex.Message));
            }
            catch (Exception ex)
            {
                return (200, JsonError(idNode, -32603, ex.Message));
            }
        }
    }

    private bool IsAuthorized(string? authorizationHeader)
    {
        if (string.IsNullOrWhiteSpace(authorizationHeader)) return false;
        const string prefix = "Bearer ";
        if (!authorizationHeader.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;

        var incoming = authorizationHeader[prefix.Length..].Trim();
        var expected = _vm.Settings.GetMcpToken();
        if (string.IsNullOrEmpty(expected)) return false;

        var incomingBytes = Encoding.UTF8.GetBytes(incoming);
        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        return CryptographicOperations.FixedTimeEquals(incomingBytes, expectedBytes);
    }

    private static object HandleInitialize(JsonElement paramsEl)
    {
        var requestedVersion = TryGetString(paramsEl, "protocolVersion");
        var version = requestedVersion is "2024-11-05" or "2025-03-26" or "2025-06-18"
            ? requestedVersion
            : "2025-03-26";

        return new
        {
            protocolVersion = version,
            capabilities = new
            {
                tools = new { listChanged = false }
            },
            serverInfo = new
            {
                name = "daynew-mcp",
                version = "0.5.0"
            },
            instructions = ServerInstructions
        };
    }

    private static object HandleToolsList() => new { tools = ToolDefinitions };

    private async Task<object> HandleToolsCallAsync(JsonElement paramsEl)
    {
        var name = TryGetString(paramsEl, "name");
        if (string.IsNullOrWhiteSpace(name))
            throw new McpProtocolException(-32602, "缺少工具名 name");

        JsonElement args = paramsEl.ValueKind == JsonValueKind.Object && paramsEl.TryGetProperty("arguments", out var argEl)
            ? argEl
            : default;

        try
        {
            var payload = await _dispatcher.InvokeAsync(() => ExecuteToolOnUiAsync(name, args)).Task.Unwrap().ConfigureAwait(false);
            return new
            {
                content = new[]
                {
                    new
                    {
                        type = "text",
                        text = JsonSerializer.Serialize(payload, JsonOptions)
                    }
                },
                isError = false
            };
        }
        catch (ToolInputException tex)
        {
            return new
            {
                content = new[]
                {
                    new
                    {
                        type = "text",
                        text = tex.Message
                    }
                },
                isError = true
            };
        }
    }

    private async Task<object> ExecuteToolOnUiAsync(string toolName, JsonElement args)
    {
        return toolName switch
        {
            "get_overview" => ToolGetOverview(),
            "list_schedules" => ToolListSchedules(args),
            "get_schedule" => ToolGetSchedule(args),
            "create_schedule" => ToolCreateSchedule(args),
            "update_schedule" => ToolUpdateSchedule(args),
            "delete_schedule" => await ToolDeleteScheduleAsync(args),
            "set_done" => ToolSetDone(args),
            "list_tags" => ToolListTags(),
            "save_tag" => ToolSaveTag(args),
            "delete_tag" => await ToolDeleteTagAsync(args),
            "list_goals" => ToolListGoals(),
            "save_goal" => ToolSaveGoal(args),
            "delete_goal" => await ToolDeleteGoalAsync(args),
            "get_completions" => ToolGetCompletions(args),
            _ => throw new McpProtocolException(-32602, $"未知工具：{toolName}")
        };
    }

    // ==================== 14 个工具实现（均已在 UI 线程执行） ====================

    private object ToolGetOverview()
    {
        var all = _vm.Store.Schedules;
        var lanes = new Dictionary<string, List<object>>(StringComparer.Ordinal)
        {
            ["overdue"] = new(),
            ["today"] = new(),
            ["tomorrow"] = new(),
            ["thisWeek"] = new(),
            ["later"] = new(),
            ["future"] = new()
        };

        foreach (var t in all.OrderBy(x => x.Order))
        {
            var lane = MainViewModel.LaneFor(t);
            var key = lane switch
            {
                LaneKind.Overdue => "overdue",
                LaneKind.Today => "today",
                LaneKind.Tomorrow => "tomorrow",
                LaneKind.ThisWeek => "thisWeek",
                LaneKind.Later => "later",
                _ => "future"
            };
            lanes[key].Add(SerializeSchedule(t));
        }

        return new
        {
            date = DateTime.Today.ToString("yyyy-MM-dd"),
            summary = new
            {
                total = all.Count,
                pending = all.Count(t => !t.Done),
                done = all.Count(t => t.Done),
                overdue = lanes["overdue"].Count,
                todayPending = all.Count(t => !t.Done && t.Due?.Date == DateTime.Today && !DeadlineRules.IsOverdue(t.Due, t.DueTime))
            },
            lanes,
            goals = _vm.Store.Goals.Select(g => new
            {
                id = g.Id,
                title = g.Title,
                isCompleted = g.IsCompleted,
                due = g.Due?.ToString("yyyy-MM-dd"),
                dueTime = g.DueTime is { } gdt ? DeadlineRules.FormatClock(gdt) : null,
                doneCount = g.DoneCount,
                total = g.Total,
                progressText = g.ProgressText
            }).ToList()
        };
    }

    private object ToolListSchedules(JsonElement args)
    {
        DateTime? dateFrom = ParseOptionalDate(TryGetString(args, "dateFrom"));
        DateTime? dateTo = ParseOptionalDate(TryGetString(args, "dateTo"));
        var tagFilter = TryGetString(args, "tag");
        var personFilter = TryGetString(args, "person");
        var locationFilter = TryGetString(args, "location");
        var goalIdFilter = TryGetString(args, "goalId");
        var doneFilter = TryGetBool(args, "done");
        var urgentFilter = TryGetBool(args, "urgent");
        var importantFilter = TryGetBool(args, "important");
        var keyword = TryGetString(args, "keyword");
        var includeUndated = TryGetBool(args, "includeUndated") ?? true;
        var offset = Math.Max(0, TryGetInt(args, "offset") ?? 0);
        var limit = Math.Clamp(TryGetInt(args, "limit") ?? 100, 1, 500);

        IEnumerable<TaskVM> query = _vm.Store.Schedules;

        if (!includeUndated)
            query = query.Where(t => t.Due.HasValue);
        if (dateFrom.HasValue)
            query = query.Where(t => !t.Due.HasValue || t.Due.Value.Date >= dateFrom.Value.Date);
        if (dateTo.HasValue)
            query = query.Where(t => !t.Due.HasValue || t.Due.Value.Date <= dateTo.Value.Date);
        if (!string.IsNullOrWhiteSpace(tagFilter))
        {
            var trimmed = tagFilter.Trim().TrimStart('#');
            query = query.Where(t => t.Tags.Any(tg =>
                string.Equals(tg.Id, tagFilter, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(tg.Name, trimmed, StringComparison.OrdinalIgnoreCase)));
        }
        if (!string.IsNullOrWhiteSpace(personFilter))
        {
            var trimmed = personFilter.Trim().TrimStart('@');
            query = query.Where(t => t.Persons.Any(p =>
                string.Equals(p.Id, personFilter, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(p.Name, trimmed, StringComparison.OrdinalIgnoreCase)));
        }
        if (!string.IsNullOrWhiteSpace(locationFilter))
        {
            var trimmed = locationFilter.Trim();
            query = query.Where(t => t.Locations.Any(l =>
                string.Equals(l.Id, locationFilter, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(l.Name, trimmed, StringComparison.OrdinalIgnoreCase)));
        }
        if (!string.IsNullOrWhiteSpace(goalIdFilter))
            query = query.Where(t => t.GoalBindings.Any(b => string.Equals(b.Goal.Id, goalIdFilter, StringComparison.Ordinal)));
        if (doneFilter.HasValue)
            query = query.Where(t => t.Done == doneFilter.Value);
        if (urgentFilter.HasValue)
            query = query.Where(t => t.IsUrgent == urgentFilter.Value);
        if (importantFilter.HasValue)
            query = query.Where(t => t.IsImportant == importantFilter.Value);
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var kw = keyword.Trim();
            query = query.Where(t =>
                t.Title.Contains(kw, StringComparison.OrdinalIgnoreCase) ||
                t.Note.Contains(kw, StringComparison.OrdinalIgnoreCase) ||
                t.Persons.Any(p => p.Name.Contains(kw, StringComparison.OrdinalIgnoreCase)) ||
                t.Locations.Any(l => l.Name.Contains(kw, StringComparison.OrdinalIgnoreCase)));
        }

        var matched = query.OrderBy(t => t.Due ?? DateTime.MaxValue)
                           .ThenBy(t => t.StartTime ?? TimeSpan.MaxValue)
                           .ThenBy(t => t.Order)
                           .ToList();

        var items = matched.Skip(offset).Take(limit).Select(SerializeSchedule).ToList();
        return new
        {
            total = matched.Count,
            offset,
            limit,
            items
        };
    }

    private object ToolGetSchedule(JsonElement args)
    {
        var id = TryGetString(args, "id");
        if (string.IsNullOrWhiteSpace(id)) throw new ToolInputException("缺少必填参数 id");

        var task = _vm.Store.Schedules.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.Ordinal))
            ?? throw new ToolInputException($"找不到 id={id} 的日程");
        return SerializeSchedule(task);
    }

    private object ToolCreateSchedule(JsonElement args)
    {
        var title = (TryGetString(args, "title") ?? "").Trim();
        if (title.Length == 0) throw new ToolInputException("日程标题 title 不能为空");

        var dueStr = TryGetString(args, "due");
        var dueTimeStr = TryGetString(args, "dueTime");
        var startStr = TryGetString(args, "startTime");
        var endStr = TryGetString(args, "endTime");
        var note = TryGetString(args, "note") ?? "";
        var repeat = NormalizeRepeat(TryGetString(args, "repeat"));
        var remind = TryGetString(args, "remind");
        var urgent = TryGetBool(args, "urgent") ?? false;
        var important = TryGetBool(args, "important") ?? false;
        var tagNames = TryGetStringArray(args, "tags");
        var personNames = TryGetStringArray(args, "persons");
        var locationNames = TryGetStringArray(args, "locations");
        var goalId = TryGetString(args, "goalId");
        var nodeId = TryGetString(args, "nodeId");

        var (due, dueTime) = ParseOptionalDue(dueStr);
        if (!string.IsNullOrWhiteSpace(dueTimeStr))
            dueTime = ParseRequiredClock(dueTimeStr, "dueTime");
        if (dueTime.HasValue && !due.HasValue)
            due = DateTime.Today;

        var (startTime, endTime, adjusted) = ResolveTimeRange(due, startStr, endStr, exclude: null, out var resolvedDue);
        due = resolvedDue;

        var bindings = ResolveSingleGoalBinding(goalId, nodeId);
        var remindValue = remind is null
            ? RemindRules.Normalize(_vm.Settings.DefaultRemind)
            : RemindRules.Normalize(remind);

        var created = _vm.Store.AddSchedule(s =>
        {
            s.Title = title;
            s.Note = note;
            s.Due = due;
            s.DueTime = due.HasValue ? dueTime : null;
            s.StartTime = startTime;
            s.EndTime = endTime;
            s.Repeat = repeat;
            s.Remind = remindValue;
            s.IsUrgent = urgent;
            s.IsImportant = important;
            if (bindings.Count > 0)
                s.SetGoalBindings(bindings);
        });

        ApplyNamedEntities(tagNames, created.Tags, _vm.Store.GetOrCreateTag);
        ApplyNamedEntities(personNames, created.Persons, _vm.Store.GetOrCreatePerson);
        ApplyNamedEntities(locationNames, created.Locations, _vm.Store.GetOrCreateLocation);

        _vm.Rebuild();
        _vm.NotifyTaskTouched(created);

        return new
        {
            created = true,
            adjustedForConflict = adjusted,
            schedule = SerializeSchedule(created)
        };
    }

    private object ToolUpdateSchedule(JsonElement args)
    {
        var id = TryGetString(args, "id");
        if (string.IsNullOrWhiteSpace(id)) throw new ToolInputException("缺少必填参数 id");

        var task = _vm.Store.Schedules.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.Ordinal))
            ?? throw new ToolInputException($"找不到 id={id} 的日程");

        var clearDue = TryGetBool(args, "clearDue") ?? false;
        var clearDueTime = TryGetBool(args, "clearDueTime") ?? false;
        var clearTime = TryGetBool(args, "clearTime") ?? false;

        var hasDueProp = HasProp(args, "due");
        var hasDueTimeProp = HasProp(args, "dueTime");
        var (parsedDue, parsedDueTime) = hasDueProp ? ParseOptionalDue(TryGetString(args, "due")) : (task.Due, task.DueTime);

        var nextDue = clearDue ? (DateTime?)null : parsedDue;
        TimeSpan? nextDueTime = clearDue || clearDueTime
            ? null
            : hasDueTimeProp
                ? (string.IsNullOrWhiteSpace(TryGetString(args, "dueTime")) ? null : ParseRequiredClock(TryGetString(args, "dueTime")!, "dueTime"))
                : (hasDueProp ? (parsedDueTime ?? (parsedDue.HasValue ? task.DueTime : null)) : task.DueTime);

        var hasStartProp = HasProp(args, "startTime");
        var hasEndProp = HasProp(args, "endTime");
        var adjusted = false;

        if (clearDue || clearTime)
        {
            task.StartTime = null;
            task.EndTime = null;
            if (clearDue)
            {
                task.Due = null;
                task.DueTime = null;
            }
            else if (hasDueProp)
            {
                task.Due = nextDue;
                task.DueTime = nextDue.HasValue ? nextDueTime : null;
            }
            else if (clearDueTime || hasDueTimeProp)
            {
                if (nextDueTime.HasValue && !task.Due.HasValue) task.Due = DateTime.Today;
                task.DueTime = task.Due.HasValue ? nextDueTime : null;
            }
        }
        else if (hasStartProp || hasEndProp)
        {
            var startText = hasStartProp ? TryGetString(args, "startTime") : FormatTime(task.StartTime);
            var endText = hasEndProp ? TryGetString(args, "endTime") : FormatTime(task.EndTime);
            var (st, et, adj) = ResolveTimeRange(nextDue, startText, endText, exclude: task, out var resolvedDue);
            task.Due = resolvedDue;
            task.DueTime = resolvedDue.HasValue ? nextDueTime : null;
            task.StartTime = st;
            task.EndTime = et;
            adjusted = adj;
        }
        else if (hasDueProp || clearDueTime || hasDueTimeProp)
        {
            var effectiveDue = nextDue ?? (nextDueTime.HasValue ? DateTime.Today : (DateTime?)null);
            if (effectiveDue.HasValue && task.HasTime)
            {
                var duration = MainViewModel.DurationMinutes(task);
                var origStart = (int)task.StartTime!.Value.TotalMinutes;
                var free = _vm.FindFreeStart(effectiveDue.Value, origStart, duration, task);
                task.Due = effectiveDue.Value.Date;
                task.DueTime = nextDueTime;
                task.StartTime = TimeSpan.FromMinutes(free);
                task.EndTime = TimeSpan.FromMinutes(free + duration);
                adjusted = free != origStart;
            }
            else
            {
                task.Due = effectiveDue;
                task.DueTime = effectiveDue.HasValue ? nextDueTime : null;
            }
        }

        if (HasProp(args, "title"))
        {
            var t = (TryGetString(args, "title") ?? "").Trim();
            if (t.Length == 0) throw new ToolInputException("标题不能为空");
            task.Title = t;
        }
        if (HasProp(args, "note")) task.Note = TryGetString(args, "note") ?? "";
        if (HasProp(args, "repeat")) task.Repeat = NormalizeRepeat(TryGetString(args, "repeat"));
        if (HasProp(args, "remind")) task.Remind = RemindRules.Normalize(TryGetString(args, "remind"));
        if (TryGetBool(args, "urgent") is { } u) task.IsUrgent = u;
        if (TryGetBool(args, "important") is { } imp) task.IsImportant = imp;

        if (TryGetStringArray(args, "tags") is { } tagNames)
        {
            task.Tags.Clear();
            ApplyNamedEntities(tagNames, task.Tags, _vm.Store.GetOrCreateTag);
        }

        if (TryGetStringArray(args, "persons") is { } personNames)
        {
            task.Persons.Clear();
            ApplyNamedEntities(personNames, task.Persons, _vm.Store.GetOrCreatePerson);
        }

        if (TryGetStringArray(args, "locations") is { } locationNames)
        {
            task.Locations.Clear();
            ApplyNamedEntities(locationNames, task.Locations, _vm.Store.GetOrCreateLocation);
        }

        if (HasProp(args, "goalId"))
        {
            var goalId = TryGetString(args, "goalId");
            var nodeId = TryGetString(args, "nodeId");
            task.SetGoalBindings(string.IsNullOrWhiteSpace(goalId)
                ? Array.Empty<GoalBindingVM>()
                : ResolveSingleGoalBinding(goalId, nodeId));
        }

        _vm.Rebuild();
        _vm.NotifyTaskTouched(task);

        return new
        {
            updated = true,
            adjustedForConflict = adjusted,
            schedule = SerializeSchedule(task)
        };
    }

    private async Task<object> ToolDeleteScheduleAsync(JsonElement args)
    {
        var id = TryGetString(args, "id");
        if (string.IsNullOrWhiteSpace(id)) throw new ToolInputException("缺少必填参数 id");

        var task = _vm.Store.Schedules.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.Ordinal))
            ?? throw new ToolInputException($"找不到 id={id} 的日程");

        var dueText = task.Due?.ToString("yyyy-MM-dd") ?? "不定日期";
        var timeText = task.HasTime ? task.TimeLabel : "不占具体时段";
        var goalText = task.HasGoal ? task.GoalLabel : "无关联目标";
        var impact = $"日期：{dueText} · {timeText}\n所属目标：{goalText}\n确认后将删除此日程并记录同步墓碑。";

        var approved = await RequestDeleteApprovalAsync("日程", task.Title, impact);
        if (!approved)
            throw new ToolInputException("用户拒绝或超时未确认，已取消删除日程");

        if (!_vm.Store.Schedules.Contains(task))
            throw new ToolInputException("该日程在确认期间已被移除");

        var title = task.Title;
        _vm.Delete(task);

        return new { deleted = true, id, title };
    }

    private object ToolSetDone(JsonElement args)
    {
        var id = TryGetString(args, "id");
        var done = TryGetBool(args, "done");
        if (string.IsNullOrWhiteSpace(id) || !done.HasValue)
            throw new ToolInputException("缺少必填参数 id 或 done");

        var task = _vm.Store.Schedules.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.Ordinal))
            ?? throw new ToolInputException($"找不到 id={id} 的日程");

        if (task.Done != done.Value)
        {
            _vm.ToggleDone(task);
            _vm.NotifyTaskTouched(task);
        }

        return new
        {
            updated = true,
            schedule = SerializeSchedule(task)
        };
    }

    private object ToolListTags()
    {
        var list = _vm.Store.Tags.Select(tag => new
        {
            id = tag.Id,
            name = tag.Name,
            color = BrushHex(tag.Color),
            scheduleCount = _vm.Store.Schedules.Count(s => s.Tags.Contains(tag))
        }).ToList();

        return new { total = list.Count, items = list };
    }

    private object ToolSaveTag(JsonElement args)
    {
        var id = TryGetString(args, "id");
        var name = (TryGetString(args, "name") ?? "").Trim().TrimStart('#');
        var color = TryGetString(args, "color");

        TagVM tag;
        var isNew = false;

        if (!string.IsNullOrWhiteSpace(id))
        {
            tag = _vm.Store.Tags.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.Ordinal))
                ?? throw new ToolInputException($"找不到 id={id} 的标签");
            if (name.Length > 0)
                _vm.RenameTag(tag, name);
        }
        else
        {
            if (name.Length == 0) throw new ToolInputException("新建标签时 name 不能为空");
            var existing = _vm.Store.FindTag(name);
            isNew = existing is null;
            tag = _vm.Store.GetOrCreateTag(name);
        }

        if (!string.IsNullOrWhiteSpace(color))
            tag.Color = DayStore.BrushFromHex(color);

        _vm.Rebuild();

        return new
        {
            created = isNew,
            tag = new
            {
                id = tag.Id,
                name = tag.Name,
                color = BrushHex(tag.Color)
            }
        };
    }

    private async Task<object> ToolDeleteTagAsync(JsonElement args)
    {
        var id = TryGetString(args, "id");
        var name = (TryGetString(args, "name") ?? "").Trim().TrimStart('#');

        TagVM? tag = !string.IsNullOrWhiteSpace(id)
            ? _vm.Store.Tags.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.Ordinal))
            : (!string.IsNullOrWhiteSpace(name) ? _vm.Store.FindTag(name) : null);

        if (tag is null)
            throw new ToolInputException("找不到要删除的标签（请提供有效 id 或 name）");

        var schedCount = _vm.Store.Schedules.Count(s => s.Tags.Contains(tag));
        var presetCount = _vm.Store.Presets.Count(p => p.Tags.Contains(tag));
        var impact = $"将从 {schedCount} 条日程和 {presetCount} 个快捷卡片中移除标签「#{tag.Name}」（不会删除日程本身）。";

        var approved = await RequestDeleteApprovalAsync("标签", "#" + tag.Name, impact);
        if (!approved)
            throw new ToolInputException("用户拒绝或超时未确认，已取消删除标签");

        if (!_vm.Store.Tags.Contains(tag))
            throw new ToolInputException("该标签在确认期间已被移除");

        var tagId = tag.Id;
        var tagName = tag.Name;
        _vm.DeleteTag(tag);

        return new { deleted = true, id = tagId, name = tagName };
    }

    private object ToolListGoals()
    {
        var list = _vm.Store.Goals.Select(g => new
        {
            id = g.Id,
            title = g.Title,
            color = BrushHex(g.Color),
            isCompleted = g.IsCompleted,
            due = g.Due?.ToString("yyyy-MM-dd"),
            dueTime = g.DueTime is { } gdt ? DeadlineRules.FormatClock(gdt) : null,
            doneCount = g.DoneCount,
            total = g.Total,
            progressText = g.ProgressText,
            nodes = g.Nodes.Select(n => new
            {
                id = n.Id,
                title = n.Title,
                isCompleted = n.IsCompleted,
                due = n.Due?.ToString("yyyy-MM-dd"),
                dueTime = n.DueTime is { } ndt ? DeadlineRules.FormatClock(ndt) : null,
                taskCount = n.TaskCount,
                doneTaskCount = n.DoneTaskCount
            }).ToList(),
            edges = g.Edges.Select(e => new
            {
                fromId = e.FromId,
                toId = e.ToId,
                kind = e.Kind == GoalEdgeKind.Progress ? "progress" : "belong",
                label = e.Label
            }).ToList()
        }).ToList();

        return new { total = list.Count, items = list };
    }

    private object ToolSaveGoal(JsonElement args)
    {
        var id = TryGetString(args, "id");
        var title = (TryGetString(args, "title") ?? "").Trim();
        var hasDue = HasProp(args, "due");
        var hasDueTime = HasProp(args, "dueTime");
        var clearDueTime = TryGetBool(args, "clearDueTime") ?? false;
        var (due, parsedDueTime) = hasDue ? ParseOptionalDue(TryGetString(args, "due")) : (null, null);
        TimeSpan? explicitDueTime = hasDueTime
            ? (string.IsNullOrWhiteSpace(TryGetString(args, "dueTime")) ? null : ParseRequiredClock(TryGetString(args, "dueTime")!, "dueTime"))
            : null;
        var isCompleted = TryGetBool(args, "isCompleted");

        GoalVM goal;
        var isNew = false;

        if (!string.IsNullOrWhiteSpace(id))
        {
            goal = _vm.Store.Goals.FirstOrDefault(g => string.Equals(g.Id, id, StringComparison.Ordinal))
                ?? throw new ToolInputException($"找不到 id={id} 的目标");
            if (title.Length > 0) goal.Title = title;
        }
        else
        {
            if (title.Length == 0) throw new ToolInputException("新建目标时 title 不能为空");
            goal = _vm.Store.AddGoal(title);
            isNew = true;
        }

        if (hasDue)
        {
            goal.Due = due;
            if (!due.HasValue) goal.DueTime = null;
            else if (parsedDueTime.HasValue) goal.DueTime = parsedDueTime;
        }
        if (clearDueTime)
            goal.DueTime = null;
        else if (hasDueTime)
            goal.DueTime = goal.Due.HasValue ? explicitDueTime : null;

        if (isCompleted.HasValue && goal.IsCompleted != isCompleted.Value)
            goal.SetGoalCompleted(isCompleted.Value);

        _vm.Rebuild();

        return new
        {
            created = isNew,
            goal = new
            {
                id = goal.Id,
                title = goal.Title,
                isCompleted = goal.IsCompleted,
                due = goal.Due?.ToString("yyyy-MM-dd"),
                dueTime = goal.DueTime is { } dt ? DeadlineRules.FormatClock(dt) : null,
                progressText = goal.ProgressText
            }
        };
    }

    private async Task<object> ToolDeleteGoalAsync(JsonElement args)
    {
        var id = TryGetString(args, "id");
        var titleArg = (TryGetString(args, "title") ?? "").Trim();

        GoalVM? goal = !string.IsNullOrWhiteSpace(id)
            ? _vm.Store.Goals.FirstOrDefault(g => string.Equals(g.Id, id, StringComparison.Ordinal))
            : (!string.IsNullOrWhiteSpace(titleArg)
                ? _vm.Store.Goals.FirstOrDefault(g => string.Equals(g.Title, titleArg, StringComparison.OrdinalIgnoreCase))
                : null);

        if (goal is null)
            throw new ToolInputException("找不到要删除的目标（请提供有效 id 或 title）");

        var schedCount = _vm.Store.Schedules.Count(t => t.HasGoalRef(goal));
        var impact = $"包含 {goal.Nodes.Count} 个子目标；不会删除关联的 {schedCount} 条日程，仅解除目标挂载。";

        var approved = await RequestDeleteApprovalAsync("目标", goal.Title, impact);
        if (!approved)
            throw new ToolInputException("用户拒绝或超时未确认，已取消删除目标");

        if (!_vm.Store.Goals.Contains(goal))
            throw new ToolInputException("该目标在确认期间已被移除");

        var goalId = goal.Id;
        var goalTitle = goal.Title;
        _vm.DeleteGoal(goal);

        return new { deleted = true, id = goalId, title = goalTitle };
    }

    private object ToolGetCompletions(JsonElement args)
    {
        var scheduleId = TryGetString(args, "scheduleId");
        var dateFrom = TryGetString(args, "dateFrom");
        var dateTo = TryGetString(args, "dateTo");
        var limit = Math.Clamp(TryGetInt(args, "limit") ?? 100, 1, 1000);

        IEnumerable<CompletionLog.CompletionEntry> query = _vm.History.ReadAll();

        if (!string.IsNullOrWhiteSpace(scheduleId))
            query = query.Where(e => string.Equals(e.Id, scheduleId, StringComparison.Ordinal));

        if (!string.IsNullOrWhiteSpace(dateFrom))
            query = query.Where(e => string.CompareOrdinal(e.At.Length >= 10 ? e.At[..10] : e.At, dateFrom) >= 0);

        if (!string.IsNullOrWhiteSpace(dateTo))
            query = query.Where(e => string.CompareOrdinal(e.At.Length >= 10 ? e.At[..10] : e.At, dateTo) <= 0);

        var items = query.Reverse().Take(limit).Reverse().ToList();
        return new { total = items.Count, items };
    }

    // ==================== 独立置顶删除确认闸口 ====================

    private Task<bool> RequestDeleteApprovalAsync(string targetKind, string targetTitle, string impactDetail)
    {
        if (ConfirmDeleteAsync is { } customHook)
            return customHook(targetKind, targetTitle, impactDetail);

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var win = new McpDeleteConfirmWindow(targetKind, targetTitle, impactDetail, timeoutSeconds: 60);
        win.Closed += (_, _) => tcs.TrySetResult(win.Approved);
        win.Show();
        win.Activate();
        return tcs.Task;
    }

    // ==================== 辅助方法 ====================

    private (TimeSpan? Start, TimeSpan? End, bool Adjusted) ResolveTimeRange(
        DateTime? due,
        string? startStr,
        string? endStr,
        TaskVM? exclude,
        out DateTime? resolvedDue)
    {
        resolvedDue = due;
        if (string.IsNullOrWhiteSpace(startStr) && string.IsNullOrWhiteSpace(endStr))
            return (null, null, false);

        if (string.IsNullOrWhiteSpace(startStr) || !MainViewModel.TryParseClock(startStr, out var startMin))
            throw new ToolInputException("startTime 格式无效，请使用 HH:mm（如 09:30）");

        int endMin;
        if (string.IsNullOrWhiteSpace(endStr))
        {
            endMin = Math.Min(24 * 60, startMin + 30);
        }
        else if (!MainViewModel.TryParseClock(endStr, out endMin))
        {
            throw new ToolInputException("endTime 格式无效，请使用 HH:mm（如 10:30）");
        }

        if (endMin <= startMin)
            throw new ToolInputException("结束时间必须晚于开始时间");

        var targetDate = (due ?? DateTime.Today).Date;
        resolvedDue = targetDate;

        var duration = Math.Max(1, endMin - startMin);
        var free = _vm.FindFreeStart(targetDate, startMin, duration, exclude);

        return (TimeSpan.FromMinutes(free), TimeSpan.FromMinutes(free + duration), free != startMin);
    }

    private List<GoalBindingVM> ResolveSingleGoalBinding(string? goalId, string? nodeId)
    {
        if (string.IsNullOrWhiteSpace(goalId)) return new List<GoalBindingVM>();

        var goal = _vm.Store.Goals.FirstOrDefault(g => string.Equals(g.Id, goalId, StringComparison.Ordinal))
            ?? throw new ToolInputException($"找不到 id={goalId} 的目标");

        GoalNodeVM? node = null;
        if (!string.IsNullOrWhiteSpace(nodeId) && !string.Equals(nodeId, GoalNodeVM.RootId, StringComparison.Ordinal))
        {
            node = goal.Nodes.FirstOrDefault(n => string.Equals(n.Id, nodeId, StringComparison.Ordinal))
                ?? throw new ToolInputException($"目标「{goal.Title}」下找不到子目标 nodeId={nodeId}");
        }

        return new List<GoalBindingVM> { new(goal, node) };
    }

    private static string LaneKeyFor(TaskVM t) => MainViewModel.LaneFor(t) switch
    {
        LaneKind.Overdue => "overdue",
        LaneKind.Today => "today",
        LaneKind.Tomorrow => "tomorrow",
        LaneKind.ThisWeek => "thisWeek",
        LaneKind.Later => "later",
        _ => "future"
    };

    private static void ApplyNamedEntities<T>(
        List<string>? names,
        IList<T> target,
        Func<string, T?> getOrCreate)
    {
        if (names is null) return;
        foreach (var rawName in names)
        {
            var entity = getOrCreate(rawName);
            if (entity is not null && !target.Contains(entity))
                target.Add(entity);
        }
    }

    private static (DateTime? Date, TimeSpan? DueTime) ParseOptionalDue(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return (null, null);
        var (date, time) = DeadlineRules.ParseDue(raw);
        if (!date.HasValue)
            throw new ToolInputException("due 格式无效，请使用 yyyy-MM-dd 或 yyyy-MM-dd HH:mm");
        return (date, time);
    }

    private static TimeSpan ParseRequiredClock(string raw, string fieldName)
    {
        if (!DeadlineRules.TryParseClock(raw, out var time))
            throw new ToolInputException($"{fieldName} 格式无效，请使用 HH:mm（如 18:00）");
        return time;
    }

    private static object SerializeSchedule(TaskVM t) => new
    {
        id = t.Id,
        title = t.Title,
        note = t.Note,
        due = t.Due?.ToString("yyyy-MM-dd"),
        dueTime = t.HasDueTime ? DeadlineRules.FormatClock(t.DueTime!.Value) : null,
        lane = LaneKeyFor(t),
        startTime = FormatTime(t.StartTime),
        endTime = FormatTime(t.EndTime),
        repeat = t.Repeat,
        remind = t.Remind,
        done = t.Done,
        urgent = t.IsUrgent,
        important = t.IsImportant,
        tags = t.Tags.Select(tg => new { id = tg.Id, name = tg.Name }).ToList(),
        persons = t.Persons.Select(p => new { id = p.Id, name = p.Name }).ToList(),
        locations = t.Locations.Select(l => new { id = l.Id, name = l.Name }).ToList(),
        goals = t.GoalBindings.Select(b => new
        {
            goalId = b.Goal.Id,
            goalTitle = b.Goal.Title,
            nodeId = b.Node?.Id,
            nodeTitle = b.Node?.DisplayTitle
        }).ToList(),
        order = t.Order
    };

    private static string? FormatTime(TimeSpan? ts)
        => ts is { } v ? $"{(int)v.TotalHours:00}:{v.Minutes:00}" : null;

    private static DateTime? ParseOptionalDate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        if (DateTime.TryParse(raw.Trim(), out var dt)) return dt.Date;
        throw new ToolInputException($"日期格式无效：{raw}，请使用 yyyy-MM-dd");
    }

    private static string NormalizeRepeat(string? raw)
    {
        var t = (raw ?? "").Trim();
        return t switch
        {
            "" or "不重复" or "none" => "",
            "每天" or "每周" or "每月" => t,
            _ => throw new ToolInputException("repeat 仅支持：\"\"（不重复）、\"每天\"、\"每周\"、\"每月\"")
        };
    }

    private static string BrushHex(System.Windows.Media.Brush brush)
        => brush is System.Windows.Media.SolidColorBrush s
            ? $"#{s.Color.R:X2}{s.Color.G:X2}{s.Color.B:X2}"
            : "#527CE6";

    private static bool HasProp(JsonElement obj, string name)
        => obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(name, out _);

    private static string? TryGetString(JsonElement obj, string name)
        => obj.ValueKind == JsonValueKind.Object &&
           obj.TryGetProperty(name, out var el) &&
           el.ValueKind == JsonValueKind.String
            ? el.GetString()
            : null;

    private static bool? TryGetBool(JsonElement obj, string name)
    {
        if (obj.ValueKind != JsonValueKind.Object || !obj.TryGetProperty(name, out var el)) return null;
        return el.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
    }

    private static int? TryGetInt(JsonElement obj, string name)
        => obj.ValueKind == JsonValueKind.Object &&
           obj.TryGetProperty(name, out var el) &&
           el.ValueKind == JsonValueKind.Number &&
           el.TryGetInt32(out var val)
            ? val
            : null;

    private static List<string>? TryGetStringArray(JsonElement obj, string name)
    {
        if (obj.ValueKind != JsonValueKind.Object || !obj.TryGetProperty(name, out var el)) return null;
        if (el.ValueKind != JsonValueKind.Array) return null;
        var list = new List<string>();
        foreach (var item in el.EnumerateArray())
            if (item.ValueKind == JsonValueKind.String && item.GetString() is { } s && !string.IsNullOrWhiteSpace(s))
                list.Add(s);
        return list;
    }

    private static string JsonSuccess(JsonNode? id, object result)
    {
        var root = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id?.DeepClone(),
            ["result"] = JsonSerializer.SerializeToNode(result, JsonOptions)
        };
        return root.ToJsonString(JsonOptions);
    }

    private static string JsonError(JsonNode? id, int code, string message)
    {
        var root = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id?.DeepClone(),
            ["error"] = new JsonObject
            {
                ["code"] = code,
                ["message"] = message
            }
        };
        return root.ToJsonString(JsonOptions);
    }

    private sealed class McpProtocolException : Exception
    {
        public int Code { get; }
        public McpProtocolException(int code, string message) : base(message) => Code = code;
    }

    private sealed class ToolInputException : Exception
    {
        public ToolInputException(string message) : base(message) { }
    }

    // ==================== 14 个工具 Schema 定义 ====================

    private static object Schema(object properties, params string[] required) => new
    {
        type = "object",
        properties,
        required
    };

    private static object Annot(bool readOnly, bool destructive) => new
    {
        readOnlyHint = readOnly,
        destructiveHint = destructive,
        idempotentHint = readOnly
    };

    private static readonly object[] ToolDefinitions =
    {
        new
        {
            name = "get_overview",
            description = "获取日新今日概览：按六档位（逾期/今天/明天/本周/之后/未来事项）分组返回日程、统计摘要与目标进度。",
            inputSchema = Schema(new { }),
            annotations = Annot(readOnly: true, destructive: false)
        },
        new
        {
            name = "list_schedules",
            description = "按日期区间、标签、人物、地点、目标、完成状态、紧急/重要标记或关键词检索日程。",
            inputSchema = Schema(new
            {
                dateFrom = new { type = "string", description = "起始日期 yyyy-MM-dd" },
                dateTo = new { type = "string", description = "截止日期 yyyy-MM-dd" },
                tag = new { type = "string", description = "标签名或 ID" },
                person = new { type = "string", description = "人物名或 ID" },
                location = new { type = "string", description = "地点名或 ID" },
                goalId = new { type = "string", description = "目标 ID" },
                done = new { type = "boolean", description = "按完成状态过滤" },
                urgent = new { type = "boolean", description = "按是否紧急过滤" },
                important = new { type = "boolean", description = "按是否重要过滤" },
                keyword = new { type = "string", description = "标题、备注、人物或地点关键词" },
                includeUndated = new { type = "boolean", description = "是否包含无日期的未来事项（默认 true）" },
                offset = new { type = "integer", description = "分页偏移" },
                limit = new { type = "integer", description = "最大返回条数（1..500，默认 100）" }
            }),
            annotations = Annot(readOnly: true, destructive: false)
        },
        new
        {
            name = "get_schedule",
            description = "根据日程 ID 获取单条日程的完整字段详情。",
            inputSchema = Schema(new
            {
                id = new { type = "string", description = "日程 ID" }
            }, "id"),
            annotations = Annot(readOnly: true, destructive: false)
        },
        new
        {
            name = "create_schedule",
            description = "新建一条日程。若填写起止时间则自动避开当天已有时间冲突。",
            inputSchema = Schema(new
            {
                title = new { type = "string", description = "日程标题（必填）" },
                due = new { type = "string", description = "截止日期 yyyy-MM-dd 或 yyyy-MM-dd HH:mm；留空为未来事项（若给了时间则默认今天）" },
                dueTime = new { type = "string", description = "可选截止具体时间点 HH:mm（如 18:00）" },
                startTime = new { type = "string", description = "开始时刻 HH:mm" },
                endTime = new { type = "string", description = "结束时刻 HH:mm（省略则默认 +30 分钟）" },
                note = new { type = "string", description = "备注" },
                repeat = new { type = "string", description = "重复规则：空/每天/每周/每月" },
                remind = new { type = "string", description = "提醒节点逗号拼接：pre15,pre5,start,end（空=不提醒）" },
                urgent = new { type = "boolean", description = "是否紧急" },
                important = new { type = "boolean", description = "是否重要" },
                tags = new { type = "array", items = new { type = "string" }, description = "标签名列表（不存在会自动创建）" },
                persons = new { type = "array", items = new { type = "string" }, description = "人物名列表（不存在会自动创建）" },
                locations = new { type = "array", items = new { type = "string" }, description = "地点名列表（不存在会自动创建）" },
                goalId = new { type = "string", description = "关联目标 ID" },
                nodeId = new { type = "string", description = "可选子目标节点 ID" }
            }, "title"),
            annotations = Annot(readOnly: false, destructive: false)
        },
        new
        {
            name = "update_schedule",
            description = "修改既有日程的标题、日期、时间（自动防冲突）、标签、人物、地点、目标、提醒、紧急重要或备注。",
            inputSchema = Schema(new
            {
                id = new { type = "string", description = "日程 ID（必填）" },
                title = new { type = "string", description = "新标题" },
                due = new { type = "string", description = "新日期 yyyy-MM-dd 或 yyyy-MM-dd HH:mm" },
                dueTime = new { type = "string", description = "可选截止具体时间点 HH:mm（传空字符串可清除）" },
                clearDueTime = new { type = "boolean", description = "设为 true 可清除截止具体时间点，仅保留日期" },
                clearDue = new { type = "boolean", description = "设为 true 可清除日期与时段，移入未来事项" },
                startTime = new { type = "string", description = "新开始时刻 HH:mm" },
                endTime = new { type = "string", description = "新结束时刻 HH:mm" },
                clearTime = new { type = "boolean", description = "设为 true 可清除具体时段，只保留日期" },
                note = new { type = "string", description = "新备注" },
                repeat = new { type = "string", description = "重复规则：空/每天/每周/每月" },
                remind = new { type = "string", description = "提醒节点：pre15,pre5,start,end" },
                urgent = new { type = "boolean", description = "是否紧急" },
                important = new { type = "boolean", description = "是否重要" },
                tags = new { type = "array", items = new { type = "string" }, description = "覆盖为指定标签名列表" },
                persons = new { type = "array", items = new { type = "string" }, description = "覆盖为指定人物名列表（不存在会自动创建）" },
                locations = new { type = "array", items = new { type = "string" }, description = "覆盖为指定地点名列表（不存在会自动创建）" },
                goalId = new { type = "string", description = "目标 ID（传空字符串解除目标）" },
                nodeId = new { type = "string", description = "可选子目标节点 ID" }
            }, "id"),
            annotations = Annot(readOnly: false, destructive: false)
        },
        new
        {
            name = "delete_schedule",
            description = "【破坏性操作】删除指定日程并记录同步墓碑。调用前须向用户说明待删日程及影响并征得同意；日新本机还会弹出置顶窗口由用户即时确认。",
            inputSchema = Schema(new
            {
                id = new { type = "string", description = "待删日程 ID" }
            }, "id"),
            annotations = Annot(readOnly: false, destructive: true)
        },
        new
        {
            name = "set_done",
            description = "切换日程完成状态，并向 completions.ndjson 追加诚实的历史记录。",
            inputSchema = Schema(new
            {
                id = new { type = "string", description = "日程 ID" },
                done = new { type = "boolean", description = "目标完成状态 true/false" }
            }, "id", "done"),
            annotations = Annot(readOnly: false, destructive: false)
        },
        new
        {
            name = "list_tags",
            description = "列出全部标签及其使用次数与颜色。",
            inputSchema = Schema(new { }),
            annotations = Annot(readOnly: true, destructive: false)
        },
        new
        {
            name = "save_tag",
            description = "新建标签（不传 id）或修改既有标签名称/颜色（传 id）。",
            inputSchema = Schema(new
            {
                id = new { type = "string", description = "既有标签 ID（省略则按 name 获取或新建）" },
                name = new { type = "string", description = "标签名称" },
                color = new { type = "string", description = "十六进制颜色 #RRGGBB" }
            }),
            annotations = Annot(readOnly: false, destructive: false)
        },
        new
        {
            name = "delete_tag",
            description = "【破坏性操作】删除标签并从所有日程/快捷卡片上移除关联。需先向用户说明影响，日新本机将弹出即时确认窗。",
            inputSchema = Schema(new
            {
                id = new { type = "string", description = "标签 ID" },
                name = new { type = "string", description = "或按标签名定位" }
            }),
            annotations = Annot(readOnly: false, destructive: true)
        },
        new
        {
            name = "list_goals",
            description = "列出全部远景目标、进度、子目标节点与连线结构。",
            inputSchema = Schema(new { }),
            annotations = Annot(readOnly: true, destructive: false)
        },
        new
        {
            name = "save_goal",
            description = "新建目标（不传 id）或更新既有目标的名称、截止日期与完成状态。",
            inputSchema = Schema(new
            {
                id = new { type = "string", description = "既有目标 ID（省略则新建）" },
                title = new { type = "string", description = "目标名称" },
                due = new { type = "string", description = "可选截止日期 yyyy-MM-dd 或 yyyy-MM-dd HH:mm（传空字符串可清除）" },
                dueTime = new { type = "string", description = "可选截止具体时间点 HH:mm（传空字符串可清除）" },
                clearDueTime = new { type = "boolean", description = "设为 true 可清除截止具体时间点" },
                isCompleted = new { type = "boolean", description = "是否标记为已完成" }
            }),
            annotations = Annot(readOnly: false, destructive: false)
        },
        new
        {
            name = "delete_goal",
            description = "【破坏性操作】删除目标（不删除其下日程，仅解除关联）。需先向用户说明影响，日新本机将弹出即时确认窗。",
            inputSchema = Schema(new
            {
                id = new { type = "string", description = "目标 ID" },
                title = new { type = "string", description = "或按目标标题定位" }
            }),
            annotations = Annot(readOnly: false, destructive: true)
        },
        new
        {
            name = "get_completions",
            description = "只读查询 completions.ndjson 完成事件历史记录。",
            inputSchema = Schema(new
            {
                scheduleId = new { type = "string", description = "按日程 ID 过滤" },
                dateFrom = new { type = "string", description = "起始日期 yyyy-MM-dd" },
                dateTo = new { type = "string", description = "结束日期 yyyy-MM-dd" },
                limit = new { type = "integer", description = "最多返回条数（1..1000，默认 100）" }
            }),
            annotations = Annot(readOnly: true, destructive: false)
        }
    };
}
