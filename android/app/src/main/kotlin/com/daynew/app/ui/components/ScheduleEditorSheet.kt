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
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.imePadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.ModalBottomSheet
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.OutlinedTextFieldDefaults
import androidx.compose.material3.Text
import androidx.compose.material3.rememberModalBottomSheetState
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.daynew.app.data.LaneKind
import com.daynew.app.data.ScheduleDraft
import com.daynew.app.ui.theme.LocalNordPalette
import com.daynew.sync.GoalDto
import com.daynew.sync.RemindRules
import com.daynew.sync.RepeatRules
import com.daynew.sync.TagDto

@OptIn(ExperimentalMaterial3Api::class, ExperimentalLayoutApi::class)
@Composable
fun ScheduleEditorSheet(
    draft: ScheduleDraft,
    goals: List<GoalDto>,
    tags: List<TagDto>,
    onUpdateDraft: ((ScheduleDraft) -> ScheduleDraft) -> Unit,
    onDismiss: () -> Unit,
    onSave: () -> Unit,
    onDelete: () -> Unit,
) {
    val palette = LocalNordPalette.current
    val sheetState = rememberModalBottomSheetState(skipPartiallyExpanded = true)

    ModalBottomSheet(
        onDismissRequest = onDismiss,
        sheetState = sheetState,
        containerColor = palette.surface4,
        contentColor = palette.textPrimary,
        tonalElevation = 0.dp,
        shape = RoundedCornerShape(topStart = 12.dp, topEnd = 12.dp),
    ) {
        Column(
            modifier = Modifier
                .fillMaxWidth()
                .imePadding()
                .verticalScroll(rememberScrollState())
                .padding(horizontal = 16.dp, vertical = 8.dp),
            verticalArrangement = Arrangement.spacedBy(12.dp),
        ) {
            Row(
                modifier = Modifier.fillMaxWidth(),
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
                SectionLabel("起止时段（可选，如 9 或 14:30）")
                Row(
                    modifier = Modifier.fillMaxWidth(),
                    horizontalArrangement = Arrangement.spacedBy(8.dp),
                ) {
                    OutlinedTextField(
                        value = draft.startText,
                        onValueChange = { v -> onUpdateDraft { it.copy(startText = v) } },
                        label = { Text("开始") },
                        placeholder = { Text("09:00") },
                        singleLine = true,
                        textStyle = TextStyle(fontFeatureSettings = "tnum", fontSize = 14.sp),
                        modifier = Modifier.weight(1f),
                        colors = nordFieldColors(),
                    )
                    OutlinedTextField(
                        value = draft.endText,
                        onValueChange = { v -> onUpdateDraft { it.copy(endText = v) } },
                        label = { Text("结束") },
                        placeholder = { Text("10:00") },
                        singleLine = true,
                        textStyle = TextStyle(fontFeatureSettings = "tnum", fontSize = 14.sp),
                        modifier = Modifier.weight(1f),
                        colors = nordFieldColors(),
                    )
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

            SectionLabel("四象限标记")
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

            Row(
                modifier = Modifier
                    .fillMaxWidth()
                    .padding(top = 8.dp, bottom = 16.dp),
                horizontalArrangement = Arrangement.spacedBy(10.dp),
            ) {
                Button(
                    onClick = onDismiss,
                    modifier = Modifier
                        .weight(1f)
                        .defaultMinSize(minHeight = 48.dp),
                    shape = RoundedCornerShape(6.dp),
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
                    shape = RoundedCornerShape(6.dp),
                    colors = ButtonDefaults.buttonColors(
                        containerColor = palette.accent,
                        contentColor = palette.onAccent,
                    ),
                ) {
                    Text(if (draft.id == null) "创建" else "保存", fontWeight = FontWeight.SemiBold)
                }
            }

            Spacer(modifier = Modifier.height(4.dp))
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
            .clickable(onClick = onClick)
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
