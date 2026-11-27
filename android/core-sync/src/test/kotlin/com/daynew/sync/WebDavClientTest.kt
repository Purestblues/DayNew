package com.daynew.sync

import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertThrows
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.Test
import java.io.IOException

class WebDavClientTest {
    @Test
    fun `hlc handles clock rollback and 16-bit counter overflow`() {
        var fakeTime = 1_700_000_000_000L
        val clock = HlcClock("android-test") { fakeTime }

        val t1 = clock.tick()
        fakeTime -= 5_000L
        val t2 = clock.tick()
        assertTrue(HlcClock.compare(t2, t1) > 0, "Counter must advance during clock rollback")

        // Force counter overflow carry into phys++
        val highCounterRemote = HlcClock.format(1_700_000_000_000L, 0xFFFF, "win-peer")
        val t3 = clock.receive(highCounterRemote)
        val parsed = HlcClock.tryParse(t3)!!
        assertEquals(1_700_000_000_001L, parsed.phys)
        assertEquals(0, parsed.counter)
    }

    @Test
    fun `presets null vs empty list preserves json distinction`() {
        val withNull = StoreDto(presets = null)
        val withEmpty = StoreDto(presets = emptyList())

        val jsonNull = DayNewJson.encodeToString(StoreDto.serializer(), withNull)
        val jsonEmpty = DayNewJson.encodeToString(StoreDto.serializer(), withEmpty)

        assertTrue(jsonNull.contains("\"presets\": null"))
        assertTrue(jsonEmpty.contains("\"presets\": []"))
        assertEquals(null, DayNewJson.decodeFromString<StoreDto>(jsonNull).presets)
        assertEquals(emptyList<PresetDto>(), DayNewJson.decodeFromString<StoreDto>(jsonEmpty).presets)
    }

    @Test
    fun `propfind xml parser handles nutstore namespaces and url decoding`() {
        val xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <D:multistatus xmlns:D="DAV:">
              <D:response>
                <D:href>/dav/DayNew/</D:href>
                <D:propstat><D:prop><D:resourcetype><D:collection/></D:resourcetype></D:prop></D:propstat>
              </D:response>
              <D:response>
                <D:href>/dav/DayNew/device-win-a1b2c3.json.gz</D:href>
                <D:propstat><D:prop><D:getetag>"etag-win-1"</D:getetag></D:prop></D:propstat>
              </D:response>
              <D:response>
                <D:href>https://dav.jianguoyun.com/dav/DayNew/device-android%209f.json.gz</D:href>
                <D:propstat><D:prop><D:getetag>"etag-and-2"</D:getetag></D:prop></D:propstat>
              </D:response>
            </D:multistatus>
        """.trimIndent()

        val entries = WebDavClient.parsePropfindDirectoryEntries(xml)
        assertEquals(3, entries.size)
        assertEquals(PropfindEntry("device-win-a1b2c3.json.gz", "\"etag-win-1\""), entries[1])
        assertEquals(PropfindEntry("device-android 9f.json.gz", "\"etag-and-2\""), entries[2])
    }

    @Test
    fun `normalizeFolderUrl rejects invalid schemes, nutstore root, and appends trailing slash`() {
        assertEquals("https://dav.jianguoyun.com/dav/DayNew/", WebDavClient.normalizeFolderUrl("https://dav.jianguoyun.com/dav/DayNew"))
        assertEquals("https://dav.jianguoyun.com/dav/DayNewSync/", WebDavClient.normalizeFolderUrl("https://dav.jianguoyun.com/dav/DayNewSync"))
        assertThrows(IllegalArgumentException::class.java) {
            WebDavClient.normalizeFolderUrl("ftp://example.com/dav")
        }
        assertThrows(IllegalArgumentException::class.java) {
            WebDavClient.normalizeFolderUrl("https://dav.jianguoyun.com/dav")
        }
        assertThrows(IllegalArgumentException::class.java) {
            WebDavClient.normalizeFolderUrl("https://dav.jianguoyun.com/dav/")
        }
    }

    @Test
    fun `propfind xml parser throws IOException on malformed xml`() {
        assertThrows(IOException::class.java) {
            WebDavClient.parsePropfindDirectoryEntries("<D:multistatus><broken")
        }
    }

    @Test
    fun `json decoder coerces null non-nullable string fields to defaults`() {
        val raw = """{"id":"s1","title":"Test","note":null,"hlc":null,"tagIds":null}"""
        val schedule = DayNewJson.decodeFromString<ScheduleDto>(raw)
        assertEquals("s1", schedule.id)
        assertEquals("", schedule.note)
        assertEquals("", schedule.hlc)
        assertEquals(emptyList<String>(), schedule.tagIds)
    }

    @Test
    fun `fingerprint matches windows format`() {
        val dto = StoreDto(
            goals = listOf(GoalDto(id = "g1", hlc = "019a-0000-win")),
            schedules = listOf(ScheduleDto(id = "s1", hlc = "019b-0000-win")),
            presets = null,
        )
        assertEquals("019b-0000-win|1|0|0|0|1|0|3", WebDavClient.computeStoreFingerprint(dto, 3))
    }
}
