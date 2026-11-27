package com.daynew.app.ui.main

import androidx.lifecycle.ViewModel
import androidx.lifecycle.ViewModelProvider
import androidx.lifecycle.viewModelScope
import com.daynew.app.alarm.ReminderAlarmScheduler
import com.daynew.app.data.DayNewRepository
import com.daynew.app.data.GoalDraft
import com.daynew.app.data.LaneKind
import com.daynew.app.data.LaneRules
import com.daynew.app.data.LocationDraft
import com.daynew.app.data.PersonDraft
import com.daynew.app.data.ScheduleDraft
import com.daynew.app.data.TagDraft
import com.daynew.app.data.TimelineBlockInput
import com.daynew.app.data.computeTimelineOverlapLayout
import com.daynew.app.sync.SyncCoordinator
import com.daynew.app.sync.SyncExecutionResult
import com.daynew.app.sync.SyncStatusUi
import com.daynew.sync.DeadlineRules
import com.daynew.sync.GoalDto
import com.daynew.sync.LocationDto
import com.daynew.sync.PersonDto
import com.daynew.sync.RemindRules
import com.daynew.sync.RepeatRules
import com.daynew.sync.ScheduleDto
import com.daynew.sync.StoreDto
import com.daynew.sync.TagDto
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.SharingStarted
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.combine
import kotlinx.coroutines.flow.stateIn
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch
import java.time.LocalDate
import java.time.LocalDateTime
import java.time.format.TextStyle as JavaTextStyle
import java.util.Locale

enum class AppPage(val title: String) {
    OVERVIEW("概览"),
    TIMELINE("视图"),
    EISENHOWER("艾森豪威尔看板"),
    GOALS("目标"),
    DEADLINES("截止日期"),
    FUTURE("未来事项"),
    TAGS("标签"),
    SETTINGS("设置"),
}

enum class BottomTab(val title: String, val page: AppPage) {
    OVERVIEW("概览", AppPage.OVERVIEW),
    TIMELINE("视图", AppPage.TIMELINE),
    EISENHOWER("艾森豪威尔看板", AppPage.EISENHOWER),
    GOALS("目标", AppPage.GOALS),
}

enum class TagPageSection(val title: String) {
    TAGS("标签"),
    PERSONS("人物"),
    LOCATIONS("地点"),
}

enum class EisenhowerKind(val title: String, val subtitle: String) {
    URGENT_IMPORTANT("紧急且重要", "优先立即处理"),
    IMPORTANT_ONLY("重要不紧急", "规划推进安排"),
    URGENT_ONLY("紧急不重要", "尽快收尾或委派"),
    UNMARKED("未标记", "常规待整理事项"),
}

data class TagChipUi(
    val id: String,
    val name: String,
    val colorHex: String,
)

data class ScheduleCardUi(
    val id: String,
    val dto: ScheduleDto,
    val lane: LaneKind?,
    val goalTitle: String?,
    val goalColorHex: String?,
    val tags: List<TagChipUi>,
    val personNames: List<String> = emptyList(),
    val locationNames: List<String> = emptyList(),
    val timeLabel: String?,
    val dueDateLabel: String?,
    val repeatLabel: String?,
    val remindLabel: String?,
    val isOverdue: Boolean,
)

data class GoalProgressUi(
    val goal: GoalDto,
    val totalCount: Int,
    val doneCount: Int,
    val schedules: List<ScheduleCardUi> = emptyList(),
)

data class TimelineBlockUi(
    val card: ScheduleCardUi,
    val startMinutes: Int,
    val endMinutes: Int,
    val columnIndex: Int,
    val columnCount: Int,
)

data class DayTimelineUi(
    val blocks: List<TimelineBlockUi> = emptyList(),
    val allDaySchedules: List<ScheduleCardUi> = emptyList(),
)

data class DeadlineGoalItemUi(
    val goalId: String,
    val title: String,
    val subtitle: String?,
    val colorHex: String,
    val dueDate: LocalDate,
)

data class DeadlineDayGroupUi(
    val date: LocalDate,
    val dateLabel: String,
    val isToday: Boolean,
    val isOverdue: Boolean,
    val schedules: List<ScheduleCardUi>,
    val goals: List<DeadlineGoalItemUi>,
)

data class EisenhowerBucketUi(
    val kind: EisenhowerKind,
    val title: String,
    val subtitle: String,
    val schedules: List<ScheduleCardUi>,
)

data class TagGroupUi(
    val tag: TagDto,
    val schedules: List<ScheduleCardUi>,
)

data class PersonGroupUi(
    val person: PersonDto,
    val schedules: List<ScheduleCardUi>,
)

data class LocationGroupUi(
    val location: LocationDto,
    val schedules: List<ScheduleCardUi>,
)

data class ToastNotice(
    val id: Long,
    val message: String,
)

