package com.daynew.app.ui.components

import androidx.compose.animation.AnimatedVisibility
import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.gestures.snapping.rememberSnapFlingBehavior
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ExperimentalLayoutApi
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.defaultMinSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.imePadding
import androidx.compose.foundation.layout.navigationBarsPadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.rememberLazyListState
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.ModalBottomSheet
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.OutlinedTextFieldDefaults
import androidx.compose.material3.SheetValue
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.rememberModalBottomSheetState
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.derivedStateOf
import androidx.compose.runtime.getValue
import androidx.compose.runtime.key
import androidx.compose.runtime.mutableFloatStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.rememberUpdatedState
import androidx.compose.runtime.setValue
import androidx.compose.runtime.snapshotFlow
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.input.nestedscroll.NestedScrollConnection
import androidx.compose.ui.input.nestedscroll.NestedScrollSource
import androidx.compose.ui.input.nestedscroll.nestedScroll
import androidx.compose.ui.layout.onSizeChanged
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.Velocity
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.daynew.app.data.LaneKind
import com.daynew.app.data.ScheduleDraft
import com.daynew.app.ui.theme.LocalNordPalette
import com.daynew.app.ui.theme.nordClickable
import com.daynew.sync.GoalDto
import com.daynew.sync.LocationDto
import com.daynew.sync.PersonDto
import com.daynew.sync.RemindRules
import com.daynew.sync.RepeatRules
import com.daynew.sync.TagDto
import kotlinx.coroutines.flow.distinctUntilChanged
import kotlinx.coroutines.launch
import java.time.LocalTime
import java.util.Locale
import kotlin.math.abs

private enum class TimeFieldTarget { START, END }

private fun parseHourMinute(text: String): Pair<Int, Int>? {
    val trimmed = text.trim()
    if (trimmed.isEmpty()) return null
    val colonIdx = trimmed.indexOf(':')
    return if (colonIdx >= 0) {
        val h = trimmed.substring(0, colonIdx).toIntOrNull()?.takeIf { it in 0..23 } ?: return null
        val m = trimmed.substring(colonIdx + 1).toIntOrNull()?.takeIf { it in 0..59 } ?: return null
        h to m
    } else {
        val h = trimmed.toIntOrNull()?.takeIf { it in 0..23 } ?: return null
        h to 0
    }
}

private fun formatHourMinute(hour: Int, minute: Int): String =
    "%02d:%02d".format(Locale.ROOT, hour.coerceIn(0, 23), minute.coerceIn(0, 59))

