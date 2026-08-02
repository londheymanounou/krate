@file:OptIn(androidx.compose.material3.ExperimentalMaterial3ExpressiveApi::class)

package app.krate

import androidx.compose.animation.core.*
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import kotlin.math.roundToInt

// ---------------------------------------------------------------- dice

/** Pip layout per face, as fractions of the die. Faces above 6 fall back to the number. */
private val PIPS: Map<Int, List<Pair<Float, Float>>> = mapOf(
    1 to listOf(.5f to .5f),
    2 to listOf(.25f to .25f, .75f to .75f),
    3 to listOf(.25f to .25f, .5f to .5f, .75f to .75f),
    4 to listOf(.25f to .25f, .75f to .25f, .25f to .75f, .75f to .75f),
    5 to listOf(.25f to .25f, .75f to .25f, .5f to .5f, .25f to .75f, .75f to .75f),
    6 to listOf(.25f to .25f, .75f to .25f, .25f to .5f, .75f to .5f, .25f to .75f, .75f to .75f),
)

@Composable
private fun Die(value: Int, faces: Int, rollId: Int = 0, modifier: Modifier = Modifier) {
    // ponytail: Minimum viable animation. We use Animatable on entry.
    // This works reliably because the Dice are wiped from composition while 'rolling' is true.
    val rotation = remember { androidx.compose.animation.core.Animatable(-90f) }
    val scale = remember { androidx.compose.animation.core.Animatable(0.5f) }
    LaunchedEffect(rollId) {
        rotation.snapTo(-90f)
        scale.snapTo(0.5f)
        launch { rotation.animateTo(0f, androidx.compose.animation.core.spring(dampingRatio = 0.5f, stiffness = 400f)) }
        launch { scale.animateTo(1f, androidx.compose.animation.core.spring(dampingRatio = 0.5f, stiffness = 400f)) }
    }

    Surface(
        shape = RoundedCornerShape(20.dp),
        color = MaterialTheme.colorScheme.primaryContainer,
        modifier = modifier.size(84.dp).graphicsLayer {
            rotationZ = rotation.value
            scaleX = scale.value
            scaleY = scale.value
        },
    ) {
        val pips = if (faces <= 6) PIPS[value] else null
        if (pips == null) {
            Box(Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
                Text(
                    "$value",
                    style = MaterialTheme.typography.headlineMedium,
                    fontWeight = FontWeight.Bold,
                    color = MaterialTheme.colorScheme.onPrimaryContainer,
                )
            }
        } else {
            val ink = MaterialTheme.colorScheme.onPrimaryContainer
            Canvas(Modifier.fillMaxSize().padding(10.dp)) {
                val r = size.minDimension * 0.09f
                pips.forEach { (fx, fy) ->
                    drawCircle(ink, r, Offset(size.width * fx, size.height * fy))
                }
            }
        }
    }
}

@Composable
fun DiceScreen(modifier: Modifier = Modifier) {
    var count by remember { mutableFloatStateOf(2f) }
    var faces by remember { mutableIntStateOf(6) }
    var rolls by remember { mutableStateOf<List<Int>>(emptyList()) }
    var rolling by remember { mutableStateOf(false) }
    var rollId by remember { mutableIntStateOf(0) }
    val scope = rememberCoroutineScope()

    fun roll() {
        val n = count.roundToInt()
        scope.launch {
            rolling = true
            // The core is the only source of randomness — it is the CSPRNG the desktop uses, and
            // reimplementing a roll in Kotlin would make the two platforms disagree.
            val res = withContext(Dispatchers.IO) { Core.run("Dice", "${n}d$faces") }
            rolls = if (res.ok) {
                Regex("\\d+").findAll(res.text.substringBefore('=')).map { it.value.toInt() }.toList()
            } else emptyList()
            rollId++
            rolling = false
        }
    }

    LaunchedEffect(Unit) { roll() }

    Column(
        modifier = modifier.fillMaxSize().padding(24.dp),
        verticalArrangement = Arrangement.spacedBy(20.dp),
    ) {
        Surface(
            shape = RoundedCornerShape(28.dp),
            color = MaterialTheme.colorScheme.surfaceContainerHigh,
            modifier = Modifier.fillMaxWidth().weight(1f),
        ) {
            Box(Modifier.fillMaxSize().padding(24.dp), contentAlignment = Alignment.Center) {
                if (rolling) {
                    LoadingIndicator()
                } else {
                    Column(horizontalAlignment = Alignment.CenterHorizontally) {
                        FlowRowSimple(rolls, faces, rollId)
                        if (rolls.size > 1) {
                            Spacer(Modifier.height(20.dp))
                            Text(
                                "Total  ${rolls.sum()}",
                                style = MaterialTheme.typography.headlineSmall,
                                fontWeight = FontWeight.Bold,
                                color = MaterialTheme.colorScheme.primary,
                            )
                        }
                    }
                }
            }
        }

        Row(
            Modifier.fillMaxWidth(),
            horizontalArrangement = Arrangement.SpaceBetween,
            verticalAlignment = Alignment.CenterVertically,
        ) {
            Text("Dice", style = MaterialTheme.typography.titleMedium)
            Text(
                count.roundToInt().toString(),
                style = MaterialTheme.typography.titleMedium,
                color = MaterialTheme.colorScheme.primary,
            )
        }
        Slider(value = count, onValueChange = { count = it }, valueRange = 1f..8f, steps = 6)

        Text("Sides", style = MaterialTheme.typography.titleMedium)
        Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            listOf(4, 6, 8, 10, 12, 20).forEach { f ->
                FilterChip(
                    selected = faces == f,
                    onClick = { faces = f },
                    label = { Text("d$f") },
                )
            }
        }

        Button(
            onClick = { roll() },
            modifier = Modifier.fillMaxWidth().height(56.dp),
            shape = RoundedCornerShape(28.dp),
        ) { Text("Roll", style = MaterialTheme.typography.titleMedium) }
    }
}