data class DayNewUiState(
    val selectedPage: AppPage = AppPage.OVERVIEW,
    val selectedTagSection: TagPageSection = TagPageSection.TAGS,
    val selectedLane: LaneKind = LaneKind.TODAY,
    val selectedTimelineDate: LocalDate = LocalDate.now(),
    val lanes: Map<LaneKind, List<ScheduleCardUi>> = LaneKind.entries.associateWith { emptyList() },
    val lanePendingCounts: Map<LaneKind, Int> = LaneKind.entries.associateWith { 0 },
    val timelineByDate: Map<LocalDate, DayTimelineUi> = emptyMap(),
    val timelineBlocks: List<TimelineBlockUi> = emptyList(),
    val timelineAllDaySchedules: List<ScheduleCardUi> = emptyList(),
    val timelineDatesWithItems: Set<LocalDate> = emptySet(),
    val deadlineGroups: List<DeadlineDayGroupUi> = emptyList(),
    val futureSchedules: List<ScheduleCardUi> = emptyList(),
    val eisenhowerNextThreeDaysOnly: Boolean = true,
    val eisenhowerBuckets: List<EisenhowerBucketUi> = emptyList(),
    val tagGroups: List<TagGroupUi> = emptyList(),
    val personGroups: List<PersonGroupUi> = emptyList(),
    val locationGroups: List<LocationGroupUi> = emptyList(),
    val activeGoals: List<GoalDto> = emptyList(),
    val activeTags: List<TagDto> = emptyList(),
    val activePersons: List<PersonDto> = emptyList(),
    val activeLocations: List<LocationDto> = emptyList(),
    val goalProgressList: List<GoalProgressUi> = emptyList(),
    val editingDraft: ScheduleDraft? = null,
    val editingGoalDraft: GoalDraft? = null,
    val editingTagDraft: TagDraft? = null,
    val editingPersonDraft: PersonDraft? = null,
    val editingLocationDraft: LocationDraft? = null,
    val toast: ToastNotice? = null,
    val isDarkThemeOverride: Boolean? = null,
    val deviceId: String = "",
    val dataDirPath: String = "",
    val completionsCount: Int = 0,
    val totalScheduleCount: Int = 0,
    val syncStatus: SyncStatusUi = SyncStatusUi(),
    val exactAlarmAllowed: Boolean = true,
    val batteryOptimizationIgnored: Boolean = true,
) {
    val selectedBottomTab: BottomTab?
        get() = BottomTab.entries.firstOrNull { it.page == selectedPage }
}

