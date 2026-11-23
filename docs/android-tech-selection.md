# 日新 / DayNew 安卓端技术选型调研报告（P4）

> **阶段定位**：本文档为 P4 排期交付物（**只调研、暂不写安卓业务代码**）。待 Windows 版核心功能稳定后，按本选型蓝图启动安卓端开发。
> **可视化伴随报告**：[android-tech-selection.html](./android-tech-selection.html)（含五维雷达图、多端同步时序图与手机端竖屏线框图）。

---

## 1. 执行摘要与最终结论

**最终推荐**：采用 **纯原生 `Kotlin + Jetpack Compose`** 作为 Android 端主技术栈，同时在 Android 仓库内部拆出**零 `android.*` 依赖的纯 Kotlin 模块 `:core-sync`** 承载混合逻辑时钟、WebDAV 客户端、DTO 契约与字段级合并引擎。

### 一句话因果

**跨端真正难的不是那约千行同步算法，而是手机上的系统触点。**

- Windows 端涉及多设备同步与模型规则的 6 个核心源文件合计 1,702 行（含注释、Zip 备份与文件原子读写），其中真正决定跨端一致性的**纯契约与合并算法仅约 1,000 行**、结构纯正、无 UI 耦合，用 Kotlin 重写加双端共用「黄金测试向量（Golden Test Vectors）」只需 **1~2 天** 即可做到 100% 兼容。
- 真正决定「日新」在手机上好不好用的，是 **桌面小组件一键勾选（Glance AppWidget）**、**不依赖流氓保活的精确闹钟提醒（AlarmManager）**、**选中文字一键收入待办（Share Intent）** 以及 **120Hz 下「看见就拖、开缝让位」的跟手度**。这些能力全部紧贴 Android 原生边界，选跨端框架只会带来桥接税、包体积膨胀与系统集成妥协。

---

## 2. 现状盘点：Windows 端需要跨端对齐的核心契约

经过核对 Windows 端源码，Android 端**只需对齐协议与行为语义**，不需要在界面布局上强求照搬桌面列数。所有对齐点已在 P1–P3 定型为清晰契约：

| 源码模块 | 行数 | 跨端需要完全对齐的契约细节 | 安卓端迁移工作量 |
|---|---:|---|---|
| [HlcClock.cs](../src/DayNew.App/Core/HlcClock.cs) | 164 行 | 格式 `{phys:x12}-{counter:x4}-{deviceId}`（12 位小写十六进制毫秒 + 4 位小写十六进制计数器 + 设备 ID）；纯字典序（Ordinal UTF-16）比较先后；含 `Tick`、`Receive`、`Observe` | 极低（~120 行 Kotlin，0.5 天） |
| [StorageDto.cs](../src/DayNew.App/Services/StorageDto.cs) | 137 行 | `DeviceSnapshotDto`（`version=1`, `deviceId`, `snapshotHlc`, `store`, `completions`）及四大实体 `GoalDto` / `TagDto` / `ScheduleDto` / `PresetDto`，均带 `hlc`、`del`、`fieldHlcs` | 极低（`@Serializable data class` 直译，0.5 天） |
| [StoreFile.cs](../src/DayNew.App/Services/StoreFile.cs) | 序列化节 | `JsonOptions` 使用 **`camelCase`** 命名（例如 `fieldHlcs`、`goalRefs`、`tagIds`、`isCompleted`、`snapshotHlc`）、中文不转义；日期存 `yyyy-MM-dd`、时刻存 `HH:mm`；追踪字段集合见下节 | 须严守大小写契约（1 个陷阱点） |
| [SyncMergeEngine.cs](../src/DayNew.App/Services/SyncMergeEngine.cs) | 492 行 | 按 `id` 对齐；`del=true` 墓碑仅在非删除端最大实体 HLC 严格更晚时才复活；`fieldHlcs` 字段级 LWW；`ScheduleFields` 中四大核心字段（`title`/`note`/`due`/`time`）双端在同周期内并发改动时，主条目留 HLC 较新者、败方克隆 `[冲突] ...` 副本（新 10 位 ID + `del=false`） | 低（核心算法 ~400 行，1 天 + 黄金测试集） |
| [RemindRules.cs](../src/DayNew.App/Core/RemindRules.cs) | 176 行 | 节点固定顺序 `pre15,pre5,start,end` 逗号拼接（空 = 不提醒，`none` 归一为空，默认 `"pre5,start"`，兼容旧版 `"HH:mm"` 自动迁移）；仅对有 `start`/`end` 时刻的日程触发 | 极低（~140 行，0.5 天） |
| [CompletionLog.cs](../src/DayNew.App/Services/CompletionLog.cs) | 177 行 | `CompletionEntry`（`id`, `for`, `at`, `done`）只追加 NDJSON，合并按 `(id, for, at, done)` 签名去重并按 `at` 字符串排序 | 极低（~100 行，0.2 天） |
| [WebDavService.cs](../src/DayNew.App/Services/WebDavService.cs) | 556 行 | 每设备独立 `device-<deviceId>.json.gz`（安卓建议前缀 `android-<shortId>`）；`PROPFIND Depth: 0` 探测 + 404 自动 `MKCOL`；`PROPFIND Depth: 1` 拉 `getetag` 与本地缓存比对跳过未变文件；`PUT .tmp` + `MOVE (Overwrite: T)` 原子更新（不支持时回退直写 `PUT`）；`503`/`429` 退避重试（`500ms`, `1200ms`）；指纹比对免冗余上传 | 中低（OkHttp + XML 解析 + GZip，1.5 天） |

