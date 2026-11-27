package com.daynew.app.sync

import android.content.Context
import androidx.work.Constraints
import androidx.work.CoroutineWorker
import androidx.work.ExistingPeriodicWorkPolicy
import androidx.work.ExistingWorkPolicy
import androidx.work.NetworkType
import androidx.work.OneTimeWorkRequestBuilder
import androidx.work.PeriodicWorkRequestBuilder
import androidx.work.WorkManager
import androidx.work.WorkerParameters
import com.daynew.app.DayNewApplication
import java.util.concurrent.TimeUnit

class WebDavSyncWorker(
    appContext: Context,
    workerParams: WorkerParameters,
) : CoroutineWorker(appContext, workerParams) {

    override suspend fun doWork(): Result {
        val app = applicationContext as? DayNewApplication ?: return Result.failure()
        val cfg = app.syncPreferences.currentConfig()
        if (!cfg.autoSyncEnabled && !inputData.getBoolean(KEY_MANUAL, false)) {
            return Result.success()
        }
        return when (app.syncCoordinator.syncNow()) {
            is SyncExecutionResult.Success -> Result.success()
            is SyncExecutionResult.NotConfigured,
            is SyncExecutionResult.AuthFailure -> Result.failure()
            is SyncExecutionResult.TransientFailure -> {
                if (runAttemptCount < 3) Result.retry() else Result.failure()
            }
        }
    }

    companion object {
        const val KEY_MANUAL = "manual"
    }
}

object SyncWorkScheduler {
    private const val PERIODIC_WORK_NAME = "daynew_webdav_periodic_sync"
    private const val DEBOUNCED_WORK_NAME = "daynew_webdav_debounced_sync"

    private fun connectedConstraints(): Constraints = Constraints.Builder()
        .setRequiredNetworkType(NetworkType.CONNECTED) // 用户已确认允许移动网络与 Wi-Fi
        .build()

    fun ensurePeriodic(context: Context) {
        val request = PeriodicWorkRequestBuilder<WebDavSyncWorker>(15L, TimeUnit.MINUTES)
            .setConstraints(connectedConstraints())
            .build()
        WorkManager.getInstance(context.applicationContext).enqueueUniquePeriodicWork(
            PERIODIC_WORK_NAME,
            ExistingPeriodicWorkPolicy.UPDATE,
            request,
        )
    }

    fun enqueueDebounced(context: Context, delaySeconds: Long = 5L) {
        val request = OneTimeWorkRequestBuilder<WebDavSyncWorker>()
            .setConstraints(connectedConstraints())
            .setInitialDelay(delaySeconds.coerceAtLeast(0L), TimeUnit.SECONDS)
            .build()
        WorkManager.getInstance(context.applicationContext).enqueueUniqueWork(
            DEBOUNCED_WORK_NAME,
            ExistingWorkPolicy.REPLACE,
            request,
        )
    }

    fun cancelPeriodic(context: Context) {
        val wm = WorkManager.getInstance(context.applicationContext)
        wm.cancelUniqueWork(PERIODIC_WORK_NAME)
        wm.cancelUniqueWork(DEBOUNCED_WORK_NAME)
    }
}
