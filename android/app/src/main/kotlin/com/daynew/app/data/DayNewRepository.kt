package com.daynew.app.data

import com.daynew.sync.CompletionEntry
import com.daynew.sync.DeadlineRules
import com.daynew.sync.GoalDto
import com.daynew.sync.GoalRefDto
import com.daynew.sync.HlcClock
import com.daynew.sync.LocationDto
import com.daynew.sync.MergeReport
import com.daynew.sync.PersonDto
import com.daynew.sync.RemindRules
import com.daynew.sync.RepeatRules
import com.daynew.sync.ScheduleDto
import com.daynew.sync.StoreDto
import com.daynew.sync.SyncMergeEngine
import com.daynew.sync.TagDto
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch
import java.io.File
import java.time.LocalDate
import java.time.LocalDateTime
import java.time.LocalTime
import java.util.UUID

data class MoveOutcome(
    val crossLane: Boolean,
    val targetLane: LaneKind,
    val toastMessage: String?,
)

data class ScheduleDraft(
    val id: String? = null,
    val createTitle: String = "新建日程",
    val title: String = "",
    val note: String = "",
    val lane: LaneKind = LaneKind.TODAY,
    val initialLane: LaneKind = LaneKind.TODAY,
    val hasDueDate: Boolean = true,
    val useExactDate: Boolean = false,
    val dueDate: LocalDate = LocalDate.now(),
    val dueTime: LocalTime? = null,
    val startText: String = "",
    val endText: String = "",
    val repeat: String = "",
    val remind: String = RemindRules.DEFAULT,
    val urgent: Boolean = false,
    val important: Boolean = false,
    val goalId: String? = null,
    val tagIds: List<String> = emptyList(),
    val personIds: List<String> = emptyList(),
    val locationIds: List<String> = emptyList(),
)

data class GoalDraft(
    val id: String? = null,
    val title: String = "",
    val color: String = "#527CE6",
    val dueText: String = "",
    val isCompleted: Boolean = false,
)

data class TagDraft(
    val id: String? = null,
    val name: String = "",
    val color: String = "#527CE6",
)

data class PersonDraft(
    val id: String? = null,
    val name: String = "",
)

data class LocationDraft(
    val id: String? = null,
    val name: String = "",
)

/**
 * 本地业务仓库：管理 StoreDto 唯一状态源、HLC 字段级时间戳、1 秒防抖原子写及 completions.ndjson 追加。
 */