### 关键序列化与字段基线细节（Kotlin 实现必须 1:1 匹配）

1. **JSON 键名必须使用 camelCase**：Windows 端采用 `PropertyNamingPolicy = JsonNamingPolicy.CamelCase` 与 `PropertyNameCaseInsensitive = true`。因此 Kotlin `kotlinx.serialization` 需要直接采用驼峰字段名，尤其注意 `RootX`/`RootY` → `rootX`/`rootY`、`FieldHlcs` → `fieldHlcs`、`GoalRefs` → `goalRefs`、`TagIds` → `tagIds`、`DurationMinutes` → `durationMinutes`。
2. **`fieldHlcs` 的逻辑分组键名必须逐字一致**：
   - `Goal`：`["title", "color", "isCompleted", "due", "tree"]`（其中 `tree` 整体打包 `rootX`/`rootY`/`nodes`/`edges`）
   - `Tag`：`["name", "color"]`
   - `Schedule`：`["title", "note", "due", "time", "repeat", "remind", "done", "flags", "goals", "tags", "order"]`（`time` 同时覆写 `start` 与 `end`；`flags` 同时覆写 `urgent` 与 `important`；`goals` 同时覆写 `goalId` 与 `goalRefs`）
   - `Preset`：`["title", "duration", "remind", "note", "flags", "goals", "tags"]`
3. **`presets == null` vs `presets == []` 语义区分**：`null` 代表旧数据缺省预设，`[]` 代表用户主动清空，安卓端序列化时不可把 `null` 写成空数组（`encodeDefaults` 策略需对齐）。

---

## 3. 备选技术栈深度对比

结合用户确定的权重：**原生体验优先（桌面小组件 Widget、精确闹钟、系统分享与动效手感）**，下表按 **原生体验 45% + 协议同步一致性 25% + 包体积与冷启动 15% + 单人长期维护成本 15%** 进行评估。

### 3.1 评分对比总表

