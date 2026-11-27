package com.daynew.sync

import java.time.LocalDate

/**
 * 基础重复规则计算（每天 / 每周 / 每月），与 Windows 端 Core/RepeatRules.cs 1:1 对齐。
 */
object RepeatRules {
    const val DAILY = "每天"
    const val WEEKLY = "每周"
    const val MONTHLY = "每月"

    fun normalize(value: String?): String = when ((value ?: "").trim()) {
        DAILY, "daily", "day" -> DAILY
        WEEKLY, "weekly", "week" -> WEEKLY
        MONTHLY, "monthly", "month" -> MONTHLY
        else -> ""
    }

    fun hasRepeat(value: String?): Boolean = normalize(value).isNotEmpty()

    fun nextOccurrence(
        currentDue: LocalDate?,
        repeat: String?,
        todayOverride: LocalDate = LocalDate.now(),
    ): LocalDate? {
        val rule = normalize(repeat)
        if (rule.isEmpty()) return null

        val baseDate = currentDue ?: todayOverride
        var next = stepOnce(baseDate, rule)
        if (baseDate.isBefore(todayOverride)) {
            while (!next.isAfter(todayOverride)) {
                next = stepOnce(next, rule)
            }
        }
        return next
    }

    private fun stepOnce(date: LocalDate, rule: String): LocalDate = when (rule) {
        DAILY -> date.plusDays(1)
        WEEKLY -> date.plusWeeks(1)
        MONTHLY -> date.plusMonths(1)
        else -> date
    }
}
