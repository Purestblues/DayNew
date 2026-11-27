package com.daynew.app.data

data class TimelineBlockInput(
    val id: String,
    val startMinutes: Int,
    val endMinutes: Int,
)

data class TimelineBlockSlot(
    val id: String,
    val startMinutes: Int,
    val endMinutes: Int,
    val columnIndex: Int,
    val columnCount: Int,
)

/**
 * 单日时间轴重叠簇与贪心分列排布，与 Windows 端 MainViewModel.LayoutOverlaps 对齐。
 */
fun computeTimelineOverlapLayout(blocks: List<TimelineBlockInput>): List<TimelineBlockSlot> {
    if (blocks.isEmpty()) return emptyList()

    val normalized = blocks.map { item ->
        val start = item.startMinutes.coerceIn(0, 24 * 60 - 1)
        val end = item.endMinutes.coerceAtLeast(start + 15).coerceAtMost(24 * 60)
        item.copy(startMinutes = start, endMinutes = end)
    }.sortedWith(compareBy<TimelineBlockInput> { it.startMinutes }.thenByDescending { it.endMinutes }.thenBy { it.id })

    val result = ArrayList<TimelineBlockSlot>(normalized.size)
    var index = 0
    while (index < normalized.size) {
        val cluster = mutableListOf(normalized[index])
        var clusterMaxEnd = normalized[index].endMinutes
        var cursor = index + 1
        while (cursor < normalized.size && normalized[cursor].startMinutes < clusterMaxEnd) {
            cluster += normalized[cursor]
            if (normalized[cursor].endMinutes > clusterMaxEnd) {
                clusterMaxEnd = normalized[cursor].endMinutes
            }
            cursor++
        }

        val colEnds = mutableListOf<Int>()
        val assignedCols = IntArray(cluster.size)
        cluster.forEachIndexed { i, item ->
            var placedCol = -1
            for (c in colEnds.indices) {
                if (colEnds[c] <= item.startMinutes) {
                    placedCol = c
                    colEnds[c] = item.endMinutes
                    break
                }
            }
            if (placedCol < 0) {
                placedCol = colEnds.size
                colEnds += item.endMinutes
            }
            assignedCols[i] = placedCol
        }

        val totalCols = colEnds.size.coerceAtLeast(1)
        cluster.forEachIndexed { i, item ->
            result += TimelineBlockSlot(
                id = item.id,
                startMinutes = item.startMinutes,
                endMinutes = item.endMinutes,
                columnIndex = assignedCols[i],
                columnCount = totalCols,
            )
        }
        index = cursor
    }
    return result
}
