package com.daynew.app.data

import android.content.Context

/**
 * 轻量 UI 偏好存储（SharedPreferences）：不经过 JSON/Keystore，确保冷启动首帧零阻塞。
 */
class UiPreferencesStore(context: Context) {
    private val prefs = context.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE)

    fun getDarkThemeOverride(): Boolean? = when (prefs.getString(KEY_THEME_MODE, MODE_SYSTEM)) {
        MODE_DARK -> true
        MODE_LIGHT -> false
        else -> null
    }

    fun setDarkThemeOverride(isDark: Boolean?) {
        val value = when (isDark) {
            true -> MODE_DARK
            false -> MODE_LIGHT
            null -> MODE_SYSTEM
        }
        prefs.edit().putString(KEY_THEME_MODE, value).apply()
    }

    companion object {
        private const val PREFS_NAME = "daynew_ui_prefs"
        private const val KEY_THEME_MODE = "theme_mode"
        private const val MODE_SYSTEM = "system"
        private const val MODE_DARK = "dark"
        private const val MODE_LIGHT = "light"
    }
}
