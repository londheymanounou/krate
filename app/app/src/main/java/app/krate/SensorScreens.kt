package app.krate

import android.content.Context
import androidx.compose.animation.core.animateFloat
import androidx.compose.ui.graphics.graphicsLayer
import android.hardware.Sensor
import android.hardware.SensorEvent
import android.hardware.SensorEventListener
import android.hardware.SensorManager
import android.util.DisplayMetrics
import androidx.compose.animation.core.animateFloatAsState
import androidx.compose.animation.core.spring
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.drawscope.rotate
import androidx.compose.ui.graphics.nativeCanvas
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import kotlin.math.abs
import kotlin.math.roundToInt
import kotlin.math.sqrt

/*
 * Sensor tools. These have no Rust counterpart — the core is pure computation and never touches
 * hardware — so, like Snake, they are the Android shell's own. Nothing here duplicates core logic.
 */

/**
 * Subscribes to a sensor for as long as the composable is on screen.
 *
 * The listener is unregistered in `onDispose`, which is the whole point: a leaked
 * `SensorEventListener` keeps the sensor powered and is one of the classic ways an app quietly
 * drains a battery. `SENSOR_DELAY_UI` rather than `_FASTEST` for the same reason — nothing here is
 * displayed faster than the eye can read.
 */
@Composable
fun rememberSensor(type: Int): FloatArray? {
    val context = LocalContext.current
    var values by remember { mutableStateOf<FloatArray?>(null) }

    DisposableEffect(type) {
        val manager = context.getSystemService(Context.SENSOR_SERVICE) as SensorManager
        val sensor = manager.getDefaultSensor(type)
        val listener = object : SensorEventListener {
            override fun onSensorChanged(event: SensorEvent) {
                values = event.values.copyOf()
            }
            override fun onAccuracyChanged(sensor: Sensor?, accuracy: Int) = Unit
        }
        if (sensor != null) {
            manager.registerListener(listener, sensor, SensorManager.SENSOR_DELAY_UI)
        }
        onDispose { manager.unregisterListener(listener) }
    }
    return values
}

@Composable
fun MissingSensor(name: String, modifier: Modifier = Modifier) {
    Box(modifier.fillMaxSize().padding(32.dp), contentAlignment = Alignment.Center) {
        Text(
            "This device has no $name.",
            style = MaterialTheme.typography.bodyLarge,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
            textAlign = TextAlign.Center,
        )
    }
}

/** One labelled axis readout. */
@Composable
private fun AxisRow(label: String, value: Float, unit: String, range: Float) {
    val progress by animateFloatAsState(
        targetValue = (abs(value) / range).coerceIn(0f, 1f),
        animationSpec = spring(stiffness = 300f),
        label = "progress"
    )
    Column(Modifier.fillMaxWidth().padding(vertical = 6.dp)) {
        Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween, verticalAlignment = Alignment.CenterVertically) {
            Text(label, style = MaterialTheme.typography.titleMedium, fontWeight = FontWeight.Bold)
            Text(
                String.format("%+.2f %s", value, unit),
                style = MaterialTheme.typography.titleMedium,
                fontFamily = FontFamily.Monospace,
                color = MaterialTheme.colorScheme.primary,
            )
        }
        Spacer(Modifier.height(8.dp))
        LinearProgressIndicator(
            progress = { progress },
            modifier = Modifier.fillMaxWidth().height(8.dp).clip(RoundedCornerShape(4.dp)),
            color = MaterialTheme.colorScheme.primary,
            trackColor = MaterialTheme.colorScheme.surfaceVariant
        )
    }
}

