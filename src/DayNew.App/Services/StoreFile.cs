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

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        // 中文不转成 \uXXXX，文件要能直接打开看
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public static string SerializeDto(StoreDto dto) => JsonSerializer.Serialize(dto, JsonOptions);

    public static StoreDto? DeserializeDto(string json) => JsonSerializer.Deserialize<StoreDto>(json, JsonOptions);

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

    public static string Folder => Environment.GetEnvironmentVariable("DAYNEW_DATA_DIR") is { Length: > 0 } dir
        ? dir
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DayNew");

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

    internal static readonly string[] GoalFields = { "title", "color", "isCompleted", "due", "tree" };
    internal static readonly string[] TagFields = { "name", "color" };
    internal static readonly string[] PersonFields = { "name" };
    internal static readonly string[] LocationFields = { "name" };
    internal static readonly string[] ScheduleFields =
        { "title", "note", "due", "time", "repeat", "remind", "done", "flags", "goals", "tags", "persons", "locations", "order" };
    internal static readonly string[] PresetFields =
        { "title", "duration", "remind", "note", "flags", "goals", "tags", "persons", "locations" };

    public static StoreDto ToDto(DayStore store)
    {
        var goals = new List<GoalDto>(store.Goals.Count + store.DeletedGoals.Count);
        var activeGoalIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var g in store.Goals)
        {
            activeGoalIds.Add(g.Id);
            var raw = BuildRawGoalDto(g);
            store.BaselineGoals.TryGetValue(g.Id, out var baseline);
            StampChanges(raw, baseline, GoalFields, GoalFieldEquals,
                hlc => g.Hlc = hlc,
                hlcs => g.FieldHlcs = hlcs);
            store.BaselineGoals[g.Id] = CloneGoalDto(raw);
            goals.Add(raw);
        }
        PruneAndAppendTombstones(store.DeletedGoals, activeGoalIds, goals, g => g.Id, CloneGoalDto);

        var tags = new List<TagDto>(store.Tags.Count + store.DeletedTags.Count);
        var activeTagIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var t in store.Tags)
        {
            activeTagIds.Add(t.Id);
            var raw = BuildRawTagDto(t);
            store.BaselineTags.TryGetValue(t.Id, out var baseline);
            StampChanges(raw, baseline, TagFields, TagFieldEquals,
                hlc => t.Hlc = hlc,
                hlcs => t.FieldHlcs = hlcs);
            store.BaselineTags[t.Id] = CloneTagDto(raw);
            tags.Add(raw);
        }
        PruneAndAppendTombstones(store.DeletedTags, activeTagIds, tags, t => t.Id, CloneTagDto);

        var persons = new List<PersonDto>(store.Persons.Count + store.DeletedPersons.Count);
        var activePersonIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var p in store.Persons)
        {
            activePersonIds.Add(p.Id);
            var raw = BuildRawPersonDto(p);
            store.BaselinePersons.TryGetValue(p.Id, out var baseline);
            StampChanges(raw, baseline, PersonFields, PersonFieldEquals,
                hlc => p.Hlc = hlc,
                hlcs => p.FieldHlcs = hlcs);
            store.BaselinePersons[p.Id] = ClonePersonDto(raw);
            persons.Add(raw);
        }
        PruneAndAppendTombstones(store.DeletedPersons, activePersonIds, persons, p => p.Id, ClonePersonDto);

        var locations = new List<LocationDto>(store.Locations.Count + store.DeletedLocations.Count);
        var activeLocationIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var l in store.Locations)
        {
            activeLocationIds.Add(l.Id);
            var raw = BuildRawLocationDto(l);
            store.BaselineLocations.TryGetValue(l.Id, out var baseline);
            StampChanges(raw, baseline, LocationFields, LocationFieldEquals,
                hlc => l.Hlc = hlc,
                hlcs => l.FieldHlcs = hlcs);
            store.BaselineLocations[l.Id] = CloneLocationDto(raw);
            locations.Add(raw);
        }
        PruneAndAppendTombstones(store.DeletedLocations, activeLocationIds, locations, l => l.Id, CloneLocationDto);

        var schedules = new List<ScheduleDto>(store.Schedules.Count + store.DeletedSchedules.Count);
        var activeScheduleIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var s in store.Schedules)
        {
            activeScheduleIds.Add(s.Id);
            var raw = BuildRawScheduleDto(s);
            store.BaselineSchedules.TryGetValue(s.Id, out var baseline);
            StampChanges(raw, baseline, ScheduleFields, ScheduleFieldEquals,
                hlc => s.Hlc = hlc,
                hlcs => s.FieldHlcs = hlcs);
            store.BaselineSchedules[s.Id] = CloneScheduleDto(raw);
            schedules.Add(raw);
        }
        PruneAndAppendTombstones(store.DeletedSchedules, activeScheduleIds, schedules, s => s.Id, CloneScheduleDto);

        var presets = new List<PresetDto>(store.Presets.Count + store.DeletedPresets.Count);
        var activePresetIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var p in store.Presets)
        {
            activePresetIds.Add(p.Id);
            var raw = BuildRawPresetDto(p);
            store.BaselinePresets.TryGetValue(p.Id, out var baseline);
            StampChanges(raw, baseline, PresetFields, PresetFieldEquals,
                hlc => p.Hlc = hlc,
                hlcs => p.FieldHlcs = hlcs);
            store.BaselinePresets[p.Id] = ClonePresetDto(raw);
            presets.Add(raw);
        }
        PruneAndAppendTombstones(store.DeletedPresets, activePresetIds, presets, p => p.Id, ClonePresetDto);

        return new StoreDto
        {
            Version = 1,
            Goals = goals,
            Tags = tags,
            Persons = persons,
            Locations = locations,
            Schedules = schedules,
            Presets = presets
        };
    }

    /// <summary>生成不修改 Baseline 与 HLC 的纯内存快照（用于撤销/重做与状态比对）。</summary>
    public static StoreDto ToRawSnapshotDto(DayStore store)
    {
        var goals = store.Goals.Select(BuildRawGoalDto).ToList();
        goals.AddRange(store.DeletedGoals.Select(CloneGoalDto));

        var tags = store.Tags.Select(BuildRawTagDto).ToList();
        tags.AddRange(store.DeletedTags.Select(CloneTagDto));

        var persons = store.Persons.Select(BuildRawPersonDto).ToList();
        persons.AddRange(store.DeletedPersons.Select(ClonePersonDto));

        var locations = store.Locations.Select(BuildRawLocationDto).ToList();
        locations.AddRange(store.DeletedLocations.Select(CloneLocationDto));

        var schedules = store.Schedules.Select(BuildRawScheduleDto).ToList();
        schedules.AddRange(store.DeletedSchedules.Select(CloneScheduleDto));

        var presets = store.Presets.Select(BuildRawPresetDto).ToList();
        presets.AddRange(store.DeletedPresets.Select(ClonePresetDto));

        return new StoreDto
        {
            Version = 1,
            Goals = goals,
            Tags = tags,
            Persons = persons,
            Locations = locations,
            Schedules = schedules,
            Presets = presets
        };
    }

    /// <summary>撤销/重做恢复快照：保留原 Baseline 以便下次写盘正确推进 HLC，并提升被复活实体的 HLC。</summary>
    public static void ApplyUndoSnapshot(StoreDto dto, DayStore store)
    {
        var prevBaselineGoals = new Dictionary<string, GoalDto>(store.BaselineGoals, StringComparer.Ordinal);
        var prevBaselineTags = new Dictionary<string, TagDto>(store.BaselineTags, StringComparer.Ordinal);
        var prevBaselinePersons = new Dictionary<string, PersonDto>(store.BaselinePersons, StringComparer.Ordinal);
        var prevBaselineLocations = new Dictionary<string, LocationDto>(store.BaselineLocations, StringComparer.Ordinal);
        var prevBaselineSchedules = new Dictionary<string, ScheduleDto>(store.BaselineSchedules, StringComparer.Ordinal);
        var prevBaselinePresets = new Dictionary<string, PresetDto>(store.BaselinePresets, StringComparer.Ordinal);

        var prevDeletedGoals = store.DeletedGoals.Select(g => g.Id).ToHashSet(StringComparer.Ordinal);
        var prevDeletedTags = store.DeletedTags.Select(t => t.Id).ToHashSet(StringComparer.Ordinal);
        var prevDeletedPersons = store.DeletedPersons.Select(p => p.Id).ToHashSet(StringComparer.Ordinal);
        var prevDeletedLocations = store.DeletedLocations.Select(l => l.Id).ToHashSet(StringComparer.Ordinal);
        var prevDeletedSchedules = store.DeletedSchedules.Select(s => s.Id).ToHashSet(StringComparer.Ordinal);
        var prevDeletedPresets = store.DeletedPresets.Select(p => p.Id).ToHashSet(StringComparer.Ordinal);

        Apply(dto, store);

        RestoreBaselines(store.BaselineGoals, prevBaselineGoals);
        RestoreBaselines(store.BaselineTags, prevBaselineTags);
        RestoreBaselines(store.BaselinePersons, prevBaselinePersons);
        RestoreBaselines(store.BaselineLocations, prevBaselineLocations);
        RestoreBaselines(store.BaselineSchedules, prevBaselineSchedules);
        RestoreBaselines(store.BaselinePresets, prevBaselinePresets);

        foreach (var g in store.Goals)
            if (prevDeletedGoals.Contains(g.Id))
                BumpFieldsHlc(GoalFields, hlc => g.Hlc = hlc, g.FieldHlcs);

        foreach (var t in store.Tags)
            if (prevDeletedTags.Contains(t.Id))
                BumpFieldsHlc(TagFields, hlc => t.Hlc = hlc, t.FieldHlcs);

        foreach (var p in store.Persons)
            if (prevDeletedPersons.Contains(p.Id))
                BumpFieldsHlc(PersonFields, hlc => p.Hlc = hlc, p.FieldHlcs);

        foreach (var l in store.Locations)
            if (prevDeletedLocations.Contains(l.Id))
                BumpFieldsHlc(LocationFields, hlc => l.Hlc = hlc, l.FieldHlcs);

        foreach (var s in store.Schedules)
            if (prevDeletedSchedules.Contains(s.Id))
                BumpFieldsHlc(ScheduleFields, hlc => s.Hlc = hlc, s.FieldHlcs);

        foreach (var p in store.Presets)
            if (prevDeletedPresets.Contains(p.Id))
                BumpFieldsHlc(PresetFields, hlc => p.Hlc = hlc, p.FieldHlcs);
    }

    private static void RestoreBaselines<TDto>(Dictionary<string, TDto> target, Dictionary<string, TDto> source)
    {
        target.Clear();
        foreach (var kv in source) target[kv.Key] = kv.Value;
    }

    private static void BumpFieldsHlc(string[] fields, Action<string> setEntityHlc, Dictionary<string, string> fieldHlcs)
    {
        var tick = HlcClock.Tick();
        setEntityHlc(tick);
        foreach (var f in fields) fieldHlcs[f] = tick;
    }

    internal static GoalDto BuildRawGoalDto(GoalVM g) => new()
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
        }).ToList(),
        Hlc = g.Hlc,
        Del = false,
        FieldHlcs = new Dictionary<string, string>(g.FieldHlcs, StringComparer.Ordinal)
    };

    internal static TagDto BuildRawTagDto(TagVM t) => new()
    {
        Id = t.Id,
        Name = t.Name,
        Color = BrushToHex(t.Color),
        Hlc = t.Hlc,
        Del = false,
        FieldHlcs = new Dictionary<string, string>(t.FieldHlcs, StringComparer.Ordinal)
    };

    internal static PersonDto BuildRawPersonDto(PersonVM p) => new()
    {
        Id = p.Id,
        Name = p.Name,
        Hlc = p.Hlc,
        Del = false,
        FieldHlcs = new Dictionary<string, string>(p.FieldHlcs, StringComparer.Ordinal)
    };

    internal static LocationDto BuildRawLocationDto(LocationVM l) => new()
    {
        Id = l.Id,
        Name = l.Name,
        Hlc = l.Hlc,
        Del = false,
        FieldHlcs = new Dictionary<string, string>(l.FieldHlcs, StringComparer.Ordinal)
    };

    internal static ScheduleDto BuildRawScheduleDto(TaskVM s) => new()
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
        PersonIds = s.Persons.Select(p => p.Id).ToList(),
        LocationIds = s.Locations.Select(l => l.Id).ToList(),
        Order = s.Order,
        Hlc = s.Hlc,
        Del = false,
        FieldHlcs = new Dictionary<string, string>(s.FieldHlcs, StringComparer.Ordinal)
    };

    internal static PresetDto BuildRawPresetDto(SchedulePresetVM p) => new()
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
        TagIds = p.Tags.Select(t => t.Id).ToList(),
        PersonIds = p.Persons.Select(person => person.Id).ToList(),
        LocationIds = p.Locations.Select(loc => loc.Id).ToList(),
        Hlc = p.Hlc,
        Del = false,
        FieldHlcs = new Dictionary<string, string>(p.FieldHlcs, StringComparer.Ordinal)
    };

    public static void Apply(StoreDto dto, DayStore store)
    {
        store.Goals.Clear();
        store.Tags.Clear();
        store.Persons.Clear();
        store.Locations.Clear();
        store.Schedules.Clear();
        store.Presets.Clear();
        store.DeletedGoals.Clear();
        store.DeletedTags.Clear();
        store.DeletedPersons.Clear();
        store.DeletedLocations.Clear();
        store.DeletedSchedules.Clear();
        store.DeletedPresets.Clear();
        store.BaselineGoals.Clear();
        store.BaselineTags.Clear();
        store.BaselinePersons.Clear();
        store.BaselineLocations.Clear();
        store.BaselineSchedules.Clear();
        store.BaselinePresets.Clear();
        store.HasLoadedPresets = dto.Presets is not null;

        foreach (var g in dto.Goals)
        {
            ObserveHlc(g.Hlc, g.FieldHlcs);
            if (g.Del)
            {
                DayStore.UpsertTombstone(store.DeletedGoals, CloneGoalDto(g), x => x.Id);
                continue;
            }

            var goal = new GoalVM
            {
                Id = string.IsNullOrEmpty(g.Id) ? "" : g.Id,
                Title = g.Title,
                Color = DayStore.BrushFromHex(g.Color),
                Due = ParseDate(g.Due),
                Hlc = g.Hlc ?? "",
                FieldHlcs = CopyFieldHlcs(g.FieldHlcs)
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
            ObserveHlc(t.Hlc, t.FieldHlcs);
            if (t.Del)
            {
                DayStore.UpsertTombstone(store.DeletedTags, CloneTagDto(t), x => x.Id);
                continue;
            }

            // 老文件没有颜色字段：按顺序补一个色，不要全部一个颜色
            var color = string.IsNullOrWhiteSpace(t.Color)
                ? DayStore.TagPaletteColor(tagIndex)
                : DayStore.BrushFromHex(t.Color);

            store.Tags.Add(new TagVM(t.Name, string.IsNullOrEmpty(t.Id) ? null : t.Id)
            {
                Color = color,
                Hlc = t.Hlc ?? "",
                FieldHlcs = CopyFieldHlcs(t.FieldHlcs)
            });
            tagIndex++;
        }

        foreach (var p in dto.Persons ?? Enumerable.Empty<PersonDto>())
        {
            ObserveHlc(p.Hlc, p.FieldHlcs);
            if (p.Del)
            {
                DayStore.UpsertTombstone(store.DeletedPersons, ClonePersonDto(p), x => x.Id);
                continue;
            }

            store.Persons.Add(new PersonVM(p.Name, string.IsNullOrEmpty(p.Id) ? null : p.Id)
            {
                Hlc = p.Hlc ?? "",
                FieldHlcs = CopyFieldHlcs(p.FieldHlcs)
            });
        }

        foreach (var l in dto.Locations ?? Enumerable.Empty<LocationDto>())
        {
            ObserveHlc(l.Hlc, l.FieldHlcs);
            if (l.Del)
            {
                DayStore.UpsertTombstone(store.DeletedLocations, CloneLocationDto(l), x => x.Id);
                continue;
            }

            store.Locations.Add(new LocationVM(l.Name, string.IsNullOrEmpty(l.Id) ? null : l.Id)
            {
                Hlc = l.Hlc ?? "",
                FieldHlcs = CopyFieldHlcs(l.FieldHlcs)
            });
        }

        var goalById = new Dictionary<string, GoalVM>(StringComparer.Ordinal);
        foreach (var g in store.Goals)
            goalById[g.Id] = g;

        var tagById = new Dictionary<string, TagVM>(StringComparer.Ordinal);
        foreach (var t in store.Tags)
            tagById[t.Id] = t;

        var personById = new Dictionary<string, PersonVM>(StringComparer.Ordinal);
        foreach (var p in store.Persons)
            personById[p.Id] = p;

        var locationById = new Dictionary<string, LocationVM>(StringComparer.Ordinal);
        foreach (var l in store.Locations)
            locationById[l.Id] = l;

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
            ObserveHlc(d.Hlc, d.FieldHlcs);
            if (d.Del)
            {
                DayStore.UpsertTombstone(store.DeletedSchedules, CloneScheduleDto(d), x => x.Id);
                continue;
            }

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
                Order = d.Order,
                Hlc = d.Hlc ?? "",
                FieldHlcs = CopyFieldHlcs(d.FieldHlcs)
            };

            schedule.SetGoalBindings(ResolveGoalBindings(d.GoalRefs, d.GoalId, goalById));

            if (d.TagIds is not null)
                foreach (var tagId in d.TagIds)
                    if (tagById.TryGetValue(tagId, out var tag) && !schedule.Tags.Contains(tag))
                        schedule.Tags.Add(tag);

            if (d.PersonIds is not null)
                foreach (var personId in d.PersonIds)
                    if (personById.TryGetValue(personId, out var person) && !schedule.Persons.Contains(person))
                        schedule.Persons.Add(person);

            if (d.LocationIds is not null)
                foreach (var locationId in d.LocationIds)
                    if (locationById.TryGetValue(locationId, out var location) && !schedule.Locations.Contains(location))
                        schedule.Locations.Add(location);

            store.Schedules.Add(schedule);
        }

        if (dto.Presets is not null)
        {
            foreach (var p in dto.Presets)
            {
                ObserveHlc(p.Hlc, p.FieldHlcs);
                if (p.Del)
                {
                    DayStore.UpsertTombstone(store.DeletedPresets, ClonePresetDto(p), x => x.Id);
                    continue;
                }

                var preset = new SchedulePresetVM
                {
                    Id = string.IsNullOrEmpty(p.Id) ? "" : p.Id,
                    Title = p.Title,
                    DurationMinutes = p.DurationMinutes,
                    Remind = p.Remind is null ? RemindRules.Default : RemindRules.Normalize(p.Remind),
                    Note = p.Note,
                    IsUrgent = p.Urgent,
                    IsImportant = p.Important,
                    Hlc = p.Hlc ?? "",
                    FieldHlcs = CopyFieldHlcs(p.FieldHlcs)
                };

                preset.SetGoalBindings(ResolveGoalBindings(p.GoalRefs, p.GoalId, goalById));

                if (p.TagIds is not null)
                    foreach (var tagId in p.TagIds)
                        if (tagById.TryGetValue(tagId, out var tag) && !preset.Tags.Contains(tag))
                            preset.Tags.Add(tag);

                if (p.PersonIds is not null)
                    foreach (var personId in p.PersonIds)
                        if (personById.TryGetValue(personId, out var person) && !preset.Persons.Contains(person))
                            preset.Persons.Add(person);

                if (p.LocationIds is not null)
                    foreach (var locationId in p.LocationIds)
                        if (locationById.TryGetValue(locationId, out var location) && !preset.Locations.Contains(location))
                            preset.Locations.Add(location);

                store.Presets.Add(preset);
            }
        }

        CaptureBaselines(store);
    }

    private static void CaptureBaselines(DayStore store)
    {
        foreach (var g in store.Goals) store.BaselineGoals[g.Id] = BuildRawGoalDto(g);
        foreach (var t in store.Tags) store.BaselineTags[t.Id] = BuildRawTagDto(t);
        foreach (var p in store.Persons) store.BaselinePersons[p.Id] = BuildRawPersonDto(p);
        foreach (var l in store.Locations) store.BaselineLocations[l.Id] = BuildRawLocationDto(l);
        foreach (var s in store.Schedules) store.BaselineSchedules[s.Id] = BuildRawScheduleDto(s);
        foreach (var p in store.Presets) store.BaselinePresets[p.Id] = BuildRawPresetDto(p);
    }

    private static void ObserveHlc(string? hlc, Dictionary<string, string>? fieldHlcs)
    {
        HlcClock.Observe(hlc);
        if (fieldHlcs is null) return;
        foreach (var v in fieldHlcs.Values)
            HlcClock.Observe(v);
    }

    private static Dictionary<string, string> CopyFieldHlcs(Dictionary<string, string>? source)
        => source is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(source, StringComparer.Ordinal);

    private static void StampChanges<TDto>(
        TDto current,
        TDto? baseline,
        string[] fields,
        Func<TDto, TDto, string, bool> equals,
        Action<string> setVmHlc,
        Action<Dictionary<string, string>> setVmFieldHlcs) where TDto : class
    {
        var fieldHlcs = GetFieldHlcs(current);
        var entityHlc = GetEntityHlc(current);

        if (baseline is null)
        {
            string? tick = null;
            foreach (var f in fields)
            {
                if (fieldHlcs.TryGetValue(f, out var existing) && !string.IsNullOrEmpty(existing))
                {
                    entityHlc = HlcClock.Max(entityHlc, existing);
                    continue;
                }

                tick ??= HlcClock.Tick();
                fieldHlcs[f] = tick;
                entityHlc = HlcClock.Max(entityHlc, tick);
            }

            if (string.IsNullOrEmpty(entityHlc))
                entityHlc = tick ?? HlcClock.Tick();
        }
        else
        {
            string? tick = null;
            foreach (var f in fields)
            {
                if (equals(baseline, current, f)) continue;
                tick ??= HlcClock.Tick();
                fieldHlcs[f] = tick;
                entityHlc = HlcClock.Max(entityHlc, tick);
            }
        }

        SetEntityHlc(current, entityHlc);
        setVmHlc(entityHlc);
        setVmFieldHlcs(new Dictionary<string, string>(fieldHlcs, StringComparer.Ordinal));
    }

    private static Dictionary<string, string> GetFieldHlcs(object dto) => dto switch
    {
        GoalDto g => g.FieldHlcs ??= new Dictionary<string, string>(StringComparer.Ordinal),
        TagDto t => t.FieldHlcs ??= new Dictionary<string, string>(StringComparer.Ordinal),
        PersonDto p => p.FieldHlcs ??= new Dictionary<string, string>(StringComparer.Ordinal),
        LocationDto l => l.FieldHlcs ??= new Dictionary<string, string>(StringComparer.Ordinal),
        ScheduleDto s => s.FieldHlcs ??= new Dictionary<string, string>(StringComparer.Ordinal),
        PresetDto p => p.FieldHlcs ??= new Dictionary<string, string>(StringComparer.Ordinal),
        _ => new Dictionary<string, string>(StringComparer.Ordinal)
    };

    private static string GetEntityHlc(object dto) => dto switch
    {
        GoalDto g => g.Hlc,
        TagDto t => t.Hlc,
        PersonDto p => p.Hlc,
        LocationDto l => l.Hlc,
        ScheduleDto s => s.Hlc,
        PresetDto p => p.Hlc,
        _ => ""
    };

    private static void SetEntityHlc(object dto, string hlc)
    {
        switch (dto)
        {
            case GoalDto g: g.Hlc = hlc; break;
            case TagDto t: t.Hlc = hlc; break;
            case PersonDto p: p.Hlc = hlc; break;
            case LocationDto l: l.Hlc = hlc; break;
            case ScheduleDto s: s.Hlc = hlc; break;
            case PresetDto p: p.Hlc = hlc; break;
        }
    }

    private static void PruneAndAppendTombstones<T>(
        List<T> tombstones,
        HashSet<string> activeIds,
        List<T> target,
        Func<T, string> getId,
        Func<T, T> clone)
    {
        for (var i = tombstones.Count - 1; i >= 0; i--)
        {
            var id = getId(tombstones[i]);
            if (activeIds.Contains(id))
            {
                tombstones.RemoveAt(i);
                continue;
            }
            target.Add(clone(tombstones[i]));
        }
    }

    internal static bool GoalFieldEquals(GoalDto a, GoalDto b, string field) => field switch
    {
        "title" => a.Title == b.Title,
        "color" => a.Color == b.Color,
        "isCompleted" => a.IsCompleted == b.IsCompleted,
        "due" => a.Due == b.Due,
        "tree" => NullableDoubleEqual(a.RootX, b.RootX) &&
                  NullableDoubleEqual(a.RootY, b.RootY) &&
                  GoalNodesEqual(a.Nodes, b.Nodes) &&
                  GoalEdgesEqual(a.Edges, b.Edges),
        _ => true
    };

    internal static bool TagFieldEquals(TagDto a, TagDto b, string field) => field switch
    {
        "name" => a.Name == b.Name,
        "color" => a.Color == b.Color,
        _ => true
    };

    internal static bool PersonFieldEquals(PersonDto a, PersonDto b, string field) => field switch
    {
        "name" => a.Name == b.Name,
        _ => true
    };

    internal static bool LocationFieldEquals(LocationDto a, LocationDto b, string field) => field switch
    {
        "name" => a.Name == b.Name,
        _ => true
    };

    internal static bool ScheduleFieldEquals(ScheduleDto a, ScheduleDto b, string field) => field switch
    {
        "title" => a.Title == b.Title,
        "note" => a.Note == b.Note,
        "due" => a.Due == b.Due,
        "time" => a.Start == b.Start && a.End == b.End,
        "repeat" => a.Repeat == b.Repeat,
        "remind" => a.Remind == b.Remind,
        "done" => a.Done == b.Done,
        "flags" => a.Urgent == b.Urgent && a.Important == b.Important,
        "goals" => a.GoalId == b.GoalId && GoalRefsEqual(a.GoalRefs, b.GoalRefs),
        "tags" => StringListEqual(a.TagIds, b.TagIds),
        "persons" => StringListEqual(a.PersonIds, b.PersonIds),
        "locations" => StringListEqual(a.LocationIds, b.LocationIds),
        "order" => a.Order == b.Order,
        _ => true
    };

    internal static bool PresetFieldEquals(PresetDto a, PresetDto b, string field) => field switch
    {
        "title" => a.Title == b.Title,
        "duration" => a.DurationMinutes == b.DurationMinutes,
        "remind" => a.Remind == b.Remind,
        "note" => a.Note == b.Note,
        "flags" => a.Urgent == b.Urgent && a.Important == b.Important,
        "goals" => a.GoalId == b.GoalId && GoalRefsEqual(a.GoalRefs, b.GoalRefs),
        "tags" => StringListEqual(a.TagIds, b.TagIds),
        "persons" => StringListEqual(a.PersonIds, b.PersonIds),
        "locations" => StringListEqual(a.LocationIds, b.LocationIds),
        _ => true
    };

    private static bool NullableDoubleEqual(double? a, double? b)
    {
        if (!a.HasValue && !b.HasValue) return true;
        if (!a.HasValue || !b.HasValue) return false;
        return Math.Abs(a.Value - b.Value) < 0.01;
    }

    private static bool GoalNodesEqual(List<GoalNodeDto>? a, List<GoalNodeDto>? b)
    {
        var la = a ?? new List<GoalNodeDto>();
        var lb = b ?? new List<GoalNodeDto>();
        if (la.Count != lb.Count) return false;
        for (var i = 0; i < la.Count; i++)
        {
            if (la[i].Id != lb[i].Id ||
                la[i].Title != lb[i].Title ||
                la[i].IsCompleted != lb[i].IsCompleted ||
                la[i].Due != lb[i].Due ||
                Math.Abs(la[i].X - lb[i].X) >= 0.01 ||
                Math.Abs(la[i].Y - lb[i].Y) >= 0.01)
                return false;
        }
        return true;
    }

    private static bool GoalEdgesEqual(List<GoalEdgeDto>? a, List<GoalEdgeDto>? b)
    {
        var la = a ?? new List<GoalEdgeDto>();
        var lb = b ?? new List<GoalEdgeDto>();
        if (la.Count != lb.Count) return false;
        for (var i = 0; i < la.Count; i++)
        {
            if (la[i].FromId != lb[i].FromId ||
                la[i].ToId != lb[i].ToId ||
                la[i].Kind != lb[i].Kind ||
                la[i].Label != lb[i].Label ||
                la[i].FromSide != lb[i].FromSide ||
                la[i].ToSide != lb[i].ToSide)
                return false;
        }
        return true;
    }

    private static bool GoalRefsEqual(List<GoalRefDto>? a, List<GoalRefDto>? b)
    {
        var la = a ?? new List<GoalRefDto>();
        var lb = b ?? new List<GoalRefDto>();
        if (la.Count != lb.Count) return false;
        for (var i = 0; i < la.Count; i++)
        {
            if (la[i].GoalId != lb[i].GoalId || la[i].NodeId != lb[i].NodeId)
                return false;
        }
        return true;
    }

    private static bool StringListEqual(List<string>? a, List<string>? b)
    {
        var la = a ?? new List<string>();
        var lb = b ?? new List<string>();
        if (la.Count != lb.Count) return false;
        for (var i = 0; i < la.Count; i++)
            if (la[i] != lb[i]) return false;
        return true;
    }

    internal static GoalDto CloneGoalDto(GoalDto g) => new()
    {
        Id = g.Id,
        Title = g.Title,
        Color = g.Color,
        IsCompleted = g.IsCompleted,
        Due = g.Due,
        RootX = g.RootX,
        RootY = g.RootY,
        Nodes = (g.Nodes ?? new()).Select(n => new GoalNodeDto
        {
            Id = n.Id,
            Title = n.Title,
            IsCompleted = n.IsCompleted,
            Due = n.Due,
            X = n.X,
            Y = n.Y
        }).ToList(),
        Edges = (g.Edges ?? new()).Select(e => new GoalEdgeDto
        {
            FromId = e.FromId,
            ToId = e.ToId,
            Kind = e.Kind,
            Label = e.Label,
            FromSide = e.FromSide,
            ToSide = e.ToSide
        }).ToList(),
        Hlc = g.Hlc,
        Del = g.Del,
        FieldHlcs = CopyFieldHlcs(g.FieldHlcs)
    };

    internal static TagDto CloneTagDto(TagDto t) => new()
    {
        Id = t.Id,
        Name = t.Name,
        Color = t.Color,
        Hlc = t.Hlc,
        Del = t.Del,
        FieldHlcs = CopyFieldHlcs(t.FieldHlcs)
    };

    internal static PersonDto ClonePersonDto(PersonDto p) => new()
    {
        Id = p.Id,
        Name = p.Name,
        Hlc = p.Hlc,
        Del = p.Del,
        FieldHlcs = CopyFieldHlcs(p.FieldHlcs)
    };

    internal static LocationDto CloneLocationDto(LocationDto l) => new()
    {
        Id = l.Id,
        Name = l.Name,
        Hlc = l.Hlc,
        Del = l.Del,
        FieldHlcs = CopyFieldHlcs(l.FieldHlcs)
    };

    internal static ScheduleDto CloneScheduleDto(ScheduleDto s) => new()
    {
        Id = s.Id,
        Title = s.Title,
        Note = s.Note,
        Due = s.Due,
        Start = s.Start,
        End = s.End,
        Repeat = s.Repeat,
        Remind = s.Remind,
        Done = s.Done,
        Urgent = s.Urgent,
        Important = s.Important,
        GoalId = s.GoalId,
        GoalRefs = (s.GoalRefs ?? new()).Select(r => new GoalRefDto { GoalId = r.GoalId, NodeId = r.NodeId }).ToList(),
        TagIds = (s.TagIds ?? new()).ToList(),
        PersonIds = (s.PersonIds ?? new()).ToList(),
        LocationIds = (s.LocationIds ?? new()).ToList(),
        Order = s.Order,
        Hlc = s.Hlc,
        Del = s.Del,
        FieldHlcs = CopyFieldHlcs(s.FieldHlcs)
    };

    internal static PresetDto ClonePresetDto(PresetDto p) => new()
    {
        Id = p.Id,
        Title = p.Title,
        DurationMinutes = p.DurationMinutes,
        Remind = p.Remind,
        Note = p.Note,
        Urgent = p.Urgent,
        Important = p.Important,
        GoalId = p.GoalId,
        GoalRefs = (p.GoalRefs ?? new()).Select(r => new GoalRefDto { GoalId = r.GoalId, NodeId = r.NodeId }).ToList(),
        TagIds = (p.TagIds ?? new()).ToList(),
        PersonIds = (p.PersonIds ?? new()).ToList(),
        LocationIds = (p.LocationIds ?? new()).ToList(),
        Hlc = p.Hlc,
        Del = p.Del,
        FieldHlcs = CopyFieldHlcs(p.FieldHlcs)
    };

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

    /// <summary>
    /// 按实体 ID 合并两份 StoreDto：保留两边独有条目，相同 ID 以 incoming 为准。
    /// </summary>
    public static StoreDto MergeDtos(StoreDto current, StoreDto incoming)
    {
        List<PresetDto>? mergedPresets = current.Presets is null && incoming.Presets is null
            ? null
            : MergeListById(
                current.Presets ?? new List<PresetDto>(),
                incoming.Presets ?? new List<PresetDto>(),
                p => p.Id);

        return new StoreDto
        {
            Version = Math.Max(current.Version, incoming.Version),
            Goals = MergeListById(current.Goals, incoming.Goals, g => g.Id),
            Tags = MergeListById(current.Tags, incoming.Tags, t => t.Id),
            Persons = MergeListById(current.Persons, incoming.Persons, p => p.Id),
            Locations = MergeListById(current.Locations, incoming.Locations, l => l.Id),
            Schedules = MergeListById(current.Schedules, incoming.Schedules, s => s.Id),
            Presets = mergedPresets
        };
    }

    private static List<T> MergeListById<T>(
        IReadOnlyList<T>? current,
        IReadOnlyList<T>? incoming,
        Func<T, string?> getId)
    {
        var curList = current ?? Array.Empty<T>();
        var incList = incoming ?? Array.Empty<T>();
        var result = new List<T>(curList.Count + incList.Count);
        var indexById = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var item in curList)
        {
            var id = getId(item);
            if (!string.IsNullOrWhiteSpace(id) && indexById.TryGetValue(id, out var existingIdx))
            {
                result[existingIdx] = item;
                continue;
            }

            if (!string.IsNullOrWhiteSpace(id))
                indexById[id] = result.Count;
            result.Add(item);
        }

        foreach (var item in incList)
        {
            var id = getId(item);
            if (!string.IsNullOrWhiteSpace(id) && indexById.TryGetValue(id, out var existingIdx))
            {
                result[existingIdx] = item;
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(id))
                    indexById[id] = result.Count;
                result.Add(item);
            }
        }

        return result;
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
