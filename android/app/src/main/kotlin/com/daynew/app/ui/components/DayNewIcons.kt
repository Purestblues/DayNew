package com.daynew.app.ui.components

import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.PathFillType
import androidx.compose.ui.graphics.SolidColor
import androidx.compose.ui.graphics.StrokeCap
import androidx.compose.ui.graphics.StrokeJoin
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.graphics.vector.path
import androidx.compose.ui.unit.dp

/**
 * 轻量内置线框图标集（1.75dp 描边，免引入庞大的 material-icons-extended）。
 */
object DayNewIcons {
    private val StrokeBrush = SolidColor(Color.Black)

    val Overview: ImageVector by lazy {
        vector("Overview") {
            roundRect(3f, 4f, 7f, 16f)
            roundRect(14f, 4f, 7f, 10f)
        }
    }

    val Timeline: ImageVector by lazy {
        vector("Timeline") {
            path(
                stroke = StrokeBrush,
                strokeLineWidth = 1.8f,
                strokeLineCap = StrokeCap.Round,
                strokeLineJoin = StrokeJoin.Round,
            ) {
                moveTo(12f, 21f)
                arcTo(9f, 9f, 0f, true, true, 12f, 3f)
                arcTo(9f, 9f, 0f, true, true, 12f, 21f)
                close()
                moveTo(12f, 7f)
                lineTo(12f, 12f)
                lineTo(15.5f, 14f)
            }
        }
    }

    val Goals: ImageVector by lazy {
        vector("Goals") {
            path(
                stroke = StrokeBrush,
                strokeLineWidth = 1.8f,
                strokeLineCap = StrokeCap.Round,
                strokeLineJoin = StrokeJoin.Round,
            ) {
                moveTo(12f, 21f)
                arcTo(9f, 9f, 0f, true, true, 12f, 3f)
                arcTo(9f, 9f, 0f, true, true, 12f, 21f)
                close()
                moveTo(12f, 16.5f)
                arcTo(4.5f, 4.5f, 0f, true, true, 12f, 7.5f)
                arcTo(4.5f, 4.5f, 0f, true, true, 12f, 16.5f)
                close()
                moveTo(12f, 12.8f)
                arcTo(0.8f, 0.8f, 0f, true, true, 12f, 11.2f)
                arcTo(0.8f, 0.8f, 0f, true, true, 12f, 12.8f)
                close()
            }
        }
    }

    val More: ImageVector by lazy {
        vector("More") {
            path(
                fill = StrokeBrush,
                pathFillType = PathFillType.NonZero,
            ) {
                moveTo(6f, 12f)
                arcTo(1.5f, 1.5f, 0f, true, true, 3f, 12f)
                arcTo(1.5f, 1.5f, 0f, true, true, 6f, 12f)
                close()
                moveTo(13.5f, 12f)
                arcTo(1.5f, 1.5f, 0f, true, true, 10.5f, 12f)
                arcTo(1.5f, 1.5f, 0f, true, true, 13.5f, 12f)
                close()
                moveTo(21f, 12f)
                arcTo(1.5f, 1.5f, 0f, true, true, 18f, 12f)
                arcTo(1.5f, 1.5f, 0f, true, true, 21f, 12f)
                close()
            }
        }
    }

    val Add: ImageVector by lazy {
        vector("Add") {
            path(
                stroke = StrokeBrush,
                strokeLineWidth = 2.0f,
                strokeLineCap = StrokeCap.Round,
            ) {
                moveTo(12f, 5f)
                lineTo(12f, 19f)
                moveTo(5f, 12f)
                lineTo(19f, 12f)
            }
        }
    }

    val Check: ImageVector by lazy {
        vector("Check") {
            path(
                stroke = StrokeBrush,
                strokeLineWidth = 2.2f,
                strokeLineCap = StrokeCap.Round,
                strokeLineJoin = StrokeJoin.Round,
            ) {
                moveTo(5f, 12.5f)
                lineTo(10f, 17f)
                lineTo(19f, 7f)
            }
        }
    }

    val Sun: ImageVector by lazy {
        vector("Sun") {
            path(
                stroke = StrokeBrush,
                strokeLineWidth = 1.8f,
                strokeLineCap = StrokeCap.Round,
            ) {
                moveTo(12f, 16f)
                arcTo(4f, 4f, 0f, true, true, 12f, 8f)
                arcTo(4f, 4f, 0f, true, true, 12f, 16f)
                close()
                moveTo(12f, 2f)
                lineTo(12f, 4f)
                moveTo(12f, 20f)
                lineTo(12f, 22f)
                moveTo(4f, 12f)
                lineTo(2f, 12f)
                moveTo(22f, 12f)
                lineTo(20f, 12f)
                moveTo(5.6f, 5.6f)
                lineTo(7f, 7f)
                moveTo(17f, 17f)
                lineTo(18.4f, 18.4f)
                moveTo(18.4f, 5.6f)
                lineTo(17f, 7f)
                moveTo(7f, 17f)
                lineTo(5.6f, 18.4f)
            }
        }
    }

