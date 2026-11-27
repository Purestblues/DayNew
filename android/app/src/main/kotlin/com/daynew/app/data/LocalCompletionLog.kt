package com.daynew.app.data

import com.daynew.sync.CompletionEntry
import com.daynew.sync.CompletionLogCodec
import java.io.File
import java.io.FileOutputStream
import java.nio.charset.StandardCharsets
import java.time.LocalDate
import java.time.LocalDateTime
import java.time.format.DateTimeFormatter

/**
 * completions.ndjson 只追加持久化（不写回主 data.json，避免历史膨胀）。
 */
class LocalCompletionLog(private val dataDir: File) {
    val logFile: File = File(dataDir, "completions.ndjson")
    private val ioLock = Any()

    fun append(
        scheduleId: String,
        forDate: LocalDate?,
        done: Boolean,
        at: LocalDateTime = LocalDateTime.now(),
    ): CompletionEntry {
        val entry = CompletionEntry(
            id = scheduleId,
            forDate = forDate?.format(DateTimeFormatter.ISO_LOCAL_DATE),
            at = at.format(AT_FORMATTER),
            done = done,
        )
        val line = CompletionLogCodec.formatLines(listOf(entry))
        synchronized(ioLock) {
            dataDir.mkdirs()
            FileOutputStream(logFile, true).use { out ->
                out.write(line.toByteArray(StandardCharsets.UTF_8))
                out.flush()
                runCatching { out.fd.sync() }
            }
        }
        return entry
    }

    fun readAll(): List<CompletionEntry> = synchronized(ioLock) {
        if (!logFile.exists()) return emptyList()
        val raw = runCatching { logFile.readText(StandardCharsets.UTF_8) }.getOrNull()
        CompletionLogCodec.parseLines(raw)
    }

    /**
     * 合并远端 completion 条目：按 (id, forDate, at, done) 去重并按时间排序写回，返回新增条数。
     */
    fun mergeEntries(incoming: List<CompletionEntry>): Int = synchronized(ioLock) {
        if (incoming.isEmpty()) return 0
        val current = if (logFile.exists()) {
            val raw = runCatching { logFile.readText(StandardCharsets.UTF_8) }.getOrNull()
            CompletionLogCodec.parseLines(raw)
        } else {
            emptyList()
        }
        val merged = CompletionLogCodec.mergeEntries(current, incoming)
        if (merged.addedCount <= 0) return 0

        dataDir.mkdirs()
        val tmp = File(dataDir, "completions.ndjson.tmp")
        val formatted = CompletionLogCodec.formatLines(merged.entries)
        FileOutputStream(tmp).use { out ->
            out.write(formatted.toByteArray(StandardCharsets.UTF_8))
            out.flush()
            runCatching { out.fd.sync() }
        }
        java.nio.file.Files.move(
            tmp.toPath(),
            logFile.toPath(),
            java.nio.file.StandardCopyOption.REPLACE_EXISTING,
        )
        merged.addedCount
    }

    companion object {
        private val AT_FORMATTER: DateTimeFormatter =
            DateTimeFormatter.ofPattern("yyyy-MM-dd HH:mm:ss")
    }
}
