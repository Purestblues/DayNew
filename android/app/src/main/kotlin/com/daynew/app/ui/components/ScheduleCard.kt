package com.daynew.app.ui.components

import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ExperimentalLayoutApi
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.defaultMinSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Icon
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextDecoration
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.daynew.app.ui.main.ScheduleCardUi
import com.daynew.app.ui.theme.LocalNordPalette
import com.daynew.app.ui.theme.nordClickable

fun parseHexColor(hex: String?, fallback: Color): Color {
    if (hex.isNullOrBlank()) return fallback
    return runCatching {
        val cleaned = hex.trim().removePrefix("#")
        val value = cleaned.toLong(16)
        when (cleaned.length) {
            6 -> Color((0xFF000000L or value).toInt())
            8 -> Color(value.toInt())
            else -> fallback
        }
    }.getOrDefault(fallback)
}

@OptIn(ExperimentalLayoutApi::class)
@Composable
fun ScheduleCard(
    card: ScheduleCardUi,
    onToggleDone: () -> Unit,
    onClick: () -> Unit,
    modifier: Modifier = Modifier,
    isDraggingGhost: Boolean = false,
) {
    val palette = LocalNordPalette.current
    val borderColor = when {
        isDraggingGhost -> palette.accent
        card.isOverdue && !card.dto.done -> palette.danger.copy(alpha = 0.55f)
        else -> palette.borderSubtle
    }
    val containerColor = if (isDraggingGhost) palette.surface4 else palette.surface2
    val elevation = if (isDraggingGhost) 6.dp else 0.dp

    Surface(
        modifier = modifier
            .fillMaxWidth()
            .clip(RoundedCornerShape(14.dp))
            .nordClickable(pressedScale = 0.985f, onClick = onClick),
        shape = RoundedCornerShape(14.dp),
        color = containerColor,
        shadowElevation = elevation,
        border = BorderStroke(1.dp, borderColor),
    ) {
        Row(
            modifier = Modifier
                .fillMaxWidth()
                .padding(start = 4.dp, end = 12.dp, top = 8.dp, bottom = 10.dp),
            verticalAlignment = Alignment.Top,
        ) {
            // 48dp × 48dp 安全触控热区复选框
            Box(
                modifier = Modifier
                    .size(48.dp)
                    .clip(RoundedCornerShape(6.dp))
                    .nordClickable(
                        pressedScale = 0.92f,
                        role = Role.Checkbox,
                        onClick = onToggleDone,
                    )
                    .semantics {
                        contentDescription = if (card.dto.done) "取消完成 ${card.dto.title}" else "完成 ${card.dto.title}"
                    },
                contentAlignment = Alignment.Center,
            ) {
                val checkBg = if (card.dto.done) palette.success else Color.Transparent
                val checkBorder = if (card.dto.done) palette.success else palette.borderStrong
                Box(
                    modifier = Modifier
                        .size(20.dp)
                        .clip(RoundedCornerShape(6.dp))
                        .background(checkBg)
                        .border(1.5.dp, checkBorder, RoundedCornerShape(6.dp)),
                    contentAlignment = Alignment.Center,
                ) {
                    if (card.dto.done) {
                        Icon(
                            imageVector = DayNewIcons.Check,
                            contentDescription = null,
                            tint = palette.onAccent,
                            modifier = Modifier.size(13.dp),
                        )
                    }
                }
            }

            Column(
                modifier = Modifier
                    .weight(1f)
                    .padding(top = 6.dp),
                verticalArrangement = Arrangement.spacedBy(4.dp),
            ) {
                Row(
                    verticalAlignment = Alignment.CenterVertically,
                    horizontalArrangement = Arrangement.spacedBy(6.dp),
                ) {
                    if (card.goalColorHex != null) {
                        Box(
                            modifier = Modifier
                                .size(6.dp)
                                .clip(CircleShape)
                                .background(parseHexColor(card.goalColorHex, palette.accent)),
                        )
                    }
                    Text(
                        text = card.dto.title,
                        color = if (card.dto.done) palette.textDisabled else palette.textPrimary,
                        fontSize = 14.sp,
                        lineHeight = 20.sp,
                        fontWeight = FontWeight.SemiBold,
                        textDecoration = if (card.dto.done) TextDecoration.LineThrough else TextDecoration.None,
                        maxLines = 2,
                        overflow = TextOverflow.Ellipsis,
                    )
                }

                if (card.dto.note.isNotBlank()) {
                    Text(
                        text = card.dto.note,
                        color = if (card.dto.done) palette.textDisabled else palette.textSecondary,
                        fontSize = 12.sp,
                        lineHeight = 17.sp,
                        maxLines = 1,
                        overflow = TextOverflow.Ellipsis,
                    )
                }

                val hasMeta = card.timeLabel != null ||
                    card.dueDateLabel != null ||
                    card.repeatLabel != null ||
                    card.remindLabel != null ||
                    card.dto.urgent ||
                    card.dto.important ||
                    card.tags.isNotEmpty() ||
                    card.personNames.isNotEmpty() ||
                    card.locationNames.isNotEmpty()

                if (hasMeta) {
                    FlowRow(
                        modifier = Modifier.padding(top = 2.dp),
                        horizontalArrangement = Arrangement.spacedBy(6.dp),
                        verticalArrangement = Arrangement.spacedBy(4.dp),
                    ) {
                        card.dueDateLabel?.let { dateText ->
                            MetaPill(
                                text = dateText,
                                textColor = if (card.isOverdue && !card.dto.done) palette.danger else palette.textSecondary,
                                containerColor = if (card.isOverdue && !card.dto.done) palette.danger.copy(alpha = 0.13f) else palette.surface3,
                            )
                        }
                        card.timeLabel?.let { time ->
                            MetaPill(
                                text = time,
                                textColor = palette.accent,
                                containerColor = palette.accentTint,
                            )
                        }
                        card.repeatLabel?.let { repeat ->
                            MetaPill(
                                text = "↻ $repeat",
                                textColor = palette.textSecondary,
                                containerColor = palette.surface3,
                            )
                        }
                        card.remindLabel?.let { remind ->
                            MetaPill(
                                text = remind,
                                textColor = palette.textSecondary,
                                containerColor = palette.surface3,
                            )
                        }
                        if (card.dto.urgent) {
                            MetaPill(
                                text = "紧急",
                                textColor = palette.danger,
                                containerColor = palette.danger.copy(alpha = 0.13f),
                            )
                        }
                        if (card.dto.important) {
                            MetaPill(
                                text = "重要",
                                textColor = palette.warning,
                                containerColor = palette.warning.copy(alpha = 0.14f),
                            )
                        }
                        card.tags.forEach { tag ->
                            TagPill(
                                name = tag.name,
                                dotColor = parseHexColor(tag.colorHex, palette.accent),
                            )
                        }
                        card.personNames.forEach { person ->
                            MetaPill(
                                text = "@$person",
                                textColor = palette.textSecondary,
                                containerColor = palette.surface3,
                            )
                        }
                        card.locationNames.forEach { loc ->
                            MetaPill(
                                text = loc,
                                textColor = palette.textSecondary,
                                containerColor = palette.surface3,
                            )
                        }
                    }
                }
            }

            Spacer(modifier = Modifier.width(2.dp))
        }
    }
}

