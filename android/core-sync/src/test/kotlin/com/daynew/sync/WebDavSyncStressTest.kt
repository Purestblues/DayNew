package com.daynew.sync

import com.sun.net.httpserver.HttpExchange
import com.sun.net.httpserver.HttpServer
import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertFalse
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.Test
import java.net.InetSocketAddress
import java.net.URI
import java.net.URLDecoder
import java.nio.charset.StandardCharsets
import java.util.concurrent.ConcurrentHashMap
import java.util.concurrent.atomic.AtomicInteger

class WebDavSyncStressTest {
    /**
     * 完整可读写内存 WebDAV 测试服务端（支持 PROPFIND / MKCOL / PUT / MOVE / GET 与错误注入）。
     */
    private class InMemoryWebDav(
        var allowMove: Boolean = true,
    ) : AutoCloseable {
        data class StoredFile(val bytes: ByteArray, val etag: String)

        private val folders = ConcurrentHashMap.newKeySet<String>()
        private val files = ConcurrentHashMap<String, StoredFile>()
        private val transientFailures = ArrayDeque<Int>()
        private val etagSeq = AtomicInteger(0)
        val getCount = AtomicInteger(0)
        val directTargetPutCount = AtomicInteger(0)

        private val server: HttpServer = HttpServer.create(InetSocketAddress("127.0.0.1", 0), 0).apply {
            createContext("/") { ex -> handle(ex) }
            start()
        }

        fun folderUrl(path: String = "/dav/DayNew/"): String =
            "http://127.0.0.1:${server.address.port}$path"

        fun enqueueTransientFailures(vararg codes: Int) {
            synchronized(transientFailures) {
                codes.forEach { transientFailures.addLast(it) }
            }
        }

        private fun handle(ex: HttpExchange) {
            try {
                val body = ex.requestBody.use { it.readBytes() }
                val method = ex.requestMethod.uppercase()
                val rawPath = ex.requestURI.rawPath ?: "/"
                val path = URLDecoder.decode(rawPath, StandardCharsets.UTF_8)

                val injected = synchronized(transientFailures) {
                    if (transientFailures.isEmpty()) null else transientFailures.removeFirst()
                }
                if (injected != null) {
                    ex.sendResponseHeaders(injected, -1)
                    return
                }

                when (method) {
                    "PROPFIND" -> {
                        val folder = if (path.endsWith("/")) path else "$path/"
                        if (folder !in folders) {
                            ex.sendResponseHeaders(404, -1)
                            return
                        }
                        val depth = ex.requestHeaders.getFirst("Depth") ?: "0"
                        val xml = buildPropfindXml(folder, includeChildren = depth == "1")
                        val bytes = xml.toByteArray(StandardCharsets.UTF_8)
                        ex.responseHeaders.set("Content-Type", "application/xml; charset=utf-8")
                        ex.sendResponseHeaders(207, bytes.size.toLong())
                        ex.responseBody.write(bytes)
                    }
                    "MKCOL" -> {
                        val folder = if (path.endsWith("/")) path else "$path/"
                        folders.add(folder)
                        ex.sendResponseHeaders(201, -1)
                    }
                    "PUT" -> {
                        if (!path.endsWith(".tmp")) {
                            directTargetPutCount.incrementAndGet()
                        }
                        val nextTag = "\"etag-${etagSeq.incrementAndGet()}\""
                        files[path] = StoredFile(body, nextTag)
                        ex.responseHeaders.set("ETag", nextTag)
                        ex.sendResponseHeaders(201, -1)
                    }
                    "MOVE" -> {
                        if (!allowMove) {
                            ex.sendResponseHeaders(405, -1)
                            return
                        }
                        val destHeader = ex.requestHeaders.getFirst("Destination")
                        if (destHeader.isNullOrBlank()) {
                            ex.sendResponseHeaders(400, -1)
                            return
                        }
                        val destPath = URLDecoder.decode(URI(destHeader).rawPath, StandardCharsets.UTF_8)
                        val src = files.remove(path)
                        if (src == null) {
                            ex.sendResponseHeaders(404, -1)
                            return
                        }
                        val nextTag = "\"etag-${etagSeq.incrementAndGet()}\""
                        files[destPath] = src.copy(etag = nextTag)
                        ex.responseHeaders.set("ETag", nextTag)
                        ex.sendResponseHeaders(204, -1)
                    }
                    "GET" -> {
                        getCount.incrementAndGet()
                        val stored = files[path]
                        if (stored == null) {
                            ex.sendResponseHeaders(404, -1)
                            return
                        }
                        ex.responseHeaders.set("ETag", stored.etag)
                        ex.responseHeaders.set("Content-Type", "application/gzip")
                        ex.sendResponseHeaders(200, stored.bytes.size.toLong())
                        ex.responseBody.write(stored.bytes)
                    }
                    else -> ex.sendResponseHeaders(405, -1)
                }
            } finally {
                ex.close()
            }
        }

        private fun buildPropfindXml(folder: String, includeChildren: Boolean): String {
            val sb = StringBuilder()
            sb.append("""<?xml version="1.0" encoding="utf-8"?><D:multistatus xmlns:D="DAV:">""")
            sb.append("""<D:response><D:href>$folder</D:href><D:propstat><D:prop><D:resourcetype><D:collection/></D:resourcetype></D:prop></D:propstat></D:response>""")
            if (includeChildren) {
                for ((filePath, stored) in files) {
                    if (!filePath.startsWith(folder)) continue
                    val rel = filePath.removePrefix(folder)
                    if ('/' in rel || rel.isBlank()) continue
                    sb.append("""<D:response><D:href>$filePath</D:href><D:propstat><D:prop><D:getetag>${stored.etag}</D:getetag></D:prop></D:propstat></D:response>""")
                }
            }
            sb.append("</D:multistatus>")
            return sb.toString()
        }

        override fun close() {
            server.stop(0)
        }
    }

