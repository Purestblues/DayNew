# AGENTS.md — 日新 / DayNew

个人日程（本质是任务）管理软件。当前处于 **原型阶段**：只有 Windows 前端，数据仅在内存，没有后端、没有同步。

## 一句话定位

不是日历软件，是任务软件。核心交互：**看见就拖，不填表**。

六档位：逾期 / 今天 / 明天 / 本周 / 之后 / 某天。
档位不是字段，是由 `Due` 和今天的关系**算出来的**——所以横向拖一下就等于改日期，不需要打开任何表单。

## 目录结构

  // ==================== 页面路由 ====================

  Pages: PageKind 枚举（Overview / Timeline / Tags / Goals / Eisenhower / Overdue），
    NavItemVM 一项 = 一个 dock 按钮（支持逾期计数角标）；MainViewModel.NavItems 定义六页。
  「有实现」的页在页面宿主里各占一层 Grid，用 IsOverview / IsGoals / IsOverduePage 等控制可见；
    其余页落到 IsPlaceholder 那层，显示 NavItemVM.Blurb。
  目前六页均有实现：概览（今天/明天/本周/之后 + 目标列）、视图、标签、目标、艾森豪威尔看板、逾期。

  // ==================== 目标 ====================

  GoalVM：标题 + 固定色（GoalPalette 轮流取）+ 任务集合（Rebuild 里重算）+ 进度。
  任务通过 TaskVM.Goal 挂到一个目标，最多一个；卡片上不写目标名，只在标题前一个 6px 色点。
  交互：把任务卡片拖到目标卡片上 = 挂过去（拖拽代码里叫“ drag to goal ”）；
    目标卡片点一下就地展开看它下面的任务。
  无日期任务（Due == null）落到「之后」档的末尾，卡片上写「不定日期」。
  删掉一个目标不会删任务，只把任务的 Goal 置空。

```
DayNew.sln
方案.html                     设计讨论稿（决策、同步方案、里程碑都在里面）
src/DayNew.App/
  Program.cs                  自定义入口：先执行 VelopackApp.Build().Run() 生命周期钩子，再启动 WPF App
  App.xaml(.cs)               启动：单实例 Mutex + 先定主题，再开窗口
  Assets/DayNew.ico           应用图标（嵌入主程序与安装器）
  Themes/
    Dark.xaml  Light.xaml     同一批 token 的两套值，运行时整体替换（索引 0）
    Styles.xaml               字体、文本、按钮、卡片、档位列、dock、分段按钮、滚动条等所有样式
  Core/
    ObservableObject.cs       INotifyPropertyChanged 基类
    Converters.cs             ZeroToVisible / EmptyToVisible / EmptyToCollapsed / BoolToVisible / RatioToWidth / TextEquals
    Motion.cs                 nord-frost-ui 动效常量（90/150/225ms，CubicEase）与安全 Transform 辅助
  Services/
    ThemeService.cs           读系统偏好、明暗切换、换掉 MergedDictionaries[0]
    DayStore.cs               **全库唯一存储**：Schedules / Goals / Tags 三个集合 + 增删查
    StorageDto.cs             落盘用的 DTO（和实体分开，文件格式 = 长期契约）
    StoreFile.cs              本地 JSON 持久化：原子写、防抖 1s、上一版备份、坏文件隔离
  ViewModels/
    TaskVM.cs                 一条日程（底层实体：标题/备注/时间/重复/提醒/完成/目标/两个标记）
    TagVM.cs                  标签（底层实体；日程里存的是引用，不是字符串）
    GoalVM.cs                 远景目标（标题 + 颜色 + 进度 + 下面挂的日程）
    TimelineVM.cs             视图页的四个小 VM：一天（TimelineDayVM）、一个块、一小时刻度、缩放档
    ScheduleEditorVM.cs       新建 / 编辑日程的弹窗状态（草稿，保存前不写回实体）
    BoardVM.cs                标签页的一行（TagRowVM）+ 四象限的一格（QuadrantVM）
    LaneVM.cs                 一个档位（Kind + 标题 + 卡片集合）
    NavItemVM.cs              左侧 dock 的一项目（PageKind + 标题 + 图标 + 说明）
    MainViewModel.cs          档位重建、拖拽落点、目标增删、时间与视图页、页面路由、演示数据
  Views/
    MainWindow.xaml(.cs)      主窗口：标题栏、dock、四档位 + 目标列、视图页、四象限、逾期页、弹窗浮层、胶囊提示、拖拽实现
```

