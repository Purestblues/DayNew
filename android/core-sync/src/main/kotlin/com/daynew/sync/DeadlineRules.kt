package com.daynew.sync

import java.time.LocalDate
import java.time.LocalDateTime
import java.time.LocalTime
import java.time.format.DateTimeFormatter
import java.util.Locale

data class ParsedDue(
    val date: LocalDate?,
    val dueTime: LocalTime?,
)

/**
 * 截止日期与可选具体时间点（HH:mm）的统一解析、格式化与分钟级逾期判定规则。
 * 与 Windows 端 Core/DeadlineRules.cs 1:1 对齐。
 */
object DeadlineRules {
    private val DATE_FORMATTER: DateTimeFormatter = DateTimeFormatter.ISO_LOCAL_DATE

    fun formatClock(time: LocalTime): String =
        formatClockMinutes(time.hour * 60 + time.minute)

    fun formatClockMinutes(totalMinutes: Int): String {
        val clamped = totalMinutes.coerceIn(0, 23 * 60 + 59)
        return "%02d:%02d".format(Locale.ROOT, clamped / 60, clamped % 60)
    }

    /** 支持 "9"、"18"、"9:30"、"18:00" 与全角冒号 "18：30"，有效范围 00:00..23:59。 */
    fun tryParseClock(text: String?): LocalTime? {
        val trimmed = (text ?: "").trim().replace('：', ':')
        if (trimmed.isEmpty()) return null

        if (':' in trimmed) {
            val parts = trimmed.split(':')
            if (parts.size != 2) return null
            val hour = parts[0].toIntOrNull() ?: return null
            val minute = parts[1].toIntOrNull() ?: return null
            if (hour !in 0..23 || minute !in 0..59) return null
            return LocalTime.of(hour, minute)
        }

        val wholeHour = trimmed.toIntOrNull() ?: return null
        if (wholeHour !in 0..23) return null
        return LocalTime.of(wholeHour, 0)
    }

    fun formatDue(due: LocalDate?, dueTime: LocalTime?): String? {
        if (due == null) return null
        val dateText = due.format(DATE_FORMATTER)
        return if (dueTime != null) "$dateText ${formatClock(dueTime)}" else dateText
    }

    fun parseDue(raw: String?): ParsedDue {
        if (raw.isNullOrBlank()) return ParsedDue(null, null)

        val trimmed = raw.trim()
        val sepIdx = trimmed.indexOfFirst { it == ' ' || it == 'T' }
        if (sepIdx > 0) {
            val datePart = trimmed.substring(0, sepIdx).trim()
            val timePart = trimmed.substring(sepIdx + 1).trim()
            val dateWithTime = tryParseIsoDate(datePart)
            if (dateWithTime != null) {
                if (timePart.isEmpty()) return ParsedDue(dateWithTime, null)
                val parsedTime = tryParseClock(timePart)
                return if (parsedTime != null) {
                    ParsedDue(dateWithTime, parsedTime)
                } else {
                    ParsedDue(null, null)
                }
            }
        }

        val dateOnly = tryParseIsoDate(trimmed)
        if (dateOnly != null) return ParsedDue(dateOnly, null)

        return ParsedDue(null, null)
    }

    fun isOverdue(
        due: LocalDate?,
        dueTime: LocalTime?,
        now: LocalDateTime = LocalDateTime.now(),
    ): Boolean {
        if (due == null) return false
        val today = now.toLocalDate()
        if (due.isBefore(today)) return true
        if (due == today && dueTime != null) {
            return !now.toLocalTime().isBefore(dueTime)
        }
        return false
    }

    private fun tryParseIsoDate(text: String): LocalDate? =
        runCatching { LocalDate.parse(text, DATE_FORMATTER) }.getOrNull()
}
