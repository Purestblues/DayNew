package com.daynew.app.data

import com.daynew.sync.DeadlineRules
import com.daynew.sync.ScheduleDto
import java.time.LocalDate
import java.time.LocalDateTime

/**
 * 五个竖屏核心档位：逾期 / 今天 / 明天 / 本周 / 之后。
 * 档位不是存储字段，而是由 due 与当前时间实时推导。
 */
enum class LaneKind(val title: String) {
    OVERDUE("逾期"),
    TODAY("今天"),
    TOMORROW("明天"),
    THIS_WEEK("本周"),
    LATER("之后"),
}

/**
 * 档位计算与反推规则，与 Windows 端 ViewModels/MainViewModel.cs 1:1 对齐。
 */
object LaneRules {
    /** 本周日（周日当天返回下周日，保持「本周」到周末的概念）。 */
    fun endOfWeek(today: LocalDate = LocalDate.now()): LocalDate {
        val dayOfWeekSun0 = today.dayOfWeek.value % 7
        val offset = (7 - dayOfWeekSun0) % 7
        return today.plusDays(if (offset == 0) 7L else offset.toLong())
    }

    /** 下周一（周一当天返回下周一）。 */
    fun nextMonday(today: LocalDate = LocalDate.now()): LocalDate {
        val dayOfWeekSun0 = today.dayOfWeek.value % 7
        val offset = (1 - dayOfWeekSun0 + 7) % 7
        return today.plusDays(if (offset == 0) 7L else offset.toLong())
    }

    fun laneFor(
        schedule: ScheduleDto,
        now: LocalDateTime = LocalDateTime.now(),
    ): LaneKind? {
        if (schedule.del) return null
        val parsed = DeadlineRules.parseDue(schedule.due)
        val dueDate = parsed.date ?: return null
        val today = now.toLocalDate()

        if (dueDate.isBefore(today)) {
            return if (schedule.done) null else LaneKind.OVERDUE
        }
        if (dueDate == today) {
            if (!schedule.done && DeadlineRules.isOverdue(dueDate, parsed.dueTime, now)) {
                return LaneKind.OVERDUE
            }
            return LaneKind.TODAY
        }
        if (dueDate == today.plusDays(1)) return LaneKind.TOMORROW
        if (!dueDate.isAfter(endOfWeek(today))) return LaneKind.THIS_WEEK
        return LaneKind.LATER
    }

    fun dueForLane(
        kind: LaneKind,
        today: LocalDate = LocalDate.now(),
    ): LocalDate = when (kind) {
        LaneKind.OVERDUE -> today.minusDays(1)
        LaneKind.TODAY -> today
        LaneKind.TOMORROW -> today.plusDays(1)
        LaneKind.THIS_WEEK -> endOfWeek(today)
        LaneKind.LATER -> nextMonday(today)
    }
}