@Composable
private fun MetaPill(
    text: String,
    textColor: Color,
    containerColor: Color,
) {
    Box(
        modifier = Modifier
            .defaultMinSize(minHeight = 20.dp)
            .clip(RoundedCornerShape(4.dp))
            .background(containerColor)
            .padding(horizontal = 6.dp, vertical = 2.dp),
        contentAlignment = Alignment.Center,
    ) {
        Text(
            text = text,
            color = textColor,
            style = TextStyle(
                fontSize = 11.sp,
                lineHeight = 14.sp,
                fontWeight = FontWeight.Medium,
                fontFeatureSettings = "tnum",
            ),
            maxLines = 1,
            overflow = TextOverflow.Ellipsis,
        )
    }
}

@Composable
private fun TagPill(
    name: String,
    dotColor: Color,
) {
    val palette = LocalNordPalette.current
    Row(
        modifier = Modifier
            .defaultMinSize(minHeight = 20.dp)
            .clip(RoundedCornerShape(4.dp))
            .background(palette.surface3)
            .border(1.dp, palette.borderSubtle, RoundedCornerShape(4.dp))
            .padding(horizontal = 6.dp, vertical = 2.dp),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(4.dp),
    ) {
        Box(
            modifier = Modifier
                .size(5.dp)
                .clip(CircleShape)
                .background(dotColor),
        )
        Text(
            text = name,
            color = palette.textSecondary,
            fontSize = 11.sp,
            lineHeight = 14.sp,
            maxLines = 1,
        )
    }
}