@OptIn(ExperimentalMaterial3Api::class, ExperimentalLayoutApi::class)
@Composable
fun ScheduleEditorSheet(
    draft: ScheduleDraft,
    goals: List<GoalDto>,
    tags: List<TagDto>,
    persons: List<PersonDto> = emptyList(),
    locations: List<LocationDto> = emptyList(),
    onUpdateDraft: ((ScheduleDraft) -> ScheduleDraft) -> Unit,
    onDismiss: () -> Unit,
    onSave: () -> Unit,
    onDelete: () -> Unit,
) {
    val palette = LocalNordPalette.current
    val density = LocalDensity.current
    val coroutineScope = rememberCoroutineScope()
    val initialDraft = remember(draft.id) { draft }
    val isDirty by remember(draft, initialDraft) { derivedStateOf { draft != initialDraft } }
    var showDiscardConfirm by remember { mutableStateOf(false) }
    var confirmedDiscard by remember { mutableStateOf(false) }
    var sheetHeightPx by remember { mutableFloatStateOf(0f) }
    var expandedOffsetPx by remember { mutableFloatStateOf(Float.NaN) }
    val latestIsDirty by rememberUpdatedState(isDirty)
    val latestConfirmedDiscard by rememberUpdatedState(confirmedDiscard)
    val offsetProvider = remember {
        object {
            var readOffset: () -> Float? = { null }
        }
    }
    val sheetState = rememberModalBottomSheetState(
        skipPartiallyExpanded = true,
        confirmValueChange = { target ->
            if (target == SheetValue.Hidden && !latestConfirmedDiscard) {
                val currentOffset = offsetProvider.readOffset()
                val draggedPx = if (currentOffset != null && !expandedOffsetPx.isNaN()) {
                    (currentOffset - expandedOffsetPx).coerceAtLeast(0f)
                } else {
                    0f
                }
                val thresholdPx = maxOf(with(density) { 180.dp.toPx() }, sheetHeightPx * 0.35f)
                when {
                    draggedPx > 2f && draggedPx < thresholdPx -> false
                    latestIsDirty -> {
                        showDiscardConfirm = true
                        false
                    }
                    else -> true
                }
            } else {
                true
            }
        },
    )
    offsetProvider.readOffset = { runCatching { sheetState.requireOffset() }.getOrNull() }

    LaunchedEffect(sheetState.currentValue, sheetHeightPx) {
        if (sheetState.currentValue == SheetValue.Expanded) {
            runCatching { sheetState.requireOffset() }.getOrNull()?.let { offset ->
                expandedOffsetPx = offset
            }
        }
    }

    val blockDownwardFling = remember {
        object : NestedScrollConnection {
            override suspend fun onPostFling(consumed: Velocity, available: Velocity): Velocity {
                return Velocity(0f, available.y.coerceAtLeast(0f))
            }
        }
    }

    var editingTimeTarget by remember(draft.id) { mutableStateOf<TimeFieldTarget?>(null) }

    ModalBottomSheet(
        onDismissRequest = {
            if (isDirty && !confirmedDiscard) {
                showDiscardConfirm = true
            } else {
                onDismiss()
            }
        },
        sheetState = sheetState,
        containerColor = palette.surface4,
        contentColor = palette.textPrimary,
        tonalElevation = 0.dp,
        shape = RoundedCornerShape(topStart = 12.dp, topEnd = 12.dp),
    ) {
        Column(
            modifier = Modifier
                .fillMaxWidth()
                .onSizeChanged { sheetHeightPx = it.height.toFloat() }
                .imePadding(),
        ) {
            // 固定顶部标题栏
            Row(
                modifier = Modifier
                    .fillMaxWidth()
                    .padding(horizontal = 16.dp, vertical = 4.dp),
                horizontalArrangement = Arrangement.SpaceBetween,
                verticalAlignment = Alignment.CenterVertically,
            ) {
                Text(
                    text = if (draft.id == null) "新建日程" else "编辑日程",
                    color = palette.textPrimary,
                    fontSize = 18.sp,
                    fontWeight = FontWeight.SemiBold,
                )
                if (draft.id != null) {
                    IconButton(
                        onClick = onDelete,
                        modifier = Modifier.defaultMinSize(minWidth = 48.dp, minHeight = 48.dp),
                    ) {
                        Icon(
                            imageVector = DayNewIcons.Delete,
                            contentDescription = "删除日程",
                            tint = palette.danger,
                            modifier = Modifier.size(20.dp),
                        )
                    }
                }
            }

            // 中间可滚动表单区
            Column(
                modifier = Modifier
                    .fillMaxWidth()
                    .weight(1f, fill = false)
                    .nestedScroll(blockDownwardFling)
                    .verticalScroll(rememberScrollState())
                    .padding(horizontal = 16.dp, vertical = 8.dp),
                verticalArrangement = Arrangement.spacedBy(12.dp),
            ) {
                OutlinedTextField(
                    value = draft.title,
                    onValueChange = { v -> onUpdateDraft { it.copy(title = v) } },
                    label = { Text("标题") },
                    placeholder = { Text("要做什么？") },
                    singleLine = true,
                    modifier = Modifier.fillMaxWidth(),
                    colors = nordFieldColors(),
                )

                OutlinedTextField(
                    value = draft.note,
                    onValueChange = { v -> onUpdateDraft { it.copy(note = v) } },
                    label = { Text("备注") },
                    placeholder = { Text("补充说明（可选）") },
                    maxLines = 3,
                    modifier = Modifier.fillMaxWidth(),
                    colors = nordFieldColors(),
                )

                SectionLabel(
                    if (draft.hasDueDate && draft.useExactDate) {
                        "归属日期（${draft.dueDate}）"
                    } else {
                        "归属档位"
                    },
                )
                FlowRow(
                    horizontalArrangement = Arrangement.spacedBy(8.dp),
                    verticalArrangement = Arrangement.spacedBy(8.dp),
                ) {
                    listOf(LaneKind.TODAY, LaneKind.TOMORROW, LaneKind.THIS_WEEK, LaneKind.LATER).forEach { lane ->
                        SelectChip(
                            label = lane.title,
                            selected = draft.hasDueDate && draft.lane == lane && !draft.useExactDate,
                            onClick = {
                                onUpdateDraft {
                                    it.copy(hasDueDate = true, useExactDate = false, lane = lane)
                                }
                            },
                        )
                    }
                    SelectChip(
                        label = "不定日期",
                        selected = !draft.hasDueDate,
                        onClick = {
                            editingTimeTarget = null
                            onUpdateDraft {
                                it.copy(
                                    hasDueDate = false,
                                    useExactDate = false,
                                    startText = "",
                                    endText = "",
                                    dueTime = null,
                                )
                            }
                        },
                    )
                }

                if (draft.hasDueDate) {
                    Row(
                        modifier = Modifier.fillMaxWidth(),
                        horizontalArrangement = Arrangement.SpaceBetween,
                        verticalAlignment = Alignment.CenterVertically,
                    ) {
                        SectionLabel("起止时段（可选）")
                        if (draft.startText.isNotBlank() || draft.endText.isNotBlank()) {
                            Text(
                                text = "清除时段",
                                color = palette.textSecondary,
                                fontSize = 12.sp,
                                fontWeight = FontWeight.Medium,
                                modifier = Modifier
                                    .clip(RoundedCornerShape(6.dp))
                                    .clickable {
                                        editingTimeTarget = null
                                        onUpdateDraft { it.copy(startText = "", endText = "") }
                                    }
                                    .padding(horizontal = 6.dp, vertical = 2.dp),
                            )
                        }
                    }

                    Row(
                        modifier = Modifier.fillMaxWidth(),
                        horizontalArrangement = Arrangement.spacedBy(8.dp),
                    ) {
                        TimeTriggerCard(
                            label = "开始",
                            valueText = draft.startText.ifBlank { "点击选择" },
                            hasValue = draft.startText.isNotBlank(),
                            active = editingTimeTarget == TimeFieldTarget.START,
                            onClick = {
                                if (draft.startText.isBlank()) {
                                    val defaultHour = (LocalTime.now().hour + 1).coerceIn(8, 22)
                                    val startStr = formatHourMinute(defaultHour, 0)
                                    val endStr = if (draft.endText.isBlank()) {
                                        if (defaultHour < 23) formatHourMinute(defaultHour + 1, 0) else "23:59"
                                    } else {
                                        draft.endText
                                    }
                                    onUpdateDraft { it.copy(startText = startStr, endText = endStr) }
                                }
                                editingTimeTarget = if (editingTimeTarget == TimeFieldTarget.START) null else TimeFieldTarget.START
                            },
                            modifier = Modifier.weight(1f),
                        )

                        TimeTriggerCard(
                            label = "结束",
                            valueText = draft.endText.ifBlank { "点击选择" },
                            hasValue = draft.endText.isNotBlank(),
                            active = editingTimeTarget == TimeFieldTarget.END,
                            onClick = {
                                if (draft.endText.isBlank()) {
                                    val parsedStart = parseHourMinute(draft.startText)
                                    if (parsedStart != null) {
                                        val (sh, sm) = parsedStart
                                        val endStr = if (sh < 23) formatHourMinute(sh + 1, sm) else "23:59"
                                        onUpdateDraft { it.copy(endText = endStr) }
                                    } else {
                                        onUpdateDraft { it.copy(startText = "09:00", endText = "10:00") }
                                    }
                                }
                                editingTimeTarget = if (editingTimeTarget == TimeFieldTarget.END) null else TimeFieldTarget.END
                            },
                            modifier = Modifier.weight(1f),
                        )
                    }

                    AnimatedVisibility(visible = editingTimeTarget != null) {
                        editingTimeTarget?.let { target ->
                            Surface(
                                modifier = Modifier.fillMaxWidth(),
                                shape = RoundedCornerShape(12.dp),
                                color = palette.surface2,
                                border = BorderStroke(1.dp, palette.borderSubtle),
                                shadowElevation = 0.dp,
                            ) {
                                Column(
                                    modifier = Modifier
                                        .fillMaxWidth()
                                        .padding(horizontal = 12.dp, vertical = 10.dp),
                                    verticalArrangement = Arrangement.spacedBy(8.dp),
                                ) {
                                    Row(
                                        modifier = Modifier.fillMaxWidth(),
                                        horizontalArrangement = Arrangement.SpaceBetween,
                                        verticalAlignment = Alignment.CenterVertically,
                                    ) {
                                        Text(
                                            text = if (target == TimeFieldTarget.START) "选择开始时间" else "选择结束时间",
                                            color = palette.textPrimary,
                                            fontSize = 13.sp,
                                            fontWeight = FontWeight.SemiBold,
                                        )
                                        Text(
                                            text = "完成",
                                            color = palette.accent,
                                            fontSize = 13.sp,
                                            fontWeight = FontWeight.SemiBold,
                                            modifier = Modifier
                                                .clip(RoundedCornerShape(6.dp))
                                                .clickable { editingTimeTarget = null }
                                                .padding(horizontal = 8.dp, vertical = 3.dp),
                                        )
                                    }

                                    key(target) {
                                        val fallback = if (target == TimeFieldTarget.START) (9 to 0) else (10 to 0)
                                        val rawText = if (target == TimeFieldTarget.START) draft.startText else draft.endText
                                        val (initHour, initMinute) = parseHourMinute(rawText) ?: fallback
                                        val currentTargetText by rememberUpdatedState(rawText)

                                        TimeWheelPicker(
                                            initialHour = initHour,
                                            initialMinute = initMinute,
                                            onTimeChanged = { h, m ->
                                                val formatted = formatHourMinute(h, m)
                                                if (formatted != currentTargetText) {
                                                    onUpdateDraft { current ->
                                                        if (target == TimeFieldTarget.START) {
                                                            current.copy(startText = formatted)
                                                        } else {
                                                            current.copy(endText = formatted)
                                                        }
                                                    }
                                                }
                                            },
                                        )
                                    }
                                }
                            }
                        }
                    }
                }

                SectionLabel("重复周期")
                FlowRow(
                    horizontalArrangement = Arrangement.spacedBy(8.dp),
                    verticalArrangement = Arrangement.spacedBy(8.dp),
                ) {
                    val options = listOf("" to "不重复", RepeatRules.DAILY to "每天", RepeatRules.WEEKLY to "每周", RepeatRules.MONTHLY to "每月")
                    options.forEach { (value, label) ->
                        SelectChip(
                            label = label,
                            selected = RepeatRules.normalize(draft.repeat) == value,
                            onClick = { onUpdateDraft { it.copy(repeat = value) } },
                        )
                    }
                }

                SectionLabel("提醒节点（有起止时刻时触发）")
                FlowRow(
                    horizontalArrangement = Arrangement.spacedBy(8.dp),
                    verticalArrangement = Arrangement.spacedBy(8.dp),
                ) {
                    val nodes = listOf(
                        RemindRules.PRE15 to "提前15分",
                        RemindRules.PRE5 to "提前5分",
                        RemindRules.START to "开始时",
                        RemindRules.END to "结束时",
                    )
                    nodes.forEach { (node, label) ->
                        SelectChip(
                            label = label,
                            selected = RemindRules.hasNode(draft.remind, node),
                            onClick = {
                                onUpdateDraft {
                                    it.copy(remind = RemindRules.toggleNode(it.remind, node))
                                }
                            },
                        )
                    }
                }

                SectionLabel("艾森豪威尔标记")
                Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    SelectChip(
                        label = "紧急",
                        selected = draft.urgent,
                        selectedColor = palette.danger,
                        onClick = { onUpdateDraft { it.copy(urgent = !it.urgent) } },
                    )
                    SelectChip(
                        label = "重要",
                        selected = draft.important,
                        selectedColor = palette.warning,
                        onClick = { onUpdateDraft { it.copy(important = !it.important) } },
                    )
                }

                if (goals.isNotEmpty()) {
                    SectionLabel("关联目标")
                    FlowRow(
                        horizontalArrangement = Arrangement.spacedBy(8.dp),
                        verticalArrangement = Arrangement.spacedBy(8.dp),
                    ) {
                        SelectChip(
                            label = "无目标",
                            selected = draft.goalId == null,
                            onClick = { onUpdateDraft { it.copy(goalId = null) } },
                        )
                        goals.forEach { goal ->
                            SelectChip(
                                label = goal.title,
                                selected = draft.goalId == goal.id,
                                dotColor = parseHexColor(goal.color, palette.accent),
                                onClick = { onUpdateDraft { it.copy(goalId = goal.id) } },
                            )
                        }
                    }
                }

                if (tags.isNotEmpty()) {
                    SectionLabel("标签")
                    FlowRow(
                        horizontalArrangement = Arrangement.spacedBy(8.dp),
                        verticalArrangement = Arrangement.spacedBy(8.dp),
                    ) {
                        tags.forEach { tag ->
                            val selected = tag.id in draft.tagIds
                            SelectChip(
                                label = tag.name,
                                selected = selected,
                                dotColor = parseHexColor(tag.color, palette.accent),
                                onClick = {
                                    onUpdateDraft { current ->
                                        val nextTags = if (selected) {
                                            current.tagIds - tag.id
                                        } else {
                                            current.tagIds + tag.id
                                        }
                                        current.copy(tagIds = nextTags)
                                    }
                                },
                            )
                        }
                    }
                }

                if (persons.isNotEmpty()) {
                    SectionLabel("人物")
                    FlowRow(
                        horizontalArrangement = Arrangement.spacedBy(8.dp),
                        verticalArrangement = Arrangement.spacedBy(8.dp),
                    ) {
                        persons.forEach { person ->
                            val selected = person.id in draft.personIds
                            SelectChip(
                                label = person.name,
                                selected = selected,
                                onClick = {
                                    onUpdateDraft { current ->
                                        val nextPersons = if (selected) {
                                            current.personIds - person.id
                                        } else {
                                            current.personIds + person.id
                                        }
                                        current.copy(personIds = nextPersons)
                                    }
                                },
                            )
                        }
                    }
                }

                if (locations.isNotEmpty()) {
                    SectionLabel("地点")
                    FlowRow(
                        horizontalArrangement = Arrangement.spacedBy(8.dp),
                        verticalArrangement = Arrangement.spacedBy(8.dp),
                    ) {
                        locations.forEach { location ->
                            val selected = location.id in draft.locationIds
                            SelectChip(
                                label = location.name,
                                selected = selected,
                                onClick = {
                                    onUpdateDraft { current ->
                                        val nextLocations = if (selected) {
                                            current.locationIds - location.id
                                        } else {
                                            current.locationIds + location.id
                                        }
                                        current.copy(locationIds = nextLocations)
                                    }
                                },
                            )
                        }
                    }
                }
            }

            // 固定底部操作栏（不随中间表单滚出屏幕）
            HorizontalDivider(thickness = 1.dp, color = palette.borderSubtle)
            Row(
                modifier = Modifier
                    .fillMaxWidth()
                    .navigationBarsPadding()
                    .padding(horizontal = 16.dp, vertical = 12.dp),
                horizontalArrangement = Arrangement.spacedBy(10.dp),
            ) {
                Button(
                    onClick = {
                        if (isDirty) {
                            showDiscardConfirm = true
                        } else {
                            onDismiss()
                        }
                    },
                    modifier = Modifier
                        .weight(1f)
                        .defaultMinSize(minHeight = 48.dp),
                    shape = RoundedCornerShape(8.dp),
                    colors = ButtonDefaults.buttonColors(
                        containerColor = palette.surface3,
                        contentColor = palette.textPrimary,
                    ),
                    border = BorderStroke(1.dp, palette.borderSubtle),
                ) {
                    Text("取消", fontWeight = FontWeight.Medium)
                }

                Button(
                    onClick = onSave,
                    modifier = Modifier
                        .weight(1.4f)
                        .defaultMinSize(minHeight = 48.dp),
                    shape = RoundedCornerShape(8.dp),
                    colors = ButtonDefaults.buttonColors(
                        containerColor = palette.accent,
                        contentColor = palette.onAccent,
                    ),
                ) {
                    Text(if (draft.id == null) "创建" else "保存", fontWeight = FontWeight.SemiBold)
                }
            }
        }
    }

    if (showDiscardConfirm) {
        AlertDialog(
            onDismissRequest = { showDiscardConfirm = false },
            containerColor = palette.surface4,
            titleContentColor = palette.textPrimary,
            textContentColor = palette.textSecondary,
            shape = RoundedCornerShape(12.dp),
            title = {
                Text(
                    text = "放弃未保存的更改？",
                    fontSize = 16.sp,
                    fontWeight = FontWeight.SemiBold,
                )
            },
            text = {
                Text(
                    text = "当前日程内容尚未保存，关闭后修改将会丢失。",
                    fontSize = 13.5.sp,
                )
            },
            dismissButton = {
                TextButton(onClick = { showDiscardConfirm = false }) {
                    Text("继续编辑", color = palette.textSecondary)
                }
            },
            confirmButton = {
                TextButton(
                    onClick = {
                        showDiscardConfirm = false
                        confirmedDiscard = true
                        coroutineScope.launch {
                            try {
                                sheetState.hide()
                            } finally {
                                onDismiss()
                            }
                        }
                    },
                ) {
                    Text("放弃", color = palette.danger, fontWeight = FontWeight.SemiBold)
                }
            },
        )
    }
}

