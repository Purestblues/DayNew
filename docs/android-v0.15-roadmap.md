# 日新 Android v0.15 深度测试问题与迭代排期表

> 梳理日期：2026-11-28  
> 来源：2026-11-28 实机深度测试记录的 18 项问题与需求（已与用户对齐全部核心设计细节）  
> 目标版本：Android v0.15（共 4 个 Batch，按「先稳底座、再补交互、后加体验、最后开新页」推进）

---

## Batch 1 — 根因级 Bug 与核心性能 / 手感（6 项）

- [x] **1. 修复每次打开变回黑暗模式** (`id=2711789343`)
  - **方案**：将主题偏好（跟随系统 / 浅色 / 深色）持久化到本地偏好文件，应用启动首帧直接读取，避免退回默认跟随系统深色。
  - **涉及**：`SyncPreferencesStore.kt`（或独立偏好存储）、`MainScreenViewModel.kt`、`MainScreen.kt`
- [x] **2. 修复后台提醒不触发** (`id=522b3b1767`)
  - **方案**：`ReminderAlarmScheduler.kt` 升级为闹钟级 `AlarmManager.setAlarmClock` 唤醒（兼容 HyperOS 等国产 ROM 后台/锁屏管控）；移除静默吞异常逻辑；设置页展示通知 / 精确闹钟 / 电池优化无限制状态与一键跳转引导。
  - **涉及**：`ReminderAlarmScheduler.kt`、`ReminderReceivers.kt`、`AndroidManifest.xml`、`MainScreen.kt`
- [x] **3. 优化冷启动性能** (`id=7fa37dad1a`)
  - **方案**：将 `DayNewApplication.onCreate()` 与 `MainActivity.onStart()` 中阻塞主线程的闹钟批量重排、Keystore 加解密、WorkManager 调度、Widget 刷新全部移入后台 IO 协程，降低首屏阻塞与冗余重组。
  - **涉及**：`DayNewApplication.kt`、`MainActivity.kt`、`DayNewRepository.kt`
- [x] **4. 全页面滑动惯性与跟手度调优** (`id=895badb4ec`)
  - **方案**：全面排查各页滚动重组与手势竞争（`OverviewDragBoard` 长按/点击冲突、`TimelineScreen` 二维 `pointerInput`）；重构 `TimelineScreen` 主轴锁定与衰减阻尼，统一校准各列表页 `FlingBehavior` 摩擦与阻尼贴近系统原生，并在实机 Release 包验证 120Hz 流畅度。
  - **涉及**：`TimelineScreen.kt`、`OverviewDragBoard.kt`、各列表页及公共滑动配置
- [x] **5. 新建日程弹窗下滑收起阈值调优** (`id=1302771ae7`)
  - **方案**：将下拉收起阈值从 `max(180.dp, 35%)` 调低为 `max(96.dp, 20%)`，同时放开表单已在顶部时的主动向下快滑收起；有未保存修改时保留二次确认弹窗。
  - **涉及**：`ScheduleEditorSheet.kt`
- [x] **6. 页面过渡动效提速** (`id=82558d2027`)
  - **方案**：主页面与标签子页切换压缩至 `90ms` 进入 / `60ms` 退出，位移缩减至 `8.dp`（顶栏标题同步提速），去除拖尾感。
  - **涉及**：`MainScreen.kt`、`CatalogScreen.kt`、`NordFrostTheme.kt`

---

## Batch 2 — 概览 / 视图 / 未来事项核心交互补齐（6 项）

- [x] **7. 概览页卡片按安排时间排序** (`id=eed1fb5526`)
  - **方案**：同档位内，有具体起止时刻的日程排在前面按开始时间升序；无具体时刻的排在后面按手动 `order` 排序；已完成事项继续沉底。
  - **涉及**：`MainScreenViewModel.kt`、`OverviewDragBoard.kt`
- [x] **8. 概览页拖动卡片到边缘自动滚动** (`id=9ab69b631a`)
  - **方案**：在 `OverviewDragBoard.kt` 增加上下边缘 `64.dp` 自动滚动热区，拖至列表顶/底边缘时持续平滑滚动并实时刷新卡片基准坐标。
  - **涉及**：`OverviewDragBoard.kt`
