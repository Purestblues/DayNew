package com.daynew.app.ui.components

import androidx.compose.animation.animateColorAsState
import androidx.compose.animation.core.tween
import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.defaultMinSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.derivedStateOf
import androidx.compose.runtime.getValue
import androidx.compose.runtime.remember
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.geometry.Rect
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.layout.boundsInRoot
import androidx.compose.ui.layout.onGloballyPositioned
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.compose.ui.unit.times
import com.daynew.app.data.LaneKind
import com.daynew.app.ui.theme.LocalNordPalette
import com.daynew.app.ui.theme.NordMotion
import com.daynew.app.ui.theme.nordClickable
import kotlin.math.roundToInt

/**
 * 顶部五档位分段胶囊条：日常点击切换档位且随水平翻页实时零延迟跟随；拖拽中自动变为“放置靶区”，悬停亮起吸附。
 */
@Composable
fun LaneStrip(
    selectedLane: LaneKind,
    hoveredLane: LaneKind?,
    isDragging: Boolean,
    pendingCounts: Map<LaneKind, Int>,
    onSelectLane: (LaneKind) -> Unit,
    onLaneBoundsChanged: (LaneKind, Rect) -> Unit,
    pagerPositionProvider: (() -> Float)? = null,
    modifier: Modifier = Modifier,
) {
    val palette = LocalNordPalette.current
    val density = LocalDensity.current
    val useLivePill = !isDragging && pagerPositionProvider != null

    val visualSelectedLane by remember(pagerPositionProvider, isDragging, selectedLane) {
        derivedStateOf {
            if (!isDragging && pagerPositionProvider != null) {
                LaneKind.entries[pagerPositionProvider().roundToInt().coerceIn(0, LaneKind.entries.lastIndex)]
            } else {
                selectedLane
            }
        }
    }

    Column(
        modifier = modifier
            .fillMaxWidth()
            .background(palette.surface1)
            .padding(horizontal = 12.dp, vertical = 8.dp),
        verticalArrangement = Arrangement.spacedBy(6.dp),
    ) {
        Surface(
            modifier = Modifier.fillMaxWidth(),
            shape = RoundedCornerShape(16.dp),
            color = palette.surface3,
            shadowElevation = 0.dp,
            border = BorderStroke(
                width = 1.dp,
                color = if (isDragging) palette.accent.copy(alpha = 0.5f) else palette.borderSubtle,
            ),
        ) {
            BoxWithConstraints(
                modifier = Modifier
                    .fillMaxWidth()
                    .padding(3.dp),
            ) {
                val slotCount = LaneKind.entries.size
                val gap = 3.dp
                val slotWidth = ((maxWidth - gap * (slotCount - 1)) / slotCount).coerceAtLeast(1.dp)
                val stepPx = with(density) { (slotWidth + gap).toPx() }

                if (useLivePill) {
                    Box(
                        modifier = Modifier
                            .width(slotWidth)
                            .height(44.dp)
                            .graphicsLayer {
                                val raw = pagerPositionProvider!!.invoke()
                                translationX = raw.coerceIn(0f, (slotCount - 1).toFloat()) * stepPx
                            }
                            .clip(RoundedCornerShape(12.dp))
                            .background(palette.surface2)
                            .border(1.dp, palette.borderStrong, RoundedCornerShape(12.dp)),
                    )
                }

                Row(
                    modifier = Modifier.fillMaxWidth(),
                    horizontalArrangement = Arrangement.spacedBy(gap),
                    verticalAlignment = Alignment.CenterVertically,
                ) {
                    LaneKind.entries.forEach { lane ->
                        val isSelected = visualSelectedLane == lane
                        val isHoveredTarget = isDragging && hoveredLane == lane
                        val count = pendingCounts[lane] ?: 0
                        val isOverdueActive = lane == LaneKind.OVERDUE && count > 0

                        val bgColor by animateColorAsState(
                            targetValue = when {
                                useLivePill -> Color.Transparent
                                isHoveredTarget -> palette.accentTint
                                isSelected -> palette.surface2
                                else -> Color.Transparent
                            },
                            animationSpec = tween(NordMotion.Xs, easing = NordMotion.Standard),
                            label = "laneBg",
                        )

                        val borderColor by animateColorAsState(
                            targetValue = when {
                                useLivePill -> Color.Transparent
                                isHoveredTarget -> palette.accent
                                isSelected -> palette.borderStrong
                                else -> Color.Transparent
                            },
                            animationSpec = tween(NordMotion.Xs, easing = NordMotion.Standard),
                            label = "laneBorder",
                        )

                        val titleColor = when {
                            isHoveredTarget -> palette.accent
                            isOverdueActive -> palette.danger
                            isSelected -> palette.textPrimary
                            else -> palette.textSecondary
                        }

                        Box(
                            modifier = Modifier
                                .weight(1f)
                                .defaultMinSize(minHeight = 44.dp)
                                .clip(RoundedCornerShape(12.dp))
                                .then(
                                    if (useLivePill) {
                                        Modifier
                                    } else {
                                        Modifier
                                            .background(bgColor)
                                            .border(1.dp, borderColor, RoundedCornerShape(12.dp))
                                    }
                                )
                                .onGloballyPositioned { coords ->
                                    onLaneBoundsChanged(lane, coords.boundsInRoot())
                                }
                                .nordClickable(enabled = !isDragging, pressedScale = 0.96f) { onSelectLane(lane) }
                                .padding(horizontal = 2.dp, vertical = 6.dp),
                            contentAlignment = Alignment.Center,
                        ) {
                            Row(
                                verticalAlignment = Alignment.CenterVertically,
                                horizontalArrangement = Arrangement.spacedBy(3.dp),
                            ) {
                                Text(
                                    text = lane.title,
                                    color = titleColor,
                                    fontSize = 12.5.sp,
                                    fontWeight = if (isSelected || isHoveredTarget) FontWeight.SemiBold else FontWeight.Medium,
                                    maxLines = 1,
                                )
                                if (count > 0) {
                                    Text(
                                        text = count.toString(),
                                        color = when {
                                            isHoveredTarget -> palette.accent
                                            isOverdueActive -> palette.danger
                                            else -> palette.textSecondary
                                        },
                                        style = TextStyle(
                                            fontSize = 11.sp,
                                            fontWeight = FontWeight.SemiBold,
                                            fontFeatureSettings = "tnum",
                                        ),
                                        maxLines = 1,
                                    )
                                }
                            }
                        }
                    }
                }
            }
        }

        if (isDragging) {
            val hint = hoveredLane?.let { "松手将移至「${it.title}」并更新日期" }
                ?: "上移至上方档位改期 · 同列上下拖动排序"
            Text(
                text = hint,
                color = if (hoveredLane != null) palette.accent else palette.textSecondary,
                fontSize = 11.5.sp,
                fontWeight = if (hoveredLane != null) FontWeight.Medium else FontWeight.Normal,
                modifier = Modifier.padding(horizontal = 4.dp),
            )
        }
    }
}
