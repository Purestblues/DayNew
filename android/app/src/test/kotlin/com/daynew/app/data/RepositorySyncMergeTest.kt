package com.daynew.app.data

import com.daynew.sync.CompletionEntry
import com.daynew.sync.ScheduleDto
import com.daynew.sync.StoreDto
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Rule
import org.junit.Test
import org.junit.rules.TemporaryFolder
import java.time.LocalDateTime

class RepositorySyncMergeTest {
    @get:Rule
    val tempFolder = TemporaryFolder()

    @Test
    fun `merges remote field updates and deduplicates completions log`() {
        val dataDir = tempFolder.newFolder("daynew-sync-test")
        val now = LocalDateTime.of(2026, 11, 27, 9, 0)
        val callbackEvents = mutableListOf<Boolean>()
        val repo = DayNewRepository(
            dataDir = dataDir,
            nowProvider = { now },
            debounceMs = 50L,
            scope = CoroutineScope(SupervisorJob() + Dispatchers.IO),
            onStoreChanged = { _, fromLocal -> callbackEvents += fromLocal },
        )

        val localCreated = repo.upsertSchedule(
            ScheduleDraft(
                title = "本地草稿",
                note = "本地备注",
                lane = LaneKind.TODAY,
                dueDate = now.toLocalDate(),
            ),
            now = now,
        )
        repo.toggleDone(localCreated.id, now)
        val existingCompletion = repo.readCompletions().first()

        // 构造远端设备同 ID 但修改了 title 且 HLC 更新的条目，以及另一条远端新建日程
        val remoteNewerTitleHlc = "9999999999990001win-peer1"
        val remoteStore = StoreDto(
            schedules = listOf(
                localCreated.copy(
                    title = "Windows 端改过标题",
                    hlc = remoteNewerTitleHlc,
                    fieldHlcs = localCreated.fieldHlcs.orEmpty() + ("title" to remoteNewerTitleHlc),
                ),
                ScheduleDto(
                    id = "remote_99",
                    title = "远端同步日程",
                    due = "2026-11-28",
                    hlc = remoteNewerTitleHlc,
                ),
            ),
        )

        val remoteCompletions = listOf(
            existingCompletion, // 重复记录应被去重忽略
            CompletionEntry(id = "remote_99", forDate = "2026-11-28", at = "2026-11-27 09:05:00", done = true),
        )

        val report = repo.applyRemoteSnapshot(
            remoteStore = remoteStore,
            remoteCompletions = remoteCompletions,
            lastSyncedRemoteHlc = null,
        )

        assertTrue(report.updatedCount >= 2)
        assertEquals(0, report.conflictCopiesCount)

        val mergedLocal = repo.getScheduleById(localCreated.id)!!
        assertEquals("Windows 端改过标题", mergedLocal.title)
        assertEquals("本地备注", mergedLocal.note)
        assertEquals("远端同步日程", repo.getScheduleById("remote_99")?.title)

        // 原 1 条 + 新增 1 条 = 2 条（重复条目未写入）
        assertEquals(2, repo.readCompletions().size)
        // onStoreChanged 最后一次应为远端合并 (fromLocalMutation = false)
        assertEquals(false, callbackEvents.last())
    }
}