## 数据层（最重要的一条）

底层**只有三类实体**，全部住在 `Services/DayStore.cs`：

| 实体 | 类 | 说明 |
|---|---|---|
| 日程 | `TaskVM` | 全库唯一，不在任何页面复制。页面上出现的都是同一个对象引用 |
| 目标 | `GoalVM` | 同上；日程通过 `Goal` 引用它 |
| 标签 | `TagVM` | 同上；日程的 `Tags` 装的是 `TagVM` 引用（**不是字符串**） |

规矩：

- **任何页面不得自己造日程。** 创建只有一个入口：在「视图」页时间轴空白处按住拖出一段（`FinishCreate`），
  松手弹「新建日程」弹窗，确认后才真正进 `store.Schedules`（`MainViewModel.SaveEditor`）。
  概览 / 目标 / 标签 / 四象限都只能从 `store.Schedules` 里摘取显示。顶部的快速录入框已经因此删掉。
- 标签改名不需要遍历日程：`TagVM.Name` 一变，日程靠 `PropertyChanged` 监听立刻刷新（`TaskVM.OnTagPropertyChanged`）。
- `MainViewModel._all` 是个属性，直接返回 `store.Schedules`，不是副本。**别再加第二个列表。**
- 新日程的标题、标签、目标、时间都在弹窗里选，**没有“先建一条空日程再补”**的中间态。

## 落盘（目前没有服务端，也不用手动备份）

- 文件位置：`%LOCALAPPDATA%\DayNew\data.json`（状态栏右下角的文字鼠标悬停就能看到完整路径）
- 上次的版本会自动留在同目录 `data.prev.json`；改坏了就把它换回 `data.json`
- 写文件是**原子**的：先写 `.tmp` 再 `File.Replace`；中途断电最多丢最后一次改动，不会留半个 JSON
- **序列化与写盘在后台线程**：UI 线程只负责拍快照（读字段、拼 DTO），耗时部分 `Task.Run`。
  快照带递增版本号，旧快照不会盖掉新快照。以后数据涨到几十 MB 也不会卡界面。
- 改动停下来 1 秒才写（拖动时不会一秒写几十次）；关窗口和退出一定会强制写一次
- 读不回来的文件会被改名成 `data.broken-时间戳.json` 并记到 `load-error.txt`，不会静默重建演示数据
- 时间存 `yyyy-MM-dd` / `HH:mm` 字符串，人可以直接打开改；中文不转义
- **实体都有稳定 `Id`**（日程 / 目标 / 标签），文件里靠 id 互指。以后做同步就靠它认“同一条”。

### 完成记录（单独文件，重要）

`%LOCALAPPDATA%\DayNew\completions.ndjson` —— 一行一次事件，**只追加、永不重写**：

```json
{"id":"6b0f965180","for":"2026-11-20","at":"2026-11-20 20:02:09","done":true}
```

- 取消勾选也记一行（`done:false`），按时间顺序读最后一条就知当前状态，误点可追溯
- **绝对不要把完成历史写进主 data.json**。5 个每天的习惯十年就是 1.8 万条，
  主文件会被历史撑爆、保存也会变慢。历史归历史，现状归现状。
- 写入口只有 `MainViewModel.ToggleDone` → `CompletionLog.Append`。

### 容量参考（2026-11 在这台机器上实测）

| 记录数 | 缩进 JSON | 序列化 | 反序列化 |
|---|---|---|---|
| 5,000 | 2.0 MB | 12 ms | 57 ms |
| 20,000 | 8.1 MB | 76 ms | 106 ms |
| 50,000 | 20 MB | 79 ms | 241 ms |
| 100,000 | 40 MB | 130 ms | 550 ms |

正常使用（每天 5～10 条）十年 ≈ 1.5 万条 ≈ 6 MB，JSON 完全够用。
到 50 MB 再考虑换 SQLite / NDJSON 追加写；不要现在提前优化。

## 关键约定（改代码前必读）

