package com.daynew.app.share

data class ParsedShareContent(
    val title: String,
    val note: String,
)

object ShareTextParser {
    private const val MAX_TITLE_LENGTH = 80

    fun parse(subject: String?, sharedText: String?): ParsedShareContent {
        val cleanSubject = subject?.trim().orEmpty()
        val rawLines = sharedText
            .orEmpty()
            .replace("\r\n", "\n")
            .lines()
            .map { it.trim() }
            .filter { it.isNotEmpty() }

        if (cleanSubject.isNotEmpty() && rawLines.isNotEmpty()) {
            val title = cleanSubject.take(MAX_TITLE_LENGTH)
            val note = rawLines.joinToString("\n")
            return ParsedShareContent(title = title, note = note)
        }

        if (rawLines.isNotEmpty()) {
            val firstLine = rawLines.first()
            val title = if (firstLine.length > MAX_TITLE_LENGTH) {
                firstLine.take(MAX_TITLE_LENGTH)
            } else {
                firstLine
            }
            val restLines = rawLines.drop(1)
            val note = if (firstLine.length > MAX_TITLE_LENGTH) {
                (listOf(firstLine) + restLines).joinToString("\n")
            } else {
                restLines.joinToString("\n")
            }
            return ParsedShareContent(title = title, note = note)
        }

        return ParsedShareContent(
            title = cleanSubject.take(MAX_TITLE_LENGTH),
            note = "",
        )
    }
}
