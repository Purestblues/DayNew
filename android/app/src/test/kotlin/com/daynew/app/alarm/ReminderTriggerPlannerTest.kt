package com.daynew.app.alarm

import com.daynew.sync.ScheduleDto
import com.daynew.sync.StoreDto
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test
import java.time.LocalDateTime

class ReminderTriggerPlannerTest {
    @Test
    fun `plans future timed and deadline reminders and filters past or completed items`() {
        val now = LocalDateTime.of(2026, 11, 27, 10, 0)
        val store = StoreDto(
            schedules = listOf(
                ScheduleDto(
                    id = "s1",
                    title = "评审会",
                    due = "2026-11-27",
                    start = "10:10",
                    end = "11:00",
                    remind = "pre15,pre5,start,end",
                    done = false,
                ),
                ScheduleDto(
                    id = "s2",
                    title = "提交日报",
                    due = "2026-11-27 18:00",
                    remind = "pre15,end",
                    done = false,
                ),
                ScheduleDto(
                    id = "s3",
                    title = "已完成任务",
                    due = "2026-11-27",
                    start = "14:00",
                    end = "15:00",
                    remind = "start",
                    done = true,
                ),
                ScheduleDto(
                    id = "s4",
                    title = "已删除任务",
                    due = "2026-11-27",
                    start = "15:00",
                    end = "16:00",
                    remind = "start",
                    del = true,
                ),
                ScheduleDto(
                    id = "s5",
                    title = "超过七天范围",
                    due = "2026-12-10",
                    start = "09:00",
                    end = "10:00",
                    remind = "start",
                ),
            ),
        )

        val planned = ReminderTriggerPlanner.planUpcoming(store, now)

        // s1 的 pre15 (09:55) 已过去，保留 pre5 (10:05)、start (10:10)、end (11:00)
        // s2 保留 pre15 (17:45) 和 end (18:00)
        assertEquals(5, planned.size)
        assertEquals(listOf("pre5", "start", "end", "pre15", "end"), planned.map { it.node })
        assertTrue(planned.all { it.triggerAt.isAfter(now) })
        assertEquals(listOf("s1", "s1", "s1", "s2", "s2"), planned.map { it.scheduleId })
    }
}
