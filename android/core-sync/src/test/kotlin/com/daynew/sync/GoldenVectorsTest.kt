package com.daynew.sync

import kotlinx.serialization.Serializable
import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertFalse
import org.junit.jupiter.api.Assertions.assertNull
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.Test
import java.io.File
import java.time.LocalDate
import java.time.LocalDateTime
import java.time.LocalTime
import kotlin.math.sign

class GoldenVectorsTest {
    @Serializable
    data class GoldenVectors(
        val hlcCompareCases: List<HlcCompareCase>,
        val mergeCases: List<MergeCase>,
        val remindNormalizeCases: List<RemindNormalizeCase>,
        val remindDeadlineDefaultCases: List<RemindDeadlineDefaultCase>,
        val remindTriggerCases: List<RemindTriggerCase>,
        val deadlineTriggerCases: List<DeadlineTriggerCase>,
        val deadlineParseCases: List<DeadlineParseCase>,
        val deadlineOverdueCases: List<DeadlineOverdueCase>,
        val repeatCases: List<RepeatCase>,
        val completionMergeCases: List<CompletionMergeCase>,
    )

    @Serializable
    data class HlcCompareCase(val name: String, val a: String, val b: String, val expectedSign: Int)

    @Serializable
    data class MergeCase(
        val name: String,
        val lastSyncedRemoteHlc: String,
        val conflictId: String,
        val conflictHlc: String,
        val local: StoreDto,
        val remote: StoreDto,
        val expected: MergeExpected,
    )

    @Serializable
    data class MergeExpected(
        val updatedCount: Int,
        val conflictCopiesCount: Int,
        val maxRemoteHlc: String,
        val presetsIsNull: Boolean,
        val goalTitle: String? = null,
        val scheduleCount: Int,
        val scheduleTitle: String,
        val scheduleRemind: String? = null,
        val scheduleTagIds: List<String>? = null,
        val schedulePersonIds: List<String>? = null,
        val scheduleLocationIds: List<String>? = null,
        val conflictCopyTitle: String? = null,
        val conflictCopyStart: String? = null,
        val scheduleDel: Boolean,
    )

    @Serializable
    data class RemindNormalizeCase(val raw: String, val legacyFallback: String, val expected: String)

    @Serializable
    data class RemindDeadlineDefaultCase(val raw: String, val expected: String)

    @Serializable
    data class ExpectedTrigger(val node: String, val targetTime: String, val toastTitle: String, val isEnd: Boolean)

    @Serializable
    data class RemindTriggerCase(
        val remind: String,
        val start: String,
        val end: String,
        val title: String,
        val expected: List<ExpectedTrigger>,
    )

    @Serializable
    data class DeadlineTriggerCase(
        val remind: String,
        val dueTime: String,
        val title: String,
        val expected: List<ExpectedTrigger>,
    )

    @Serializable
    data class DeadlineParseCase(val raw: String?, val expectedDate: String?, val expectedTime: String?)

    @Serializable
    data class DeadlineOverdueCase(val due: String, val now: String, val expectedOverdue: Boolean)

    @Serializable
    data class RepeatCase(val currentDue: String?, val repeat: String, val today: String, val expectedNext: String?)

    @Serializable
    data class CompletionMergeCase(
        val name: String,
        val current: List<CompletionEntry>,
        val incoming: List<CompletionEntry>,
        val expectedAdded: Int,
        val expectedEntries: List<CompletionEntry>,
    )

    private fun loadVectors(): GoldenVectors {
        val candidates = listOf(
            File("../tests/golden-sync-vectors.json"),
            File("tests/golden-sync-vectors.json"),
            File("../../tests/golden-sync-vectors.json"),
        )
        val file = candidates.firstOrNull { it.exists() }
            ?: error("Could not locate tests/golden-sync-vectors.json from ${File(".").absolutePath}")
        return DayNewJson.decodeFromString(file.readText())
    }

    @Test
    fun `hlc compare vectors match`() {
        val vectors = loadVectors()
        for (c in vectors.hlcCompareCases) {
            assertEquals(c.expectedSign, HlcClock.compare(c.a, c.b).sign, c.name)
        }
    }

