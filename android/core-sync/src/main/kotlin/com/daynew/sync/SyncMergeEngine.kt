package com.daynew.sync

import java.util.UUID
import kotlin.math.abs

data class MergeReport(
    val mergedDto: StoreDto,
    val updatedCount: Int,
    val conflictCopiesCount: Int,
    val maxRemoteHlc: String,
)

/**
 * 多设备快照的 HLC 字段级合并引擎，与 Windows 端 Services/SyncMergeEngine.cs 及 StoreFile.cs 1:1 对齐。
 */
object SyncMergeEngine {
    val GOAL_FIELDS = listOf("title", "color", "isCompleted", "due", "tree")
    val TAG_FIELDS = listOf("name", "color")
    val PERSON_FIELDS = listOf("name")
    val LOCATION_FIELDS = listOf("name")
    val SCHEDULE_FIELDS = listOf(
        "title", "note", "due", "time", "repeat", "remind",
        "done", "flags", "goals", "tags", "persons", "locations", "order"
    )
    val PRESET_FIELDS = listOf(
        "title", "duration", "remind", "note", "flags",
        "goals", "tags", "persons", "locations"
    )

    private val CORE_SCHEDULE_FIELDS = setOf("title", "note", "due", "time")

    fun mergeStore(
        local: StoreDto,
        remote: StoreDto,
        lastSyncedRemoteHlc: String? = null,
        conflictIdGenerator: () -> String = { UUID.randomUUID().toString().replace("-", "").take(10) },
        conflictHlcGenerator: () -> String = { HlcClock().tick() },
    ): MergeReport {
        var updatedCount = 0
        var conflictCopiesCount = 0
        var maxRemoteHlc = ""

        fun trackRemote(hlc: String?, fieldHlcs: Map<String, String>?) {
            if (!hlc.isNullOrEmpty()) {
                maxRemoteHlc = HlcClock.max(maxRemoteHlc, hlc)
            }
            if (fieldHlcs != null) {
                for (v in fieldHlcs.values) {
                    if (v.isNotEmpty()) {
                        maxRemoteHlc = HlcClock.max(maxRemoteHlc, v)
                    }
                }
            }
        }

        remote.goals.forEach { trackRemote(it.hlc, it.fieldHlcs) }
        remote.tags.forEach { trackRemote(it.hlc, it.fieldHlcs) }
        remote.persons.forEach { trackRemote(it.hlc, it.fieldHlcs) }
        remote.locations.forEach { trackRemote(it.hlc, it.fieldHlcs) }
        remote.schedules.forEach { trackRemote(it.hlc, it.fieldHlcs) }
        remote.presets?.forEach { trackRemote(it.hlc, it.fieldHlcs) }

        val mergedGoals = mergeCollection(
            localList = local.goals,
            remoteList = remote.goals,
            getId = { it.id },
            getDel = { it.del },
            getHlc = { it.hlc },
            getFieldHlcs = { it.fieldHlcs },
            fields = GOAL_FIELDS,
            fieldEquals = ::goalFieldEquals,
            copyField = ::copyGoalField,
            setHlc = { g, hlc, hlcs -> g.copy(hlc = hlc, fieldHlcs = hlcs) },
            onUpdated = { updatedCount++ },
        )

        val mergedTags = mergeCollection(
            localList = local.tags,
            remoteList = remote.tags,
            getId = { it.id },
            getDel = { it.del },
            getHlc = { it.hlc },
            getFieldHlcs = { it.fieldHlcs },
            fields = TAG_FIELDS,
            fieldEquals = ::tagFieldEquals,
            copyField = ::copyTagField,
            setHlc = { t, hlc, hlcs -> t.copy(hlc = hlc, fieldHlcs = hlcs) },
            onUpdated = { updatedCount++ },
        )

        val mergedPersons = mergeCollection(
            localList = local.persons,
            remoteList = remote.persons,
            getId = { it.id },
            getDel = { it.del },
            getHlc = { it.hlc },
            getFieldHlcs = { it.fieldHlcs },
            fields = PERSON_FIELDS,
            fieldEquals = ::personFieldEquals,
            copyField = ::copyPersonField,
            setHlc = { p, hlc, hlcs -> p.copy(hlc = hlc, fieldHlcs = hlcs) },
            onUpdated = { updatedCount++ },
        )

        val mergedLocations = mergeCollection(
            localList = local.locations,
            remoteList = remote.locations,
            getId = { it.id },
            getDel = { it.del },
            getHlc = { it.hlc },
            getFieldHlcs = { it.fieldHlcs },
            fields = LOCATION_FIELDS,
            fieldEquals = ::locationFieldEquals,
            copyField = ::copyLocationField,
            setHlc = { l, hlc, hlcs -> l.copy(hlc = hlc, fieldHlcs = hlcs) },
            onUpdated = { updatedCount++ },
        )

        val scheduleResult = mergeSchedules(
            localList = local.schedules,
            remoteList = remote.schedules,
            lastSyncedRemoteHlc = lastSyncedRemoteHlc,
            conflictIdGenerator = conflictIdGenerator,
            conflictHlcGenerator = conflictHlcGenerator,
        )
        updatedCount += scheduleResult.updatedDelta
        conflictCopiesCount += scheduleResult.conflictsDelta

        val mergedPresets: List<PresetDto>? =
            if (local.presets == null && remote.presets == null) {
                null
            } else {
                mergeCollection(
                    localList = local.presets ?: emptyList(),
                    remoteList = remote.presets ?: emptyList(),
                    getId = { it.id },
                    getDel = { it.del },
                    getHlc = { it.hlc },
                    getFieldHlcs = { it.fieldHlcs },
                    fields = PRESET_FIELDS,
                    fieldEquals = ::presetFieldEquals,
                    copyField = ::copyPresetField,
                    setHlc = { p, hlc, hlcs -> p.copy(hlc = hlc, fieldHlcs = hlcs) },
                    onUpdated = { updatedCount++ },
                )
            }

        val merged = StoreDto(
            version = maxOf(local.version, remote.version),
            goals = mergedGoals,
            tags = mergedTags,
            persons = mergedPersons,
            locations = mergedLocations,
            schedules = scheduleResult.items,
            presets = mergedPresets,
        )

        return MergeReport(
            mergedDto = merged,
            updatedCount = updatedCount,
            conflictCopiesCount = conflictCopiesCount,
            maxRemoteHlc = maxRemoteHlc,
        )
    }

