@file:OptIn(
    androidx.compose.material3.ExperimentalMaterial3Api::class,
    androidx.compose.foundation.layout.ExperimentalLayoutApi::class,
)

package app.krate

import androidx.compose.animation.core.animateFloatAsState
import androidx.compose.animation.core.tween
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.horizontalScroll
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.interaction.collectIsPressedAsState
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.rounded.Backspace
import androidx.compose.material.icons.rounded.SwapVert
import androidx.compose.material.icons.rounded.UnfoldMore
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext

/**
 * Unit converter in NumberHub's shape: a two-pane display with its own numeric keypad, and units
 * chosen from a bottom sheet rather than a dropdown.
 *
 * Two reasons this replaced the dropdown version. The `ExposedDropdownMenuBox` anchors were not
 * opening on tap — a read-only text field inside the anchor swallows the click, which is why the
 * pickers appeared dead. And a dropdown is the wrong control regardless: a sheet shows every unit
 * at once with room for the full name, where a dropdown showed four cramped rows.
 *
 * The system keyboard is gone too. It covered the result on a screen whose entire point is watching
 * the result change as you type.
 */
private val CATEGORIES: List<Pair<String, List<Pair<String, String>>>> = listOf(
    "Length" to listOf(
        "Millimetres" to "mm", "Centimetres" to "cm", "Metres" to "m", "Kilometres" to "km",
        "Inches" to "in", "Feet" to "ft", "Yards" to "yd", "Miles" to "mi",
        "Nautical miles" to "nmi",
    ),
    "Mass" to listOf(
        "Milligrams" to "mg", "Grams" to "g", "Kilograms" to "kg", "Tonnes" to "t",
        "Ounces" to "oz", "Pounds" to "lb", "Stone" to "st",
    ),
    "Temperature" to listOf("Celsius" to "c", "Fahrenheit" to "f", "Kelvin" to "k"),
    "Time" to listOf(
        "Milliseconds" to "ms", "Seconds" to "s", "Minutes" to "min", "Hours" to "h",
        "Days" to "d", "Weeks" to "wk",
    ),
    "Data" to listOf(
        "Bits" to "b", "Kilobits" to "kb", "Megabits" to "mb",
        "Bytes" to "B", "Kilobytes (1000)" to "kB", "Megabytes (1000)" to "MB",
        "Gigabytes (1000)" to "GB", "Terabytes (1000)" to "TB",
        "Kibibytes (1024)" to "KiB", "Mebibytes (1024)" to "MiB",
        "Gibibytes (1024)" to "GiB", "Tebibytes (1024)" to "TiB",
    ),
    "Speed" to listOf(
        "Metres per second" to "mps", "Kilometres per hour" to "kmh",
        "Miles per hour" to "mph", "Knots" to "kn",
    ),
    "Area" to listOf("Hectares" to "ha", "Acres" to "acre"),
    "Volume" to listOf(
        "Millilitres" to "ml", "Litres" to "l", "Gallons" to "gal",
        "Pints" to "pt", "Fluid ounces" to "floz",
    ),
    "Angle" to listOf(
        "Degrees" to "deg", "Radians" to "rad", "Gradians" to "grad", "Turns" to "turn",
    ),
)

/** Same squash-on-press key as the calculator: circle at rest, rounded square while held. */
@Composable
private fun ConvKey(
    label: String,
    modifier: Modifier,
    container: Color,
    content: Color,
    onClick: () -> Unit,
    icon: (@Composable () -> Unit)? = null,
) {
    val interaction = remember { MutableInteractionSource() }
    val pressed by interaction.collectIsPressedAsState()
    val view = androidx.compose.ui.platform.LocalView.current
    val haptics = LocalHaptics.current
    val corner by animateFloatAsState(
        targetValue = if (pressed) 30f else 50f,
        animationSpec = tween(200),
        label = "corner",
    )
    Box(
        modifier = modifier
            .clip(RoundedCornerShape(percent = corner.toInt()))
            .background(container)
            .clickable(interactionSource = interaction, indication = null) { view.tick(haptics); onClick() },
        contentAlignment = Alignment.Center,
    ) {
        CompositionLocalProvider(LocalContentColor provides content) {
            if (icon != null) icon() else Text(label, fontSize = 30.sp)
        }
    }
}