1. **档位由 `Due` 推导**，见 `MainViewModel.LaneFor` / `DueForLane`。
   不要给 `TaskVM` 加 `Lane` 字段，否则拖拽和“逾期自动归位”会立刻不一致。
   现在是五档：逾期 / 今天 / 明天 / 本周 / 之后（「某天」已取消，无日期任务并入「之后」）。
2. **一周以周日结束**（`EndOfWeek`）。「本周」= 明天之后到本周日；「之后」= 下周一及以后。
3. **深色/浅色两套 token 必须同时改**：`Themes/Dark.xaml` 和 `Themes/Light.xaml` 的 key 必须完全一致，
   缺一个 key 不会报错，会静默回落到另一套的颜色。
4. **样式一律用 `DynamicResource` 引用颜色**，用 `StaticResource` 只会锁死当前主题，切换时不刷新。
5. **不要引入重特效**：无 Acrylic/Mica、无渐变光晕、无回弹动画。设计规范见 skill `nord-frost-ui`。
6. 拖拽是**自己实现的指针拖拽**（`MainWindow.xaml.cs`），没有用 WPF DragDrop。
   原因：要同时支持横向改档位和纵向改顺序，还要落点高亮，OLE 拖放反而更绕。
7. 弹窗里提醒默认**空**（= 不提醒）；种子数据里的 `09:00` 是历史值。`MetaText` **不显示 09:00**
   —— 满屏时间里最吵的就是这个默认值。
8. **左侧 dock 是页面路由，不是装饰**：`NavItems` 里五页，只有「概览」有实现，其余页显示 `NavItemVM.Blurb` 说明。
   新页落地的做法是：在页面宿主里加一层带 `Visibility="{Binding IsXxx, Converter={StaticResource BoolToVisible}}"` 的布局，
   同时改 `MainViewModel.IsOverview` 那组计算属性的含义（目前只有 Overview / 占位两态）。
9. `LanesHost` 这个 x:Name 被拖拽代码依赖（算落点、算插入位置），**移动标题栏或页面宿主时不要改它的名字**。
10. dock 的图标是 Segoe MDL2 Assets 字形码（在 `NavItemVM` 里以 `\uXXXX` 写），换页时只换字形码，不要换成图片资源。
11. **时间只存时刻，不存日期**：`TaskVM.StartTime / EndTime` 是 `TimeSpan?`，它属于哪天由 `Due` 决定。
    不要另存 `StartDate`，否则改日期时要同步两个字段，必错。有 `StartTime` 就必须有 `Due`（`SetTime` 里会自动补今天）。
12. **视图页的网格线是画刷，不是元素**：`CreateGridBrush` 生成一个平铺的 `DrawingBrush`，贴在天列的 Background 上。
    一分钟档一天要 1440 条线，用 Line 元素画会卡。改网格只能改这个画刷，不要在 XAML 里堆线。
    主题切换后画刷里的颜色不会自己变，`Theme_Click` 里调了 `RefreshTimelineBrushes()`，改主题逻辑时别忘。
13. 视图页是三个 ScrollViewer（日期表头 / 左侧刻度 / 网格）靠 `BodyScroll_ScrollChanged` 同步偏移，
    表头和刻度都设了 `IsHitTestVisible="False"`，只能被同步，不能自己滚。
14. 缩放时保持「屏幕顶部是几点」不变（`Zoom_Click` 里按旧 PxPerHour 换算再回写）。
15. 全局 `ScrollBar` 样式必须同时处理横竖：早先写死了 `Width=6`，导致横向滚动条被压成 6px 宽、完全不可用。
    现在靠 `Orientation` 触发器分别给宽/高，模板里横条还要把 `IsDirectionReversed` 改成 False。
16. **落点判定必须先看 `IsVisible`。** 页面切走用的是 `Collapsed`，容器还在、坐标还是旧的，
    不检查就会把“拖到别的页的元素上”误判成“拖到了某个档位”（曾把任务莫名改成今天）。
    `HitLane` / `HitGoal` / `HitQuadrant` / `InsertIndex` 四处都加了，新增落点判定时照样加。
17. **四象限靠两个 bool**：`IsUrgent` / `IsImportant`。两个都没打的落「没标记」格，
    所以四格实际是“全部日程的分类视图”，不是“只看已标记的”。
