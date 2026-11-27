package com.daynew.app.alarm

import android.Manifest
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.content.Context
import android.content.Intent
import android.content.pm.PackageManager
import android.os.Build
import androidx.core.app.NotificationCompat
import androidx.core.app.NotificationManagerCompat
import androidx.core.content.ContextCompat
import com.daynew.app.MainActivity
import com.daynew.app.R

object ReminderNotificationHelper {
    const val CHANNEL_ID = "daynew_reminders"
    const val EXTRA_SCHEDULE_ID = "extra_schedule_id"
    const val EXTRA_NOTIFICATION_ID = "extra_notification_id"
    const val EXTRA_TITLE = "extra_title"
    const val EXTRA_CONTENT = "extra_content"
    const val EXTRA_IS_TEST = "extra_is_test"

    fun ensureChannel(context: Context) {
        val manager = context.getSystemService(NotificationManager::class.java) ?: return
        if (manager.getNotificationChannel(CHANNEL_ID) != null) return
        val channel = NotificationChannel(
            CHANNEL_ID,
            "日程节点提醒",
            NotificationManager.IMPORTANCE_HIGH,
        ).apply {
            description = "日程提前 15 分钟、提前 5 分钟、开始与结束/截止节点提醒"
            enableVibration(true)
        }
        manager.createNotificationChannel(channel)
    }

    fun hasNotificationPermission(context: Context): Boolean {
        if (Build.VERSION.SDK_INT < Build.VERSION_CODES.TIRAMISU) return true
        return ContextCompat.checkSelfPermission(
            context,
            Manifest.permission.POST_NOTIFICATIONS,
        ) == PackageManager.PERMISSION_GRANTED
    }

    fun showReminderNotification(
        context: Context,
        scheduleId: String,
        title: String,
        content: String,
        notificationId: Int,
        isTest: Boolean = false,
    ) {
        ensureChannel(context)
        if (!hasNotificationPermission(context)) return

        val openIntent = Intent(context, MainActivity::class.java).apply {
            flags = Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_CLEAR_TOP
            if (!isTest && scheduleId.isNotBlank()) {
                putExtra(EXTRA_SCHEDULE_ID, scheduleId)
            }
        }
        val contentPendingIntent = PendingIntent.getActivity(
            context,
            notificationId,
            openIntent,
            PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE,
        )

        val builder = NotificationCompat.Builder(context, CHANNEL_ID)
            .setSmallIcon(R.mipmap.ic_launcher)
            .setContentTitle(title)
            .setContentText(content)
            .setPriority(NotificationCompat.PRIORITY_HIGH)
            .setCategory(NotificationCompat.CATEGORY_REMINDER)
            .setAutoCancel(true)
            .setContentIntent(contentPendingIntent)

        if (!isTest && scheduleId.isNotBlank()) {
            val completeIntent = Intent(context, ReminderActionReceiver::class.java).apply {
                action = ReminderActionReceiver.ACTION_COMPLETE
                putExtra(EXTRA_SCHEDULE_ID, scheduleId)
                putExtra(EXTRA_NOTIFICATION_ID, notificationId)
            }
            val completePending = PendingIntent.getBroadcast(
                context,
                notificationId xor 0x1001,
                completeIntent,
                PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE,
            )

            val snoozeIntent = Intent(context, ReminderActionReceiver::class.java).apply {
                action = ReminderActionReceiver.ACTION_SNOZE
                putExtra(EXTRA_SCHEDULE_ID, scheduleId)
                putExtra(EXTRA_NOTIFICATION_ID, notificationId)
                putExtra(EXTRA_TITLE, title)
                putExtra(EXTRA_CONTENT, content)
            }
            val snoozePending = PendingIntent.getBroadcast(
                context,
                notificationId xor 0x2002,
                snoozeIntent,
                PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE,
            )

            builder.addAction(0, "完成", completePending)
            builder.addAction(0, "延后 10 分钟", snoozePending)
        }

        runCatching {
            NotificationManagerCompat.from(context).notify(notificationId, builder.build())
        }
    }

    fun showTestNotification(context: Context) {
        showReminderNotification(
            context = context,
            scheduleId = "",
            title = "5 分钟后开始 · 日新测试提醒",
            content = "通知通道正常。实际日程提醒支持「完成」与「延后 10 分钟」操作。",
            notificationId = 90001,
            isTest = true,
        )
    }

    fun cancelNotification(context: Context, notificationId: Int) {
        NotificationManagerCompat.from(context).cancel(notificationId)
    }
}
