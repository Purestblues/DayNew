package com.daynew.app.ui.components

import androidx.compose.animation.core.animateFloatAsState
import androidx.compose.animation.core.tween
import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.background
import androidx.compose.foundation.gestures.detectDragGesturesAfterLongPress
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.itemsIndexed
import androidx.compose.foundation.pager.HorizontalPager
import androidx.compose.foundation.pager.rememberPagerState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.Stable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateMapOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberUpdatedState
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Rect
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.hapticfeedback.HapticFeedbackType
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.layout.boundsInRoot
import androidx.compose.ui.layout.onGloballyPositioned
import androidx.compose.ui.layout.positionInRoot
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.platform.LocalHapticFeedback
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.IntSize
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.compose.ui.zIndex
import com.daynew.app.data.LaneKind
import com.daynew.app.ui.main.ScheduleCardUi
import com.daynew.app.ui.theme.LocalNordPalette
import com.daynew.app.ui.theme.NordMotion

@Stable
private class OverviewDragState {
    var draggingCard by mutableStateOf<ScheduleCardUi?>(null)
    var sourceLane by mutableStateOf<LaneKind?>(null)
    var sourceIndex by mutableIntStateOf(-1)
    var pointerInRoot by mutableStateOf(Offset.Zero)
    var grabOffsetInCard by mutableStateOf(Offset.Zero)
    var cardSize by mutableStateOf(IntSize.Zero)
    var hoveredLane by mutableStateOf<LaneKind?>(null)
    var targetInsertIndex by mutableIntStateOf(-1)
    var rootOriginInRoot by mutableStateOf(Offset.Zero)

    val laneRects = mutableStateMapOf<LaneKind, Rect>()
    val cardBaseRects = mutableStateMapOf<String, Rect>()

    val isDragging: Boolean
        get() = draggingCard != null

    fun reset() {
        draggingCard = null
        sourceLane = null
        sourceIndex = -1
        pointerInRoot = Offset.Zero
        grabOffsetInCard = Offset.Zero
        cardSize = IntSize.Zero
        hoveredLane = null
        targetInsertIndex = -1
    }
}

/**
 * 竖屏五档位看板：
 * - 顶部五档位胶囊支持点击切换，也作为拖拽改 Due 的放置靶区；
 * - HorizontalPager 支持左右横滑切档（拖拽中自动锁定）；
 * - 长按卡片拖拽时同档位卡片采用未平移动前基准坐标计算插槽，并以 120ms graphicsLayer 开缝让位。
 */
