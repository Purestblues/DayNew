package com.daynew.app.data

import com.daynew.sync.DayNewJson
import com.daynew.sync.StoreDto
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import kotlinx.coroutines.runBlocking
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import java.io.File
import java.io.FileOutputStream
import java.nio.charset.StandardCharsets
import java.nio.file.AtomicMoveNotSupportedException
import java.nio.file.Files
import java.nio.file.StandardCopyOption
import java.time.LocalDateTime
import java.time.format.DateTimeFormatter
import java.util.UUID
import java.util.concurrent.atomic.AtomicLong

sealed interface StoreLoadResult {
    data class Loaded(val store: StoreDto) : StoreLoadResult
    data object NotFound : StoreLoadResult
    data class Quarantined(val errorMessage: String) : StoreLoadResult
}

/**
 * 本地 JSON 原子持久化（data.json + data.prev.json + data.json.tmp）。
 * 与 Windows 端 Services/StoreFile.cs 的原子写、上一版备份和坏文件隔离策略对齐。
 */
class LocalStoreFile(val dataDir: File) {
    val dataFile: File = File(dataDir, "data.json")
    val prevFile: File = File(dataDir, "data.prev.json")
    private val tmpFile: File = File(dataDir, "data.json.tmp")
    private val deviceIdFile: File = File(dataDir, "device-id.txt")
    private val ioLock = Any()

    fun loadOrCreateDeviceId(): String = synchronized(ioLock) {
        if (deviceIdFile.exists()) {
            val existing = runCatching {
                deviceIdFile.readText(StandardCharsets.UTF_8).trim()
            }.getOrNull()
            if (!existing.isNullOrBlank()) return existing
        }
        dataDir.mkdirs()
        val generated = "android-" + UUID.randomUUID().toString().replace("-", "").take(6)
        deviceIdFile.writeText(generated, StandardCharsets.UTF_8)
        generated
    }

    fun load(): StoreLoadResult = synchronized(ioLock) {
        if (!dataFile.exists()) return StoreLoadResult.NotFound
        return try {
            val raw = dataFile.readText(StandardCharsets.UTF_8)
            val dto = DayNewJson.decodeFromString(StoreDto.serializer(), raw)
            StoreLoadResult.Loaded(dto)
        } catch (ex: Exception) {
            quarantineCorruptedFile(ex)
            StoreLoadResult.Quarantined(ex.message ?: ex.javaClass.simpleName)
        }
    }

    fun writeAtomic(dto: StoreDto) = synchronized(ioLock) {
        dataDir.mkdirs()
        val json = DayNewJson.encodeToString(StoreDto.serializer(), dto)
        FileOutputStream(tmpFile, false).use { out ->
            out.write(json.toByteArray(StandardCharsets.UTF_8))
            out.flush()
            runCatching { out.fd.sync() }
        }

        if (dataFile.exists()) {
            runCatching {
                dataFile.copyTo(prevFile, overwrite = true)
            }
        }

        try {
            Files.move(
                tmpFile.toPath(),
                dataFile.toPath(),
                StandardCopyOption.REPLACE_EXISTING,
                StandardCopyOption.ATOMIC_MOVE,
            )
        } catch (_: AtomicMoveNotSupportedException) {
            Files.move(
                tmpFile.toPath(),
                dataFile.toPath(),
                StandardCopyOption.REPLACE_EXISTING,
            )
        }
    }

    private fun quarantineCorruptedFile(ex: Exception) {
        runCatching {
            dataDir.mkdirs()
            val stamp = LocalDateTime.now().format(DateTimeFormatter.ofPattern("yyyyMMdd-HHmmss"))
            val brokenFile = File(dataDir, "data.broken-$stamp.json")
            if (dataFile.exists()) {
                Files.move(
                    dataFile.toPath(),
                    brokenFile.toPath(),
                    StandardCopyOption.REPLACE_EXISTING,
                )
            }
            val errLog = File(dataDir, "load-error.txt")
            val nowText = LocalDateTime.now().format(DateTimeFormatter.ofPattern("yyyy-MM-dd HH:mm:ss"))
            errLog.writeText("$nowText\n${ex.stackTraceToString()}\n", StandardCharsets.UTF_8)
        }
    }
}

/**
 * 1 秒防抖写入调度器：带单调快照版本号，防止旧快照覆盖新快照；支持后台/切屏时 flushNow。
 */
class DebouncedStoreWriter(
    private val storeFile: LocalStoreFile,
    private val scope: CoroutineScope,
    private val debounceMs: Long = 1000L,
) {
    private val snapshotVersion = AtomicLong(0L)
    private var writtenVersion: Long = 0L
    private val writeMutex = Mutex()
    private var pendingJob: Job? = null

    @Volatile
    private var latestSnapshot: Pair<Long, StoreDto>? = null

    fun scheduleSave(dto: StoreDto) {
        val version = snapshotVersion.incrementAndGet()
        latestSnapshot = version to dto
        pendingJob?.cancel()
        pendingJob = scope.launch(Dispatchers.IO) {
            delay(debounceMs)
            writeLatestIfNewer()
        }
    }

    fun flushNow(dto: StoreDto? = null) {
        if (dto != null) {
            val version = snapshotVersion.incrementAndGet()
            latestSnapshot = version to dto
        }
        pendingJob?.cancel()
        pendingJob = null
        runBlocking(Dispatchers.IO) {
            writeLatestIfNewer()
        }
    }

    private suspend fun writeLatestIfNewer() {
        writeMutex.withLock {
            val snap = latestSnapshot ?: return
            if (snap.first <= writtenVersion) return
            storeFile.writeAtomic(snap.second)
            writtenVersion = snap.first
        }
    }
}
