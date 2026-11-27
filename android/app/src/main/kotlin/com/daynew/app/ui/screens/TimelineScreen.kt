package com.daynew.app.ui.screens

import androidx.compose.animation.AnimatedVisibility
import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.defaultMinSize
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.offset
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextDecoration
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.compose.ui.unit.times
import com.daynew.app.ui.components.DayNewIcons
import com.daynew.app.ui.components.ScheduleCard
import com.daynew.app.ui.components.parseHexColor
import com.daynew.app.ui.main.ScheduleCardUi
import com.daynew.app.ui.main.TimelineBlockUi
import com.daynew.app.ui.theme.LocalNordPalette
import java.time.LocalDate
import java.time.LocalTime
import java.time.format.TextStyle as JavaTextStyle
import java.util.Locale

private val HourRowHeight = 56.dp
private val TimeAxisWidth = 48.dp

@Composable
fun TimelineScreen(
    selectedDate: LocalDate,
    blocks: List<TimelineBlockUi>,
    allDaySchedules: List<ScheduleCardUi>,
    datesWithItems: Set<LocalDate>,
    onSelectDate: (LocalDate) -> Unit,
    onCreateAtHour: (LocalDate, Int) -> Unit,
    onToggleDone: (String) -> Unit,
    onCardClick: (String) -> Unit,
) {
    val palette = LocalNordPalette.current
    val today = remember { LocalDate.now() }
    val scrollState = rememberScrollState()
    val density = LocalDensity.current
    var showAllDay by remember { mutableStateOf(false) }

    LaunchedEffect(Unit) {
        val targetHour = (LocalTime.now().hour - 1).coerceIn(0, 17)
        val px = with(density) { (targetHour * HourRowHeight).toPx().toInt() }
        scrollState.scrollTo(px)
    }

    val weekdayText = remember(selectedDate) {
        selectedDate.dayOfWeek.getDisplayName(JavaTextStyle.SHORT, Locale.CHINA)
    }
    val stripDates = remember(selectedDate) {
        (-2L..4L).map { selectedDate.plusDays(it) }
    }

    Column(modifier = Modifier.fillMaxSize()) {
        // 顶部日期切换栏 + 7 日快捷胶囊条
        Surface(
            color = palette.surface1,
            shadowElevation = 0.dp,
        ) {
            Column(
                modifier = Modifier
                    .fillMaxWidth()
                    .padding(horizontal = 12.dp, vertical = 8.dp),
                verticalArrangement = Arrangement.spacedBy(8.dp),
            ) {
                Row(
                    modifier = Modifier.fillMaxWidth(),
                    horizontalArrangement = Arrangement.SpaceBetween,
                    verticalAlignment = Alignment.CenterVertically,
                ) {
                    IconButton(
                        onClick = { onSelectDate(selectedDate.minusDays(1)) },
                        modifier = Modifier.defaultMinSize(minWidth = 48.dp, minHeight = 48.dp),
                    ) {
                        Icon(
                            imageVector = DayNewIcons.ChevronLeft,
                            contentDescription = "前一天",
                            tint = palette.textSecondary,
                            modifier = Modifier.size(20.dp),
                        )
                    }

                    Row(
                        modifier = Modifier
                            .clip(RoundedCornerShape(8.dp))
                            .clickable { onSelectDate(today) }
                            .padding(horizontal = 12.dp, vertical = 6.dp),
                        verticalAlignment = Alignment.CenterVertically,
                        horizontalArrangement = Arrangement.spacedBy(6.dp),
                    ) {
                        Text(
                            text = "${selectedDate.monthValue}月${selectedDate.dayOfMonth}日 $weekdayText",
                            color = palette.textPrimary,
                            style = TextStyle(
                                fontSize = 15.sp,
                                fontWeight = FontWeight.SemiBold,
                                fontFeatureSettings = "tnum",
                            ),
                        )
                        if (selectedDate == today) {
                            Box(
                                modifier = Modifier
                                    .clip(RoundedCornerShape(4.dp))
                                    .background(palette.accentTint)
                                    .padding(horizontal = 6.dp, vertical = 2.dp),
                            ) {
                                Text(
                                    text = "今天",
                                    color = palette.accent,
                                    fontSize = 11.sp,
                                    fontWeight = FontWeight.SemiBold,
                                )
                            }
                        } else {
                            Text(
                                text = "回今天",
                                color = palette.accent,
                                fontSize = 11.5.sp,
                                fontWeight = FontWeight.Medium,
                            )
                        }
                    }

                    IconButton(
                        onClick = { onSelectDate(selectedDate.plusDays(1)) },
                        modifier = Modifier.defaultMinSize(minWidth = 48.dp, minHeight = 48.dp),
                    ) {
                        Icon(
                            imageVector = DayNewIcons.ChevronRight,
                            contentDescription = "后一天",
                            tint = palette.textSecondary,
                            modifier = Modifier.size(20.dp),
                        )
                    }
                }

                Row(
                    modifier = Modifier.fillMaxWidth(),
                    horizontalArrangement = Arrangement.spacedBy(6.dp),
                ) {
                    stripDates.forEach { d ->
                        val isSelected = d == selectedDate
                        val isToday = d == today
                        val hasBlocks = d in datesWithItems
                        val dow = d.dayOfWeek.getDisplayName(JavaTextStyle.NARROW, Locale.CHINA)
                        Column(
                            modifier = Modifier
                                .weight(1f)
                                .defaultMinSize(minHeight = 44.dp)
                                .clip(RoundedCornerShape(8.dp))
                                .background(
                                    when {
                                        isSelected -> palette.selectionPill
                                        else -> palette.surface2
                                    }
                                )
                                .border(
                                    width = 1.dp,
                                    color = if (isSelected) palette.accent else palette.borderSubtle,
                                    shape = RoundedCornerShape(8.dp),
                                )
                                .clickable { onSelectDate(d) }
                                .padding(vertical = 6.dp),
                            horizontalAlignment = Alignment.CenterHorizontally,
                            verticalArrangement = Arrangement.spacedBy(2.dp),
                        ) {
                            Text(
                                text = dow,
                                color = if (isSelected) palette.accent else palette.textSecondary,
                                fontSize = 10.5.sp,
                            )
                            Text(
                                text = d.dayOfMonth.toString(),
                                color = when {
                                    isSelected -> palette.textPrimary
                                    isToday -> palette.accent
                                    else -> palette.textPrimary
                                },
                                style = TextStyle(
                                    fontSize = 13.sp,
                                    fontWeight = if (isSelected || isToday) FontWeight.SemiBold else FontWeight.Medium,
                                    fontFeatureSettings = "tnum",
                                ),
                            )
                            Box(
                                modifier = Modifier
                                    .size(4.dp)
                                    .clip(CircleShape)
                                    .background(
                                        when {
                                            hasBlocks && isSelected -> palette.accent
                                            hasBlocks -> palette.textSecondary
                                            else -> palette.surface2.copy(alpha = 0f)
                                        }
                                    ),
                            )
                        }
                    }
                }

                if (allDaySchedules.isNotEmpty()) {
                    Column(
                        modifier = Modifier
                            .fillMaxWidth()
                            .clip(RoundedCornerShape(8.dp))
                            .background(palette.surface2)
                            .border(1.dp, palette.borderSubtle, RoundedCornerShape(8.dp)),
                    ) {
                        Row(
                            modifier = Modifier
                                .fillMaxWidth()
                                .clickable { showAllDay = !showAllDay }
                                .padding(horizontal = 12.dp, vertical = 8.dp),
                            horizontalArrangement = Arrangement.SpaceBetween,
                            verticalAlignment = Alignment.CenterVertically,
                        ) {
                            Text(
                                text = "全天 / 未排时段（${allDaySchedules.size}）",
                                color = palette.textSecondary,
                                style = TextStyle(
                                    fontSize = 12.sp,
                                    fontWeight = FontWeight.SemiBold,
                                    fontFeatureSettings = "tnum",
                                ),
                            )
                            Text(
                                text = if (showAllDay) "收起" else "展开",
                                color = palette.accent,
                                fontSize = 12.sp,
                                fontWeight = FontWeight.Medium,
                            )
                        }
                        AnimatedVisibility(visible = showAllDay) {
                            Column(
                                modifier = Modifier
                                    .fillMaxWidth()
                                    .padding(start = 10.dp, end = 10.dp, bottom = 10.dp),
                                verticalArrangement = Arrangement.spacedBy(8.dp),
                            ) {
                                allDaySchedules.forEach { card ->
                                    ScheduleCard(
                                        card = card,
                                        onToggleDone = { onToggleDone(card.id) },
                                        onClick = { onCardClick(card.id) },
                                    )
                                }
                            }
                        }
                    }
                }
            }
        }

        HorizontalDivider(thickness = 1.dp, color = palette.borderSubtle)

        // 24 小时可滚动单日时间轴
        Box(
            modifier = Modifier
                .weight(1f)
                .fillMaxWidth()
                .verticalScroll(scrollState),
        ) {
            val totalHeight = HourRowHeight * 24
            Row(
                modifier = Modifier
                    .fillMaxWidth()
                    .height(totalHeight),
            ) {
                // 左侧小时刻度
                Column(modifier = Modifier.width(TimeAxisWidth)) {
                    for (hour in 0..23) {
                        Box(
                            modifier = Modifier
                                .fillMaxWidth()
                                .height(HourRowHeight)
                                .padding(top = 4.dp, end = 6.dp),
                            contentAlignment = Alignment.TopEnd,
                        ) {
                            Text(
                                text = "%02d:00".format(Locale.ROOT, hour),
                                color = palette.textSecondary,
                                style = TextStyle(
                                    fontSize = 11.sp,
                                    fontFeatureSettings = "tnum",
                                ),
                            )
                        }
                    }
                }

                // 右侧纵轴画布（点击空白小时直接新建 + 重叠并排日程块）
                BoxWithConstraints(
                    modifier = Modifier
                        .weight(1f)
                        .height(totalHeight)
                        .padding(end = 10.dp),
                ) {
                    val gridWidth = maxWidth

                    // 24 小时整点格子与细线
                    Column(modifier = Modifier.fillMaxSize()) {
                        for (hour in 0..23) {
                            Box(
                                modifier = Modifier
                                    .fillMaxWidth()
                                    .height(HourRowHeight)
                                    .clickable { onCreateAtHour(selectedDate, hour) },
                            ) {
                                HorizontalDivider(
                                    thickness = 1.dp,
                                    color = palette.borderSubtle,
                                    modifier = Modifier.align(Alignment.TopStart),
                                )
                            }
                        }
                    }

                    // 日程时间块（支持重叠并排分列）
                    blocks.forEach { block ->
                        val startY = (block.startMinutes / 60f) * HourRowHeight
                        val durationMin = (block.endMinutes - block.startMinutes).coerceAtLeast(24)
                        val blockHeight = ((durationMin / 60f) * HourRowHeight).coerceAtLeast(28.dp)
                        val colWidth = gridWidth / block.columnCount
                        val offsetX = colWidth * block.columnIndex
                        val accentColor = parseHexColor(block.card.goalColorHex, palette.accent)
                        val bgTint = if (block.card.dto.done) {
                            palette.surface3
                        } else {
                            accentColor.copy(alpha = 0.14f)
                        }
                        val borderColor = if (block.card.dto.done) {
                            palette.borderSubtle
                        } else {
                            accentColor.copy(alpha = 0.72f)
                        }

                        Surface(
                            modifier = Modifier
                                .offset(x = offsetX + 2.dp, y = startY + 1.dp)
                                .size(width = (colWidth - 4.dp).coerceAtLeast(24.dp), height = blockHeight - 2.dp)
                                .clickable { onCardClick(block.card.id) },
                            shape = RoundedCornerShape(6.dp),
                            color = bgTint,
                            border = BorderStroke(1.dp, borderColor),
                            shadowElevation = 0.dp,
                        ) {
                            Row(modifier = Modifier.fillMaxSize()) {
                                Box(
                                    modifier = Modifier
                                        .width(3.dp)
                                        .height(blockHeight)
                                        .background(if (block.card.dto.done) palette.textDisabled else accentColor),
                                )
                                Column(
                                    modifier = Modifier
                                        .weight(1f)
                                        .padding(horizontal = 8.dp, vertical = 4.dp),
                                    verticalArrangement = Arrangement.spacedBy(2.dp),
                                ) {
                                    Text(
                                        text = block.card.dto.title,
                                        color = if (block.card.dto.done) palette.textDisabled else palette.textPrimary,
                                        fontSize = 12.5.sp,
                                        fontWeight = FontWeight.SemiBold,
                                        textDecoration = if (block.card.dto.done) TextDecoration.LineThrough else TextDecoration.None,
                                        maxLines = if (blockHeight >= 48.dp) 2 else 1,
                                        overflow = TextOverflow.Ellipsis,
                                    )
                                    if (blockHeight >= 46.dp && block.card.timeLabel != null) {
                                        Text(
                                            text = block.card.timeLabel,
                                            color = if (block.card.dto.done) palette.textDisabled else accentColor,
                                            style = TextStyle(
                                                fontSize = 11.sp,
                                                fontFeatureSettings = "tnum",
                                            ),
                                            maxLines = 1,
                                        )
                                    }
                                    if (blockHeight >= 66.dp && block.card.goalTitle != null) {
                                        Text(
                                            text = block.card.goalTitle,
                                            color = palette.textSecondary,
                                            fontSize = 10.5.sp,
                                            maxLines = 1,
                                            overflow = TextOverflow.Ellipsis,
                                        )
                                    }
                                }
                            }
                        }
                    }

                    // 今天显示当前时刻线
                    if (selectedDate == today) {
                        val now = LocalTime.now()
                        val nowY = ((now.hour * 60 + now.minute) / 60f) * HourRowHeight
                        Row(
                            modifier = Modifier
                                .fillMaxWidth()
                                .offset(y = nowY - 4.dp),
                            verticalAlignment = Alignment.CenterVertically,
                        ) {
                            Box(
                                modifier = Modifier
                                    .size(7.dp)
                                    .clip(CircleShape)
                                    .background(palette.danger),
                            )
                            Box(
                                modifier = Modifier
                                    .weight(1f)
                                    .height(1.5.dp)
                                    .background(palette.danger),
                            )
                        }
                    }
                }
            }
        }
    }
}