@Composable
fun OverviewDragBoard(
    selectedLane: LaneKind,
    lanes: Map<LaneKind, List<ScheduleCardUi>>,
    lanePendingCounts: Map<LaneKind, Int>,
    onSelectLane: (LaneKind) -> Unit,
    onMoveSchedule: (scheduleId: String, targetLane: LaneKind, targetIndex: Int) -> Unit,
    onToggleDone: (scheduleId: String) -> Unit,
    onCardClick: (scheduleId: String) -> Unit,
    modifier: Modifier = Modifier,
) {
    val palette = LocalNordPalette.current
    val haptic = LocalHapticFeedback.current
    val density = LocalDensity.current
    val dragState = remember { OverviewDragState() }

    val pagerState = rememberPagerState(
        initialPage = LaneKind.entries.indexOf(selectedLane).coerceAtLeast(0),
        pageCount = { LaneKind.entries.size },
    )

    val latestSelectedLane by rememberUpdatedState(selectedLane)
    val latestLanes by rememberUpdatedState(lanes)
    val latestOnSelectLane by rememberUpdatedState(onSelectLane)
    val latestOnMoveSchedule by rememberUpdatedState(onMoveSchedule)

    LaunchedEffect(selectedLane) {
        val targetPage = LaneKind.entries.indexOf(selectedLane)
        if (targetPage >= 0 && pagerState.currentPage != targetPage) {
            pagerState.animateScrollToPage(
                page = targetPage,
                animationSpec = tween(NordMotion.M, easing = NordMotion.Standard),
            )
        }
    }

    LaunchedEffect(pagerState.settledPage) {
        val pageLane = LaneKind.entries.getOrNull(pagerState.settledPage) ?: return@LaunchedEffect
        if (pageLane != latestSelectedLane) {
            latestOnSelectLane(pageLane)
        }
    }

    val itemSpacingPx = with(density) { 8.dp.toPx() }
    val hitMarginPx = with(density) { 14.dp.toPx() }

    Box(
        modifier = modifier
            .fillMaxSize()
            .onGloballyPositioned { coords ->
                dragState.rootOriginInRoot = coords.positionInRoot()
            },
    ) {
        Column(modifier = Modifier.fillMaxSize()) {
            LaneStrip(
                selectedLane = selectedLane,
                hoveredLane = dragState.hoveredLane,
                isDragging = dragState.isDragging,
                pendingCounts = lanePendingCounts,
                onSelectLane = onSelectLane,
                onLaneBoundsChanged = { lane, rect ->
                    dragState.laneRects[lane] = rect
                },
            )
            HorizontalDivider(thickness = 1.dp, color = palette.borderSubtle)

            HorizontalPager(
                state = pagerState,
                userScrollEnabled = !dragState.isDragging,
                modifier = Modifier
                    .fillMaxWidth()
                    .weight(1f),
            ) { page ->
                val lane = LaneKind.entries[page]
                val cards = lanes[lane].orEmpty()

                if (cards.isEmpty()) {
                    EmptyLanePlaceholder(lane = lane)
                } else {
                    LazyColumn(
                        modifier = Modifier.fillMaxSize(),
                        contentPadding = PaddingValues(
                            start = 16.dp,
                            end = 16.dp,
                            top = 12.dp,
                            bottom = 96.dp,
                        ),
                        verticalArrangement = Arrangement.spacedBy(8.dp),
                        userScrollEnabled = !dragState.isDragging,
                    ) {
                        itemsIndexed(
                            items = cards,
                            key = { _, item -> item.id },
                        ) { index, card ->
                            val isDraggedSource =
                                dragState.isDragging &&
                                    dragState.draggingCard?.id == card.id &&
                                    dragState.sourceLane == lane

                            val shiftTargetPx = computeGapShiftPx(
                                dragState = dragState,
                                currentLane = lane,
                                itemIndex = index,
                                totalCount = cards.size,
                                itemSpacingPx = itemSpacingPx,
                            )

                            val animatedShiftPx by animateFloatAsState(
                                targetValue = shiftTargetPx,
                                animationSpec = tween(durationMillis = 120, easing = NordMotion.Standard),
                                label = "gapShift_${card.id}",
                            )

                            // 外层 Box 不做 translationY，专门用于测量稳定基准布局坐标，彻底杜绝开缝闪烁
                            Box(
                                modifier = Modifier
                                    .fillMaxWidth()
                                    .onGloballyPositioned { coords ->
                                        dragState.cardBaseRects[card.id] = coords.boundsInRoot()
                                    }
                                    .pointerInput(card.id, lane) {
                                        detectDragGesturesAfterLongPress(
                                            onDragStart = { localOffset ->
                                                val laneCards = latestLanes[lane].orEmpty()
                                                val startIndex = laneCards.indexOfFirst { it.id == card.id }
                                                val baseRect = dragState.cardBaseRects[card.id]
                                                if (startIndex >= 0 && baseRect != null) {
                                                    haptic.performHapticFeedback(HapticFeedbackType.LongPress)
                                                    dragState.draggingCard = card
                                                    dragState.sourceLane = lane
                                                    dragState.sourceIndex = startIndex
                                                    dragState.grabOffsetInCard = localOffset
                                                    dragState.cardSize = IntSize(
                                                        baseRect.width.toInt(),
                                                        baseRect.height.toInt(),
                                                    )
                                                    dragState.pointerInRoot = baseRect.topLeft + localOffset
                                                    dragState.hoveredLane = null
                                                    dragState.targetInsertIndex = startIndex
                                                }
                                            },
                                            onDrag = { change, dragAmount ->
                                                change.consume()
                                                if (!dragState.isDragging) return@detectDragGesturesAfterLongPress
                                                dragState.pointerInRoot += dragAmount

                                                val hitLane = findHitLane(
                                                    pointerInRoot = dragState.pointerInRoot,
                                                    laneRects = dragState.laneRects,
                                                    marginPx = hitMarginPx,
                                                )
                                                if (hitLane != dragState.hoveredLane) {
                                                    if (hitLane != null) {
                                                        haptic.performHapticFeedback(HapticFeedbackType.TextHandleMove)
                                                    }
                                                    dragState.hoveredLane = hitLane
                                                }

                                                if (hitLane != null) {
                                                    dragState.targetInsertIndex = -1
                                                } else {
                                                    val laneCards = latestLanes[lane].orEmpty()
                                                    dragState.targetInsertIndex = computeTargetInsertIndex(
                                                        pointerY = dragState.pointerInRoot.y,
                                                        cards = laneCards,
                                                        sourceIndex = dragState.sourceIndex,
                                                        cardBaseRects = dragState.cardBaseRects,
                                                    )
                                                }
                                            },
                                            onDragEnd = {
                                                val dragged = dragState.draggingCard
                                                val fromLane = dragState.sourceLane
                                                val fromIdx = dragState.sourceIndex
                                                val dropLane = dragState.hoveredLane
                                                val insertIdx = dragState.targetInsertIndex
                                                dragState.reset()

                                                if (dragged != null && fromLane != null) {
                                                    when {
                                                        dropLane != null && dropLane != fromLane -> {
                                                            latestOnMoveSchedule(dragged.id, dropLane, 0)
                                                        }
                                                        dropLane == null && insertIdx >= 0 && insertIdx != fromIdx -> {
                                                            latestOnMoveSchedule(dragged.id, fromLane, insertIdx)
                                                        }
                                                    }
                                                }
                                            },
                                            onDragCancel = {
                                                dragState.reset()
                                            },
                                        )
                                    },
                            ) {
                                ScheduleCard(
                                    card = card,
                                    onToggleDone = { onToggleDone(card.id) },
                                    onClick = { onCardClick(card.id) },
                                    modifier = Modifier.graphicsLayer {
                                        translationY = if (isDraggedSource) 0f else animatedShiftPx
                                        alpha = if (isDraggedSource) 0f else 1f
                                    },
                                )
                            }
                        }
                    }
                }
            }
        }

        // 浮层幽灵卡片（120Hz GPU 合成位移，不受列表滚动/Pagers 裁剪影响）
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

@Composable
private fun EmptyLanePlaceholder(lane: LaneKind) {
    val palette = LocalNordPalette.current
    Box(
        modifier = Modifier
            .fillMaxSize()
            .padding(24.dp),
        contentAlignment = Alignment.Center,
    ) {
        Surface(
            shape = RoundedCornerShape(8.dp),
            color = palette.surface2,
            shadowElevation = 0.dp,
            border = BorderStroke(1.dp, palette.borderSubtle),
        ) {
            Column(
                modifier = Modifier.padding(horizontal = 20.dp, vertical = 18.dp),
                horizontalAlignment = Alignment.CenterHorizontally,
                verticalArrangement = Arrangement.spacedBy(6.dp),
            ) {
                Text(
                    text = "「${lane.title}」暂无日程",
                    color = palette.textPrimary,
                    fontSize = 14.sp,
                    fontWeight = FontWeight.SemiBold,
                )
                Text(
                    text = "点右下角 ＋ 新建，或长按其它档位卡片拖到这里",
                    color = palette.textSecondary,
                    fontSize = 12.5.sp,
                )
            }
        }
    }
}

private fun findHitLane(
    pointerInRoot: Offset,
    laneRects: Map<LaneKind, Rect>,
    marginPx: Float,
): LaneKind? {
    for ((lane, rect) in laneRects) {
        if (
            pointerInRoot.x >= rect.left - marginPx &&
            pointerInRoot.x <= rect.right + marginPx &&
            pointerInRoot.y >= rect.top - marginPx &&
            pointerInRoot.y <= rect.bottom + marginPx
        ) {
            return lane
        }
    }
    return null
}

private fun computeTargetInsertIndex(
    pointerY: Float,
    cards: List<ScheduleCardUi>,
    sourceIndex: Int,
    cardBaseRects: Map<String, Rect>,
): Int {
    if (sourceIndex !in cards.indices) return -1
    var insertPos = 0
    for (i in cards.indices) {
        if (i == sourceIndex) continue
        val rect = cardBaseRects[cards[i].id] ?: continue
        if (pointerY > rect.center.y) {
            insertPos++
        }
    }
    return insertPos.coerceIn(0, (cards.size - 1).coerceAtLeast(0))
}

private fun computeGapShiftPx(
    dragState: OverviewDragState,
    currentLane: LaneKind,
    itemIndex: Int,
    totalCount: Int,
    itemSpacingPx: Float,
): Float {
    if (!dragState.isDragging) return 0f
    if (dragState.sourceLane != currentLane) return 0f
    if (dragState.hoveredLane != null) return 0f

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
