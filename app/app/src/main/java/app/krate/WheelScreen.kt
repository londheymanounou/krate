package app.krate

import androidx.compose.animation.core.Animatable
import androidx.compose.animation.core.FastOutSlowInEasing
import androidx.compose.animation.core.tween
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.Path
import androidx.compose.ui.graphics.drawscope.rotate
import androidx.compose.ui.graphics.nativeCanvas
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import kotlin.math.cos
import kotlin.math.sin

/**
 * Wheel of fortune — a visual front-end for the core's `Pick`.
 *
 * **The core chooses the winner; the animation is told where to stop.** Spinning to a
 * Kotlin-random angle and reading off whatever landed would use a different (weaker) generator than
 * every other random tool in the app, and would disagree with the desktop. So `Pick` runs first and
 * the wheel is animated onto that result.
 *
 * Consequence worth knowing: with duplicate entries the core returns the *text*, so the wheel lands
 * on the first segment with that text. The outcome is still correct, the segment may not be the one
 * a purist expected.
 */
@Composable
fun WheelScreen(modifier: Modifier = Modifier) {
    var raw by remember { mutableStateOf("") }
    var winner by remember { mutableStateOf("") }
    var spinning by remember { mutableStateOf(false) }
    val rotation = remember { Animatable(0f) }
    val scope = rememberCoroutineScope()

    val items = remember(raw) {
        raw.split(',', '\n').map { it.trim() }.filter { it.isNotEmpty() }
    }

    // Material You palette cycled around the wheel, so it themes with the wallpaper like everything
    // else rather than using fixed carnival colours.
    val palette = listOf(
        MaterialTheme.colorScheme.primaryContainer,
        MaterialTheme.colorScheme.tertiaryContainer,
        MaterialTheme.colorScheme.secondaryContainer,
        MaterialTheme.colorScheme.surfaceVariant,
    )
    val onPalette = listOf(
        MaterialTheme.colorScheme.onPrimaryContainer,
        MaterialTheme.colorScheme.onTertiaryContainer,
        MaterialTheme.colorScheme.onSecondaryContainer,
        MaterialTheme.colorScheme.onSurfaceVariant,
    )
    val pointerColor = MaterialTheme.colorScheme.primary

    fun spin() {
        if (spinning || items.size < 2) return
        scope.launch {
            spinning = true
            winner = ""
            val res = withContext(Dispatchers.IO) { Core.run("Pick", items.joinToString("\n")) }
            if (!res.ok) { spinning = false; return@launch }
            val index = items.indexOf(res.text).takeIf { it >= 0 } ?: 0

            // Segment i spans [i*seg, (i+1)*seg) measured clockwise from 3 o'clock, which is where
            // drawArc's zero angle sits. The pointer is at 12 o'clock = -90 degrees, so the wheel
            // must turn until segment i's centre lands there — plus whole extra turns for the spin.
            val seg = 360f / items.size
            val target = -90f - (index * seg + seg / 2f)
            val turns = 5
            val current = rotation.value
            val normalized = ((target - current) % 360f + 360f) % 360f
            rotation.animateTo(
                targetValue = current + turns * 360f + normalized,
                animationSpec = tween(2600, easing = FastOutSlowInEasing),
            )
            winner = res.text
            spinning = false
        }
    }

    Column(
        modifier.fillMaxSize().padding(20.dp),
        verticalArrangement = Arrangement.spacedBy(16.dp),
        horizontalAlignment = Alignment.CenterHorizontally,
    ) {
        Box(
            Modifier.fillMaxWidth().weight(1f),
            contentAlignment = Alignment.Center,
        ) {
            if (items.size < 2) {
                Text(
                    "Add at least two entries below",
                    style = MaterialTheme.typography.bodyLarge,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                    textAlign = TextAlign.Center,
                )
            } else {
                Canvas(Modifier.fillMaxWidth().aspectRatio(1f)) {
                    val d = size.minDimension
                    val r = d / 2f
                    val topLeft = Offset((size.width - d) / 2f, (size.height - d) / 2f)
                    val seg = 360f / items.size

                    rotate(rotation.value, pivot = Offset(size.width / 2f, size.height / 2f)) {
                        items.forEachIndexed { i, label ->
                            drawArc(
                                color = palette[i % palette.size],
                                startAngle = i * seg,
                                sweepAngle = seg,
                                useCenter = true,
                                topLeft = topLeft,
                                size = Size(d, d),
                            )
                            // Label along the segment's bisector, rotated to sit radially.
                            val mid = Math.toRadians((i * seg + seg / 2f).toDouble())
                            val cx = size.width / 2f + cos(mid).toFloat() * r * 0.62f
                            val cy = size.height / 2f + sin(mid).toFloat() * r * 0.62f
                            drawContext.canvas.nativeCanvas.apply {
                                val paint = android.graphics.Paint().apply {
                                    color = onPalette[i % onPalette.size].value.toLong().let {
                                        android.graphics.Color.argb(
                                            (onPalette[i % onPalette.size].alpha * 255).toInt(),
                                            (onPalette[i % onPalette.size].red * 255).toInt(),
                                            (onPalette[i % onPalette.size].green * 255).toInt(),
                                            (onPalette[i % onPalette.size].blue * 255).toInt(),
                                        )
                                    }
                                    textSize = (r * 0.11f).coerceAtMost(44f)
                                    textAlign = android.graphics.Paint.Align.CENTER
                                    isAntiAlias = true
                                }
                                save()
                                // Only the segment angle here: this canvas is already inside the
                                // wheel's rotate() scope, so adding rotation.value again applies
                                // the spin twice and the labels drift off their own segments.
                                val bisector = i * seg + seg / 2f
                                // Flip anything on the left half so it is never upside down.
                                val spun = ((bisector + rotation.value) % 360f + 360f) % 360f
                                val flip = if (spun > 90f && spun < 270f) 180f else 0f
                                rotate(bisector + flip, size.width / 2f, size.height / 2f)
                                drawText(
                                    label.take(12),
                                    size.width / 2f + (if (flip == 0f) r * 0.62f else -r * 0.62f),
                                    size.height / 2f + paint.textSize / 3f,
                                    paint,
                                )
                                restore()
                            }
                        }
                    }

                    // Pointer at 12 o'clock, outside the rotation so it stays put.
                    val cx = size.width / 2f
                    val top = topLeft.y
                    drawPath(
                        Path().apply {
                            moveTo(cx, top + r * 0.16f)
                            lineTo(cx - r * 0.075f, top - r * 0.02f)
                            lineTo(cx + r * 0.075f, top - r * 0.02f)
                            close()
                        },
                        color = pointerColor,
                    )
                }
            }
        }

        Text(
            winner.ifEmpty { if (spinning) "Spinning…" else " " },
            style = MaterialTheme.typography.headlineSmall,
            fontWeight = FontWeight.Bold,
            color = MaterialTheme.colorScheme.primary,
            maxLines = 1,
        )

        Button(
            onClick = { spin() },
            enabled = items.size >= 2 && !spinning,
            modifier = Modifier.fillMaxWidth().height(56.dp),
            shape = RoundedCornerShape(28.dp),
        ) { Text("Spin", style = MaterialTheme.typography.titleMedium) }

        OutlinedTextField(
            value = raw,
            onValueChange = { raw = it },
            label = { Text("Entries, one per line or comma-separated") },
            modifier = Modifier.fillMaxWidth().height(120.dp),
            shape = RoundedCornerShape(24.dp),
        )
    }
}
