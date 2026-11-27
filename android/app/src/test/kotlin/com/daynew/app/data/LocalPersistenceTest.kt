package com.daynew.app.data

import com.daynew.sync.CompletionLogCodec
import com.daynew.sync.ScheduleDto
import com.daynew.sync.StoreDto
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Rule
import org.junit.Test
import org.junit.rules.TemporaryFolder
import java.io.File
import java.nio.charset.StandardCharsets
import java.time.LocalDate
import java.time.LocalDateTime

class LocalPersistenceTest {

    @get:Rule
    val tempFolder = TemporaryFolder()

    @Test
    fun writeAtomic_writesDataAndRotatesPreviousAndKeepsUtf8() {
        val dir = tempFolder.newFolder("daynew-atomic")
        val storeFile = LocalStoreFile(dir)

        val v1 = StoreDto(
            schedules = listOf(ScheduleDto(id = "s1", title = "第一次写入中文测试", due = "2026-11-27")),
            presets = emptyList(),
        )
        storeFile.writeAtomic(v1)
        assertTrue(storeFile.dataFile.exists())
        assertFalse(storeFile.prevFile.exists())

        val raw1 = storeFile.dataFile.readText(StandardCharsets.UTF_8)
        assertTrue(raw1.contains("第一次写入中文测试"))
        assertTrue(raw1.contains("\"fieldHlcs\""))

        val v2 = StoreDto(
            schedules = listOf(ScheduleDto(id = "s1", title = "第二次更新", due = "2026-11-28")),
            presets = emptyList(),
        )
        storeFile.writeAtomic(v2)
        assertTrue(storeFile.prevFile.exists())

        val loaded = storeFile.load() as StoreLoadResult.Loaded
        assertEquals("第二次更新", loaded.store.schedules.single().title)

        val prevRaw = storeFile.prevFile.readText(StandardCharsets.UTF_8)
        assertTrue(prevRaw.contains("第一次写入中文测试"))
    }

    @Test
    fun load_quarantinesCorruptedJson() {
        val dir = tempFolder.newFolder("daynew-broken")
        val storeFile = LocalStoreFile(dir)
        storeFile.dataFile.writeText("{ broken json !!!", StandardCharsets.UTF_8)

        val result = storeFile.load()
        assertTrue(result is StoreLoadResult.Quarantined)
        assertFalse(storeFile.dataFile.exists())

        val files = dir.listFiles()?.map { it.name }.orEmpty()
        assertTrue(files.any { it.startsWith("data.broken-") && it.endsWith(".json") })
        assertTrue(File(dir, "load-error.txt").exists())
    }

    @Test
    fun completionLog_appendsNdjsonAndParsesCleanly() {
        val dir = tempFolder.newFolder("daynew-log")
        val log = LocalCompletionLog(dir)

        log.append("sch_01", LocalDate.of(2026, 11, 27), done = true, at = LocalDateTime.of(2026, 11, 27, 15, 30, 0))
        log.append("sch_01", LocalDate.of(2026, 11, 27), done = false, at = LocalDateTime.of(2026, 11, 27, 15, 31, 0))

        val lines = log.logFile.readLines(StandardCharsets.UTF_8)
        assertEquals(2, lines.size)
        assertTrue(lines[0].contains("\"for\":\"2026-11-27\""))

        val parsed = CompletionLogCodec.parseLines(log.logFile.readText(StandardCharsets.UTF_8))
        assertEquals(2, parsed.size)
        assertTrue(parsed[0].done)
        assertFalse(parsed[1].done)
    }
}
