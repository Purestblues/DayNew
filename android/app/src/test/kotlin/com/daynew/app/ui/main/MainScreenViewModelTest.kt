package com.daynew.app.ui.main

import com.daynew.app.data.DayNewRepository
import com.daynew.app.data.LaneKind
import com.daynew.sync.RepeatRules
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.ExperimentalCoroutinesApi
import kotlinx.coroutines.test.StandardTestDispatcher
import kotlinx.coroutines.test.resetMain
import kotlinx.coroutines.test.setMain
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNotEquals
import org.junit.Assert.assertTrue
import org.junit.Before
import org.junit.Rule
import org.junit.Test
import org.junit.rules.TemporaryFolder
import java.time.LocalDateTime

@OptIn(ExperimentalCoroutinesApi::class)
class MainScreenViewModelTest {

    @get:Rule
    val tempFolder = TemporaryFolder()

    private val testDispatcher = StandardTestDispatcher()
    private val fixedNow = LocalDateTime.of(2026, 11, 27, 15, 0) // 周五

    @Before
    fun setup() {
        Dispatchers.setMain(testDispatcher)
    }

    @After
    fun tearDown() {
        Dispatchers.resetMain()
    }

    @Test
    fun seedsDemoDataOnFirstStartAndMovesAcrossLanesUpdatingDue() {
        val dir = tempFolder.newFolder("vm-move")
        val repo = DayNewRepository(dir, nowProvider = { fixedNow }, debounceMs = 10L)
        val vm = MainScreenViewModel(repo, nowProvider = { fixedNow })

        val state0 = vm.uiState.value
        assertEquals(LaneKind.TODAY, state0.selectedLane)
        assertEquals(3, state0.lanes[LaneKind.TODAY]?.size)
        assertEquals(1, state0.lanes[LaneKind.OVERDUE]?.size)

        // 把「逾期」第一张卡片拖到「今天」第 0 个位置
        val overdueCard = state0.lanes[LaneKind.OVERDUE]!!.first()
        vm.moveSchedule(overdueCard.id, LaneKind.TODAY, 0)
        testDispatcher.scheduler.advanceUntilIdle()

        val afterMove = repo.currentStore().schedules.first { it.id == overdueCard.id }
        assertEquals("2026-11-27", afterMove.due)
        assertTrue((afterMove.fieldHlcs?.get("due") ?: "").isNotEmpty())
        assertEquals(4, repo.currentStore().schedules.count { !it.del && com.daynew.app.data.LaneRules.laneFor(it, fixedNow) == LaneKind.TODAY })
        assertEquals(LaneKind.TODAY, vm.uiState.value.selectedLane)
        assertEquals("已移到「今天」", vm.uiState.value.toast?.message)
    }

    @Test
    fun reorderInsideSameLane_preservesDueAndUpdatesOrder() {
        val dir = tempFolder.newFolder("vm-reorder")
        val repo = DayNewRepository(dir, nowProvider = { fixedNow }, debounceMs = 10L)
        val vm = MainScreenViewModel(repo, nowProvider = { fixedNow })

        val todayCards = vm.uiState.value.lanes[LaneKind.TODAY]!!
        val first = todayCards[0]
        val second = todayCards[1]
        val originalDue = first.dto.due

        vm.moveSchedule(first.id, LaneKind.TODAY, 1)
        testDispatcher.scheduler.advanceUntilIdle()

        val reordered = repo.currentStore().schedules
            .filter { it.id == first.id || it.id == second.id }
            .associateBy { it.id }

        assertEquals(originalDue, reordered.getValue(first.id).due)
        assertTrue(reordered.getValue(second.id).order < reordered.getValue(first.id).order)
    }

    @Test
    fun toggleDone_writesCompletionLogAndAdvancesRepeatingTask() {
        val dir = tempFolder.newFolder("vm-done")
        val repo = DayNewRepository(dir, nowProvider = { fixedNow }, debounceMs = 10L)
        val vm = MainScreenViewModel(repo, nowProvider = { fixedNow })

        // 1. 勾选重复任务（sch_toda02 是每日重复）
        val repeatCard = vm.uiState.value.lanes[LaneKind.TODAY]!!
            .first { RepeatRules.hasRepeat(it.dto.repeat) }
        vm.toggleDone(repeatCard.id)

        val updatedRepeat = repo.currentStore().schedules.first { it.id == repeatCard.id }
        assertFalse(updatedRepeat.done)
        assertEquals("2026-11-28", updatedRepeat.due)

        // 2. 勾选普通任务
        val normalCard = vm.uiState.value.lanes[LaneKind.TODAY]!!
            .first { !RepeatRules.hasRepeat(it.dto.repeat) }
        vm.toggleDone(normalCard.id)

        val updatedNormal = repo.currentStore().schedules.first { it.id == normalCard.id }
        assertTrue(updatedNormal.done)

        val logs = repo.readCompletions()
        assertEquals(2, logs.size)
        assertEquals(repeatCard.id, logs[0].id)
        assertTrue(logs[0].done)
        assertEquals(normalCard.id, logs[1].id)
        assertTrue(logs[1].done)
    }