@Composable
private fun TimeTriggerCard(
    label: String,
    valueText: String,
    hasValue: Boolean,
    active: Boolean,
    onClick: () -> Unit,
    modifier: Modifier = Modifier,
) {
    val palette = LocalNordPalette.current
    Column(
        modifier = modifier
            .clip(RoundedCornerShape(8.dp))
            .background(if (active) palette.selectionPill else palette.surface3)
            .border(
                width = 1.dp,
                color = if (active) palette.accent else palette.borderSubtle,
                shape = RoundedCornerShape(8.dp),
            )
            .clickable(onClick = onClick)
            .padding(horizontal = 12.dp, vertical = 10.dp),
        verticalArrangement = Arrangement.spacedBy(2.dp),
    ) {
        Text(
            text = label,
            color = if (active) palette.accent else palette.textSecondary,
            fontSize = 11.sp,
            fontWeight = FontWeight.Medium,
        )
        Text(
            text = valueText,
            color = if (hasValue || active) palette.textPrimary else palette.textSecondary,
            style = TextStyle(
                fontSize = 15.sp,
                fontWeight = if (hasValue) FontWeight.SemiBold else FontWeight.Normal,
                fontFeatureSettings = "tnum",
            ),
        )
    }
}

@Composable
private fun TimeWheelPicker(
    initialHour: Int,
    initialMinute: Int,
    onTimeChanged: (Int, Int) -> Unit,
) {
    val palette = LocalNordPalette.current
    val hourState = rememberLazyListState(initialFirstVisibleItemIndex = initialHour.coerceIn(0, 23))
    val minuteState = rememberLazyListState(initialFirstVisibleItemIndex = initialMinute.coerceIn(0, 59))
    val currentOnTimeChanged by rememberUpdatedState(onTimeChanged)

    val selectedHour by remember {
        derivedStateOf {
            val info = hourState.layoutInfo
            val center = (info.viewportStartOffset + info.viewportEndOffset) / 2
            info.visibleItemsInfo.minByOrNull { item ->
                abs((item.offset + item.size / 2) - center)
            }?.index?.coerceIn(0, 23) ?: hourState.firstVisibleItemIndex.coerceIn(0, 23)
        }
    }

    val selectedMinute by remember {
        derivedStateOf {
            val info = minuteState.layoutInfo
            val center = (info.viewportStartOffset + info.viewportEndOffset) / 2
            info.visibleItemsInfo.minByOrNull { item ->
                abs((item.offset + item.size / 2) - center)
            }?.index?.coerceIn(0, 59) ?: minuteState.firstVisibleItemIndex.coerceIn(0, 59)
        }
    }

    val blockParentScroll = remember {
        object : NestedScrollConnection {
            override fun onPostScroll(
                consumed: Offset,
                available: Offset,
                source: NestedScrollSource,
            ): Offset = available

            override suspend fun onPostFling(
                consumed: Velocity,
                available: Velocity,
            ): Velocity = available
        }
    }

    LaunchedEffect(Unit) {
        snapshotFlow { selectedHour to selectedMinute }
            .distinctUntilChanged()
            .collect { (h, m) ->
                currentOnTimeChanged(h, m)
            }
    }

    Column(
        modifier = Modifier
            .fillMaxWidth()
            .nestedScroll(blockParentScroll),
        verticalArrangement = Arrangement.spacedBy(4.dp),
    ) {
        Row(modifier = Modifier.fillMaxWidth()) {
            Box(
                modifier = Modifier.weight(1f),
                contentAlignment = Alignment.Center,
            ) {
                Text(
                    text = "时",
                    color = palette.textSecondary,
                    fontSize = 12.sp,
                    fontWeight = FontWeight.SemiBold,
                )
            }
            Box(
                modifier = Modifier.weight(1f),
                contentAlignment = Alignment.Center,
            ) {
                Text(
                    text = "分",
                    color = palette.textSecondary,
                    fontSize = 12.sp,
                    fontWeight = FontWeight.SemiBold,
                )
            }
        }

        Box(
            modifier = Modifier
                .fillMaxWidth()
                .height(200.dp),
            contentAlignment = Alignment.Center,
        ) {
            // 中央选中指示带
            Box(
                modifier = Modifier
                    .fillMaxWidth()
                    .height(40.dp)
                    .clip(RoundedCornerShape(10.dp))
                    .background(palette.surface3)
                    .border(1.dp, palette.borderSubtle, RoundedCornerShape(10.dp)),
            )

            Row(modifier = Modifier.fillMaxWidth()) {
                WheelColumn(
                    count = 24,
                    listState = hourState,
                    selectedIndex = selectedHour,
                    modifier = Modifier.weight(1f),
                )
                WheelColumn(
                    count = 60,
                    listState = minuteState,
                    selectedIndex = selectedMinute,
                    modifier = Modifier.weight(1f),
                )
            }
        }
    }
}

