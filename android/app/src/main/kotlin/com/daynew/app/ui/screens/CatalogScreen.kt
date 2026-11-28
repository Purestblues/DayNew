package com.daynew.app.ui.screens

import androidx.compose.animation.AnimatedContent
import androidx.compose.animation.AnimatedVisibility
import androidx.compose.animation.core.animateFloatAsState
import androidx.compose.animation.core.tween
import androidx.compose.animation.fadeIn
import androidx.compose.animation.fadeOut
import androidx.compose.animation.slideInHorizontally
import androidx.compose.animation.slideOutHorizontally
import androidx.compose.animation.togetherWith
import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.gestures.detectDragGesturesAfterLongPress
import androidx.compose.foundation.gestures.scrollBy
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.defaultMinSize
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.LazyListLayoutInfo
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.lazy.itemsIndexed
import androidx.compose.foundation.lazy.rememberLazyListState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.Stable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberUpdatedState
import androidx.compose.runtime.setValue
import androidx.compose.runtime.withFrameNanos
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Rect
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.hapticfeedback.HapticFeedbackType
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.layout.boundsInRoot
import androidx.compose.ui.layout.onGloballyPositioned
import androidx.compose.ui.layout.positionInRoot
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.platform.LocalHapticFeedback
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.IntSize
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.compose.ui.zIndex
import com.daynew.app.ui.components.DayNewIcons
import com.daynew.app.ui.components.ScheduleCard
import com.daynew.app.ui.components.parseHexColor
import com.daynew.app.ui.main.DeadlineDayGroupUi
import com.daynew.app.ui.main.EisenhowerBucketUi
import com.daynew.app.ui.main.EisenhowerKind
import com.daynew.app.ui.main.LocationGroupUi
import com.daynew.app.ui.main.PersonGroupUi
import com.daynew.app.ui.main.ScheduleCardUi
import com.daynew.app.ui.main.TagGroupUi
import com.daynew.app.ui.main.TagPageSection
import com.daynew.app.ui.theme.LocalNordPalette
import com.daynew.app.ui.theme.NordMotion
import com.daynew.app.ui.theme.nordClickable
import com.daynew.app.ui.theme.rememberDayNewHaptics
import java.time.LocalDate

@Composable
fun DeadlinesScreen(
    groups: List<DeadlineDayGroupUi>,
    onToggleDone: (String) -> Unit,
    onCardClick: (String) -> Unit,
    onOpenGoal: (String) -> Unit,
) {
    val palette = LocalNordPalette.current
    LazyColumn(
        modifier = Modifier.fillMaxSize(),
        contentPadding = PaddingValues(16.dp),
        verticalArrangement = Arrangement.spacedBy(10.dp),
    ) {
        if (groups.isEmpty()) {
            item { EmptyPlaceholder("暂无待办截止日期，无具体时段的日程和带截止日的目标会在此按天汇聚。") }
        } else {
            groups.forEach { group ->
                item(key = "header-${group.date}") {
                    val headerColor = when {
                        group.isOverdue -> palette.danger
                        group.isToday -> palette.accent
                        else -> palette.textSecondary
                    }
                    Text(
                        text = group.dateLabel,
                        color = headerColor,
                        style = TextStyle(
                            fontSize = 13.sp,
                            fontWeight = FontWeight.SemiBold,
                            fontFeatureSettings = "tnum",
                        ),
                        modifier = Modifier.padding(top = 4.dp, bottom = 2.dp),
                    )
                }

                items(group.goals, key = { "goal-${group.date}-${it.goalId}-${it.title}" }) { goalItem ->
                    val dotColor = parseHexColor(goalItem.colorHex, palette.accent)
                    Surface(
                        modifier = Modifier
                            .fillMaxWidth()
                            .clip(RoundedCornerShape(14.dp))
                            .nordClickable(pressedScale = 0.985f) { onOpenGoal(goalItem.goalId) },
                        shape = RoundedCornerShape(14.dp),
                        color = palette.surface2,
                        shadowElevation = 0.dp,
                        border = BorderStroke(1.dp, palette.borderSubtle),
                    ) {
                        Row(
                            modifier = Modifier
                                .fillMaxWidth()
                                .padding(horizontal = 14.dp, vertical = 12.dp),
                            verticalAlignment = Alignment.CenterVertically,
                            horizontalArrangement = Arrangement.spacedBy(10.dp),
                        ) {
                            Box(
                                modifier = Modifier
                                    .size(8.dp)
                                    .clip(CircleShape)
                                    .background(dotColor),
                            )
                            Column(
                                modifier = Modifier.weight(1f),
                                verticalArrangement = Arrangement.spacedBy(2.dp),
                            ) {
                                Text(
                                    text = goalItem.title,
                                    color = palette.textPrimary,
                                    fontSize = 14.sp,
                                    fontWeight = FontWeight.SemiBold,
                                    maxLines = 1,
                                    overflow = TextOverflow.Ellipsis,
                                )
                                goalItem.subtitle?.let { sub ->
                                    Text(
                                        text = sub,
                                        color = palette.textSecondary,
                                        fontSize = 11.5.sp,
                                        maxLines = 1,
                                        overflow = TextOverflow.Ellipsis,
                                    )
                                }
                            }
                            Text(
                                text = "目标",
                                color = palette.accent,
                                fontSize = 11.sp,
                                fontWeight = FontWeight.Medium,
                            )
                        }
                    }
                }

                items(group.schedules, key = { "sched-${group.date}-${it.id}" }) { card ->
                    ScheduleCard(
                        card = card,
                        onToggleDone = { onToggleDone(card.id) },
                        onClick = { onCardClick(card.id) },
                        modifier = Modifier.animateItem(
                            fadeInSpec = tween(150, easing = NordMotion.Enter),
                            placementSpec = tween(180, easing = NordMotion.Standard),
                            fadeOutSpec = tween(140, easing = NordMotion.Leave),
                        ),
                    )
                }
            }
        }
    }
}

