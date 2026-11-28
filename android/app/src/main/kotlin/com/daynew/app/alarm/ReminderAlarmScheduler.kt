package com.daynew.app.alarm

import android.app.AlarmManager
import android.app.PendingIntent
import android.content.Context
import android.content.Intent
import android.os.Build
import android.os.PowerManager
import android.util.Log
import com.daynew.app.MainActivity
import com.daynew.sync.DayNewJson
import com.daynew.sync.StoreDto
import kotlinx.serialization.builtins.ListSerializer
import kotlinx.serialization.builtins.serializer
import java.io.File
import java.nio.charset.StandardCharsets
import java.time.LocalDateTime
import java.time.ZoneId

/**
 * 节点闹钟调度器：有精确闹钟权限时用 setExactAndAllowWhileIdle，未授权时自动降级 setAndAllowWhileIdle。
 */
class ReminderAlarmScheduler(
    private val context: Context,
    private val dataDir: File,
    private val nowProvider: () -> LocalDateTime = { LocalDateTime.now() },
) {
    private val stateFile = File(dataDir, "scheduled-alarms.json")
    private val lock = Any()

    fun canScheduleExactAlarms(): Boolean {
        if (Build.VERSION.SDK_INT < Build.VERSION_CODES.S) return true
        val am = context.getSystemService(AlarmManager::class.java) ?: return false
        return am.canScheduleExactAlarms()
    }

    fun isIgnoringBatteryOptimizations(): Boolean {
        val pm = context.getSystemService(PowerManager::class.java) ?: return true
        return pm.isIgnoringBatteryOptimizations(context.packageName)
    }

    fun rescheduleAll(store: StoreDto) {
        synchronized(lock) {
            val am = context.getSystemService(AlarmManager::class.java) ?: return
            cancelPreviousAlarms(am)

            val upcoming = ReminderTriggerPlanner.planUpcoming(store, nowProvider())
            val exactAllowed = canScheduleExactAlarms()
            val zoneId = ZoneId.systemDefault()
            val scheduledCodes = ArrayList<Int>(upcoming.size)

            for (item in upcoming) {
                val triggerMillis = item.triggerAt.atZone(zoneId).toInstant().toEpochMilli()
                val pending = buildAlarmPendingIntent(
                    requestCode = item.requestCode,
                    scheduleId = item.scheduleId,
                    title = item.title,
                    content = item.content,
                )
                schedulePlatformAlarm(am, triggerMillis, pending, exactAllowed)
                scheduledCodes += item.requestCode
            }

            saveScheduledCodes(scheduledCodes)
        }
    }

    fun scheduleSnooze(
        scheduleId: String,
        title: String,
        content: String,
        delayMinutes: Long = 10L,
    ) {
        synchronized(lock) {
            val am = context.getSystemService(AlarmManager::class.java) ?: return
            val triggerAt = nowProvider().plusMinutes(delayMinutes)
            val triggerMillis = triggerAt.atZone(ZoneId.systemDefault()).toInstant().toEpochMilli()
            val requestCode = ReminderTriggerPlanner.stableRequestCode("snooze|$scheduleId")
            val pending = buildAlarmPendingIntent(
                requestCode = requestCode,
                scheduleId = scheduleId,
                title = title,
                content = content,
            )
            schedulePlatformAlarm(am, triggerMillis, pending, canScheduleExactAlarms())
        }
    }

    fun scheduleTestBackgroundAlarm(delaySeconds: Long = 10L) {
        synchronized(lock) {
            val am = context.getSystemService(AlarmManager::class.java) ?: return
            val triggerAt = nowProvider().plusSeconds(delaySeconds.coerceAtLeast(1L))
            val triggerMillis = triggerAt.atZone(ZoneId.systemDefault()).toInstant().toEpochMilli()
            val requestCode = ReminderTriggerPlanner.stableRequestCode("bgtest|$TEST_ALARM_SCHEDULE_ID")
            val pending = buildAlarmPendingIntent(
                requestCode = requestCode,
                scheduleId = TEST_ALARM_SCHEDULE_ID,
                title = "日新后台提醒测试",
                content = "闹钟级唤醒正常：已于后台/息屏状态下准时触发",
            )
            schedulePlatformAlarm(am, triggerMillis, pending, canScheduleExactAlarms())
        }
    }

    private fun schedulePlatformAlarm(
        am: AlarmManager,
        triggerMillis: Long,
        pending: PendingIntent,
        exactAllowed: Boolean,
    ) {
        if (exactAllowed) {
            try {
                val info = AlarmManager.AlarmClockInfo(triggerMillis, buildShowActivityPendingIntent())
                am.setAlarmClock(info, pending)
                return
            } catch (e: SecurityException) {
                Log.w("DayNewAlarm", "setAlarmClock denied, falling back to setAndAllowWhileIdle", e)
            } catch (t: Throwable) {
                Log.w("DayNewAlarm", "setAlarmClock failed, falling back to setAndAllowWhileIdle", t)
            }
        }
        try {
            am.setAndAllowWhileIdle(AlarmManager.RTC_WAKEUP, triggerMillis, pending)
        } catch (t: Throwable) {
            Log.e("DayNewAlarm", "Failed to schedule fallback alarm", t)
        }
    }

    private fun buildShowActivityPendingIntent(): PendingIntent {
        val intent = Intent(context, MainActivity::class.java).apply {
            flags = Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_CLEAR_TOP
        }
        return PendingIntent.getActivity(
            context,
            SHOW_ACTIVITY_REQUEST_CODE,
            intent,
            PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE,
        )
    }

    companion object {
        const val TEST_ALARM_SCHEDULE_ID = "__bg_test__"
        private const val SHOW_ACTIVITY_REQUEST_CODE = 0x5A10
    }

    private fun cancelPreviousAlarms(am: AlarmManager) {
        val oldCodes = loadScheduledCodes()
        for (code in oldCodes) {
            val intent = Intent(context, ReminderAlarmReceiver::class.java)
            val existing = PendingIntent.getBroadcast(
                context,
                code,
                intent,
                PendingIntent.FLAG_NO_CREATE or PendingIntent.FLAG_IMMUTABLE,
            )
            if (existing != null) {
                am.cancel(existing)
                existing.cancel()
            }
        }
    }

    private fun buildAlarmPendingIntent(
        requestCode: Int,
        scheduleId: String,
        title: String,
        content: String,
    ): PendingIntent {
        val intent = Intent(context, ReminderAlarmReceiver::class.java).apply {
            putExtra(ReminderNotificationHelper.EXTRA_SCHEDULE_ID, scheduleId)
            putExtra(ReminderNotificationHelper.EXTRA_TITLE, title)
            putExtra(ReminderNotificationHelper.EXTRA_CONTENT, content)
            putExtra(ReminderNotificationHelper.EXTRA_NOTIFICATION_ID, requestCode)
        }
        return PendingIntent.getBroadcast(
            context,
            requestCode,
            intent,
            PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE,
        )
    }

    private fun loadScheduledCodes(): List<Int> {
        if (!stateFile.exists()) return emptyList()
        return runCatching {
            val raw = stateFile.readText(StandardCharsets.UTF_8)
            DayNewJson.decodeFromString(ListSerializer(Int.serializer()), raw)
        }.getOrDefault(emptyList())
    }

    private fun saveScheduledCodes(codes: List<Int>) {
        runCatching {
            dataDir.mkdirs()
            val raw = DayNewJson.encodeToString(ListSerializer(Int.serializer()), codes)
            stateFile.writeText(raw, StandardCharsets.UTF_8)
        }
    }
}
