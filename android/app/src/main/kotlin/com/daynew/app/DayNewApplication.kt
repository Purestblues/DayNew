package com.daynew.app

import android.app.Application
import com.daynew.app.alarm.ReminderAlarmScheduler
import com.daynew.app.alarm.ReminderNotificationHelper
import com.daynew.app.data.DayNewRepository
import com.daynew.app.data.UiPreferencesStore
import com.daynew.app.sync.SyncCoordinator
import com.daynew.app.sync.SyncPreferencesStore
import com.daynew.app.sync.SyncWorkScheduler
import com.daynew.app.widget.WidgetUpdater
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import java.io.File

class DayNewApplication : Application() {
    val appScope: CoroutineScope = CoroutineScope(SupervisorJob() + Dispatchers.IO)

    val storageDir: File by lazy {
        File(applicationContext.filesDir, "daynew")
    }

    val uiPreferences: UiPreferencesStore by lazy {
        UiPreferencesStore(applicationContext)
    }

    val syncPreferences: SyncPreferencesStore by lazy {
        SyncPreferencesStore(storageDir)
    }

    val alarmScheduler: ReminderAlarmScheduler by lazy {
        ReminderAlarmScheduler(applicationContext, storageDir)
    }

    val repository: DayNewRepository by lazy {
        DayNewRepository(dataDir = storageDir, scope = appScope)
    }

    val syncCoordinator: SyncCoordinator by lazy {
        SyncCoordinator(
            appContext = applicationContext,
            repository = repository,
            preferencesStore = syncPreferences,
        )
    }

    override fun onCreate() {
        super.onCreate()
        ReminderNotificationHelper.ensureChannel(this)

        repository.onStoreChanged = { store, fromLocalMutation ->
            appScope.launch {
                alarmScheduler.rescheduleAll(store)
                WidgetUpdater.refreshAllAsync(this@DayNewApplication)
                if (fromLocalMutation && syncPreferences.currentConfig().autoSyncEnabled && syncPreferences.isConfigured()) {
                    SyncWorkScheduler.enqueueDebounced(this@DayNewApplication, delaySeconds = 5L)
                }
            }
        }

        // 首帧之后再于后台 IO 线程重排闹钟、解密 Keystore 同步配置及初始化 WorkManager，避免阻塞冷启动
        appScope.launch {
            delay(450L)
            alarmScheduler.rescheduleAll(repository.currentStore())
            WidgetUpdater.refreshAllAsync(this@DayNewApplication)
            if (syncPreferences.currentConfig().autoSyncEnabled && syncPreferences.isConfigured()) {
                SyncWorkScheduler.ensurePeriodic(this@DayNewApplication)
            }
        }
    }
}