    private data class ScheduleMergeResult(
        val items: List<ScheduleDto>,
        val updatedDelta: Int,
        val conflictsDelta: Int,
    )

    private fun mergeSchedules(
        localList: List<ScheduleDto>,
        remoteList: List<ScheduleDto>,
        lastSyncedRemoteHlc: String?,
        conflictIdGenerator: () -> String,
        conflictHlcGenerator: () -> String,
    ): ScheduleMergeResult {
        val remoteById = indexById(remoteList) { it.id }
        val result = ArrayList<ScheduleDto>(localList.size + remoteList.size)
        val conflicts = ArrayList<ScheduleDto>()
        val seenIds = HashSet<String>()
        var updatedCount = 0
        var conflictCopiesCount = 0

        for (l in localList) {
            if (l.id.isBlank()) {
                result += l
                continue
            }
            if (!seenIds.add(l.id)) continue

            val r = remoteById[l.id]
            if (r == null) {
                result += l
                continue
            }

            if (l.del || r.del) {
                val tombstone = resolveTombstone(
                    local = l,
                    remote = r,
                    localDel = l.del,
                    remoteDel = r.del,
                    localHlc = maxEntityHlc(l.hlc, l.fieldHlcs),
                    remoteHlc = maxEntityHlc(r.hlc, r.fieldHlcs),
                )
                result += tombstone.winner
                if (tombstone.remoteWon) updatedCount++
                continue
            }

            var merged = l
            val mergedFieldHlcs = (l.fieldHlcs ?: emptyMap()).toMutableMap()
            var hasCoreConflict = false
            var changedFromLocal = false

            for (field in SCHEDULE_FIELDS) {
                val lFieldHlc = getFieldHlc(l.hlc, l.fieldHlcs, field)
                val rFieldHlc = getFieldHlc(r.hlc, r.fieldHlcs, field)

                if (scheduleFieldEquals(l, r, field)) {
                    val maxF = HlcClock.max(lFieldHlc, rFieldHlc)
                    if (maxF.isNotEmpty()) mergedFieldHlcs[field] = maxF
                    continue
                }

                if (field in CORE_SCHEDULE_FIELDS &&
                    !lastSyncedRemoteHlc.isNullOrEmpty() &&
                    HlcClock.compare(lFieldHlc, lastSyncedRemoteHlc) > 0 &&
                    HlcClock.compare(rFieldHlc, lastSyncedRemoteHlc) > 0
                ) {
                    hasCoreConflict = true
                }

                if (HlcClock.compare(rFieldHlc, lFieldHlc) > 0) {
                    merged = copyScheduleField(r, merged, field)
                    if (rFieldHlc.isNotEmpty()) mergedFieldHlcs[field] = rFieldHlc
                    changedFromLocal = true
                } else {
                    if (lFieldHlc.isNotEmpty()) mergedFieldHlcs[field] = lFieldHlc
                }
            }

            merged = merged.copy(
                hlc = maxEntityHlc(HlcClock.max(l.hlc, r.hlc), mergedFieldHlcs),
                fieldHlcs = mergedFieldHlcs,
            )
            result += merged
            if (changedFromLocal) updatedCount++

            if (hasCoreConflict) {
                val lMax = maxEntityHlc(l.hlc, l.fieldHlcs)
                val rMax = maxEntityHlc(r.hlc, r.fieldHlcs)
                val loser = if (HlcClock.compare(rMax, lMax) > 0) l else r
                val conflictTitle = if (loser.title.startsWith("[冲突]")) {
                    loser.title
                } else {
                    "[冲突] ${loser.title}"
                }

                val alreadyExists = (result + conflicts).any { existing ->
                    !existing.del &&
                        existing.title == conflictTitle &&
                        existing.due == loser.due &&
                        existing.start == loser.start &&
                        existing.end == loser.end &&
                        existing.note == loser.note
                }

                if (!alreadyExists) {
                    val copyHlc = conflictHlcGenerator()
                    val copyFieldHlcs = SCHEDULE_FIELDS.associateWith { copyHlc }
                    val copy = loser.copy(
                        id = conflictIdGenerator(),
                        title = conflictTitle,
                        del = false,
                        hlc = copyHlc,
                        fieldHlcs = copyFieldHlcs,
                    )
                    conflicts += copy
                    conflictCopiesCount++
                    updatedCount++
                }
            }
        }

        for (r in remoteList) {
            if (r.id.isBlank() || r.id in seenIds) continue
            seenIds.add(r.id)
            result += r
            updatedCount++
        }

        result += conflicts
        return ScheduleMergeResult(result, updatedCount, conflictCopiesCount)
    }

