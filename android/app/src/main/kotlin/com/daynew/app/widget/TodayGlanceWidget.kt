package com.daynew.app.widget

import android.content.Context
import android.content.Intent
import androidx.compose.runtime.Composable
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.glance.GlanceId
import androidx.glance.GlanceModifier
import androidx.glance.action.actionParametersOf
import androidx.glance.action.clickable
import androidx.glance.appwidget.GlanceAppWidget
import androidx.glance.appwidget.GlanceAppWidgetReceiver
import androidx.glance.appwidget.action.actionRunCallback
import androidx.glance.appwidget.action.actionStartActivity
import androidx.glance.appwidget.cornerRadius
import androidx.glance.appwidget.lazy.LazyColumn
import androidx.glance.appwidget.lazy.items
import androidx.glance.appwidget.provideContent
import androidx.glance.background
import androidx.glance.color.ColorProvider
import androidx.glance.layout.Alignment
import androidx.glance.layout.Box
import androidx.glance.layout.Column
import androidx.glance.layout.Row
import androidx.glance.layout.Spacer
import androidx.glance.layout.fillMaxSize
import androidx.glance.layout.fillMaxWidth
import androidx.glance.layout.height
import androidx.glance.layout.padding
import androidx.glance.layout.size
import androidx.glance.layout.width
import androidx.glance.text.FontWeight
import androidx.glance.text.Text
import androidx.glance.text.TextStyle
import com.daynew.app.DayNewApplication
import com.daynew.app.MainActivity
import com.daynew.app.alarm.ReminderNotificationHelper
import com.daynew.app.data.LaneKind
import com.daynew.app.data.LaneRules
import com.daynew.app.share.ShareToDayNewActivity
import com.daynew.app.ui.theme.DarkNordPalette
import com.daynew.app.ui.theme.LightNordPalette
import com.daynew.sync.DeadlineRules
import com.daynew.sync.ScheduleDto
import java.time.LocalDateTime

internal object NordWidgetColors {
    val surface0 = ColorProvider(LightNordPalette.surface0, DarkNordPalette.surface0)
    val surface2 = ColorProvider(LightNordPalette.surface2, DarkNordPalette.surface2)
    val surface3 = ColorProvider(LightNordPalette.surface3, DarkNordPalette.surface3)
    val borderSubtle = ColorProvider(LightNordPalette.borderSubtle, DarkNordPalette.borderSubtle)
    val accent = ColorProvider(LightNordPalette.accent, DarkNordPalette.accent)
    val onAccent = ColorProvider(LightNordPalette.onAccent, DarkNordPalette.onAccent)
    val textPrimary = ColorProvider(LightNordPalette.textPrimary, DarkNordPalette.textPrimary)
    val textSecondary = ColorProvider(LightNordPalette.textSecondary, DarkNordPalette.textSecondary)
    val danger = ColorProvider(LightNordPalette.danger, DarkNordPalette.danger)
    val dangerBg = ColorProvider(Color(0x1EBF616A), Color(0x2ED6747D))
}

private data class WidgetTaskItem(
    val dto: ScheduleDto,
    val isOverdue: Boolean,
    val timeLabel: String?,
)

class TodayGlanceWidget : GlanceAppWidget() {
    override suspend fun provideGlance(context: Context, id: GlanceId) {
        val app = context.applicationContext as? DayNewApplication
        val now = LocalDateTime.now()
        val store = app?.repository?.currentStore()
        val active = store?.schedules?.filter { !it.del }.orEmpty()

        val overdueItems = active
            .filter { !it.done && LaneRules.laneFor(it, now) == LaneKind.OVERDUE }
            .sortedWith(compareBy<ScheduleDto> { it.due ?: "" }.thenBy { it.order })
            .map { WidgetTaskItem(it, isOverdue = true, timeLabel = formatTime(it)) }

        val todayItems = active
            .filter { !it.done && LaneRules.laneFor(it, now) == LaneKind.TODAY }
            .sortedWith(compareBy<ScheduleDto> { it.order }.thenBy { it.id })
            .map { WidgetTaskItem(it, isOverdue = false, timeLabel = formatTime(it)) }

        val visibleItems = (overdueItems + todayItems).take(12)
        val totalPending = overdueItems.size + todayItems.size

        provideContent {
            TodayWidgetContent(
                context = context,
                items = visibleItems,
                overdueCount = overdueItems.size,
                totalPending = totalPending,
            )
        }
    }

    private fun formatTime(item: ScheduleDto): String? {
        val start = item.start
        val end = item.end
        if (!start.isNullOrBlank()) {
            return if (!end.isNullOrBlank()) "$start-$end" else start
        }
        val (_, dueTime) = DeadlineRules.parseDue(item.due)
        return dueTime?.let { DeadlineRules.formatClock(it) }
    }
}