@Composable
private fun AxisScreen(
    type: Int,
    name: String,
    unit: String,
    range: Float,
    modifier: Modifier = Modifier,
) {
    val v = rememberSensor(type) ?: return MissingSensor(name, modifier)
    val magnitude = sqrt(v[0] * v[0] + v[1] * v[1] + v[2] * v[2])

    Column(
        modifier.fillMaxSize().padding(24.dp),
        verticalArrangement = Arrangement.spacedBy(12.dp),
    ) {
        Surface(
            shape = RoundedCornerShape(28.dp),
            color = MaterialTheme.colorScheme.primaryContainer,
            modifier = Modifier.fillMaxWidth(),
        ) {
            Column(Modifier.padding(24.dp), horizontalAlignment = Alignment.CenterHorizontally) {
                Text(
                    String.format("%.2f", magnitude),
                    fontSize = 56.sp,
                    fontWeight = FontWeight.Medium,
                    color = MaterialTheme.colorScheme.onPrimaryContainer,
                )
                Text(
                    "magnitude ($unit)",
                    style = MaterialTheme.typography.bodyMedium,
                    color = MaterialTheme.colorScheme.onPrimaryContainer,
                )
            }
        }
        AxisRow("X", v[0], unit, range)
        AxisRow("Y", v[1], unit, range)
        AxisRow("Z", v[2], unit, range)
    }
}

/** m/s^2, so roughly ±20 covers gravity plus a decent shake. */
@Composable
fun AccelerometerScreen(modifier: Modifier = Modifier) =
    AxisScreen(Sensor.TYPE_ACCELEROMETER, "accelerometer", "m/s²", 20f, modifier)

/** rad/s. A brisk twist is a few rad/s. */
@Composable
fun GyroscopeScreen(modifier: Modifier = Modifier) =
    AxisScreen(Sensor.TYPE_GYROSCOPE, "gyroscope", "rad/s", 10f, modifier)

/** microtesla. Earth's field is ~25–65 µT, so 100 leaves room for nearby metal. */
@Composable
fun MagnetometerScreen(modifier: Modifier = Modifier) =
    AxisScreen(Sensor.TYPE_MAGNETIC_FIELD, "magnetometer", "µT", 100f, modifier)

/**
 * Compass.
 *
 * Heading comes from the rotation vector rather than accelerometer+magnetometer fusion done by
 * hand: `getRotationMatrixFromVector` already applies the device's own calibration, and rolling
 * that fusion manually is how compasses end up drifting.
 */