@Stable
private class FutureDragState {
    var draggingCard by mutableStateOf<ScheduleCardUi?>(null)
    var sourceIndex by mutableIntStateOf(-1)
    var pointerInRoot by mutableStateOf(Offset.Zero)
    var grabOffsetInCard by mutableStateOf(Offset.Zero)
    var cardSize by mutableStateOf(IntSize.Zero)
    var targetInsertIndex by mutableIntStateOf(-1)
    var rootOriginInRoot by mutableStateOf(Offset.Zero)

    val cardBaseRects = HashMap<String, Rect>()
    var listViewportRect: Rect? = null

    val isDragging: Boolean
        get() = draggingCard != null

    fun reset() {
        draggingCard = null
        sourceIndex = -1
        pointerInRoot = Offset.Zero
        grabOffsetInCard = Offset.Zero
        cardSize = IntSize.Zero
        targetInsertIndex = -1
    }
}

@Composable
fun FutureScreen(
    schedules: List<ScheduleCardUi>,
    onToggleDone: (String) -> Unit,
    onCardClick: (String) -> Unit,
    onMoveToDate: (String, LocalDate?) -> Unit,
    onReorderSchedule: (String, Int) -> Unit = { _, _ -> },
) {
    val today = remember { LocalDate.now() }
    val tomorrow = remember(today) { today.plusDays(1) }
    val density = LocalDensity.current
    val haptics = rememberDayNewHaptics()
    val dragState = remember { FutureDragState() }
    val listState = rememberLazyListState()

    val latestSchedules by rememberUpdatedState(schedules)
    val latestOnReorder by rememberUpdatedState(onReorderSchedule)

    val itemSpacingPx = with(density) { 10.dp.toPx() }
    val edgeZonePx = with(density) { 64.dp.toPx() }
    val maxAutoScrollStepPx = with(density) { 14.dp.toPx() }

    LaunchedEffect(dragState.isDragging) {
        if (!dragState.isDragging) return@LaunchedEffect
        while (dragState.isDragging) {
            withFrameNanos { }
            val viewport = dragState.listViewportRect ?: continue
            val pointerY = dragState.pointerInRoot.y
            val delta = when {
                pointerY < viewport.top + edgeZonePx -> {
                    val ratio = ((viewport.top + edgeZonePx - pointerY) / edgeZonePx).coerceIn(0.2f, 1.5f)
                    -maxAutoScrollStepPx * ratio
                }
                pointerY > viewport.bottom - edgeZonePx -> {
                    val ratio = ((pointerY - (viewport.bottom - edgeZonePx)) / edgeZonePx).coerceIn(0.2f, 1.5f)
                    maxAutoScrollStepPx * ratio
                }
                else -> 0f
            }
            if (delta != 0f) {
                val scrolled = listState.scrollBy(delta)
                if (scrolled != 0f) {
                    dragState.targetInsertIndex = computeFutureInsertIndexFromLayout(
                        pointerY = dragState.pointerInRoot.y,
                        cards = latestSchedules,
                        sourceIndex = dragState.sourceIndex,
                        layoutInfo = listState.layoutInfo,
                        cardBaseRects = dragState.cardBaseRects,
                    )
                }
            }
        }
    }

    Box(
        modifier = Modifier
            .fillMaxSize()
            .onGloballyPositioned { coords ->
                dragState.rootOriginInRoot = coords.positionInRoot()
            }
            .pointerInput(Unit) {
                detectDragGesturesAfterLongPress(
                    onDragStart = { localOffset ->
                        val list = latestSchedules
                        val pointer = dragState.rootOriginInRoot + localOffset
                        val hit = listState.layoutInfo.visibleItemsInfo.firstNotNullOfOrNull { vis ->
                            val candidate = list.getOrNull(vis.index) ?: return@firstNotNullOfOrNull null
                            val rect = dragState.cardBaseRects[candidate.id] ?: return@firstNotNullOfOrNull null
                            if (rect.contains(pointer)) Triple(vis.index, candidate, rect) else null
                        } ?: return@detectDragGesturesAfterLongPress

                        val (startIdx, card, baseRect) = hit
                        haptics.longPress()
                        dragState.draggingCard = card
                        dragState.sourceIndex = startIdx
                        dragState.grabOffsetInCard = pointer - baseRect.topLeft
                        dragState.cardSize = IntSize(
                            baseRect.width.toInt(),
                            baseRect.height.toInt(),
                        )
                        dragState.pointerInRoot = pointer
                        dragState.targetInsertIndex = startIdx
                    },
                    onDrag = { change, dragAmount ->
                        change.consume()
                        if (!dragState.isDragging) return@detectDragGesturesAfterLongPress
                        dragState.pointerInRoot += dragAmount
                        val nextIdx = computeFutureInsertIndexFromLayout(
                            pointerY = dragState.pointerInRoot.y,
                            cards = latestSchedules,
                            sourceIndex = dragState.sourceIndex,
                            layoutInfo = listState.layoutInfo,
                            cardBaseRects = dragState.cardBaseRects,
                        )
                        if (nextIdx != dragState.targetInsertIndex) {
                            haptics.tick()
                            dragState.targetInsertIndex = nextIdx
                        }
                    },
                    onDragEnd = {
                        val dragged = dragState.draggingCard
                        val fromIdx = dragState.sourceIndex
                        val insertIdx = dragState.targetInsertIndex
                        dragState.reset()
                        if (dragged != null && insertIdx >= 0 && insertIdx != fromIdx) {
                            latestOnReorder(dragged.id, insertIdx)
                        }
                    },
                    onDragCancel = {
                        dragState.reset()
                    },
                )
            },
    ) {
        LazyColumn(
            state = listState,
            modifier = Modifier
                .fillMaxSize()
                .onGloballyPositioned { coords ->
                    dragState.listViewportRect = coords.boundsInRoot()
                },
            contentPadding = PaddingValues(16.dp),
            verticalArrangement = Arrangement.spacedBy(10.dp),
            userScrollEnabled = !dragState.isDragging,
        ) {
            if (schedules.isEmpty()) {
                item { EmptyPlaceholder("暂无不定日期事项，点右下角「+」可先收进未来事项稍后再排期。") }
            } else {
                itemsIndexed(schedules, key = { _, item -> item.id }) { index, card ->
                    val isDraggedSource = dragState.isDragging && dragState.draggingCard?.id == card.id
                    val shiftTargetPx = computeFutureGapShiftPx(
                        dragState = dragState,
                        itemIndex = index,
                        totalCount = schedules.size,
                        itemSpacingPx = itemSpacingPx,
                    )
                    val animatedShiftPx by animateFloatAsState(
                        targetValue = shiftTargetPx,
                        animationSpec = tween(durationMillis = 120, easing = NordMotion.Standard),
                        label = "futureGapShift_${card.id}",
                    )

                    Box(
                        modifier = Modifier
                            .fillMaxWidth()
                            .animateItem(
                                fadeInSpec = tween(150, easing = NordMotion.Enter),
                                placementSpec = if (dragState.isDragging) null else tween(180, easing = NordMotion.Standard),
                                fadeOutSpec = tween(140, easing = NordMotion.Leave),
                            )
                            .onGloballyPositioned { coords ->
                                dragState.cardBaseRects[card.id] = coords.boundsInRoot()
                            },
                    ) {
                        Column(
                            modifier = Modifier
                                .fillMaxWidth()
                                .graphicsLayer {
                                    translationY = if (isDraggedSource) 0f else animatedShiftPx
                                    alpha = if (isDraggedSource) 0f else 1f
                                },
                            verticalArrangement = Arrangement.spacedBy(6.dp),
                        ) {
                            ScheduleCard(
                                card = card,
                                onToggleDone = { onToggleDone(card.id) },
                                onClick = { onCardClick(card.id) },
                            )
                            if (!card.dto.done) {
                                Row(
                                    modifier = Modifier.fillMaxWidth(),
                                    horizontalArrangement = Arrangement.spacedBy(8.dp),
                                ) {
                                    QuickMoveButton(
                                        label = "移到今天",
                                        modifier = Modifier.weight(1f),
                                        onClick = {
                                            haptics.tap()
                                            onMoveToDate(card.id, today)
                                        },
                                    )
                                    QuickMoveButton(
                                        label = "移到明天",
                                        modifier = Modifier.weight(1f),
                                        onClick = {
                                            haptics.tap()
                                            onMoveToDate(card.id, tomorrow)
                                        },
                                    )
                                }
                            }
                        }
                    }
                }
            }
        }

        val ghost = dragState.draggingCard
        if (ghost != null) {
            val ghostWidthDp = with(density) { dragState.cardSize.width.toDp() }
            val tx = dragState.pointerInRoot.x - dragState.rootOriginInRoot.x - dragState.grabOffsetInCard.x
            val ty = dragState.pointerInRoot.y - dragState.rootOriginInRoot.y - dragState.grabOffsetInCard.y

            Box(
                modifier = Modifier
                    .zIndex(20f)
                    .width(ghostWidthDp)
                    .graphicsLayer {
                        translationX = tx
                        translationY = ty
                        scaleX = 1.02f
                        scaleY = 1.02f
                        alpha = 0.96f
                    },
            ) {
                ScheduleCard(
                    card = ghost,
                    onToggleDone = {},
                    onClick = {},
                    isDraggingGhost = true,
                )
            }
        }
    }
}

