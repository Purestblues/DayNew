# AGENTS.md — 日新 / DayNew（Android 端）

个人日程（本质是任务）管理软件 Android 原生端，采用 **Kotlin + Jetpack Compose + 纯 Kotlin `:core-sync` 模块**，与 Windows 端（WPF）通过同一份多设备同步协议与黄金测试集保持 100% 互通。

## 模块划分

```text
android/
├── settings.gradle.kts        # 含 :core-sync 与 :app 两个模块
├── gradle/libs.versions.toml  # 统一版本目录
├── core-sync/                 # 纯 Kotlin JVM 模块（零 android.* 依赖）
│   └── src/main/kotlin/com/daynew/sync/
│       ├── HlcClock.kt        # 混合逻辑时钟（{phys:x12}-{counter:x4}-{deviceId}）
│       ├── StorageDto.kt      # 6 类实体 + DeviceSnapshotDto + CompletionEntry
│       ├── DeadlineRules.kt   # 截止日期与 HH:mm 解析/逾期判定
│       ├── RemindRules.kt     # pre15,pre5,start,end 相对提醒规则
│       ├── RepeatRules.kt     # 每天 / 每周 / 每月周期推进
│       ├── CompletionLogCodec.kt  # completions.ndjson 编解码与去重合并
│       ├── SyncMergeEngine.kt # 字段级 LWW、墓碑仲裁、[冲突] 副本生成
│       └── WebDavClient.kt    # OkHttp WebDAV 客户端（PROPFIND/MKCOL/PUT+MOVE/GZip）
└── app/                       # Android 原生应用（minSdk 29, targetSdk 35）
    └── src/main/kotlin/com/daynew/app/
        ├── MainActivity.kt
        └── ui/theme/NordFrostTheme.kt  # Nord Frost 主题令牌与动效常量
```

## 协议与架构红线（不可违背）

1. **`:core-sync` 严禁引入任何 `android.*` 或 `androidx.*` 依赖**，保证可在桌面 JVM 秒级跑完 `./gradlew :core-sync:test`，并保留未来无痛迁移到 KMP `commonMain` 的能力。
2. **六类同步实体必须完整**：`Goals`、`Tags`、`Persons`、`Locations`、`Schedules`、`Presets`（与 Windows `StorageDto.cs` 严格一致）。
3. **JSON 键名严守 camelCase**：与 C# `JsonNamingPolicy.CamelCase` 完全对齐，尤其是 `rootX`、`rootY`、`isCompleted`、`fieldHlcs`、`goalRefs`、`tagIds`、`personIds`、`locationIds`、`durationMinutes`、`snapshotHlc`、`for`。
4. **`presets == null` 与 `presets == []` 语义分离**：`null` 代表旧数据缺省预设，`[]` 代表用户主动清空；必须保持 `encodeDefaults = true` 且 `explicitNulls = true`。
5. **`fieldHlcs` 逻辑分组键名必须与 Windows `StoreFile.cs` 逐字一致**。
6. **档位由 `Due` 实时计算，严禁在实体上存储 `lane` 字段**。

## UI 规范（Nord Frost UI）

- 严格遵循 `nord-frost-ui` 技能与 `ui/theme/NordFrostTheme.kt`：
  - 浅色 Snow Storm（`#ECEFF4` / `#FFFFFF`）与深色 Polar Night（`#181A20` / `#21252E`）双套令牌，禁止仅靠系统 `dynamicColor`。
  - 卡片统一扁平 `1.dp` 细边框 + `RoundedCornerShape(8.dp)` + `shadowElevation = 0.dp`。
  - 禁止列表模糊（`RenderEffect.createBlurEffect`）、霓虹发光阴影、回弹过冲动画（`Spring.DampingRatioHighBouncy`）。
  - 所有数字、日期、计数使用等宽数字 `fontFeatureSettings = "tnum"`。
  - 交互触控热区保持最小 `48.dp × 48.dp`。

## 常用命令（在 `android/` 目录下执行）

```powershell
# 运行纯同步核单元测试（含 ../tests/golden-sync-vectors.json 黄金测试集）
.\gradlew.bat :core-sync:test

# 编译 Debug APK（已在 gradle.properties 开启 android.overridePathCheck=true 兼容中文仓库路径）
.\gradlew.bat :app:assembleDebug
```