@Composable
fun CompassScreen(modifier: Modifier = Modifier) {
    val v = rememberSensor(Sensor.TYPE_ROTATION_VECTOR) ?: return MissingSensor("compass", modifier)

    val heading = remember(v) {
        val matrix = FloatArray(9)
        SensorManager.getRotationMatrixFromVector(matrix, v)
        val orientation = FloatArray(3)
        SensorManager.getOrientation(matrix, orientation)
        ((Math.toDegrees(orientation[0].toDouble()) + 360.0) % 360.0).toFloat()
    }
    // Spring rather than snap, so the needle settles like a real one instead of twitching.
    val angle by animateFloatAsState(
        targetValue = -heading,
        animationSpec = spring(dampingRatio = 0.7f, stiffness = 200f),
        label = "needle",
    )

    val dial = MaterialTheme.colorScheme.surfaceContainerHigh
    val north = MaterialTheme.colorScheme.error
    val south = MaterialTheme.colorScheme.onSurfaceVariant
    val ink = MaterialTheme.colorScheme.onSurface

    Column(
        modifier.fillMaxSize().padding(24.dp),
        verticalArrangement = Arrangement.Center,
        horizontalAlignment = Alignment.CenterHorizontally,
    ) {
        Box(Modifier.fillMaxWidth().aspectRatio(1f), contentAlignment = Alignment.Center) {
            Canvas(Modifier.fillMaxSize()) {
                val r = size.minDimension / 2f
                val c = Offset(size.width / 2f, size.height / 2f)
                
                // Draw Dial Background
                drawCircle(dial, r, c)
                
                // Draw Tick Marks
                for (i in 0 until 12) {
                    rotate(i * 30f, c) {
                        drawLine(
                            color = if (i % 3 == 0) ink else ink.copy(alpha = 0.5f),
                            start = Offset(c.x, c.y - r + 10f),
                            end = Offset(c.x, c.y - r + if (i % 3 == 0) 30f else 20f),
                            strokeWidth = if (i % 3 == 0) 6f else 3f
                        )
                    }
                }
                
                // Draw Labels
                val textPaint = android.graphics.Paint().apply {
                    color = android.graphics.Color.argb(255, (ink.red*255).toInt(), (ink.green*255).toInt(), (ink.blue*255).toInt())
                    textSize = 40f
                    textAlign = android.graphics.Paint.Align.CENTER
                    isAntiAlias = true
                }
                drawContext.canvas.nativeCanvas.apply {
                    drawText("N", c.x, c.y - r + 75f, textPaint.apply { color = android.graphics.Color.argb(255, (north.red*255).toInt(), (north.green*255).toInt(), (north.blue*255).toInt()); isFakeBoldText = true })
                    drawText("E", c.x + r - 50f, c.y + 15f, textPaint.apply { color = android.graphics.Color.argb(255, (ink.red*255).toInt(), (ink.green*255).toInt(), (ink.blue*255).toInt()); isFakeBoldText = false })
                    drawText("S", c.x, c.y + r - 40f, textPaint)
                    drawText("W", c.x - r + 50f, c.y + 15f, textPaint)
                }

                rotate(angle, c) {
                    // Needle: red half points north, grey half south.
                    drawRoundRect(
                        color = north,
                        topLeft = Offset(c.x - r * 0.05f, c.y - r * 0.78f),
                        size = androidx.compose.ui.geometry.Size(r * 0.1f, r * 0.78f),
                        cornerRadius = androidx.compose.ui.geometry.CornerRadius(r * 0.05f),
                    )
                    drawRoundRect(
                        color = south,
                        topLeft = Offset(c.x - r * 0.05f, c.y),
                        size = androidx.compose.ui.geometry.Size(r * 0.1f, r * 0.78f),
                        cornerRadius = androidx.compose.ui.geometry.CornerRadius(r * 0.05f),
                    )
                }
                drawCircle(ink, r * 0.08f, c)
                drawCircle(dial, r * 0.04f, c)
            }
        }
        Spacer(Modifier.height(24.dp))
        Text(
            "${heading.roundToInt() % 360}°",
            fontSize = 56.sp,
            fontWeight = FontWeight.Medium,
            color = MaterialTheme.colorScheme.primary,
        )
        Text(
            cardinal(heading),
            style = MaterialTheme.typography.titleMedium,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
        )
    }
}

private fun cardinal(deg: Float): String {
    val names = listOf("North", "North-east", "East", "South-east", "South", "South-west", "West", "North-west")
    return names[(((deg + 22.5f) % 360f) / 45f).toInt().coerceIn(0, 7)]
}

/**
 * On-screen ruler.
 *
 * Marks are drawn from the display's real physical DPI (`DisplayMetrics.ydpi`), which is what makes
 * a centimetre an actual centimetre rather than a guess in pixels. Manufacturers report that value
 * loosely, so a calibration slider scales it — hold a real ruler against the screen once and it is
 * right from then on. Hardware never matches the spec sheet; leaving the knob is the point.
 */
