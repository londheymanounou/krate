package app.krate

import android.view.InputDevice
import android.view.KeyEvent
import android.view.MotionEvent
import android.view.View
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.compose.ui.viewinterop.AndroidView

/**
 * Gamepad tester.
 *
 * Input arrives as two different event streams that Compose does not surface together: buttons are
 * `KeyEvent`s, sticks and triggers are `MotionEvent`s with `SOURCE_JOYSTICK`. So a plain focusable
 * [View] is hosted to receive both and push into Compose state — `Modifier.onKeyEvent` alone would
 * miss every axis.
 *
 * The view must be focusable and hold focus, or key events go to the window instead and nothing
 * registers.
 */
private class PadView(
    context: android.content.Context,
    val onKey: (Int, Boolean) -> Unit,
    val onMotion: (MotionEvent) -> Unit,
) : View(context) {
    init {
        isFocusable = true
        isFocusableInTouchMode = true
        requestFocus()
    }

    override fun onKeyDown(code: Int, event: KeyEvent): Boolean {
        onKey(code, true)
        // Consume only real gamepad keys: swallowing Back or Volume would trap the user here.
        return KeyEvent.isGamepadButton(code) || code == KeyEvent.KEYCODE_DPAD_UP ||
            code == KeyEvent.KEYCODE_DPAD_DOWN || code == KeyEvent.KEYCODE_DPAD_LEFT ||
            code == KeyEvent.KEYCODE_DPAD_RIGHT || code == KeyEvent.KEYCODE_DPAD_CENTER
    }

    override fun onKeyUp(code: Int, event: KeyEvent): Boolean {
        onKey(code, false)
        return KeyEvent.isGamepadButton(code)
    }

    override fun onGenericMotionEvent(event: MotionEvent): Boolean {
        if (event.source and InputDevice.SOURCE_JOYSTICK == InputDevice.SOURCE_JOYSTICK) {
            onMotion(event)
            return true
        }
        return super.onGenericMotionEvent(event)
    }
}

private data class PadState(
    val leftX: Float = 0f,
    val leftY: Float = 0f,
    val rightX: Float = 0f,
    val rightY: Float = 0f,
    val leftTrigger: Float = 0f,
    val rightTrigger: Float = 0f,
    val hatX: Float = 0f,
    val hatY: Float = 0f,
)

/** Buttons worth showing, in the order a controller lays them out. */
private val BUTTONS = listOf(
    KeyEvent.KEYCODE_BUTTON_A to "A",
    KeyEvent.KEYCODE_BUTTON_B to "B",
    KeyEvent.KEYCODE_BUTTON_X to "X",
    KeyEvent.KEYCODE_BUTTON_Y to "Y",
    KeyEvent.KEYCODE_BUTTON_L1 to "L1",
    KeyEvent.KEYCODE_BUTTON_R1 to "R1",
    KeyEvent.KEYCODE_BUTTON_THUMBL to "L3",
    KeyEvent.KEYCODE_BUTTON_THUMBR to "R3",
    KeyEvent.KEYCODE_BUTTON_START to "Start",
    KeyEvent.KEYCODE_BUTTON_SELECT to "Select",
    KeyEvent.KEYCODE_BUTTON_MODE to "Home",
    KeyEvent.KEYCODE_DPAD_UP to "Up",
    KeyEvent.KEYCODE_DPAD_DOWN to "Down",
    KeyEvent.KEYCODE_DPAD_LEFT to "Left",
    KeyEvent.KEYCODE_DPAD_RIGHT to "Right",
)

