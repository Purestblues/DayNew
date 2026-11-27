package com.daynew.app.widget

import android.content.Context
import android.content.Intent
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.glance.GlanceId
import androidx.glance.GlanceModifier
import androidx.glance.action.clickable
import androidx.glance.appwidget.GlanceAppWidget
import androidx.glance.appwidget.GlanceAppWidgetReceiver
import androidx.glance.appwidget.action.actionStartActivity
import androidx.glance.appwidget.cornerRadius
import androidx.glance.appwidget.provideContent
import androidx.glance.background
import androidx.glance.layout.Alignment
import androidx.glance.layout.Box
import androidx.glance.layout.Column
import androidx.glance.layout.Row
import androidx.glance.layout.Spacer
import androidx.glance.layout.fillMaxSize
import androidx.glance.layout.padding
import androidx.glance.layout.size
import androidx.glance.layout.width
import androidx.glance.text.FontWeight
import androidx.glance.text.Text
import androidx.glance.text.TextStyle
import com.daynew.app.DayNewApplication
import com.daynew.app.data.LaneKind
import com.daynew.app.data.LaneRules
import com.daynew.app.share.ShareToDayNewActivity
import java.time.LocalDateTime

class QuickCaptureGlanceWidget : GlanceAppWidget() {
    override suspend fun provideGlance(context: Context, id: GlanceId) {
        val app = context.applicationContext as? DayNewApplication
        val now = LocalDateTime.now()
        val schedules = app?.repository?.currentStore()?.schedules.orEmpty()
        val pendingToday = schedules.count { item ->
            !item.del && !item.done &&
                (LaneRules.laneFor(item, now) == LaneKind.TODAY || LaneRules.laneFor(item, now) == LaneKind.OVERDUE)
        }

        val quickCreateIntent = Intent(context, ShareToDayNewActivity::class.java).apply {
            action = ShareToDayNewActivity.ACTION_QUICK_CAPTURE
            flags = Intent.FLAG_ACTIVITY_NEW_TASK
        }

        provideContent {
            Row(
                modifier = GlanceModifier
                    .fillMaxSize()
                    .background(NordWidgetColors.surface2)
                    .cornerRadius(12.dp)
                    .clickable(actionStartActivity(quickCreateIntent))
                    .padding(horizontal = 12.dp, vertical = 10.dp),
                verticalAlignment = Alignment.CenterVertically,
            ) {
                Box(
                    modifier = GlanceModifier
                        .size(30.dp)
                        .background(NordWidgetColors.accent)
                        .cornerRadius(8.dp),
                    contentAlignment = Alignment.Center,
                ) {
                    Text(
                        text = "+",
                        style = TextStyle(
                            color = NordWidgetColors.onAccent,
                            fontSize = 18.sp,
                            fontWeight = FontWeight.Bold,
                        ),
                    )
                }
                Spacer(modifier = GlanceModifier.width(10.dp))
                Column(modifier = GlanceModifier.defaultWeight()) {
                    Text(
                        text = "快速新建日程",
                        maxLines = 1,
                        style = TextStyle(
                            color = NordWidgetColors.textPrimary,
                            fontSize = 13.sp,
                            fontWeight = FontWeight.Bold,
                        ),
                    )
                    Text(
                        text = if (pendingToday > 0) "今日待办 $pendingToday 项" else "今日已清空",
                        maxLines = 1,
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

class QuickCaptureGlanceWidgetReceiver : GlanceAppWidgetReceiver() {
    override val glanceAppWidget: GlanceAppWidget = QuickCaptureGlanceWidget()
}