    private inline fun <T> mergeCollection(
        localList: List<T>,
        remoteList: List<T>,
        getId: (T) -> String,
        getDel: (T) -> Boolean,
        getHlc: (T) -> String,
        getFieldHlcs: (T) -> Map<String, String>?,
        fields: List<String>,
        fieldEquals: (T, T, String) -> Boolean,
        copyField: (T, T, String) -> T,
        setHlc: (T, String, Map<String, String>) -> T,
        onUpdated: () -> Unit,
    ): List<T> {
        val remoteById = indexById(remoteList, getId)
        val result = ArrayList<T>(localList.size + remoteList.size)
        val seenIds = HashSet<String>()

        for (l in localList) {
            val id = getId(l)
            if (id.isBlank()) {
                result += l
                continue
            }
            if (!seenIds.add(id)) continue

            val r = remoteById[id]
            if (r == null) {
                result += l
                continue
            }

            if (getDel(l) || getDel(r)) {
                val tombstone = resolveTombstone(
                    local = l,
                    remote = r,
                    localDel = getDel(l),
                    remoteDel = getDel(r),
                    localHlc = maxEntityHlc(getHlc(l), getFieldHlcs(l)),
                    remoteHlc = maxEntityHlc(getHlc(r), getFieldHlcs(r)),
                )
                result += tombstone.winner
                if (tombstone.remoteWon) onUpdated()
                continue
            }

            var merged = l
            val mergedFieldHlcs = (getFieldHlcs(l) ?: emptyMap()).toMutableMap()
            var changedFromLocal = false

            for (field in fields) {
                val lf = getFieldHlc(getHlc(l), getFieldHlcs(l), field)
                val rf = getFieldHlc(getHlc(r), getFieldHlcs(r), field)

                if (fieldEquals(l, r, field)) {
                    val maxF = HlcClock.max(lf, rf)
                    if (maxF.isNotEmpty()) mergedFieldHlcs[field] = maxF
                    continue
                }

                if (HlcClock.compare(rf, lf) > 0) {
                    merged = copyField(r, merged, field)
                    if (rf.isNotEmpty()) mergedFieldHlcs[field] = rf
                    changedFromLocal = true
                } else {
                    if (lf.isNotEmpty()) mergedFieldHlcs[field] = lf
                }
            }

            val maxHlc = maxEntityHlc(HlcClock.max(getHlc(l), getHlc(r)), mergedFieldHlcs)
            merged = setHlc(merged, maxHlc, mergedFieldHlcs)
            result += merged
            if (changedFromLocal) onUpdated()
        }

        for (r in remoteList) {
            val id = getId(r)
            if (id.isBlank() || id in seenIds) continue
            seenIds.add(id)
            result += r
            onUpdated()
        }

        return result
    }