    @Test
    fun `merge vectors match across all six entities and conflict scenarios`() {
        val vectors = loadVectors()
        for (c in vectors.mergeCases) {
            val report = SyncMergeEngine.mergeStore(
                local = c.local,
                remote = c.remote,
                lastSyncedRemoteHlc = c.lastSyncedRemoteHlc,
                conflictIdGenerator = { c.conflictId },
                conflictHlcGenerator = { c.conflictHlc },
            )
            val exp = c.expected
            assertEquals(exp.updatedCount, report.updatedCount, "${c.name}: updatedCount")
            assertEquals(exp.conflictCopiesCount, report.conflictCopiesCount, "${c.name}: conflictCopiesCount")
            assertEquals(exp.maxRemoteHlc, report.maxRemoteHlc, "${c.name}: maxRemoteHlc")

            if (exp.presetsIsNull) {
                assertNull(report.mergedDto.presets, "${c.name}: presets should stay null")
            } else {
                assertTrue(report.mergedDto.presets != null, "${c.name}: presets should be non-null")
            }

            if (exp.goalTitle != null) {
                assertEquals(exp.goalTitle, report.mergedDto.goals.first().title, "${c.name}: goalTitle")
            }

            assertEquals(exp.scheduleCount, report.mergedDto.schedules.size, "${c.name}: scheduleCount")
            val primary = report.mergedDto.schedules.first()
            assertEquals(exp.scheduleTitle, primary.title, "${c.name}: scheduleTitle")
            assertEquals(exp.scheduleDel, primary.del, "${c.name}: scheduleDel")
            exp.scheduleRemind?.let { assertEquals(it, primary.remind, "${c.name}: remind") }
            exp.scheduleTagIds?.let { assertEquals(it, primary.tagIds, "${c.name}: tagIds") }
            exp.schedulePersonIds?.let { assertEquals(it, primary.personIds, "${c.name}: personIds") }
            exp.scheduleLocationIds?.let { assertEquals(it, primary.locationIds, "${c.name}: locationIds") }

            if (exp.conflictCopyTitle != null) {
                val copy = report.mergedDto.schedules[1]
                assertEquals(c.conflictId, copy.id, "${c.name}: conflictCopyId")
                assertEquals(exp.conflictCopyTitle, copy.title, "${c.name}: conflictCopyTitle")
                assertEquals(exp.conflictCopyStart, copy.start, "${c.name}: conflictCopyStart")
                assertFalse(copy.del, "${c.name}: conflict copy must not be deleted")
            }

            val roundTripped = WebDavClient.deserializeSnapshotGzip(
                WebDavClient.serializeSnapshotGzip(
                    DeviceSnapshotDto(
                        version = 1,
                        deviceId = "android-test",
                        snapshotHlc = c.conflictHlc,
                        store = report.mergedDto,
                        completions = emptyList(),
                    )
                )
            )
            assertEquals(report.mergedDto, roundTripped.store, "${c.name}: gzip snapshot roundtrip")
        }
    }

    @Test
    fun `remind, deadline, repeat, and completion vectors match`() {
        val vectors = loadVectors()
        for (c in vectors.remindNormalizeCases) {
            assertEquals(c.expected, RemindRules.normalize(c.raw, c.legacyFallback), c.raw)
        }
        for (c in vectors.remindDeadlineDefaultCases) {
            assertEquals(c.expected, RemindRules.normalizeForDeadlineDefault(c.raw), c.raw)
        }
        for (c in vectors.remindTriggerCases) {
            val triggers = RemindRules.getTriggers(
                c.remind,
                LocalTime.parse(c.start),
                LocalTime.parse(c.end),
                c.title,
            )
            assertEquals(c.expected.size, triggers.size, c.title)
            c.expected.zip(triggers).forEach { (exp, act) ->
                assertEquals(exp.node, act.node)
                assertEquals(exp.targetTime, DeadlineRules.formatClock(act.targetTime))
                assertEquals(exp.toastTitle, act.toastTitle)
                assertEquals(exp.isEnd, act.isEnd)
            }
        }
        for (c in vectors.deadlineTriggerCases) {
            val triggers = RemindRules.getDeadlineTriggers(c.remind, LocalTime.parse(c.dueTime), c.title)
            assertEquals(c.expected.size, triggers.size, c.title)
            c.expected.zip(triggers).forEach { (exp, act) ->
                assertEquals(exp.node, act.node)
                assertEquals(exp.targetTime, DeadlineRules.formatClock(act.targetTime))
                assertEquals(exp.toastTitle, act.toastTitle)
                assertEquals(exp.isEnd, act.isEnd)
            }
        }
        for (c in vectors.deadlineParseCases) {
            val parsed = DeadlineRules.parseDue(c.raw)
            assertEquals(c.expectedDate, parsed.date?.toString(), "parseDue date: ${c.raw}")
            assertEquals(c.expectedTime, parsed.dueTime?.let(DeadlineRules::formatClock), "parseDue time: ${c.raw}")
        }
        for (c in vectors.deadlineOverdueCases) {
            val parsedDue = DeadlineRules.parseDue(c.due)
            val parsedNow = LocalDateTime.parse(c.now.replace(' ', 'T'))
            assertEquals(c.expectedOverdue, DeadlineRules.isOverdue(parsedDue.date, parsedDue.dueTime, parsedNow), c.due)
        }
        for (c in vectors.repeatCases) {
            val cur = c.currentDue?.let(LocalDate::parse)
            val today = LocalDate.parse(c.today)
            assertEquals(c.expectedNext, RepeatRules.nextOccurrence(cur, c.repeat, today)?.toString(), c.repeat)
        }
        for (c in vectors.completionMergeCases) {
            val merged = CompletionLogCodec.mergeEntries(c.current, c.incoming)
            assertEquals(c.expectedAdded, merged.addedCount, c.name)
            assertEquals(c.expectedEntries, merged.entries, c.name)
            val ndjson = CompletionLogCodec.formatLines(merged.entries)
            assertEquals(c.expectedEntries, CompletionLogCodec.parseLines(ndjson), "${c.name}: ndjson roundtrip")
        }
    }
}
