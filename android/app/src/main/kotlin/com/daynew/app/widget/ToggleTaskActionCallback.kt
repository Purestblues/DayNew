package com.daynew.app.widget

import android.content.Context
import androidx.glance.GlanceId
import androidx.glance.action.ActionParameters
import androidx.glance.appwidget.action.ActionCallback
import com.daynew.app.DayNewApplication

class ToggleTaskActionCallback : ActionCallback {
    override suspend fun onAction(
        context: Context,
        glanceId: GlanceId,
        parameters: ActionParameters,
    ) {
        val scheduleId = parameters[KEY_SCHEDULE_ID] ?: return
        val app = context.applicationContext as? DayNewApplication ?: return
        app.repository.toggleDone(scheduleId)
        app.repository.flush()
        WidgetUpdater.refreshAll(app)
    }

    companion object {
        val KEY_SCHEDULE_ID = ActionParameters.Key<String>("schedule_id")
    }
}