@Composable
private fun WheelColumn(
    count: Int,
    listState: androidx.compose.foundation.lazy.LazyListState,
    selectedIndex: Int,
    modifier: Modifier = Modifier,
) {
    val palette = LocalNordPalette.current
    val coroutineScope = rememberCoroutineScope()
    val snapBehavior = rememberSnapFlingBehavior(lazyListState = listState)

    LazyColumn(
        state = listState,
        flingBehavior = snapBehavior,
        contentPadding = PaddingValues(vertical = 80.dp),
        modifier = modifier.height(200.dp),
        horizontalAlignment = Alignment.CenterHorizontally,
    ) {
        items(count = count, key = { it }) { index ->
            val distance = abs(index - selectedIndex)
            val fontSize = when (distance) {
                0 -> 22.sp
                1 -> 18.sp
                else -> 15.sp
            }
            val textColor = when (distance) {
                0 -> palette.textPrimary
                1 -> palette.textSecondary.copy(alpha = 0.65f)
                else -> palette.textSecondary.copy(alpha = 0.30f)
            }
            Box(
                modifier = Modifier
                    .fillMaxWidth()
                    .height(40.dp)
                    .clickable {
                        coroutineScope.launch { listState.animateScrollToItem(index) }
                    },
                contentAlignment = Alignment.Center,
            ) {
                Text(
                    text = "%02d".format(Locale.ROOT, index),
                    color = textColor,
                    style = TextStyle(
                        fontSize = fontSize,
                        fontWeight = if (distance == 0) FontWeight.Bold else FontWeight.Medium,
                        fontFeatureSettings = "tnum",
                    ),
                )
            }
        }
    }
}

