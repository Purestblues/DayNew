package com.daynew.sync

import okhttp3.Credentials
import okhttp3.MediaType.Companion.toMediaType
import okhttp3.OkHttpClient
import okhttp3.Request
import okhttp3.RequestBody.Companion.toRequestBody
import okhttp3.Response
import org.w3c.dom.Element
import org.xml.sax.InputSource
import java.io.ByteArrayInputStream
import java.io.ByteArrayOutputStream
import java.io.IOException
import java.io.StringReader
import java.net.URI
import java.net.URLDecoder
import java.net.URLEncoder
import java.nio.charset.StandardCharsets
import java.util.concurrent.TimeUnit
import java.util.zip.GZIPInputStream
import java.util.zip.GZIPOutputStream
import javax.xml.XMLConstants
import javax.xml.parsers.DocumentBuilderFactory

data class PropfindEntry(
    val fileName: String,
    val eTag: String,
)

data class RemotePeerSnapshot(
    val fileName: String,
    val eTag: String,
    val snapshot: DeviceSnapshotDto,
)

data class RemotePullBundle(
    val peers: List<RemotePeerSnapshot>,
    val ownFileExistsOnServer: Boolean,
    val remoteDeviceFilesCount: Int,
)

data class ConnectionTestResult(
    val ok: Boolean,
    val message: String,
)

/**
 * 轻量 WebDAV 客户端（兼容坚果云），与 Windows 端 Services/WebDavService.cs 1:1 对齐：
 *   - PROPFIND Depth: 0 / 1 + 404 自动 MKCOL
 *   - device-<deviceId>.json.gz ETag 增量拉取与 GZip 互解
 *   - PUT .tmp + MOVE (Overwrite: T) 原子更新（不支持 MOVE 时回退直写 PUT）
 *   - 503 / 429 指数退避重试（500ms, 1200ms）
 */
