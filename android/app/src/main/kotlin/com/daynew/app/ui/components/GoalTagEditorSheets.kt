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
import androidx.compose.material3.Switch
import androidx.compose.material3.Text
import androidx.compose.material3.rememberModalBottomSheetState
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.daynew.app.data.GoalDraft
import com.daynew.app.data.TagDraft
import com.daynew.app.ui.theme.LocalNordPalette
import java.time.LocalDate

private val NordAccentSwatches = listOf(
    "#527CE6",
    "#5E81AC",
    "#81A1C1",
    "#88C0D0",
    "#5C9A64",
    "#C98A3E",
    "#BF616A",
    "#B48EAD",
)

@OptIn(ExperimentalMaterial3Api::class, ExperimentalLayoutApi::class)
@Composable
fun GoalEditorSheet(
    draft: GoalDraft,
    onUpdateDraft: ((GoalDraft) -> GoalDraft) -> Unit,
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
                    text = if (draft.id == null) "新建目标" else "编辑目标",
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
                            contentDescription = "删除目标",
                            tint = palette.danger,
                            modifier = Modifier.size(20.dp),
                        )
                    }
                }
            }

            OutlinedTextField(
                value = draft.title,
                onValueChange = { v -> onUpdateDraft { it.copy(title = v) } },
                label = { Text("目标名称") },
                placeholder = { Text("例如：完成 A3 全页面补全") },
                singleLine = true,
                modifier = Modifier.fillMaxWidth(),
                colors = editorFieldColors(),
            )

            Text(
                text = "目标颜色",
                color = palette.textSecondary,
                fontSize = 12.sp,
                fontWeight = FontWeight.SemiBold,
            )
            SwatchRow(
                selectedHex = draft.color,
                onSelect = { hex -> onUpdateDraft { it.copy(color = hex) } },
            )

            OutlinedTextField(
                value = draft.dueText,
                onValueChange = { v -> onUpdateDraft { it.copy(dueText = v) } },
                label = { Text("截止日期（可选，yyyy-MM-dd）") },
                placeholder = { Text("2026-12-31") },
                singleLine = true,
                textStyle = TextStyle(fontFeatureSettings = "tnum", fontSize = 14.sp),
                modifier = Modifier.fillMaxWidth(),
                colors = editorFieldColors(),
            )

            FlowRow(
                horizontalArrangement = Arrangement.spacedBy(8.dp),
                verticalArrangement = Arrangement.spacedBy(8.dp),
            ) {
                QuickChip("今天") {
                    onUpdateDraft { it.copy(dueText = LocalDate.now().toString()) }
                }
                QuickChip("7天后") {
                    onUpdateDraft { it.copy(dueText = LocalDate.now().plusDays(7).toString()) }
                }
                QuickChip("本月月末") {
                    val today = LocalDate.now()
                    onUpdateDraft { it.copy(dueText = today.withDayOfMonth(today.lengthOfMonth()).toString()) }
                }
                QuickChip("清除截止日") {
                    onUpdateDraft { it.copy(dueText = "") }
                }
            }

            if (draft.id != null) {
                Row(
                    modifier = Modifier.fillMaxWidth(),
                    horizontalArrangement = Arrangement.SpaceBetween,
                    verticalAlignment = Alignment.CenterVertically,
                ) {
                    Text(
                        text = "标记目标为已完成",
                        color = palette.textPrimary,
                        fontSize = 14.sp,
                    )
                    Switch(
                        checked = draft.isCompleted,
                        onCheckedChange = { done -> onUpdateDraft { it.copy(isCompleted = done) } },
                    )
                }
            }

            SheetActionBar(
                confirmLabel = if (draft.id == null) "创建" else "保存",
                onDismiss = onDismiss,
                onSave = onSave,
            )
            Spacer(modifier = Modifier.height(4.dp))
        }
    }
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun TagEditorSheet(
    draft: TagDraft,
    onUpdateDraft: ((TagDraft) -> TagDraft) -> Unit,
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
                    text = if (draft.id == null) "新建标签" else "编辑标签",
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
                            contentDescription = "删除标签",
                            tint = palette.danger,
                            modifier = Modifier.size(20.dp),
                        )
                    }
                }
            }

            OutlinedTextField(
                value = draft.name,
                onValueChange = { v -> onUpdateDraft { it.copy(name = v) } },
                label = { Text("标签名称") },
                placeholder = { Text("例如：工作 / 学习 / 家务") },
                singleLine = true,
                modifier = Modifier.fillMaxWidth(),
                colors = editorFieldColors(),
            )

            Text(
                text = "标签颜色",
                color = palette.textSecondary,
                fontSize = 12.sp,
                fontWeight = FontWeight.SemiBold,
            )
            SwatchRow(
                selectedHex = draft.color,
                onSelect = { hex -> onUpdateDraft { it.copy(color = hex) } },
            )

            SheetActionBar(
                confirmLabel = if (draft.id == null) "创建" else "保存",
                onDismiss = onDismiss,
                onSave = onSave,
            )
            Spacer(modifier = Modifier.height(4.dp))
        }
    }
}

@OptIn(ExperimentalLayoutApi::class)
@Composable
private fun SwatchRow(
    selectedHex: String,
    onSelect: (String) -> Unit,
) {
    val palette = LocalNordPalette.current
    FlowRow(
        horizontalArrangement = Arrangement.spacedBy(10.dp),
        verticalArrangement = Arrangement.spacedBy(10.dp),
    ) {
        NordAccentSwatches.forEach { hex ->
            val color = parseHexColor(hex, palette.accent)
            val isSelected = selectedHex.equals(hex, ignoreCase = true)
            Box(
                modifier = Modifier
                    .size(34.dp)
                    .clip(CircleShape)
                    .background(color)
                    .border(
                        width = if (isSelected) 2.5.dp else 1.dp,
                        color = if (isSelected) palette.textPrimary else palette.borderSubtle,
                        shape = CircleShape,
                    )
                    .clickable { onSelect(hex) },
                contentAlignment = Alignment.Center,
            ) {
                if (isSelected) {
                    Icon(
                        imageVector = DayNewIcons.Check,
                        contentDescription = null,
                        tint = palette.onAccent,
                        modifier = Modifier.size(16.dp),
                    )
                }
            }
        }
    }
}

@Composable
private fun QuickChip(label: String, onClick: () -> Unit) {
    val palette = LocalNordPalette.current
    Box(
        modifier = Modifier
            .defaultMinSize(minHeight = 32.dp)
            .clip(RoundedCornerShape(6.dp))
            .background(palette.surface3)
            .border(1.dp, palette.borderSubtle, RoundedCornerShape(6.dp))
            .clickable(onClick = onClick)
            .padding(horizontal = 10.dp, vertical = 6.dp),
        contentAlignment = Alignment.Center,
    ) {
        Text(
            text = label,
            color = palette.textSecondary,
            fontSize = 12.sp,
        )
    }
}

@Composable
private fun SheetActionBar(
    confirmLabel: String,
    onDismiss: () -> Unit,
    onSave: () -> Unit,
) {
    val palette = LocalNordPalette.current
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .padding(top = 8.dp, bottom = 12.dp),
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
            Text(confirmLabel, fontWeight = FontWeight.SemiBold)
        }
    }
}

@Composable
private fun editorFieldColors() = LocalNordPalette.current.let { p ->
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