- [x] **9. 未来事项页面支持手动排序** (`id=888e973365`)
  - **方案**：为 `FutureScreen` 接入长按拖拽排序、开缝让位与边缘自动滚动，落盘更新 `order` 并推进 HLC（与桌面端同步）。
  - **涉及**：`CatalogScreen.kt`（或 `FutureScreen` 所在文件）、`DayNewRepository.kt`、`MainScreenViewModel.kt`
- [x] **10. 视图页长按卡片拖动改变时间** (`id=9ef191ca04`)
  - **方案**：`TimelineScreen.kt` 时间块支持长按起拖（配震动反馈），纵向按 15 分钟刻度吸附改时段（保持原时长，松手自动避开重叠），并支持横向跨天拖放改日期。
  - **涉及**：`TimelineScreen.kt`、`DayNewRepository.kt`、`MainScreenViewModel.kt`
- [x] **11. 视图页「回到今日」改为「回到今日今时」** (`id=cd3be5b6d6`)
  - **方案**：点击后同时横向滚回今天 + 纵向滚至当前时刻（上方预留约 1.5 小时视野），对齐桌面端体验。
  - **涉及**：`TimelineScreen.kt`
- [x] **12. 新建弹窗动态标题与文案** (`id=207dd59196`)
  - **方案**：按打开入口动态显示「新建未来事项」「新建截止事项」「新建日程」（编辑态保持「编辑日程」）。
  - **涉及**：`ScheduleEditorSheet.kt`、`MainScreenViewModel.kt`

---

## Batch 3 — 动效、触感与数据管理能力（5 项）

- [x] **13. 加号展开动画** (`id=6690c71362`)
  - **方案**：点击右下角 `+` 时按钮原地微扩并轻旋，丝滑衔接底部新建弹窗拉起，弹窗关闭后复位（严格遵守 Nord Frost 无回弹过冲约束）。
  - **涉及**：`MainScreen.kt`
- [x] **14. 卡片收起动画** (`id=a563d8cbe3`)
  - **方案**：勾选完成、删除或移出当前列表时，卡片平滑折叠高度并淡出，避免列表生硬跳变。
  - **涉及**：`ScheduleCard.kt`、`OverviewDragBoard.kt`、各列表页
- [x] **15. 全局震动适配** (`id=6427aa49fa`)
  - **方案**：统一封装分级触感反馈（轻触、勾选完成、长按起拖、档位/时间刻度吸附、删除确认），适配 `HapticFeedback` 与 `VibrationEffect`。
  - **涉及**：新增触感工具类及各交互组件
- [x] **16. 顶栏手动同步按钮** (`id=f0f6089fca`)
  - **方案**：顶栏右侧（主题切换按钮旁）增加小型手动同步图标，点击触发 `triggerManualSync`，同步中旋转反馈，完成后 Toast 提示。
  - **涉及**：`MainScreen.kt`
- [x] **17. 安卓端导出 / 导入数据** (`id=ee7cd28ab7`)
  - **方案**：设置页新增 `.daynew.zip` 导出 / 导入（包含 `data.json` + `completions.ndjson`，与桌面端格式完全互通），通过系统 SAF 选择文件，导入前自动在本地 `backups/` 备份现有数据。
  - **涉及**：新增 `DataTransferService.kt`、`DayNewRepository.kt`、`MainScreenViewModel.kt`、`MainScreen.kt`

---

## Batch 4 — 新页面占位（1 项）

- [ ] **18. 新增专注页面** (`id=d14f9f43ab`)
  - **方案**：排在本轮最后；进入 Batch 4 时再与用户单独对齐详细设计（番茄钟 / 正计时、关联日程或目标、页面入口、专注时长记录方式）后实施。

---

## 推进原则与验收标准

1. **分批验证**：每完成一个 Batch，运行 `:core-sync:test` 与 `:app:testDebugUnitTest`，编译 Debug/Release APK，并在已连接真机上实测验证通过后再勾选本排期表对应条目。
2. **协议与设计红线**：`:core-sync` 保持纯 Kotlin JVM 零 Android 依赖；`data.json` / `completions.ndjson` 字段契约与 Windows 端 100% 互通；UI 严格遵守 Nord Frost 规范（扁平 1dp 边框、无模糊/发光/回弹过冲、等宽数字 `tnum`）。
