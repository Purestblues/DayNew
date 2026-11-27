package com.daynew.sync

data class CompletionMergeResult(
    val entries: List<CompletionEntry>,
    val addedCount: Int,
)

/**
 * completions.ndjson 编解码与去重合并，与 Windows 端 Services/CompletionLog.cs 1:1 对齐。
 */
object CompletionLogCodec {
    fun parseLines(ndjsonText: String?): List<CompletionEntry> {
        if (ndjsonText.isNullOrBlank()) return emptyList()
        val list = mutableListOf<CompletionEntry>()
        for (line in ndjsonText.lineSequence()) {
            if (line.isBlank()) continue
            runCatching {
                DayNewCompactJson.decodeFromString<CompletionEntry>(line)
            }.getOrNull()?.let { list += it }
        }
        return list
    }

    fun formatLines(entries: List<CompletionEntry>): String {
        if (entries.isEmpty()) return ""
        return entries.joinToString("\n") {
            DayNewCompactJson.encodeToString(CompletionEntry.serializer(), it)
        } + "\n"
    }

    fun mergeEntries(
        current: List<CompletionEntry>,
        incoming: List<CompletionEntry>,
    ): CompletionMergeResult {
        val seen = HashSet<EntryKey>(current.size + incoming.size)
        val combined = ArrayList<CompletionEntry>(current.size + incoming.size)

        for (item in current) {
            seen.add(entryKey(item))
            combined.add(item)
        }

        var added = 0
        for (item in incoming) {
            if (seen.add(entryKey(item))) {
                combined.add(item)
                added++
            }
        }

        if (added == 0) {
            return CompletionMergeResult(current.toList(), 0)
        }

        val ordered = combined.sortedBy { it.at }
        return CompletionMergeResult(ordered, added)
    }

    private data class EntryKey(
        val id: String,
        val forDate: String,
        val at: String,
        val done: Boolean,
    )

    private fun entryKey(entry: CompletionEntry): EntryKey =
        EntryKey(entry.id, entry.forDate ?: "", entry.at, entry.done)
}