private fun computeFutureInsertIndexFromLayout(
    pointerY: Float,
    cards: List<ScheduleCardUi>,
    sourceIndex: Int,
    layoutInfo: LazyListLayoutInfo,
    cardBaseRects: Map<String, Rect>,
): Int {
    val maxSlot = (cards.size - 1).coerceAtLeast(0)
    if (sourceIndex !in cards.indices) return -1
    val visible = layoutInfo.visibleItemsInfo.filter { it.index in cards.indices && it.index != sourceIndex }
    if (visible.isEmpty()) return sourceIndex.coerceIn(0, maxSlot)

    for (vis in visible) {
        val rect = cardBaseRects[cards[vis.index].id] ?: continue
        if (pointerY <= rect.center.y) {
            val slot = if (vis.index < sourceIndex) vis.index else vis.index - 1
            return slot.coerceIn(0, maxSlot)
        }
    }
    val lastIdx = visible.last().index
    val afterLast = if (lastIdx < sourceIndex) lastIdx + 1 else lastIdx
    return afterLast.coerceIn(0, maxSlot)
}

private fun computeFutureGapShiftPx(
    dragState: FutureDragState,
    itemIndex: Int,
    totalCount: Int,
    itemSpacingPx: Float,
): Float {
    if (!dragState.isDragging) return 0f
    val sourceIdx = dragState.sourceIndex
    val insertIdx = dragState.targetInsertIndex
    if (sourceIdx !in 0 until totalCount) return 0f
    if (insertIdx !in 0 until totalCount) return 0f
    if (itemIndex == sourceIdx) return 0f

    val slotDelta = dragState.cardSize.height.toFloat() + itemSpacingPx
    val logicalIdx = if (itemIndex < sourceIdx) itemIndex else itemIndex - 1
    val targetVisualIdx = if (logicalIdx < insertIdx) logicalIdx else logicalIdx + 1
    return (targetVisualIdx - itemIndex) * slotDelta
}