    @Test
    fun `webdav client retries 503 and 429 and auto creates missing collection`() {
        InMemoryWebDav().use { dav ->
            val client = WebDavClient()
            val url = dav.folderUrl()

            // TestConnection: 503 -> 429 -> 404 (triggers MKCOL) -> 201
            dav.enqueueTransientFailures(503, 429)
            val testRes = client.testConnection(url, "u", "p")
            assertTrue(testRes.ok, "Expected connection test to recover from 503/429: ${testRes.message}")

            // Push snapshot: 503 -> 429 during PUT .tmp / MOVE
            dav.enqueueTransientFailures(503, 429)
            val snapshot = DeviceSnapshotDto(
                version = 1,
                deviceId = "android-test1",
                snapshotHlc = HlcClock("android-test1").tick(),
                store = StoreDto(schedules = listOf(ScheduleDto(id = "s1", title = "压测任务", due = "2026-11-27"))),
            )
            client.pushDeviceSnapshot(url, "u", "p", snapshot)

            // Pull from peer device with 503 backoff
            dav.enqueueTransientFailures(503)
            val pull = client.pullRemoteSnapshots(url, "u", "p", ownDeviceId = "win-peer")
            assertEquals(1, pull.peers.size)
            assertEquals("压测任务", pull.peers.first().snapshot.store.schedules.first().title)
        }
    }

    @Test
    fun `webdav push falls back to direct PUT when MOVE returns 405`() {
        InMemoryWebDav(allowMove = false).use { dav ->
            val client = WebDavClient()
            val url = dav.folderUrl()
            assertTrue(client.testConnection(url, "u", "p").ok)

            val snapshot = DeviceSnapshotDto(
                version = 1,
                deviceId = "android-nomove",
                snapshotHlc = HlcClock("android-nomove").tick(),
                store = StoreDto(schedules = listOf(ScheduleDto(id = "s2", title = "回退直写"))),
            )
            client.pushDeviceSnapshot(url, "u", "p", snapshot)
            assertEquals(1, dav.directTargetPutCount.get())

            val pull = client.pullRemoteSnapshots(url, "u", "p", ownDeviceId = "win-peer")
            assertEquals(1, pull.peers.size)
            assertEquals("回退直写", pull.peers.first().snapshot.store.schedules.first().title)
        }
    }

