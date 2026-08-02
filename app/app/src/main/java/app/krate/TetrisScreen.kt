package app.krate

import androidx.compose.foundation.Canvas
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.rounded.KeyboardArrowDown
import androidx.compose.material.icons.rounded.KeyboardArrowLeft
import androidx.compose.material.icons.rounded.KeyboardArrowRight
import androidx.compose.material.icons.rounded.PlayArrow
import androidx.compose.material.icons.rounded.Refresh
import androidx.compose.material.icons.rounded.RotateRight
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.geometry.CornerRadius
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import kotlinx.coroutines.delay
import kotlin.random.Random

private const val COLS = 10
private const val ROWS = 20

/**
 * Tetris.
 *
 * Pieces are stored as coordinate lists per rotation rather than as matrices to spin at runtime:
 * the I and O pieces do not rotate about their centre, and computing that correctly is fiddlier
 * than writing the four states out. `1` is the colour index, so a locked cell remembers its piece.
 */
private val PIECES: List<List<List<Pair<Int, Int>>>> = listOf(
    // I
    listOf(
        listOf(0 to 1, 1 to 1, 2 to 1, 3 to 1),
        listOf(2 to 0, 2 to 1, 2 to 2, 2 to 3),
    ),
    // O
    listOf(listOf(1 to 0, 2 to 0, 1 to 1, 2 to 1)),
    // T
    listOf(
        listOf(1 to 0, 0 to 1, 1 to 1, 2 to 1),
        listOf(1 to 0, 1 to 1, 2 to 1, 1 to 2),
        listOf(0 to 1, 1 to 1, 2 to 1, 1 to 2),
        listOf(1 to 0, 0 to 1, 1 to 1, 1 to 2),
    ),
    // S
    listOf(
        listOf(1 to 0, 2 to 0, 0 to 1, 1 to 1),
        listOf(1 to 0, 1 to 1, 2 to 1, 2 to 2),
    ),
    // Z
    listOf(
        listOf(0 to 0, 1 to 0, 1 to 1, 2 to 1),
        listOf(2 to 0, 1 to 1, 2 to 1, 1 to 2),
    ),
    // J
    listOf(
        listOf(0 to 0, 0 to 1, 1 to 1, 2 to 1),
        listOf(1 to 0, 2 to 0, 1 to 1, 1 to 2),
        listOf(0 to 1, 1 to 1, 2 to 1, 2 to 2),
        listOf(1 to 0, 1 to 1, 0 to 2, 1 to 2),
    ),
    // L
    listOf(
        listOf(2 to 0, 0 to 1, 1 to 1, 2 to 1),
        listOf(1 to 0, 1 to 1, 1 to 2, 2 to 2),
        listOf(0 to 1, 1 to 1, 2 to 1, 0 to 2),
        listOf(0 to 0, 1 to 0, 1 to 1, 1 to 2),
    ),
)

private data class Falling(val piece: Int, val rotation: Int, val x: Int, val y: Int) {
    fun cells(): List<Pair<Int, Int>> =
        PIECES[piece][rotation % PIECES[piece].size].map { (cx, cy) -> x + cx to y + cy }
}

