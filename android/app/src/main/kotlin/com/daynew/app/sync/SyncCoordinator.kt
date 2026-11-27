package com.daynew.app.sync

import android.content.Context
import com.daynew.app.data.DayNewRepository
import com.daynew.sync.ConnectionTestResult
import com.daynew.sync.DeviceSnapshotDto
import com.daynew.sync.HlcClock
import com.daynew.sync.WebDavClient
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import kotlinx.coroutines.withContext
import java.io.IOException
import java.time.LocalDateTime
import java.time.format.DateTimeFormatter

sealed interface SyncExecutionResult {
    data class Success(
        val summary: String,
        val mergedUpdates: Int,
        val conflictCopies: Int,
        val pushedOwnSnapshot: Boolean,
    ) : SyncExecutionResult

    data class NotConfigured(val message: String) : SyncExecutionResult
    data class AuthFailure(val message: String) : SyncExecutionResult
    data class TransientFailure(val message: String) : SyncExecutionResult
}

/**
 * 协调 WebDAV 拉取 -> HLC 字段级合并 -> 推送本机快照 (.json.gz)，由互斥锁保护防止并发冲突。
 */
class SyncCoordinator(
    private val appContext: Context,
    private val repository: DayNewRepository,
    val preferencesStore: SyncPreferencesStore,
    private val clientFactory: () -> WebDavClient = { WebDavClient() },
) {
    private val syncMutex = Mutex()
    private val timeFormatter = DateTimeFormatter.ofPattern("MM-dd HH:mm:ss")

    suspend fun testConnection(
        folderUrl: String,
        username: String,
        passwordInput: String,
    ): ConnectionTestResult = withContext(Dispatchers.IO) {
        val resolvedPassword = passwordInput.ifEmpty { preferencesStore.readDecryptedPassword() }
        if (folderUrl.isBlank() || username.isBlank() || resolvedPassword.isEmpty()) {
            return@withContext ConnectionTestResult(false, "请填写完整的 WebDAV 地址、账号与应用密码")
        }
        runCatching {
            clientFactory().testConnection(folderUrl.trim(), username.trim(), resolvedPassword)
        }.getOrElse { ex ->
            ConnectionTestResult(false, ex.message ?: "连接异常")
        }
    }

    fun saveConfig(
        folderUrl: String,
        username: String,
        passwordInput: String,
        autoSyncEnabled: Boolean,
    ) {
        val encrypted = if (passwordInput.isNotEmpty()) {
            KeystoreCrypto.encrypt(passwordInput)
        } else {
            preferencesStore.currentConfig().encryptedPassword
        }
        val updated = preferencesStore.updateConfig { old ->
            val urlChanged = old.serverUrl.trim() != folderUrl.trim()
            old.copy(
                serverUrl = folderUrl.trim(),
                username = username.trim(),
                encryptedPassword = encrypted,
                autoSyncEnabled = autoSyncEnabled,
                peerEtags = if (urlChanged) emptyMap() else old.peerEtags,
                peerWatermarks = if (urlChanged) emptyMap() else old.peerWatermarks,
                lastSyncError = "",
            )
        }

        val ready = updated.serverUrl.isNotBlank() &&
            updated.username.isNotBlank() &&
            updated.encryptedPassword.isNotBlank()
        if (updated.autoSyncEnabled && ready) {
            SyncWorkScheduler.ensurePeriodic(appContext)
        } else {
            SyncWorkScheduler.cancelPeriodic(appContext)
        }
    }

    fun setAutoSyncEnabled(enabled: Boolean) {
        val updated = preferencesStore.updateConfig { it.copy(autoSyncEnabled = enabled) }
        val ready = updated.serverUrl.isNotBlank() &&
            updated.username.isNotBlank() &&
            updated.encryptedPassword.isNotBlank()
        if (enabled && ready) {
            SyncWorkScheduler.ensurePeriodic(appContext)
        } else {
            SyncWorkScheduler.cancelPeriodic(appContext)
        }
    }

    suspend fun syncNow(forcePush: Boolean = false): SyncExecutionResult = syncMutex.withLock {
        val cfg = preferencesStore.currentConfig()
        val password = preferencesStore.readDecryptedPassword()
        if (cfg.serverUrl.isBlank() || cfg.username.isBlank() || password.isEmpty()) {
            return@withLock SyncExecutionResult.NotConfigured("尚未配置 WebDAV 同步信息")
        }

        preferencesStore.setSyncing(true)
        try {
            withContext(Dispatchers.IO) {
                val client = clientFactory()
                val pullBundle = client.pullRemoteSnapshots(
                    folderUrl = cfg.serverUrl,
                    user = cfg.username,
                    password = password,
                    ownDeviceId = repository.deviceId,
                    cachedPeerEtags = cfg.peerEtags,
                )

                val nextEtags = cfg.peerEtags.toMutableMap()
                val nextWatermarks = cfg.peerWatermarks.toMutableMap()
                var totalMergedUpdates = 0
                var totalConflicts = 0

                for (peer in pullBundle.peers) {
                    val peerKey = peer.snapshot.deviceId.ifBlank { peer.fileName }
                    val lastWatermark = nextWatermarks[peerKey]
                    val report = repository.applyRemoteSnapshot(
                        remoteStore = peer.snapshot.store,
                        remoteCompletions = peer.snapshot.completions,
                        lastSyncedRemoteHlc = lastWatermark,
                    )
                    totalMergedUpdates += report.updatedCount
                    totalConflicts += report.conflictCopiesCount
                    if (peer.eTag.isNotEmpty()) {
                        nextEtags[peer.fileName] = peer.eTag
                    }
                    val nextHlc = HlcClock.max(
                        lastWatermark.orEmpty(),
                        HlcClock.max(report.maxRemoteHlc, peer.snapshot.snapshotHlc),
                    )
                    if (nextHlc.isNotEmpty()) {
                        nextWatermarks[peerKey] = nextHlc
                    }
                }

                val currentStore = repository.currentStore()
                val currentCompletions = repository.readCompletions()
                val fingerprint = WebDavClient.computeStoreFingerprint(currentStore, currentCompletions.size)
                val needPush = forcePush ||
                    !pullBundle.ownFileExistsOnServer ||
                    fingerprint != cfg.lastSyncedFingerprint ||
                    totalMergedUpdates > 0

                var pushed = false
                if (needPush) {
                    val snapshot = DeviceSnapshotDto(
                        version = 1,
                        deviceId = repository.deviceId,
                        snapshotHlc = repository.clock.tick(),
                        store = currentStore,
                        completions = currentCompletions,
                    )
                    client.pushDeviceSnapshot(
                        folderUrl = cfg.serverUrl,
                        user = cfg.username,
                        password = password,
                        snapshot = snapshot,
                    )
                    pushed = true
                }

                val nowLabel = LocalDateTime.now().format(timeFormatter)
                val summary = buildSummary(
                    remoteDeviceCount = pullBundle.remoteDeviceFilesCount,
                    mergedUpdates = totalMergedUpdates,
                    conflicts = totalConflicts,
                    pushed = pushed,
                )
                preferencesStore.updateConfig { latest ->
                    latest.copy(
                        peerEtags = nextEtags,
                        peerWatermarks = nextWatermarks,
                        lastSyncedFingerprint = fingerprint,
                        lastSyncAt = nowLabel,
                        lastSyncSummary = summary,
                        lastSyncError = "",
                    )
                }

                SyncExecutionResult.Success(
                    summary = summary,
                    mergedUpdates = totalMergedUpdates,
                    conflictCopies = totalConflicts,
                    pushedOwnSnapshot = pushed,
                )
            }
        } catch (sec: SecurityException) {
            val msg = sec.message ?: "WebDAV 鉴权失败"
            preferencesStore.updateConfig { it.copy(lastSyncError = msg) }
            SyncExecutionResult.AuthFailure(msg)
        } catch (io: IOException) {
            val msg = io.message ?: "网络异常，稍后自动重试"
            preferencesStore.updateConfig { it.copy(lastSyncError = msg) }
            SyncExecutionResult.TransientFailure(msg)
        } catch (ex: Exception) {
            val msg = ex.message ?: ex.javaClass.simpleName
            preferencesStore.updateConfig { it.copy(lastSyncError = msg) }
            SyncExecutionResult.TransientFailure(msg)
        } finally {
            preferencesStore.setSyncing(false)
        }
    }

    private fun buildSummary(
        remoteDeviceCount: Int,
        mergedUpdates: Int,
        conflicts: Int,
        pushed: Boolean,
    ): String {
        val parts = mutableListOf<String>()
        if (remoteDeviceCount == 0) {
            parts += "远端暂无其它设备"
        } else {
            parts += "发现 $remoteDeviceCount 台设备"
        }
        if (mergedUpdates > 0) {
            parts += "合并 $mergedUpdates 项改动"
        }
        if (conflicts > 0) {
            parts += "生成 $conflicts 个冲突副本"
        }
        if (pushed) {
            parts += "已推送本机快照"
        } else if (mergedUpdates == 0) {
            parts += "已是最新"
        }
        return parts.joinToString(" · ")
    }
}