    @Test
    fun `offline concurrent core field edits produce conflict copy and converge across devices`() {
        InMemoryWebDav().use { dav ->
            val client = WebDavClient()
            val url = dav.folderUrl()
            assertTrue(client.testConnection(url, "u", "p").ok)

            var nowMs = 1_764_200_000_000L
            val winClock = HlcClock("win-peer") { nowMs }
            val andClock = HlcClock("android-peer") { nowMs + 5 }

            // Round 1: Windows creates task and pushes baseline
            val baseTick = winClock.tick()
            var winStore = StoreDto(
                schedules = listOf(
                    ScheduleDto(
                        id = "shared01",
                        title = "原始任务",
                        due = "2026-11-27",
                        hlc = baseTick,
                        fieldHlcs = SyncMergeEngine.SCHEDULE_FIELDS.associateWith { baseTick },
                    )
                )
            )
            client.pushDeviceSnapshot(
                url, "u", "p",
                DeviceSnapshotDto(deviceId = "win-peer", snapshotHlc = baseTick, store = winStore),
            )

            // Android pulls baseline and records watermark
            val andPull1 = client.pullRemoteSnapshots(url, "u", "p", ownDeviceId = "android-peer")
            val winWatermark = andPull1.peers.first().snapshot.snapshotHlc
            var andStore = SyncMergeEngine.mergeStore(StoreDto(), andPull1.peers.first().snapshot.store, null).mergedDto
            andClock.observe(winWatermark)

            // Both devices go offline and edit title + due concurrently after watermark
            nowMs += 10_000L
            val winEditTick = winClock.tick()
            val winEditTick2 = winClock.tick()
            winStore = winStore.copy(
                schedules = winStore.schedules.map {
                    it.copy(
                        title = "电脑端修改标题",
                        due = "2026-11-28",
                        hlc = winEditTick2,
                        fieldHlcs = it.fieldHlcs.orEmpty() + mapOf("title" to winEditTick, "due" to winEditTick2),
                    )
                }
            )
            client.pushDeviceSnapshot(
                url, "u", "p",
                DeviceSnapshotDto(deviceId = "win-peer", snapshotHlc = winEditTick2, store = winStore),
            )

            nowMs += 1_000L
            val andEditTick = andClock.tick()
            val andEditTick2 = andClock.tick()
            andStore = andStore.copy(
                schedules = andStore.schedules.map {
                    it.copy(
                        title = "手机端离线标题",
                        due = "2026-11-29",
                        hlc = andEditTick2,
                        fieldHlcs = it.fieldHlcs.orEmpty() + mapOf("title" to andEditTick, "due" to andEditTick2),
                    )
                }
            )

            // Android pulls Windows update and merges with watermark -> expects [冲突] copy
            val andPull2 = client.pullRemoteSnapshots(url, "u", "p", ownDeviceId = "android-peer")
            val conflictSeq = AtomicInteger(1)
            val andReport = SyncMergeEngine.mergeStore(
                local = andStore,
                remote = andPull2.peers.first().snapshot.store,
                lastSyncedRemoteHlc = winWatermark,
                conflictIdGenerator = { "conf000${conflictSeq.getAndIncrement()}" },
                conflictHlcGenerator = { andClock.tick() },
            )
            andStore = andReport.mergedDto
            assertEquals(1, andReport.conflictCopiesCount)
            assertEquals(2, andStore.schedules.count { !it.del })
            assertTrue(andStore.schedules.any { it.title == "手机端离线标题" })
            assertTrue(andStore.schedules.any { it.title.startsWith("[冲突]") && "电脑端修改标题" in it.title })

            // Android pushes merged store -> Windows pulls and converges to exact same active titles
            client.pushDeviceSnapshot(
                url, "u", "p",
                DeviceSnapshotDto(deviceId = "android-peer", snapshotHlc = andClock.tick(), store = andStore),
            )
            val winPull2 = client.pullRemoteSnapshots(url, "u", "p", ownDeviceId = "win-peer")
            val winReport = SyncMergeEngine.mergeStore(
                local = winStore,
                remote = winPull2.peers.first().snapshot.store,
                lastSyncedRemoteHlc = null,
            )
            winStore = winReport.mergedDto

            val andTitles = andStore.schedules.filter { !it.del }.map { it.title }.sorted()
            val winTitles = winStore.schedules.filter { !it.del }.map { it.title }.sorted()
            assertEquals(andTitles, winTitles)
        }
    }

    @Test
    fun `tombstone deletion vs newer offline modification arbitrates deterministically`() {
        val clockA = HlcClock("win-peer") { 1_764_200_000_000L }
        val t1 = clockA.tick()
        val delTick = HlcClock("win-peer") { 1_764_200_005_000L }.tick()
        val resurrectTick = HlcClock("android-peer") { 1_764_200_010_000L }.tick()

        val deletedRemote = StoreDto(
            schedules = listOf(ScheduleDto(id = "x1", title = "旧任务", hlc = delTick, del = true))
        )
        val unmodifiedLocal = StoreDto(
            schedules = listOf(ScheduleDto(id = "x1", title = "旧任务", hlc = t1, del = false))
        )
        val stayDeleted = SyncMergeEngine.mergeStore(unmodifiedLocal, deletedRemote).mergedDto
        assertTrue(stayDeleted.schedules.single().del)

        val newerLocalEdit = StoreDto(
            schedules = listOf(
                ScheduleDto(
                    id = "x1",
                    title = "手机复活改动",
                    hlc = resurrectTick,
                    del = false,
                    fieldHlcs = mapOf("title" to resurrectTick),
                )
            )
        )
        val resurrected = SyncMergeEngine.mergeStore(newerLocalEdit, deletedRemote).mergedDto
        assertFalse(resurrected.schedules.single().del)
        assertEquals("手机复活改动", resurrected.schedules.single().title)
    }

    @Test
    fun `pullRemoteSnapshots skips unchanged peer files via cached ETag`() {
        InMemoryWebDav().use { dav ->
            val client = WebDavClient()
            val url = dav.folderUrl()
            assertTrue(client.testConnection(url, "u", "p").ok)

            client.pushDeviceSnapshot(
                url, "u", "p",
                DeviceSnapshotDto(deviceId = "win-peer", snapshotHlc = HlcClock("win-peer").tick(), store = StoreDto()),
            )

            val pull1 = client.pullRemoteSnapshots(url, "u", "p", ownDeviceId = "android-peer")
            assertEquals(1, pull1.peers.size)
            assertEquals(1, dav.getCount.get())

            val cached = mapOf(pull1.peers.first().fileName to pull1.peers.first().eTag)
            val pull2 = client.pullRemoteSnapshots(url, "u", "p", ownDeviceId = "android-peer", cachedPeerEtags = cached)
            assertEquals(0, pull2.peers.size)
            assertEquals(1, pull2.remoteDeviceFilesCount)
            assertEquals(1, dav.getCount.get(), "ETag hit must avoid HTTP GET download")
        }
    }
}
