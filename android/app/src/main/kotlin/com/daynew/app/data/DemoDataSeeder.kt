package com.daynew.app.data

import com.daynew.sync.DeadlineRules
import com.daynew.sync.GoalDto
import com.daynew.sync.GoalRefDto
import com.daynew.sync.HlcClock
import com.daynew.sync.PresetDto
import com.daynew.sync.RemindRules
import com.daynew.sync.RepeatRules
import com.daynew.sync.ScheduleDto
import com.daynew.sync.StoreDto
import com.daynew.sync.TagDto
import java.time.LocalDate

/**
 * 首次运行无 data.json 时注入一套涵盖五档位的中文演示数据，便于直接体验拖拽改档与勾选记录。
 */
object DemoDataSeeder {
    fun seed(
        clock: HlcClock,
        today: LocalDate = LocalDate.now(),
    ): StoreDto {
        val goalWork = GoalDto(
            id = "goal_work01",
            title = "日新安卓端推进",
            color = "#527CE6",
            hlc = clock.tick(),
            fieldHlcs = stampFields(clock, listOf("title", "color", "isCompleted", "due", "tree")),
        )
        val goalLife = GoalDto(
            id = "goal_life01",
            title = "日常与健康",
            color = "#5C9A64",
            hlc = clock.tick(),
            fieldHlcs = stampFields(clock, listOf("title", "color", "isCompleted", "due", "tree")),
        )

        val tagWork = TagDto(
            id = "tag_work001",
            name = "工作",
            color = "#527CE6",
            hlc = clock.tick(),
            fieldHlcs = stampFields(clock, listOf("name", "color")),
        )
        val tagLife = TagDto(
            id = "tag_life001",
            name = "生活",
            color = "#5C9A64",
            hlc = clock.tick(),
            fieldHlcs = stampFields(clock, listOf("name", "color")),
        )
        val tagStudy = TagDto(
            id = "tag_study01",
            name = "学习",
            color = "#9D7298",
            hlc = clock.tick(),
            fieldHlcs = stampFields(clock, listOf("name", "color")),
        )

        val tomorrow = today.plusDays(1)
        val thisWeek = LaneRules.endOfWeek(today)
        val nextWeek = LaneRules.nextMonday(today)

        val schedules = listOf(
            newSchedule(
                clock = clock,
                id = "sch_over01",
                title = "补充多端同步协议校验用例",
                note = "长按卡片可拖到顶部「今天」或「明天」直接改期",
                due = DeadlineRules.formatDue(today.minusDays(1), null),
                important = true,
                goalId = goalWork.id,
                tagIds = listOf(tagWork.id),
                order = 1024,
            ),
            newSchedule(
                clock = clock,
                id = "sch_toda01",
                title = "走查竖屏五档位拖拽与开缝手势",
                note = "长按卡片后上移至顶部档位胶囊即可变亮吸附改 Due",
                due = DeadlineRules.formatDue(today, null),
                start = "14:00",
                end = "15:30",
                remind = RemindRules.DEFAULT,
                urgent = true,
                important = true,
                goalId = goalWork.id,
                tagIds = listOf(tagWork.id),
                order = 1024,
            ),
            newSchedule(
                clock = clock,
                id = "sch_toda02",
                title = "每日喝水与晚间拉伸 15 分钟",
                note = "勾选重复日程会自动推进到下一周期并追加 completions.ndjson",
                due = DeadlineRules.formatDue(today, null),
                repeat = RepeatRules.DAILY,
                goalId = goalLife.id,
                tagIds = listOf(tagLife.id),
                order = 2048,
            ),
            newSchedule(
                clock = clock,
                id = "sch_toda03",
                title = "阅读 Jetpack Compose 手势与合成优化章节",
                due = DeadlineRules.formatDue(today, null),
                tagIds = listOf(tagStudy.id),
                order = 3072,
            ),
            newSchedule(
                clock = clock,
                id = "sch_tomo01",
                title = "梳理阶段 A2 桌面小组件数据源接口",
                due = DeadlineRules.formatDue(tomorrow, null),
                start = "10:00",
                end = "11:00",
                remind = "pre15,start",
                important = true,
                goalId = goalWork.id,
                tagIds = listOf(tagWork.id),
                order = 1024,
            ),
            newSchedule(
                clock = clock,
                id = "sch_tomo02",
                title = "采购周末露营与补给食材",
                due = DeadlineRules.formatDue(tomorrow, null),
                goalId = goalLife.id,
                tagIds = listOf(tagLife.id),
                order = 2048,
            ),
            newSchedule(
                clock = clock,
                id = "sch_week01",
                title = "整理本周书房桌面与备份归档",
                due = DeadlineRules.formatDue(thisWeek, null),
                goalId = goalLife.id,
                tagIds = listOf(tagLife.id),
                order = 1024,
            ),
            newSchedule(
                clock = clock,
                id = "sch_late01",
                title = "规划 WebDAV 坚果云真机弱网联调清单",
                due = DeadlineRules.formatDue(nextWeek, null),
                goalId = goalWork.id,
                tagIds = listOf(tagWork.id, tagStudy.id),
                order = 1024,
            ),
        )

        return StoreDto(
            version = 1,
            goals = listOf(goalWork, goalLife),
            tags = listOf(tagWork, tagLife, tagStudy),
            schedules = schedules,
            presets = emptyList<PresetDto>(),
        )
    }

    private fun newSchedule(
        clock: HlcClock,
        id: String,
        title: String,
        note: String = "",
        due: String? = null,
        start: String? = null,
        end: String? = null,
        repeat: String = "",
        remind: String = "",
        urgent: Boolean = false,
        important: Boolean = false,
        goalId: String? = null,
        tagIds: List<String> = emptyList(),
        order: Int = 1024,
    ): ScheduleDto {
        val hlc = clock.tick()
        val fields = listOf(
            "title", "note", "due", "time", "repeat", "remind",
            "done", "flags", "goals", "tags", "persons", "locations", "order",
        )
        return ScheduleDto(
            id = id,
            title = title,
            note = note,
            due = due,
            start = start,
            end = end,
            repeat = repeat,
            remind = remind,
            done = false,
            urgent = urgent,
            important = important,
            goalId = goalId,
            goalRefs = if (goalId != null) listOf(GoalRefDto(goalId = goalId)) else emptyList(),
            tagIds = tagIds,
            order = order,
            hlc = hlc,
            del = false,
            fieldHlcs = fields.associateWith { hlc },
        )
    }

    private fun stampFields(clock: HlcClock, fields: List<String>): Map<String, String> {
        val hlc = clock.tick()
        return fields.associateWith { hlc }
    }
}
