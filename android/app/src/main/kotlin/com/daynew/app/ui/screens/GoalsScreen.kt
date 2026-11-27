package com.daynew.app.ui.screens

import androidx.compose.animation.AnimatedVisibility
import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.defaultMinSize
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
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
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextDecoration
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.daynew.app.ui.components.DayNewIcons
import com.daynew.app.ui.components.ScheduleCard
import com.daynew.app.ui.components.parseHexColor
import com.daynew.app.ui.main.GoalProgressUi
import com.daynew.app.ui.theme.LocalNordPalette
import com.daynew.sync.DeadlineRules
import java.time.LocalDate

@Composable
fun GoalsScreen(
    goalProgressList: List<GoalProgressUi>,
    onToggleGoalCompleted: (String) -> Unit,
    onEditGoal: (String) -> Unit,
    onToggleScheduleDone: (String) -> Unit,
    onScheduleClick: (String) -> Unit,
) {
    val palette = LocalNordPalette.current
    val today = remember { LocalDate.now() }
    var expandedGoalIds by remember { mutableStateOf<Set<String>>(emptySet()) }

    LazyColumn(
        modifier = Modifier.fillMaxSize(),
        contentPadding = PaddingValues(16.dp),
        verticalArrangement = Arrangement.spacedBy(10.dp),
    ) {
        if (goalProgressList.isEmpty()) {
            item {
                Surface(
                    shape = RoundedCornerShape(8.dp),
                    color = palette.surface2,
                    shadowElevation = 0.dp,
                    border = BorderStroke(1.dp, palette.borderSubtle),
                ) {
                    Text(
                        text = "暂无远景目标，可点击右下角「+」创建目标并挂接日程。",
                        color = palette.textSecondary,
                        fontSize = 13.sp,
                        modifier = Modifier
                            .fillMaxWidth()
                            .padding(16.dp),
                    )
                }
            }
        } else {
            items(goalProgressList, key = { it.goal.id }) { item ->
                val color = parseHexColor(item.goal.color, palette.accent)
                val ratio = if (item.totalCount == 0) 0f else item.doneCount.toFloat() / item.totalCount
                val isExpanded = item.goal.id in expandedGoalIds
                val parsedDue = remember(item.goal.due) { DeadlineRules.parseDue(item.goal.due).date }
                val isGoalOverdue = !item.goal.isCompleted && parsedDue != null && parsedDue.isBefore(today)

                Surface(
                    shape = RoundedCornerShape(8.dp),
                    color = palette.surface2,
                    shadowElevation = 0.dp,
                    border = BorderStroke(1.dp, palette.borderSubtle),
                ) {
                    Column(
                        modifier = Modifier
                            .fillMaxWidth()
                            .clickable {
                                expandedGoalIds = if (isExpanded) {
                                    expandedGoalIds - item.goal.id
                                } else {
                                    expandedGoalIds + item.goal.id
                                }
                            }
                            .padding(start = 4.dp, end = 6.dp, top = 8.dp, bottom = 12.dp),
                        verticalArrangement = Arrangement.spacedBy(8.dp),
                    ) {
                        Row(
                            modifier = Modifier.fillMaxWidth(),
                            verticalAlignment = Alignment.CenterVertically,
                        ) {
                            // 目标完成勾选项（48dp 触控热区）
                            Box(
                                modifier = Modifier
                                    .size(48.dp)
                                    .clip(RoundedCornerShape(6.dp))
                                    .clickable { onToggleGoalCompleted(item.goal.id) },
                                contentAlignment = Alignment.Center,
                            ) {
                                Box(
                                    modifier = Modifier
                                        .size(20.dp)
                                        .clip(RoundedCornerShape(6.dp))
                                        .background(if (item.goal.isCompleted) palette.success else Color.Transparent)
                                        .border(
                                            1.5.dp,
                                            if (item.goal.isCompleted) palette.success else palette.borderStrong,
                                            RoundedCornerShape(6.dp),
                                        ),
                                    contentAlignment = Alignment.Center,
                                ) {
                                    if (item.goal.isCompleted) {
                                        Icon(
                                            imageVector = DayNewIcons.Check,
                                            contentDescription = "已完成",
                                            tint = palette.onAccent,
                                            modifier = Modifier.size(13.dp),
                                        )
                                    }
                                }
                            }

                            Column(
                                modifier = Modifier.weight(1f),
                                verticalArrangement = Arrangement.spacedBy(2.dp),
                            ) {
                                Row(
                                    verticalAlignment = Alignment.CenterVertically,
                                    horizontalArrangement = Arrangement.spacedBy(7.dp),
                                ) {
                                    Box(
                                        modifier = Modifier
                                            .size(8.dp)
                                            .clip(CircleShape)
                                            .background(color),
                                    )
                                    Text(
                                        text = item.goal.title,
                                        color = if (item.goal.isCompleted) palette.textDisabled else palette.textPrimary,
                                        fontSize = 14.5.sp,
                                        fontWeight = FontWeight.SemiBold,
                                        textDecoration = if (item.goal.isCompleted) TextDecoration.LineThrough else TextDecoration.None,
                                        maxLines = 1,
                                        overflow = TextOverflow.Ellipsis,
                                    )
                                }
                                if (parsedDue != null) {
                                    Text(
                                        text = if (isGoalOverdue) "已逾期 · $parsedDue" else "截止 $parsedDue",
                                        color = if (isGoalOverdue) palette.danger else palette.textSecondary,
                                        style = TextStyle(fontSize = 11.5.sp, fontFeatureSettings = "tnum"),
                                    )
                                }
                            }

                            Text(
                                text = "${item.doneCount}/${item.totalCount}",
                                color = palette.textSecondary,
                                style = TextStyle(
                                    fontSize = 12.sp,
                                    fontWeight = FontWeight.Medium,
                                    fontFeatureSettings = "tnum",
                                ),
                                modifier = Modifier.padding(horizontal = 4.dp),
                            )

                            IconButton(
                                onClick = { onEditGoal(item.goal.id) },
                                modifier = Modifier.defaultMinSize(minWidth = 48.dp, minHeight = 48.dp),
                            ) {
                                Icon(
                                    imageVector = DayNewIcons.Edit,
                                    contentDescription = "编辑目标",
                                    tint = palette.textSecondary,
                                    modifier = Modifier.size(18.dp),
                                )
                            }
                        }

                        Box(
                            modifier = Modifier
                                .fillMaxWidth()
                                .padding(horizontal = 12.dp)
                                .height(6.dp)
                                .clip(RoundedCornerShape(4.dp))
                                .background(palette.surface3),
                        ) {
                            Box(
                                modifier = Modifier
                                    .fillMaxWidth(fraction = ratio.coerceIn(0f, 1f))
                                    .height(6.dp)
                                    .clip(RoundedCornerShape(4.dp))
                                    .background(if (item.goal.isCompleted) palette.textDisabled else color),
                            )
                        }

                        AnimatedVisibility(visible = isExpanded) {
                            Column(
                                modifier = Modifier
                                    .fillMaxWidth()
                                    .padding(start = 10.dp, end = 8.dp, top = 4.dp),
                                verticalArrangement = Arrangement.spacedBy(8.dp),
                            ) {
                                if (item.schedules.isEmpty()) {
                                    Text(
                                        text = "暂无关联日程，可在日程编辑页挂接到此目标。",
                                        color = palette.textSecondary,
                                        fontSize = 12.sp,
                                        modifier = Modifier.padding(vertical = 4.dp),
                                    )
                                } else {
                                    item.schedules.forEach { card ->
                                        ScheduleCard(
                                            card = card,
                                            onToggleDone = { onToggleScheduleDone(card.id) },
                                            onClick = { onScheduleClick(card.id) },
                                        )
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
    }
}
