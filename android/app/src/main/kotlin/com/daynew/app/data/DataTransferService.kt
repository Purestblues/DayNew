package com.daynew.app.data

import com.daynew.sync.CompletionEntry
import com.daynew.sync.CompletionLogCodec
import com.daynew.sync.DayNewJson
import com.daynew.sync.StoreDto
import java.io.ByteArrayInputStream
import java.io.ByteArrayOutputStream
import java.io.File
import java.io.FileOutputStream
import java.io.InputStream
import java.io.OutputStream
import java.nio.charset.StandardCharsets
import java.time.LocalDateTime
import java.time.format.DateTimeFormatter
import java.util.zip.ZipEntry
import java.util.zip.ZipInputStream
import java.util.zip.ZipOutputStream

data class ImportPackagePreview(
    val fileName: String,
    val dto: StoreDto,
    val completions: List<CompletionEntry>,
    val hasCompletionsEntry: Boolean,
) {
    val scheduleCount: Int get() = dto.schedules.count { !it.del }
    val goalCount: Int get() = dto.goals.count { !it.del }
    val tagCount: Int get() = dto.tags.count { !it.del }
    val completionCount: Int get() = completions.size
}

data class ImportApplyResult(
    val backupFile: File,
    val addedCompletions: Int,
    val mergedUpdatedCount: Int,
)

/**
 * 安卓端全部数据导出（.daynew.zip）与导入（覆盖 / 按 ID 合并），与桌面端 DataTransferService.cs 100% 互通。
 */
object DataTransferService {
    const val DATA_ENTRY_NAME = "data.json"
    const val COMPLETIONS_ENTRY_NAME = "completions.ndjson"

    fun exportToStream(repository: DayNewRepository, output: OutputStream) {
        repository.flush()
        writePackageToStream(
            dto = repository.currentStore(),
            completions = repository.readCompletions(),
            output = output,
        )
    }

    fun backupCurrentToZip(repository: DayNewRepository): File {
        repository.flush()
        val backupsDir = File(repository.storeFile.dataDir, "backups").apply { mkdirs() }
        val stamp = LocalDateTime.now().format(DateTimeFormatter.ofPattern("yyyyMMdd-HHmmss"))
        var candidate = File(backupsDir, "backup-$stamp.daynew.zip")
        var seq = 2
        while (candidate.exists()) {
            candidate = File(backupsDir, "backup-$stamp-$seq.daynew.zip")
            seq++
        }
        FileOutputStream(candidate).use { out ->
            writePackageToStream(
                dto = repository.currentStore(),
                completions = repository.readCompletions(),
                output = out,
            )
        }
        return candidate
    }

    fun inspectStream(fileName: String, input: InputStream): ImportPackagePreview {
        val rawBytes = input.readBytes()
        if (rawBytes.isEmpty()) {
            throw IllegalArgumentException("所选文件为空")
        }

        if (fileName.endsWith(".json", ignoreCase = true)) {
            val dto = parseStoreDto(rawBytes.toString(StandardCharsets.UTF_8))
            return ImportPackagePreview(
                fileName = fileName,
                dto = dto,
                completions = emptyList(),
                hasCompletionsEntry = false,
            )
        }

        var dataJson: String? = null
        var completionsRaw: String? = null

        ZipInputStream(ByteArrayInputStream(rawBytes), StandardCharsets.UTF_8).use { zip ->
            while (true) {
                val entry = zip.nextEntry ?: break
                if (!entry.isDirectory) {
                    val entryShortName = entry.name.substringAfterLast('/')
                    when {
                        entry.name.equals(DATA_ENTRY_NAME, ignoreCase = true) ||
                            entryShortName.equals(DATA_ENTRY_NAME, ignoreCase = true) -> {
                            dataJson = readEntryUtf8(zip)
                        }
                        entry.name.equals(COMPLETIONS_ENTRY_NAME, ignoreCase = true) ||
                            entryShortName.equals(COMPLETIONS_ENTRY_NAME, ignoreCase = true) -> {
                            completionsRaw = readEntryUtf8(zip)
                        }
                    }
                }
                zip.closeEntry()
            }
        }

        val resolvedJson = dataJson
            ?: throw IllegalArgumentException("备份包内缺少 $DATA_ENTRY_NAME")
        val dto = parseStoreDto(resolvedJson)
        val completions = completionsRaw?.let { CompletionLogCodec.parseLines(it) }.orEmpty()

        return ImportPackagePreview(
            fileName = fileName,
            dto = dto,
            completions = completions,
            hasCompletionsEntry = completionsRaw != null,
        )
    }

    fun applyImport(
        preview: ImportPackagePreview,
        merge: Boolean,
        repository: DayNewRepository,
    ): ImportApplyResult {
        val backupFile = backupCurrentToZip(repository)
        return if (merge) {
            val report = repository.mergeFromImport(preview.dto, preview.completions)
            ImportApplyResult(
                backupFile = backupFile,
                addedCompletions = preview.completions.size,
                mergedUpdatedCount = report.updatedCount,
            )
        } else {
            repository.replaceAllFromImport(preview.dto, preview.completions)
            ImportApplyResult(
                backupFile = backupFile,
                addedCompletions = preview.completions.size,
                mergedUpdatedCount = preview.scheduleCount,
            )
        }
    }

    private fun writePackageToStream(
        dto: StoreDto,
        completions: List<CompletionEntry>,
        output: OutputStream,
    ) {
        val dataJson = DayNewJson.encodeToString(StoreDto.serializer(), dto)
        val completionsText = CompletionLogCodec.formatLines(completions)
        ZipOutputStream(output, StandardCharsets.UTF_8).use { zip ->
            zip.putNextEntry(ZipEntry(DATA_ENTRY_NAME))
            zip.write(dataJson.toByteArray(StandardCharsets.UTF_8))
            zip.closeEntry()

            zip.putNextEntry(ZipEntry(COMPLETIONS_ENTRY_NAME))
            zip.write(completionsText.toByteArray(StandardCharsets.UTF_8))
            zip.closeEntry()
        }
    }

    private fun readEntryUtf8(zip: ZipInputStream): String {
        val buffer = ByteArrayOutputStream()
        val chunk = ByteArray(8192)
        while (true) {
            val read = zip.read(chunk)
            if (read <= 0) break
            buffer.write(chunk, 0, read)
        }
        return buffer.toByteArray().toString(StandardCharsets.UTF_8).removePrefix("\uFEFF")
    }

    private fun parseStoreDto(json: String): StoreDto {
        val clean = json.removePrefix("\uFEFF")
        return DayNewJson.decodeFromString(StoreDto.serializer(), clean)
    }
}
