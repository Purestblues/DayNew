using DayNew.Core;

namespace DayNew.Services;

/// <summary>
/// 多设备快照的 HLC 字段级合并引擎：
///   - 按实体 Id 对齐，互不丢弃各自新增条目；
///   - 支持 Del=true 墓碑删除语义（仅当非删除端 HLC 严格晚于删除端时才恢复）；
///   - 核心内容字段（title/note/due/time）在同一同步周期内被双端并发改动时，
///     主条目取 HLC 较新者，并将落败方克隆为 "[冲突] ..." 副本保留。
/// </summary>
public static class SyncMergeEngine
{
    public sealed record MergeReport(
        StoreDto MergedDto,
        int UpdatedCount,
        int ConflictCopiesCount,
        string MaxRemoteHlc);

    private static readonly HashSet<string> CoreScheduleFields = new(StringComparer.Ordinal)
    {
        "title", "note", "due", "time"
    };

    public static MergeReport MergeStore(StoreDto local, StoreDto remote, string? lastSyncedRemoteHlc)
    {
        var updatedCount = 0;
        var conflictCopiesCount = 0;
        var maxRemoteHlc = "";

        void TrackRemote(string? hlc, Dictionary<string, string>? fieldHlcs)
        {
            if (!string.IsNullOrEmpty(hlc))
                maxRemoteHlc = HlcClock.Max(maxRemoteHlc, hlc);
            if (fieldHlcs is null) return;
            foreach (var v in fieldHlcs.Values)
                if (!string.IsNullOrEmpty(v))
                    maxRemoteHlc = HlcClock.Max(maxRemoteHlc, v);
        }

        foreach (var g in remote.Goals ?? Enumerable.Empty<GoalDto>()) TrackRemote(g.Hlc, g.FieldHlcs);
        foreach (var t in remote.Tags ?? Enumerable.Empty<TagDto>()) TrackRemote(t.Hlc, t.FieldHlcs);
        foreach (var s in remote.Schedules ?? Enumerable.Empty<ScheduleDto>()) TrackRemote(s.Hlc, s.FieldHlcs);
        if (remote.Presets is not null)
            foreach (var p in remote.Presets) TrackRemote(p.Hlc, p.FieldHlcs);

        var mergedGoals = MergeCollection(
            local.Goals,
            remote.Goals,
            g => g.Id,
            g => g.Del,
            g => g.Hlc,
            g => g.FieldHlcs,
            StoreFile.GoalFields,
            StoreFile.GoalFieldEquals,
            CopyGoalField,
            SetGoalHlc,
            StoreFile.CloneGoalDto,
            ref updatedCount);

        var mergedTags = MergeCollection(
            local.Tags,
            remote.Tags,
            t => t.Id,
            t => t.Del,
            t => t.Hlc,
            t => t.FieldHlcs,
            StoreFile.TagFields,
            StoreFile.TagFieldEquals,
            CopyTagField,
            SetTagHlc,
            StoreFile.CloneTagDto,
            ref updatedCount);

        var mergedSchedules = MergeSchedules(
            local.Schedules,
            remote.Schedules,
            lastSyncedRemoteHlc,
            ref updatedCount,
            ref conflictCopiesCount);

        List<PresetDto>? mergedPresets = local.Presets is null && remote.Presets is null
            ? null
            : MergeCollection(
                local.Presets,
                remote.Presets,
                p => p.Id,
                p => p.Del,
                p => p.Hlc,
                p => p.FieldHlcs,
                StoreFile.PresetFields,
                StoreFile.PresetFieldEquals,
                CopyPresetField,
                SetPresetHlc,
                StoreFile.ClonePresetDto,
                ref updatedCount);

        var merged = new StoreDto
        {
            Version = Math.Max(local.Version, remote.Version),
            Goals = mergedGoals,
            Tags = mergedTags,
            Schedules = mergedSchedules,
            Presets = mergedPresets
        };

        return new MergeReport(merged, updatedCount, conflictCopiesCount, maxRemoteHlc);
    }

