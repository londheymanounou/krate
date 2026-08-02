@file:OptIn(androidx.compose.foundation.layout.ExperimentalLayoutApi::class)

package app.krate

import android.view.HapticFeedbackConstants
import androidx.compose.animation.core.animateFloatAsState
import androidx.compose.animation.core.tween
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.interaction.collectIsPressedAsState
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.foundation.horizontalScroll
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.rounded.Backspace
import androidx.compose.material.icons.rounded.KeyboardArrowDown
import androidx.compose.material.icons.rounded.KeyboardArrowUp
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.platform.LocalView
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext

/*
 * Calculator modelled directly on NumberHub (Myzel394/NumberHub, itself a fork of Unitto by
 * Elshan Agaev, GPL-3.0). The layout, button variants and the squash-on-press interaction are
 * reproduced from its `CalculatorKeyboard.kt`, `KeypadFlow.kt` and `KeyboardButton.kt`.
 *
 * Only the evaluation differs: expressions go to KRATE's Rust core rather than NumberHub's own
 * engine, so the displayed maths is still the same code the desktop and CLI use.
 */

/** `KeypadFlow`: equal cells via FlowRow, 10% of each axis given up to spacing. */
@Composable
private fun KeypadFlow(
    modifier: Modifier,
    rows: Int,
    columns: Int,
    horizontalPadding: Int = 6,
    verticalPadding: Int = 6,
    content: @Composable FlowRowScope.(width: Float, height: Float) -> Unit,
) {
    val height = (1f - verticalPadding / 100f) / rows
    val width = (1f - horizontalPadding / 100f) / columns
    FlowRow(
        modifier = modifier,
        maxItemsInEachRow = columns,
        horizontalArrangement = Arrangement.SpaceAround,
        verticalArrangement = Arrangement.SpaceAround,
    ) { content(width, height) }
}

/**
 * `squashable`: keys are **circles at rest** (50% corner radius) and squash toward a rounded square
 * (30%) while held — that direction, verified against the installed app, is the whole point of the
 * name. Round-at-rest is the single most recognisable thing about this keypad.
 */
@Composable
private fun KeyButton(
    modifier: Modifier,
    container: Color,
    content: Color,
    onClick: () -> Unit,
    body: @Composable () -> Unit,
) {
    val interaction = remember { MutableInteractionSource() }
    val pressed by interaction.collectIsPressedAsState()
    val corner by animateFloatAsState(
        targetValue = if (pressed) 30f else 50f,
        animationSpec = tween(200),
        label = "corner",
    )
    val view = LocalView.current
    val haptics = LocalHaptics.current
    Box(
        modifier = modifier
            .clip(RoundedCornerShape(percent = corner.toInt()))
            .background(container)
            .clickable(
                interactionSource = interaction,
                indication = null,
            ) {
                view.tick(haptics)
                onClick()
            },
        contentAlignment = Alignment.Center,
    ) {
        CompositionLocalProvider(LocalContentColor provides content) { body() }
    }
}

