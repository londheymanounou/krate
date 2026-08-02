package app.krate

import androidx.compose.foundation.Canvas
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.gestures.detectDragGestures
import androidx.compose.foundation.gestures.detectTapGestures
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.unit.dp

/**
 * A saturation/value square with a hue strip — the picker every design tool uses.
 *
 * Hex stays editable underneath rather than being replaced: pasting `#3366ff` from somewhere else
 * is often faster than hunting for it, and the picker is for when you do not already know the code.
 * HSV rather than RGB sliders because "make this lighter" is one axis in HSV and three in RGB.
 */
private fun hsvToColor(h: Float, s: Float, v: Float): Color {
    val c = v * s
    val x = c * (1f - kotlin.math.abs((h / 60f) % 2f - 1f))
    val m = v - c
    val (r, g, b) = when {
        h < 60f -> Triple(c, x, 0f)
        h < 120f -> Triple(x, c, 0f)
        h < 180f -> Triple(0f, c, x)
        h < 240f -> Triple(0f, x, c)
        h < 300f -> Triple(x, 0f, c)
        else -> Triple(c, 0f, x)
    }
    return Color(r + m, g + m, b + m)
}

private fun Color.toHex(): String =
    "#%02x%02x%02x".format((red * 255).toInt(), (green * 255).toInt(), (blue * 255).toInt())

private fun parseHex(text: String): Triple<Float, Float, Float>? {
    val hex = text.trim().removePrefix("#")
    if (hex.length != 6) return null
    val value = hex.toLongOrNull(16) ?: return null
    val r = ((value shr 16) and 0xff) / 255f
    val g = ((value shr 8) and 0xff) / 255f
    val b = (value and 0xff) / 255f
    val max = maxOf(r, g, b)
    val min = minOf(r, g, b)
    val d = max - min
    val h = when {
        d == 0f -> 0f
        max == r -> (60f * (((g - b) / d) % 6f) + 360f) % 360f
        max == g -> 60f * ((b - r) / d + 2f)
        else -> 60f * ((r - g) / d + 4f)
    }
    return Triple(h, if (max == 0f) 0f else d / max, max)
}

/** A colour field that opens a real picker, with hex still editable by hand. */
@Composable
fun ColourPickerField(
    label: String,
    value: String,
    onValueChange: (String) -> Unit,
    modifier: Modifier = Modifier,
) {
    var open by remember { mutableStateOf(false) }
    val swatch = remember(value) {
        parseHex(value)?.let { (h, s, v) -> hsvToColor(h, s, v) }
    }

    OutlinedTextField(
        value = value,
        onValueChange = onValueChange,
        label = { Text(label) },
        singleLine = true,
        shape = RoundedCornerShape(24.dp),
        modifier = modifier,
        textStyle = MaterialTheme.typography.bodyLarge.copy(fontFamily = FontFamily.Monospace),
        leadingIcon = {
            Box(
                Modifier
                    .padding(start = 4.dp)
                    .size(28.dp)
                    .clip(CircleShape)
                    .background(swatch ?: MaterialTheme.colorScheme.surfaceContainerHighest)
                    .clickable { open = true },
            )
        },
    )

    if (open) {
        val start = parseHex(value) ?: Triple(210f, 0.8f, 1f)
        var hue by remember { mutableFloatStateOf(start.first) }
        var sat by remember { mutableFloatStateOf(start.second) }
        var bright by remember { mutableFloatStateOf(start.third) }
        val picked = hsvToColor(hue, sat, bright)

        AlertDialog(
            onDismissRequest = { open = false },
            confirmButton = {
                TextButton(onClick = { onValueChange(picked.toHex()); open = false }) { Text("Select") }
            },
            dismissButton = { TextButton(onClick = { open = false }) { Text("Cancel") } },
            title = { Text(label) },
            text = {
                Column(verticalArrangement = Arrangement.spacedBy(14.dp)) {
                    // Saturation across, brightness down — the standard square.
                    Box(
                        Modifier
                            .fillMaxWidth()
                            .height(180.dp)
                            .clip(RoundedCornerShape(16.dp))
                            .pointerInput(hue) {
                                fun set(p: Offset) {
                                    sat = (p.x / size.width).coerceIn(0f, 1f)
                                    bright = 1f - (p.y / size.height).coerceIn(0f, 1f)
                                }
                                detectTapGestures { set(it) }
                            }
                            .pointerInput(hue) {
                                detectDragGestures { change, _ ->
                                    sat = (change.position.x / size.width).coerceIn(0f, 1f)
                                    bright = 1f - (change.position.y / size.height).coerceIn(0f, 1f)
                                }
                            },
                    ) {
                        Canvas(Modifier.fillMaxSize()) {
                            drawRect(
                                Brush.horizontalGradient(
                                    listOf(Color.White, hsvToColor(hue, 1f, 1f))
                                )
                            )
                            drawRect(Brush.verticalGradient(listOf(Color.Transparent, Color.Black)))
                            drawCircle(
                                color = Color.White,
                                radius = 14f,
                                center = Offset(sat * size.width, (1f - bright) * size.height),
                                style = androidx.compose.ui.graphics.drawscope.Stroke(width = 5f),
                            )
                        }
                    }

                    Box(
                        Modifier
                            .fillMaxWidth()
                            .height(28.dp)
                            .clip(CircleShape)
                            .pointerInput(Unit) {
                                detectTapGestures { hue = (it.x / size.width).coerceIn(0f, 1f) * 360f }
                            }
                            .pointerInput(Unit) {
                                detectDragGestures { change, _ ->
                                    hue = (change.position.x / size.width).coerceIn(0f, 1f) * 360f
                                }
                            },
                    ) {
                        Canvas(Modifier.fillMaxSize()) {
                            drawRect(
                                Brush.horizontalGradient(
                                    (0..6).map { hsvToColor(it * 60f, 1f, 1f) }
                                )
                            )
                            drawCircle(
                                color = Color.White,
                                radius = size.height / 2f - 3f,
                                center = Offset((hue / 360f) * size.width, size.height / 2f),
                                style = androidx.compose.ui.graphics.drawscope.Stroke(width = 5f),
                            )
                        }
                    }

                    Row(verticalAlignment = Alignment.CenterVertically) {
                        Box(Modifier.size(40.dp).clip(CircleShape).background(picked))
                        Spacer(Modifier.width(12.dp))
                        Text(picked.toHex(), fontFamily = FontFamily.Monospace)
                    }
                }
            },
        )
    }
}
