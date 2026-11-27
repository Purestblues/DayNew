package com.daynew.app.ui.screens

import androidx.compose.animation.AnimatedVisibility
import androidx.compose.animation.core.AnimationState
import androidx.compose.animation.core.animateDecay
import androidx.compose.animation.rememberSplineBasedDecay
import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.ScrollState
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.gestures.awaitEachGesture
import androidx.compose.foundation.gestures.awaitFirstDown
import androidx.compose.foundation.gestures.detectTapGestures
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.defaultMinSize
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.offset
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyListState
import androidx.compose.foundation.lazy.LazyRow
import androidx.compose.foundation.lazy.rememberLazyListState
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
import androidx.compose.runtime.derivedStateOf
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.rememberUpdatedState
import androidx.compose.runtime.setValue
import androidx.compose.runtime.snapshotFlow
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.draw.drawBehind
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.input.pointer.positionChange
import androidx.compose.ui.input.pointer.util.VelocityTracker
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextDecoration
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.compose.ui.unit.times
import androidx.compose.ui.zIndex
import com.daynew.app.ui.components.DayNewIcons
import com.daynew.app.ui.components.ScheduleCard
import com.daynew.app.ui.components.parseHexColor
import com.daynew.app.ui.main.DayTimelineUi
import com.daynew.app.ui.theme.LocalNordPalette
import kotlinx.coroutines.Job
import kotlinx.coroutines.coroutineScope
import kotlinx.coroutines.flow.distinctUntilChanged
import kotlinx.coroutines.flow.filter
import kotlinx.coroutines.flow.map
import kotlinx.coroutines.launch
import java.time.DayOfWeek
import java.time.LocalDate
import java.time.LocalTime
import java.time.temporal.ChronoUnit
import java.util.Locale
import kotlin.math.abs

private val HourRowHeight = 56.dp
private val TimeAxisWidth = 44.dp
private val TimelineTopNavBarHeight = 53.dp
private val DayHeaderHeight = 54.dp
private const val DefaultStartHour = 8
private const val TimelineTotalDays = 20001
private const val TimelineCenterIndex = 10000

private fun shortWeekdayName(dayOfWeek: DayOfWeek): String = when (dayOfWeek) {
    DayOfWeek.MONDAY -> "周一"
    DayOfWeek.TUESDAY -> "周二"
    DayOfWeek.WEDNESDAY -> "周三"
    DayOfWeek.THURSDAY -> "周四"
    DayOfWeek.FRIDAY -> "周五"
    DayOfWeek.SATURDAY -> "周六"
    DayOfWeek.SUNDAY -> "周日"
}