class WebDavClient(
    private val httpClient: OkHttpClient = defaultOkHttpClient(),
) {
    fun testConnection(folderUrl: String, user: String, password: String): ConnectionTestResult {
        val normalized = normalizeFolderUrl(folderUrl)
        val auth = buildAuthHeader(user, password)

        sendWithRetry {
            Request.Builder()
                .url(normalized)
                .method("PROPFIND", ByteArray(0).toRequestBody(null))
                .header("Depth", "0")
                .applyAuth(auth)
                .build()
        }.use { propResp ->
            if (propResp.code == 200 || propResp.code == 207) {
                return ConnectionTestResult(true, "连接成功")
            }

            if (propResp.code == 404) {
                sendWithRetry {
                    Request.Builder()
                        .url(normalized)
                        .method("MKCOL", ByteArray(0).toRequestBody(null))
                        .applyAuth(auth)
                        .build()
                }.use { mkcolResp ->
                    if (mkcolResp.isSuccessful || mkcolResp.code == 405) {
                        return ConnectionTestResult(true, "连接成功（已自动创建远端目录）")
                    }
                    if (mkcolResp.code == 401 || mkcolResp.code == 403) {
                        return ConnectionTestResult(false, "鉴权失败：请检查账号与坚果云应用专属密码")
                    }
                    if (mkcolResp.code == 409) {
                        return ConnectionTestResult(false, "远端上级目录不存在，请检查 WebDAV 路径")
                    }
                    return ConnectionTestResult(false, "创建目录失败：服务器返回 ${mkcolResp.code} ${mkcolResp.message}")
                }
            }

            if (propResp.code == 401 || propResp.code == 403) {
                return ConnectionTestResult(false, "鉴权失败：请检查账号与坚果云应用专属密码")
            }
            return ConnectionTestResult(false, "服务器返回 ${propResp.code} ${propResp.message}")
        }
    }

    fun pullRemoteSnapshots(
        folderUrl: String,
        user: String,
        password: String,
        ownDeviceId: String,
        cachedPeerEtags: Map<String, String> = emptyMap(),
    ): RemotePullBundle {
        val normalized = normalizeFolderUrl(folderUrl)
        val auth = buildAuthHeader(user, password)
        ensureDirectory(normalized, auth)

        val entries = sendWithRetry {
            Request.Builder()
                .url(normalized)
                .method("PROPFIND", PROPFIND_BODY_XML.toRequestBody("application/xml; charset=utf-8".toMediaType()))
                .header("Depth", "1")
                .applyAuth(auth)
                .build()
        }.use { listResp ->
            throwIfAuthOrError(listResp, "列举远端同步目录失败")
            val xmlText = listResp.body?.string().orEmpty()
            parsePropfindDirectoryEntries(xmlText)
        }

        val ownFileName = getDeviceSnapshotFileName(ownDeviceId)
        var ownExists = false
        var remoteDeviceFiles = 0
        val peers = mutableListOf<RemotePeerSnapshot>()

        for ((fileName, etag) in entries) {
            if (!fileName.startsWith("device-", ignoreCase = true) ||
                !fileName.endsWith(".json.gz", ignoreCase = true)
            ) {
                continue
            }

            if (fileName.equals(ownFileName, ignoreCase = true)) {
                ownExists = true
                continue
            }

            remoteDeviceFiles++
            val cached = cachedPeerEtags[fileName]
            if (etag.isNotEmpty() && cached != null && cached == etag) {
                continue
            }

            val fileUrl = normalized + encodePathSegment(fileName)
            sendWithRetry {
                Request.Builder()
                    .url(fileUrl)
                    .get()
                    .applyAuth(auth)
                    .build()
            }.use { getResp ->
                if (getResp.code == 404) continue
                throwIfAuthOrError(getResp, "下载远端快照 $fileName 失败")

                val gzBytes = getResp.body?.bytes() ?: ByteArray(0)
                val resolvedEtag = getResp.header("ETag") ?: etag
                val snapshot = deserializeSnapshotGzip(gzBytes)
                peers += RemotePeerSnapshot(fileName, resolvedEtag, snapshot)
            }
        }

        return RemotePullBundle(peers, ownExists, remoteDeviceFiles)
    }

    fun pushDeviceSnapshot(
        folderUrl: String,
        user: String,
        password: String,
        snapshot: DeviceSnapshotDto,
    ) {
        val normalized = normalizeFolderUrl(folderUrl)
        val auth = buildAuthHeader(user, password)
        val gzBytes = serializeSnapshotGzip(snapshot)
        val targetName = getDeviceSnapshotFileName(snapshot.deviceId)
        val targetUrl = normalized + encodePathSegment(targetName)
        val tmpRemoteUrl = "$targetUrl.tmp"
        val gzipMediaType = "application/gzip".toMediaType()

        sendWithRetry {
            Request.Builder()
                .url(tmpRemoteUrl)
                .put(gzBytes.toRequestBody(gzipMediaType))
                .applyAuth(auth)
                .build()
        }.use { putTmpResp ->
            throwIfAuthOrError(putTmpResp, "上传本机快照失败")
        }

        val moveOk = sendWithRetry {
            Request.Builder()
                .url(tmpRemoteUrl)
                .method("MOVE", ByteArray(0).toRequestBody(null))
                .header("Destination", targetUrl)
                .header("Overwrite", "T")
                .applyAuth(auth)
                .build()
        }.use { moveResp ->
            moveResp.isSuccessful
        }

        if (!moveOk) {
            sendWithRetry {
                Request.Builder()
                    .url(targetUrl)
                    .put(gzBytes.toRequestBody(gzipMediaType))
                    .applyAuth(auth)
                    .build()
            }.use { directPutResp ->
                throwIfAuthOrError(directPutResp, "上传本机快照失败")
            }
        }
    }

    private fun ensureDirectory(folderUrl: String, auth: String?) {
        sendWithRetry {
            Request.Builder()
                .url(folderUrl)
                .method("PROPFIND", ByteArray(0).toRequestBody(null))
                .header("Depth", "0")
                .applyAuth(auth)
                .build()
        }.use { propResp ->
            if (propResp.code == 200 || propResp.code == 207) return

            if (propResp.code == 404) {
                sendWithRetry {
                    Request.Builder()
                        .url(folderUrl)
                        .method("MKCOL", ByteArray(0).toRequestBody(null))
                        .applyAuth(auth)
                        .build()
                }.use { mkcolResp ->
                    if (mkcolResp.isSuccessful || mkcolResp.code == 405) return
                    if (mkcolResp.code == 409) {
                        throw IllegalStateException("远端上级目录不存在，请检查 WebDAV 路径")
                    }
                    throwIfAuthOrError(mkcolResp, "创建远端目录失败")
                    return
                }
            }

            throwIfAuthOrError(propResp, "访问远端目录失败")
        }
    }

    private fun sendWithRetry(requestFactory: () -> Request): Response {
        var attempt = 0
        while (true) {
            val response = httpClient.newCall(requestFactory()).execute()
            val status = response.code
            if ((status == 503 || status == 429) && attempt < RETRY_DELAYS_MS.size) {
                response.close()
                Thread.sleep(RETRY_DELAYS_MS[attempt])
                attempt++
                continue
            }
            return response
        }
    }

    private fun throwIfAuthOrError(resp: Response, action: String) {
        if (resp.isSuccessful) return
        if (resp.code == 401 || resp.code == 403) {
            throw SecurityException("鉴权失败：请检查账号与坚果云应用专属密码")
        }
        throw IOException("$action：服务器返回 ${resp.code} ${resp.message}")
    }

    private fun buildAuthHeader(user: String, password: String): String? =
        if (user.isNotBlank() || password.isNotEmpty()) {
            Credentials.basic(user, password, StandardCharsets.UTF_8)
        } else {
            null
        }

    private fun Request.Builder.applyAuth(auth: String?): Request.Builder {
        header("User-Agent", USER_AGENT)
        if (auth != null) header("Authorization", auth)
        return this
    }

    companion object {
        private const val USER_AGENT = "DayNew-Android/0.1"
        private val RETRY_DELAYS_MS = longArrayOf(500L, 1200L)
        private const val PROPFIND_BODY_XML =
            "<?xml version=\"1.0\" encoding=\"utf-8\"?><d:propfind xmlns:d=\"DAV:\"><d:prop><d:getetag/><d:resourcetype/></d:prop></d:propfind>"

        fun defaultOkHttpClient(): OkHttpClient = OkHttpClient.Builder()
            .connectTimeout(30, TimeUnit.SECONDS)
            .readTimeout(30, TimeUnit.SECONDS)
            .writeTimeout(30, TimeUnit.SECONDS)
            .build()

        fun getDeviceSnapshotFileName(deviceId: String?): String {
            val safe = if (deviceId.isNullOrBlank()) "android-local" else deviceId.trim()
            return "device-$safe.json.gz"
        }

        fun normalizeFolderUrl(rawUrl: String?): String {
            val trimmed = (rawUrl ?: "").trim()
            val uri = runCatching { URI(trimmed) }.getOrNull()
            val scheme = uri?.scheme?.lowercase()
            if (uri == null || !uri.isAbsolute || (scheme != "http" && scheme != "https")) {
                throw IllegalArgumentException("WebDAV 地址格式无效，需以 http:// 或 https:// 开头")
            }
            val host = uri.host?.lowercase() ?: ""
            val cleanPath = (uri.path ?: "").trim('/')
            if (host == "dav.jianguoyun.com" && (cleanPath.isEmpty() || cleanPath.equals("dav", ignoreCase = true))) {
                throw IllegalArgumentException("坚果云根目录 /dav/ 不支持直接存放文件，请在末尾补充子目录，例如 https://dav.jianguoyun.com/dav/DayNewSync/")
            }
            return if (trimmed.endsWith("/")) trimmed else "$trimmed/"
        }

        fun serializeSnapshotGzip(snapshot: DeviceSnapshotDto): ByteArray {
            val jsonBytes = DayNewJson.encodeToString(DeviceSnapshotDto.serializer(), snapshot)
                .toByteArray(StandardCharsets.UTF_8)
            val baos = ByteArrayOutputStream()
            GZIPOutputStream(baos).use { gz ->
                gz.write(jsonBytes)
            }
            return baos.toByteArray()
        }

        fun deserializeSnapshotGzip(gzBytes: ByteArray): DeviceSnapshotDto {
            val jsonBytes = GZIPInputStream(ByteArrayInputStream(gzBytes)).use { gz ->
                gz.readBytes()
            }
            val jsonText = jsonBytes.toString(StandardCharsets.UTF_8)
            return DayNewJson.decodeFromString(DeviceSnapshotDto.serializer(), jsonText)
        }

        fun computeStoreFingerprint(dto: StoreDto, completionsCount: Int): String {
            var maxHlc = ""
            dto.goals.forEach { maxHlc = HlcClock.max(maxHlc, it.hlc) }
            dto.tags.forEach { maxHlc = HlcClock.max(maxHlc, it.hlc) }
            dto.persons.forEach { maxHlc = HlcClock.max(maxHlc, it.hlc) }
            dto.locations.forEach { maxHlc = HlcClock.max(maxHlc, it.hlc) }
            dto.schedules.forEach { maxHlc = HlcClock.max(maxHlc, it.hlc) }
            dto.presets?.forEach { maxHlc = HlcClock.max(maxHlc, it.hlc) }

            return "$maxHlc|${dto.goals.size}|${dto.tags.size}|${dto.persons.size}|${dto.locations.size}|${dto.schedules.size}|${dto.presets?.size ?: 0}|$completionsCount"
        }

        fun parsePropfindDirectoryEntries(xmlText: String): List<PropfindEntry> {
            if (xmlText.isBlank()) return emptyList()
            return try {
                val factory = DocumentBuilderFactory.newInstance().apply {
                    isNamespaceAware = true
                    isExpandEntityReferences = false
                    // Android 内置 Harmony XML 解析器不支持部分桌面 JDK 安全特性，逐项安全降级
                    runCatching { setFeature(XMLConstants.FEATURE_SECURE_PROCESSING, true) }
                    runCatching { setFeature("http://apache.org/xml/features/disallow-doctype-decl", true) }
                }
                val doc = factory.newDocumentBuilder().parse(InputSource(StringReader(xmlText)))
                val responses = doc.getElementsByTagNameNS("*", "response")
                val result = ArrayList<PropfindEntry>(responses.length)

                for (i in 0 until responses.length) {
                    val respEl = responses.item(i) as? Element ?: continue
                    val hrefRaw = findFirstDescendantText(respEl, "href")
                    if (hrefRaw.isNullOrBlank()) continue

                    val pathPart = runCatching {
                        val u = URI(hrefRaw)
                        if (u.isAbsolute) u.rawPath ?: hrefRaw else hrefRaw
                    }.getOrDefault(hrefRaw)

                    val trimmed = pathPart.trimEnd('/')
                    val slash = trimmed.lastIndexOf('/')
                    val rawName = if (slash >= 0) trimmed.substring(slash + 1) else trimmed
                    val name = URLDecoder.decode(rawName.replace("+", "%2B"), StandardCharsets.UTF_8)
                    if (name.isBlank()) continue

                    val etag = findFirstDescendantText(respEl, "getetag")?.trim() ?: ""
                    result += PropfindEntry(name, etag)
                }
                result
            } catch (ex: Exception) {
                throw IOException("解析远端目录列表失败：${ex.message ?: ex.javaClass.simpleName}", ex)
            }
        }

        private fun findFirstDescendantText(root: Element, localName: String): String? {
            val nodes = root.getElementsByTagNameNS("*", localName)
            if (nodes.length == 0) return null
            return nodes.item(0)?.textContent
        }

        private fun encodePathSegment(segment: String): String =
            URLEncoder.encode(segment, StandardCharsets.UTF_8).replace("+", "%20")
    }
}
