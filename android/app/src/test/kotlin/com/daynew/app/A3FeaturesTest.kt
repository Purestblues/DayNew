package com.daynew.app

import com.daynew.app.data.DayNewRepository
import com.daynew.app.data.GoalDraft
import com.daynew.app.data.ScheduleDraft
import com.daynew.app.data.TagDraft
import com.daynew.app.data.TimelineBlockInput
import com.daynew.app.data.computeTimelineOverlapLayout
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Rule
import org.junit.Test
import org.junit.rules.TemporaryFolder
import java.time.LocalDate
import java.time.LocalDateTime

class A3FeaturesTest {
    @get:Rule
    val tempFolder = TemporaryFolder()

    private val fixedNow = LocalDateTime.of(2026, 11, 27, 10, 0)

    private fun newRepo(): DayNewRepository = DayNewRepository(
        dataDir = tempFolder.newFolder(),
        nowProvider = { fixedNow },
        debounceMs = 10L,
        scope = CoroutineScope(SupervisorJob() + Dispatchers.IO),
    )

    @Test
    fun `timeline overlap layout handles disjoint and chained blocks`() {
        val slots = computeTimelineOverlapLayout(
            listOf(
                TimelineBlockInput("a", 9 * 60, 10 * 60),
                TimelineBlockInput("b", 9 * 60 + 30, 11 * 60),
                TimelineBlockInput("c", 10 * 60 + 15, 11 * 60 + 30),
                TimelineBlockInput("d", 14 * 60, 15 * 60),
            )
        ).associateBy { it.id }

        assertEquals(2, slots.getValue("a").columnCount)
        assertEquals(2, slots.getValue("b").columnCount)
        assertEquals(2, slots.getValue("c").columnCount)
        assertEquals(0, slots.getValue("a").columnIndex)
        assertEquals(1, slots.getValue("b").columnIndex)
        assertEquals(0, slots.getValue("c").columnIndex)

        assertEquals(1, slots.getValue("d").columnCount)
        assertEquals(0, slots.getValue("d").columnIndex)
    }

    @Test
    fun `future undated schedule and moveScheduleToDate update due and fieldHlcs`() {
        val repo = newRepo()
        val created = repo.upsertSchedule(
            ScheduleDraft(
                title = "不定日期想法",
                hasDueDate = false,
                startText = "09:00",
                endText = "10:00",
            ),
            now = fixedNow,
        )
        assertNull(created.due)
        assertNull(created.start)
        assertNull(created.end)

        repo.moveScheduleToDate(created.id, LocalDate.of(2026, 11, 27), fixedNow)
        val movedToday = repo.getScheduleById(created.id)!!
        assertEquals("2026-11-27", movedToday.due)
        assertTrue(movedToday.fieldHlcs?.containsKey("due") == true)

        repo.moveScheduleToDate(created.id, null, fixedNow)
        val movedBack = repo.getScheduleById(created.id)!!
        assertNull(movedBack.due)
    }

    @Test
    fun `deleting goal keeps linked schedule and clears goal references`() {
        val repo = newRepo()
        val goal = repo.upsertGoal(
            GoalDraft(title = "发布新版", color = "#5C9A64", dueText = "2026-12-01")
        )
        assertEquals("2026-12-01", goal.due)

        val sched = repo.upsertSchedule(
            ScheduleDraft(title = "打包测试", goalId = goal.id),
            now = fixedNow,
        )
        assertEquals(goal.id, sched.goalId)

        assertNotNull(repo.toggleGoalCompleted(goal.id))
        assertTrue(repo.currentStore().goals.first { it.id == goal.id }.isCompleted)

        assertTrue(repo.deleteGoal(goal.id))
        assertTrue(repo.currentStore().goals.first { it.id == goal.id }.del)

        val afterSched = repo.getScheduleById(sched.id)
        assertNotNull(afterSched)
        assertNull(afterSched!!.goalId)
        assertTrue(afterSched.goalRefs.isEmpty())
        assertTrue(afterSched.fieldHlcs?.containsKey("goals") == true)
    }

    @Test
    fun `deleting tag removes tagId from schedules with tags fieldHlc`() {
        val repo = newRepo()
        val tag = repo.upsertTag(TagDraft(name = "客户端", color = "#88C0D0"))
        val sched = repo.upsertSchedule(
            ScheduleDraft(title = "联调", tagIds = listOf(tag.id)),
            now = fixedNow,
        )
        assertEquals(listOf(tag.id), sched.tagIds)

        assertTrue(repo.deleteTag(tag.id))
        assertTrue(repo.currentStore().tags.first { it.id == tag.id }.del)

        val afterSched = repo.getScheduleById(sched.id)!!
        assertFalse(tag.id in afterSched.tagIds)
        assertTrue(afterSched.fieldHlcs?.containsKey("tags") == true)
    }
}