@Composable
fun TimelineScreen(
    selectedDate: LocalDate,
    timelineByDate: Map<LocalDate, DayTimelineUi>,
    datesWithItems: Set<LocalDate>,
    onSelectDate: (LocalDate) -> Unit,
    onCreateAtHour: (LocalDate, Int) -> Unit,
    onToggleDone: (String) -> Unit,
    onCardClick: (String) -> Unit,
    onVerticalScrollDelta: (Float) -> Unit = {},
    topChromeOffsetPx: () -> Float = { 0f },
    topChromeMaxHeightDp: Dp = 0.dp,
    bottomChromeMaxHeightDp: Dp = 0.dp,
) {
    val palette = LocalNordPalette.current
    val today = remember { LocalDate.now() }
    val nowMinutes = remember {
        val t = LocalTime.now()
        t.hour * 60 + t.minute
    }
    val density = LocalDensity.current
    val coroutineScope = rememberCoroutineScope()
    val flingDecay = rememberSplineBasedDecay<Float>()

    val anchorDate = remember { today }
    val initialDayIndex = remember(anchorDate) {
        (TimelineCenterIndex + ChronoUnit.DAYS.between(anchorDate, selectedDate).toInt())
            .coerceIn(0, TimelineTotalDays - 1)
    }
    val daysListState = rememberLazyListState(initialFirstVisibleItemIndex = initialDayIndex)

    // 默认纵向定位到早上 08:00 开始
    val hourRowHeightPx = remember(density) { with(density) { HourRowHeight.toPx() } }
    val initialScrollPx = remember(density) {
        with(density) { (DefaultStartHour * HourRowHeight).roundToPx() }
    }
    val verticalScrollState = rememberScrollState(initial = initialScrollPx)

    var expandedAllDayDate by remember { mutableStateOf<LocalDate?>(null) }
    var isPanningOrFlinging by remember { mutableStateOf(false) }
    var activeFlingJob by remember { mutableStateOf<Job?>(null) }

    val currentOnSelectDate by rememberUpdatedState(onSelectDate)
    val currentSelectedDate by rememberUpdatedState(selectedDate)
    val currentOnVerticalScrollDelta by rememberUpdatedState(onVerticalScrollDelta)

    // 仅在全向滑动/惯性彻底停稳后同步首列日期给 ViewModel，避免滑动期间触发全局重组
    LaunchedEffect(daysListState, anchorDate) {
        snapshotFlow { (isPanningOrFlinging || daysListState.isScrollInProgress) to daysListState.firstVisibleItemIndex }
            .filter { (busy, _) -> !busy }
            .map { (_, idx) -> anchorDate.plusDays((idx - TimelineCenterIndex).toLong()) }
            .distinctUntilChanged()
            .collect { date ->
                if (date != currentSelectedDate) {
                    currentOnSelectDate(date)
                }
            }
    }

    val navHeaderHeightPx = remember(density) { with(density) { TimelineTopNavBarHeight.toPx() } }
    val gridTopInset = topChromeMaxHeightDp + TimelineTopNavBarHeight + DayHeaderHeight

    Box(modifier = Modifier.fillMaxSize()) {
        // 底层：全屏固定坐标的 24 小时时间表（不随顶底栏收起/展开发生位移动荡）
        BoxWithConstraints(modifier = Modifier.fillMaxSize()) {
            val gridViewportWidth = (maxWidth - TimeAxisWidth).coerceAtLeast(180.dp)
            val dayColumnWidth = (gridViewportWidth / 3f).coerceAtLeast(96.dp)
            val totalGridHeight = HourRowHeight * 24

            Row(modifier = Modifier.fillMaxSize()) {
                TimelineTimeAxis(
                    gridTopInset = gridTopInset,
                    totalGridHeight = totalGridHeight,
                    bottomInset = bottomChromeMaxHeightDp,
                    verticalScrollState = verticalScrollState,
                    modifier = Modifier
                        .width(TimeAxisWidth)
                        .fillMaxHeight()
                        .background(palette.surface0),
                )

                // 右侧全向自由滑动多日时间表：外层静止视口承载二维手势，内层承载纵向滚动内容，避免坐标系随滚动平移导致垂直抖动
                Box(
                    modifier = Modifier
                        .weight(1f)
                        .fillMaxHeight()
                        .pointerInput(daysListState, verticalScrollState) {
                            awaitEachGesture {
                                val down = awaitFirstDown(requireUnconsumed = false)
                                activeFlingJob?.cancel()
                                activeFlingJob = null
                                isPanningOrFlinging = false

                                val velocityTracker = VelocityTracker()
                                velocityTracker.addPosition(down.uptimeMillis, down.position)
                                var totalDrag = Offset.Zero
                                var passedSlop = false
                                val touchSlop = viewConfiguration.touchSlop

                                while (true) {
                                    val event = awaitPointerEvent()
                                    val change = event.changes.firstOrNull { it.id == down.id } ?: break
                                    if (!change.pressed) {
                                        velocityTracker.addPosition(change.uptimeMillis, change.position)
                                        break
                                    }

                                    val delta = change.positionChange()
                                    if (!passedSlop) {
                                        totalDrag += delta
                                        if (totalDrag.getDistance() > touchSlop) {
                                            passedSlop = true
                                            isPanningOrFlinging = true
                                            change.consume()
                                            velocityTracker.addPosition(change.uptimeMillis, change.position)
                                            daysListState.dispatchRawDelta(-totalDrag.x)
                                            verticalScrollState.dispatchRawDelta(-totalDrag.y)
                                            if (abs(totalDrag.y) > abs(totalDrag.x)) {
                                                currentOnVerticalScrollDelta(totalDrag.y)
                                            }
                                        }
                                    } else {
                                        change.consume()
                                        velocityTracker.addPosition(change.uptimeMillis, change.position)
                                        if (delta.x != 0f) {
                                            daysListState.dispatchRawDelta(-delta.x)
                                        }
                                        if (delta.y != 0f) {
                                            verticalScrollState.dispatchRawDelta(-delta.y)
                                            if (abs(delta.y) > abs(delta.x)) {
                                                currentOnVerticalScrollDelta(delta.y)
                                            }
                                        }
                                    }
                                }

                                if (passedSlop) {
                                    val velocity = velocityTracker.calculateVelocity()
                                    activeFlingJob = coroutineScope.launch {
                                        try {
                                            coroutineScope {
                                                if (abs(velocity.x) > 50f) {
                                                    launch {
                                                        var prev = 0f
                                                        AnimationState(
                                                            initialValue = 0f,
                                                            initialVelocity = -velocity.x,
                                                        ).animateDecay(flingDecay) {
                                                            val step = value - prev
                                                            prev = value
                                                            val consumed = daysListState.dispatchRawDelta(step)
                                                            if (abs(step - consumed) > 0.5f) {
                                                                cancelAnimation()
                                                            }
                                                        }
                                                    }
                                                }
                                                if (abs(velocity.y) > 50f) {
                                                    launch {
                                                        var prev = 0f
                                                        AnimationState(
                                                            initialValue = 0f,
                                                            initialVelocity = -velocity.y,
                                                        ).animateDecay(flingDecay) {
                                                            val step = value - prev
                                                            prev = value
                                                            val consumed = verticalScrollState.dispatchRawDelta(step)
                                                            if (abs(step - consumed) > 0.5f) {
                                                                cancelAnimation()
                                                            }
                                                        }
                                                    }
                                                }
                                            }
                                        } finally {
                                            isPanningOrFlinging = false
                                        }
                                    }
                                }
                            }
                        },
                ) {
                    Box(
                        modifier = Modifier
                            .fillMaxSize()
                            .verticalScroll(verticalScrollState, enabled = false),
                    ) {
                        LazyRow(
                            state = daysListState,
                            userScrollEnabled = false,
                            modifier = Modifier
                                .fillMaxWidth()
                                .height(gridTopInset + totalGridHeight + bottomChromeMaxHeightDp),
                        ) {
                            items(
                                count = TimelineTotalDays,
                                key = { index -> index },
                            ) { index ->
                                val date = anchorDate.plusDays((index - TimelineCenterIndex).toLong())
                                val dayData = timelineByDate[date] ?: DayTimelineUi()
                                val isToday = date == today
                                val hasTimedItems = date in datesWithItems

                                TimelineDayColumn(
                                    date = date,
                                    isToday = isToday,
                                    dayData = dayData,
                                    hasTimedItems = hasTimedItems,
                                    isAllDayExpanded = expandedAllDayDate == date,
                                    dayColumnWidth = dayColumnWidth,
                                    gridTopInset = gridTopInset,
                                    totalGridHeight = totalGridHeight,
                                    bottomInset = bottomChromeMaxHeightDp,
                                    hourRowHeightPx = hourRowHeightPx,
                                    nowMinutes = nowMinutes,
                                    navHeaderHeightPx = navHeaderHeightPx,
                                    topChromeOffsetPx = topChromeOffsetPx,
                                    verticalScrollState = verticalScrollState,
                                    onToggleAllDay = {
                                        expandedAllDayDate = if (expandedAllDayDate == date) null else date
                                    },
                                    onCreateAtHour = onCreateAtHour,
                                    onCardClick = onCardClick,
                                )
                            }
                        }
                    }
                }
            }
        }

        // 中层：左侧月份角标（跟随顶栏收起/展开同步平移）
        TimelineMonthCornerBadge(
            daysListState = daysListState,
            anchorDate = anchorDate,
            modifier = Modifier
                .align(Alignment.TopStart)
                .width(TimeAxisWidth)
                .height(DayHeaderHeight)
                .zIndex(4f)
                .graphicsLayer { translationY = topChromeOffsetPx() + navHeaderHeightPx },
        )

        // 顶层：精简导航条（前进 / 后退 / 回今天，纯 GPU 平移贴合顶栏底部）
        Surface(
            color = palette.surface1,
            shadowElevation = 0.dp,
            modifier = Modifier
                .fillMaxWidth()
                .align(Alignment.TopCenter)
                .zIndex(5f)
                .graphicsLayer { translationY = topChromeOffsetPx() },
        ) {
            Column(modifier = Modifier.fillMaxWidth()) {
                TimelineTopNavBar(
                    daysListState = daysListState,
                    anchorDate = anchorDate,
                    today = today,
                    onPrevDay = {
                        activeFlingJob?.cancel()
                        activeFlingJob = null
                        isPanningOrFlinging = false
                        val target = (daysListState.firstVisibleItemIndex - 1).coerceAtLeast(0)
                        coroutineScope.launch { daysListState.animateScrollToItem(target) }
                    },
                    onToday = {
                        activeFlingJob?.cancel()
                        activeFlingJob = null
                        isPanningOrFlinging = false
                        coroutineScope.launch { daysListState.animateScrollToItem(TimelineCenterIndex) }
                    },
                    onNextDay = {
                        activeFlingJob?.cancel()
                        activeFlingJob = null
                        isPanningOrFlinging = false
                        val target = (daysListState.firstVisibleItemIndex + 1).coerceAtMost(TimelineTotalDays - 1)
                        coroutineScope.launch { daysListState.animateScrollToItem(target) }
                    },
                )

                // 可选全天/未排时段展开面板（点击某列顶部小角标打开）
                val expandedSchedules = expandedAllDayDate?.let { timelineByDate[it]?.allDaySchedules }.orEmpty()
                AnimatedVisibility(visible = expandedAllDayDate != null && expandedSchedules.isNotEmpty()) {
                    expandedAllDayDate?.let { targetDate ->
                        Column(
                            modifier = Modifier
                                .fillMaxWidth()
                                .padding(start = 12.dp, end = 12.dp, bottom = 8.dp)
                                .clip(RoundedCornerShape(12.dp))
                                .background(palette.surface2)
                                .border(1.dp, palette.borderSubtle, RoundedCornerShape(12.dp))
                                .padding(10.dp),
                            verticalArrangement = Arrangement.spacedBy(8.dp),
                        ) {
                            Row(
                                modifier = Modifier.fillMaxWidth(),
                                horizontalArrangement = Arrangement.SpaceBetween,
                                verticalAlignment = Alignment.CenterVertically,
                            ) {
                                Text(
                                    text = "${targetDate.monthValue}月${targetDate.dayOfMonth}日 · 全天/未排时段（${expandedSchedules.size}）",
                                    color = palette.textSecondary,
                                    style = TextStyle(
                                        fontSize = 12.sp,
                                        fontWeight = FontWeight.SemiBold,
                                        fontFeatureSettings = "tnum",
                                    ),
                                )
                                Text(
                                    text = "收起",
                                    color = palette.accent,
                                    fontSize = 12.sp,
                                    fontWeight = FontWeight.Medium,
                                    modifier = Modifier
                                        .clip(RoundedCornerShape(6.dp))
                                        .clickable { expandedAllDayDate = null }
                                        .padding(horizontal = 6.dp, vertical = 2.dp),
                                )
                            }
                            expandedSchedules.forEach { card ->
                                ScheduleCard(
                                    card = card,
                                    onToggleDone = { onToggleDone(card.id) },
                                    onClick = { onCardClick(card.id) },
                                )
                            }
                        }
                    }
                }

                HorizontalDivider(thickness = 1.dp, color = palette.borderSubtle)
            }
        }
    }
}