@Composable
fun ConvertScreen(modifier: Modifier = Modifier) {
    var category by remember { mutableIntStateOf(0) }
    val units = CATEGORIES[category].second
    var from by remember(category) { mutableStateOf(units.first()) }
    var to by remember(category) { mutableStateOf(units.getOrElse(1) { units.first() }) }
    var value by remember { mutableStateOf("1") }
    var result by remember { mutableStateOf("") }
    var isError by remember { mutableStateOf(false) }
    var picking by remember { mutableStateOf<String?>(null) }
    val scope = rememberCoroutineScope()

    LaunchedEffect(value, from, to) {
        if (value.isBlank() || value == "-") {
            result = ""
            isError = false
            return@LaunchedEffect
        }
        scope.launch {
            val res = withContext(Dispatchers.IO) {
                Core.run("Convert", value + " " + from.second + " " + to.second)
            }
            // The core echoes the unit back; the pane already shows it, so keep only the number.
            result = if (res.ok) res.text.substringBeforeLast(' ') else res.text
            isError = !res.ok
        }
    }

    Column(modifier.fillMaxSize()) {
        ScrollableTabRow(
            selectedTabIndex = category,
            edgePadding = 12.dp,
            containerColor = MaterialTheme.colorScheme.surface,
        ) {
            CATEGORIES.forEachIndexed { i, pair ->
                Tab(
                    selected = category == i,
                    onClick = { category = i },
                    text = { Text(pair.first) },
                )
            }
        }

        Surface(
            modifier = Modifier.fillMaxWidth().weight(34f),
            color = MaterialTheme.colorScheme.surfaceVariant,
            shape = RoundedCornerShape(bottomStart = 28.dp, bottomEnd = 28.dp),
        ) {
            Column(Modifier.fillMaxSize().padding(horizontal = 20.dp)) {
                Pane(value, from, true, Modifier.weight(1f)) { picking = "from" }
                Pane(if (isError) "—" else result, to, false, Modifier.weight(1f)) { picking = "to" }
                Box(
                    Modifier
                        .align(Alignment.CenterHorizontally)
                        .padding(bottom = 6.dp)
                        .width(32.dp)
                        .height(4.dp)
                        .clip(RoundedCornerShape(2.dp))
                        .background(MaterialTheme.colorScheme.onSurfaceVariant.copy(alpha = 0.4f)),
                )
            }
        }

        val light = MaterialTheme.colorScheme.inverseOnSurface
        val onLight = MaterialTheme.colorScheme.onSurfaceVariant
        val accent = MaterialTheme.colorScheme.primaryContainer
        val onAccent = MaterialTheme.colorScheme.onPrimaryContainer
        val warn = MaterialTheme.colorScheme.tertiaryContainer
        val onWarn = MaterialTheme.colorScheme.onTertiaryContainer

        FlowRow(
            modifier = Modifier
                .fillMaxWidth()
                .weight(66f)
                .windowInsetsPadding(WindowInsets.navigationBars)
                .padding(horizontal = 8.dp, vertical = 4.dp),
            maxItemsInEachRow = 4,
            horizontalArrangement = Arrangement.SpaceAround,
            verticalArrangement = Arrangement.SpaceAround,
        ) {
            // 4 x 4. Actions occupy the right column and 0 is double width, so there is no ragged
            // gap — the previous layout left two empty cells beside "." and "0" and looked broken.
            val cell = Modifier.fillMaxWidth(0.235f).fillMaxHeight(0.235f)
            val wide = Modifier.fillMaxWidth(0.49f).fillMaxHeight(0.235f)

            fun push(c: Char) {
                value = when {
                    c == '.' && value.contains('.') -> value
                    value == "0" && c != '.' -> c.toString()
                    else -> value + c
                }
            }

            ConvKey("7", cell, light, onLight, { push('7') })
            ConvKey("8", cell, light, onLight, { push('8') })
            ConvKey("9", cell, light, onLight, { push('9') })
            ConvKey("AC", cell, warn, onWarn, { value = "0"; result = "" })

            ConvKey("4", cell, light, onLight, { push('4') })
            ConvKey("5", cell, light, onLight, { push('5') })
            ConvKey("6", cell, light, onLight, { push('6') })
            ConvKey("", cell, light, onLight, {
                value = if (value.length <= 1) "0" else value.dropLast(1)
            }) { Icon(Icons.Rounded.Backspace, "Delete") }

            ConvKey("1", cell, light, onLight, { push('1') })
            ConvKey("2", cell, light, onLight, { push('2') })
            ConvKey("3", cell, light, onLight, { push('3') })
            ConvKey("+/-", cell, accent, onAccent, {
                value = if (value.startsWith("-")) value.removePrefix("-") else "-" + value
            })

            ConvKey("0", wide, light, onLight, { push('0') })
            ConvKey(".", cell, light, onLight, { push('.') })
            ConvKey("", cell, accent, onAccent, {
                val t = from; from = to; to = t
            }) { Icon(Icons.Rounded.SwapVert, "Swap units") }
        }
    }

    if (picking != null) {
        ModalBottomSheet(onDismissRequest = { picking = null }) {
            LazyColumn(Modifier.fillMaxWidth().heightIn(max = 520.dp)) {
                items(units, key = { it.second }) { unit ->
                    val selected = if (picking == "from") unit == from else unit == to
                    ListItem(
                        headlineContent = { Text(unit.first) },
                        trailingContent = {
                            Text(unit.second, style = MaterialTheme.typography.labelLarge)
                        },
                        colors = ListItemDefaults.colors(
                            containerColor = if (selected) {
                                MaterialTheme.colorScheme.secondaryContainer
                            } else {
                                Color.Transparent
                            }
                        ),
                        modifier = Modifier.clickable {
                            if (picking == "from") from = unit else to = unit
                            picking = null
                        },
                    )
                }
            }
        }
    }
}