@Composable
fun RulerScreen(modifier: Modifier = Modifier) {
    val context = LocalContext.current
    val metrics: DisplayMetrics = context.resources.displayMetrics
    var calibration by remember { mutableFloatStateOf(1f) }
    var metric by remember { mutableStateOf(true) }
    var fullscreen by remember { mutableStateOf(false) }

    // Immersive mode while full screen: a ruler is measuring real distance, and the status and
    // navigation bars are millimetres it cannot use. Restored on exit and on leaving the screen,
    // or the bars stay hidden across the rest of the app.
    val view = androidx.compose.ui.platform.LocalView.current
    DisposableEffect(fullscreen) {
        val window = (view.context as? android.app.Activity)?.window
        val controller = window?.let {
            androidx.core.view.WindowCompat.getInsetsController(it, view)
        }
        if (fullscreen) {
            controller?.hide(androidx.core.view.WindowInsetsCompat.Type.systemBars())
            controller?.systemBarsBehavior =
                androidx.core.view.WindowInsetsControllerCompat.BEHAVIOR_SHOW_TRANSIENT_BARS_BY_SWIPE
        } else {
            controller?.show(androidx.core.view.WindowInsetsCompat.Type.systemBars())
        }
        onDispose { controller?.show(androidx.core.view.WindowInsetsCompat.Type.systemBars()) }
    }

    // Back leaves full screen before it leaves the tool.
    androidx.activity.compose.BackHandler(enabled = fullscreen) { fullscreen = false }

    val ink = MaterialTheme.colorScheme.onSurface
    val accent = MaterialTheme.colorScheme.primary

    Column(modifier.fillMaxSize()) {
        Box(
            Modifier
                .fillMaxWidth()
                .weight(1f)
                .clickable { fullscreen = !fullscreen }
        ) {
            Canvas(Modifier.fillMaxSize()) {
                // Pixels per unit along the long edge.
                val perInch = metrics.ydpi * calibration
                val step = if (metric) perInch / 2.54f else perInch
                val subdivisions = if (metric) 10 else 8

                var i = 0
                var y = 0f
                while (y < size.height) {
                    val isUnit = i % subdivisions == 0
                    val isHalf = !isUnit && i % (subdivisions / 2) == 0
                    val len = when {
                        isUnit -> size.width * 0.42f
                        isHalf -> size.width * 0.26f
                        else -> size.width * 0.15f
                    }
                    drawLine(
                        color = if (isUnit) accent else ink,
                        start = Offset(0f, y),
                        end = Offset(len, y),
                        strokeWidth = if (isUnit) 4f else 2f,
                    )
                    if (isUnit && i > 0) {
                        drawContext.canvas.nativeCanvas.drawText(
                            (i / subdivisions).toString(),
                            len + 16f,
                            y + 14f,
                            android.graphics.Paint().apply {
                                color = android.graphics.Color.argb(
                                    255,
                                    (ink.red * 255).toInt(),
                                    (ink.green * 255).toInt(),
                                    (ink.blue * 255).toInt(),
                                )
                                textSize = 40f
                                isAntiAlias = true
                            },
                        )
                    }
                    i++
                    y = i * step / subdivisions
                }
            }
        }

        if (fullscreen) return@Column
        Column(
            Modifier
                .fillMaxWidth()
                .windowInsetsPadding(WindowInsets.navigationBars)
                .padding(20.dp),
            verticalArrangement = Arrangement.spacedBy(8.dp),
        ) {
            Row(
                Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.SpaceBetween,
                verticalAlignment = Alignment.CenterVertically,
            ) {
                Text(
                    if (metric) "Centimetres" else "Inches",
                    style = MaterialTheme.typography.titleMedium,
                )
                FilledTonalButton(onClick = { metric = !metric }) {
                    Text(if (metric) "Switch to inches" else "Switch to cm")
                }
            }
            Text(
                "Calibration  ${(calibration * 100).roundToInt()}%",
                style = MaterialTheme.typography.bodyMedium,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
            )
            Slider(
                value = calibration,
                onValueChange = { calibration = it },
                valueRange = 0.7f..1.3f,
            )
            Text(
                "Hold a real ruler against the screen and adjust until the marks line up.",
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
            )
        }
    }
}

