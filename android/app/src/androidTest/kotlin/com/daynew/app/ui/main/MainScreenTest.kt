package com.daynew.app.ui.main

import androidx.activity.ComponentActivity
import androidx.compose.ui.test.junit4.createAndroidComposeRule
import androidx.compose.ui.test.onNodeWithText
import com.daynew.app.data.LaneKind
import com.daynew.app.ui.theme.NordFrostTheme
import com.daynew.sync.ScheduleDto
import org.junit.Before
import org.junit.Rule
import org.junit.Test

/** UI tests for [MainScreenContent]. */
class MainScreenTest {

    @get:Rule
    val composeTestRule = createAndroidComposeRule<ComponentActivity>()

    @Before
    fun setup() {
        val sampleCard = ScheduleCardUi(
            id = "sample_1",
            dto = ScheduleDto(id = "sample_1", title = "阶段 A1 测试任务", due = "2026-11-27"),
            lane = LaneKind.TODAY,
            goalTitle = null,
            goalColorHex = null,
            tags = emptyList(),
            timeLabel = "09:00 - 10:00",
            dueDateLabel = null,
            repeatLabel = null,
            remindLabel = null,
            isOverdue = false,
        )
        val fakeState = DayNewUiState(
            selectedLane = LaneKind.TODAY,
            lanes = mapOf(LaneKind.TODAY to listOf(sampleCard)),
            lanePendingCounts = mapOf(LaneKind.TODAY to 1),
        )
        composeTestRule.setContent {
            NordFrostTheme(darkTheme = false) {
                MainScreenContent(
                    uiState = fakeState,
                    isDarkTheme = false,
                    onSelectTab = {},
                    onSelectLane = {},
                    onMoveSchedule = { _, _, _ -> },
                    onToggleDone = {},
                    onOpenCreate = {},
                    onOpenEdit = {},
                    onUpdateDraft = {},
                    onDismissEditor = {},
                    onSaveEditor = {},
                    onDeleteEditor = {},
                    onToggleTheme = {},
                    onConsumeToast = {},
                )
            }
        }
    }

    @Test
    fun fiveLanesAndSampleCard_areDisplayed() {
        composeTestRule.onNodeWithText("今天").assertExists()
        composeTestRule.onNodeWithText("明天").assertExists()
        composeTestRule.onNodeWithText("本周").assertExists()
        composeTestRule.onNodeWithText("之后").assertExists()
        composeTestRule.onNodeWithText("阶段 A1 测试任务").assertExists()
    }
}