/** One half of the display: a big number, and the unit chip that opens the picker. */
@Composable
private fun Pane(
    number: String,
    unit: Pair<String, String>,
    emphasised: Boolean,
    modifier: Modifier = Modifier,
    onPickUnit: () -> Unit,
) {
    Column(modifier.fillMaxWidth(), verticalArrangement = Arrangement.Center) {
        Text(
            number.ifEmpty { "0" },
            fontSize = if (emphasised) 52.sp else 44.sp,
            fontWeight = if (emphasised) FontWeight.Normal else FontWeight.Light,
            color = if (emphasised) {
                MaterialTheme.colorScheme.onSurfaceVariant
            } else {
                MaterialTheme.colorScheme.onSurfaceVariant.copy(alpha = 0.6f)
            },
            maxLines = 1,
            textAlign = TextAlign.End,
            modifier = Modifier.fillMaxWidth().horizontalScroll(rememberScrollState()),
        )
        Spacer(Modifier.height(2.dp))
        Row(
            Modifier
                .align(Alignment.End)
                .clip(RoundedCornerShape(50))
                .clickable(onClick = onPickUnit)
                .padding(horizontal = 12.dp, vertical = 4.dp),
            verticalAlignment = Alignment.CenterVertically,
        ) {
            Text(
                unit.first,
                style = MaterialTheme.typography.labelLarge,
                color = MaterialTheme.colorScheme.primary,
            )
            Spacer(Modifier.width(4.dp))
            Icon(
                Icons.Rounded.UnfoldMore,
                null,
                Modifier.size(16.dp),
                tint = MaterialTheme.colorScheme.primary,
            )
        }
    }
}