    private data class TombstoneResult<T>(val winner: T, val remoteWon: Boolean)

    private fun <T> resolveTombstone(
        local: T,
        remote: T,
        localDel: Boolean,
        remoteDel: Boolean,
        localHlc: String,
        remoteHlc: String,
    ): TombstoneResult<T> {
        val cmp = HlcClock.compare(remoteHlc, localHlc)
        if (localDel && remoteDel) {
            val remoteWon = cmp > 0
            return TombstoneResult(if (cmp >= 0) remote else local, remoteWon)
        }
        if (remoteDel) {
            val remoteWon = cmp >= 0
            return TombstoneResult(if (remoteWon) remote else local, remoteWon)
        }
        val remoteWon = cmp > 0
        return TombstoneResult(if (remoteWon) remote else local, remoteWon)
    }

    private inline fun <T> indexById(items: List<T>, getId: (T) -> String): Map<String, T> {
        val map = LinkedHashMap<String, T>(items.size)
        for (item in items) {
            val id = getId(item)
            if (id.isNotBlank()) map[id] = item
        }
        return map
    }

    private fun getFieldHlc(
        entityHlc: String,
        fieldHlcs: Map<String, String>?,
        field: String,
    ): String {
        val fh = fieldHlcs?.get(field)
        return if (!fh.isNullOrEmpty()) fh else entityHlc
    }

    private fun maxEntityHlc(baseHlc: String?, fieldHlcs: Map<String, String>?): String {
        var max = baseHlc ?: ""
        if (fieldHlcs != null) {
            for (v in fieldHlcs.values) {
                max = HlcClock.max(max, v)
            }
        }
        return max
    }

    private fun goalFieldEquals(a: GoalDto, b: GoalDto, field: String): Boolean = when (field) {
        "title" -> a.title == b.title
        "color" -> a.color == b.color
        "isCompleted" -> a.isCompleted == b.isCompleted
        "due" -> a.due == b.due
        "tree" -> nullableDoubleEqual(a.rootX, b.rootX) &&
            nullableDoubleEqual(a.rootY, b.rootY) &&
            goalNodesEqual(a.nodes, b.nodes) &&
            goalEdgesEqual(a.edges, b.edges)
        else -> true
    }

    private fun tagFieldEquals(a: TagDto, b: TagDto, field: String): Boolean = when (field) {
        "name" -> a.name == b.name
        "color" -> a.color == b.color
        else -> true
    }

    private fun personFieldEquals(a: PersonDto, b: PersonDto, field: String): Boolean = when (field) {
        "name" -> a.name == b.name
        else -> true
    }

    private fun locationFieldEquals(a: LocationDto, b: LocationDto, field: String): Boolean = when (field) {
        "name" -> a.name == b.name
        else -> true
    }

    private fun scheduleFieldEquals(a: ScheduleDto, b: ScheduleDto, field: String): Boolean = when (field) {
        "title" -> a.title == b.title
        "note" -> a.note == b.note
        "due" -> a.due == b.due
        "time" -> a.start == b.start && a.end == b.end
        "repeat" -> a.repeat == b.repeat
        "remind" -> a.remind == b.remind
        "done" -> a.done == b.done
        "flags" -> a.urgent == b.urgent && a.important == b.important
        "goals" -> a.goalId == b.goalId && a.goalRefs == b.goalRefs
        "tags" -> a.tagIds == b.tagIds
        "persons" -> a.personIds == b.personIds
        "locations" -> a.locationIds == b.locationIds
        "order" -> a.order == b.order
        else -> true
    }