18. **标签页是派生视图**：`TagRows` 在 `Rebuild` 里从 store 现算。
    标签改名只改 `TagVM.Name` 一处，日程靠 `PropertyChanged` 监听刷新（`TaskVM.OnTagPropertyChanged`）。
19. 时间轴拖块吸附到缩放档的间隔，**时长不变**（`MoveBlock` 里按原时长推 End）。
20. **日程的所有字段只在弹窗里改**（`ScheduleEditorVM` + `MainWindow.xaml` 的 `EditorLayer`）：
    双击时间轴上的块 / 概览里的卡片打开，Esc 或「取消」丢弃。拖出一段新建也走同一个弹窗。
    就地标题编辑（`IsEditingTitle` → 块模板里的 TextBox）已经删除，不要再加回来。
21. **同一时间只能干一件事**：`MainViewModel.FindFreeStart` 从想要的位置往两边按缩放间隔找最近的空档。
    拖动块的幽灵、概览里手打的时间、弹窗里的保存全走它，所以看到的落点就是存下去的位置。
    已有的重叠数据**不自动清洗**（那是用户的数据）；动一下其中一条就自然解开。
22. **重叠并排分宽度**：`MainViewModel.LayoutOverlaps` 把相互重叠的块归簇，簇内贪心分列。
    块的 Left/Width 只能由它算，不要在 XAML 里写死 `Canvas.Left`。正常使用下它只在老数据上生效。
23. **滚动条上按下不算“拖时间段”**：`IsInteractive` 把 `Thumb` 和 `ScrollBar` 也算交互。
    否则拖滚动条会凭空建出日程（曾经就这么出了十几条「未命名」）。
24. **窗口位置在 `CenterOnScreen` 里显式算**：高 DPI / 多屏下 `WindowStartupLocation=CenterScreen`
    会把窗口丢到屏幕外（实测 y=-1384），截图和用户都会看到“界面碎裂”。
25. 旧文件里所有标签同一个颜色时，加载阶段当成“未分配”重新上色（`StoreFile.Apply` 那段迁移），不要删。
26. **拖拽时其它卡片要开缝**（手机桌面拖图标那种手感）：`UpdateGaps` 用 `TranslateTransform`
    把落点之后的卡片整体挪一个卡高（120ms 缓动），不重排集合。同一档位内按“先抽出再插入”算偏移，
    所以源卡片的空位会自己合上。松手/离开档位在 `ResetAllShifts` 里清零。
    `InsertIndex` 必须：① 排除被拖的那张（否则同档位内差一位）；② 减掉 `_gapTargets` 里的位移
    （否则卡片一让位就把落点算翻，会抽搽）。
27. **标签页 / 目标页 / 四象限都是卡片网格**：`ItemsPanel = WrapPanel` + 固定宽度（标签 236 / 目标 272 /
    四象限 212），窗口越宽列越多；卡片宽度不跟着窗口拉糊，避免一行字被拉成一整排。
28. **块下方的标签和目标要有“够高才显示”的判断**：`TimelineBlockVM.HasRoomForMeta`（Height >= 52）。
    否则 30 分钟的块会露出半行字，看起来像渲染错误。
29. **最大化尺寸自己接管**（`MainWindow.MaximizeHook` 拦 `WM_GETMINMAXINFO`）。
    `WindowStyle=None` + `WindowChrome` 的窗口最大化时，Windows 会把它往屏幕外每边多长 6 DIP
    （实测窗口变成 -12,-12 → 2892,1812），最底下的横向滚动条和状态栏就切没了。
    钩子把 `ptMaxSize/ptMaxPosition` 改成显示器工作区，别删；也别只在外层加 Margin 绕。

## 常用命令

```powershell
# 编译（开发用）
dotnet build DayNew.sln -c Debug

# 发布 Velopack 安装包（输出到 releases\win-x64\，自动检测 net10.0-x64-desktop 运行时）
.\scripts\pack.ps1                # 读取 csproj 中的 <Version>
.\scripts\pack.ps1 -Version 0.2.0 # 指定新版本号（vpk 要求递增，或先清理同版本包）

# 旧版单文件直出（如需保留便携单文件到 dist\）
dotnet publish src\DayNew.App\DayNew.App.csproj -c Release -r win-x64 `
  --self-contained false -p:PublishSingleFile=true -o dist