@Composable
private fun TimelineTopNavBar(
    daysListState: LazyListState,
    anchorDate: LocalDate,
    today: LocalDate,
    onPrevDay: () -> Unit,
    onToday: () -> Unit,
    onNextDay: () -> Unit,
) {
    val palette = LocalNordPalette.current
    val firstVisibleDate by remember(daysListState, anchorDate) {
        derivedStateOf {
            anchorDate.plusDays((daysListState.firstVisibleItemIndex - TimelineCenterIndex).toLong())
        }
    }
    val lastVisibleDate = firstVisibleDate.plusDays(2)
    val headerRangeText = if (firstVisibleDate.monthValue == lastVisibleDate.monthValue) {
        "${firstVisibleDate.year}年${firstVisibleDate.monthValue}月 · ${firstVisibleDate.dayOfMonth}日 - ${lastVisibleDate.dayOfMonth}日"
    } else {
        "${firstVisibleDate.monthValue}月${firstVisibleDate.dayOfMonth}日 - ${lastVisibleDate.monthValue}月${lastVisibleDate.dayOfMonth}日"
    }
    val isAtToday = firstVisibleDate == today

    Row(
        modifier = Modifier
            .fillMaxWidth()
            .height(52.dp)
            .padding(horizontal = 6.dp),
        horizontalArrangement = Arrangement.SpaceBetween,
        verticalAlignment = Alignment.CenterVertically,
    ) {
        IconButton(
            onClick = onPrevDay,
            modifier = Modifier.defaultMinSize(minWidth = 44.dp, minHeight = 44.dp),
        ) {
            Icon(
                imageVector = DayNewIcons.ChevronLeft,
                contentDescription = "前一天",
                tint = palette.textSecondary,
                modifier = Modifier.size(19.dp),
            )
        }

        Row(
            modifier = Modifier
                .clip(RoundedCornerShape(10.dp))
                .clickable(onClick = onToday)
                .padding(horizontal = 10.dp, vertical = 6.dp),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(8.dp),
        ) {
            Text(
                text = headerRangeText,
                color = palette.textPrimary,
                style = TextStyle(
                    fontSize = 14.sp,
                    fontWeight = FontWeight.SemiBold,
                    fontFeatureSettings = "tnum",
                ),
            )
            Box(
                modifier = Modifier
                    .clip(RoundedCornerShape(6.dp))
                    .background(if (isAtToday) palette.accentTint else palette.surface3)
                    .padding(horizontal = 7.dp, vertical = 2.dp),
            ) {
                Text(
                    text = if (isAtToday) "今天起" else "回今天",
                    color = palette.accent,
                    fontSize = 11.sp,
                    fontWeight = FontWeight.SemiBold,
                )
            }
        }

        IconButton(
            onClick = onNextDay,
            modifier = Modifier.defaultMinSize(minWidth = 44.dp, minHeight = 44.dp),
        ) {
            Icon(
                imageVector = DayNewIcons.ChevronRight,
                contentDescription = "后一天",
                tint = palette.textSecondary,
                modifier = Modifier.size(19.dp),
            )
        }
    }
}