    private static List<ScheduleDto> MergeSchedules(
        List<ScheduleDto>? localList,
        List<ScheduleDto>? remoteList,
        string? lastSyncedRemoteHlc,
        ref int updatedCount,
        ref int conflictCopiesCount)
    {
        var lList = localList ?? new List<ScheduleDto>();
        var rList = remoteList ?? new List<ScheduleDto>();
        var localById = IndexById(lList, s => s.Id);
        var remoteById = IndexById(rList, s => s.Id);

        var result = new List<ScheduleDto>(lList.Count + rList.Count);
        var conflicts = new List<ScheduleDto>();
        var seenIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var l in lList)
        {
            if (string.IsNullOrWhiteSpace(l.Id))
            {
                result.Add(StoreFile.CloneScheduleDto(l));
                continue;
            }

            if (!seenIds.Add(l.Id)) continue;

            if (!remoteById.TryGetValue(l.Id, out var r))
            {
                result.Add(StoreFile.CloneScheduleDto(l));
                continue;
            }

            if (l.Del || r.Del)
            {
                var winner = ResolveTombstone(
                    l, r, l.Del, r.Del,
                    MaxEntityHlc(l.Hlc, l.FieldHlcs),
                    MaxEntityHlc(r.Hlc, r.FieldHlcs),
                    out var remoteWon);
                result.Add(StoreFile.CloneScheduleDto(winner));
                if (remoteWon) updatedCount++;
                continue;
            }

            var merged = StoreFile.CloneScheduleDto(l);
            merged.FieldHlcs ??= new Dictionary<string, string>(StringComparer.Ordinal);

            var hasCoreConflict = false;
            var changedFromLocal = false;

            foreach (var field in StoreFile.ScheduleFields)
            {
                var lFieldHlc = GetFieldHlc(l.Hlc, l.FieldHlcs, field);
                var rFieldHlc = GetFieldHlc(r.Hlc, r.FieldHlcs, field);

                if (StoreFile.ScheduleFieldEquals(l, r, field))
                {
                    var maxF = HlcClock.Max(lFieldHlc, rFieldHlc);
                    if (!string.IsNullOrEmpty(maxF))
                        merged.FieldHlcs[field] = maxF;
                    continue;
                }

                if (CoreScheduleFields.Contains(field) &&
                    !string.IsNullOrEmpty(lastSyncedRemoteHlc) &&
                    HlcClock.Compare(lFieldHlc, lastSyncedRemoteHlc) > 0 &&
                    HlcClock.Compare(rFieldHlc, lastSyncedRemoteHlc) > 0)
                {
                    hasCoreConflict = true;
                }

                if (HlcClock.Compare(rFieldHlc, lFieldHlc) > 0)
                {
                    CopyScheduleField(r, merged, field);
                    if (!string.IsNullOrEmpty(rFieldHlc))
                        merged.FieldHlcs[field] = rFieldHlc;
                    changedFromLocal = true;
                }
                else
                {
                    if (!string.IsNullOrEmpty(lFieldHlc))
                        merged.FieldHlcs[field] = lFieldHlc;
                }
            }

            merged.Hlc = MaxEntityHlc(HlcClock.Max(l.Hlc, r.Hlc), merged.FieldHlcs);
            result.Add(merged);
            if (changedFromLocal) updatedCount++;

            if (hasCoreConflict)
            {
                var lMax = MaxEntityHlc(l.Hlc, l.FieldHlcs);
                var rMax = MaxEntityHlc(r.Hlc, r.FieldHlcs);
                var loser = HlcClock.Compare(rMax, lMax) > 0 ? l : r;
                var conflictTitle = loser.Title.StartsWith("[冲突]", StringComparison.Ordinal)
                    ? loser.Title
                    : $"[冲突] {loser.Title}";

                var alreadyExists = result.Concat(conflicts).Any(existing =>
                    !existing.Del &&
                    existing.Title == conflictTitle &&
                    existing.Due == loser.Due &&
                    existing.Start == loser.Start &&
                    existing.End == loser.End &&
                    existing.Note == loser.Note);

                if (!alreadyExists)
                {
                    var copy = StoreFile.CloneScheduleDto(loser);
                    copy.Id = Guid.NewGuid().ToString("N")[..10];
                    copy.Title = conflictTitle;
                    copy.Del = false;
                    copy.Hlc = HlcClock.Tick();
                    copy.FieldHlcs = StoreFile.ScheduleFields.ToDictionary(
                        f => f, _ => copy.Hlc, StringComparer.Ordinal);
                    conflicts.Add(copy);
                    conflictCopiesCount++;
                    updatedCount++;
                }
            }
        }

