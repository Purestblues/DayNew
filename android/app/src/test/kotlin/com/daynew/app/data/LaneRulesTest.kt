package com.daynew.app.data

import com.daynew.sync.ScheduleDto
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test
import java.time.LocalDate
import java.time.LocalDateTime

class LaneRulesTest {
    @Test
    fun endOfWeek_and_nextMonday_handleWeekdayAndSunday() {
        // 2026-11-27 是周五
        val friday = LocalDate.of(2026, 11, 27)
        assertEquals(LocalDate.of(2026, 11, 29), LaneRules.endOfWeek(friday))
        assertEquals(LocalDate.of(2026, 11, 30), LaneRules.nextMonday(friday))

        // 2026-11-29 是周日，与 Windows EndOfWeek() 对齐返回下周日 (+7天)
        val sunday = LocalDate.of(2026, 11, 29)
        assertEquals(LocalDate.of(2026, 12, 6), LaneRules.endOfWeek(sunday))
        assertEquals(LocalDate.of(2026, 11, 30), LaneRules.nextMonday(sunday))

        // 2026-11-30 是周一，nextMonday 返回下周一 (+7天)
        val monday = LocalDate.of(2026, 11, 30)
        assertEquals(LocalDate.of(2026, 12, 6), LaneRules.endOfWeek(monday))
        assertEquals(LocalDate.of(2026, 12, 7), LaneRules.nextMonday(monday))
    }

    @Test
    fun laneFor_computesFiveLanesAndHandlesDoneAndMinuteOverdue() {
        val now = LocalDateTime.of(2026, 11, 27, 15, 0) // 周五 15:00

        // 无日期或已删除不入五档
        assertNull(LaneRules.laneFor(ScheduleDto(id = "1", due = null), now))
        assertNull(LaneRules.laneFor(ScheduleDto(id = "2", due = "2026-11-27", del = true), now))

        // 过去未完成 -> 逾期；过去已完成 -> null（不混入今天）
        assertEquals(LaneKind.OVERDUE, LaneRules.laneFor(ScheduleDto(id = "3", due = "2026-11-26", done = false), now))
        assertNull(LaneRules.laneFor(ScheduleDto(id = "4", due = "2026-11-26", done = true), now))

        // 今日带截止时刻：14:30 已到点且未完成 -> 逾期；已完成 -> 今天；16:00 未到点 -> 今天
        assertEquals(LaneKind.OVERDUE, LaneRules.laneFor(ScheduleDto(id = "5", due = "2026-11-27 14:30", done = false), now))
        assertEquals(LaneKind.TODAY, LaneRules.laneFor(ScheduleDto(id = "6", due = "2026-11-27 14:30", done = true), now))
        assertEquals(LaneKind.TODAY, LaneRules.laneFor(ScheduleDto(id = "7", due = "2026-11-27 16:00", done = false), now))

        // 明天 / 本周日 / 下周一
        assertEquals(LaneKind.TOMORROW, LaneRules.laneFor(ScheduleDto(id = "8", due = "2026-11-28"), now))
        assertEquals(LaneKind.THIS_WEEK, LaneRules.laneFor(ScheduleDto(id = "9", due = "2026-11-29"), now))
        assertEquals(LaneKind.LATER, LaneRules.laneFor(ScheduleDto(id = "10", due = "2026-11-30"), now))
    }
}