@Composable
fun SoundTesterScreen(modifier: Modifier = Modifier) {
    val track = remember {
        val sampleRate = 44100
        val duration = 5 // seconds
        val numSamples = sampleRate * duration
        val t = android.media.AudioTrack(
            android.media.AudioManager.STREAM_MUSIC, sampleRate, android.media.AudioFormat.CHANNEL_OUT_MONO,
            android.media.AudioFormat.ENCODING_PCM_16BIT, numSamples * 2, android.media.AudioTrack.MODE_STATIC
        )
        val samples = ShortArray(numSamples) { (kotlin.math.sin(2 * kotlin.math.PI * it * 440.0 / sampleRate) * 32767).toInt().toShort() }
        t.write(samples, 0, samples.size)
        t
    }
    
    DisposableEffect(Unit) {
        onDispose {
            track.stop()
            track.release()
        }
    }

    var playing by remember { mutableStateOf(false) }
    var channel by remember { mutableStateOf("Both") }

    LaunchedEffect(playing, channel) {
        if (playing) {
            when (channel) {
                "Left" -> track.setStereoVolume(1f, 0f)
                "Right" -> track.setStereoVolume(0f, 1f)
                else -> track.setStereoVolume(1f, 1f)
            }
            if (track.playState != android.media.AudioTrack.PLAYSTATE_PLAYING) {
                track.stop() // Reset position to start before playing again
                track.reloadStaticData()
                track.play()
            }
            kotlinx.coroutines.delay(5000)
            playing = false
        } else {
            track.pause()
        }
    }

    val transition = androidx.compose.animation.core.rememberInfiniteTransition()
    val pulse by transition.animateFloat(
        initialValue = 1f,
        targetValue = if (playing) 1.25f else 1f,
        animationSpec = androidx.compose.animation.core.infiniteRepeatable(
            animation = androidx.compose.animation.core.tween(500, easing = androidx.compose.animation.core.FastOutSlowInEasing),
            repeatMode = androidx.compose.animation.core.RepeatMode.Reverse
        )
    )

    Column(
        modifier = modifier.fillMaxSize().padding(24.dp),
        horizontalAlignment = Alignment.CenterHorizontally,
        verticalArrangement = Arrangement.Center
    ) {
        Box(
            modifier = Modifier
                .size(240.dp)
                .graphicsLayer { scaleX = pulse; scaleY = pulse }
                .clip(androidx.compose.foundation.shape.CircleShape)
                .background(MaterialTheme.colorScheme.primaryContainer.copy(alpha = 0.5f)),
            contentAlignment = Alignment.Center
        ) {
            Box(
                modifier = Modifier
                    .size(160.dp)
                    .clip(androidx.compose.foundation.shape.CircleShape)
                    .background(MaterialTheme.colorScheme.primary),
                contentAlignment = Alignment.Center
            ) {
                Icon(
                    toolIcon("SoundTester", "Sensors"), 
                    contentDescription = null, 
                    modifier = Modifier.size(80.dp), 
                    tint = MaterialTheme.colorScheme.onPrimary
                )
            }
        }
        
        Spacer(Modifier.height(48.dp))
        Text(
            text = "Test Tone",
            style = MaterialTheme.typography.titleLarge,
            color = MaterialTheme.colorScheme.onSurfaceVariant
        )
        Spacer(Modifier.height(48.dp))
        
        SingleChoiceSegmentedButtonRow(modifier = Modifier.fillMaxWidth()) {
            listOf("Left", "Both", "Right").forEachIndexed { index, label ->
                SegmentedButton(
                    selected = channel == label,
                    onClick = { channel = label },
                    shape = SegmentedButtonDefaults.itemShape(index = index, count = 3)
                ) {
                    Text(label)
                }
            }
        }
        
        Spacer(Modifier.height(32.dp))
        Button(
            onClick = { playing = !playing },
            modifier = Modifier.fillMaxWidth().height(72.dp),
            shape = RoundedCornerShape(36.dp),
            colors = ButtonDefaults.buttonColors(
                containerColor = if (playing) MaterialTheme.colorScheme.error else MaterialTheme.colorScheme.primary,
                contentColor = if (playing) MaterialTheme.colorScheme.onError else MaterialTheme.colorScheme.onPrimary
            ),
            elevation = ButtonDefaults.buttonElevation(defaultElevation = 8.dp, pressedElevation = 4.dp)
        ) {
            Text(if (playing) "■ Stop Tone" else "▶ Play Tone", style = MaterialTheme.typography.headlineSmall, fontWeight = FontWeight.Bold)
        }
    }
}