class DayNewRepository(
    dataDir: File,
    private val nowProvider: () -> LocalDateTime = { LocalDateTime.now() },
    debounceMs: Long = 1000L,
    scope: CoroutineScope = CoroutineScope(SupervisorJob() + Dispatchers.IO),
    @Volatile var onStoreChanged: ((StoreDto, Boolean) -> Unit)? = null,
) {
    val storeFile: LocalStoreFile = LocalStoreFile(dataDir)
    val completionLog: LocalCompletionLog = LocalCompletionLog(dataDir)
    val deviceId: String = storeFile.loadOrCreateDeviceId()
    val clock: HlcClock = HlcClock(deviceId)

    private val writer = DebouncedStoreWriter(storeFile, scope, debounceMs)
    private val stateLock = Any()

    private val _storeFlow: MutableStateFlow<StoreDto>
    val storeFlow: StateFlow<StoreDto>

    private val _completionsCountFlow: MutableStateFlow<Int>
    val completionsCountFlow: StateFlow<Int>

    var loadNotice: String? = null
        private set

    init {
        val initialStore = when (val loaded = storeFile.load()) {
            is StoreLoadResult.Loaded -> {
                observeStoreHlcs(loaded.store)
                loaded.store
            }
            StoreLoadResult.NotFound -> {
                val seeded = DemoDataSeeder.seed(clock, nowProvider().toLocalDate())
                storeFile.writeAtomic(seeded)
                seeded
            }
            is StoreLoadResult.Quarantined -> {
                loadNotice = "检测到损坏数据文件并已隔离：${loaded.errorMessage}"
                StoreDto()
            }
        }
        _storeFlow = MutableStateFlow(initialStore)
        storeFlow = _storeFlow.asStateFlow()
        _completionsCountFlow = MutableStateFlow(0)
        completionsCountFlow = _completionsCountFlow.asStateFlow()
        scope.launch {
            _completionsCountFlow.value = completionLog.readAll().size
        }
    }

    fun currentStore(): StoreDto = _storeFlow.value

    fun getScheduleById(scheduleId: String): ScheduleDto? =
        _storeFlow.value.schedules.firstOrNull { it.id == scheduleId && !it.del }

    fun readCompletions(): List<CompletionEntry> = completionLog.readAll()

    fun applyRemoteSnapshot(
        remoteStore: StoreDto,
        remoteCompletions: List<CompletionEntry>,
        lastSyncedRemoteHlc: String?,
    ): MergeReport = synchronized(stateLock) {
        val current = _storeFlow.value
        val report = SyncMergeEngine.mergeStore(
            local = current,
            remote = remoteStore,
            lastSyncedRemoteHlc = lastSyncedRemoteHlc,
            conflictIdGenerator = ::generateShortId,
            conflictHlcGenerator = { clock.tick() },
        )
        val addedCompletions = completionLog.mergeEntries(remoteCompletions)
        if (addedCompletions > 0) {
            _completionsCountFlow.value = completionLog.readAll().size
        }
        observeStoreHlcs(report.mergedDto)
        if (report.updatedCount > 0) {
            commitStore(report.mergedDto, fromLocalMutation = false)
            writer.flushNow(report.mergedDto)
        }
        report
    }

    fun replaceAllFromImport(
        importedStore: StoreDto,
        importedCompletions: List<CompletionEntry>,
    ) = synchronized(stateLock) {
        observeStoreHlcs(importedStore)
        completionLog.replaceAll(importedCompletions)
        _completionsCountFlow.value = importedCompletions.size
        commitStore(importedStore, fromLocalMutation = true)
        writer.flushNow(importedStore)
    }

    fun mergeFromImport(
        importedStore: StoreDto,
        importedCompletions: List<CompletionEntry>,
    ): MergeReport = synchronized(stateLock) {
        val current = _storeFlow.value
        val report = SyncMergeEngine.mergeStore(
            local = current,
            remote = importedStore,
            lastSyncedRemoteHlc = null,
            conflictIdGenerator = ::generateShortId,
            conflictHlcGenerator = { clock.tick() },
        )
        val addedCompletions = completionLog.mergeEntries(importedCompletions)
        if (addedCompletions > 0) {
            _completionsCountFlow.value = completionLog.readAll().size
        }
        observeStoreHlcs(report.mergedDto)
        commitStore(report.mergedDto, fromLocalMutation = true)
        writer.flushNow(report.mergedDto)
        report
    }

    fun moveSchedule(
        scheduleId: String,
        targetLane: LaneKind,
        targetIndex: Int,
        now: LocalDateTime = nowProvider(),
    ): MoveOutcome = synchronized(stateLock) {
        val store = _storeFlow.value
        val source = store.schedules.firstOrNull { it.id == scheduleId && !it.del }
            ?: return MoveOutcome(crossLane = false, targetLane = targetLane, toastMessage = null)

        val oldLane = LaneRules.laneFor(source, now)
        val crossLane = oldLane != targetLane

        var updatedSource = source
        if (crossLane) {
            val parsed = DeadlineRules.parseDue(source.due)
            val nextDate = LaneRules.dueForLane(targetLane, now.toLocalDate())
            val nextTime = if (
                targetLane == LaneKind.TODAY &&
                parsed.dueTime != null &&
                !now.toLocalTime().isBefore(parsed.dueTime)
            ) {
                null
            } else {
                parsed.dueTime
            }
            val nextDue = DeadlineRules.formatDue(nextDate, nextTime)
            val tick = clock.tick()
            updatedSource = updatedSource.copy(
                due = nextDue,
                hlc = tick,
                fieldHlcs = updatedSource.fieldHlcs.orEmpty() + ("due" to tick),
            )
        }

        val targetSiblings = store.schedules
            .asSequence()
            .filter { it.id != scheduleId && !it.del && LaneRules.laneFor(it, now) == targetLane }
            .sortedWith(
                compareBy<ScheduleDto> { it.done }
                    .thenBy { item ->
                        DeadlineRules.tryParseClock(item.start)?.let { it.hour * 60 + it.minute }
                            ?: Int.MAX_VALUE
                    }
                    .thenBy { it.order }
                    .thenBy { it.id }
            )
            .toMutableList()

        val clampedIndex = targetIndex.coerceIn(0, targetSiblings.size)
        targetSiblings.add(clampedIndex, updatedSource)

        val updatedById = HashMap<String, ScheduleDto>(targetSiblings.size + 1)
        targetSiblings.forEachIndexed { idx, item ->
            val targetOrder = (idx + 1) * 1024
            if (item.order != targetOrder) {
                val orderTick = clock.tick()
                updatedById[item.id] = item.copy(
                    order = targetOrder,
                    hlc = orderTick,
                    fieldHlcs = item.fieldHlcs.orEmpty() + ("order" to orderTick),
                )
            } else if (item.id == scheduleId) {
                updatedById[item.id] = item
            }
        }

        val newSchedules = store.schedules.map { updatedById[it.id] ?: it }
        commitStore(store.copy(schedules = newSchedules))

        val toast = if (crossLane) "已移到「${targetLane.title}」" else null
        MoveOutcome(crossLane = crossLane, targetLane = targetLane, toastMessage = toast)
    }

    fun reorderFutureSchedule(
        scheduleId: String,
        targetIndex: Int,
    ): Boolean = synchronized(stateLock) {
        val store = _storeFlow.value
        val source = store.schedules.firstOrNull { it.id == scheduleId && !it.del } ?: return false
        if (DeadlineRules.parseDue(source.due).date != null) return false

        val siblings = store.schedules
            .asSequence()
            .filter { it.id != scheduleId && !it.del && DeadlineRules.parseDue(it.due).date == null }
            .sortedWith(compareBy<ScheduleDto> { it.done }.thenBy { it.order }.thenBy { it.id })
            .toMutableList()

        val clampedIndex = targetIndex.coerceIn(0, siblings.size)
        siblings.add(clampedIndex, source)

        val updatedById = HashMap<String, ScheduleDto>(siblings.size)
        siblings.forEachIndexed { idx, item ->
            val targetOrder = (idx + 1) * 1024
            if (item.order != targetOrder) {
                val orderTick = clock.tick()
                updatedById[item.id] = item.copy(
                    order = targetOrder,
                    hlc = orderTick,
                    fieldHlcs = item.fieldHlcs.orEmpty() + ("order" to orderTick),
                )
            }
        }
        if (updatedById.isEmpty()) return false

        val newSchedules = store.schedules.map { updatedById[it.id] ?: it }
        commitStore(store.copy(schedules = newSchedules))
        true
    }

    fun rescheduleTimelineBlock(
        scheduleId: String,
        targetDate: LocalDate,
        desiredStartMinutes: Int,
        snapMinutes: Int = 15,
    ): String? = synchronized(stateLock) {
        val store = _storeFlow.value
        val source = store.schedules.firstOrNull { it.id == scheduleId && !it.del } ?: return null

        val parsedStart = DeadlineRules.tryParseClock(source.start)
        val parsedEnd = DeadlineRules.tryParseClock(source.end)
        val durationMinutes = if (parsedStart != null && parsedEnd != null && parsedEnd.isAfter(parsedStart)) {
            ((parsedEnd.hour * 60 + parsedEnd.minute) - (parsedStart.hour * 60 + parsedStart.minute))
                .coerceAtLeast(15)
        } else {
            60
        }

        val step = snapMinutes.coerceAtLeast(1)
        val maxStart = (24 * 60 - durationMinutes).coerceAtLeast(0)
        val snapped = (((desiredStartMinutes + step / 2) / step) * step).coerceIn(0, maxStart)

        val busyIntervals = store.schedules
            .asSequence()
            .filter { it.id != scheduleId && !it.del }
            .mapNotNull { item ->
                val date = DeadlineRules.parseDue(item.due).date ?: return@mapNotNull null
                if (date != targetDate) return@mapNotNull null
                val s = DeadlineRules.tryParseClock(item.start) ?: return@mapNotNull null
                val sMin = s.hour * 60 + s.minute
                val e = DeadlineRules.tryParseClock(item.end)
                val eMin = if (e != null && e.isAfter(s)) {
                    (e.hour * 60 + e.minute).coerceAtMost(24 * 60)
                } else {
                    (sMin + 30).coerceAtMost(24 * 60)
                }
                sMin to eMin
            }
            .toList()

        val finalStart = findFreeStartMinutes(
            desired = snapped,
            durationMinutes = durationMinutes,
            step = step,
            maxStart = maxStart,
            busyIntervals = busyIntervals,
        )
        val finalEnd = (finalStart + durationMinutes).coerceAtMost(24 * 60 - 1)
        val newStartStr = "%02d:%02d".format(java.util.Locale.ROOT, finalStart / 60, finalStart % 60)
        val newEndStr = "%02d:%02d".format(java.util.Locale.ROOT, finalEnd / 60, finalEnd % 60)

        val oldDueTime = DeadlineRules.parseDue(source.due).dueTime
        val newDue = DeadlineRules.formatDue(targetDate, oldDueTime)

        val dueChanged = source.due != newDue
        val timeChanged = source.start != newStartStr || source.end != newEndStr
        if (!dueChanged && !timeChanged) return null

        val fieldMap = source.fieldHlcs.orEmpty().toMutableMap()
        var latestHlc = source.hlc
        if (dueChanged) {
            val t = clock.tick()
            fieldMap["due"] = t
            latestHlc = t
        }
        if (timeChanged) {
            val t = clock.tick()
            fieldMap["time"] = t
            latestHlc = t
        }

        val updated = source.copy(
            due = newDue,
            start = newStartStr,
            end = newEndStr,
            hlc = latestHlc,
            fieldHlcs = fieldMap,
        )
        commitStore(store.copy(schedules = store.schedules.map { if (it.id == scheduleId) updated else it }))
        "${targetDate.monthValue}月${targetDate.dayOfMonth}日 $newStartStr - $newEndStr"
    }

    private fun findFreeStartMinutes(
        desired: Int,
        durationMinutes: Int,
        step: Int,
        maxStart: Int,
        busyIntervals: List<Pair<Int, Int>>,
    ): Int {
        fun isFree(startMin: Int): Boolean {
            val endMin = startMin + durationMinutes
            return busyIntervals.none { (bStart, bEnd) -> startMin < bEnd && endMin > bStart }
        }
        if (isFree(desired)) return desired
        val maxOffsetSteps = (24 * 60) / step + 1
        for (offset in 1..maxOffsetSteps) {
            val plus = desired + offset * step
            if (plus in 0..maxStart && isFree(plus)) return plus
            val minus = desired - offset * step
            if (minus in 0..maxStart && isFree(minus)) return minus
        }
        return desired
    }

    fun toggleDone(
        scheduleId: String,
        now: LocalDateTime = nowProvider(),
    ): String? = synchronized(stateLock) {
        val store = _storeFlow.value
        val target = store.schedules.firstOrNull { it.id == scheduleId && !it.del } ?: return null
        val parsed = DeadlineRules.parseDue(target.due)
        val rule = RepeatRules.normalize(target.repeat)

        if (!target.done && rule.isNotEmpty()) {
            completionLog.append(target.id, parsed.date, done = true, at = now)
            _completionsCountFlow.value = _completionsCountFlow.value + 1

            val nextDate = RepeatRules.nextOccurrence(parsed.date, rule, now.toLocalDate())
            if (nextDate != null) {
                val nextDue = DeadlineRules.formatDue(nextDate, parsed.dueTime)
                val tick = clock.tick()
                val updated = target.copy(
                    due = nextDue,
                    done = false,
                    hlc = tick,
                    fieldHlcs = target.fieldHlcs.orEmpty() + ("due" to tick),
                )
                commitStore(store.copy(schedules = store.schedules.map { if (it.id == target.id) updated else it }))
                return "已完成本次「${target.title}」，推进至 ${nextDate.monthValue}月${nextDate.dayOfMonth}日"
            }
        }

        val newDone = !target.done
        val tick = clock.tick()
        val updated = target.copy(
            done = newDone,
            hlc = tick,
            fieldHlcs = target.fieldHlcs.orEmpty() + ("done" to tick),
        )
        completionLog.append(target.id, parsed.date, done = newDone, at = now)
        _completionsCountFlow.value = _completionsCountFlow.value + 1

        commitStore(store.copy(schedules = store.schedules.map { if (it.id == target.id) updated else it }))
        return if (newDone) "已完成「${target.title}」" else "已恢复待办「${target.title}」"
    }

    fun upsertSchedule(
        draft: ScheduleDraft,
        now: LocalDateTime = nowProvider(),
    ): ScheduleDto = synchronized(stateLock) {
        val store = _storeFlow.value
        val cleanTitle = draft.title.trim().ifEmpty { "未命名日程" }
        val cleanNote = draft.note.trim()
        val parsedStart = if (draft.hasDueDate) DeadlineRules.tryParseClock(draft.startText) else null
        val parsedEnd = if (draft.hasDueDate) DeadlineRules.tryParseClock(draft.endText) else null
        val normalizedStart = parsedStart?.let { DeadlineRules.formatClock(it) }
        val normalizedEnd = if (parsedStart != null && parsedEnd != null && parsedEnd.isAfter(parsedStart)) {
            DeadlineRules.formatClock(parsedEnd)
        } else if (parsedStart != null) {
            DeadlineRules.formatClock(parsedStart.plusMinutes(30))
        } else {
            null
        }

        val resolvedDue = if (!draft.hasDueDate) {
            null
        } else {
            val resolvedDate = if (draft.useExactDate || (draft.id != null && draft.lane == draft.initialLane)) {
                draft.dueDate
            } else {
                LaneRules.dueForLane(draft.lane, now.toLocalDate())
            }
            DeadlineRules.formatDue(resolvedDate, draft.dueTime)
        }
        val normalizedRepeat = RepeatRules.normalize(draft.repeat)
        val normalizedRemind = RemindRules.normalize(draft.remind, legacyFallback = "")
        val goalRefs = if (draft.goalId != null) listOf(GoalRefDto(goalId = draft.goalId)) else emptyList()

        val existing = draft.id?.let { id -> store.schedules.firstOrNull { it.id == id } }
        if (existing == null) {
            val newId = generateShortId()
            val maxOrder = store.schedules
                .asSequence()
                .filter {
                    !it.del && if (!draft.hasDueDate) {
                        DeadlineRules.parseDue(it.due).date == null
                    } else {
                        LaneRules.laneFor(it, now) == draft.lane
                    }
                }
                .maxOfOrNull { it.order } ?: 0
            val tick = clock.tick()
            val allFields = listOf(
                "title", "note", "due", "time", "repeat", "remind",
                "done", "flags", "goals", "tags", "persons", "locations", "order",
            )
            val created = ScheduleDto(
                id = newId,
                title = cleanTitle,
                note = cleanNote,
                due = resolvedDue,
                start = normalizedStart,
                end = normalizedEnd,
                repeat = normalizedRepeat,
                remind = normalizedRemind,
                done = false,
                urgent = draft.urgent,
                important = draft.important,
                goalId = draft.goalId,
                goalRefs = goalRefs,
                tagIds = draft.tagIds.distinct(),
                personIds = draft.personIds.distinct(),
                locationIds = draft.locationIds.distinct(),
                order = maxOrder + 1024,
                hlc = tick,
                del = false,
                fieldHlcs = allFields.associateWith { tick },
            )
            commitStore(store.copy(schedules = store.schedules + created))
            return created
        }

        val fieldMap = existing.fieldHlcs.orEmpty().toMutableMap()
        var latestHlc = existing.hlc

        fun stamp(field: String) {
            val t = clock.tick()
            fieldMap[field] = t
            latestHlc = t
        }

        val cleanTagIds = draft.tagIds.distinct()
        val cleanPersonIds = draft.personIds.distinct()
        val cleanLocationIds = draft.locationIds.distinct()

        if (existing.title != cleanTitle) stamp("title")
        if (existing.note != cleanNote) stamp("note")
        if (existing.due != resolvedDue) stamp("due")
        if (existing.start != normalizedStart || existing.end != normalizedEnd) stamp("time")
        if (existing.repeat != normalizedRepeat) stamp("repeat")
        if (existing.remind != normalizedRemind) stamp("remind")
        if (existing.urgent != draft.urgent || existing.important != draft.important) stamp("flags")
        if (existing.goalId != draft.goalId || existing.goalRefs != goalRefs) stamp("goals")
        if (existing.tagIds != cleanTagIds) stamp("tags")
        if (existing.personIds != cleanPersonIds) stamp("persons")
        if (existing.locationIds != cleanLocationIds) stamp("locations")

        val updated = existing.copy(
            title = cleanTitle,
            note = cleanNote,
            due = resolvedDue,
            start = normalizedStart,
            end = normalizedEnd,
            repeat = normalizedRepeat,
            remind = normalizedRemind,
            urgent = draft.urgent,
            important = draft.important,
            goalId = draft.goalId,
            goalRefs = goalRefs,
            tagIds = cleanTagIds,
            personIds = cleanPersonIds,
            locationIds = cleanLocationIds,
            del = false,
            hlc = latestHlc,
            fieldHlcs = fieldMap,
        )
        commitStore(store.copy(schedules = store.schedules.map { if (it.id == existing.id) updated else it }))
        return updated
    }

    fun deleteSchedule(scheduleId: String): Boolean = synchronized(stateLock) {
        val store = _storeFlow.value
        val existing = store.schedules.firstOrNull { it.id == scheduleId && !it.del } ?: return false
        val tick = clock.tick()
        val tombstone = existing.copy(
            del = true,
            hlc = tick,
        )
        commitStore(store.copy(schedules = store.schedules.map { if (it.id == scheduleId) tombstone else it }))
        true
    }

    fun moveScheduleToDate(
        scheduleId: String,
        targetDate: LocalDate?,
        now: LocalDateTime = nowProvider(),
    ): String? = synchronized(stateLock) {
        val store = _storeFlow.value
        val existing = store.schedules.firstOrNull { it.id == scheduleId && !it.del } ?: return null
        val tick = clock.tick()
        val fieldMap = existing.fieldHlcs.orEmpty().toMutableMap()

        val updated = if (targetDate == null) {
            fieldMap["due"] = tick
            fieldMap["time"] = tick
            existing.copy(
                due = null,
                start = null,
                end = null,
                hlc = tick,
                fieldHlcs = fieldMap,
            )
        } else {
            val parsed = DeadlineRules.parseDue(existing.due)
            val nextTime = if (
                targetDate == now.toLocalDate() &&
                parsed.dueTime != null &&
                !now.toLocalTime().isBefore(parsed.dueTime)
            ) {
                null
            } else {
                parsed.dueTime
            }
            val nextDue = DeadlineRules.formatDue(targetDate, nextTime)
            fieldMap["due"] = tick
            existing.copy(
                due = nextDue,
                hlc = tick,
                fieldHlcs = fieldMap,
            )
        }

        commitStore(store.copy(schedules = store.schedules.map { if (it.id == scheduleId) updated else it }))
        val today = now.toLocalDate()
        return when (targetDate) {
            null -> "已移入「未来事项」"
            today -> "已移到「今天」"
            today.plusDays(1) -> "已移到「明天」"
            else -> "已改期到 ${targetDate.monthValue}月${targetDate.dayOfMonth}日"
        }
    }

    fun upsertGoal(draft: GoalDraft): GoalDto = synchronized(stateLock) {
        val store = _storeFlow.value
        val cleanTitle = draft.title.trim().ifEmpty { "未命名目标" }
        val cleanColor = draft.color.trim().ifEmpty { "#527CE6" }
        val parsedDue = DeadlineRules.parseDue(draft.dueText)
        val normalizedDue = parsedDue.date?.let { DeadlineRules.formatDue(it, parsedDue.dueTime) }

        val existing = draft.id?.let { id -> store.goals.firstOrNull { it.id == id } }
        if (existing == null) {
            val tick = clock.tick()
            val created = GoalDto(
                id = generateShortId(),
                title = cleanTitle,
                color = cleanColor,
                isCompleted = draft.isCompleted,
                due = normalizedDue,
                hlc = tick,
                del = false,
                fieldHlcs = SyncMergeEngine.GOAL_FIELDS.associateWith { tick },
            )
            commitStore(store.copy(goals = store.goals + created))
            return created
        }

        val fieldMap = existing.fieldHlcs.orEmpty().toMutableMap()
        var latestHlc = existing.hlc
        fun stamp(field: String) {
            val t = clock.tick()
            fieldMap[field] = t
            latestHlc = t
        }
        if (existing.title != cleanTitle) stamp("title")
        if (existing.color != cleanColor) stamp("color")
        if (existing.isCompleted != draft.isCompleted) stamp("isCompleted")
        if (existing.due != normalizedDue) stamp("due")

        val updated = existing.copy(
            title = cleanTitle,
            color = cleanColor,
            isCompleted = draft.isCompleted,
            due = normalizedDue,
            del = false,
            hlc = latestHlc,
            fieldHlcs = fieldMap,
        )
        commitStore(store.copy(goals = store.goals.map { if (it.id == existing.id) updated else it }))
        return updated
    }

    fun toggleGoalCompleted(goalId: String): String? = synchronized(stateLock) {
        val store = _storeFlow.value
        val existing = store.goals.firstOrNull { it.id == goalId && !it.del } ?: return null
        val nextDone = !existing.isCompleted
        val tick = clock.tick()
        val updated = existing.copy(
            isCompleted = nextDone,
            hlc = tick,
            fieldHlcs = existing.fieldHlcs.orEmpty() + ("isCompleted" to tick),
        )
        commitStore(store.copy(goals = store.goals.map { if (it.id == goalId) updated else it }))
        return if (nextDone) "已完成目标「${existing.title}」" else "已重启目标「${existing.title}」"
    }

    fun deleteGoal(goalId: String): Boolean = synchronized(stateLock) {
        val store = _storeFlow.value
        val existing = store.goals.firstOrNull { it.id == goalId && !it.del } ?: return false
        val tick = clock.tick()
        val tombstone = existing.copy(del = true, hlc = tick)
        val nextGoals = store.goals.map { if (it.id == goalId) tombstone else it }

        // 删目标不删日程，仅解除引用并推进对应日程 goals 字段 HLC
        val nextSchedules = store.schedules.map { s ->
            val refsMatch = s.goalRefs.any { it.goalId == goalId }
            if (s.goalId == goalId || refsMatch) {
                val remainingRefs = s.goalRefs.filterNot { it.goalId == goalId }
                val nextPrimaryGoalId = if (s.goalId == goalId) remainingRefs.firstOrNull()?.goalId else s.goalId
                val sTick = clock.tick()
                s.copy(
                    goalId = nextPrimaryGoalId,
                    goalRefs = remainingRefs,
                    hlc = sTick,
                    fieldHlcs = s.fieldHlcs.orEmpty() + ("goals" to sTick),
                )
            } else {
                s
            }
        }
        commitStore(store.copy(goals = nextGoals, schedules = nextSchedules))
        true
    }

    fun upsertTag(draft: TagDraft): TagDto = synchronized(stateLock) {
        val store = _storeFlow.value
        val cleanName = draft.name.trim().ifEmpty { "未命名标签" }
        val cleanColor = draft.color.trim().ifEmpty { "#527CE6" }
        val existing = draft.id?.let { id -> store.tags.firstOrNull { it.id == id } }
        if (existing == null) {
            val tick = clock.tick()
            val created = TagDto(
                id = generateShortId(),
                name = cleanName,
                color = cleanColor,
                hlc = tick,
                del = false,
                fieldHlcs = SyncMergeEngine.TAG_FIELDS.associateWith { tick },
            )
            commitStore(store.copy(tags = store.tags + created))
            return created
        }

        val fieldMap = existing.fieldHlcs.orEmpty().toMutableMap()
        var latestHlc = existing.hlc
        fun stamp(field: String) {
            val t = clock.tick()
            fieldMap[field] = t
            latestHlc = t
        }
        if (existing.name != cleanName) stamp("name")
        if (existing.color != cleanColor) stamp("color")

        val updated = existing.copy(
            name = cleanName,
            color = cleanColor,
            del = false,
            hlc = latestHlc,
            fieldHlcs = fieldMap,
        )
        commitStore(store.copy(tags = store.tags.map { if (it.id == existing.id) updated else it }))
        return updated
    }

    fun deleteTag(tagId: String): Boolean = synchronized(stateLock) {
        val store = _storeFlow.value
        val existing = store.tags.firstOrNull { it.id == tagId && !it.del } ?: return false
        val tick = clock.tick()
        val tombstone = existing.copy(del = true, hlc = tick)
        val nextTags = store.tags.map { if (it.id == tagId) tombstone else it }
        val nextSchedules = store.schedules.map { s ->
            if (tagId in s.tagIds) {
                val sTick = clock.tick()
                s.copy(
                    tagIds = s.tagIds.filterNot { it == tagId },
                    hlc = sTick,
                    fieldHlcs = s.fieldHlcs.orEmpty() + ("tags" to sTick),
                )
            } else {
                s
            }
        }
        commitStore(store.copy(tags = nextTags, schedules = nextSchedules))
        true
    }

    fun upsertPerson(draft: PersonDraft): PersonDto = synchronized(stateLock) {
        val store = _storeFlow.value
        val cleanName = draft.name.trim().ifEmpty { "未命名人物" }
        val existing = draft.id?.let { id -> store.persons.firstOrNull { it.id == id } }
        if (existing == null) {
            val tick = clock.tick()
            val created = PersonDto(
                id = generateShortId(),
                name = cleanName,
                hlc = tick,
                del = false,
                fieldHlcs = SyncMergeEngine.PERSON_FIELDS.associateWith { tick },
            )
            commitStore(store.copy(persons = store.persons + created))
            return created
        }

        val fieldMap = existing.fieldHlcs.orEmpty().toMutableMap()
        var latestHlc = existing.hlc
        if (existing.name != cleanName) {
            val t = clock.tick()
            fieldMap["name"] = t
            latestHlc = t
        }
        val updated = existing.copy(
            name = cleanName,
            del = false,
            hlc = latestHlc,
            fieldHlcs = fieldMap,
        )
        commitStore(store.copy(persons = store.persons.map { if (it.id == existing.id) updated else it }))
        return updated
    }

    fun deletePerson(personId: String): Boolean = synchronized(stateLock) {
        val store = _storeFlow.value
        val existing = store.persons.firstOrNull { it.id == personId && !it.del } ?: return false
        val tick = clock.tick()
        val tombstone = existing.copy(del = true, hlc = tick)
        val nextPersons = store.persons.map { if (it.id == personId) tombstone else it }
        val nextSchedules = store.schedules.map { s ->
            if (personId in s.personIds) {
                val sTick = clock.tick()
                s.copy(
                    personIds = s.personIds.filterNot { it == personId },
                    hlc = sTick,
                    fieldHlcs = s.fieldHlcs.orEmpty() + ("persons" to sTick),
                )
            } else {
                s
            }
        }
        commitStore(store.copy(persons = nextPersons, schedules = nextSchedules))
        true
    }

    fun upsertLocation(draft: LocationDraft): LocationDto = synchronized(stateLock) {
        val store = _storeFlow.value
        val cleanName = draft.name.trim().ifEmpty { "未命名地点" }
        val existing = draft.id?.let { id -> store.locations.firstOrNull { it.id == id } }
        if (existing == null) {
            val tick = clock.tick()
            val created = LocationDto(
                id = generateShortId(),
                name = cleanName,
                hlc = tick,
                del = false,
                fieldHlcs = SyncMergeEngine.LOCATION_FIELDS.associateWith { tick },
            )
            commitStore(store.copy(locations = store.locations + created))
            return created
        }

        val fieldMap = existing.fieldHlcs.orEmpty().toMutableMap()
        var latestHlc = existing.hlc
        if (existing.name != cleanName) {
            val t = clock.tick()
            fieldMap["name"] = t
            latestHlc = t
        }
        val updated = existing.copy(
            name = cleanName,
            del = false,
            hlc = latestHlc,
            fieldHlcs = fieldMap,
        )
        commitStore(store.copy(locations = store.locations.map { if (it.id == existing.id) updated else it }))
        return updated
    }

    fun deleteLocation(locationId: String): Boolean = synchronized(stateLock) {
        val store = _storeFlow.value
        val existing = store.locations.firstOrNull { it.id == locationId && !it.del } ?: return false
        val tick = clock.tick()
        val tombstone = existing.copy(del = true, hlc = tick)
        val nextLocations = store.locations.map { if (it.id == locationId) tombstone else it }
        val nextSchedules = store.schedules.map { s ->
            if (locationId in s.locationIds) {
                val sTick = clock.tick()
                s.copy(
                    locationIds = s.locationIds.filterNot { it == locationId },
                    hlc = sTick,
                    fieldHlcs = s.fieldHlcs.orEmpty() + ("locations" to sTick),
                )
            } else {
                s
            }
        }
        commitStore(store.copy(locations = nextLocations, schedules = nextSchedules))
        true
    }

    fun flush() {
        writer.flushNow(_storeFlow.value)
    }

    private fun commitStore(next: StoreDto, fromLocalMutation: Boolean = true) {
        _storeFlow.value = next
        writer.scheduleSave(next)
        onStoreChanged?.invoke(next, fromLocalMutation)
    }

    private fun observeStoreHlcs(store: StoreDto) {
        store.goals.forEach { g ->
            clock.observe(g.hlc)
            g.fieldHlcs?.values?.forEach(clock::observe)
        }
        store.tags.forEach { t ->
            clock.observe(t.hlc)
            t.fieldHlcs?.values?.forEach(clock::observe)
        }
        store.persons.forEach { p ->
            clock.observe(p.hlc)
            p.fieldHlcs?.values?.forEach(clock::observe)
        }
        store.locations.forEach { l ->
            clock.observe(l.hlc)
            l.fieldHlcs?.values?.forEach(clock::observe)
        }
        store.schedules.forEach { s ->
            clock.observe(s.hlc)
            s.fieldHlcs?.values?.forEach(clock::observe)
        }
        store.presets.orEmpty().forEach { p ->
            clock.observe(p.hlc)
            p.fieldHlcs?.values?.forEach(clock::observe)
        }
    }

    private fun generateShortId(): String =
        UUID.randomUUID().toString().replace("-", "").take(10)
}