        foreach (var r in rList)
        {
            if (string.IsNullOrWhiteSpace(r.Id) || seenIds.Contains(r.Id)) continue;
            seenIds.Add(r.Id);
            result.Add(StoreFile.CloneScheduleDto(r));
            updatedCount++;
        }

        result.AddRange(conflicts);
        return result;
    }

    private static List<T> MergeCollection<T>(
        List<T>? localList,
        List<T>? remoteList,
        Func<T, string> getId,
        Func<T, bool> getDel,
        Func<T, string> getHlc,
        Func<T, Dictionary<string, string>?> getFieldHlcs,
        string[] fields,
        Func<T, T, string, bool> fieldEquals,
        Action<T, T, string> copyField,
        Action<T, string, Dictionary<string, string>> setHlc,
        Func<T, T> clone,
        ref int updatedCount) where T : class
    {
        var lList = localList ?? new List<T>();
        var rList = remoteList ?? new List<T>();
        var remoteById = IndexById(rList, getId);

        var result = new List<T>(lList.Count + rList.Count);
        var seenIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var l in lList)
        {
            var id = getId(l);
            if (string.IsNullOrWhiteSpace(id))
            {
                result.Add(clone(l));
                continue;
            }

            if (!seenIds.Add(id)) continue;

            if (!remoteById.TryGetValue(id, out var r))
            {
                result.Add(clone(l));
                continue;
            }

            if (getDel(l) || getDel(r))
            {
                var winner = ResolveTombstone(
                    l, r, getDel(l), getDel(r),
                    MaxEntityHlc(getHlc(l), getFieldHlcs(l)),
                    MaxEntityHlc(getHlc(r), getFieldHlcs(r)),
                    out var remoteWon);
                result.Add(clone(winner));
                if (remoteWon) updatedCount++;
                continue;
            }

            var merged = clone(l);
            var mergedFieldHlcs = new Dictionary<string, string>(
                getFieldHlcs(merged) ?? new Dictionary<string, string>(), StringComparer.Ordinal);
            var changedFromLocal = false;

            foreach (var field in fields)
            {
                var lf = GetFieldHlc(getHlc(l), getFieldHlcs(l), field);
                var rf = GetFieldHlc(getHlc(r), getFieldHlcs(r), field);

                if (fieldEquals(l, r, field))
                {
                    var maxF = HlcClock.Max(lf, rf);
                    if (!string.IsNullOrEmpty(maxF))
                        mergedFieldHlcs[field] = maxF;
                    continue;
                }

                if (HlcClock.Compare(rf, lf) > 0)
                {
                    copyField(r, merged, field);
                    if (!string.IsNullOrEmpty(rf))
                        mergedFieldHlcs[field] = rf;
                    changedFromLocal = true;
                }
                else
                {
                    if (!string.IsNullOrEmpty(lf))
                        mergedFieldHlcs[field] = lf;
                }
            }

            var maxHlc = MaxEntityHlc(HlcClock.Max(getHlc(l), getHlc(r)), mergedFieldHlcs);
            setHlc(merged, maxHlc, mergedFieldHlcs);
            result.Add(merged);
            if (changedFromLocal) updatedCount++;
        }

        foreach (var r in rList)
        {
            var id = getId(r);
            if (string.IsNullOrWhiteSpace(id) || seenIds.Contains(id)) continue;
            seenIds.Add(id);
            result.Add(clone(r));
            updatedCount++;
        }

        return result;
    }

    private static T ResolveTombstone<T>(
        T local,
        T remote,
        bool localDel,
        bool remoteDel,
        string localHlc,
        string remoteHlc,
        out bool remoteWon)
    {
        var cmp = HlcClock.Compare(remoteHlc, localHlc);

        if (localDel && remoteDel)
        {
            remoteWon = cmp > 0;
            return cmp >= 0 ? remote : local;
        }

        if (remoteDel)
        {
            // 删除端 HLC 大于等于本地修改时删除生效；若本地修改严格更新则保留本地
            remoteWon = cmp >= 0;
            return remoteWon ? remote : local;
        }

        // localDel && !remoteDel: 仅当远端非删除端严格更新时才恢复
        remoteWon = cmp > 0;
        return remoteWon ? remote : local;
    }

    private static Dictionary<string, T> IndexById<T>(List<T> items, Func<T, string> getId)
    {
        var map = new Dictionary<string, T>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            var id = getId(item);
            if (!string.IsNullOrWhiteSpace(id))
                map[id] = item;
        }
        return map;
    }

    private static string GetFieldHlc(string entityHlc, Dictionary<string, string>? fieldHlcs, string field)
    {
        if (fieldHlcs is not null && fieldHlcs.TryGetValue(field, out var fh) && !string.IsNullOrEmpty(fh))
            return fh;
        return entityHlc ?? "";
    }

    private static string MaxEntityHlc(string? baseHlc, Dictionary<string, string>? fieldHlcs)
    {
        var max = baseHlc ?? "";
        if (fieldHlcs is null) return max;
        foreach (var v in fieldHlcs.Values)
            max = HlcClock.Max(max, v);
        return max;
    }

    private static void CopyScheduleField(ScheduleDto src, ScheduleDto dst, string field)
    {
        switch (field)
        {
            case "title": dst.Title = src.Title; break;
            case "note": dst.Note = src.Note; break;
            case "due": dst.Due = src.Due; break;
            case "time": dst.Start = src.Start; dst.End = src.End; break;
            case "repeat": dst.Repeat = src.Repeat; break;
            case "remind": dst.Remind = src.Remind; break;
            case "done": dst.Done = src.Done; break;
            case "flags": dst.Urgent = src.Urgent; dst.Important = src.Important; break;
            case "goals":
                dst.GoalId = src.GoalId;
                dst.GoalRefs = (src.GoalRefs ?? new()).Select(r => new GoalRefDto { GoalId = r.GoalId, NodeId = r.NodeId }).ToList();
                break;
            case "tags":
                dst.TagIds = (src.TagIds ?? new()).ToList();
                break;
            case "order": dst.Order = src.Order; break;
        }
    }

    private static void CopyGoalField(GoalDto src, GoalDto dst, string field)
    {
        switch (field)
        {
            case "title": dst.Title = src.Title; break;
            case "color": dst.Color = src.Color; break;
            case "isCompleted": dst.IsCompleted = src.IsCompleted; break;
            case "due": dst.Due = src.Due; break;
            case "tree":
                dst.RootX = src.RootX;
                dst.RootY = src.RootY;
                dst.Nodes = (src.Nodes ?? new()).Select(n => new GoalNodeDto
                {
                    Id = n.Id, Title = n.Title, IsCompleted = n.IsCompleted, Due = n.Due, X = n.X, Y = n.Y
                }).ToList();
                dst.Edges = (src.Edges ?? new()).Select(e => new GoalEdgeDto
                {
                    FromId = e.FromId, ToId = e.ToId, Kind = e.Kind, Label = e.Label, FromSide = e.FromSide, ToSide = e.ToSide
                }).ToList();
                break;
        }
    }

    private static void SetGoalHlc(GoalDto g, string hlc, Dictionary<string, string> fieldHlcs)
    {
        g.Hlc = hlc;
        g.FieldHlcs = fieldHlcs;
    }

    private static void CopyTagField(TagDto src, TagDto dst, string field)
    {
        switch (field)
        {
            case "name": dst.Name = src.Name; break;
            case "color": dst.Color = src.Color; break;
        }
    }

    private static void SetTagHlc(TagDto t, string hlc, Dictionary<string, string> fieldHlcs)
    {
        t.Hlc = hlc;
        t.FieldHlcs = fieldHlcs;
    }

    private static void CopyPresetField(PresetDto src, PresetDto dst, string field)
    {
        switch (field)
        {
            case "title": dst.Title = src.Title; break;
            case "duration": dst.DurationMinutes = src.DurationMinutes; break;
            case "remind": dst.Remind = src.Remind; break;
            case "note": dst.Note = src.Note; break;
            case "flags": dst.Urgent = src.Urgent; dst.Important = src.Important; break;
            case "goals":
                dst.GoalId = src.GoalId;
                dst.GoalRefs = (src.GoalRefs ?? new()).Select(r => new GoalRefDto { GoalId = r.GoalId, NodeId = r.NodeId }).ToList();
                break;
            case "tags":
                dst.TagIds = (src.TagIds ?? new()).ToList();
                break;
        }
    }

    private static void SetPresetHlc(PresetDto p, string hlc, Dictionary<string, string> fieldHlcs)
    {
        p.Hlc = hlc;
        p.FieldHlcs = fieldHlcs;
    }
}
