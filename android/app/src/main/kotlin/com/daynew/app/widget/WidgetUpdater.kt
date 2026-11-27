package com.daynew.app.widget

import android.content.Context
import androidx.glance.appwidget.updateAll
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.launch

object WidgetUpdater {
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.IO)

    fun refreshAllAsync(context: Context) {
        val appContext = context.applicationContext
        scope.launch {
            refreshAll(appContext)
        }
    }

    suspend fun refreshAll(context: Context) {
        val appContext = context.applicationContext
        runCatching { TodayGlanceWidget().updateAll(appContext) }
        runCatching { QuickCaptureGlanceWidget().updateAll(appContext) }
    }
}
