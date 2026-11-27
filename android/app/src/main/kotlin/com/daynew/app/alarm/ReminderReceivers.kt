package com.daynew.app.alarm

import android.app.AlarmManager
import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import com.daynew.app.DayNewApplication
import com.daynew.app.sync.SyncWorkScheduler

class ReminderAlarmReceiver : BroadcastReceiver() {
    override fun onReceive(context: Context, intent: Intent) {
        val scheduleId = intent.getStringExtra(ReminderNotificationHelper.EXTRA_SCHEDULE_ID).orEmpty()
        val title = intent.getStringExtra(ReminderNotificationHelper.EXTRA_TITLE) ?: "日程提醒"
        val content = intent.getStringExtra(ReminderNotificationHelper.EXTRA_CONTENT) ?: "点击查看详情"
        val notificationId = intent.getIntExtra(
            ReminderNotificationHelper.EXTRA_NOTIFICATION_ID,
            scheduleId.hashCode() and 0x7FFFFFFF,
        )

        val app = context.applicationContext as? DayNewApplication
        if (app != null && scheduleId.isNotBlank()) {
            val item = app.repository.getScheduleById(scheduleId) ?: return
            if (item.done) return
        }

        ReminderNotificationHelper.showReminderNotification(
            context = context,
            scheduleId = scheduleId,
            title = title,
            content = content,
            notificationId = notificationId,
        )
    }
}

class ReminderActionReceiver : BroadcastReceiver() {
    override fun onReceive(context: Context, intent: Intent) {
        val scheduleId = intent.getStringExtra(ReminderNotificationHelper.EXTRA_SCHEDULE_ID).orEmpty()
        val notificationId = intent.getIntExtra(ReminderNotificationHelper.EXTRA_NOTIFICATION_ID, -1)
        val app = context.applicationContext as? DayNewApplication

        when (intent.action) {
            ACTION_COMPLETE -> {
                if (app != null && scheduleId.isNotBlank()) {
                    val item = app.repository.getScheduleById(scheduleId)
                    if (item != null && !item.done) {
                        app.repository.toggleDone(scheduleId)
                        app.repository.flush()
                    }
                }
                if (notificationId >= 0) {
                    ReminderNotificationHelper.cancelNotification(context, notificationId)
                }
            }

            ACTION_SNOZE -> {
                val title = intent.getStringExtra(ReminderNotificationHelper.EXTRA_TITLE) ?: "延后提醒"
                val content = intent.getStringExtra(ReminderNotificationHelper.EXTRA_CONTENT) ?: "10 分钟延后到期"
                if (app != null && scheduleId.isNotBlank()) {
                    app.alarmScheduler.scheduleSnooze(scheduleId, title, content, delayMinutes = 10L)
                }
                if (notificationId >= 0) {
                    ReminderNotificationHelper.cancelNotification(context, notificationId)
                }
            }
        }
    }

    companion object {
        const val ACTION_COMPLETE = "com.daynew.app.action.REMINDER_COMPLETE"
        const val ACTION_SNOZE = "com.daynew.app.action.REMINDER_SNOOZE"
    }
}

class BootRescheduleReceiver : BroadcastReceiver() {
    override fun onReceive(context: Context, intent: Intent) {
        val action = intent.action ?: return
        if (action != Intent.ACTION_BOOT_COMPLETED &&
            action != Intent.ACTION_TIME_CHANGED &&
            action != Intent.ACTION_TIMEZONE_CHANGED &&
            action != AlarmManager.ACTION_SCHEDULE_EXACT_ALARM_PERMISSION_STATE_CHANGED
        ) {
            return
        }
        val app = context.applicationContext as? DayNewApplication ?: return
        app.alarmScheduler.rescheduleAll(app.repository.currentStore())
        if (app.syncPreferences.currentConfig().autoSyncEnabled && app.syncPreferences.isConfigured()) {
            SyncWorkScheduler.ensurePeriodic(app)
        }
    }
}