@Composable
private fun TodayWidgetContent(
    context: Context,
    items: List<WidgetTaskItem>,
    overdueCount: Int,
    totalPending: Int,
) {
    val openAppIntent = Intent(context, MainActivity::class.java).apply {
        flags = Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_CLEAR_TOP
    }
    val quickCreateIntent = Intent(context, ShareToDayNewActivity::class.java).apply {
        action = ShareToDayNewActivity.ACTION_QUICK_CAPTURE
        flags = Intent.FLAG_ACTIVITY_NEW_TASK
    }

    Column(
        modifier = GlanceModifier
            .fillMaxSize()
            .background(NordWidgetColors.surface0)
            .padding(12.dp),
    ) {
        Row(
            modifier = GlanceModifier
                .fillMaxWidth()
                .clickable(actionStartActivity(openAppIntent))
                .padding(bottom = 8.dp),
            verticalAlignment = Alignment.CenterVertically,
        ) {
            Text(
                text = "今日待办",
                style = TextStyle(
                    color = NordWidgetColors.textPrimary,
                    fontSize = 15.sp,
                    fontWeight = FontWeight.Bold,
                ),
            )
            Spacer(modifier = GlanceModifier.width(6.dp))
            Text(
                text = "$totalPending",
                style = TextStyle(
                    color = NordWidgetColors.textSecondary,
                    fontSize = 13.sp,
                    fontWeight = FontWeight.Medium,
                ),
            )
            if (overdueCount > 0) {
                Spacer(modifier = GlanceModifier.width(6.dp))
                Box(
                    modifier = GlanceModifier
                        .background(NordWidgetColors.dangerBg)
                        .cornerRadius(6.dp)
                        .padding(horizontal = 6.dp, vertical = 2.dp),
                ) {
                    Text(
                        text = "逾期 $overdueCount",
                        style = TextStyle(
                            color = NordWidgetColors.danger,
                            fontSize = 11.sp,
                            fontWeight = FontWeight.Medium,
                        ),
                    )
                }
            }
            Spacer(modifier = GlanceModifier.defaultWeight())
            Box(
                modifier = GlanceModifier
                    .size(28.dp)
                    .background(NordWidgetColors.accent)
                    .cornerRadius(8.dp)
                    .clickable(actionStartActivity(quickCreateIntent)),
                contentAlignment = Alignment.Center,
            ) {
                Text(
                    text = "+",
                    style = TextStyle(
                        color = NordWidgetColors.onAccent,
                        fontSize = 16.sp,
                        fontWeight = FontWeight.Bold,
                    ),
                )
            }
        }

        if (items.isEmpty()) {
            Box(
                modifier = GlanceModifier
                    .fillMaxSize()
                    .background(NordWidgetColors.surface2)
                    .cornerRadius(8.dp)
                    .clickable(actionStartActivity(openAppIntent))
                    .padding(12.dp),
                contentAlignment = Alignment.Center,
            ) {
                Text(
                    text = "今日待办已清空，点右上角 + 新建",
                    style = TextStyle(
                        color = NordWidgetColors.textSecondary,
                        fontSize = 12.sp,
                    ),
                )
            }
        } else {
            LazyColumn(
                modifier = GlanceModifier.fillMaxSize(),
            ) {
                items(items, itemId = { it.dto.id.hashCode().toLong() }) { item ->
                    Column(modifier = GlanceModifier.fillMaxWidth()) {
                        TodayWidgetRow(context = context, item = item)
                        Spacer(modifier = GlanceModifier.height(6.dp))
                    }
                }
            }
        }
    }
}

@Composable
private fun TodayWidgetRow(
    context: Context,
    item: WidgetTaskItem,
) {
    val editIntent = Intent(context, MainActivity::class.java).apply {
        flags = Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_CLEAR_TOP
        putExtra(ReminderNotificationHelper.EXTRA_SCHEDULE_ID, item.dto.id)
    }

    Row(
        modifier = GlanceModifier
            .fillMaxWidth()
            .background(NordWidgetColors.surface2)
            .cornerRadius(8.dp)
            .padding(horizontal = 10.dp, vertical = 8.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Box(
            modifier = GlanceModifier
                .size(22.dp)
                .background(if (item.isOverdue) NordWidgetColors.dangerBg else NordWidgetColors.surface3)
                .cornerRadius(6.dp)
                .clickable(
                    actionRunCallback<ToggleTaskActionCallback>(
                        actionParametersOf(ToggleTaskActionCallback.KEY_SCHEDULE_ID to item.dto.id),
                    ),
                ),
            contentAlignment = Alignment.Center,
        ) {
            Text(
                text = "○",
                style = TextStyle(
                    color = if (item.isOverdue) NordWidgetColors.danger else NordWidgetColors.accent,
                    fontSize = 13.sp,
                    fontWeight = FontWeight.Bold,
                ),
            )
        }
        Spacer(modifier = GlanceModifier.width(8.dp))
        Column(
            modifier = GlanceModifier
                .defaultWeight()
                .clickable(actionStartActivity(editIntent)),
        ) {
            Text(
                text = item.dto.title.ifBlank { "未命名日程" },
                maxLines = 1,
                style = TextStyle(
                    color = NordWidgetColors.textPrimary,
                    fontSize = 13.sp,
                    fontWeight = FontWeight.Medium,
                ),
            )
            if (item.isOverdue || item.timeLabel != null) {
                Row(verticalAlignment = Alignment.CenterVertically) {
                    if (item.isOverdue) {
                        Text(
                            text = "逾期",
                            style = TextStyle(
                                color = NordWidgetColors.danger,
                                fontSize = 11.sp,
                                fontWeight = FontWeight.Medium,
                            ),
                        )
                        if (item.timeLabel != null) {
                            Spacer(modifier = GlanceModifier.width(6.dp))
                        }
                    }
                    if (item.timeLabel != null) {
                        Text(
                            text = item.timeLabel,
                            style = TextStyle(
                                color = NordWidgetColors.textSecondary,
                                fontSize = 11.sp,
                            ),
                        )
                    }
                }
            }
        }
    }
}

class TodayGlanceWidgetReceiver : GlanceAppWidgetReceiver() {
    override val glanceAppWidget: GlanceAppWidget = TodayGlanceWidget()
}