@Composable
private fun SectionLabel(text: String) {
    val palette = LocalNordPalette.current
    Text(
        text = text,
        color = palette.textSecondary,
        fontSize = 12.sp,
        fontWeight = FontWeight.SemiBold,
    )
}

@Composable
private fun SelectChip(
    label: String,
    selected: Boolean,
    onClick: () -> Unit,
    dotColor: Color? = null,
    selectedColor: Color? = null,
) {
    val palette = LocalNordPalette.current
    val activeTint = selectedColor?.copy(alpha = 0.16f) ?: palette.selectionPill
    val activeBorder = selectedColor ?: palette.accent
    val activeText = selectedColor ?: palette.textPrimary

    Row(
        modifier = Modifier
            .defaultMinSize(minHeight = 36.dp)
            .clip(RoundedCornerShape(6.dp))
            .background(if (selected) activeTint else palette.surface3)
            .border(
                width = 1.dp,
                color = if (selected) activeBorder else palette.borderSubtle,
                shape = RoundedCornerShape(6.dp),
            )
            .nordClickable(pressedScale = 0.96f, onClick = onClick)
            .padding(horizontal = 12.dp, vertical = 7.dp),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(6.dp),
    ) {
        if (dotColor != null) {
            Box(
                modifier = Modifier
                    .size(6.dp)
                    .clip(CircleShape)
                    .background(dotColor),
            )
        }
        Text(
            text = label,
            color = if (selected) activeText else palette.textSecondary,
            fontSize = 13.sp,
            fontWeight = if (selected) FontWeight.SemiBold else FontWeight.Normal,
        )
    }
}

@Composable
private fun nordFieldColors() = LocalNordPalette.current.let { p ->
    OutlinedTextFieldDefaults.colors(
        focusedContainerColor = p.surface3,
        unfocusedContainerColor = p.surface3,
        focusedBorderColor = p.accent,
        unfocusedBorderColor = p.borderSubtle,
        focusedTextColor = p.textPrimary,
        unfocusedTextColor = p.textPrimary,
        focusedLabelColor = p.accent,
        unfocusedLabelColor = p.textSecondary,
        cursorColor = p.accent,
    )
}
