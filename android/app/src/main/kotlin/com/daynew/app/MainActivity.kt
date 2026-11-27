package com.daynew.app

import android.content.Intent
import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.lifecycle.ViewModelProvider
import com.daynew.app.alarm.ReminderNotificationHelper
import com.daynew.app.data.LaneKind
import com.daynew.app.sync.SyncWorkScheduler
import com.daynew.app.ui.main.BottomTab
import com.daynew.app.ui.main.DayNewAppScreen
import com.daynew.app.ui.main.MainScreenViewModel

class MainActivity : ComponentActivity() {
    private val app: DayNewApplication
        get() = applicationContext as DayNewApplication

    private lateinit var viewModel: MainScreenViewModel

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        enableEdgeToEdge()
        viewModel = ViewModelProvider(
            this,
            MainScreenViewModel.factory(
                repository = app.repository,
                syncCoordinator = app.syncCoordinator,
                alarmScheduler = app.alarmScheduler,
            ),
        )[MainScreenViewModel::class.java]

        handleLaunchIntent(intent)

        setContent {
            DayNewAppScreen(viewModel = viewModel)
        }
    }

    override fun onStart() {
        super.onStart()
        viewModel.refreshPermissionsState()
        if (app.syncPreferences.currentConfig().autoSyncEnabled && app.syncPreferences.isConfigured()) {
            SyncWorkScheduler.enqueueDebounced(this, delaySeconds = 0L)
        }
    }

    override fun onNewIntent(intent: Intent) {
        super.onNewIntent(intent)
        setIntent(intent)
        handleLaunchIntent(intent)
    }

    override fun onStop() {
        super.onStop()
        app.repository.flush()
    }

    private fun handleLaunchIntent(intent: Intent?) {
        if (intent == null) return
        val laneName = intent.getStringExtra(EXTRA_OPEN_LANE)
        if (!laneName.isNullOrBlank()) {
            runCatching { LaneKind.valueOf(laneName) }.getOrNull()?.let { lane ->
                viewModel.selectTab(BottomTab.OVERVIEW)
                viewModel.selectLane(lane)
            }
            intent.removeExtra(EXTRA_OPEN_LANE)
        }

        val scheduleId = intent.getStringExtra(ReminderNotificationHelper.EXTRA_SCHEDULE_ID)
        if (!scheduleId.isNullOrBlank()) {
            viewModel.selectTab(BottomTab.OVERVIEW)
            viewModel.openEditSheet(scheduleId)
            intent.removeExtra(ReminderNotificationHelper.EXTRA_SCHEDULE_ID)
        }

        if (intent.getBooleanExtra(EXTRA_OPEN_CREATE, false)) {
            viewModel.selectTab(BottomTab.OVERVIEW)
            viewModel.openCreateSheet(LaneKind.TODAY)
            intent.removeExtra(EXTRA_OPEN_CREATE)
        }
    }

    companion object {
        const val EXTRA_OPEN_LANE = "extra_open_lane"
        const val EXTRA_OPEN_CREATE = "extra_open_create"
    }
}