| 评估维度 | 权重 | Kotlin + Jetpack Compose（原生 + `:core-sync`） | Kotlin Multiplatform + Compose Multiplatform | Flutter（Dart 自绘） |
|---|---:|---:|---:|---:|
| **桌面小组件 Widget** | 15% | **96**（Jetpack Glance 一等公民，Compose 语法直出远程视图，冷启动零额外运行时） | **90**（Android 端仍需在 `androidMain` 用 Glance 写原生层） | **58**（`home_widget` 仍需手写 Kotlin XML/Glance + SharedPrefs 桥接两套模型） |
| **精确闹钟与后台唤醒** | 15% | **96**（直调 `AlarmManager` / `BroadcastReceiver` / `WorkManager`，后台触发不必唤醒图形引擎） | **92**（仍集中在 `androidMain` 原生编写，无框架阻隔） | **64**（后台唤醒 Dart Isolate 冷启动重，或仍须用 Kotlin 写全套闹钟与同步服务） |
| **系统分享 / 快捷入口** | 5% | **95**（`ACTION_SEND` 透明 BottomSheet Activity、App Shortcuts、Tile 原生无缝） | **90**（`androidMain` 原生实现，略受多模块工程结构约束） | **66**（冷启动整套 FlutterEngine 弹极速收录框有明显白屏/首帧延迟） |
| **120Hz 拖拽与动效手感** | 10% | **92**（Compose `Modifier.pointerInput` + `graphicsLayer` 平移开缝，无桥接抖动，贴合 `nord-frost-ui`） | **86**（Android 上跑 CMP 底层即 Compose，多一层跨平台抽象与 Skia 资源包考量） | **88**（120Hz 自绘流畅，但手势冲突、系统返回预测动画及文字排版偶有非原生感） |
| **协议与合并一致性** | 25% | **90**（`:core-sync` 纯 Kotlin + Golden Vectors 验证，与 C# 100% 同构；未来可选开启 KMP） | **92**（同样用 Kotlin 写公共核心，天然跨 Android/iOS/Desktop 共享） | **78**（Dart 无 C# 同源感，需多维护一套 Dart 序列化/HLC/GZip 测试，且 XML/WebDAV 库生态较分散） |
| **APK 体积与冷启动** | 15% | **92**（Release R8 后 ~6–10 MB；Widget/Receiver 触发仅加载轻量 JVM 类） | **80**（多平台库依赖略增配置复杂度与编译耗时，Android 运行期体积接近原生） | **68**（需打包 `libflutter.so` + Skia/Impeller 引擎，Release 包普遍 18–25 MB） |
| **单人长期维护成本** | 15% | **85**（Android Studio 工具链最成熟，文档/报错/AI 辅助生态最高；仅需熟悉 Kotlin） | **68**（Gradle KMP/CMP 多 SourceSet 配置、版本矩阵调试心智负担显著高一档） | **76**（仅 UI 层高效；但日新必需的闹钟/小组件/分享仍逼着写 Kotlin，变相同时维护 Dart + Kotlin 两套代码） |
| **加权总分（满分 100）** | **100%** | **91.8（首选推荐）** | **85.6（备选）** | **71.5（不推荐）** |

### 3.2 逐项剖析

#### 方案 A：Kotlin + Jetpack Compose（内部拆分纯 Kotlin `:core-sync` 模块）—— **强烈推荐**
- **为什么最契合「日新」**：
  - 「日新」是一次只做一件事的任务工具，安卓端 80% 的高频动作发生在 **主屏之外**（扫一眼 Widget、点一下打勾、收到 `pre5` 提醒弹通知点完成、在微信/浏览器选中文字点「分享到日新」）。Compose + Glance + Android 标准服务能让这些极速场景在 200ms 内完成。
  - UI 设计规范 `nord-frost-ui` 已有完整现成的 `android-compose.md` 映射方案：直接套用 `LightNordPalette` / `DarkNordPalette`、`1.dp` 描边扁平卡片、`shadowElevation = 0.dp`（列表零阴影过绘）与 `NordMotion`（90/150/225ms CubicBezier）。
  - 把 `HlcClock`、`SyncMergeEngine`、`RemindRules`、DTO 与 WebDAV 放进纯 Kotlin `:core-sync`（不引入 `android.*`），就能直接在桌面端用 `./gradlew :core-sync:test` 秒级跑完黄金测试集，以后若增加 iOS 端也只需把 `:core-sync` 升级为 KMP `commonMain` 模块即可。

#### 方案 B：Kotlin Multiplatform (KMP) + Compose Multiplatform (CMP) —— **次优备选**
- **定位判断**：如果「日新」未来明确要做 macOS / Linux / iOS 三端且共用一套 UI，CMP 是很好选择；但现实是桌面端已用 **WPF 完成 P1–P3 且稳定运行**，短期并无用 CMP 替换 WPF 的打算。
- **额外成本**：在「只有 Windows (WPF) + Android」的双端格局下，一上来引入 CMP 对 Android 原生并没有多带来任何体验加成，反而增加了 Gradle 多平台插件兼容排查、Expect/Actual 样板代码和 Compose Multiplatform 与 Android Jetpack 最新特性间的版本滞后（尤其 Glance、Material3 Adaptive 迭代节奏）。
- **最佳实践折叠**：先用**纯 Kotlin `:core-sync`** 享受原生 Compose 的全部红利，同时保留未来零重构切入 KMP 的能力。

#### 方案 C：Flutter（Dart）—— **不推荐**
- **核心矛盾**：跨端 UI 的优势，会被「三大原生刚需」抵消甚至反噬：
  1. **Widget 数据双写与同步分裂**：桌面小组件点击「完成」必须修改本地持久化、写 `completions.ndjson` 并调度同步；Flutter 无法直接在 Glance Receiver 里跑 UI 业务，逼迫你用 Kotlin 再写一套数据层 + SQLite/JSON 读写，或者从后台唤醒沉重的 FlutterEngine。
  2. **双语言上下文切换**：WebDAV XML 解析、Keystore 加密、Exact Alarm 调度与重排都得写 Kotlin 原生通道（MethodChannel），最终一人既要调试 Dart 状态树又要处理 Android 原生渠道。

