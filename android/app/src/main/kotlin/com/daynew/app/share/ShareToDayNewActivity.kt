package com.daynew.app.share

import android.content.Intent
import android.os.Bundle
import android.widget.Toast
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.imePadding
import androidx.compose.foundation.layout.navigationBarsPadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.daynew.app.DayNewApplication
import com.daynew.app.data.LaneKind
import com.daynew.app.data.LaneRules
import com.daynew.app.data.ScheduleDraft
import com.daynew.app.ui.theme.LocalNordPalette
import com.daynew.app.ui.theme.NordFrostTheme
import com.daynew.sync.RemindRules
import java.time.LocalDate

/**
 * 系统分享快录 & 桌面快速新建透明弹窗：
 * 响应 ACTION_SEND (text/plain) 与内部 QUICK_CAPTURE，支持四档位切换后保存落盘。
 */
class ShareToDayNewActivity : ComponentActivity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        enableEdgeToEdge()

        val subject = intent?.getStringExtra(Intent.EXTRA_SUBJECT)
        val sharedText = intent?.getStringExtra(Intent.EXTRA_TEXT)
        val parsed = ShareTextParser.parse(subject, sharedText)

        setContent {
            NordFrostTheme {
                ShareQuickCaptureSheet(
                    initialTitle = parsed.title,
                    initialNote = parsed.note,
                    onCancel = { finish() },
                    onSave = { title, note, lane ->
                        val app = applicationContext as DayNewApplication
                        val today = LocalDate.now()
                        val saved = app.repository.upsertSchedule(
                            ScheduleDraft(
                                title = title,
                                note = note,
                                lane = lane,
                                initialLane = lane,
                                dueDate = LaneRules.dueForLane(lane, today),
                                remind = RemindRules.DEFAULT,
                            ),
                        )
                        app.repository.flush()
                        Toast.makeText(
                            this,
                            "已添加到「${lane.title}」· ${saved.title}",
                            Toast.LENGTH_SHORT,
                        ).show()
                        finish()
                    },
                )
            }
        }
    }

    companion object {
        const val ACTION_QUICK_CAPTURE = "com.daynew.app.action.QUICK_CAPTURE"
    }
}

@Composable
private fun ShareQuickCaptureSheet(
    initialTitle: String,
    initialNote: String,
    onCancel: () -> Unit,
    onSave: (title: String, note: String, lane: LaneKind) -> Unit,
) {
    val palette = LocalNordPalette.current
    var title by remember { mutableStateOf(initialTitle) }
    var note by remember { mutableStateOf(initialNote) }
    var selectedLane by remember { mutableStateOf(LaneKind.TODAY) }
    val targetLanes = remember {
        listOf(LaneKind.TODAY, LaneKind.TOMORROW, LaneKind.THIS_WEEK, LaneKind.LATER)
    }

    Box(
        modifier = Modifier
            .fillMaxSize()
            .background(Color.Black.copy(alpha = 0.42f))
            .clickable(
                interactionSource = remember { MutableInteractionSource() },
                indication = null,
                onClick = onCancel,
            ),
        contentAlignment = Alignment.BottomCenter,
    ) {
        Surface(
            modifier = Modifier
                .fillMaxWidth()
                .navigationBarsPadding()
                .imePadding()
                .clickable(
                    interactionSource = remember { MutableInteractionSource() },
                    indication = null,
                    onClick = {},
                ),
            shape = RoundedCornerShape(topStart = 16.dp, topEnd = 16.dp),
            color = palette.surface4,
            border = BorderStroke(1.dp, palette.borderPopup),
            shadowElevation = 0.dp,
        ) {
            Column(
                modifier = Modifier
                    .fillMaxWidth()
                    .padding(horizontal = 20.dp, vertical = 18.dp),
                verticalArrangement = Arrangement.spacedBy(12.dp),
            ) {
                Row(
                    modifier = Modifier.fillMaxWidth(),
                    horizontalArrangement = Arrangement.SpaceBetween,
                    verticalAlignment = Alignment.CenterVertically,
                ) {
                    Text(
                        text = "快速收进日新",
                        color = palette.textPrimary,
                        fontSize = 17.sp,
                        fontWeight = FontWeight.SemiBold,
                    )
                    Text(
                        text = "目标档位：${selectedLane.title}",
                        color = palette.accent,
                        fontSize = 12.sp,
                        fontWeight = FontWeight.Medium,
                    )
                }

                OutlinedTextField(
                    value = title,
                    onValueChange = { title = it },
                    label = { Text("日程标题") },
                    placeholder = { Text("要做什么？") },
                    singleLine = true,
                    modifier = Modifier.fillMaxWidth(),
                )

                OutlinedTextField(
                    value = note,
                    onValueChange = { note = it },
                    label = { Text("备注（可选）") },
                    placeholder = { Text("分享正文或补充说明") },
                    minLines = 2,
                    maxLines = 4,
                    modifier = Modifier.fillMaxWidth(),
                )

                Column(verticalArrangement = Arrangement.spacedBy(6.dp)) {
                    Text(
                        text = "安排到",
                        color = palette.textSecondary,
                        fontSize = 12.sp,
                    )
                    Row(
                        modifier = Modifier.fillMaxWidth(),
                        horizontalArrangement = Arrangement.spacedBy(8.dp),
                    ) {
                        targetLanes.forEach { lane ->
                            val selected = lane == selectedLane
                            Surface(
                                modifier = Modifier
                                    .weight(1f)
                                    .clip(RoundedCornerShape(8.dp))
                                    .clickable { selectedLane = lane },
                                shape = RoundedCornerShape(8.dp),
                                color = if (selected) palette.selectionPill else palette.surface2,
                                border = BorderStroke(
                                    width = 1.dp,
                                    color = if (selected) palette.accent else palette.borderSubtle,
                                ),
                                shadowElevation = 0.dp,
                            ) {
                                Box(
                                    modifier = Modifier.padding(vertical = 9.dp),
                                    contentAlignment = Alignment.Center,
                                ) {
                                    Text(
                                        text = lane.title,
                                        color = if (selected) palette.accent else palette.textPrimary,
                                        fontSize = 13.sp,
                                        fontWeight = if (selected) FontWeight.SemiBold else FontWeight.Medium,
                                    )
                                }
                            }
                        }
                    }
                }

                Spacer(modifier = Modifier.height(2.dp))

                Row(
                    modifier = Modifier.fillMaxWidth(),
                    horizontalArrangement = Arrangement.spacedBy(10.dp),
                ) {
                    OutlinedButton(
                        onClick = onCancel,
                        shape = RoundedCornerShape(8.dp),
                        modifier = Modifier.weight(1f),
                    ) {
                        Text("取消")
                    }
                    Button(
                        onClick = { onSave(title, note, selectedLane) },
                        shape = RoundedCornerShape(8.dp),
                        colors = ButtonDefaults.buttonColors(
                            containerColor = palette.accent,
                            contentColor = palette.onAccent,
                        ),
                        modifier = Modifier.weight(1f),
                    ) {
                        Text("保存到${selectedLane.title}")
                    }
                }
            }
        }
    }
}