    @Test
    fun createAndSoftDeleteSchedule_persistsTombstoneAndFlushes() {
        val dir = tempFolder.newFolder("vm-crud")
        val repo = DayNewRepository(dir, nowProvider = { fixedNow }, debounceMs = 10L)
        val vm = MainScreenViewModel(repo, nowProvider = { fixedNow })

        vm.openCreateSheet(LaneKind.TOMORROW)
        vm.updateEditingDraft { it.copy(title = "安卓 A1 真机测试", startText = "09:30", endText = "10:30") }
        vm.saveEditor()

        val created = repo.currentStore().schedules.first { it.title == "安卓 A1 真机测试" }
        assertEquals("2026-11-28", created.due)
        assertEquals("09:30", created.start)
        assertEquals("10:30", created.end)
        assertFalse(created.del)

        vm.openEditSheet(created.id)
        vm.deleteEditing()
        repo.flush()

        val deleted = repo.currentStore().schedules.first { it.id == created.id }
        assertTrue(deleted.del)
        assertNotEquals(created.hlc, deleted.hlc)
    }

    @Test
    fun navigationPagesAndTagSections_updateUiStateCorrectly() {
        val dir = tempFolder.newFolder("vm-nav")
        val repo = DayNewRepository(dir, nowProvider = { fixedNow }, debounceMs = 10L)
        val vm = MainScreenViewModel(repo, nowProvider = { fixedNow })

        assertEquals(AppPage.OVERVIEW, vm.uiState.value.selectedPage)
        assertEquals(BottomTab.OVERVIEW, vm.uiState.value.selectedBottomTab)

        vm.selectTab(BottomTab.EISENHOWER)
        testDispatcher.scheduler.advanceUntilIdle()
        assertEquals(AppPage.EISENHOWER, vm.uiState.value.selectedPage)
        assertEquals(BottomTab.EISENHOWER, vm.uiState.value.selectedBottomTab)

        vm.selectPage(AppPage.TAGS)
        testDispatcher.scheduler.advanceUntilIdle()
        assertEquals(AppPage.TAGS, vm.uiState.value.selectedPage)
        assertEquals(null, vm.uiState.value.selectedBottomTab)

        vm.selectTagSection(TagPageSection.PERSONS)
        testDispatcher.scheduler.advanceUntilIdle()
        assertEquals(TagPageSection.PERSONS, vm.uiState.value.selectedTagSection)

        vm.openCreatePersonSheet()
        vm.updatePersonDraft { it.copy(name = "测试联系人") }
        vm.savePersonEditor()
        testDispatcher.scheduler.advanceUntilIdle()
        assertTrue(vm.uiState.value.personGroups.any { it.person.name == "测试联系人" })

        vm.selectTagSection(TagPageSection.LOCATIONS)
        vm.openCreateLocationSheet()
        vm.updateLocationDraft { it.copy(name = "测试地点") }
        vm.saveLocationEditor()
        testDispatcher.scheduler.advanceUntilIdle()
        assertTrue(vm.uiState.value.locationGroups.any { it.location.name == "测试地点" })
    }

    @Test
    fun eisenhowerBoard_hidesCompletedAndDefaultsToNextThreeDays() {
        val dir = tempFolder.newFolder("vm-eisenhower")
        val repo = DayNewRepository(dir, nowProvider = { fixedNow }, debounceMs = 10L)
        val today = fixedNow.toLocalDate()

        repo.upsertSchedule(
            com.daynew.app.data.ScheduleDraft(
                title = "今天未完成",
                hasDueDate = true,
                useExactDate = true,
                dueDate = today,
                urgent = true,
                important = true,
            ),
            now = fixedNow,
        )
        repo.upsertSchedule(
            com.daynew.app.data.ScheduleDraft(
                title = "后天未完成",
                hasDueDate = true,
                useExactDate = true,
                dueDate = today.plusDays(2),
                urgent = false,
                important = true,
            ),
            now = fixedNow,
        )
        repo.upsertSchedule(
            com.daynew.app.data.ScheduleDraft(
                title = "四天后未完成",
                hasDueDate = true,
                useExactDate = true,
                dueDate = today.plusDays(3),
                urgent = true,
                important = false,
            ),
            now = fixedNow,
        )
        val completed = repo.upsertSchedule(
            com.daynew.app.data.ScheduleDraft(
                title = "今天已完成",
                hasDueDate = true,
                useExactDate = true,
                dueDate = today,
                urgent = true,
                important = true,
            ),
            now = fixedNow,
        )
        repo.toggleDone(completed.id, fixedNow)

        val vm = MainScreenViewModel(repo, nowProvider = { fixedNow })
        testDispatcher.scheduler.advanceUntilIdle()

        val defaultTitles = vm.uiState.value.eisenhowerBuckets.flatMap { it.schedules }.map { it.dto.title }
        assertTrue(vm.uiState.value.eisenhowerNextThreeDaysOnly)
        assertTrue("今天未完成" in defaultTitles)
        assertTrue("后天未完成" in defaultTitles)
        assertFalse("四天后未完成" in defaultTitles)
        assertFalse("今天已完成" in defaultTitles)
        // 超过 3 天或逾期的默认也不出现
        assertTrue(
            vm.uiState.value.eisenhowerBuckets.flatMap { it.schedules }.all { card ->
                val d = com.daynew.sync.DeadlineRules.parseDue(card.dto.due).date
                !card.dto.done && d != null && !d.isBefore(today) && !d.isAfter(today.plusDays(2))
            }
        )

        vm.setEisenhowerNextThreeDaysOnly(false)
        testDispatcher.scheduler.advanceUntilIdle()
        val allPendingTitles = vm.uiState.value.eisenhowerBuckets.flatMap { it.schedules }.map { it.dto.title }
        assertTrue("四天后未完成" in allPendingTitles)
        assertFalse("今天已完成" in allPendingTitles)
    }
}
