using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Windows.Threading;
using DayNew.Core;
using DayNew.ViewModels;

namespace DayNew.Services;

/// <summary>
/// 本机持久化。不做服务端、不做同步，只保证：关掉再开，数据还在。
///
/// 三个要点：
///   1. **原子写**：先写 .tmp，再用 File.Replace 换上去。中途断电最多丢最后一次改动，
///      不会把原文件写坏成半个 JSON。
///   2. **留一份上一版**：每次换文件前把旧文件留成 data.prev.json，改坏了还能捞回来。
///   3. **合并写**：改动停下来 1 秒才落盘，拖拽时不会一秒写几十次。
/// </summary>
public sealed class StoreFile
{
    private const int DebounceMs = 1000;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        // 中文不转成 \uXXXX，文件要能直接打开看
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly DispatcherTimer _timer;

    // 后台写盘：UI 线程只负责拍快照，序列化和落盘都在线程池里
    private readonly object _writeLock = new();
    private long _snapshotVersion;
    private long _writtenVersion;

    public StoreFile()
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(DebounceMs) };
        _timer.Tick += (_, _) =>
        {
            _timer.Stop();
            KickSave();
        };
    }

    public static string Folder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DayNew");

    public static string DataPath => Path.Combine(Folder, "data.json");

    public static string PreviousPath => Path.Combine(Folder, "data.prev.json");

    public bool Exists => File.Exists(DataPath);

    /// <summary>读回三个集合。文件不存在返回 false（调用方自己去塞演示数据）。</summary>
    public bool TryLoad(DayStore store)
    {
        if (!File.Exists(DataPath)) return false;

        try
        {
            var json = File.ReadAllText(DataPath);
            var dto = JsonSerializer.Deserialize<StoreDto>(json, JsonOptions);
            if (dto is null) return false;

            Apply(dto, store);
            return true;
        }
        catch (Exception ex)
        {
            // 文件坏了：挪到一边留证据，别静默吞掉
            TryQuarantine(ex);
            return false;
        }
    }

    private static void TryQuarantine(Exception ex)
    {
        try
        {
            var target = Path.Combine(Folder, $"data.broken-{DateTime.Now:yyyyMMdd-HHmmss}.json");
            if (File.Exists(DataPath)) File.Move(DataPath, target);
            File.WriteAllText(Path.Combine(Folder, "load-error.txt"),
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}\n{ex}\n");
        }
        catch
        {
            // 连挪都挪不动就放弃，至少别崩
        }
    }

    /// <summary>标记「有改动」，1 秒后落盘。重复调用只会重置计时。</summary>
    public void MarkDirty(DayStore store)
    {
        _store = store;
        _timer.Stop();
        _timer.Start();
    }

    private DayStore? _store;

    /// <summary>立即落盘（关窗口、退出时用）。会等正在写的任务先写完。</summary>
    public void Flush(DayStore store)
    {
        _timer.Stop();
        _store = store;

        var snapshot = TakeSnapshot(store);
        lock (_writeLock)
        {
            if (snapshot.Version <= _writtenVersion) return;
            Write(snapshot);
            _writtenVersion = snapshot.Version;
        }
    }

    /// <summary>UI 线程拍快照（只是读字段、拼 DTO，很快），耗时的部分丢给线程池。</summary>
    private void KickSave()
    {
        if (_store is null) return;

        var snapshot = TakeSnapshot(_store);
        Task.Run(() =>
        {
            lock (_writeLock)
            {
                // 可能已经有更新的快照先写完了，旧的就不要再盖回去
                if (snapshot.Version <= _writtenVersion) return;

                Write(snapshot);
                _writtenVersion = snapshot.Version;
            }
        });
    }

    private Snapshot TakeSnapshot(DayStore store)
        => new(++_snapshotVersion, ToDto(store));

    private sealed record Snapshot(long Version, StoreDto Dto);

    private void Write(Snapshot snapshot)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            var json = JsonSerializer.Serialize(snapshot.Dto, JsonOptions);

            var tmp = DataPath + ".tmp";
            File.WriteAllText(tmp, json);

            if (File.Exists(DataPath))
            {
                try
                {
                    File.Replace(tmp, DataPath, PreviousPath, ignoreMetadataErrors: true);
                }
                catch (Exception)
                {
                    // 某些文件系统不支持 Replace，退化成「先备份再覆盖」
                    File.Copy(DataPath, PreviousPath, overwrite: true);
                    File.Move(tmp, DataPath, overwrite: true);
                }
            }
            else
            {
                File.Move(tmp, DataPath);
            }
        }
        catch (Exception ex)
        {
            try
            {
                Directory.CreateDirectory(Folder);
                File.AppendAllText(Path.Combine(Folder, "save-error.txt"),
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {ex.Message}\n");
            }
            catch
            {
                // 记不下来就算了，不能让保存失败把程序带崩
            }
        }
    }

    // ---------------- 实体 ↔ DTO ----------------

    private static StoreDto ToDto(DayStore store) => new()
    {
        Version = 1,
        Goals = store.Goals.Select(g => new GoalDto
        {
            Id = g.Id,
            Title = g.Title,
            Color = BrushToHex(g.Color),
            IsCompleted = g.IsCompleted,
            Due = g.Due?.ToString("yyyy-MM-dd"),
            RootX = g.RootNode.X,
            RootY = g.RootNode.Y,
            Nodes = g.Nodes.Select(n => new GoalNodeDto
            {
                Id = n.Id,
                Title = n.Title,
                IsCompleted = n.IsCompleted,
                Due = n.Due?.ToString("yyyy-MM-dd"),
                X = n.X,
                Y = n.Y
            }).ToList(),
            Edges = g.Edges.Select(e => new GoalEdgeDto
            {
                FromId = e.FromId,
                ToId = e.ToId,
                Kind = e.Kind == GoalEdgeKind.Progress ? "progress" : "belong",
                Label = string.IsNullOrWhiteSpace(e.Label) ? null : e.Label.Trim(),
                FromSide = e.FromSide == GoalPortSide.Auto ? null : e.FromSide.ToString().ToLowerInvariant(),
                ToSide = e.ToSide == GoalPortSide.Auto ? null : e.ToSide.ToString().ToLowerInvariant()
            }).ToList()
        }).ToList(),

        Tags = store.Tags.Select(t => new TagDto
        {
            Id = t.Id,
            Name = t.Name,
            Color = BrushToHex(t.Color)
        }).ToList(),

        Schedules = store.Schedules.Select(s => new ScheduleDto
        {
            Id = s.Id,
            Title = s.Title,
            Note = s.Note,
            Due = s.Due?.ToString("yyyy-MM-dd"),
            Start = s.StartTime is { } st ? $"{(int)st.TotalHours:00}:{st.Minutes:00}" : null,
            End = s.EndTime is { } et ? $"{(int)et.TotalHours:00}:{et.Minutes:00}" : null,
            Repeat = s.Repeat,
            Remind = RemindRules.Normalize(s.Remind),
            Done = s.Done,
            Urgent = s.IsUrgent,
            Important = s.IsImportant,
            GoalId = s.Goal?.Id,
            GoalRefs = s.GoalBindings.Select(b => new GoalRefDto
            {
                GoalId = b.Goal.Id,
                NodeId = b.Node?.Id
            }).ToList(),
            TagIds = s.Tags.Select(t => t.Id).ToList(),
            Order = s.Order
        }).ToList(),

        Presets = store.Presets.Select(p => new PresetDto
        {
            Id = p.Id,
            Title = p.Title,
            DurationMinutes = p.DurationMinutes,
            Remind = RemindRules.Normalize(p.Remind),
            Note = p.Note,
            Urgent = p.IsUrgent,
            Important = p.IsImportant,
            GoalId = p.Goal?.Id,
            GoalRefs = p.GoalBindings.Select(b => new GoalRefDto
            {
                GoalId = b.Goal.Id,
                NodeId = b.Node?.Id
            }).ToList(),
            TagIds = p.Tags.Select(t => t.Id).ToList()
        }).ToList()
    };

    private static void Apply(StoreDto dto, DayStore store)
    {
        store.Goals.Clear();
        store.Tags.Clear();
        store.Schedules.Clear();
        store.Presets.Clear();
        store.HasLoadedPresets = dto.Presets is not null;

        foreach (var g in dto.Goals)
        {
            var goal = new GoalVM
            {
                Id = string.IsNullOrEmpty(g.Id) ? "" : g.Id,
                Title = g.Title,
                Color = DayStore.BrushFromHex(g.Color),
                Due = ParseDate(g.Due)
            };

            if (g.Nodes is { Count: > 0 })
            {
                foreach (var n in g.Nodes)
                {
                    var nodeId = string.IsNullOrWhiteSpace(n.Id) ? Guid.NewGuid().ToString("N")[..10] : n.Id;
                    if (nodeId == GoalNodeVM.RootId) continue;

                    var node = new GoalNodeVM(goal, nodeId, n.Title)
                    {
                        X = n.X,
                        Y = n.Y,
                        Due = ParseDate(n.Due)
                    };
                    node.SetCompletedRaw(n.IsCompleted);
                    goal.Nodes.Add(node);
                    goal.AllCanvasNodes.Add(node);
                }

                if (g.Edges is not null)
                {
                    foreach (var e in g.Edges)
                    {
                        var kind = string.Equals(e.Kind, "progress", StringComparison.OrdinalIgnoreCase)
                            ? GoalEdgeKind.Progress
                            : GoalEdgeKind.Belong;
                        var edge = new GoalEdgeVM(e.FromId, e.ToId, kind)
                        {
                            Label = e.Label ?? "",
                            FromSide = ParsePortSide(e.FromSide),
                            ToSide = ParsePortSide(e.ToSide)
                        };
                        goal.Edges.Add(edge);
                    }
                }

                var hasCoords = g.RootX.HasValue || g.Nodes.Any(n => Math.Abs(n.X) > 0.1 || Math.Abs(n.Y) > 0.1);
                if (hasCoords)
                {
                    goal.RootNode.X = g.RootX ?? 20;
                    goal.RootNode.Y = g.RootY ?? 18;
                    goal.UpdateLayoutBoundsAndEdges();
                }
                else
                {
                    goal.AutoLayout();
                }
            }
            else
            {
                goal.AutoLayout();
            }

            if (g.IsCompleted)
                goal.SetGoalCompleted(true);

            store.Goals.Add(goal);
        }

        var tagIndex = 0;
        foreach (var t in dto.Tags)
        {
            // 老文件没有颜色字段：按顺序补一个色，不要全部一个颜色
            var color = string.IsNullOrWhiteSpace(t.Color)
                ? DayStore.TagPaletteColor(tagIndex)
                : DayStore.BrushFromHex(t.Color);

            store.Tags.Add(new TagVM(t.Name, string.IsNullOrEmpty(t.Id) ? null : t.Id) { Color = color });
            tagIndex++;
        }

        var goalById = store.Goals.ToDictionary(g => g.Id);
        var tagById = store.Tags.ToDictionary(t => t.Id);

        // 旧文件里所有标签都是同一个颜色（当时还没有可选色），这种“全是同色”就当成未分配，按顺序补色
        if (store.Tags.Count > 1)
        {
            var distinct = store.Tags
                .Select(t => (t.Color as System.Windows.Media.SolidColorBrush)?.Color)
                .Distinct()
                .Count();

            if (distinct == 1)
                for (var i = 0; i < store.Tags.Count; i++) store.Tags[i].Color = DayStore.TagPaletteColor(i);
        }

        foreach (var d in dto.Schedules)
        {
            var schedule = new TaskVM
            {
                Id = string.IsNullOrEmpty(d.Id) ? "" : d.Id,
                Title = d.Title,
                Note = d.Note,
                Due = ParseDate(d.Due),
                StartTime = ParseTime(d.Start),
                EndTime = ParseTime(d.End),
                Repeat = d.Repeat,
                Remind = RemindRules.Normalize(d.Remind),
                Done = d.Done,
                IsUrgent = d.Urgent,
                IsImportant = d.Important,
                Order = d.Order
            };

            schedule.SetGoalBindings(ResolveGoalBindings(d.GoalRefs, d.GoalId, goalById));

            foreach (var tagId in d.TagIds)
                if (tagById.TryGetValue(tagId, out var tag) && !schedule.Tags.Contains(tag))
                    schedule.Tags.Add(tag);

            store.Schedules.Add(schedule);
        }

        if (dto.Presets is null) return;

        foreach (var p in dto.Presets)
        {
            var preset = new SchedulePresetVM
            {
                Id = string.IsNullOrEmpty(p.Id) ? "" : p.Id,
                Title = p.Title,
                DurationMinutes = p.DurationMinutes,
                Remind = p.Remind is null ? RemindRules.Default : RemindRules.Normalize(p.Remind),
                Note = p.Note,
                IsUrgent = p.Urgent,
                IsImportant = p.Important
            };

            preset.SetGoalBindings(ResolveGoalBindings(p.GoalRefs, p.GoalId, goalById));

            foreach (var tagId in p.TagIds)
                if (tagById.TryGetValue(tagId, out var tag) && !preset.Tags.Contains(tag))
                    preset.Tags.Add(tag);

            store.Presets.Add(preset);
        }
    }

    private static List<GoalBindingVM> ResolveGoalBindings(
        List<GoalRefDto>? refs,
        string? legacyGoalId,
        Dictionary<string, GoalVM> goalById)
    {
        var list = new List<GoalBindingVM>();

        if (refs is { Count: > 0 })
        {
            foreach (var r in refs)
            {
                if (string.IsNullOrEmpty(r.GoalId) || !goalById.TryGetValue(r.GoalId, out var goal))
                    continue;

                GoalNodeVM? node = null;
                if (!string.IsNullOrEmpty(r.NodeId) && r.NodeId != GoalNodeVM.RootId)
                    node = goal.Nodes.FirstOrDefault(n => n.Id == r.NodeId);

                list.Add(new GoalBindingVM(goal, node));
            }

            return list;
        }

        if (!string.IsNullOrEmpty(legacyGoalId) && goalById.TryGetValue(legacyGoalId, out var legacyGoal))
            list.Add(new GoalBindingVM(legacyGoal, null));

        return list;
    }

    private static GoalPortSide ParsePortSide(string? text)
        => (text ?? "").Trim().ToLowerInvariant() switch
        {
            "top" => GoalPortSide.Top,
            "right" => GoalPortSide.Right,
            "bottom" => GoalPortSide.Bottom,
            "left" => GoalPortSide.Left,
            _ => GoalPortSide.Auto
        };

    private static DateTime? ParseDate(string? text)
        => DateTime.TryParse(text, out var value) ? value.Date : null;

    private static TimeSpan? ParseTime(string? text)
        => TimeSpan.TryParse(text, out var value) ? value : null;

    private static string BrushToHex(System.Windows.Media.Brush brush)
        => brush is System.Windows.Media.SolidColorBrush solid
            ? $"#{solid.Color.R:X2}{solid.Color.G:X2}{solid.Color.B:X2}"
            : "#527CE6";
}