# 运行（进程在跑会锁住 bin 输出，先杀进程再 build）
Get-Process DayNew -ErrorAction SilentlyContinue | Stop-Process -Force
.\src\DayNew.App\bin\Debug\net10.0-windows\DayNew.exe
```

安装版说明：
- Velopack `PackId` 为 `DayNew.App`（安装目录 `%LOCALAPPDATA%\DayNew.App\`），**刻意与数据目录 `%LOCALAPPDATA%\DayNew\` 隔离**，卸载不会误删用户数据。
- 快捷方式由安装器自动管理：桌面 `日新.lnk`、开始菜单 `程序\日新.lnk`，并在 Windows「设置 → 应用」注册卸载项。
- **单实例保护**：`App.OnStartup` 用命名 Mutex；第二个进程会把已有窗口前置然后自己退出。`Program.cs` 中的 `VelopackApp.Build().Run()` 必须在 Mutex 之前执行。
  不要去掉——两个进程同时写 `data.json` 是最常见的数据丢失方式。

## 已完成 / 未完成

已做（纯前端，六个页面全部有东西）：

- **概览**：四档位列（今天 / 明天 / 本周 / 之后）+ 最右目标列（只显示，不创建）；卡片横拖改档位、纵拖改顺序，**拖动时其它卡片滑开让位（iOS 手感）**，落点高亮；单击等待双击判定后展开，双击或展开态双击卡片空白处直接弹编辑窗，点同页其它空白处收起
- **逾期（Dock 独立页）**：网格卡片显示所有逾期未完成日程，Dock 旁带红色逾期计数角标（折叠 dock 时显示红点）
- **卡片展开**：改标题、备注、标签、时间（打 `9-10:30`）、重复、提醒、紧急/重要、删除
- **弹窗（两用）**：拖出一段 / 双击块或卡片 → 名称、日期、起止时间、标签（可当场新建）、目标、重复、提醒、紧急/重要、备注；边打字边算冲突，保存时自动让到最近空档
- **视图**：**唯一的创建入口**；块可拖改天、改时刻（时长不变，**幽灵直接吸到最近的空档**）；块下方显示所属标签和目标，极矮块自动切换单行布局不裁切文字；缩放 1时/30分/15分/5分/1分；今天列高亮；回到今天平滑滚动
- **标签**：卡片网格，改名只改实体一处，所有日程立刻跟着变；可删除
- **目标**：卡片网格（色点 + 名称 + 进度条 + x/y）；新建 / 改名 / 删除；任务拖到目标卡片挂过去；卡片展开看任务
- **艾森豪威尔看板**：2x2 四格，格内是卡片网格；任务在格子间拖就改两个标记；「没标记」格装两个都没打的
- **动效与反馈**：按 `nord-frost-ui` 补齐页面切入、弹窗进出、侧栏收展、展开显隐、勾选、按钮/卡片微缩放、拖拽悬浮阴影与磁吸、底部胶囊 Toast（`90–225ms`，无过冲）
- 左侧 dock 可收起（只留图标）；卡片右键菜单（完成 / 移到今天 / 移到明天 / 删除）
- **时间不重叠**：新建 / 拖动 / 手打时间都会自动找最近的空档
- 明暗主题 + 跟随系统、最小宽度 1080 不错版
- **本地持久化**：自动存 `%LOCALAPPDATA%\DayNew\data.json`，关掉再开数据还在（已实测重启不丢）

未做：

- 一切持久化（目前已做本地 JSON；**多设备同步 / 手机端仍未做**）
- 重复规则的真实推进逻辑（现在只是显示文本）
- 真正的提醒（无托盘、无通知）
- 多设备同步（坚果云 WebDAV + 每设备一个快照文件 + HLC 字段级合并）
- 安卓端
- 视图范围固定 42 天，不是真无限横向滚动；拖到边缘不自动滚；不能拖底边改时长
- 标签页不能凭空新建标签（标签从任务卡上长出来；弹窗里可以当场建）
- 已有的重叠数据不会自动清洗（只对新操作生效）

## 死约束

- 没有用户许可，**不允许删除任何文件**。
- 数据模型改动前，先看 `方案.html` 里为什么只留这几个字段。
