package app.krate

import androidx.compose.animation.core.animateFloatAsState
import androidx.compose.animation.core.spring
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.rounded.Add
import androidx.compose.material.icons.rounded.Refresh
import androidx.compose.material.icons.rounded.Remove
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.LocalView
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp

/**
 * Tally counter.
 *
 * **Persisted, deliberately.** The point of a tally counter is that you leave the screen, come back,
 * and the count is still there — counting stock, laps, people through a door. A counter that resets
 * on navigation is a toy.
 *
 * Haptics fire here, unlike ordinary navigation: this is repeated eyes-down input where the finger
 * wants confirmation without looking, which is the same reason the calculator keys buzz.
 */
private const val PREFS = "krate"
private const val KEY = "tally_count"
private const val STEP = "tally_step"

@Composable
fun TallyScreen(modifier: Modifier = Modifier) {
    val context = LocalContext.current
    val prefs = remember { context.getSharedPreferences(PREFS, android.content.Context.MODE_PRIVATE) }
    var count by remember { mutableIntStateOf(prefs.getInt(KEY, 0)) }
    var step by remember { mutableIntStateOf(prefs.getInt(STEP, 1)) }
    var confirmReset by remember { mutableStateOf(false) }
    val view = LocalView.current
    val haptics = LocalHaptics.current

    fun set(value: Int) {
        count = value
        prefs.edit().putInt(KEY, value).apply()
        view.tick(haptics)
    }

    // The number springs when it changes, so a tap registers even when you are not looking at it.
    val scale by animateFloatAsState(
        targetValue = 1f,
        animationSpec = spring(dampingRatio = 0.4f, stiffness = 600f),
        label = "bump",
    )

    Column(
        modifier.fillMaxSize().padding(20.dp),
        verticalArrangement = Arrangement.spacedBy(16.dp),
        horizontalAlignment = Alignment.CenterHorizontally,
    ) {
        Surface(
            shape = RoundedCornerShape(32.dp),
            color = MaterialTheme.colorScheme.primaryContainer,
            modifier = Modifier.fillMaxWidth().weight(1f),
        ) {
            Box(Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
                Text(
                    count.toString(),
                    fontSize = 96.sp,
                    fontWeight = FontWeight.Medium,
                    color = MaterialTheme.colorScheme.onPrimaryContainer,
                    modifier = Modifier.graphicsLayer { scaleX = scale; scaleY = scale },
                )
            }
        }

        // The plus is deliberately far larger than the minus: counting up is the common action and
        // should be hittable without aiming.
        Row(
            Modifier.fillMaxWidth(),
            horizontalArrangement = Arrangement.spacedBy(12.dp),
            verticalAlignment = Alignment.CenterVertically,
        ) {
            FilledTonalIconButton(
                onClick = { set(count - step) },
                modifier = Modifier.size(72.dp),
                shape = CircleShape,
            ) { Icon(Icons.Rounded.Remove, "Subtract", Modifier.size(32.dp)) }

            Button(
                onClick = { set(count + step) },
                modifier = Modifier.weight(1f).height(96.dp),
                shape = RoundedCornerShape(32.dp),
            ) { Icon(Icons.Rounded.Add, "Add", Modifier.size(44.dp)) }
        }

        Row(
            Modifier.fillMaxWidth(),
            horizontalArrangement = Arrangement.SpaceBetween,
            verticalAlignment = Alignment.CenterVertically,
        ) {
            Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                listOf(1, 2, 5, 10).forEach {
                    FilterChip(
                        selected = step == it,
                        onClick = { step = it; prefs.edit().putInt(STEP, it).apply() },
                        label = { Text("+$it") },
                    )
                }
            }
            TextButton(onClick = { confirmReset = true }, enabled = count != 0) {
                Icon(Icons.Rounded.Refresh, null, Modifier.size(18.dp))
                Spacer(Modifier.width(6.dp))
                Text("Reset")
            }
        }
    }

    if (confirmReset) {
        // Confirmed, because losing a long count to a stray tap is the one unrecoverable thing here.
        AlertDialog(
            onDismissRequest = { confirmReset = false },
            title = { Text("Reset to zero?") },
            text = { Text("The current count of $count will be lost.") },
            confirmButton = {
                TextButton(onClick = { set(0); confirmReset = false }) { Text("Reset") }
            },
            dismissButton = {
                TextButton(onClick = { confirmReset = false }) { Text("Cancel") }
            },
        )
    }
}