#### 附录 D：为何不选 `.NET MAUI / Avalonia`（按同权重加权得分 `65.6`，回应 `方案.html` 早期设想）
- 早先在 `方案.html` 中考虑过「用 C# 跨端直接引用 `DayNew` `.cs` 源码」。但如果按 **「原生体验优先」** 同权重拆解，C# 跨端尽管协议复用满分（`98`），但在 Widget（`45`）、闹钟冷启（`52`）与包体（`42`）拖累下仅得 `65.6`，存在三项硬伤：
  1. **Mono/.NET 运行时冷启动与体积**：即便启用 AOT / Trimming，Android APK 基础运行时仍有 ~15–25 MB，且 `BroadcastReceiver` 处理一次 `BOOT_COMPLETED` 或提醒触发时，拉起 .NET Runtime 的内存与耗电明显高于原生 Kotlin。
  2. **原生生态二等公民**：Jetpack Glance（小组件）、`WorkManager` 约束调度、Material You / 预测性返回手势都需要手动写 Android Callable Wrappers / Binding，更新永远慢原生半拍。
  3. **可复用逻辑本身太薄**：真正可跨端共享的纯契约与合并核心仅约千行，不值得为了复用这千行代码而背负整个 .NET Android 宿主运行时。

---

## 4. 四大原生体验场景落地架构（Android 专项）

| 原生触点 | 推荐原生组件 | 关键落地设计 | 与 Windows 端的对齐规则 |
|---|---|---|---|
| **1. 桌面小组件 (Widget)** | **Jetpack Glance (`GlanceAppWidget`)** + Room/DataStore 共享数据源 | 提供「今日待办列表 + 一键打勾」和「2x1 极速录入条」两种尺寸。小组件勾选直接在 `ActionCallback` 中完成：写本地 JSON/DB、更新 `fieldHlcs["done"]`、追加 `CompletionEntry`、刷新 Glance UI，再入队一个带 **6 秒防抖** 的 `WorkManager` 同步请求。 | 与 Win 端 `MainViewModel.ToggleDone` + 6 秒防抖自动同步语义完全一致。 |
| **2. 精确闹钟与提醒** | **AlarmManager (`setExactAndAllowWhileIdle`)** + `NotificationCompat` | 读取所有未完成且有 `start`/`end` 时刻的今日/未来日程，按 `RemindRules`（`pre15/pre5/start/end`）向系统注册下一个精确唤醒；通知栏直接带「完成」「延后 5 分」「延后 10 分」三个 PendingIntent Action；监听 `BOOT_COMPLETED` / `TIME_SET` / `TIMEZONE_CHANGED` 重算闹钟。 | 严守 `方案.html` 原则：「不追求流氓式后台进程常驻」，在设置页首次开启提醒时检查 Android 12+ `SCHEDULE_EXACT_ALARM` 权限与电池优化豁免即可。 |
| **3. 系统分享与快捷入口** | **`ACTION_SEND` 透明 `BottomSheet` Activity** + App Shortcuts | 任意 App 选中文字点击系统「分享 → 日新」，只拉起轻量透明宿主，自动填充标题、附带来源信息，默认高亮「今天」或「不定日期（未来事项）」，一键点击落地；长按桌面图标支持直达「新建今天」「新建未来」。 | 对齐「看见就拖、不填表」：默认收起高级字段，不弹窗填长表单。 |
| **4. 竖屏交互「看见就拖」** | **Jetpack Compose `HorizontalPager` + 自定义 `Modifier.pointerInput` 拖拽** | 桌面端是 4 档位横排 + 目标列；手机竖屏改造成：**顶部五档位分段胶囊条**（`逾期 / 今天 / 明天 / 本周 / 之后`）作为实时「放置靶区」——日常可左右横滑切档；**长按卡片拖起时，手指上移至顶部对应档位胶囊即变亮吸附改 `Due`**，下移至底部弹出的「目标芯片区」即挂接 `Goal`；同档位内上下拖动复用 `nord-frost-ui` 90–150ms 平移开缝让位。 | 依然保持「档位是由 `Due` 算出来的，不存 `Lane` 字段」；移动档位只更新 `due` + `fieldHlcs["due"]`。 |

---

## 5. 双端协议 100% 一致性保障方案（Golden Test Vectors）

