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
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.HorizontalDivider
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
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.daynew.app.ui.components.DayNewIcons
import com.daynew.app.ui.components.ScheduleCard
import com.daynew.app.ui.components.parseHexColor
import com.daynew.app.ui.main.CatalogSubTab
import com.daynew.app.ui.main.DeadlineDayGroupUi
import com.daynew.app.ui.main.EisenhowerBucketUi
import com.daynew.app.ui.main.EisenhowerKind
import com.daynew.app.ui.main.ScheduleCardUi
import com.daynew.app.ui.main.TagGroupUi
import com.daynew.app.ui.theme.LocalNordPalette
import java.time.LocalDate

@Composable
fun CatalogScreen(
    selectedSubTab: CatalogSubTab,
    deadlineGroups: List<DeadlineDayGroupUi>,
    futureSchedules: List<ScheduleCardUi>,
    eisenhowerBuckets: List<EisenhowerBucketUi>,
    tagGroups: List<TagGroupUi>,
    onSelectSubTab: (CatalogSubTab) -> Unit,
    onToggleDone: (String) -> Unit,
    onCardClick: (String) -> Unit,
    onMoveToDate: (String, LocalDate?) -> Unit,
    onOpenGoal: (String) -> Unit,
    onOpenEditTag: (String) -> Unit,
    onCreateTag: () -> Unit,
) {
    val palette = LocalNordPalette.current

    Column(modifier = Modifier.fillMaxSize()) {
        // 顶部四分栏胶囊切换条
        Surface(
            color = palette.surface1,
            shadowElevation = 0.dp,
        ) {
            Row(
                modifier = Modifier
                    .fillMaxWidth()
                    .padding(horizontal = 12.dp, vertical = 8.dp),
                horizontalArrangement = Arrangement.spacedBy(6.dp),
            ) {
                CatalogSubTab.entries.forEach { tab ->
                    val selected = selectedSubTab == tab
                    Box(
                        modifier = Modifier
                            .weight(1f)
                            .defaultMinSize(minHeight = 36.dp)
                            .clip(RoundedCornerShape(8.dp))
                            .background(if (selected) palette.selectionPill else palette.surface2)
                            .border(
                                width = 1.dp,
                                color = if (selected) palette.accent else palette.borderSubtle,
                                shape = RoundedCornerShape(8.dp),
                            )
                            .clickable { onSelectSubTab(tab) }
                            .padding(horizontal = 4.dp, vertical = 8.dp),
                        contentAlignment = Alignment.Center,
                    ) {
                        Text(
                            text = tab.title,
                            color = if (selected) palette.textPrimary else palette.textSecondary,
                            fontSize = 12.sp,
                            fontWeight = if (selected) FontWeight.SemiBold else FontWeight.Medium,
                            maxLines = 1,
                            overflow = TextOverflow.Ellipsis,
                        )
                    }
                }
            }
        }

        HorizontalDivider(thickness = 1.dp, color = palette.borderSubtle)

        when (selectedSubTab) {
            CatalogSubTab.DEADLINES -> DeadlinesList(
                groups = deadlineGroups,
                onToggleDone = onToggleDone,
                onCardClick = onCardClick,
                onOpenGoal = onOpenGoal,
            )
            CatalogSubTab.FUTURE -> FutureList(
                schedules = futureSchedules,
                onToggleDone = onToggleDone,
                onCardClick = onCardClick,
                onMoveToDate = onMoveToDate,
            )
            CatalogSubTab.EISENHOWER -> EisenhowerList(
                buckets = eisenhowerBuckets,
                onToggleDone = onToggleDone,
                onCardClick = onCardClick,
            )
            CatalogSubTab.TAGS -> TagsList(
                tagGroups = tagGroups,
                onToggleDone = onToggleDone,
                onCardClick = onCardClick,
                onOpenEditTag = onOpenEditTag,
                onCreateTag = onCreateTag,
            )
        }
    }
}

@Composable
private fun DeadlinesList(
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
                            .clickable { onOpenGoal(goalItem.goalId) },
                        shape = RoundedCornerShape(8.dp),
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
                    )
                }
            }
        }
    }
}

@Composable
private fun FutureList(
    schedules: List<ScheduleCardUi>,
    onToggleDone: (String) -> Unit,
    onCardClick: (String) -> Unit,
    onMoveToDate: (String, LocalDate?) -> Unit,
) {
    val today = remember { LocalDate.now() }
    val tomorrow = remember(today) { today.plusDays(1) }

    LazyColumn(
        modifier = Modifier.fillMaxSize(),
        contentPadding = PaddingValues(16.dp),
        verticalArrangement = Arrangement.spacedBy(10.dp),
    ) {
        if (schedules.isEmpty()) {
            item { EmptyPlaceholder("暂无不定日期事项，点右下角「+」可先收进未来事项稍后再排期。") }
        } else {
            items(schedules, key = { it.id }) { card ->
                Column(verticalArrangement = Arrangement.spacedBy(6.dp)) {
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
                                onClick = { onMoveToDate(card.id, today) },
                            )
                            QuickMoveButton(
                                label = "移到明天",
                                modifier = Modifier.weight(1f),
                                onClick = { onMoveToDate(card.id, tomorrow) },
                            )
                        }
                    }
                }
            }
        }
    }
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
            .clip(RoundedCornerShape(6.dp))
            .background(palette.surface3)
            .border(1.dp, palette.borderSubtle, RoundedCornerShape(6.dp))
            .clickable(onClick = onClick)
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
private fun EisenhowerList(
    buckets: List<EisenhowerBucketUi>,
    onToggleDone: (String) -> Unit,
    onCardClick: (String) -> Unit,
) {
    val palette = LocalNordPalette.current
    var collapsedKinds by remember { mutableStateOf(setOf(EisenhowerKind.UNMARKED)) }

    LazyColumn(
        modifier = Modifier.fillMaxSize(),
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
                shape = RoundedCornerShape(8.dp),
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
                            text = "${bucket.schedules.count { !it.dto.done }}/${bucket.schedules.size}",
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
                                    text = "该象限暂无日程",
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
            Surface(
                modifier = Modifier
                    .fillMaxWidth()
                    .clickable(onClick = onCreateTag),
                shape = RoundedCornerShape(8.dp),
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
                            imageVector = DayNewIcons.Tag,
                            contentDescription = null,
                            tint = palette.accent,
                            modifier = Modifier.size(18.dp),
                        )
                        Text(
                            text = "新建标签",
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

        if (tagGroups.isEmpty()) {
            item { EmptyPlaceholder("暂无标签，点上方「新建标签」创建后即可在日程上打标。") }
        } else {
            items(tagGroups, key = { it.tag.id }) { group ->
                val color = parseHexColor(group.tag.color, palette.accent)
                val isExpanded = group.tag.id in expandedTagIds

                Surface(
                    shape = RoundedCornerShape(8.dp),
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
private fun EmptyPlaceholder(message: String) {
    val palette = LocalNordPalette.current
    Surface(
        shape = RoundedCornerShape(8.dp),
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
