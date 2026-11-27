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
}