放弃跨语言运行时共享，不等于放弃代码级约束。双端通过同一份 **黄金测试集（`tests/golden-sync-vectors.json`）** 自动锁死兼容性——Windows 端 xUnit/NUnit（或验证器）与 Android `:core-sync` Gradle 测试同时读取同一组 JSON 用例并校验输出**逐字段完全相等**：

1. **HLC 边界向量**：物理时间回拨时 counter 递增；counter 溢出 `0xFFFF` 时进位 `phys`；不同 `deviceId`（如 `win-a1b2c3` vs `android-9f8e7d`）按 Ordinal 字典序排序一致。
2. **字段级无冲突合并向量**：A 端改 `title`、B 端离线改 `remind` / `tags` → 合并结果同时保留双方改动，不产生 `[冲突]` 副本。
3. **核心字段并发冲突向量**：双端均晚于 `lastSyncedRemoteHlc` 修改同一条目的 `due` 或 `time` → 主条目取 `fieldHlcs` 更晚者，同时按确切规则生成 `[冲突] ...` 副本（`del=false`）。
4. **墓碑仲裁向量**：
   - A 端删除（`del=true`, HLC=`t2`），B 端未改（HLC=`t1`）→ 保持 `del=true`；
   - A 端删除（HLC=`t2`），B 端在 `t3 > t2` 修改复活 → 恢复 `del=false`；
   - 双端相同 HLC 下删除优先（`ResolveTombstone` `cmp >= 0` 语义）。
5. **GZip 快照互解向量**：用 Windows `WebDavService.SerializeSnapshotGzip` 生成的真实 `.json.gz` 字节流直接喂给 Kotlin `GZIPInputStream + Json.decodeFromString<DeviceSnapshotDto>()`，再反向生成让 Windows 端反序列化。

---

## 6. 建议的 Android 工程骨架与分期蓝图（暂不开发）

待 Windows 版功能进一步收敛后，按以下低风险顺序推进安卓端：

### 建议目录结构

```text
android/                              # 未来新增，现阶段不建目录
├── settings.gradle.kts
├── core-sync/                        # 纯 Kotlin JVM 模块（未来可无痛转 KMP commonMain）
│   └── src/
│       ├── common/                   # HlcClock, RemindRules, StoreDto, DeviceSnapshotDto
│       ├── merge/                    # SyncMergeEngine, FieldBaselineTracker
│       ├── webdav/                   # WebDavClient (PROPFIND/ETag/MKCOL/MOVE/503退避)
│       └── test/                     # 加载仓库 tests/golden-sync-vectors.json
└── app/                              # Android 原生应用（Kotlin + Jetpack Compose）
    ├── ui/
    │   ├── theme/                    # NordFrostTheme (LightNordPalette / DarkNordPalette)
    │   ├── screens/                  # 档位页 / 未来事项 / 截止日期 / 视图 / 目标 / 四象限 / 标签 / 设置
    │   └── components/               # 卡片、拖拽开缝列表、顶部档位放置胶囊、底部目标抽屉
    ├── widget/                       # TodayGlanceWidget + 勾选 ActionCallback
    ├── alarm/                        # AlarmScheduler + BootReceiver + NotificationActions
    ├── share/                        # ShareToDayNewActivity (ACTION_SEND BottomSheet)
    └── sync/                         # Android Keystore 加密存储凭证 + WorkManager 调度
```

### 分阶段实施路线（A0 → A4）

- **A0 · 纯 Kotlin 同步核与黄金测试**：建立 `:core-sync`，跑通与 Windows 端 100% 对齐的 HLC、DTO、WebDAV 与 `SyncMergeEngine` 测试；实测坚果云移动端 `PROPFIND` / `ETag` / `GZip`。
- **A1 · 最小可用主界面（Nord Frost + 看见就拖）**：搭建底部导航（概览 / 视图 / 目标 / 更多）、顶部五档位横滑 + 长按拖向档位胶囊改 `Due`，打通本地原子落盘与 `completions.ndjson` 追加。
- **A2 · 原生三大触点落地**：接入 Jetpack Glance 今日小组件、AlarmManager 节点提醒（`pre15/pre5/start/end` + 贪睡）、系统文本分享快录，并完成 `WorkManager` 后台增量同步。
- **A3 · 全页面补全**：补全截止日期分组、未来事项、逾期角标、四象限、标签及目标进度卡片。
- **A4 · 打磨与联调**：Windows 端与 Android 端实机真同步压力测试（弱网、503 重试、离线冲突生成 `[冲突]` 副本、锁屏闹钟唤醒）。