@Composable
fun TetrisScreen(modifier: Modifier = Modifier) {
    // 0 is empty; anything else is 1 + the piece index, so a locked cell keeps its colour.
    var grid by remember { mutableStateOf(List(ROWS) { List(COLS) { 0 } }) }
    var current by remember { mutableStateOf<Falling?>(null) }
    var score by remember { mutableIntStateOf(0) }
    var lines by remember { mutableIntStateOf(0) }
    var running by remember { mutableStateOf(false) }
    var over by remember { mutableStateOf(false) }

    fun fits(f: Falling): Boolean = f.cells().all { (x, y) ->
        x in 0 until COLS && y < ROWS && (y < 0 || grid[y][x] == 0)
    }

    fun newPiece(): Falling = Falling(Random.nextInt(PIECES.size), 0, COLS / 2 - 2, 0)

    fun lock(f: Falling) {
        val next = grid.map { it.toMutableList() }.toMutableList()
        f.cells().forEach { (x, y) -> if (y in 0 until ROWS) next[y][x] = f.piece + 1 }
        // Clearing is "keep the rows that are not full, then pad from the top".
        val kept = next.filter { row -> row.any { it == 0 } }
        val cleared = ROWS - kept.size
        if (cleared > 0) {
            lines += cleared
            // Standard scoring: four at once is worth far more than four singles.
            score += when (cleared) { 1 -> 100; 2 -> 300; 3 -> 500; else -> 800 }
        }
        grid = List(cleared) { List(COLS) { 0 } } + kept.map { it.toList() }
        val spawned = newPiece()
        if (!fits(spawned)) { over = true; running = false; current = null } else current = spawned
    }

    fun step() {
        val f = current ?: return
        val down = f.copy(y = f.y + 1)
        if (fits(down)) current = down else lock(f)
    }

    fun nudge(dx: Int) {
        val f = current ?: return
        val moved = f.copy(x = f.x + dx)
        if (fits(moved)) current = moved
    }

    fun spin() {
        val f = current ?: return
        val turned = f.copy(rotation = f.rotation + 1)
        // Wall kick: if a rotation clips a wall, try shifting one or two cells before refusing.
        listOf(0, -1, 1, -2, 2).forEach { kick ->
            val candidate = turned.copy(x = turned.x + kick)
            if (fits(candidate)) { current = candidate; return }
        }
    }

    fun drop() {
        var f = current ?: return
        while (fits(f.copy(y = f.y + 1))) f = f.copy(y = f.y + 1)
        current = f
        lock(f)
    }

    fun reset() {
        grid = List(ROWS) { List(COLS) { 0 } }
        score = 0
        lines = 0
        over = false
        current = newPiece()
        running = true
    }

    LaunchedEffect(running, over) {
        while (running && !over) {
            // Speeds up every ten lines, which is the difficulty curve.
            delay((520L - lines / 10 * 60L).coerceAtLeast(120L))
            step()
        }
    }

    val palette = listOf(
        MaterialTheme.colorScheme.primary,
        MaterialTheme.colorScheme.tertiary,
        MaterialTheme.colorScheme.secondary,
        MaterialTheme.colorScheme.error,
        MaterialTheme.colorScheme.primaryContainer,
        MaterialTheme.colorScheme.tertiaryContainer,
        MaterialTheme.colorScheme.secondaryContainer,
    )
    val well = MaterialTheme.colorScheme.surfaceContainerHigh

    Column(
        modifier.fillMaxSize().padding(16.dp),
        verticalArrangement = Arrangement.spacedBy(10.dp),
        horizontalAlignment = Alignment.CenterHorizontally,
    ) {
        Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween) {
            Text("Score  $score", style = MaterialTheme.typography.titleMedium, fontWeight = FontWeight.Bold)
            Text(
                "Lines  $lines",
                style = MaterialTheme.typography.titleMedium,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
            )
        }

        Box(Modifier.weight(1f), contentAlignment = Alignment.Center) {
            Canvas(Modifier.fillMaxHeight().aspectRatio(COLS.toFloat() / ROWS)) {
                val cell = minOf(size.width / COLS, size.height / ROWS)
                drawRoundRect(well, size = size, cornerRadius = CornerRadius(16f, 16f))
                fun block(x: Int, y: Int, colour: Color) {
                    if (y < 0) return
                    drawRoundRect(
                        color = colour,
                        topLeft = Offset(x * cell + 1f, y * cell + 1f),
                        size = Size(cell - 2f, cell - 2f),
                        cornerRadius = CornerRadius(cell * 0.22f, cell * 0.22f),
                    )
                }
                grid.forEachIndexed { y, row ->
                    row.forEachIndexed { x, v -> if (v != 0) block(x, y, palette[(v - 1) % palette.size]) }
                }
                current?.let { f ->
                    f.cells().forEach { (x, y) -> block(x, y, palette[f.piece % palette.size]) }
                }
            }

            if (!running) {
                Surface(
                    shape = RoundedCornerShape(28.dp),
                    color = MaterialTheme.colorScheme.secondaryContainer,
                    tonalElevation = 6.dp,
                ) {
                    Column(Modifier.padding(26.dp), horizontalAlignment = Alignment.CenterHorizontally) {
                        Text(
                            if (over) "Game over" else "Tetris",
                            style = MaterialTheme.typography.headlineSmall,
                            fontWeight = FontWeight.Bold,
                            color = MaterialTheme.colorScheme.onSecondaryContainer,
                        )
                        Spacer(Modifier.height(14.dp))
                        Button(onClick = { reset() }, shape = RoundedCornerShape(24.dp)) {
                            Icon(
                                if (over) Icons.Rounded.Refresh else Icons.Rounded.PlayArrow,
                                null,
                                Modifier.size(18.dp),
                            )
                            Spacer(Modifier.width(8.dp))
                            Text(if (over) "Play again" else "Play")
                        }
                    }
                }
            }
        }

        // Buttons rather than swipes: Tetris needs repeated precise nudges, and a swipe gesture
        // cannot express "one cell left" reliably.
        Row(
            Modifier.fillMaxWidth().windowInsetsPadding(WindowInsets.navigationBars),
            horizontalArrangement = Arrangement.spacedBy(8.dp),
        ) {
            listOf(
                Icons.Rounded.KeyboardArrowLeft to { nudge(-1) },
                Icons.Rounded.RotateRight to { spin() },
                Icons.Rounded.KeyboardArrowRight to { nudge(1) },
                Icons.Rounded.KeyboardArrowDown to { drop() },
            ).forEach { (icon, action) ->
                FilledTonalIconButton(
                    onClick = action,
                    enabled = running && !over,
                    modifier = Modifier.weight(1f).height(56.dp),
                    shape = RoundedCornerShape(20.dp),
                ) { Icon(icon, null) }
            }
        }
    }
}