@Composable
fun GamepadScreen(modifier: Modifier = Modifier) {
    var pressed by remember { mutableStateOf(setOf<Int>()) }
    var state by remember { mutableStateOf(PadState()) }
    var lastCode by remember { mutableIntStateOf(0) }

    // Recomputed on every recomposition rather than remembered: a controller can be plugged in
    // while this screen is open, and a cached list would never notice.
    val pads = InputDevice.getDeviceIds().toList().mapNotNull { InputDevice.getDevice(it) }.filter {
        it.sources and InputDevice.SOURCE_GAMEPAD == InputDevice.SOURCE_GAMEPAD ||
            it.sources and InputDevice.SOURCE_JOYSTICK == InputDevice.SOURCE_JOYSTICK
    }

    Box(modifier.fillMaxSize()) {
        // Invisible, but must be in the layout and focused to receive input at all.
        AndroidView(
            factory = { ctx ->
                PadView(
                    ctx,
                    onKey = { code, down ->
                        lastCode = code
                        pressed = if (down) pressed + code else pressed - code
                    },
                    onMotion = { e ->
                        state = PadState(
                            leftX = e.getAxisValue(MotionEvent.AXIS_X),
                            leftY = e.getAxisValue(MotionEvent.AXIS_Y),
                            rightX = e.getAxisValue(MotionEvent.AXIS_Z),
                            rightY = e.getAxisValue(MotionEvent.AXIS_RZ),
                            // Triggers report on either pair depending on the controller.
                            leftTrigger = maxOf(
                                e.getAxisValue(MotionEvent.AXIS_LTRIGGER),
                                e.getAxisValue(MotionEvent.AXIS_BRAKE),
                            ),
                            rightTrigger = maxOf(
                                e.getAxisValue(MotionEvent.AXIS_RTRIGGER),
                                e.getAxisValue(MotionEvent.AXIS_GAS),
                            ),
                            hatX = e.getAxisValue(MotionEvent.AXIS_HAT_X),
                            hatY = e.getAxisValue(MotionEvent.AXIS_HAT_Y),
                        )
                    },
                )
            },
            modifier = Modifier.size(1.dp),
        )

        Column(
            Modifier.fillMaxSize().padding(20.dp),
            verticalArrangement = Arrangement.spacedBy(14.dp),
        ) {
            Surface(
                shape = RoundedCornerShape(24.dp),
                color = if (pads.isEmpty()) {
                    MaterialTheme.colorScheme.errorContainer
                } else {
                    MaterialTheme.colorScheme.primaryContainer
                },
                modifier = Modifier.fillMaxWidth(),
            ) {
                Column(Modifier.padding(18.dp)) {
                    Text(
                        if (pads.isEmpty()) "No controller detected" else pads.first().name,
                        style = MaterialTheme.typography.titleMedium,
                        fontWeight = FontWeight.Bold,
                        color = if (pads.isEmpty()) {
                            MaterialTheme.colorScheme.onErrorContainer
                        } else {
                            MaterialTheme.colorScheme.onPrimaryContainer
                        },
                    )
                    Text(
                        if (pads.isEmpty()) {
                            "Connect a gamepad over USB or Bluetooth."
                        } else {
                            "${pads.size} device(s) · press anything to test"
                        },
                        style = MaterialTheme.typography.bodyMedium,
                        color = if (pads.isEmpty()) {
                            MaterialTheme.colorScheme.onErrorContainer
                        } else {
                            MaterialTheme.colorScheme.onPrimaryContainer
                        },
                    )
                }
            }

            Row(horizontalArrangement = Arrangement.spacedBy(16.dp)) {
                Stick("Left stick", state.leftX, state.leftY, Modifier.weight(1f))
                Stick("Right stick", state.rightX, state.rightY, Modifier.weight(1f))
            }

            Trigger("L2", state.leftTrigger)
            Trigger("R2", state.rightTrigger)

            Text("Buttons", style = MaterialTheme.typography.titleSmall)
            // Fixed grid rather than a flow: a button that never lights is as informative as one
            // that does, so every pad button stays visible whether pressed or not.
            BUTTONS.chunked(4).forEach { row ->
                Row(
                    Modifier.fillMaxWidth(),
                    horizontalArrangement = Arrangement.spacedBy(8.dp),
                ) {
                    row.forEach { (code, label) ->
                        val on = code in pressed
                        Surface(
                            shape = RoundedCornerShape(16.dp),
                            color = if (on) {
                                MaterialTheme.colorScheme.primary
                            } else {
                                MaterialTheme.colorScheme.surfaceContainerHigh
                            },
                            modifier = Modifier.weight(1f).height(44.dp),
                        ) {
                            Box(Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
                                Text(
                                    label,
                                    style = MaterialTheme.typography.labelLarge,
                                    color = if (on) {
                                        MaterialTheme.colorScheme.onPrimary
                                    } else {
                                        MaterialTheme.colorScheme.onSurfaceVariant
                                    },
                                )
                            }
                        }
                    }
                    repeat(4 - row.size) { Spacer(Modifier.weight(1f)) }
                }
            }

            if (lastCode != 0) {
                Text(
                    "Last key code: $lastCode  (${KeyEvent.keyCodeToString(lastCode)})",
                    style = MaterialTheme.typography.bodySmall,
                    fontFamily = FontFamily.Monospace,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
            }
        }
    }
}

@Composable
private fun Stick(label: String, x: Float, y: Float, modifier: Modifier = Modifier) {
    val ring = MaterialTheme.colorScheme.surfaceContainerHighest
    val dot = MaterialTheme.colorScheme.primary
    Column(modifier, horizontalAlignment = Alignment.CenterHorizontally) {
        Box(Modifier.fillMaxWidth().aspectRatio(1f)) {
            Canvas(Modifier.fillMaxSize()) {
                val r = size.minDimension / 2f
                val c = Offset(size.width / 2f, size.height / 2f)
                drawCircle(ring, r, c)
                drawCircle(dot, r * 0.16f, Offset(c.x + x * r * 0.8f, c.y + y * r * 0.8f))
            }
        }
        Spacer(Modifier.height(4.dp))
        Text(label, style = MaterialTheme.typography.labelMedium)
        Text(
            String.format("%+.2f, %+.2f", x, y),
            fontSize = 11.sp,
            fontFamily = FontFamily.Monospace,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
            textAlign = TextAlign.Center,
        )
    }
}

@Composable
private fun Trigger(label: String, value: Float) {
    Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
        Text(label, style = MaterialTheme.typography.labelLarge, modifier = Modifier.width(32.dp))
        LinearProgressIndicator(
            progress = { value.coerceIn(0f, 1f) },
            modifier = Modifier.weight(1f).height(10.dp).clip(CircleShape),
        )
        Spacer(Modifier.width(8.dp))
        Text(
            String.format("%.2f", value),
            fontSize = 12.sp,
            fontFamily = FontFamily.Monospace,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
        )
    }
}
