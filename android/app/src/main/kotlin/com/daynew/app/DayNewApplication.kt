package com.daynew.app

import android.app.Application
import com.daynew.app.alarm.ReminderAlarmScheduler
import com.daynew.app.alarm.ReminderNotificationHelper
import com.daynew.app.data.DayNewRepository
import com.daynew.app.sync.SyncCoordinator
import com.daynew.app.sync.SyncPreferencesStore
import com.daynew.app.sync.SyncWorkScheduler
import com.daynew.app.widget.WidgetUpdater
import java.io.File

class DayNewApplication : Application() {
    val storageDir: File by lazy {
        File(applicationContext.filesDir, "daynew")
    }

    val syncPreferences: SyncPreferencesStore by lazy {
        SyncPreferencesStore(storageDir)
    }

    val alarmScheduler: ReminderAlarmScheduler by lazy {
        ReminderAlarmScheduler(applicationContext, storageDir)
    }

    val repository: DayNewRepository by lazy {
        DayNewRepository(dataDir = storageDir)
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
            alarmScheduler.rescheduleAll(store)
            WidgetUpdater.refreshAllAsync(this)
            if (fromLocalMutation && syncPreferences.currentConfig().autoSyncEnabled && syncPreferences.isConfigured()) {
                SyncWorkScheduler.enqueueDebounced(this, delaySeconds = 5L)
            }
        }

        alarmScheduler.rescheduleAll(repository.currentStore())
        WidgetUpdater.refreshAllAsync(this)

        if (syncPreferences.currentConfig().autoSyncEnabled && syncPreferences.isConfigured()) {
            SyncWorkScheduler.ensurePeriodic(this)
        }
    }
}
