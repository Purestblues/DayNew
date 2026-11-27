package com.daynew.sync

import kotlinx.serialization.ExperimentalSerializationApi
import kotlinx.serialization.SerialName
import kotlinx.serialization.Serializable
import kotlinx.serialization.json.Json

@OptIn(ExperimentalSerializationApi::class)
val DayNewJson: Json = Json {
    ignoreUnknownKeys = true
    encodeDefaults = true
    explicitNulls = true
    coerceInputValues = true
    prettyPrint = true
}

@OptIn(ExperimentalSerializationApi::class)
val DayNewCompactJson: Json = Json {
    ignoreUnknownKeys = true
    encodeDefaults = true
    explicitNulls = true
    coerceInputValues = true
    prettyPrint = false
}

/**
 * 落盘与同步契约 DTO，严格对齐 Windows 端 Services/StorageDto.cs 的 camelCase JSON 键名。
 * presets 为 null 表示老版本文件，[] 表示用户主动清空。
 */
@Serializable
data class StoreDto(
    val version: Int = 1,
    val goals: List<GoalDto> = emptyList(),
    val tags: List<TagDto> = emptyList(),
    val persons: List<PersonDto> = emptyList(),
    val locations: List<LocationDto> = emptyList(),
    val schedules: List<ScheduleDto> = emptyList(),
    val presets: List<PresetDto>? = null,
)

@Serializable
data class GoalDto(
    val id: String = "",
    val title: String = "",
    val color: String = "#527CE6",
    val isCompleted: Boolean = false,
    val due: String? = null,
    val rootX: Double? = null,
    val rootY: Double? = null,
    val nodes: List<GoalNodeDto> = emptyList(),
    val edges: List<GoalEdgeDto> = emptyList(),
    val hlc: String = "",
    val del: Boolean = false,
    val fieldHlcs: Map<String, String>? = null,
)

@Serializable
data class GoalNodeDto(
    val id: String = "",
    val title: String = "",
    val isCompleted: Boolean = false,
    val due: String? = null,
    val x: Double = 0.0,
    val y: Double = 0.0,
)

@Serializable
data class GoalEdgeDto(
    val fromId: String = "",
    val toId: String = "",
    val kind: String = "belong",
    val label: String? = null,
    val fromSide: String? = null,
    val toSide: String? = null,
)

@Serializable
data class GoalRefDto(
    val goalId: String = "",
    val nodeId: String? = null,
)

@Serializable
data class TagDto(
    val id: String = "",
    val name: String = "",
    val color: String = "",
    val hlc: String = "",
    val del: Boolean = false,
    val fieldHlcs: Map<String, String>? = null,
)

@Serializable
data class PersonDto(
    val id: String = "",
    val name: String = "",
    val hlc: String = "",
    val del: Boolean = false,
    val fieldHlcs: Map<String, String>? = null,
)

@Serializable
data class LocationDto(
    val id: String = "",
    val name: String = "",
    val hlc: String = "",
    val del: Boolean = false,
    val fieldHlcs: Map<String, String>? = null,
)

@Serializable
data class ScheduleDto(
    val id: String = "",
    val title: String = "",
    val note: String = "",
    val due: String? = null,
    val start: String? = null,
    val end: String? = null,
    val repeat: String = "",
    val remind: String = "",
    val done: Boolean = false,
    val urgent: Boolean = false,
    val important: Boolean = false,
    val goalId: String? = null,
    val goalRefs: List<GoalRefDto> = emptyList(),
    val tagIds: List<String> = emptyList(),
    val personIds: List<String> = emptyList(),
    val locationIds: List<String> = emptyList(),
    val order: Int = 0,
    val hlc: String = "",
    val del: Boolean = false,
    val fieldHlcs: Map<String, String>? = null,
)

@Serializable
data class PresetDto(
    val id: String = "",
    val title: String = "",
    val durationMinutes: Int = 10,
    val remind: String? = null,
    val note: String = "",
    val urgent: Boolean = false,
    val important: Boolean = false,
    val goalId: String? = null,
    val goalRefs: List<GoalRefDto> = emptyList(),
    val tagIds: List<String> = emptyList(),
    val personIds: List<String> = emptyList(),
    val locationIds: List<String> = emptyList(),
    val hlc: String = "",
    val del: Boolean = false,
    val fieldHlcs: Map<String, String>? = null,
)

@Serializable
data class CompletionEntry(
    val id: String = "",
    @SerialName("for")
    val forDate: String? = null,
    val at: String = "",
    val done: Boolean = false,
)

@Serializable
data class DeviceSnapshotDto(
    val version: Int = 1,
    val deviceId: String = "",
    val snapshotHlc: String = "",
    val store: StoreDto = StoreDto(),
    val completions: List<CompletionEntry> = emptyList(),
)
