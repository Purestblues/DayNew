package com.daynew.app.ui.theme

import androidx.compose.animation.core.CubicBezierEasing
import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.darkColorScheme
import androidx.compose.material3.lightColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.runtime.Immutable
import androidx.compose.runtime.staticCompositionLocalOf
import androidx.compose.ui.graphics.Color

@Immutable
data class NordFrostPalette(
    val surface0: Color,
    val surface1: Color,
    val surface2: Color,
    val surface3: Color,
    val surface4: Color,
    val borderSubtle: Color,
    val borderStrong: Color,
    val borderPopup: Color,
    val accent: Color,
    val accentTint: Color,
    val onAccent: Color,
    val selectionPill: Color,
    val textPrimary: Color,
    val textSecondary: Color,
    val textDisabled: Color,
    val success: Color,
    val warning: Color,
    val danger: Color,
)

val LightNordPalette = NordFrostPalette(
    surface0 = Color(0xFFECEFF4),
    surface1 = Color(0xFFE5E9F0),
    surface2 = Color(0xFFFFFFFF),
    surface3 = Color(0xFFF2F4F8),
    surface4 = Color(0xFFFFFFFF),
    borderSubtle = Color(0xFFD8DEE9),
    borderStrong = Color(0xFFC2CAD8),
    borderPopup = Color(0xFFCFD6E4),
    accent = Color(0xFF527CE6),
    accentTint = Color(0x24527CE6),
    onAccent = Color(0xFFFFFFFF),
    selectionPill = Color(0xFFD2DEF5),
    textPrimary = Color(0xFF2E3440),
    textSecondary = Color(0xFF646E82),
    textDisabled = Color(0xFF9AA3B5),
    success = Color(0xFF5C9A64),
    warning = Color(0xFFC98A3E),
    danger = Color(0xFFBF616A),
)

val DarkNordPalette = NordFrostPalette(
    surface0 = Color(0xFF181A20),
    surface1 = Color(0xFF13151A),
    surface2 = Color(0xFF21252E),
    surface3 = Color(0xFF282D38),
    surface4 = Color(0xFF2E3440),
    borderSubtle = Color(0xFF2A303C),
    borderStrong = Color(0xFF3B4252),
    borderPopup = Color(0xFF3E4656),
    accent = Color(0xFF78A0F0),
    accentTint = Color(0x2678A0F0),
    onAccent = Color(0xFF0F141C),
    selectionPill = Color(0xFF2A3856),
    textPrimary = Color(0xFFECEFF4),
    textSecondary = Color(0xFFA5B0C2),
    textDisabled = Color(0xFF687285),
    success = Color(0xFFA3BE8C),
    warning = Color(0xFFEBCB8B),
    danger = Color(0xFFD6747D),
)

object NordMotion {
    val Standard = CubicBezierEasing(0.4f, 0.0f, 0.2f, 1.0f)
    val Enter = CubicBezierEasing(0.0f, 0.0f, 0.2f, 1.0f)
    val Leave = CubicBezierEasing(0.4f, 0.0f, 1.0f, 1.0f)
    const val Xs = 90
    const val S = 150
    const val M = 225
}

val LocalNordPalette = staticCompositionLocalOf { LightNordPalette }

@Composable
fun NordFrostTheme(
    darkTheme: Boolean = isSystemInDarkTheme(),
    content: @Composable () -> Unit,
) {
    val palette = if (darkTheme) DarkNordPalette else LightNordPalette
    val colorScheme = if (darkTheme) {
        darkColorScheme(
            background = palette.surface0,
            surface = palette.surface2,
            surfaceVariant = palette.surface3,
            primary = palette.accent,
            onPrimary = palette.onAccent,
            primaryContainer = palette.selectionPill,
            outline = palette.borderStrong,
            outlineVariant = palette.borderSubtle,
            onBackground = palette.textPrimary,
            onSurface = palette.textPrimary,
            onSurfaceVariant = palette.textSecondary,
            error = palette.danger,
        )
    } else {
        lightColorScheme(
            background = palette.surface0,
            surface = palette.surface2,
            surfaceVariant = palette.surface3,
            primary = palette.accent,
            onPrimary = palette.onAccent,
            primaryContainer = palette.selectionPill,
            outline = palette.borderStrong,
            outlineVariant = palette.borderSubtle,
            onBackground = palette.textPrimary,
            onSurface = palette.textPrimary,
            onSurfaceVariant = palette.textSecondary,
            error = palette.danger,
        )
    }

    CompositionLocalProvider(LocalNordPalette provides palette) {
        MaterialTheme(
            colorScheme = colorScheme,
            typography = Typography,
            content = content,
        )
    }
}
