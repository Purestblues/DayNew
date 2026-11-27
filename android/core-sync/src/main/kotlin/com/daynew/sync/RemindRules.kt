package com.daynew.sync

import java.time.LocalTime
import java.util.Locale

data class RemindTrigger(
    val node: String,
    val targetTime: LocalTime,
    val toastTitle: String,
    val isEnd: Boolean,
)

/**
 * 相对提醒规则层（pre15 / pre5 / start / end），与 Windows 端 Core/RemindRules.cs 1:1 对齐。
 */
object RemindRules {
    const val PRE15 = "pre15"
    const val PRE5 = "pre5"
    const val START = "start"
    const val END = "end"
    const val NONE = "none"
    const val DEFAULT = "pre5,start"

    private val orderedNodes = listOf(PRE15, PRE5, START, END)

    fun normalize(raw: String?, legacyFallback: String = DEFAULT): String {
        if (raw.isNullOrBlank()) return ""

        val trimmed = raw.trim()
        if (trimmed.equals(NONE, ignoreCase = true)) return ""

        val active = parseValidSet(trimmed)
        if (active.isNotEmpty()) return joinOrdered(active)

        if (isLegacyClock(trimmed)) {
            return if (legacyFallback.isBlank()) "" else joinOrdered(parseValidSet(legacyFallback))
        }

        return ""
    }

    fun hasAny(remind: String?): Boolean = normalize(remind).isNotEmpty()

    fun hasNode(remind: String?, node: String): Boolean {
        val normalized = normalize(remind)
        if (normalized.isEmpty() || node.isBlank()) return false
        val target = node.trim().lowercase(Locale.ROOT)
        return normalized.split(',').any { it == target }
    }

    fun toggleNode(current: String?, node: String): String {
        if (node.isBlank()) return ""
        val target = node.trim().lowercase(Locale.ROOT)
        if (target == NONE) return ""

        val active = parseValidSet(normalize(current)).toMutableSet()
        if (target !in orderedNodes) return joinOrdered(active)

        if (!active.add(target)) {
            active.remove(target)
        }
        return joinOrdered(active)
    }

    fun normalizeForDeadlineDefault(current: String?): String {
        val normalized = normalize(current)
        if (normalized.isEmpty()) return ""

        val active = parseValidSet(normalized).toMutableSet()
        if (active.remove(START)) {
            active.add(END)
        }
        return joinOrdered(active)
    }

    fun formatLabel(remind: String?, isDeadlineOnly: Boolean = false): String {
        val normalized = normalize(remind)
        if (normalized.isEmpty()) return "不提醒"

        val parts = mutableListOf<String>()
        for (node in normalized.split(',')) {
            if (isDeadlineOnly && node == START) continue
            parts += when (node) {
                PRE15 -> "提前15分"
                PRE5 -> "提前5分"
                START -> "开始时"
                END -> if (isDeadlineOnly) "截止时" else "结束时"
                else -> node
            }
        }
        return if (parts.isEmpty()) "不提醒" else parts.joinToString(" · ")
    }

    fun formatTooltip(
        remind: String?,
        hasTime: Boolean = true,
        hasDueTime: Boolean = false,
    ): String {
        if (!hasTime && !hasDueTime) return "未设具体时间，提醒不触发"

        val isDeadlineOnly = !hasTime && hasDueTime
        val normalized = normalize(remind)
        if (normalized.isEmpty()) return "不提醒"

        val parts = mutableListOf<String>()
        for (node in normalized.split(',')) {
            if (isDeadlineOnly && node == START) continue
            parts += when (node) {
                PRE15 -> "提前 15 分钟"
                PRE5 -> "提前 5 分钟"
                START -> "开始时"
                END -> if (isDeadlineOnly) "截止时" else "结束时"
                else -> node
            }
        }
        return if (parts.isEmpty()) "不提醒" else "提醒：" + parts.joinToString(" · ")
    }

    fun getTriggers(
        remind: String?,
        startTime: LocalTime,
        endTime: LocalTime,
        taskTitle: String,
    ): List<RemindTrigger> {
        val normalized = normalize(remind)
        if (normalized.isEmpty()) return emptyList()

        val startMinutes = startTime.hour * 60 + startTime.minute
        val result = ArrayList<RemindTrigger>(4)
        for (node in normalized.split(',')) {
            when (node) {
                PRE15 -> if (startMinutes >= 15) {
                    result += RemindTrigger(PRE15, startTime.minusMinutes(15), "15 分钟后开始 · $taskTitle", false)
                }
                PRE5 -> if (startMinutes >= 5) {
                    result += RemindTrigger(PRE5, startTime.minusMinutes(5), "5 分钟后开始 · $taskTitle", false)
                }
                START -> result += RemindTrigger(START, startTime, "现在开始 · $taskTitle", false)
                END -> result += RemindTrigger(END, endTime, "日程已结束 · $taskTitle", true)
            }
        }
        return result
    }

    fun getDeadlineTriggers(
        remind: String?,
        dueTime: LocalTime,
        taskTitle: String,
    ): List<RemindTrigger> {
        val normalized = normalize(remind)
        if (normalized.isEmpty()) return emptyList()

        val dueMinutes = dueTime.hour * 60 + dueTime.minute
        val result = ArrayList<RemindTrigger>(3)
        for (node in normalized.split(',')) {
            when (node) {
                PRE15 -> if (dueMinutes >= 15) {
                    result += RemindTrigger(PRE15, dueTime.minusMinutes(15), "15 分钟后截止 · $taskTitle", false)
                }
                PRE5 -> if (dueMinutes >= 5) {
                    result += RemindTrigger(PRE5, dueTime.minusMinutes(5), "5 分钟后截止 · $taskTitle", false)
                }
                END -> result += RemindTrigger(END, dueTime, "已到截止时间 · $taskTitle", true)
            }
        }
        return result
    }

    private fun parseValidSet(raw: String): Set<String> {
        val set = LinkedHashSet<String>()
        for (token in raw.split(',')) {
            val lower = token.trim().lowercase(Locale.ROOT)
            if (lower.isNotEmpty() && lower in orderedNodes) {
                set.add(lower)
            }
        }
        return set
    }

    private fun joinOrdered(active: Set<String>): String {
        if (active.isEmpty()) return ""
        return orderedNodes.filter { it in active }.joinToString(",")
    }

    private fun isLegacyClock(text: String): Boolean {
        if (':' in text) return true
        return DeadlineRules.tryParseClock(text) != null
    }
}