class MainScreenViewModel(
    private val repository: DayNewRepository,
    private val syncCoordinator: SyncCoordinator? = null,
    private val alarmScheduler: ReminderAlarmScheduler? = null,
    private val nowProvider: () -> LocalDateTime = { LocalDateTime.now() },
) : ViewModel() {

    private var toastSequence = 0L

    private data class UiControls(
        val page: AppPage = AppPage.OVERVIEW,
        val tagSection: TagPageSection = TagPageSection.TAGS,
        val lane: LaneKind = LaneKind.TODAY,
        val timelineDate: LocalDate,
        val eisenhowerNextThreeDaysOnly: Boolean = true,
        val draft: ScheduleDraft? = null,
        val goalDraft: GoalDraft? = null,
        val tagDraft: TagDraft? = null,
        val personDraft: PersonDraft? = null,
        val locationDraft: LocationDraft? = null,
        val toast: ToastNotice? = null,
        val darkOverride: Boolean? = null,
        val exactAlarmAllowed: Boolean = true,
        val batteryOptimizationIgnored: Boolean = true,
    )

    private val controlsFlow = MutableStateFlow(
        UiControls(
            timelineDate = nowProvider().toLocalDate(),
            toast = repository.loadNotice?.let { ToastNotice(++toastSequence, it) },
            exactAlarmAllowed = alarmScheduler?.canScheduleExactAlarms() ?: true,
            batteryOptimizationIgnored = alarmScheduler?.isIgnoringBatteryOptimizations() ?: true,
        )
    )

    private val syncStatusFlow: StateFlow<SyncStatusUi> =
        syncCoordinator?.preferencesStore?.statusFlow ?: MutableStateFlow(SyncStatusUi())

    val uiState: StateFlow<DayNewUiState> = combine(
        repository.storeFlow,
        repository.completionsCountFlow,
        controlsFlow,
        syncStatusFlow,
    ) { store, completionsCount, controls, syncStatus ->
        buildUiState(store, completionsCount, controls, syncStatus)
    }.stateIn(
        scope = viewModelScope,
        started = SharingStarted.Eagerly,
        initialValue = buildUiState(
            store = repository.currentStore(),
            completionsCount = repository.completionsCountFlow.value,
            controls = controlsFlow.value,
            syncStatus = syncStatusFlow.value,
        ),
    )

    fun selectPage(page: AppPage) {
        controlsFlow.update { it.copy(page = page) }
    }

    fun selectTab(tab: BottomTab) {
        selectPage(tab.page)
    }

    fun selectTagSection(section: TagPageSection) {
        controlsFlow.update { it.copy(tagSection = section) }
    }

    fun setEisenhowerNextThreeDaysOnly(enabled: Boolean) {
        controlsFlow.update { it.copy(eisenhowerNextThreeDaysOnly = enabled) }
    }

    fun selectLane(lane: LaneKind) {
        controlsFlow.update { it.copy(lane = lane) }
    }

    fun selectTimelineDate(date: LocalDate) {
        controlsFlow.update { it.copy(timelineDate = date) }
    }

    fun moveSchedule(scheduleId: String, targetLane: LaneKind, targetIndex: Int) {
        val outcome = repository.moveSchedule(scheduleId, targetLane, targetIndex, nowProvider())
        if (outcome.crossLane) {
            controlsFlow.update { it.copy(lane = targetLane) }
        }
        outcome.toastMessage?.let(::showToast)
    }

    fun moveScheduleToDate(scheduleId: String, targetDate: LocalDate?) {
        val msg = repository.moveScheduleToDate(scheduleId, targetDate, nowProvider())
        msg?.let(::showToast)
    }

    fun toggleDone(scheduleId: String) {
        val msg = repository.toggleDone(scheduleId, nowProvider())
        msg?.let(::showToast)
    }

    fun openCreateSheet(lane: LaneKind = controlsFlow.value.lane) {
        val date = LaneRules.dueForLane(lane, nowProvider().toLocalDate())
        controlsFlow.update {
            it.copy(
                draft = ScheduleDraft(
                    lane = lane,
                    initialLane = lane,
                    hasDueDate = true,
                    useExactDate = false,
                    dueDate = date,
                    remind = RemindRules.DEFAULT,
                )
            )
        }
    }

    fun openCreateTimelineSheet(date: LocalDate, startHour: Int) {
        val clampedStart = startHour.coerceIn(0, 23)
        val endHour = (clampedStart + 1).coerceAtMost(23)
        val startStr = "%02d:00".format(Locale.ROOT, clampedStart)
        val endStr = if (clampedStart < 23) "%02d:00".format(Locale.ROOT, endHour) else "23:59"
        val previewLane = LaneRules.laneFor(
            ScheduleDto(due = DeadlineRules.formatDue(date, null)),
            nowProvider(),
        ) ?: LaneKind.TODAY
        controlsFlow.update {
            it.copy(
                draft = ScheduleDraft(
                    lane = previewLane,
                    initialLane = previewLane,
                    hasDueDate = true,
                    useExactDate = true,
                    dueDate = date,
                    startText = startStr,
                    endText = endStr,
                    remind = RemindRules.DEFAULT,
                )
            )
        }
    }

    fun openCreateDeadlineSheet() {
        val today = nowProvider().toLocalDate()
        controlsFlow.update {
            it.copy(
                draft = ScheduleDraft(
                    lane = LaneKind.TODAY,
                    initialLane = LaneKind.TODAY,
                    hasDueDate = true,
                    useExactDate = true,
                    dueDate = today,
                    remind = RemindRules.DEFAULT,
                )
            )
        }
    }

    fun openCreateFutureSheet() {
        controlsFlow.update {
            it.copy(
                draft = ScheduleDraft(
                    lane = LaneKind.LATER,
                    initialLane = LaneKind.LATER,
                    hasDueDate = false,
                    useExactDate = false,
                    dueDate = nowProvider().toLocalDate(),
                    remind = "",
                )
            )
        }
    }

    fun openEditSheet(scheduleId: String) {
        val store = repository.currentStore()
        val item = store.schedules.firstOrNull { it.id == scheduleId && !it.del } ?: return
        val now = nowProvider()
        val parsed = DeadlineRules.parseDue(item.due)
        val hasDue = parsed.date != null
        val lane = LaneRules.laneFor(item, now) ?: controlsFlow.value.lane
        val goalId = item.goalRefs.firstOrNull()?.goalId ?: item.goalId
        controlsFlow.update {
            it.copy(
                draft = ScheduleDraft(
                    id = item.id,
                    title = item.title,
                    note = item.note,
                    lane = lane,
                    initialLane = lane,
                    hasDueDate = hasDue,
                    useExactDate = hasDue,
                    dueDate = parsed.date ?: LaneRules.dueForLane(lane, now.toLocalDate()),
                    dueTime = parsed.dueTime,
                    startText = item.start.orEmpty(),
                    endText = item.end.orEmpty(),
                    repeat = RepeatRules.normalize(item.repeat),
                    remind = RemindRules.normalize(item.remind, legacyFallback = ""),
                    urgent = item.urgent,
                    important = item.important,
                    goalId = goalId,
                    tagIds = item.tagIds,
                    personIds = item.personIds,
                    locationIds = item.locationIds,
                )
            )
        }
    }

    fun updateEditingDraft(transform: (ScheduleDraft) -> ScheduleDraft) {
        controlsFlow.update { state ->
            val current = state.draft ?: return@update state
            state.copy(draft = transform(current))
        }
    }

    fun dismissEditor() {
        controlsFlow.update { it.copy(draft = null) }
    }

    fun saveEditor() {
        val draft = controlsFlow.value.draft ?: return
        val now = nowProvider()
        val saved = repository.upsertSchedule(draft, now)
        val nextLane = LaneRules.laneFor(saved, now)
        val savedDate = DeadlineRules.parseDue(saved.due).date
        controlsFlow.update { state ->
            state.copy(
                draft = null,
                lane = if (state.page == AppPage.OVERVIEW && nextLane != null) nextLane else state.lane,
                timelineDate = if (state.page == AppPage.TIMELINE && savedDate != null) savedDate else state.timelineDate,
            )
        }
        showToast(if (draft.id == null) "已新建「${saved.title}」" else "已保存「${saved.title}」")
    }

    fun deleteEditing() {
        val draft = controlsFlow.value.draft ?: return
        val id = draft.id ?: return
        if (repository.deleteSchedule(id)) {
            controlsFlow.update { it.copy(draft = null) }
            showToast("已删除「${draft.title.ifBlank { "日程" }}」")
        }
    }

    // ==================== 目标管理 ====================

    fun openCreateGoalSheet() {
        controlsFlow.update { it.copy(goalDraft = GoalDraft()) }
    }

    fun openEditGoalSheet(goalId: String) {
        val goal = repository.currentStore().goals.firstOrNull { it.id == goalId && !it.del } ?: return
        controlsFlow.update {
            it.copy(
                goalDraft = GoalDraft(
                    id = goal.id,
                    title = goal.title,
                    color = goal.color.ifBlank { "#527CE6" },
                    dueText = goal.due.orEmpty(),
                    isCompleted = goal.isCompleted,
                )
            )
        }
    }

    fun updateGoalDraft(transform: (GoalDraft) -> GoalDraft) {
        controlsFlow.update { state ->
            val current = state.goalDraft ?: return@update state
            state.copy(goalDraft = transform(current))
        }
    }

    fun dismissGoalEditor() {
        controlsFlow.update { it.copy(goalDraft = null) }
    }

    fun saveGoalEditor() {
        val draft = controlsFlow.value.goalDraft ?: return
        val saved = repository.upsertGoal(draft)
        controlsFlow.update { it.copy(goalDraft = null) }
        showToast(if (draft.id == null) "已新建目标「${saved.title}」" else "已保存目标「${saved.title}」")
    }

    fun deleteEditingGoal() {
        val draft = controlsFlow.value.goalDraft ?: return
        val id = draft.id ?: return
        if (repository.deleteGoal(id)) {
            controlsFlow.update { it.copy(goalDraft = null) }
            showToast("已删除目标「${draft.title.ifBlank { "目标" }}」（关联日程已保留）")
        }
    }

    fun toggleGoalCompleted(goalId: String) {
        repository.toggleGoalCompleted(goalId)?.let(::showToast)
    }

    // ==================== 标签管理 ====================

    fun openCreateTagSheet() {
        controlsFlow.update { it.copy(tagDraft = TagDraft()) }
    }

    fun openEditTagSheet(tagId: String) {
        val tag = repository.currentStore().tags.firstOrNull { it.id == tagId && !it.del } ?: return
        controlsFlow.update {
            it.copy(
                tagDraft = TagDraft(
                    id = tag.id,
                    name = tag.name,
                    color = tag.color.ifBlank { "#527CE6" },
                )
            )
        }
    }

    fun updateTagDraft(transform: (TagDraft) -> TagDraft) {
        controlsFlow.update { state ->
            val current = state.tagDraft ?: return@update state
            state.copy(tagDraft = transform(current))
        }
    }

    fun dismissTagEditor() {
        controlsFlow.update { it.copy(tagDraft = null) }
    }

    fun saveTagEditor() {
        val draft = controlsFlow.value.tagDraft ?: return
        val saved = repository.upsertTag(draft)
        controlsFlow.update { it.copy(tagDraft = null) }
        showToast(if (draft.id == null) "已新建标签「${saved.name}」" else "已保存标签「${saved.name}」")
    }

    fun deleteEditingTag() {
        val draft = controlsFlow.value.tagDraft ?: return
        val id = draft.id ?: return
        if (repository.deleteTag(id)) {
            controlsFlow.update { it.copy(tagDraft = null) }
            showToast("已删除标签「${draft.name.ifBlank { "标签" }}」")
        }
    }

    // ==================== 人物管理 ====================

    fun openCreatePersonSheet() {
        controlsFlow.update { it.copy(personDraft = PersonDraft()) }
    }

    fun openEditPersonSheet(personId: String) {
        val person = repository.currentStore().persons.firstOrNull { it.id == personId && !it.del } ?: return
        controlsFlow.update { it.copy(personDraft = PersonDraft(id = person.id, name = person.name)) }
    }

    fun updatePersonDraft(transform: (PersonDraft) -> PersonDraft) {
        controlsFlow.update { state ->
            val current = state.personDraft ?: return@update state
            state.copy(personDraft = transform(current))
        }
    }

    fun dismissPersonEditor() {
        controlsFlow.update { it.copy(personDraft = null) }
    }

    fun savePersonEditor() {
        val draft = controlsFlow.value.personDraft ?: return
        val saved = repository.upsertPerson(draft)
        controlsFlow.update { it.copy(personDraft = null) }
        showToast(if (draft.id == null) "已新建人物「${saved.name}」" else "已保存人物「${saved.name}」")
    }

    fun deleteEditingPerson() {
        val draft = controlsFlow.value.personDraft ?: return
        val id = draft.id ?: return
        if (repository.deletePerson(id)) {
            controlsFlow.update { it.copy(personDraft = null) }
            showToast("已删除人物「${draft.name.ifBlank { "人物" }}」")
        }
    }

    // ==================== 地点管理 ====================

    fun openCreateLocationSheet() {
        controlsFlow.update { it.copy(locationDraft = LocationDraft()) }
    }

    fun openEditLocationSheet(locationId: String) {
        val loc = repository.currentStore().locations.firstOrNull { it.id == locationId && !it.del } ?: return
        controlsFlow.update { it.copy(locationDraft = LocationDraft(id = loc.id, name = loc.name)) }
    }

    fun updateLocationDraft(transform: (LocationDraft) -> LocationDraft) {
        controlsFlow.update { state ->
            val current = state.locationDraft ?: return@update state
            state.copy(locationDraft = transform(current))
        }
    }

    fun dismissLocationEditor() {
        controlsFlow.update { it.copy(locationDraft = null) }
    }

    fun saveLocationEditor() {
        val draft = controlsFlow.value.locationDraft ?: return
        val saved = repository.upsertLocation(draft)
        controlsFlow.update { it.copy(locationDraft = null) }
        showToast(if (draft.id == null) "已新建地点「${saved.name}」" else "已保存地点「${saved.name}」")
    }

    fun deleteEditingLocation() {
        val draft = controlsFlow.value.locationDraft ?: return
        val id = draft.id ?: return
        if (repository.deleteLocation(id)) {
            controlsFlow.update { it.copy(locationDraft = null) }
            showToast("已删除地点「${draft.name.ifBlank { "地点" }}」")
        }
    }

    fun toggleTheme(currentlyDark: Boolean) {
        controlsFlow.update { it.copy(darkOverride = !currentlyDark) }
    }

    fun consumeToast(id: Long) {
        controlsFlow.update { state ->
            if (state.toast?.id == id) state.copy(toast = null) else state
        }
    }

    fun flushPersistence() {
        repository.flush()
    }

    fun refreshPermissionsState() {
        val allowed = alarmScheduler?.canScheduleExactAlarms() ?: true
        val batteryIgnored = alarmScheduler?.isIgnoringBatteryOptimizations() ?: true
        controlsFlow.update {
            it.copy(
                exactAlarmAllowed = allowed,
                batteryOptimizationIgnored = batteryIgnored,
            )
        }
    }

    fun testWebDavConnection(url: String, username: String, password: String) {
        val coordinator = syncCoordinator ?: return
        viewModelScope.launch {
            val result = coordinator.testConnection(url, username, password)
            showToast(result.message)
        }
    }

    fun saveWebDavConfig(url: String, username: String, password: String, autoSync: Boolean) {
        val coordinator = syncCoordinator ?: return
        coordinator.saveConfig(url, username, password, autoSync)
        showToast("已保存 WebDAV 同步配置")
    }

    fun setAutoSyncEnabled(enabled: Boolean) {
        syncCoordinator?.setAutoSyncEnabled(enabled)
        showToast(if (enabled) "已开启自动增量同步" else "已关闭自动同步")
    }

    fun triggerManualSync() {
        val coordinator = syncCoordinator ?: return
        viewModelScope.launch {
            when (val res = coordinator.syncNow()) {
                is SyncExecutionResult.Success -> showToast("同步完成：${res.summary}")
                is SyncExecutionResult.NotConfigured -> showToast(res.message)
                is SyncExecutionResult.AuthFailure -> showToast(res.message)
                is SyncExecutionResult.TransientFailure -> showToast("同步失败：${res.message}")
            }
        }
    }

    fun showToastMessage(message: String) {
        showToast(message)
    }

    private fun showToast(message: String) {
        controlsFlow.update { it.copy(toast = ToastNotice(++toastSequence, message)) }
    }

    private fun buildUiState(
        store: StoreDto,
        completionsCount: Int,
        controls: UiControls,
        syncStatus: SyncStatusUi,
    ): DayNewUiState {
        val now = nowProvider()
        val today = now.toLocalDate()

        val activeGoals = store.goals.filter { !it.del }
        val goalsById = activeGoals.associateBy { it.id }

        val activeTags = store.tags.filter { !it.del }
        val tagsById = activeTags.associateBy { it.id }

        val activePersons = store.persons.filter { !it.del }
        val personsById = activePersons.associateBy { it.id }

        val activeLocations = store.locations.filter { !it.del }
        val locationsById = activeLocations.associateBy { it.id }

        val activeSchedules = store.schedules.filter { !it.del }
        val laneBuckets = LaneKind.entries.associateWith { mutableListOf<ScheduleCardUi>() }
        val cardById = HashMap<String, ScheduleCardUi>(activeSchedules.size)

        val timelineInputsByDate = HashMap<LocalDate, MutableList<TimelineBlockInput>>()
        val timelineAllDayByDate = HashMap<LocalDate, MutableList<ScheduleCardUi>>()
        val timelineDatesWithItems = HashSet<LocalDate>()

        val futureList = mutableListOf<ScheduleCardUi>()

        val deadlineScheduleGroups = sortedMapOf<LocalDate, MutableList<ScheduleCardUi>>()
        val deadlineGoalGroups = sortedMapOf<LocalDate, MutableList<DeadlineGoalItemUi>>()

        val eisenhowerMap = EisenhowerKind.entries.associateWith { mutableListOf<ScheduleCardUi>() }

        for (item in activeSchedules) {
            val lane = LaneRules.laneFor(item, now)
            val parsed = DeadlineRules.parseDue(item.due)
            val card = toScheduleCardUi(item, now, today, lane, goalsById, tagsById, personsById, locationsById)
            cardById[item.id] = card

            if (lane != null) {
                laneBuckets.getValue(lane).add(card)
            }

            val parsedDueDate = parsed.date
            if (parsedDueDate != null) {
                val startClock = DeadlineRules.tryParseClock(item.start)
                if (startClock != null) {
                    timelineDatesWithItems.add(parsedDueDate)
                    val endClock = DeadlineRules.tryParseClock(item.end)
                    val startMinutes = startClock.hour * 60 + startClock.minute
                    val rawEndMinutes = if (endClock != null && endClock.isAfter(startClock)) {
                        endClock.hour * 60 + endClock.minute
                    } else {
                        (startMinutes + 30).coerceAtMost(24 * 60)
                    }
                    timelineInputsByDate.getOrPut(parsedDueDate) { mutableListOf() } += TimelineBlockInput(
                        id = item.id,
                        startMinutes = startMinutes,
                        endMinutes = rawEndMinutes,
                    )
                } else {
                    timelineAllDayByDate.getOrPut(parsedDueDate) { mutableListOf() } += card
                }
            } else {
                futureList += card
            }

            if (!item.done && parsedDueDate != null && item.start.isNullOrBlank()) {
                deadlineScheduleGroups.getOrPut(parsedDueDate) { mutableListOf() }.add(card)
            }

            val eisenhowerWindowEnd = today.plusDays(2)
            val matchesEisenhowerScope = !item.done && (
                !controls.eisenhowerNextThreeDaysOnly ||
                    (parsedDueDate != null && !parsedDueDate.isBefore(today) && !parsedDueDate.isAfter(eisenhowerWindowEnd))
                )
            if (matchesEisenhowerScope) {
                val qKind = when {
                    item.urgent && item.important -> EisenhowerKind.URGENT_IMPORTANT
                    !item.urgent && item.important -> EisenhowerKind.IMPORTANT_ONLY
                    item.urgent && !item.important -> EisenhowerKind.URGENT_ONLY
                    else -> EisenhowerKind.UNMARKED
                }
                eisenhowerMap.getValue(qKind).add(card)
            }
        }

        // 未完成且有截止日的目标及其子节点也汇入截止日期分组
        for (goal in activeGoals) {
            if (goal.isCompleted) continue
            val goalColor = goal.color.ifBlank { "#527CE6" }
            DeadlineRules.parseDue(goal.due).date?.let { dueDate ->
                deadlineGoalGroups.getOrPut(dueDate) { mutableListOf() }.add(
                    DeadlineGoalItemUi(
                        goalId = goal.id,
                        title = goal.title,
                        subtitle = "远景目标",
                        colorHex = goalColor,
                        dueDate = dueDate,
                    )
                )
            }
            for (node in goal.nodes) {
                if (node.isCompleted) continue
                DeadlineRules.parseDue(node.due).date?.let { nodeDate ->
                    deadlineGoalGroups.getOrPut(nodeDate) { mutableListOf() }.add(
                        DeadlineGoalItemUi(
                            goalId = goal.id,
                            title = node.title,
                            subtitle = "子节点 · ${goal.title}",
                            colorHex = goalColor,
                            dueDate = nodeDate,
                        )
                    )
                }
            }
        }

        val allTimelineDates = (timelineInputsByDate.keys + timelineAllDayByDate.keys)
        val timelineByDate = allTimelineDates.associateWith { date ->
            val daySlots = computeTimelineOverlapLayout(timelineInputsByDate[date].orEmpty())
            val dayBlocks = daySlots.mapNotNull { slot ->
                val card = cardById[slot.id] ?: return@mapNotNull null
                TimelineBlockUi(
                    card = card,
                    startMinutes = slot.startMinutes,
                    endMinutes = slot.endMinutes,
                    columnIndex = slot.columnIndex,
                    columnCount = slot.columnCount,
                )
            }
            val dayAllDay = timelineAllDayByDate[date].orEmpty().sortedWith(
                compareBy<ScheduleCardUi> { it.dto.done }.thenBy { it.dto.order }.thenBy { it.id }
            )
            DayTimelineUi(blocks = dayBlocks, allDaySchedules = dayAllDay)
        }
        val currentTimelineDay = timelineByDate[controls.timelineDate] ?: DayTimelineUi()

        val allDeadlineDates = (deadlineScheduleGroups.keys + deadlineGoalGroups.keys).toSortedSet()
        val deadlineGroups = allDeadlineDates.map { date ->
            val schedules = deadlineScheduleGroups[date].orEmpty()
                .sortedWith(compareBy<ScheduleCardUi> { it.dto.order }.thenBy { it.id })
            val goals = deadlineGoalGroups[date].orEmpty()
            DeadlineDayGroupUi(
                date = date,
                dateLabel = formatDeadlineHeader(date, today),
                isToday = date == today,
                isOverdue = date.isBefore(today),
                schedules = schedules,
                goals = goals,
            )
        }

        val sortedLanes = laneBuckets.mapValues { (_, list) ->
            list.sortedWith(compareBy<ScheduleCardUi> { it.dto.done }.thenBy { it.dto.order }.thenBy { it.id })
        }
        val pendingCounts = sortedLanes.mapValues { (_, list) -> list.count { !it.dto.done } }

        val sortedFuture = futureList.sortedWith(
            compareBy<ScheduleCardUi> { it.dto.done }.thenBy { it.dto.order }.thenBy { it.id }
        )

        val eisenhowerBuckets = EisenhowerKind.entries.map { kind ->
            val list = eisenhowerMap.getValue(kind).sortedWith(
                compareBy<ScheduleCardUi> { it.dto.done }
                    .thenBy { it.dto.due ?: "9999-12-31" }
                    .thenBy { it.dto.order }
                    .thenBy { it.id }
            )
            EisenhowerBucketUi(
                kind = kind,
                title = kind.title,
                subtitle = kind.subtitle,
                schedules = list,
            )
        }

        val tagGroups = activeTags.map { tag ->
            val linked = activeSchedules
                .asSequence()
                .filter { tag.id in it.tagIds }
                .mapNotNull { cardById[it.id] }
                .sortedWith(compareBy<ScheduleCardUi> { it.dto.done }.thenBy { it.dto.order }.thenBy { it.id })
                .toList()
            TagGroupUi(tag = tag, schedules = linked)
        }

        val personGroups = activePersons.map { person ->
            val linked = activeSchedules
                .asSequence()
                .filter { person.id in it.personIds }
                .mapNotNull { cardById[it.id] }
                .sortedWith(compareBy<ScheduleCardUi> { it.dto.done }.thenBy { it.dto.order }.thenBy { it.id })
                .toList()
            PersonGroupUi(person = person, schedules = linked)
        }

        val locationGroups = activeLocations.map { location ->
            val linked = activeSchedules
                .asSequence()
                .filter { location.id in it.locationIds }
                .mapNotNull { cardById[it.id] }
                .sortedWith(compareBy<ScheduleCardUi> { it.dto.done }.thenBy { it.dto.order }.thenBy { it.id })
                .toList()
            LocationGroupUi(location = location, schedules = linked)
        }

        val goalProgress = activeGoals.map { goal ->
            val linked = activeSchedules
                .asSequence()
                .filter { s ->
                    val primary = s.goalId == goal.id
                    val byRef = s.goalRefs.any { it.goalId == goal.id }
                    primary || byRef
                }
                .mapNotNull { cardById[it.id] }
                .sortedWith(compareBy<ScheduleCardUi> { it.dto.done }.thenBy { it.dto.order }.thenBy { it.id })
                .toList()
            GoalProgressUi(
                goal = goal,
                totalCount = linked.size,
                doneCount = linked.count { it.dto.done },
                schedules = linked,
            )
        }

        return DayNewUiState(
            selectedPage = controls.page,
            selectedTagSection = controls.tagSection,
            selectedLane = controls.lane,
            selectedTimelineDate = controls.timelineDate,
            lanes = sortedLanes,
            lanePendingCounts = pendingCounts,
            timelineByDate = timelineByDate,
            timelineBlocks = currentTimelineDay.blocks,
            timelineAllDaySchedules = currentTimelineDay.allDaySchedules,
            timelineDatesWithItems = timelineDatesWithItems,
            deadlineGroups = deadlineGroups,
            futureSchedules = sortedFuture,
            eisenhowerNextThreeDaysOnly = controls.eisenhowerNextThreeDaysOnly,
            eisenhowerBuckets = eisenhowerBuckets,
            tagGroups = tagGroups,
            personGroups = personGroups,
            locationGroups = locationGroups,
            activeGoals = activeGoals,
            activeTags = activeTags,
            activePersons = activePersons,
            activeLocations = activeLocations,
            goalProgressList = goalProgress,
            editingDraft = controls.draft,
            editingGoalDraft = controls.goalDraft,
            editingTagDraft = controls.tagDraft,
            editingPersonDraft = controls.personDraft,
            editingLocationDraft = controls.locationDraft,
            toast = controls.toast,
            isDarkThemeOverride = controls.darkOverride,
            deviceId = repository.deviceId,
            dataDirPath = repository.storeFile.dataFile.absolutePath,
            completionsCount = completionsCount,
            totalScheduleCount = activeSchedules.size,
            syncStatus = syncStatus,
            exactAlarmAllowed = controls.exactAlarmAllowed,
            batteryOptimizationIgnored = controls.batteryOptimizationIgnored,
        )
    }

    private fun toScheduleCardUi(
        item: ScheduleDto,
        now: LocalDateTime,
        today: LocalDate,
        lane: LaneKind?,
        goalsById: Map<String, GoalDto>,
        tagsById: Map<String, TagDto>,
        personsById: Map<String, PersonDto>,
        locationsById: Map<String, LocationDto>,
    ): ScheduleCardUi {
        val (dueDate, dueTime) = DeadlineRules.parseDue(item.due)
        val goalId = item.goalRefs.firstOrNull()?.goalId ?: item.goalId
        val goal = goalId?.let { goalsById[it] }
        val tagChips = item.tagIds.mapNotNull { tid ->
            tagsById[tid]?.let { TagChipUi(it.id, it.name, it.color.ifBlank { "#527CE6" }) }
        }
        val personNames = item.personIds.mapNotNull { pid -> personsById[pid]?.name }
        val locationNames = item.locationIds.mapNotNull { lid -> locationsById[lid]?.name }

        val timeLabel = when {
            !item.start.isNullOrBlank() && !item.end.isNullOrBlank() -> "${item.start} - ${item.end}"
            !item.start.isNullOrBlank() -> item.start
            dueTime != null -> "截止 ${DeadlineRules.formatClock(dueTime)}"
            else -> null
        }

        val dueDateLabel = when {
            dueDate == null -> "不定日期"
            dueDate == today -> null
            else -> formatShortDate(dueDate, today)
        }

        val repeatLabel = RepeatRules.normalize(item.repeat).ifEmpty { null }
        val remindLabel = if (!item.start.isNullOrBlank() || dueTime != null) {
            RemindRules.normalize(item.remind).takeIf { it.isNotEmpty() }?.let {
                RemindRules.formatLabel(it, isDeadlineOnly = item.start.isNullOrBlank() && dueTime != null)
            }
        } else {
            null
        }

        return ScheduleCardUi(
            id = item.id,
            dto = item,
            lane = lane,
            goalTitle = goal?.title,
            goalColorHex = goal?.color,
            tags = tagChips,
            personNames = personNames,
            locationNames = locationNames,
            timeLabel = timeLabel,
            dueDateLabel = dueDateLabel,
            repeatLabel = repeatLabel,
            remindLabel = remindLabel,
            isOverdue = lane == LaneKind.OVERDUE,
        )
    }

    private fun formatShortDate(date: LocalDate, today: LocalDate): String {
        return if (date.year == today.year) {
            "${date.monthValue}/${date.dayOfMonth}"
        } else {
            "${date.year}/${date.monthValue}/${date.dayOfMonth}"
        }
    }

    private fun formatDeadlineHeader(date: LocalDate, today: LocalDate): String {
        val weekday = date.dayOfWeek.getDisplayName(JavaTextStyle.SHORT, Locale.CHINA)
        val dateText = if (date.year == today.year) {
            "${date.monthValue}月${date.dayOfMonth}日"
        } else {
            "${date.year}年${date.monthValue}月${date.dayOfMonth}日"
        }
        return when {
            date == today -> "今天 · $dateText $weekday"
            date == today.plusDays(1) -> "明天 · $dateText $weekday"
            date.isBefore(today) -> "已逾期 · $dateText $weekday"
            else -> "$dateText $weekday"
        }
    }

    companion object {
        fun factory(
            repository: DayNewRepository,
            syncCoordinator: SyncCoordinator? = null,
            alarmScheduler: ReminderAlarmScheduler? = null,
        ): ViewModelProvider.Factory =
            object : ViewModelProvider.Factory {
                @Suppress("UNCHECKED_CAST")
                override fun <T : ViewModel> create(modelClass: Class<T>): T {
                    return MainScreenViewModel(
                        repository = repository,
                        syncCoordinator = syncCoordinator,
                        alarmScheduler = alarmScheduler,
                    ) as T
                }
            }
    }
}