@Composable
private fun TimelineMonthCornerBadge(
    daysListState: LazyListState,
    anchorDate: LocalDate,
    modifier: Modifier = Modifier,
) {
    val palette = LocalNordPalette.current
    val firstVisibleMonth by remember(daysListState, anchorDate) {
        derivedStateOf {
            anchorDate.plusDays((daysListState.firstVisibleItemIndex - TimelineCenterIndex).toLong()).monthValue
        }
    }

    Box(
        modifier = modifier
            .background(palette.surface1),
        contentAlignment = Alignment.Center,
    ) {
        Text(
            text = "${firstVisibleMonth}月",
            color = palette.textSecondary,
            style = TextStyle(
                fontSize = 11.5.sp,
                fontWeight = FontWeight.SemiBold,
                fontFeatureSettings = "tnum",
            ),
        )
        HorizontalDivider(
            thickness = 1.dp,
            color = palette.borderSubtle,
            modifier = Modifier.align(Alignment.BottomCenter),
        )
    }
}

@Composable
private fun TimelineTimeAxis(
    gridTopInset: Dp,
    totalGridHeight: Dp,
    bottomInset: Dp,
    verticalScrollState: ScrollState,
    modifier: Modifier = Modifier,
) {
    val palette = LocalNordPalette.current
    Box(
        modifier = modifier.verticalScroll(verticalScrollState, enabled = false),
    ) {
        Column(
            modifier = Modifier
                .fillMaxWidth()
                .height(gridTopInset + totalGridHeight + bottomInset),
        ) {
            Spacer(modifier = Modifier.height(gridTopInset))
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
                            fontSize = 10.5.sp,
                            fontFeatureSettings = "tnum",
                        ),
                    )
                }
            }
            Spacer(modifier = Modifier.height(bottomInset))
        }
    }
}

