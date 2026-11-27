package com.daynew.app.ui.main

import android.Manifest
import android.app.Activity
import android.content.Context
import android.content.ContextWrapper
import android.content.Intent
import android.net.Uri
import android.os.Build
import android.provider.Settings
import androidx.activity.compose.BackHandler
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.animation.AnimatedContent
import androidx.compose.animation.AnimatedVisibility
import androidx.compose.animation.core.animateFloatAsState
import androidx.compose.animation.core.tween
import androidx.compose.animation.fadeIn
import androidx.compose.animation.fadeOut
import androidx.compose.animation.slideInHorizontally
import androidx.compose.animation.slideInVertically
import androidx.compose.animation.slideOutHorizontally
import androidx.compose.animation.slideOutVertically
import androidx.compose.animation.togetherWith
import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.defaultMinSize
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.navigationBarsPadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.statusBarsPadding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Badge
import androidx.compose.material3.BadgedBox
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.DrawerValue
import androidx.compose.material3.FloatingActionButton
import androidx.compose.material3.FloatingActionButtonDefaults
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.ModalDrawerSheet
import androidx.compose.material3.ModalNavigationDrawer
import androidx.compose.material3.NavigationBar
import androidx.compose.material3.NavigationBarItem
import androidx.compose.material3.NavigationBarItemDefaults
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Surface
import androidx.compose.material3.Switch
import androidx.compose.material3.Text
import androidx.compose.material3.rememberDrawerState
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.SideEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableFloatStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.layout.onSizeChanged
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.platform.LocalView
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.compose.ui.zIndex
import androidx.core.view.WindowCompat
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import androidx.lifecycle.viewmodel.compose.viewModel
import androidx.navigation3.runtime.NavKey
import com.daynew.app.DayNewApplication
import com.daynew.app.alarm.ReminderNotificationHelper
import com.daynew.app.data.DayNewRepository
import com.daynew.app.data.GoalDraft
import com.daynew.app.data.LaneKind
import com.daynew.app.data.LocationDraft
import com.daynew.app.data.PersonDraft
import com.daynew.app.data.ScheduleDraft
import com.daynew.app.data.TagDraft
import com.daynew.app.ui.components.DayNewIcons
import com.daynew.app.ui.components.GoalEditorSheet
import com.daynew.app.ui.components.OverviewDragBoard
import com.daynew.app.ui.components.ScheduleEditorSheet
import com.daynew.app.ui.components.SimpleNameEditorSheet
import com.daynew.app.ui.components.TagEditorSheet
import com.daynew.app.ui.screens.DeadlinesScreen
import com.daynew.app.ui.screens.EisenhowerScreen
import com.daynew.app.ui.screens.FutureScreen
import com.daynew.app.ui.screens.GoalsScreen
import com.daynew.app.ui.screens.TagsHubScreen
import com.daynew.app.ui.screens.TimelineScreen
import com.daynew.app.ui.theme.LocalNordPalette
import com.daynew.app.ui.theme.NordFrostTheme
import com.daynew.app.ui.theme.NordMotion
import com.daynew.app.ui.theme.nordClickable
import com.daynew.app.ui.theme.nordPressScale
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import java.io.File
import java.time.LocalDate
import java.time.LocalTime
import java.time.format.TextStyle as JavaTextStyle
import java.util.Locale

@Composable
fun MainScreen(
    onItemClick: (NavKey) -> Unit = {},
    modifier: Modifier = Modifier,
) {
    val context = LocalContext.current
    val app = context.applicationContext as? DayNewApplication
    val vm: MainScreenViewModel = if (app != null) {
        viewModel(
            factory = MainScreenViewModel.factory(
                repository = app.repository,
                syncCoordinator = app.syncCoordinator,
                alarmScheduler = app.alarmScheduler,
            ),
        )
    } else {
        val repository = remember(context) {
            DayNewRepository(File(context.filesDir, "daynew"))
        }
        viewModel(factory = MainScreenViewModel.factory(repository))
    }
    DayNewAppScreen(viewModel = vm, modifier = modifier)
}

@Composable
fun DayNewAppScreen(
    viewModel: MainScreenViewModel,
    modifier: Modifier = Modifier,
) {
    val uiState by viewModel.uiState.collectAsStateWithLifecycle()
    val systemDark = isSystemInDarkTheme()
    val isDark = uiState.isDarkThemeOverride ?: systemDark
    val view = LocalView.current

    if (!view.isInEditMode) {
        SideEffect {
            val window = view.context.findActivity()?.window ?: return@SideEffect
            val controller = WindowCompat.getInsetsController(window, view)
            controller.isAppearanceLightStatusBars = !isDark
            controller.isAppearanceLightNavigationBars = !isDark
        }
    }

    NordFrostTheme(darkTheme = isDark) {
        MainScreenContent(
            uiState = uiState,
            isDarkTheme = isDark,
            onSelectPage = viewModel::selectPage,
            onSelectTab = viewModel::selectTab,
            onSelectTagSection = viewModel::selectTagSection,
            onToggleEisenhowerNextThreeDaysOnly = viewModel::setEisenhowerNextThreeDaysOnly,
            onSelectLane = viewModel::selectLane,
            onSelectTimelineDate = viewModel::selectTimelineDate,
            onMoveSchedule = viewModel::moveSchedule,
            onMoveScheduleToDate = viewModel::moveScheduleToDate,
            onToggleDone = viewModel::toggleDone,
            onOpenCreate = { viewModel.openCreateSheet() },
            onOpenCreateAtHour = viewModel::openCreateTimelineSheet,
            onOpenCreateDeadline = viewModel::openCreateDeadlineSheet,
            onOpenCreateFuture = viewModel::openCreateFutureSheet,
            onOpenEdit = viewModel::openEditSheet,
            onUpdateDraft = viewModel::updateEditingDraft,
            onDismissEditor = viewModel::dismissEditor,
            onSaveEditor = viewModel::saveEditor,
            onDeleteEditor = viewModel::deleteEditing,
            onOpenCreateGoal = viewModel::openCreateGoalSheet,
            onOpenEditGoal = viewModel::openEditGoalSheet,
            onUpdateGoalDraft = viewModel::updateGoalDraft,
            onDismissGoalEditor = viewModel::dismissGoalEditor,
            onSaveGoalEditor = viewModel::saveGoalEditor,
            onDeleteGoalEditor = viewModel::deleteEditingGoal,
            onToggleGoalCompleted = viewModel::toggleGoalCompleted,
            onOpenCreateTag = viewModel::openCreateTagSheet,
            onOpenEditTag = viewModel::openEditTagSheet,
            onUpdateTagDraft = viewModel::updateTagDraft,
            onDismissTagEditor = viewModel::dismissTagEditor,
            onSaveTagEditor = viewModel::saveTagEditor,
            onDeleteTagEditor = viewModel::deleteEditingTag,
            onOpenCreatePerson = viewModel::openCreatePersonSheet,
            onOpenEditPerson = viewModel::openEditPersonSheet,
            onUpdatePersonDraft = viewModel::updatePersonDraft,
            onDismissPersonEditor = viewModel::dismissPersonEditor,
            onSavePersonEditor = viewModel::savePersonEditor,
            onDeletePersonEditor = viewModel::deleteEditingPerson,
            onOpenCreateLocation = viewModel::openCreateLocationSheet,
            onOpenEditLocation = viewModel::openEditLocationSheet,
            onUpdateLocationDraft = viewModel::updateLocationDraft,
            onDismissLocationEditor = viewModel::dismissLocationEditor,
            onSaveLocationEditor = viewModel::saveLocationEditor,
            onDeleteLocationEditor = viewModel::deleteEditingLocation,
            onToggleTheme = { viewModel.toggleTheme(isDark) },
            onConsumeToast = viewModel::consumeToast,
            onTestWebDav = viewModel::testWebDavConnection,
            onSaveWebDav = viewModel::saveWebDavConfig,
            onToggleAutoSync = viewModel::setAutoSyncEnabled,
            onSyncNow = viewModel::triggerManualSync,
            onRefreshPermissions = viewModel::refreshPermissionsState,
            onShowToast = viewModel::showToastMessage,
            modifier = modifier,
        )
    }
}