    val Moon: ImageVector by lazy {
        vector("Moon") {
            path(
                stroke = StrokeBrush,
                strokeLineWidth = 1.8f,
                strokeLineCap = StrokeCap.Round,
                strokeLineJoin = StrokeJoin.Round,
            ) {
                moveTo(20f, 14.5f)
                arcTo(8f, 8f, 0f, true, true, 9.5f, 4f)
                arcTo(6.5f, 6.5f, 0f, false, false, 20f, 14.5f)
                close()
            }
        }
    }

    val Delete: ImageVector by lazy {
        vector("Delete") {
            path(
                stroke = StrokeBrush,
                strokeLineWidth = 1.8f,
                strokeLineCap = StrokeCap.Round,
                strokeLineJoin = StrokeJoin.Round,
            ) {
                moveTo(4f, 7f)
                lineTo(20f, 7f)
                moveTo(9f, 7f)
                lineTo(9f, 4f)
                lineTo(15f, 4f)
                lineTo(15f, 7f)
                moveTo(6f, 7f)
                lineTo(7f, 20f)
                lineTo(17f, 20f)
                lineTo(18f, 7f)
            }
        }
    }

    val Catalog: ImageVector by lazy {
        vector("Catalog") {
            roundRect(3.5f, 3.5f, 7f, 7f)
            roundRect(13.5f, 3.5f, 7f, 7f)
            roundRect(3.5f, 13.5f, 7f, 7f)
            roundRect(13.5f, 13.5f, 7f, 7f)
        }
    }

    val ChevronLeft: ImageVector by lazy {
        vector("ChevronLeft") {
            path(
                stroke = StrokeBrush,
                strokeLineWidth = 2.0f,
                strokeLineCap = StrokeCap.Round,
                strokeLineJoin = StrokeJoin.Round,
            ) {
                moveTo(15f, 5f)
                lineTo(8f, 12f)
                lineTo(15f, 19f)
            }
        }
    }

    val ChevronRight: ImageVector by lazy {
        vector("ChevronRight") {
            path(
                stroke = StrokeBrush,
                strokeLineWidth = 2.0f,
                strokeLineCap = StrokeCap.Round,
                strokeLineJoin = StrokeJoin.Round,
            ) {
                moveTo(9f, 5f)
                lineTo(16f, 12f)
                lineTo(9f, 19f)
            }
        }
    }

    val ChevronDown: ImageVector by lazy {
        vector("ChevronDown") {
            path(
                stroke = StrokeBrush,
                strokeLineWidth = 2.0f,
                strokeLineCap = StrokeCap.Round,
                strokeLineJoin = StrokeJoin.Round,
            ) {
                moveTo(6f, 9f)
                lineTo(12f, 15f)
                lineTo(18f, 9f)
            }
        }
    }

    val Edit: ImageVector by lazy {
        vector("Edit") {
            path(
                stroke = StrokeBrush,
                strokeLineWidth = 1.8f,
                strokeLineCap = StrokeCap.Round,
                strokeLineJoin = StrokeJoin.Round,
            ) {
                moveTo(12f, 20f)
                lineTo(20f, 20f)
                moveTo(16.5f, 3.5f)
                lineTo(20.5f, 7.5f)
                lineTo(8f, 20f)
                lineTo(4f, 20f)
                lineTo(4f, 16f)
                close()
            }
        }
    }

    val Tag: ImageVector by lazy {
        vector("Tag") {
            path(
                stroke = StrokeBrush,
                strokeLineWidth = 1.8f,
                strokeLineCap = StrokeCap.Round,
                strokeLineJoin = StrokeJoin.Round,
            ) {
                moveTo(3f, 11f)
                lineTo(3f, 4f)
                lineTo(10f, 4f)
                lineTo(20f, 14f)
                lineTo(13f, 21f)
                close()
                moveTo(7.5f, 8.5f)
                arcTo(1f, 1f, 0f, true, true, 7.5f, 6.5f)
                arcTo(1f, 1f, 0f, true, true, 7.5f, 8.5f)
            }
        }
    }

    private inline fun vector(
        name: String,
        block: ImageVector.Builder.() -> Unit,
    ): ImageVector = ImageVector.Builder(
        name = name,
        defaultWidth = 24.dp,
        defaultHeight = 24.dp,
        viewportWidth = 24f,
        viewportHeight = 24f,
    ).apply(block).build()

    private fun ImageVector.Builder.roundRect(x: Float, y: Float, w: Float, h: Float) {
        path(
            stroke = StrokeBrush,
            strokeLineWidth = 1.8f,
            strokeLineCap = StrokeCap.Round,
            strokeLineJoin = StrokeJoin.Round,
        ) {
            moveTo(x + 1.5f, y)
            lineTo(x + w - 1.5f, y)
            quadTo(x + w, y, x + w, y + 1.5f)
            lineTo(x + w, y + h - 1.5f)
            quadTo(x + w, y + h, x + w - 1.5f, y + h)
            lineTo(x + 1.5f, y + h)
            quadTo(x, y + h, x, y + h - 1.5f)
            lineTo(x, y + 1.5f)
            quadTo(x, y, x + 1.5f, y)
            close()
        }
    }
}