@Composable
private fun TimelineDayColumn(
    date: LocalDate,
    isToday: Boolean,
    dayData: DayTimelineUi,
    hasTimedItems: Boolean,
    isAllDayExpanded: Boolean,
    dayColumnWidth: Dp,
    gridTopInset: Dp,
    totalGridHeight: Dp,
    bottomInset: Dp,
    hourRowHeightPx: Float,
    nowMinutes: Int,
    navHeaderHeightPx: Float,
    topChromeOffsetPx: () -> Float,
    verticalScrollState: ScrollState,
    onToggleAllDay: () -> Unit,
    onCreateAtHour: (LocalDate, Int) -> Unit,
    onCardClick: (String) -> Unit,
) {
    val palette = LocalNordPalette.current
    val allDayCount = dayData.allDaySchedules.size
    val dow = shortWeekdayName(date.dayOfWeek)
    val currentOnCreateAtHour by rememberUpdatedState(onCreateAtHour)
    val gridLineColor = palette.borderSubtle

    Box(
        modifier = Modifier
            .width(dayColumnWidth)
            .height(gridTopInset + totalGridHeight + bottomInset),
    ) {
        // 下层：24 小时 Canvas 网格（单层绘制 + 单一点击检测）+ 日程块 + 当前时刻线
        Box(
            modifier = Modifier
                .fillMaxWidth()
                .padding(top = gridTopInset)
                .height(totalGridHeight),
        ) {
            Box(
                modifier = Modifier
                    .fillMaxSize()
                    .drawBehind {
                        val strokePx = 1.dp.toPx()
                        for (hour in 0..23) {
                            val y = hour * hourRowHeightPx
                            drawLine(
                                color = gridLineColor,
                                start = Offset(0f, y),
                                end = Offset(size.width, y),
                                strokeWidth = strokePx,
                            )
                        }
                        val rightX = size.width - strokePx / 2f
                        drawLine(
                            color = gridLineColor,
                            start = Offset(rightX, 0f),
                            end = Offset(rightX, size.height),
                            strokeWidth = strokePx,
                        )
                    }
                    .pointerInput(date, hourRowHeightPx) {
                        detectTapGestures { offset ->
                            val hour = (offset.y / hourRowHeightPx).toInt().coerceIn(0, 23)
                            currentOnCreateAtHour(date, hour)
                        }
                    },
            )

            // 日程时间块（支持重叠并排分列）
            val blockUsableWidth = (dayColumnWidth - 4.dp).coerceAtLeast(36.dp)
            dayData.blocks.forEach { block ->
                val startY = (block.startMinutes / 60f) * HourRowHeight
                val durationMin = (block.endMinutes - block.startMinutes).coerceAtLeast(24)
                val blockHeight = ((durationMin / 60f) * HourRowHeight).coerceAtLeast(26.dp)
                val colWidth = blockUsableWidth / block.columnCount
                val offsetX = colWidth * block.columnIndex + 2.dp
                val accentColor = parseHexColor(block.card.goalColorHex, palette.accent)
                val bgTint = if (block.card.dto.done) {
                    palette.surface3
                } else {
                    accentColor.copy(alpha = 0.16f)
                }
                val borderColor = if (block.card.dto.done) {
                    palette.borderSubtle
                } else {
                    accentColor.copy(alpha = 0.72f)
                }

                Surface(
                    modifier = Modifier
                        .offset(x = offsetX, y = startY + 1.dp)
                        .size(
                            width = (colWidth - 2.dp).coerceAtLeast(22.dp),
                            height = blockHeight - 2.dp,
                        )
                        .clickable { onCardClick(block.card.id) },
                    shape = RoundedCornerShape(8.dp),
                    color = bgTint,
                    border = BorderStroke(1.dp, borderColor),
                    shadowElevation = 0.dp,
                ) {
                    Row(modifier = Modifier.fillMaxSize()) {
                        Box(
                            modifier = Modifier
                                .width(2.5.dp)
                                .fillMaxHeight()
                                .background(if (block.card.dto.done) palette.textDisabled else accentColor),
                        )
                        Column(
                            modifier = Modifier
                                .weight(1f)
                                .padding(horizontal = 5.dp, vertical = 3.dp),
                            verticalArrangement = Arrangement.spacedBy(1.dp),
                        ) {
                            Text(
                                text = block.card.dto.title,
                                color = if (block.card.dto.done) palette.textDisabled else palette.textPrimary,
                                fontSize = 11.5.sp,
                                lineHeight = 14.sp,
                                fontWeight = FontWeight.SemiBold,
                                textDecoration = if (block.card.dto.done) TextDecoration.LineThrough else TextDecoration.None,
                                maxLines = if (blockHeight >= 44.dp) 2 else 1,
                                overflow = TextOverflow.Ellipsis,
                            )
                            if (blockHeight >= 46.dp && block.card.timeLabel != null) {
                                Text(
                                    text = block.card.timeLabel,
                                    color = if (block.card.dto.done) palette.textDisabled else accentColor,
                                    style = TextStyle(
                                        fontSize = 10.sp,
                                        fontFeatureSettings = "tnum",
                                    ),
                                    maxLines = 1,
                                    overflow = TextOverflow.Ellipsis,
                                )
                            }
                        }
                    }
                }
            }

            // 今天显示当前时刻线
            if (isToday) {
                val nowY = (nowMinutes / 60f) * HourRowHeight
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

        // 顶层：纵向滚动时吸顶的日期列头（叠加顶栏收起/展开偏移，graphicsLayer 仅走绘制阶段）
        Box(
            modifier = Modifier
                .fillMaxWidth()
                .height(DayHeaderHeight)
                .zIndex(2f)
                .graphicsLayer {
                    translationY = verticalScrollState.value.toFloat() + topChromeOffsetPx() + navHeaderHeightPx
                }
                .background(if (isToday) palette.accentTint.copy(alpha = 0.25f) else palette.surface1)
                .background(palette.surface1.copy(alpha = if (isToday) 0.72f else 1f))
                .clickable(enabled = allDayCount > 0, onClick = onToggleAllDay),
        ) {
            Column(
                modifier = Modifier
                    .fillMaxSize()
                    .padding(horizontal = 4.dp, vertical = 5.dp),
                horizontalAlignment = Alignment.CenterHorizontally,
                verticalArrangement = Arrangement.Center,
            ) {
                Row(
                    verticalAlignment = Alignment.CenterVertically,
                    horizontalArrangement = Arrangement.spacedBy(4.dp),
                ) {
                    Text(
                        text = dow,
                        color = if (isToday) palette.accent else palette.textSecondary,
                        fontSize = 10.5.sp,
                        fontWeight = if (isToday) FontWeight.SemiBold else FontWeight.Normal,
                    )
                    if (hasTimedItems) {
                        Box(
                            modifier = Modifier
                                .size(4.dp)
                                .clip(CircleShape)
                                .background(palette.accent),
                        )
                    }
                }

                Row(
                    verticalAlignment = Alignment.CenterVertically,
                    horizontalArrangement = Arrangement.spacedBy(4.dp),
                ) {
                    Box(
                        modifier = Modifier
                            .clip(RoundedCornerShape(8.dp))
                            .background(if (isToday) palette.accent else Color.Transparent)
                            .padding(horizontal = 7.dp, vertical = 1.dp),
                    ) {
                        Text(
                            text = "${date.dayOfMonth}",
                            color = if (isToday) palette.onAccent else palette.textPrimary,
                            style = TextStyle(
                                fontSize = 13.5.sp,
                                fontWeight = FontWeight.SemiBold,
                                fontFeatureSettings = "tnum",
                            ),
                        )
                    }
                    if (allDayCount > 0) {
                        Box(
                            modifier = Modifier
                                .clip(RoundedCornerShape(6.dp))
                                .background(
                                    if (isAllDayExpanded) palette.accent else palette.surface3
                                )
                                .padding(horizontal = 5.dp, vertical = 1.dp),
                        ) {
                            Text(
                                text = "$allDayCount",
                                color = if (isAllDayExpanded) palette.onAccent else palette.textSecondary,
                                style = TextStyle(
                                    fontSize = 10.sp,
                                    fontWeight = FontWeight.SemiBold,
                                    fontFeatureSettings = "tnum",
                                ),
                            )
                        }
                    }
                }
            }

            // 右侧分隔线与底部分隔线
            Box(
                modifier = Modifier
                    .align(Alignment.CenterEnd)
                    .width(1.dp)
                    .fillMaxHeight()
                    .background(palette.borderSubtle),
            )
            HorizontalDivider(
                thickness = 1.dp,
                color = if (isToday) palette.accent else palette.borderSubtle,
                modifier = Modifier.align(Alignment.BottomCenter),
            )
        }
    }
}
