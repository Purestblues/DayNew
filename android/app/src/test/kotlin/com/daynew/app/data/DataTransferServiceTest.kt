package com.daynew.app.data

import com.daynew.sync.CompletionEntry
import com.daynew.sync.StoreDto
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertTrue
import org.junit.Rule
import org.junit.Test
import org.junit.rules.TemporaryFolder
import java.io.ByteArrayInputStream
import java.io.ByteArrayOutputStream
import java.io.File
import java.time.LocalDate
import java.time.LocalDateTime

class DataTransferServiceTest {
    @get:Rule
    val tempFolder = TemporaryFolder()

    private val fixedNow = LocalDateTime.of(2026, 11, 28, 14, 0)

    private fun newRepo(name: String): DayNewRepository = DayNewRepository(
        dataDir = tempFolder.newFolder(name),
        nowProvider = { fixedNow },
        debounceMs = 10L,
        scope = CoroutineScope(SupervisorJob() + Dispatchers.IO),
    )

    @Test
    fun `exportToStream and inspectStream roundtrip preserves schedules and completions`() {
        val repo = newRepo("export-src")
        val created = repo.upsertSchedule(
            ScheduleDraft(title = "导出校验条目", hasDueDate = true, useExactDate = true, dueDate = LocalDate.of(2026, 11, 28)),
            now = fixedNow,
        )
        repo.toggleDone(created.id, fixedNow)

        val zipBytes = ByteArrayOutputStream().use { out ->
            DataTransferService.exportToStream(repo, out)
            out.toByteArray()
        }
        assertTrue(zipBytes.isNotEmpty())

        val preview = DataTransferService.inspectStream("backup.daynew.zip", ByteArrayInputStream(zipBytes))
        assertTrue(preview.hasCompletionsEntry)
        assertTrue(preview.dto.schedules.any { it.id == created.id && it.title == "导出校验条目" })
        assertTrue(preview.completions.any { it.id == created.id })
    }

    @Test
    fun `applyImport creates auto backup in backups folder and supports merge and replace`() {
        val srcRepo = newRepo("import-src")
        val srcItem = srcRepo.upsertSchedule(
            ScheduleDraft(title = "来源包独特日程", hasDueDate = false),
            now = fixedNow,
        )
        val zipBytes = ByteArrayOutputStream().use { out ->
            DataTransferService.exportToStream(srcRepo, out)
            out.toByteArray()
        }
        val preview = DataTransferService.inspectStream("from-win.daynew.zip", ByteArrayInputStream(zipBytes))

        val destRepo = newRepo("import-dest")
        val destOnly = destRepo.upsertSchedule(
            ScheduleDraft(title = "目标本地保留项", hasDueDate = false),
            now = fixedNow,
        )

        // 1. 按 ID 合并保留本地项并合入来源项，且生成自动备份
        val mergeRes = DataTransferService.applyImport(preview, merge = true, repository = destRepo)
        assertTrue(mergeRes.backupFile.exists())
        assertEquals("backups", mergeRes.backupFile.parentFile?.name)
        assertNotNull(destRepo.getScheduleById(srcItem.id))
        assertNotNull(destRepo.getScheduleById(destOnly.id))

        // 2. 覆盖导入
        val replaceRes = DataTransferService.applyImport(preview, merge = false, repository = destRepo)
        assertTrue(replaceRes.backupFile.exists())
        val backupsCount = File(destRepo.storeFile.dataDir, "backups").listFiles()?.size ?: 0
        assertTrue(backupsCount >= 2)
        assertNotNull(destRepo.getScheduleById(srcItem.id))
        assertEquals(null, destRepo.getScheduleById(destOnly.id))
    }
}
