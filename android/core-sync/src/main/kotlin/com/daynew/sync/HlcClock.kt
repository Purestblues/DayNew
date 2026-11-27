package com.daynew.sync

import java.util.Locale

/**
 * 混合逻辑时钟（Hybrid Logical Clock）：
 * 固定格式 "{12位十六进制物理毫秒}-{4位十六进制计数}-{设备ID}"，
 * 可直接按字典序（Ordinal / UTF-16）比较因果与时间先后，与 Windows 端 HlcClock.cs 1:1 对齐。
 */
class HlcClock(
    defaultDeviceId: String = DEFAULT_DEVICE_ID,
    private val nowMs: () -> Long = System::currentTimeMillis,
) {
    private var lastPhys: Long = 0L
    private var counter: Int = 0
    private var defaultDeviceId: String = normalizeDeviceId(defaultDeviceId)

    @Synchronized
    fun setDefaultDeviceId(deviceId: String?) {
        if (deviceId.isNullOrBlank()) return
        defaultDeviceId = normalizeDeviceId(deviceId)
    }

    @Synchronized
    fun tick(deviceId: String? = null): String {
        val dev = normalizeDeviceId(deviceId ?: defaultDeviceId)
        val now = nowMs()

        if (now > lastPhys) {
            lastPhys = now
            counter = 0
        } else {
            counter++
            if (counter > MAX_COUNTER) {
                lastPhys++
                counter = 0
            }
        }

        return format(lastPhys, counter, dev)
    }

    @Synchronized
    fun receive(remoteHlc: String?, deviceId: String? = null): String {
        val dev = normalizeDeviceId(deviceId ?: defaultDeviceId)
        val now = nowMs()
        val parsed = tryParse(remoteHlc)

        if (parsed == null) {
            if (now > lastPhys) {
                lastPhys = now
                counter = 0
            } else {
                counter++
                if (counter > MAX_COUNTER) {
                    lastPhys++
                    counter = 0
                }
            }
            return format(lastPhys, counter, dev)
        }

        val (rPhys, rCnt) = parsed
        val maxPhys = maxOf(now, lastPhys, rPhys)
        when {
            maxPhys == lastPhys && maxPhys == rPhys -> {
                counter = maxOf(counter, rCnt) + 1
            }
            maxPhys == lastPhys -> {
                counter++
            }
            maxPhys == rPhys -> {
                lastPhys = maxPhys
                counter = rCnt + 1
            }
            else -> {
                lastPhys = now
                counter = 0
            }
        }

        if (counter > MAX_COUNTER) {
            lastPhys++
            counter = 0
        }

        return format(lastPhys, counter, dev)
    }

    /** 仅在加载本地或远端状态时推进时钟上限，不生成新事件字符串。 */
    @Synchronized
    fun observe(hlc: String?) {
        val (rPhys, rCnt) = tryParse(hlc) ?: return
        if (rPhys > lastPhys) {
            lastPhys = rPhys
            counter = rCnt
        } else if (rPhys == lastPhys && rCnt > counter) {
            counter = rCnt
        }
    }

    data class ParsedHlc(val phys: Long, val counter: Int)

    companion object {
        const val DEFAULT_DEVICE_ID = "android-local"
        const val MAX_COUNTER = 0xFFFF

        fun compare(a: String?, b: String?): Int {
            if (a.isNullOrEmpty() && b.isNullOrEmpty()) return 0
            if (a.isNullOrEmpty()) return -1
            if (b.isNullOrEmpty()) return 1
            return a.compareTo(b)
        }

        fun max(a: String?, b: String?): String =
            if (compare(a, b) >= 0) (a ?: "") else (b ?: "")

        fun format(phys: Long, counter: Int, deviceId: String): String =
            "%012x-%04x-%s".format(Locale.ROOT, phys, counter, deviceId)

        fun tryParse(hlc: String?): ParsedHlc? {
            if (hlc.isNullOrBlank()) return null
            val firstDash = hlc.indexOf('-')
            if (firstDash <= 0) return null
            val secondDash = hlc.indexOf('-', firstDash + 1)
            if (secondDash <= firstDash + 1) return null

            val physPart = hlc.substring(0, firstDash)
            val cntPart = hlc.substring(firstDash + 1, secondDash)
            val phys = physPart.toLongOrNull(16) ?: return null
            val cnt = cntPart.toIntOrNull(16) ?: return null
            return ParsedHlc(phys, maxOf(0, cnt))
        }

        private fun normalizeDeviceId(deviceId: String?): String {
            val raw = (deviceId ?: "").trim()
            return if (raw.isEmpty()) DEFAULT_DEVICE_ID else raw
        }
    }
}
