package app.krate

import androidx.compose.animation.core.animateFloatAsState
import androidx.compose.animation.core.spring
import androidx.compose.foundation.background
import androidx.compose.foundation.gestures.detectDragGestures
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.rounded.Refresh
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import kotlin.math.abs
import kotlin.random.Random

private const val SIZE = 4

/**
 * 2048.
 *
 * The core has no implementation — the catalogue delegates games to each shell — so this is the
 * Android shell's own, like Snake.
 *
 * ponytail: `kotlin.random` for tile spawning, not the core CSPRNG. Same reasoning as Snake: this
 * is a game board, not a value the user keeps.
 */
private fun slide(row: List<Int>): Pair<List<Int>, Int> {
    val tiles = row.filter { it != 0 }
    val merged = mutableListOf<Int>()
    var gained = 0
    var i = 0
    while (i < tiles.size) {
        // Merge a pair once, then move past both — 4,4,4 becomes 8,4 rather than 16.
        if (i + 1 < tiles.size && tiles[i] == tiles[i + 1]) {
            val value = tiles[i] * 2
            merged.add(value)
            gained += value
            i += 2
        } else {
            merged.add(tiles[i])
            i++
        }
    }
    return merged + List(SIZE - merged.size) { 0 } to gained
}

private fun rotate(board: List<List<Int>>): List<List<Int>> =
    List(SIZE) { r -> List(SIZE) { c -> board[SIZE - 1 - c][r] } }

/** Applies a move by rotating the board so every direction reduces to a left-slide. */
private fun move(board: List<List<Int>>, turns: Int): Pair<List<List<Int>>, Int> {
    var b = board
    repeat(turns) { b = rotate(b) }
    var gained = 0
    b = b.map { row -> slide(row).also { gained += it.second }.first }
    repeat((4 - turns) % 4) { b = rotate(b) }
    return b to gained
}

private fun spawn(board: List<List<Int>>): List<List<Int>> {
    val empty = board.indices.flatMap { r -> board[r].indices.filter { board[r][it] == 0 }.map { r to it } }
    if (empty.isEmpty()) return board
    val (r, c) = empty[Random.nextInt(empty.size)]
    // Nine 2s for every 4, as the original does.
    val value = if (Random.nextInt(10) == 0) 4 else 2
    return board.mapIndexed { i, row -> row.mapIndexed { j, v -> if (i == r && j == c) value else v } }
}

private fun stuck(board: List<List<Int>>): Boolean {
    if (board.any { row -> row.any { it == 0 } }) return false
    for (r in 0 until SIZE) {
        for (c in 0 until SIZE) {
            if (c + 1 < SIZE && board[r][c] == board[r][c + 1]) return false
            if (r + 1 < SIZE && board[r][c] == board[r + 1][c]) return false
        }
    }
    return true
}

@Composable
private fun tileColour(value: Int): Pair<Color, Color> {
    // Derived from the theme rather than the original's fixed browns, so it follows the wallpaper.
    val scheme = MaterialTheme.colorScheme
    return when (value) {
        0 -> scheme.surfaceContainerHighest to scheme.onSurface
        2 -> scheme.surfaceContainerHigh to scheme.onSurface
        4 -> scheme.secondaryContainer to scheme.onSecondaryContainer
        8 -> scheme.tertiaryContainer to scheme.onTertiaryContainer
        16 -> scheme.primaryContainer to scheme.onPrimaryContainer
        32 -> scheme.primary to scheme.onPrimary
        64 -> scheme.tertiary to scheme.onTertiary
        else -> scheme.error to scheme.onError
    }
}

@Composable
fun Game2048Screen(modifier: Modifier = Modifier) {
    var board by remember { mutableStateOf(spawn(spawn(List(SIZE) { List(SIZE) { 0 } }))) }
    var score by remember { mutableIntStateOf(0) }
    var best by remember { mutableIntStateOf(0) }
    var over by remember { mutableStateOf(false) }

    fun apply(turns: Int) {
        if (over) return
        val (next, gained) = move(board, turns)
        // A move that changes nothing must not spawn a tile, or the board fills from dead swipes.
        if (next == board) return
        score += gained
        if (score > best) best = score
        board = spawn(next)
        if (stuck(board)) over = true
    }

    fun reset() {
        board = spawn(spawn(List(SIZE) { List(SIZE) { 0 } }))
        score = 0
        over = false
    }

    Column(
        modifier.fillMaxSize().padding(20.dp),
        verticalArrangement = Arrangement.spacedBy(16.dp),
        horizontalAlignment = Alignment.CenterHorizontally,
    ) {
        Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween) {
            Text("Score  $score", style = MaterialTheme.typography.titleMedium, fontWeight = FontWeight.Bold)
            Text(
                "Best  $best",
                style = MaterialTheme.typography.titleMedium,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
            )
        }

        Box(
            Modifier
                .fillMaxWidth()
                .aspectRatio(1f)
                .clip(RoundedCornerShape(20.dp))
                .background(MaterialTheme.colorScheme.surfaceContainer)
                .padding(8.dp)
                .pointerInput(over) {
                    detectDragGestures(
                        onDragEnd = {},
                    ) { _, drag ->
                        // Dominant axis decides; turns map a direction onto the left-slide.
                        if (abs(drag.x) > abs(drag.y)) {
                            if (drag.x > 6f) apply(2) else if (drag.x < -6f) apply(0)
                        } else {
                            if (drag.y > 6f) apply(1) else if (drag.y < -6f) apply(3)
                        }
                    }
                },
        ) {
            Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
                board.forEach { row ->
                    Row(
                        Modifier.weight(1f),
                        horizontalArrangement = Arrangement.spacedBy(8.dp),
                    ) {
                        row.forEach { value ->
                            val (bg, fg) = tileColour(value)
                            // New and merged tiles pop in rather than appearing, which is what makes
                            // the board readable while it changes.
                            val scale by animateFloatAsState(
                                targetValue = if (value == 0) 0.86f else 1f,
                                animationSpec = spring(dampingRatio = 0.5f, stiffness = 700f),
                                label = "tile",
                            )
                            Box(
                                Modifier
                                    .weight(1f)
                                    .fillMaxHeight()
                                    .graphicsLayer { scaleX = scale; scaleY = scale }
                                    .clip(RoundedCornerShape(12.dp))
                                    .background(bg),
                                contentAlignment = Alignment.Center,
                            ) {
                                if (value != 0) {
                                    Text(
                                        value.toString(),
                                        color = fg,
                                        fontWeight = FontWeight.Bold,
                                        fontSize = when {
                                            value < 100 -> 30.sp
                                            value < 1000 -> 24.sp
                                            else -> 19.sp
                                        },
                                    )
                                }
                            }
                        }
                    }
                }
            }
        }

        if (over) {
            Text(
                "No moves left",
                style = MaterialTheme.typography.titleLarge,
                fontWeight = FontWeight.Bold,
                color = MaterialTheme.colorScheme.error,
            )
        } else {
            Text(
                "Swipe to move tiles",
                style = MaterialTheme.typography.bodyMedium,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
            )
        }

        FilledTonalButton(onClick = { reset() }, shape = RoundedCornerShape(24.dp)) {
            Icon(Icons.Rounded.Refresh, null, Modifier.size(18.dp))
            Spacer(Modifier.width(8.dp))
            Text("New game")
        }
    }
}