    private fun presetFieldEquals(a: PresetDto, b: PresetDto, field: String): Boolean = when (field) {
        "title" -> a.title == b.title
        "duration" -> a.durationMinutes == b.durationMinutes
        "remind" -> a.remind == b.remind
        "note" -> a.note == b.note
        "flags" -> a.urgent == b.urgent && a.important == b.important
        "goals" -> a.goalId == b.goalId && a.goalRefs == b.goalRefs
        "tags" -> a.tagIds == b.tagIds
        "persons" -> a.personIds == b.personIds
        "locations" -> a.locationIds == b.locationIds
        else -> true
    }

    private fun nullableDoubleEqual(a: Double?, b: Double?): Boolean {
        if (a == null && b == null) return true
        if (a == null || b == null) return false
        return abs(a - b) < 0.01
    }

    private fun goalNodesEqual(a: List<GoalNodeDto>, b: List<GoalNodeDto>): Boolean {
        if (a.size != b.size) return false
        for (i in a.indices) {
            val na = a[i]
            val nb = b[i]
            if (na.id != nb.id ||
                na.title != nb.title ||
                na.isCompleted != nb.isCompleted ||
                na.due != nb.due ||
                abs(na.x - nb.x) >= 0.01 ||
                abs(na.y - nb.y) >= 0.01
            ) {
                return false
            }
        }
        return true
    }

    private fun goalEdgesEqual(a: List<GoalEdgeDto>, b: List<GoalEdgeDto>): Boolean {
        if (a.size != b.size) return false
        for (i in a.indices) {
            if (a[i] != b[i]) return false
        }
        return true
    }

    private fun copyGoalField(src: GoalDto, dst: GoalDto, field: String): GoalDto = when (field) {
        "title" -> dst.copy(title = src.title)
        "color" -> dst.copy(color = src.color)
        "isCompleted" -> dst.copy(isCompleted = src.isCompleted)
        "due" -> dst.copy(due = src.due)
        "tree" -> dst.copy(
            rootX = src.rootX,
            rootY = src.rootY,
            nodes = src.nodes.map { it.copy() },
            edges = src.edges.map { it.copy() },
        )
        else -> dst
    }

    private fun copyTagField(src: TagDto, dst: TagDto, field: String): TagDto = when (field) {
        "name" -> dst.copy(name = src.name)
        "color" -> dst.copy(color = src.color)
        else -> dst
    }

    private fun copyPersonField(src: PersonDto, dst: PersonDto, field: String): PersonDto = when (field) {
        "name" -> dst.copy(name = src.name)
        else -> dst
    }

    private fun copyLocationField(src: LocationDto, dst: LocationDto, field: String): LocationDto = when (field) {
        "name" -> dst.copy(name = src.name)
        else -> dst
    }

    private fun copyScheduleField(src: ScheduleDto, dst: ScheduleDto, field: String): ScheduleDto = when (field) {
        "title" -> dst.copy(title = src.title)
        "note" -> dst.copy(note = src.note)
        "due" -> dst.copy(due = src.due)
        "time" -> dst.copy(start = src.start, end = src.end)
        "repeat" -> dst.copy(repeat = src.repeat)
        "remind" -> dst.copy(remind = src.remind)
        "done" -> dst.copy(done = src.done)
        "flags" -> dst.copy(urgent = src.urgent, important = src.important)
        "goals" -> dst.copy(goalId = src.goalId, goalRefs = src.goalRefs.map { it.copy() })
        "tags" -> dst.copy(tagIds = src.tagIds.toList())
        "persons" -> dst.copy(personIds = src.personIds.toList())
        "locations" -> dst.copy(locationIds = src.locationIds.toList())
        "order" -> dst.copy(order = src.order)
        else -> dst
    }

    private fun copyPresetField(src: PresetDto, dst: PresetDto, field: String): PresetDto = when (field) {
        "title" -> dst.copy(title = src.title)
        "duration" -> dst.copy(durationMinutes = src.durationMinutes)
        "remind" -> dst.copy(remind = src.remind)
        "note" -> dst.copy(note = src.note)
        "flags" -> dst.copy(urgent = src.urgent, important = src.important)
        "goals" -> dst.copy(goalId = src.goalId, goalRefs = src.goalRefs.map { it.copy() })
        "tags" -> dst.copy(tagIds = src.tagIds.toList())
        "persons" -> dst.copy(personIds = src.personIds.toList())
        "locations" -> dst.copy(locationIds = src.locationIds.toList())
        else -> dst
    }
}