@Composable
private fun QuickMoveButton(
    label: String,
    modifier: Modifier = Modifier,
    onClick: () -> Unit,
) {
    val palette = LocalNordPalette.current
    Box(
        modifier = modifier
            .defaultMinSize(minHeight = 36.dp)
            .clip(RoundedCornerShape(10.dp))
            .background(palette.surface3)
            .border(1.dp, palette.borderSubtle, RoundedCornerShape(10.dp))
            .nordClickable(pressedScale = 0.97f, onClick = onClick)
            .padding(horizontal = 10.dp, vertical = 7.dp),
        contentAlignment = Alignment.Center,
    ) {
        Text(
            text = label,
            color = palette.accent,
            fontSize = 12.sp,
            fontWeight = FontWeight.Medium,
        )
    }
}

@Composable
fun EisenhowerScreen(
    buckets: List<EisenhowerBucketUi>,
    nextThreeDaysOnly: Boolean,
    onToggleNextThreeDaysOnly: (Boolean) -> Unit,
    onToggleDone: (String) -> Unit,
    onCardClick: (String) -> Unit,
) {
    val palette = LocalNordPalette.current
    val haptics = rememberDayNewHaptics()
    var collapsedKinds by remember { mutableStateOf(setOf(EisenhowerKind.UNMARKED)) }

    Column(modifier = Modifier.fillMaxSize()) {
        Surface(
            color = palette.surface1,
            shadowElevation = 0.dp,
        ) {
            Box(
                modifier = Modifier
                    .fillMaxWidth()
                    .padding(horizontal = 16.dp, vertical = 8.dp),
            ) {
                Surface(
                    modifier = Modifier.fillMaxWidth(),
                    shape = RoundedCornerShape(14.dp),
                    color = palette.surface3,
                    shadowElevation = 0.dp,
                    border = BorderStroke(1.dp, palette.borderSubtle),
                ) {
                    Row(
                        modifier = Modifier
                            .fillMaxWidth()
                            .padding(3.dp),
                        horizontalArrangement = Arrangement.spacedBy(4.dp),
                    ) {
                        listOf(true to "未来 3 天", false to "全部未完成").forEach { (onlyThreeDays, title) ->
                            val selected = nextThreeDaysOnly == onlyThreeDays
                            Box(
                                modifier = Modifier
                                    .weight(1f)
                                    .defaultMinSize(minHeight = 36.dp)
                                    .clip(RoundedCornerShape(10.dp))
                                    .background(if (selected) palette.surface2 else Color.Transparent)
                                    .border(
                                        width = 1.dp,
                                        color = if (selected) palette.borderStrong else Color.Transparent,
                                        shape = RoundedCornerShape(10.dp),
                                    )
                                    .clickable {
                                        haptics.tap()
                                        onToggleNextThreeDaysOnly(onlyThreeDays)
                                    }
                                    .padding(horizontal = 8.dp, vertical = 7.dp),
                                contentAlignment = Alignment.Center,
                            ) {
                                Text(
                                    text = title,
                                    color = if (selected) palette.textPrimary else palette.textSecondary,
                                    fontSize = 12.5.sp,
                                    fontWeight = if (selected) FontWeight.SemiBold else FontWeight.Medium,
                                    maxLines = 1,
                                    overflow = TextOverflow.Ellipsis,
                                )
                            }
                        }
                    }
                }
            }
        }

        HorizontalDivider(thickness = 1.dp, color = palette.borderSubtle)

        LazyColumn(
            modifier = Modifier
                .weight(1f)
                .fillMaxWidth(),
            contentPadding = PaddingValues(16.dp),
            verticalArrangement = Arrangement.spacedBy(10.dp),
        ) {
            items(buckets, key = { it.kind.name }) { bucket ->
                val accent = when (bucket.kind) {
                    EisenhowerKind.URGENT_IMPORTANT -> palette.danger
                    EisenhowerKind.IMPORTANT_ONLY -> palette.accent
                    EisenhowerKind.URGENT_ONLY -> palette.warning
                    EisenhowerKind.UNMARKED -> palette.textSecondary
                }
                val isCollapsed = bucket.kind in collapsedKinds

                Surface(
                    shape = RoundedCornerShape(14.dp),
                    color = palette.surface2,
                    shadowElevation = 0.dp,
                    border = BorderStroke(1.dp, palette.borderSubtle),
                ) {
                    Column(modifier = Modifier.fillMaxWidth()) {
                        Row(
                            modifier = Modifier
                                .fillMaxWidth()
                                .clickable {
                                    collapsedKinds = if (isCollapsed) {
                                        collapsedKinds - bucket.kind
                                    } else {
                                        collapsedKinds + bucket.kind
                                    }
                                }
                                .padding(horizontal = 14.dp, vertical = 12.dp),
                            horizontalArrangement = Arrangement.SpaceBetween,
                            verticalAlignment = Alignment.CenterVertically,
                        ) {
                            Row(
                                verticalAlignment = Alignment.CenterVertically,
                                horizontalArrangement = Arrangement.spacedBy(8.dp),
                            ) {
                                Box(
                                    modifier = Modifier
                                        .size(8.dp)
                                        .clip(CircleShape)
                                        .background(accent),
                                )
                                Column {
                                    Text(
                                        text = bucket.title,
                                        color = palette.textPrimary,
                                        fontSize = 14.sp,
                                        fontWeight = FontWeight.SemiBold,
                                    )
                                    Text(
                                        text = bucket.subtitle,
                                        color = palette.textSecondary,
                                        fontSize = 11.5.sp,
                                    )
                                }
                            }
                            Text(
                                text = bucket.schedules.size.toString(),
                                color = accent,
                                style = TextStyle(
                                    fontSize = 12.sp,
                                    fontWeight = FontWeight.SemiBold,
                                    fontFeatureSettings = "tnum",
                                ),
                            )
                        }

                        AnimatedVisibility(visible = !isCollapsed) {
                            Column(
                                modifier = Modifier
                                    .fillMaxWidth()
                                    .padding(start = 10.dp, end = 10.dp, bottom = 10.dp),
                                verticalArrangement = Arrangement.spacedBy(8.dp),
                            ) {
                                if (bucket.schedules.isEmpty()) {
                                    Text(
                                        text = if (nextThreeDaysOnly) "未来 3 天内暂无未完成日程" else "该象限暂无未完成日程",
                                        color = palette.textSecondary,
                                        fontSize = 12.sp,
                                        modifier = Modifier.padding(horizontal = 4.dp, vertical = 4.dp),
                                    )
                                } else {
                                    bucket.schedules.forEach { card ->
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
        }
    }
}

@Composable
fun TagsHubScreen(
    selectedSection: TagPageSection,
    tagGroups: List<TagGroupUi>,
    personGroups: List<PersonGroupUi>,
    locationGroups: List<LocationGroupUi>,
    onSelectSection: (TagPageSection) -> Unit,
    onToggleDone: (String) -> Unit,
    onCardClick: (String) -> Unit,
    onOpenEditTag: (String) -> Unit,
    onCreateTag: () -> Unit,
    onOpenEditPerson: (String) -> Unit,
    onCreatePerson: () -> Unit,
    onOpenEditLocation: (String) -> Unit,
    onCreateLocation: () -> Unit,
) {
    val palette = LocalNordPalette.current
    val haptics = rememberDayNewHaptics()

    Column(modifier = Modifier.fillMaxSize()) {
        // 顶部三段圆润胶囊切换条：标签 | 人物 | 地点
        Surface(
            color = palette.surface1,
            shadowElevation = 0.dp,
        ) {
            Box(
                modifier = Modifier
                    .fillMaxWidth()
                    .padding(horizontal = 12.dp, vertical = 8.dp),
            ) {
                Surface(
                    modifier = Modifier.fillMaxWidth(),
                    shape = RoundedCornerShape(14.dp),
                    color = palette.surface3,
                    shadowElevation = 0.dp,
                    border = BorderStroke(1.dp, palette.borderSubtle),
                ) {
                    Row(
                        modifier = Modifier
                            .fillMaxWidth()
                            .padding(3.dp),
                        horizontalArrangement = Arrangement.spacedBy(4.dp),
                    ) {
                        TagPageSection.entries.forEach { section ->
                            val selected = selectedSection == section
                            Box(
                                modifier = Modifier
                                    .weight(1f)
                                    .defaultMinSize(minHeight = 36.dp)
                                    .clip(RoundedCornerShape(10.dp))
                                    .background(if (selected) palette.surface2 else Color.Transparent)
                                    .border(
                                        width = 1.dp,
                                        color = if (selected) palette.borderStrong else Color.Transparent,
                                        shape = RoundedCornerShape(10.dp),
                                    )
                                    .clickable {
                                        haptics.tap()
                                        onSelectSection(section)
                                    }
                                    .padding(horizontal = 6.dp, vertical = 7.dp),
                                contentAlignment = Alignment.Center,
                            ) {
                                Text(
                                    text = section.title,
                                    color = if (selected) palette.textPrimary else palette.textSecondary,
                                    fontSize = 12.5.sp,
                                    fontWeight = if (selected) FontWeight.SemiBold else FontWeight.Medium,
                                    maxLines = 1,
                                    overflow = TextOverflow.Ellipsis,
                                )
                            }
                        }
                    }
                }
            }
        }

        HorizontalDivider(thickness = 1.dp, color = palette.borderSubtle)

        val sectionSlidePx = with(LocalDensity.current) { 8.dp.roundToPx() }
        AnimatedContent(
            targetState = selectedSection,
            transitionSpec = {
                val forward = targetState.ordinal >= initialState.ordinal
                val enterOffset = if (forward) sectionSlidePx else -sectionSlidePx
                val exitOffset = if (forward) -sectionSlidePx else sectionSlidePx
                (fadeIn(tween(90, easing = NordMotion.Enter)) +
                    slideInHorizontally(tween(90, easing = NordMotion.Enter)) { enterOffset }) togetherWith
                    (fadeOut(tween(60, easing = NordMotion.Leave)) +
                        slideOutHorizontally(tween(60, easing = NordMotion.Leave)) { exitOffset })
            },
            label = "tagSectionTransition",
        ) { section ->
            when (section) {
                TagPageSection.TAGS -> TagsList(
                    tagGroups = tagGroups,
                    onToggleDone = onToggleDone,
                    onCardClick = onCardClick,
                    onOpenEditTag = onOpenEditTag,
                    onCreateTag = onCreateTag,
                )
                TagPageSection.PERSONS -> SimpleTaxonomyList(
                    createLabel = "新建人物",
                    emptyMessage = "暂无人物，点上方「新建人物」或右下角「+」创建后即可在日程上关联。",
                    emptyItemMessage = "暂无关联日程",
                    editDescription = "编辑人物",
                    icon = DayNewIcons.Person,
                    entries = personGroups.map {
                        TaxonomyGroupItem(
                            id = it.person.id,
                            name = it.person.name,
                            schedules = it.schedules,
                        )
                    },
                    onCreate = onCreatePerson,
                    onEdit = onOpenEditPerson,
                    onToggleDone = onToggleDone,
                    onCardClick = onCardClick,
                )
                TagPageSection.LOCATIONS -> SimpleTaxonomyList(
                    createLabel = "新建地点",
                    emptyMessage = "暂无地点，点上方「新建地点」或右下角「+」创建后即可在日程上关联。",
                    emptyItemMessage = "暂无关联日程",
                    editDescription = "编辑地点",
                    icon = DayNewIcons.Location,
                    entries = locationGroups.map {
                        TaxonomyGroupItem(
                            id = it.location.id,
                            name = it.location.name,
                            schedules = it.schedules,
                        )
                    },
                    onCreate = onCreateLocation,
                    onEdit = onOpenEditLocation,
                    onToggleDone = onToggleDone,
                    onCardClick = onCardClick,
                )
            }
        }
    }
}

private data class TaxonomyGroupItem(
    val id: String,
    val name: String,
    val schedules: List<ScheduleCardUi>,
)

@Composable
private fun TagsList(
    tagGroups: List<TagGroupUi>,
    onToggleDone: (String) -> Unit,
    onCardClick: (String) -> Unit,
    onOpenEditTag: (String) -> Unit,
    onCreateTag: () -> Unit,
) {
    val palette = LocalNordPalette.current
    var expandedTagIds by remember { mutableStateOf<Set<String>>(emptySet()) }

    LazyColumn(
        modifier = Modifier.fillMaxSize(),
        contentPadding = PaddingValues(16.dp),
        verticalArrangement = Arrangement.spacedBy(10.dp),
    ) {
        item {
            CreateRowCard(
                label = "新建标签",
                icon = DayNewIcons.Tag,
                onClick = onCreateTag,
            )
        }

        if (tagGroups.isEmpty()) {
            item { EmptyPlaceholder("暂无标签，点上方「新建标签」创建后即可在日程上打标。") }
        } else {
            items(tagGroups, key = { it.tag.id }) { group ->
                val color = parseHexColor(group.tag.color, palette.accent)
                val isExpanded = group.tag.id in expandedTagIds

                Surface(
                    shape = RoundedCornerShape(14.dp),
                    color = palette.surface2,
                    shadowElevation = 0.dp,
                    border = BorderStroke(1.dp, palette.borderSubtle),
                ) {
                    Column(modifier = Modifier.fillMaxWidth()) {
                        Row(
                            modifier = Modifier
                                .fillMaxWidth()
                                .clickable {
                                    expandedTagIds = if (isExpanded) {
                                        expandedTagIds - group.tag.id
                                    } else {
                                        expandedTagIds + group.tag.id
                                    }
                                }
                                .padding(start = 14.dp, end = 4.dp, top = 6.dp, bottom = 6.dp),
                            verticalAlignment = Alignment.CenterVertically,
                        ) {
                            Box(
                                modifier = Modifier
                                    .size(8.dp)
                                    .clip(CircleShape)
                                    .background(color),
                            )
                            Text(
                                text = group.tag.name,
                                color = palette.textPrimary,
                                fontSize = 14.sp,
                                fontWeight = FontWeight.SemiBold,
                                maxLines = 1,
                                overflow = TextOverflow.Ellipsis,
                                modifier = Modifier
                                    .weight(1f)
                                    .padding(horizontal = 10.dp),
                            )
                            Text(
                                text = "${group.schedules.count { !it.dto.done }}/${group.schedules.size}",
                                color = palette.textSecondary,
                                style = TextStyle(
                                    fontSize = 12.sp,
                                    fontFeatureSettings = "tnum",
                                ),
                            )
                            IconButton(
                                onClick = { onOpenEditTag(group.tag.id) },
                                modifier = Modifier.defaultMinSize(minWidth = 48.dp, minHeight = 48.dp),
                            ) {
                                Icon(
                                    imageVector = DayNewIcons.Edit,
                                    contentDescription = "编辑标签",
                                    tint = palette.textSecondary,
                                    modifier = Modifier.size(18.dp),
                                )
                            }
                        }

                        AnimatedVisibility(visible = isExpanded) {
                            Column(
                                modifier = Modifier
                                    .fillMaxWidth()
                                    .padding(start = 10.dp, end = 10.dp, bottom = 10.dp),
                                verticalArrangement = Arrangement.spacedBy(8.dp),
                            ) {
                                if (group.schedules.isEmpty()) {
                                    Text(
                                        text = "暂无关联日程",
                                        color = palette.textSecondary,
                                        fontSize = 12.sp,
                                        modifier = Modifier.padding(horizontal = 4.dp, vertical = 4.dp),
                                    )
                                } else {
                                    group.schedules.forEach { card ->
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
        }
    }
}

@Composable
private fun SimpleTaxonomyList(
    createLabel: String,
    emptyMessage: String,
    emptyItemMessage: String,
    editDescription: String,
    icon: ImageVector,
    entries: List<TaxonomyGroupItem>,
    onCreate: () -> Unit,
    onEdit: (String) -> Unit,
    onToggleDone: (String) -> Unit,
    onCardClick: (String) -> Unit,
) {
    val palette = LocalNordPalette.current
    var expandedIds by remember { mutableStateOf<Set<String>>(emptySet()) }

    LazyColumn(
        modifier = Modifier.fillMaxSize(),
        contentPadding = PaddingValues(16.dp),
        verticalArrangement = Arrangement.spacedBy(10.dp),
    ) {
        item {
            CreateRowCard(
                label = createLabel,
                icon = icon,
                onClick = onCreate,
            )
        }

        if (entries.isEmpty()) {
            item { EmptyPlaceholder(emptyMessage) }
        } else {
            items(entries, key = { it.id }) { item ->
                val isExpanded = item.id in expandedIds
                Surface(
                    shape = RoundedCornerShape(14.dp),
                    color = palette.surface2,
                    shadowElevation = 0.dp,
                    border = BorderStroke(1.dp, palette.borderSubtle),
                ) {
                    Column(modifier = Modifier.fillMaxWidth()) {
                        Row(
                            modifier = Modifier
                                .fillMaxWidth()
                                .clickable {
                                    expandedIds = if (isExpanded) {
                                        expandedIds - item.id
                                    } else {
                                        expandedIds + item.id
                                    }
                                }
                                .padding(start = 14.dp, end = 4.dp, top = 6.dp, bottom = 6.dp),
                            verticalAlignment = Alignment.CenterVertically,
                        ) {
                            Icon(
                                imageVector = icon,
                                contentDescription = null,
                                tint = palette.accent,
                                modifier = Modifier.size(16.dp),
                            )
                            Text(
                                text = item.name,
                                color = palette.textPrimary,
                                fontSize = 14.sp,
                                fontWeight = FontWeight.SemiBold,
                                maxLines = 1,
                                overflow = TextOverflow.Ellipsis,
                                modifier = Modifier
                                    .weight(1f)
                                    .padding(horizontal = 10.dp),
                            )
                            Text(
                                text = "${item.schedules.count { !it.dto.done }}/${item.schedules.size}",
                                color = palette.textSecondary,
                                style = TextStyle(
                                    fontSize = 12.sp,
                                    fontFeatureSettings = "tnum",
                                ),
                            )
                            IconButton(
                                onClick = { onEdit(item.id) },
                                modifier = Modifier.defaultMinSize(minWidth = 48.dp, minHeight = 48.dp),
                            ) {
                                Icon(
                                    imageVector = DayNewIcons.Edit,
                                    contentDescription = editDescription,
                                    tint = palette.textSecondary,
                                    modifier = Modifier.size(18.dp),
                                )
                            }
                        }

                        AnimatedVisibility(visible = isExpanded) {
                            Column(
                                modifier = Modifier
                                    .fillMaxWidth()
                                    .padding(start = 10.dp, end = 10.dp, bottom = 10.dp),
                                verticalArrangement = Arrangement.spacedBy(8.dp),
                            ) {
                                if (item.schedules.isEmpty()) {
                                    Text(
                                        text = emptyItemMessage,
                                        color = palette.textSecondary,
                                        fontSize = 12.sp,
                                        modifier = Modifier.padding(horizontal = 4.dp, vertical = 4.dp),
                                    )
                                } else {
                                    item.schedules.forEach { card ->
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
        }
    }
}

@Composable
private fun CreateRowCard(
    label: String,
    icon: ImageVector,
    onClick: () -> Unit,
) {
    val palette = LocalNordPalette.current
    Surface(
        modifier = Modifier
            .fillMaxWidth()
            .clip(RoundedCornerShape(14.dp))
            .nordClickable(pressedScale = 0.985f, onClick = onClick),
        shape = RoundedCornerShape(14.dp),
        color = palette.surface2,
        shadowElevation = 0.dp,
        border = BorderStroke(1.dp, palette.borderSubtle),
    ) {
        Row(
            modifier = Modifier
                .fillMaxWidth()
                .defaultMinSize(minHeight = 48.dp)
                .padding(horizontal = 14.dp, vertical = 10.dp),
            horizontalArrangement = Arrangement.SpaceBetween,
            verticalAlignment = Alignment.CenterVertically,
        ) {
            Row(
                verticalAlignment = Alignment.CenterVertically,
                horizontalArrangement = Arrangement.spacedBy(8.dp),
            ) {
                Icon(
                    imageVector = icon,
                    contentDescription = null,
                    tint = palette.accent,
                    modifier = Modifier.size(18.dp),
                )
                Text(
                    text = label,
                    color = palette.accent,
                    fontSize = 14.sp,
                    fontWeight = FontWeight.SemiBold,
                )
            }
            Icon(
                imageVector = DayNewIcons.Add,
                contentDescription = null,
                tint = palette.accent,
                modifier = Modifier.size(18.dp),
            )
        }
    }
}

@Composable
private fun EmptyPlaceholder(message: String) {
    val palette = LocalNordPalette.current
    Surface(
        shape = RoundedCornerShape(14.dp),
        color = palette.surface2,
        shadowElevation = 0.dp,
        border = BorderStroke(1.dp, palette.borderSubtle),
    ) {
        Text(
            text = message,
            color = palette.textSecondary,
            fontSize = 13.sp,
            modifier = Modifier
                .fillMaxWidth()
                .padding(16.dp),
        )
    }
}