@Composable
internal fun MainScreenContent(
    uiState: DayNewUiState,
    isDarkTheme: Boolean,
    onSelectPage: (AppPage) -> Unit = {},
    onSelectTab: (BottomTab) -> Unit,
    onSelectTagSection: (TagPageSection) -> Unit = {},
    onToggleEisenhowerNextThreeDaysOnly: (Boolean) -> Unit = {},
    onSelectLane: (LaneKind) -> Unit,
    onSelectTimelineDate: (LocalDate) -> Unit = {},
    onMoveSchedule: (String, LaneKind, Int) -> Unit,
    onMoveScheduleToDate: (String, LocalDate?) -> Unit = { _, _ -> },
    onToggleDone: (String) -> Unit,
    onOpenCreate: () -> Unit,
    onOpenCreateAtHour: (LocalDate, Int) -> Unit = { _, _ -> },
    onOpenCreateDeadline: () -> Unit = {},
    onOpenCreateFuture: () -> Unit = {},
    onOpenEdit: (String) -> Unit,
    onUpdateDraft: ((ScheduleDraft) -> ScheduleDraft) -> Unit,
    onDismissEditor: () -> Unit,
    onSaveEditor: () -> Unit,
    onDeleteEditor: () -> Unit,
    onOpenCreateGoal: () -> Unit = {},
    onOpenEditGoal: (String) -> Unit = {},
    onUpdateGoalDraft: ((GoalDraft) -> GoalDraft) -> Unit = {},
    onDismissGoalEditor: () -> Unit = {},
    onSaveGoalEditor: () -> Unit = {},
    onDeleteGoalEditor: () -> Unit = {},
    onToggleGoalCompleted: (String) -> Unit = {},
    onOpenCreateTag: () -> Unit = {},
    onOpenEditTag: (String) -> Unit = {},
    onUpdateTagDraft: ((TagDraft) -> TagDraft) -> Unit = {},
    onDismissTagEditor: () -> Unit = {},
    onSaveTagEditor: () -> Unit = {},
    onDeleteTagEditor: () -> Unit = {},
    onOpenCreatePerson: () -> Unit = {},
    onOpenEditPerson: (String) -> Unit = {},
    onUpdatePersonDraft: ((PersonDraft) -> PersonDraft) -> Unit = {},
    onDismissPersonEditor: () -> Unit = {},
    onSavePersonEditor: () -> Unit = {},
    onDeletePersonEditor: () -> Unit = {},
    onOpenCreateLocation: () -> Unit = {},
    onOpenEditLocation: (String) -> Unit = {},
    onUpdateLocationDraft: ((LocationDraft) -> LocationDraft) -> Unit = {},
    onDismissLocationEditor: () -> Unit = {},
    onSaveLocationEditor: () -> Unit = {},
    onDeleteLocationEditor: () -> Unit = {},
    onToggleTheme: () -> Unit,
    onConsumeToast: (Long) -> Unit,
    onTestWebDav: (String, String, String) -> Unit = { _, _, _ -> },
    onSaveWebDav: (String, String, String, Boolean) -> Unit = { _, _, _, _ -> },
    onToggleAutoSync: (Boolean) -> Unit = {},
    onSyncNow: () -> Unit = {},
    onRefreshPermissions: () -> Unit = {},
    onShowToast: (String) -> Unit = {},
    modifier: Modifier = Modifier,
) {
    val palette = LocalNordPalette.current
    val density = LocalDensity.current
    val today = remember { LocalDate.now() }
    val weekdayText = remember(today) {
        today.dayOfWeek.getDisplayName(JavaTextStyle.SHORT, Locale.CHINA)
    }
    val drawerState = rememberDrawerState(initialValue = DrawerValue.Closed)
    val coroutineScope = rememberCoroutineScope()

    val scrollThresholdPx = remember(density) { with(density) { 12.dp.toPx() } }
    var chromeVisible by remember { mutableStateOf(true) }
    var scrollAccumulatorPx by remember { mutableFloatStateOf(0f) }

    LaunchedEffect(uiState.selectedPage) {
        chromeVisible = true
        scrollAccumulatorPx = 0f
    }

    val isChromeShown = uiState.selectedPage != AppPage.TIMELINE || chromeVisible
    var topBarHeightPx by remember { mutableFloatStateOf(with(density) { 49.dp.toPx() }) }
    var bottomBarHeightPx by remember { mutableFloatStateOf(with(density) { 80.dp.toPx() }) }
    val topBarHeightDp = with(density) { topBarHeightPx.toDp() }
    val bottomBarHeightDp = with(density) { bottomBarHeightPx.toDp() }

    val chromeProgress by animateFloatAsState(
        targetValue = if (isChromeShown) 1f else 0f,
        animationSpec = tween(
            durationMillis = if (isChromeShown) 140 else 120,
            easing = if (isChromeShown) NordMotion.Enter else NordMotion.Leave,
        ),
        label = "chromeProgress",
    )

    val handleVerticalScrollDelta: (Float) -> Unit = remember(scrollThresholdPx) {
        { dy ->
            if (dy != 0f) {
                if ((scrollAccumulatorPx > 0f && dy < 0f) || (scrollAccumulatorPx < 0f && dy > 0f)) {
                    scrollAccumulatorPx = 0f
                }
                scrollAccumulatorPx += dy
                if (scrollAccumulatorPx <= -scrollThresholdPx) {
                    chromeVisible = false
                    scrollAccumulatorPx = 0f
                } else if (scrollAccumulatorPx >= scrollThresholdPx) {
                    chromeVisible = true
                    scrollAccumulatorPx = 0f
                }
            }
        }
    }

    BackHandler(enabled = drawerState.isOpen || uiState.selectedBottomTab == null) {
        if (drawerState.isOpen) {
            coroutineScope.launch { drawerState.close() }
        } else {
            onSelectPage(AppPage.OVERVIEW)
        }
    }

    LaunchedEffect(uiState.toast?.id) {
        val currentToast = uiState.toast ?: return@LaunchedEffect
        delay(2200L)
        onConsumeToast(currentToast.id)
    }

    ModalNavigationDrawer(
        drawerState = drawerState,
        gesturesEnabled = drawerState.isOpen,
        drawerContent = {
            ModalDrawerSheet(
                modifier = Modifier.width(272.dp),
                drawerContainerColor = palette.surface1,
                drawerContentColor = palette.textPrimary,
                drawerTonalElevation = 0.dp,
                drawerShape = RoundedCornerShape(topEnd = 16.dp, bottomEnd = 16.dp),
            ) {
                Column(
                    modifier = Modifier
                        .fillMaxHeight()
                        .statusBarsPadding()
                        .padding(horizontal = 14.dp, vertical = 14.dp),
                ) {
                    Column(
                        modifier = Modifier
                            .fillMaxWidth()
                            .clip(RoundedCornerShape(10.dp))
                            .nordClickable(pressedScale = 0.985f) {
                                onSelectPage(AppPage.OVERVIEW)
                                coroutineScope.launch { drawerState.close() }
                            }
                            .padding(horizontal = 8.dp, vertical = 6.dp),
                        verticalArrangement = Arrangement.spacedBy(2.dp),
                    ) {
                        Text(
                            text = "日新",
                            color = palette.textPrimary,
                            fontSize = 20.sp,
                            fontWeight = FontWeight.SemiBold,
                        )
                        Text(
                            text = "${today.monthValue}月${today.dayOfMonth}日 $weekdayText",
                            color = palette.textSecondary,
                            style = TextStyle(
                                fontSize = 12.sp,
                                fontFeatureSettings = "tnum",
                            ),
                        )
                    }

                    HorizontalDivider(
                        modifier = Modifier.padding(vertical = 10.dp),
                        thickness = 1.dp,
                        color = palette.borderSubtle,
                    )

                    Column(verticalArrangement = Arrangement.spacedBy(6.dp)) {
                        DockMenuRow(
                            title = AppPage.DEADLINES.title,
                            subtitle = null,
                            icon = DayNewIcons.Deadline,
                            selected = uiState.selectedPage == AppPage.DEADLINES,
                            onClick = {
                                onSelectPage(AppPage.DEADLINES)
                                coroutineScope.launch { drawerState.close() }
                            },
                        )
                        DockMenuRow(
                            title = AppPage.FUTURE.title,
                            subtitle = null,
                            icon = DayNewIcons.Future,
                            selected = uiState.selectedPage == AppPage.FUTURE,
                            onClick = {
                                onSelectPage(AppPage.FUTURE)
                                coroutineScope.launch { drawerState.close() }
                            },
                        )
                        DockMenuRow(
                            title = AppPage.TAGS.title,
                            subtitle = "标签 · 人物 · 地点",
                            icon = DayNewIcons.Tag,
                            selected = uiState.selectedPage == AppPage.TAGS,
                            onClick = {
                                onSelectPage(AppPage.TAGS)
                                coroutineScope.launch { drawerState.close() }
                            },
                        )
                    }

                    Spacer(modifier = Modifier.weight(1f))

                    HorizontalDivider(
                        modifier = Modifier.padding(vertical = 10.dp),
                        thickness = 1.dp,
                        color = palette.borderSubtle,
                    )

                    DockMenuRow(
                        title = AppPage.SETTINGS.title,
                        subtitle = "外观 · 同步 · 权限",
                        icon = DayNewIcons.Settings,
                        selected = uiState.selectedPage == AppPage.SETTINGS,
                        onClick = {
                            onSelectPage(AppPage.SETTINGS)
                            coroutineScope.launch { drawerState.close() }
                        },
                    )
                }
            }
        },
    ) {
        Box(
            modifier = modifier
                .fillMaxSize()
                .background(palette.surface0),
        ) {
            // 1. 主内容层：视图页保持全屏固定坐标，其余页面固定留出顶底栏安全间距
            val horizontalSlidePx = remember(density) { with(density) { 16.dp.roundToPx() } }
            val verticalSlidePx = remember(density) { with(density) { 6.dp.roundToPx() } }
            AnimatedContent(
                targetState = uiState.selectedPage,
                modifier = Modifier.fillMaxSize(),
                transitionSpec = {
                    val fromTabIdx = BottomTab.entries.indexOfFirst { it.page == initialState }
                    val toTabIdx = BottomTab.entries.indexOfFirst { it.page == targetState }
                    if (fromTabIdx >= 0 && toTabIdx >= 0 && fromTabIdx != toTabIdx) {
                        val forward = toTabIdx > fromTabIdx
                        val enterOffset = if (forward) horizontalSlidePx else -horizontalSlidePx
                        val exitOffset = if (forward) -horizontalSlidePx else horizontalSlidePx
                        (fadeIn(tween(140, easing = NordMotion.Enter)) +
                            slideInHorizontally(tween(140, easing = NordMotion.Enter)) { enterOffset }) togetherWith
                            (fadeOut(tween(90, easing = NordMotion.Leave)) +
                                slideOutHorizontally(tween(90, easing = NordMotion.Leave)) { exitOffset })
                    } else {
                        (fadeIn(tween(140, easing = NordMotion.Enter)) +
                            slideInVertically(tween(140, easing = NordMotion.Enter)) { verticalSlidePx }) togetherWith
                            fadeOut(tween(90, easing = NordMotion.Leave))
                    }
                },
                label = "mainPageTransition",
            ) { page ->
                val pageModifier = if (page == AppPage.TIMELINE) {
                    Modifier
                        .fillMaxSize()
                        .statusBarsPadding()
                } else {
                    Modifier
                        .fillMaxSize()
                        .statusBarsPadding()
                        .padding(
                            top = topBarHeightDp,
                            bottom = if (page == AppPage.SETTINGS) 0.dp else bottomBarHeightDp,
                        )
                }
                Box(modifier = pageModifier) {
                    when (page) {
                        AppPage.OVERVIEW -> {
                            OverviewDragBoard(
                                selectedLane = uiState.selectedLane,
                                lanes = uiState.lanes,
                                lanePendingCounts = uiState.lanePendingCounts,
                                onSelectLane = onSelectLane,
                                onMoveSchedule = onMoveSchedule,
                                onToggleDone = onToggleDone,
                                onCardClick = onOpenEdit,
                            )
                        }
                        AppPage.TIMELINE -> {
                            TimelineScreen(
                                selectedDate = uiState.selectedTimelineDate,
                                timelineByDate = uiState.timelineByDate,
                                datesWithItems = uiState.timelineDatesWithItems,
                                onSelectDate = onSelectTimelineDate,
                                onCreateAtHour = onOpenCreateAtHour,
                                onToggleDone = onToggleDone,
                                onCardClick = onOpenEdit,
                                onVerticalScrollDelta = handleVerticalScrollDelta,
                                topChromeOffsetPx = { topBarHeightPx * chromeProgress },
                                topChromeMaxHeightDp = topBarHeightDp,
                                bottomChromeMaxHeightDp = bottomBarHeightDp,
                            )
                        }
                        AppPage.EISENHOWER -> {
                            EisenhowerScreen(
                                buckets = uiState.eisenhowerBuckets,
                                nextThreeDaysOnly = uiState.eisenhowerNextThreeDaysOnly,
                                onToggleNextThreeDaysOnly = onToggleEisenhowerNextThreeDaysOnly,
                                onToggleDone = onToggleDone,
                                onCardClick = onOpenEdit,
                            )
                        }
                        AppPage.GOALS -> {
                            GoalsScreen(
                                goalProgressList = uiState.goalProgressList,
                                onToggleGoalCompleted = onToggleGoalCompleted,
                                onEditGoal = onOpenEditGoal,
                                onToggleScheduleDone = onToggleDone,
                                onScheduleClick = onOpenEdit,
                            )
                        }
                        AppPage.DEADLINES -> {
                            DeadlinesScreen(
                                groups = uiState.deadlineGroups,
                                onToggleDone = onToggleDone,
                                onCardClick = onOpenEdit,
                                onOpenGoal = onOpenEditGoal,
                            )
                        }
                        AppPage.FUTURE -> {
                            FutureScreen(
                                schedules = uiState.futureSchedules,
                                onToggleDone = onToggleDone,
                                onCardClick = onOpenEdit,
                                onMoveToDate = onMoveScheduleToDate,
                            )
                        }
                        AppPage.TAGS -> {
                            TagsHubScreen(
                                selectedSection = uiState.selectedTagSection,
                                tagGroups = uiState.tagGroups,
                                personGroups = uiState.personGroups,
                                locationGroups = uiState.locationGroups,
                                onSelectSection = onSelectTagSection,
                                onToggleDone = onToggleDone,
                                onCardClick = onOpenEdit,
                                onOpenEditTag = onOpenEditTag,
                                onCreateTag = onOpenCreateTag,
                                onOpenEditPerson = onOpenEditPerson,
                                onCreatePerson = onOpenCreatePerson,
                                onOpenEditLocation = onOpenEditLocation,
                                onCreateLocation = onOpenCreateLocation,
                            )
                        }
                        AppPage.SETTINGS -> {
                            SettingsPage(
                                uiState = uiState,
                                isDarkTheme = isDarkTheme,
                                onToggleTheme = onToggleTheme,
                                onTestWebDav = onTestWebDav,
                                onSaveWebDav = onSaveWebDav,
                                onToggleAutoSync = onToggleAutoSync,
                                onSyncNow = onSyncNow,
                                onRefreshPermissions = onRefreshPermissions,
                                onShowToast = onShowToast,
                            )
                        }
                    }
                }
            }

            // 2. 顶部状态栏纯色遮罩（防止顶栏滑出时穿透系统状态栏）
            Box(
                modifier = Modifier
                    .fillMaxWidth()
                    .align(Alignment.TopCenter)
                    .zIndex(12f)
                    .background(palette.surface1)
                    .statusBarsPadding(),
            )

            // 3. 浮层顶栏：纯 GPU 平移进出，不引起主内容重排与眼动焦点偏移
            Box(
                modifier = Modifier
                    .fillMaxWidth()
                    .align(Alignment.TopCenter)
                    .zIndex(10f)
                    .statusBarsPadding(),
            ) {
                Surface(
                    color = palette.surface1,
                    shadowElevation = 0.dp,
                    modifier = Modifier
                        .fillMaxWidth()
                        .onSizeChanged { topBarHeightPx = it.height.toFloat() }
                        .graphicsLayer {
                            translationY = -(1f - chromeProgress) * topBarHeightPx
                            alpha = chromeProgress
                        },
                ) {
                    Column {
                        Row(
                            modifier = Modifier
                                .fillMaxWidth()
                                .padding(start = 10.dp, end = 6.dp, top = 2.dp, bottom = 2.dp),
                            horizontalArrangement = Arrangement.SpaceBetween,
                            verticalAlignment = Alignment.CenterVertically,
                        ) {
                            Row(
                                modifier = Modifier
                                    .weight(1f, fill = false)
                                    .clip(RoundedCornerShape(10.dp))
                                    .nordClickable(enabled = isChromeShown, pressedScale = 0.98f) {
                                        coroutineScope.launch { drawerState.open() }
                                    }
                                    .defaultMinSize(minHeight = 40.dp)
                                    .padding(horizontal = 8.dp, vertical = 4.dp),
                                verticalAlignment = Alignment.CenterVertically,
                                horizontalArrangement = Arrangement.spacedBy(8.dp),
                            ) {
                                Icon(
                                    imageVector = DayNewIcons.Menu,
                                    contentDescription = "打开侧边收纳栏",
                                    tint = palette.textSecondary,
                                    modifier = Modifier.size(18.dp),
                                )
                                AnimatedContent(
                                    targetState = uiState.selectedPage.title,
                                    transitionSpec = {
                                        (fadeIn(tween(110, easing = NordMotion.Enter)) +
                                            slideInVertically(tween(110, easing = NordMotion.Enter)) { it / 4 }) togetherWith
                                            fadeOut(tween(70, easing = NordMotion.Leave))
                                    },
                                    modifier = Modifier.weight(1f, fill = false),
                                    label = "topBarPageTitle",
                                ) { pageTitle ->
                                    Text(
                                        text = pageTitle,
                                        color = palette.textPrimary,
                                        fontSize = 17.sp,
                                        fontWeight = FontWeight.SemiBold,
                                        maxLines = 1,
                                        overflow = TextOverflow.Ellipsis,
                                    )
                                }
                                Text(
                                    text = "${today.monthValue}月${today.dayOfMonth}日",
                                    color = palette.textSecondary,
                                    style = TextStyle(
                                        fontSize = 12.sp,
                                        fontFeatureSettings = "tnum",
                                    ),
                                    maxLines = 1,
                                )
                            }

                            Row(
                                verticalAlignment = Alignment.CenterVertically,
                                horizontalArrangement = Arrangement.spacedBy(2.dp),
                            ) {
                                Text(
                                    text = "日新",
                                    color = palette.textSecondary,
                                    fontSize = 14.sp,
                                    fontWeight = FontWeight.SemiBold,
                                    modifier = Modifier.padding(end = 2.dp),
                                )
                                val themeInteraction = remember { MutableInteractionSource() }
                                IconButton(
                                    onClick = { if (isChromeShown) onToggleTheme() },
                                    enabled = isChromeShown,
                                    interactionSource = themeInteraction,
                                    modifier = Modifier
                                        .defaultMinSize(minWidth = 42.dp, minHeight = 42.dp)
                                        .nordPressScale(themeInteraction, pressedScale = 0.94f),
                                ) {
                                    Icon(
                                        imageVector = if (isDarkTheme) DayNewIcons.Sun else DayNewIcons.Moon,
                                        contentDescription = if (isDarkTheme) "切换浅色主题" else "切换深色主题",
                                        tint = palette.textSecondary,
                                        modifier = Modifier.size(19.dp),
                                    )
                                }
                            }
                        }
                        HorizontalDivider(thickness = 1.dp, color = palette.borderSubtle)
                    }
                }
            }

            // 4. 浮层底栏：纯 GPU 平移进出
            if (uiState.selectedPage != AppPage.SETTINGS) {
                Column(
                    modifier = Modifier
                        .fillMaxWidth()
                        .align(Alignment.BottomCenter)
                        .zIndex(10f)
                        .onSizeChanged { bottomBarHeightPx = it.height.toFloat() }
                        .graphicsLayer {
                            translationY = (1f - chromeProgress) * bottomBarHeightPx
                            alpha = chromeProgress
                        },
                ) {
                    HorizontalDivider(thickness = 1.dp, color = palette.borderSubtle)
                    NavigationBar(
                        containerColor = palette.surface1,
                        tonalElevation = 0.dp,
                    ) {
                        BottomTab.entries.forEach { tab ->
                            val icon = when (tab) {
                                BottomTab.OVERVIEW -> DayNewIcons.Overview
                                BottomTab.TIMELINE -> DayNewIcons.Timeline
                                BottomTab.EISENHOWER -> DayNewIcons.Catalog
                                BottomTab.GOALS -> DayNewIcons.Goals
                            }
                            val overdueCount = if (tab == BottomTab.OVERVIEW) {
                                uiState.lanePendingCounts[LaneKind.OVERDUE] ?: 0
                            } else {
                                0
                            }
                            val isSelected = uiState.selectedPage == tab.page
                            NavigationBarItem(
                                selected = isSelected,
                                onClick = { if (isChromeShown) onSelectTab(tab) },
                                icon = {
                                    if (overdueCount > 0) {
                                        BadgedBox(
                                            badge = {
                                                Badge(
                                                    containerColor = palette.danger,
                                                    contentColor = palette.onAccent,
                                                ) {
                                                    Text(
                                                        text = if (overdueCount > 99) "99+" else overdueCount.toString(),
                                                        style = TextStyle(
                                                            fontSize = 9.5.sp,
                                                            fontFeatureSettings = "tnum",
                                                        ),
                                                    )
                                                }
                                            },
                                        ) {
                                            Icon(
                                                imageVector = icon,
                                                contentDescription = tab.title,
                                                modifier = Modifier.size(21.dp),
                                            )
                                        }
                                    } else {
                                        Icon(
                                            imageVector = icon,
                                            contentDescription = tab.title,
                                            modifier = Modifier.size(21.dp),
                                        )
                                    }
                                },
                                label = {
                                    Text(
                                        text = tab.title,
                                        fontSize = if (tab == BottomTab.EISENHOWER) 10.sp else 11.sp,
                                        maxLines = 1,
                                        softWrap = false,
                                        overflow = TextOverflow.Ellipsis,
                                        fontWeight = if (isSelected) FontWeight.SemiBold else FontWeight.Normal,
                                    )
                                },
                                colors = NavigationBarItemDefaults.colors(
                                    selectedIconColor = palette.accent,
                                    selectedTextColor = palette.textPrimary,
                                    unselectedIconColor = palette.textSecondary,
                                    unselectedTextColor = palette.textSecondary,
                                    indicatorColor = palette.selectionPill,
                                ),
                            )
                        }
                    }
                }
            }

            // 5. 浮层悬浮新建按钮
            if (uiState.selectedPage != AppPage.SETTINGS && chromeProgress > 0.01f) {
                val fabAction: () -> Unit = when (uiState.selectedPage) {
                    AppPage.OVERVIEW, AppPage.EISENHOWER -> onOpenCreate
                    AppPage.TIMELINE -> {
                        { onOpenCreateAtHour(uiState.selectedTimelineDate, LocalTime.now().hour.coerceIn(0, 23)) }
                    }
                    AppPage.GOALS -> onOpenCreateGoal
                    AppPage.DEADLINES -> onOpenCreateDeadline
                    AppPage.FUTURE -> onOpenCreateFuture
                    AppPage.TAGS -> when (uiState.selectedTagSection) {
                        TagPageSection.TAGS -> onOpenCreateTag
                        TagPageSection.PERSONS -> onOpenCreatePerson
                        TagPageSection.LOCATIONS -> onOpenCreateLocation
                    }
                    AppPage.SETTINGS -> onOpenCreate
                }
                val fabDesc = when (uiState.selectedPage) {
                    AppPage.GOALS -> "新建目标"
                    AppPage.DEADLINES -> "新建截止日程"
                    AppPage.FUTURE -> "新建未来事项"
                    AppPage.TAGS -> when (uiState.selectedTagSection) {
                        TagPageSection.TAGS -> "新建标签"
                        TagPageSection.PERSONS -> "新建人物"
                        TagPageSection.LOCATIONS -> "新建地点"
                    }
                    else -> "新建日程"
                }
                val fabInteraction = remember { MutableInteractionSource() }
                FloatingActionButton(
                    onClick = { if (isChromeShown) fabAction() },
                    interactionSource = fabInteraction,
                    shape = CircleShape,
                    containerColor = palette.accent,
                    contentColor = palette.onAccent,
                    modifier = Modifier
                        .align(Alignment.BottomEnd)
                        .zIndex(11f)
                        .padding(end = 16.dp, bottom = bottomBarHeightDp + 16.dp)
                        .graphicsLayer {
                            val s = 0.90f + 0.10f * chromeProgress
                            scaleX = s
                            scaleY = s
                            alpha = chromeProgress
                        }
                        .nordPressScale(fabInteraction, pressedScale = 0.94f),
                    elevation = FloatingActionButtonDefaults.elevation(
                        defaultElevation = 0.dp,
                        pressedElevation = 0.dp,
                        focusedElevation = 0.dp,
                        hoveredElevation = 0.dp,
                    ),
                ) {
                    Icon(
                        imageVector = DayNewIcons.Add,
                        contentDescription = fabDesc,
                        modifier = Modifier.size(22.dp),
                    )
                }
            }

            // 6. 底部轻量状态胶囊 Toast
            Box(
                modifier = Modifier
                    .align(Alignment.BottomCenter)
                    .zIndex(15f)
                    .padding(
                        start = 20.dp,
                        end = 20.dp,
                        bottom = if (uiState.selectedPage == AppPage.SETTINGS) 24.dp else bottomBarHeightDp + 16.dp,
                    ),
            ) {
                AnimatedVisibility(
                    visible = uiState.toast != null,
                    enter = fadeIn(tween(NordMotion.S)) + slideInVertically(tween(NordMotion.S)) { it / 2 },
                    exit = fadeOut(tween(NordMotion.S)) + slideOutVertically(tween(NordMotion.S)) { it / 2 },
                ) {
                    uiState.toast?.let { notice ->
                        Surface(
                            shape = RoundedCornerShape(9999.dp),
                            color = palette.surface4,
                            shadowElevation = 4.dp,
                            border = BorderStroke(1.dp, palette.borderPopup),
                        ) {
                            Text(
                                text = notice.message,
                                color = palette.textPrimary,
                                fontSize = 13.sp,
                                fontWeight = FontWeight.Medium,
                                modifier = Modifier.padding(horizontal = 16.dp, vertical = 10.dp),
                            )
                        }
                    }
                }
            }
        }
    }

    uiState.editingDraft?.let { draft ->
        ScheduleEditorSheet(
            draft = draft,
            goals = uiState.activeGoals,
            tags = uiState.activeTags,
            persons = uiState.activePersons,
            locations = uiState.activeLocations,
            onUpdateDraft = onUpdateDraft,
            onDismiss = onDismissEditor,
            onSave = onSaveEditor,
            onDelete = onDeleteEditor,
        )
    }

    uiState.editingGoalDraft?.let { goalDraft ->
        GoalEditorSheet(
            draft = goalDraft,
            onUpdateDraft = onUpdateGoalDraft,
            onDismiss = onDismissGoalEditor,
            onSave = onSaveGoalEditor,
            onDelete = onDeleteGoalEditor,
        )
    }

    uiState.editingTagDraft?.let { tagDraft ->
        TagEditorSheet(
            draft = tagDraft,
            onUpdateDraft = onUpdateTagDraft,
            onDismiss = onDismissTagEditor,
            onSave = onSaveTagEditor,
            onDelete = onDeleteTagEditor,
        )
    }

    uiState.editingPersonDraft?.let { personDraft ->
        SimpleNameEditorSheet(
            title = if (personDraft.id == null) "新建人物" else "编辑人物",
            label = "人物名称",
            placeholder = "例如：张三 / 导师",
            value = personDraft.name,
            isEditing = personDraft.id != null,
            deleteDescription = "删除人物",
            onValueChange = { v -> onUpdatePersonDraft { it.copy(name = v) } },
            onDismiss = onDismissPersonEditor,
            onSave = onSavePersonEditor,
            onDelete = onDeletePersonEditor,
        )
    }

    uiState.editingLocationDraft?.let { locationDraft ->
        SimpleNameEditorSheet(
            title = if (locationDraft.id == null) "新建地点" else "编辑地点",
            label = "地点名称",
            placeholder = "例如：图书馆 / 会议室 A",
            value = locationDraft.name,
            isEditing = locationDraft.id != null,
            deleteDescription = "删除地点",
            onValueChange = { v -> onUpdateLocationDraft { it.copy(name = v) } },
            onDismiss = onDismissLocationEditor,
            onSave = onSaveLocationEditor,
            onDelete = onDeleteLocationEditor,
        )
    }
}