@Composable
fun CalcScreen(modifier: Modifier = Modifier) {
    val view = LocalView.current
    val haptics = LocalHaptics.current
    var expr by remember { mutableStateOf("") }
    var result by remember { mutableStateOf("") }
    val scope = rememberCoroutineScope()

    fun evaluate(text: String) {
        if (text.isBlank()) { result = ""; return }
        scope.launch {
            val res = withContext(Dispatchers.IO) { Core.run("Calc", text) }
            if (res.ok) result = res.text
        }
    }
    fun press(key: String) { expr += key; evaluate(expr) }

    // Display glyphs differ from what the core is sent: the keys show × ÷ − and so must the
    // expression, but `Calc` needs ASCII operators. `expr` stays ASCII; only rendering is prettified.
    val pretty = expr.replace("*", "×").replace("/", "÷").replace("-", "−")

    Column(modifier.fillMaxSize()) {
        // TextBox: a surfaceVariant panel with rounded *bottom* corners, taking a quarter of the
        // height, with the drag handle at its base. Floating text on the background is the thing
        // that made this not look like NumberHub.
        Surface(
            modifier = Modifier.fillMaxWidth().weight(28f),
            color = MaterialTheme.colorScheme.surfaceVariant,
            shape = RoundedCornerShape(bottomStart = 28.dp, bottomEnd = 28.dp),
        ) {
            Column(
                Modifier.fillMaxSize().padding(horizontal = 20.dp),
                horizontalAlignment = Alignment.CenterHorizontally,
            ) {
                // Input and result are weighted 3:2, so the expression gets ~60% of the panel.
                Box(Modifier.weight(3f).fillMaxWidth(), contentAlignment = Alignment.CenterEnd) {
                    Text(
                        pretty,
                        fontSize = 76.sp,
                        maxLines = 1,
                        textAlign = TextAlign.End,
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                        modifier = Modifier.horizontalScroll(rememberScrollState()),
                    )
                }
                Box(Modifier.weight(2f).fillMaxWidth(), contentAlignment = Alignment.CenterEnd) {
                    Text(
                        result,
                        fontSize = 50.sp,
                        maxLines = 1,
                        textAlign = TextAlign.End,
                        // 60% opacity is what makes the result read as secondary to the input.
                        color = MaterialTheme.colorScheme.onSurfaceVariant.copy(alpha = 0.6f),
                        modifier = Modifier.horizontalScroll(rememberScrollState()),
                    )
                }
                Box(
                    Modifier
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
        val filled = MaterialTheme.colorScheme.primaryContainer
        val onFilled = MaterialTheme.colorScheme.onPrimaryContainer
        val tertiary = MaterialTheme.colorScheme.tertiaryContainer
        val onTertiary = MaterialTheme.colorScheme.onTertiaryContainer

        // Secondary functions. The chevron expands a second page, as NumberHub's does — every
        // entry below is a name `calc.rs` actually parses, so no key here is decorative.
        var expanded by remember { mutableStateOf(false) }
        val quick = listOf("√" to "sqrt(", "π" to "pi", "^" to "^", "!" to "!")
        val more = listOf(
            "sin" to "sin(", "cos" to "cos(", "tan" to "tan(",
            "asin" to "asin(", "acos" to "acos(", "atan" to "atan(",
            "ln" to "ln(", "log" to "log(", "log₂" to "log2(",
            "e^x" to "exp(", "e" to "e", "τ" to "tau",
            "φ" to "phi", "abs" to "abs(", "∛" to "cbrt(",
            "⌊⌋" to "floor(", "⌈⌉" to "ceil(", "round" to "round(",
            "deg" to "deg(", "rad" to "rad(", "(" to "(", ")" to ")",
        )

        Row(
            Modifier.fillMaxWidth().padding(horizontal = 16.dp, vertical = 4.dp),
            horizontalArrangement = Arrangement.SpaceAround,
            verticalAlignment = Alignment.CenterVertically,
        ) {
            quick.forEach { (glyph, emit) ->
                Text(
                    glyph,
                    fontSize = 26.sp,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                    modifier = Modifier
                        .clickable { view.tick(haptics); expr += emit; evaluate(expr) }
                        .padding(horizontal = 16.dp, vertical = 8.dp),
                )
            }
            Box(
                Modifier
                    .size(44.dp)
                    .clip(RoundedCornerShape(percent = 50))
                    .background(MaterialTheme.colorScheme.surfaceVariant)
                    .clickable { expanded = !expanded },
                contentAlignment = Alignment.Center,
            ) {
                Icon(
                    if (expanded) Icons.Rounded.KeyboardArrowUp else Icons.Rounded.KeyboardArrowDown,
                    contentDescription = if (expanded) "Fewer functions" else "More functions",
                    tint = MaterialTheme.colorScheme.onSurfaceVariant,
                )
            }
        }

        if (expanded) {
            // Scrolls rather than shrinking the keypad: the digits stay where the thumb expects.
            FlowRow(
                modifier = Modifier
                    .fillMaxWidth()
                    .heightIn(max = 168.dp)
                    .verticalScroll(rememberScrollState())
                    .padding(horizontal = 12.dp),
                horizontalArrangement = Arrangement.spacedBy(6.dp),
            ) {
                more.forEach { (label, emit) ->
                    SuggestionChip(
                        onClick = { view.tick(haptics); expr += emit; evaluate(expr) },
                        label = { Text(label) },
                    )
                }
            }
        }

        KeypadFlow(
            modifier = Modifier
                .fillMaxWidth()
                .weight(64f)
                .windowInsetsPadding(WindowInsets.navigationBars)
                .padding(horizontal = 8.dp, vertical = 4.dp),
            rows = 5,
            columns = 4,
        ) { width, height ->
            val cell = Modifier.fillMaxWidth(width).fillMaxHeight(height)

            @Composable
            fun key(label: String, c: Color, oc: Color, emit: String = label) =
                KeyButton(cell, c, oc, {
                    when (label) {
                        "AC" -> { expr = ""; result = "" }
                        "=" -> if (result.isNotBlank()) { expr = result; evaluate(result) }
                        else -> press(emit)
                    }
                }) {
                    Text(
                        label,
                        fontSize = when {
                            label == "AC" -> 32.sp
                            label.length == 1 && label[0].isDigit() -> 36.sp
                            // Operators read heavier than digits in the reference, not just larger.
                            else -> 42.sp
                        },
                        fontWeight = if (label.length == 1 && label[0].isDigit()) {
                            androidx.compose.ui.text.font.FontWeight.Normal
                        } else {
                            androidx.compose.ui.text.font.FontWeight.Medium
                        },
                    )
                }

            // Row 1 — AC is the only tertiary key; brackets, percent and divide are filled.
            key("AC", tertiary, onTertiary)
            key("( )", filled, onFilled, emit = "(")
            key("%", filled, onFilled)
            key("÷", filled, onFilled, emit = "/")
            // Rows 2-4 — digits light, operator filled at the right edge.
            key("7", light, onLight); key("8", light, onLight); key("9", light, onLight)
            key("×", filled, onFilled, emit = "*")
            key("4", light, onLight); key("5", light, onLight); key("6", light, onLight)
            key("−", filled, onFilled, emit = "-")
            key("1", light, onLight); key("2", light, onLight); key("3", light, onLight)
            key("+", filled, onFilled)
            // Row 5 — dot, zero, backspace, equals.
            key(".", light, onLight); key("0", light, onLight)
            KeyButton(cell, light, onLight, {
                if (expr.isNotEmpty()) { expr = expr.dropLast(1); evaluate(expr) }
            }) { Icon(Icons.Rounded.Backspace, "Delete") }
            key("=", filled, onFilled)
        }
    }
}