/** Wraps dice onto rows without pulling in an experimental FlowRow. */
@Composable
private fun FlowRowSimple(rolls: List<Int>, faces: Int, rollId: Int = 0) {
    Column(
        verticalArrangement = Arrangement.spacedBy(12.dp),
        horizontalAlignment = Alignment.CenterHorizontally,
    ) {
        rolls.chunked(4).forEach { row ->
            Row(horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                row.forEach { Die(it, faces, rollId) }
            }
        }
    }
}

// ---------------------------------------------------------------- coin

@Composable
fun CoinScreen(modifier: Modifier = Modifier) {
    var result by remember { mutableStateOf("") }
    var flipping by remember { mutableStateOf(false) }
    val scope = rememberCoroutineScope()
    val spin = remember { Animatable(0f) }

    fun flip() {
        if (flipping) return
        scope.launch {
            flipping = true
            val res = withContext(Dispatchers.IO) { Core.run("Coin", "") }
            spin.snapTo(0f)
            // Land on a whole number of half-turns so the face is square to the viewer.
            spin.animateTo(1800f, tween(900, easing = FastOutSlowInEasing))
            result = if (res.ok) res.text else ""
            flipping = false
        }
    }

    Column(
        modifier = modifier.fillMaxSize().padding(24.dp),
        verticalArrangement = Arrangement.Center,
        horizontalAlignment = Alignment.CenterHorizontally,
    ) {
        Surface(
            shape = androidx.compose.foundation.shape.CircleShape,
            color = MaterialTheme.colorScheme.primaryContainer,
            modifier = Modifier.size(180.dp).graphicsLayer {
                rotationY = spin.value
                cameraDistance = 16f * density
            },
        ) {
            Box(Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
                Text(
                    result.ifEmpty { "?" },
                    style = MaterialTheme.typography.headlineMedium,
                    fontWeight = FontWeight.Bold,
                    color = MaterialTheme.colorScheme.onPrimaryContainer,
                    textAlign = TextAlign.Center,
                )
            }
        }
        Spacer(Modifier.height(40.dp))
        Button(
            onClick = { flip() },
            enabled = !flipping,
            modifier = Modifier.fillMaxWidth().height(56.dp),
            shape = RoundedCornerShape(28.dp),
        ) { Text("Flip", style = MaterialTheme.typography.titleMedium) }
    }
}

// ---------------------------------------------------------------- cards

@Composable
private fun PlayingCard(card: String) {
    // Suit is the last character; hearts and diamonds are red, as on a real deck.
    val suit = card.lastOrNull()?.toString() ?: ""
    val rank = card.dropLast(1)
    val red = suit == "♥" || suit == "♦"
    Surface(
        shape = RoundedCornerShape(12.dp),
        color = Color.White,
        border = androidx.compose.foundation.BorderStroke(1.dp, Color(0xFFBDBDBD)),
        modifier = Modifier.size(width = 64.dp, height = 92.dp),
    ) {
        Box(Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
            Column(horizontalAlignment = Alignment.CenterHorizontally) {
                Text(
                    rank,
                    style = MaterialTheme.typography.titleMedium,
                    fontWeight = FontWeight.Bold,
                    color = if (red) Color(0xFFD32F2F) else Color(0xFF212121),
                )
                Text(
                    suit,
                    style = MaterialTheme.typography.headlineSmall,
                    color = if (red) Color(0xFFD32F2F) else Color(0xFF212121),
                )
            }
        }
    }
}

@Composable
fun CardsScreen(modifier: Modifier = Modifier) {
    var count by remember { mutableFloatStateOf(5f) }
    var cards by remember { mutableStateOf<List<String>>(emptyList()) }
    val scope = rememberCoroutineScope()

    fun draw() {
        scope.launch {
            val res = withContext(Dispatchers.IO) { Core.run("Cards", count.roundToInt().toString()) }
            cards = if (res.ok) res.text.split(" ").filter { it.isNotBlank() } else emptyList()
        }
    }

    LaunchedEffect(Unit) { draw() }

    Column(
        modifier = modifier.fillMaxSize().padding(24.dp),
        verticalArrangement = Arrangement.spacedBy(20.dp),
    ) {
        Surface(
            shape = RoundedCornerShape(28.dp),
            color = MaterialTheme.colorScheme.surfaceContainerHigh,
            modifier = Modifier.fillMaxWidth().weight(1f),
        ) {
            Box(Modifier.fillMaxSize().padding(20.dp), contentAlignment = Alignment.Center) {
                Column(
                    verticalArrangement = Arrangement.spacedBy(12.dp),
                    horizontalAlignment = Alignment.CenterHorizontally,
                ) {
                    cards.chunked(4).forEach { row ->
                        Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                            row.forEach { PlayingCard(it) }
                        }
                    }
                }
            }
        }

        Row(
            Modifier.fillMaxWidth(),
            horizontalArrangement = Arrangement.SpaceBetween,
            verticalAlignment = Alignment.CenterVertically,
        ) {
            Text("Cards", style = MaterialTheme.typography.titleMedium)
            Text(
                count.roundToInt().toString(),
                style = MaterialTheme.typography.titleMedium,
                color = MaterialTheme.colorScheme.primary,
            )
        }
        Slider(value = count, onValueChange = { count = it }, valueRange = 1f..12f, steps = 10)

        Button(
            onClick = { draw() },
            modifier = Modifier.fillMaxWidth().height(56.dp),
            shape = RoundedCornerShape(28.dp),
        ) { Text("Deal", style = MaterialTheme.typography.titleMedium) }
    }
}