@Composable
private fun DockMenuRow(
    title: String,
    subtitle: String?,
    icon: ImageVector,
    selected: Boolean,
    onClick: () -> Unit,
) {
    val palette = LocalNordPalette.current
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .defaultMinSize(minHeight = 48.dp)
            .clip(RoundedCornerShape(12.dp))
            .background(if (selected) palette.selectionPill else Color.Transparent)
            .nordClickable(pressedScale = 0.985f, onClick = onClick)
            .padding(horizontal = 12.dp, vertical = 10.dp),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(12.dp),
    ) {
        Icon(
            imageVector = icon,
            contentDescription = title,
            tint = if (selected) palette.accent else palette.textSecondary,
            modifier = Modifier.size(19.dp),
        )
        Column(verticalArrangement = Arrangement.spacedBy(1.dp)) {
            Text(
                text = title,
                color = if (selected) palette.textPrimary else palette.textSecondary,
                fontSize = 14.sp,
                fontWeight = if (selected) FontWeight.SemiBold else FontWeight.Medium,
            )
            if (subtitle != null) {
                Text(
                    text = subtitle,
                    color = palette.textSecondary,
                    fontSize = 11.sp,
                )
            }
        }
    }
}

@Composable
private fun SettingsPage(
    uiState: DayNewUiState,
    isDarkTheme: Boolean,
    onToggleTheme: () -> Unit,
    onTestWebDav: (String, String, String) -> Unit,
    onSaveWebDav: (String, String, String, Boolean) -> Unit,
    onToggleAutoSync: (Boolean) -> Unit,
    onSyncNow: () -> Unit,
    onRefreshPermissions: () -> Unit,
    onShowToast: (String) -> Unit,
) {
    val palette = LocalNordPalette.current
    val context = LocalContext.current
    val syncStatus = uiState.syncStatus

    var serverUrl by remember(syncStatus.serverUrl) { mutableStateOf(syncStatus.serverUrl) }
    var username by remember(syncStatus.username) { mutableStateOf(syncStatus.username) }
    var passwordInput by remember { mutableStateOf("") }
    var hasNotificationPerm by remember {
        mutableStateOf(ReminderNotificationHelper.hasNotificationPermission(context))
    }

    val notificationPermLauncher = rememberLauncherForActivityResult(
        contract = ActivityResultContracts.RequestPermission(),
    ) { granted ->
        hasNotificationPerm = granted || ReminderNotificationHelper.hasNotificationPermission(context)
        onShowToast(if (hasNotificationPerm) "已授予通知权限" else "未授予通知权限，可在系统设置开启")
    }

    LazyColumn(
        modifier = Modifier
            .fillMaxSize()
            .navigationBarsPadding(),
        contentPadding = PaddingValues(16.dp),
        verticalArrangement = Arrangement.spacedBy(10.dp),
    ) {
        item {
            Surface(
                shape = RoundedCornerShape(14.dp),
                color = palette.surface2,
                shadowElevation = 0.dp,
                border = BorderStroke(1.dp, palette.borderSubtle),
            ) {
                Row(
                    modifier = Modifier
                        .fillMaxWidth()
                        .padding(horizontal = 16.dp, vertical = 12.dp),
                    horizontalArrangement = Arrangement.SpaceBetween,
                    verticalAlignment = Alignment.CenterVertically,
                ) {
                    Column(
                        modifier = Modifier
                            .weight(1f)
                            .padding(end = 12.dp),
                        verticalArrangement = Arrangement.spacedBy(2.dp),
                    ) {
                        Text(
                            text = "深色外观（Nord Polar Night）",
                            color = palette.textPrimary,
                            fontSize = 14.sp,
                            fontWeight = FontWeight.SemiBold,
                        )
                        Text(
                            text = "关闭跟随浅色 Snow Storm，点击可即时校验双套令牌",
                            color = palette.textSecondary,
                            fontSize = 12.sp,
                        )
                    }
                    Switch(
                        checked = isDarkTheme,
                        onCheckedChange = { onToggleTheme() },
                    )
                }
            }
        }

        item {
            Surface(
                shape = RoundedCornerShape(14.dp),
                color = palette.surface2,
                shadowElevation = 0.dp,
                border = BorderStroke(1.dp, palette.borderSubtle),
            ) {
                Column(
                    modifier = Modifier
                        .fillMaxWidth()
                        .padding(16.dp),
                    verticalArrangement = Arrangement.spacedBy(10.dp),
                ) {
                    Row(
                        modifier = Modifier.fillMaxWidth(),
                        horizontalArrangement = Arrangement.SpaceBetween,
                        verticalAlignment = Alignment.CenterVertically,
                    ) {
                        Column(
                            modifier = Modifier
                                .weight(1f)
                                .padding(end = 12.dp),
                            verticalArrangement = Arrangement.spacedBy(2.dp),
                        ) {
                            Text(
                                text = "WebDAV 多端云同步（坚果云兼容）",
                                color = palette.textPrimary,
                                fontSize = 14.sp,
                                fontWeight = FontWeight.SemiBold,
                            )
                            Text(
                                text = "允许移动网络，15 分钟后台周期 + 切回前台/改动自动同步",
                                color = palette.textSecondary,
                                fontSize = 12.sp,
                            )
                        }
                        Switch(
                            checked = syncStatus.autoSyncEnabled,
                            onCheckedChange = onToggleAutoSync,
                        )
                    }

                    OutlinedTextField(
                        value = serverUrl,
                        onValueChange = { serverUrl = it },
                        label = { Text("WebDAV 目录地址") },
                        placeholder = { Text("https://dav.jianguoyun.com/dav/DayNewSync/") },
                        singleLine = true,
                        modifier = Modifier.fillMaxWidth(),
                    )
                    OutlinedTextField(
                        value = username,
                        onValueChange = { username = it },
                        label = { Text("账号") },
                        singleLine = true,
                        modifier = Modifier.fillMaxWidth(),
                    )
                    OutlinedTextField(
                        value = passwordInput,
                        onValueChange = { passwordInput = it },
                        label = { Text(if (syncStatus.hasSavedPassword) "应用密码（留空保持已加密凭证）" else "应用专属密码") },
                        visualTransformation = PasswordVisualTransformation(),
                        singleLine = true,
                        modifier = Modifier.fillMaxWidth(),
                    )

                    Row(
                        modifier = Modifier.fillMaxWidth(),
                        horizontalArrangement = Arrangement.spacedBy(8.dp),
                    ) {
                        OutlinedButton(
                            onClick = { onTestWebDav(serverUrl, username, passwordInput) },
                            shape = RoundedCornerShape(8.dp),
                            contentPadding = PaddingValues(horizontal = 8.dp, vertical = 10.dp),
                            modifier = Modifier.weight(1f),
                        ) {
                            Text("测试连接", fontSize = 12.5.sp, maxLines = 1, overflow = TextOverflow.Ellipsis)
                        }
                        OutlinedButton(
                            onClick = {
                                onSaveWebDav(serverUrl, username, passwordInput, syncStatus.autoSyncEnabled)
                                passwordInput = ""
                            },
                            shape = RoundedCornerShape(8.dp),
                            contentPadding = PaddingValues(horizontal = 8.dp, vertical = 10.dp),
                            modifier = Modifier.weight(1f),
                        ) {
                            Text("保存", fontSize = 12.5.sp, maxLines = 1, overflow = TextOverflow.Ellipsis)
                        }
                        Button(
                            onClick = onSyncNow,
                            enabled = !syncStatus.isSyncing,
                            shape = RoundedCornerShape(8.dp),
                            contentPadding = PaddingValues(horizontal = 8.dp, vertical = 10.dp),
                            colors = ButtonDefaults.buttonColors(
                                containerColor = palette.accent,
                                contentColor = palette.onAccent,
                            ),
                            modifier = Modifier.weight(1f),
                        ) {
                            Text(
                                text = if (syncStatus.isSyncing) "同步中" else "立即同步",
                                fontSize = 12.5.sp,
                                maxLines = 1,
                                overflow = TextOverflow.Ellipsis,
                            )
                        }
                    }

                    if (syncStatus.lastSyncAt.isNotEmpty() || syncStatus.lastSyncSummary.isNotEmpty()) {
                        StatusRow(
                            "最近同步",
                            listOf(syncStatus.lastSyncAt, syncStatus.lastSyncSummary)
                                .filter { it.isNotBlank() }
                                .joinToString(" · "),
                        )
                    }
                    if (syncStatus.lastSyncError.isNotEmpty()) {
                        Text(
                            text = "上次异常：${syncStatus.lastSyncError}",
                            color = palette.danger,
                            fontSize = 12.sp,
                        )
                    }
                }
            }
        }

        item {
            Surface(
                shape = RoundedCornerShape(14.dp),
                color = palette.surface2,
                shadowElevation = 0.dp,
                border = BorderStroke(1.dp, palette.borderSubtle),
            ) {
                Column(
                    modifier = Modifier
                        .fillMaxWidth()
                        .padding(16.dp),
                    verticalArrangement = Arrangement.spacedBy(10.dp),
                ) {
                    Text(
                        text = "节点提醒与系统权限",
                        color = palette.textPrimary,
                        fontSize = 14.sp,
                        fontWeight = FontWeight.SemiBold,
                    )
                    StatusRow(
                        "通知权限",
                        if (hasNotificationPerm) "已允许" else "未允许（请点击授权）",
                    )
                    StatusRow(
                        "精确闹钟",
                        if (uiState.exactAlarmAllowed) "已授权（准时触发）" else "未授权（已降级非精确闹钟）",
                    )
                    StatusRow(
                        "后台省电豁免",
                        if (uiState.batteryOptimizationIgnored) "已豁免（无限制）" else "未豁免（息屏可能延迟）",
                    )
                    Text(
                        text = "提醒通知支持「完成」与「延后 10 分钟」快捷按钮；建议在系统省电设置中设为「无限制」。",
                        color = palette.textSecondary,
                        fontSize = 12.sp,
                    )
                    if ((!hasNotificationPerm && Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) ||
                        (!uiState.exactAlarmAllowed && Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) ||
                        !uiState.batteryOptimizationIgnored
                    ) {
                        Row(
                            modifier = Modifier.fillMaxWidth(),
                            horizontalArrangement = Arrangement.spacedBy(8.dp),
                        ) {
                            if (!hasNotificationPerm && Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
                                OutlinedButton(
                                    onClick = {
                                        notificationPermLauncher.launch(Manifest.permission.POST_NOTIFICATIONS)
                                    },
                                    shape = RoundedCornerShape(8.dp),
                                    contentPadding = PaddingValues(horizontal = 8.dp, vertical = 10.dp),
                                    modifier = Modifier.weight(1f),
                                ) {
                                    Text("申请通知", fontSize = 12.sp, maxLines = 1, overflow = TextOverflow.Ellipsis)
                                }
                            }
                            if (!uiState.exactAlarmAllowed && Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) {
                                OutlinedButton(
                                    onClick = {
                                        val intent = Intent(Settings.ACTION_REQUEST_SCHEDULE_EXACT_ALARM).apply {
                                            data = Uri.parse("package:${context.packageName}")
                                        }
                                        runCatching { context.startActivity(intent) }
                                        onRefreshPermissions()
                                    },
                                    shape = RoundedCornerShape(8.dp),
                                    contentPadding = PaddingValues(horizontal = 8.dp, vertical = 10.dp),
                                    modifier = Modifier.weight(1f),
                                ) {
                                    Text("精确闹钟", fontSize = 12.sp, maxLines = 1, overflow = TextOverflow.Ellipsis)
                                }
                            }
                            if (!uiState.batteryOptimizationIgnored) {
                                OutlinedButton(
                                    onClick = {
                                        val intent = Intent(Settings.ACTION_IGNORE_BATTERY_OPTIMIZATION_SETTINGS)
                                        runCatching { context.startActivity(intent) }
                                        onRefreshPermissions()
                                    },
                                    shape = RoundedCornerShape(8.dp),
                                    contentPadding = PaddingValues(horizontal = 8.dp, vertical = 10.dp),
                                    modifier = Modifier.weight(1f),
                                ) {
                                    Text("电池豁免", fontSize = 12.sp, maxLines = 1, overflow = TextOverflow.Ellipsis)
                                }
                            }
                        }
                    }
                    Button(
                        onClick = {
                            if (!ReminderNotificationHelper.hasNotificationPermission(context) &&
                                Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU
                            ) {
                                notificationPermLauncher.launch(Manifest.permission.POST_NOTIFICATIONS)
                            } else {
                                ReminderNotificationHelper.showTestNotification(context)
                                onShowToast("已发送测试提醒通知")
                            }
                        },
                        shape = RoundedCornerShape(8.dp),
                        contentPadding = PaddingValues(horizontal = 12.dp, vertical = 10.dp),
                        colors = ButtonDefaults.buttonColors(
                            containerColor = palette.accent,
                            contentColor = palette.onAccent,
                        ),
                        modifier = Modifier.fillMaxWidth(),
                    ) {
                        Text("发送测试提醒通知", fontSize = 12.5.sp, maxLines = 1, overflow = TextOverflow.Ellipsis)
                    }
                }
            }
        }

        item {
            Surface(
                shape = RoundedCornerShape(14.dp),
                color = palette.surface2,
                shadowElevation = 0.dp,
                border = BorderStroke(1.dp, palette.borderSubtle),
            ) {
                Column(
                    modifier = Modifier
                        .fillMaxWidth()
                        .padding(16.dp),
                    verticalArrangement = Arrangement.spacedBy(10.dp),
                ) {
                    Text(
                        text = "本地原子存储与同步契约状态",
                        color = palette.textPrimary,
                        fontSize = 14.sp,
                        fontWeight = FontWeight.SemiBold,
                    )
                    StatusRow("设备标识", uiState.deviceId)
                    StatusRow("活跃日程", "${uiState.totalScheduleCount} 条")
                    StatusRow("完成日志（completions.ndjson）", "${uiState.completionsCount} 条")
                    Column(verticalArrangement = Arrangement.spacedBy(2.dp)) {
                        Text(
                            text = "主数据路径",
                            color = palette.textSecondary,
                            fontSize = 12.sp,
                        )
                        Text(
                            text = uiState.dataDirPath,
                            color = palette.textPrimary,
                            style = TextStyle(fontSize = 12.sp, fontFeatureSettings = "tnum"),
                            maxLines = 2,
                            overflow = TextOverflow.Ellipsis,
                        )
                    }
                }
            }
        }
    }
}

@Composable
private fun StatusRow(label: String, value: String) {
    val palette = LocalNordPalette.current
    Row(
        modifier = Modifier.fillMaxWidth(),
        horizontalArrangement = Arrangement.SpaceBetween,
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Text(text = label, color = palette.textSecondary, fontSize = 12.5.sp)
        Text(
            text = value,
            color = palette.textPrimary,
            style = TextStyle(
                fontSize = 12.5.sp,
                fontWeight = FontWeight.Medium,
                fontFeatureSettings = "tnum",
            ),
        )
    }
}

private tailrec fun Context.findActivity(): Activity? = when (this) {
    is Activity -> this
    is ContextWrapper -> baseContext.findActivity()
    else -> null
}
