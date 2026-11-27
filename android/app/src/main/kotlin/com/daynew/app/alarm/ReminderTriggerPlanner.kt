package com.daynew.app.alarm

import com.daynew.sync.DeadlineRules
import com.daynew.sync.RemindRules
import com.daynew.sync.StoreDto
import java.time.LocalDateTime

data class PlannedReminder(
    val requestCode: Int,
    val scheduleId: String,
    val node: String,
    val title: String,
    val content: String,
    val triggerAt: LocalDateTime,
)

/**
 * 纯 Kotlin 提醒规划器：按 RemindRules 计算未来 7 天内待办的提醒时刻并生成确定性 requestCode。
 */
object ReminderTriggerPlanner {
    private const val HORIZON_DAYS = 7L

    fun planUpcoming(
        store: StoreDto,
        now: LocalDateTime = LocalDateTime.now(),
    ): List<PlannedReminder> {
        val today = now.toLocalDate()
        val endDate = today.plusDays(HORIZON_DAYS)
        val result = mutableListOf<PlannedReminder>()

        for (item in store.schedules) {
            if (item.del || item.done) continue
            val remind = RemindRules.normalize(item.remind, legacyFallback = "")
            if (remind.isEmpty()) continue

            val (dueDate, dueTime) = DeadlineRules.parseDue(item.due)
            if (dueDate == null || dueDate.isBefore(today) || dueDate.isAfter(endDate)) continue

            val parsedStart = DeadlineRules.tryParseClock(item.start)
            val parsedEnd = DeadlineRules.tryParseClock(item.end)
            val taskTitle = item.title.ifBlank { "未命名日程" }

            val triggers = when {
                parsedStart != null && parsedEnd != null ->
                    RemindRules.getTriggers(remind, parsedStart, parsedEnd, taskTitle)
                dueTime != null ->
                    RemindRules.getDeadlineTriggers(remind, dueTime, taskTitle)
                else -> emptyList()
            }

            for (trigger in triggers) {
                val triggerAt = LocalDateTime.of(dueDate, trigger.targetTime)
                if (!triggerAt.isAfter(now)) continue

                val key = "${item.id}|$dueDate|${trigger.node}"
                val requestCode = stableRequestCode(key)
                result += PlannedReminder(
                    requestCode = requestCode,
                    scheduleId = item.id,
                    node = trigger.node,
                    title = trigger.toastTitle,
                    content = item.note.trim().ifEmpty { "点击查看或快速处理该日程" },
                    triggerAt = triggerAt,
                )
            }
        }

        return result.sortedBy { it.triggerAt }
    }

    fun stableRequestCode(key: String): Int = key.hashCode() and 0x7FFFFFFF
}
