package com.daynew.app.ui.theme

import android.os.Build
import android.view.HapticFeedbackConstants
import android.view.View
import androidx.compose.runtime.Composable
import androidx.compose.runtime.remember
import androidx.compose.ui.platform.LocalView

/**
 * 统一分级震动/触感反馈：遵循系统触感开关设置，按语义强度区分轻触、确认、长按、吸附与警示。
 */
class DayNewHaptics(private val view: View) {
    fun tap() {
        val code = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.LOLLIPOP) {
            HapticFeedbackConstants.CLOCK_TICK
        } else {
            HapticFeedbackConstants.KEYBOARD_TAP
        }
        view.performHapticFeedback(code)
    }

    fun confirm() {
        val code = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) {
            HapticFeedbackConstants.CONFIRM
        } else {
            HapticFeedbackConstants.CONTEXT_CLICK
        }
        view.performHapticFeedback(code)
    }

    fun longPress() {
        view.performHapticFeedback(HapticFeedbackConstants.LONG_PRESS)
    }

    fun tick() {
        val code = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.UPSIDE_DOWN_CAKE) {
            HapticFeedbackConstants.SEGMENT_FREQUENT_TICK
        } else {
            HapticFeedbackConstants.TEXT_HANDLE_MOVE
        }
        view.performHapticFeedback(code)
    }

    fun reject() {
        val code = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) {
            HapticFeedbackConstants.REJECT
        } else {
            HapticFeedbackConstants.LONG_PRESS
        }
        view.performHapticFeedback(code)
    }
}

@Composable
fun rememberDayNewHaptics(): DayNewHaptics {
    val view = LocalView.current
    return remember(view) { DayNewHaptics(view) }
}
