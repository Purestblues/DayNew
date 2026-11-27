package com.daynew.app.sync

import com.daynew.sync.DayNewJson
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.serialization.Serializable
import java.io.File
import java.io.FileOutputStream
import java.nio.charset.StandardCharsets
import java.nio.file.Files
import java.nio.file.StandardCopyOption

@Serializable
data class SyncConfigDto(
    val serverUrl: String = "",
    val username: String = "",
    val encryptedPassword: String = "",
    val autoSyncEnabled: Boolean = true,
    val notificationsEnabled: Boolean = true,
    val peerEtags: Map<String, String> = emptyMap(),
    val peerWatermarks: Map<String, String> = emptyMap(),
    val lastSyncedFingerprint: String = "",
    val lastSyncAt: String = "",
    val lastSyncSummary: String = "",
    val lastSyncError: String = "",
)

data class SyncStatusUi(
    val serverUrl: String = "",
    val username: String = "",
    val hasSavedPassword: Boolean = false,
    val isConfigured: Boolean = false,
    val autoSyncEnabled: Boolean = true,
    val notificationsEnabled: Boolean = true,
    val isSyncing: Boolean = false,
    val lastSyncAt: String = "",
    val lastSyncSummary: String = "",
    val lastSyncError: String = "",
)

/**
 * 同步配置与水位线独立落盘（sync-prefs.json，不混入 data.json 业务契约）。
 */
class SyncPreferencesStore(private val dataDir: File) {
    private val prefsFile = File(dataDir, "sync-prefs.json")
    private val tmpFile = File(dataDir, "sync-prefs.json.tmp")
    private val lock = Any()

    @Volatile
    private var config: SyncConfigDto = readFromDisk()

    @Volatile
    private var syncing: Boolean = false

    private val _statusFlow = MutableStateFlow(toStatusUi(config, syncing))
    val statusFlow: StateFlow<SyncStatusUi> = _statusFlow.asStateFlow()

    fun currentConfig(): SyncConfigDto = synchronized(lock) { config }

    fun readDecryptedPassword(): String = synchronized(lock) {
        KeystoreCrypto.decrypt(config.encryptedPassword)
    }

    fun isConfigured(): Boolean = synchronized(lock) {
        config.serverUrl.isNotBlank() && config.username.isNotBlank() && config.encryptedPassword.isNotBlank()
    }

    fun updateConfig(transform: (SyncConfigDto) -> SyncConfigDto): SyncConfigDto = synchronized(lock) {
        val next = transform(config)
        config = next
        writeToDisk(next)
        _statusFlow.value = toStatusUi(next, syncing)
        next
    }

    fun setSyncing(isSyncing: Boolean) = synchronized(lock) {
        syncing = isSyncing
        _statusFlow.value = toStatusUi(config, syncing)
    }

    private fun toStatusUi(cfg: SyncConfigDto, isSyncingNow: Boolean): SyncStatusUi = SyncStatusUi(
        serverUrl = cfg.serverUrl,
        username = cfg.username,
        hasSavedPassword = cfg.encryptedPassword.isNotBlank(),
        isConfigured = cfg.serverUrl.isNotBlank() && cfg.username.isNotBlank() && cfg.encryptedPassword.isNotBlank(),
        autoSyncEnabled = cfg.autoSyncEnabled,
        notificationsEnabled = cfg.notificationsEnabled,
        isSyncing = isSyncingNow,
        lastSyncAt = cfg.lastSyncAt,
        lastSyncSummary = cfg.lastSyncSummary,
        lastSyncError = cfg.lastSyncError,
    )

    private fun readFromDisk(): SyncConfigDto {
        if (!prefsFile.exists()) return SyncConfigDto()
        return runCatching {
            val raw = prefsFile.readText(StandardCharsets.UTF_8)
            DayNewJson.decodeFromString(SyncConfigDto.serializer(), raw)
        }.getOrDefault(SyncConfigDto())
    }

    private fun writeToDisk(dto: SyncConfigDto) {
        dataDir.mkdirs()
        val raw = DayNewJson.encodeToString(SyncConfigDto.serializer(), dto)
        FileOutputStream(tmpFile, false).use { out ->
            out.write(raw.toByteArray(StandardCharsets.UTF_8))
            out.flush()
            runCatching { out.fd.sync() }
        }
        Files.move(tmpFile.toPath(), prefsFile.toPath(), StandardCopyOption.REPLACE_EXISTING)
    }
}
