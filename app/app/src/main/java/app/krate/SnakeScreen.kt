package app.krate

import androidx.compose.foundation.Canvas
import androidx.compose.foundation.gestures.detectDragGestures
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.rounded.PlayArrow
import androidx.compose.material.icons.rounded.Refresh
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.geometry.CornerRadius
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import kotlinx.coroutines.delay
import kotlin.math.abs
import kotlin.random.Random

private const val COLS = 15
private const val ROWS = 20

private data class Cell(val x: Int, val y: Int)

/**
 * Snake.
 *
 * The core has no snake implementation — `Tool { id: "Snake" }` is delegated to the shell, which is
 * why the desktop has its own. So this is the Android shell's copy, not a duplicate of core logic.
 *
 * ponytail: `kotlin.random` for food placement, not the core's CSPRNG. Every other random tool goes
 * through the core because the *result* is the product; here it is a game board, and an FFI call
 * per apple buys nothing. Do not copy this exception to anything that produces a value the user
 * keeps.
 */
@Composable
fun SnakeScreen(modifier: Modifier = Modifier) {
    var snake by remember { mutableStateOf(listOf(Cell(COLS / 2, ROWS / 2))) }
    var dir by remember { mutableStateOf(Cell(0, -1)) }
    var food by remember { mutableStateOf(Cell(COLS / 2, ROWS / 2 - 5)) }
    var running by remember { mutableStateOf(false) }
    var dead by remember { mutableStateOf(false) }
    var score by remember { mutableIntStateOf(0) }
    var best by remember { mutableIntStateOf(0) }

    fun reset() {
        snake = listOf(Cell(COLS / 2, ROWS / 2))
        dir = Cell(0, -1)
        food = Cell(Random.nextInt(COLS), Random.nextInt(ROWS))
        score = 0
        dead = false
        running = true
    }

    LaunchedEffect(running, dead) {
        while (running && !dead) {
            // Speeds up as the snake grows, which is the whole difficulty curve.
            delay((190L - score * 4).coerceAtLeast(70L))
            val head = snake.first()
            val next = Cell(head.x + dir.x, head.y + dir.y)

            val hitWall = next.x !in 0 until COLS || next.y !in 0 until ROWS
            // The tail tip is excluded: it moves out of the way on the same tick, so following your
            // own tail exactly is legal rather than instant death.
            val hitSelf = next in snake.dropLast(1)
            if (hitWall || hitSelf) {
                dead = true
                running = false
                if (score > best) best = score
                break
            }

            val ate = next == food
            snake = if (ate) listOf(next) + snake else listOf(next) + snake.dropLast(1)
            if (ate) {
                score++
                // Never place food under the snake, or it becomes unreachable.
                var candidate: Cell
                do {
                    candidate = Cell(Random.nextInt(COLS), Random.nextInt(ROWS))
                } while (candidate in snake)
                food = candidate
            }
        }
    }

    val snakeColor = MaterialTheme.colorScheme.primary
    val headColor = MaterialTheme.colorScheme.onPrimaryContainer
    val foodColor = MaterialTheme.colorScheme.error
    val boardColor = MaterialTheme.colorScheme.surfaceContainerHigh

    Column(
        modifier.fillMaxSize().padding(16.dp),
        verticalArrangement = Arrangement.spacedBy(12.dp),
        horizontalAlignment = Alignment.CenterHorizontally,
    ) {
        Row(
            Modifier.fillMaxWidth(),
            horizontalArrangement = Arrangement.SpaceBetween,
        ) {
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
                .weight(1f)
                .pointerInput(Unit) {
                    detectDragGestures { _, drag ->
                        // Dominant axis wins, and a reversal into your own neck is ignored rather
                        // than being an instant loss.
                        val d = if (abs(drag.x) > abs(drag.y)) {
                            Cell(if (drag.x > 0) 1 else -1, 0)
                        } else {
                            Cell(0, if (drag.y > 0) 1 else -1)
                        }
                        if (d.x != -dir.x || d.y != -dir.y) dir = d
                    }
                },
            contentAlignment = Alignment.Center,
        ) {
            Canvas(Modifier.fillMaxSize()) {
                val cell = minOf(size.width / COLS, size.height / ROWS)
                val ox = (size.width - cell * COLS) / 2f
                val oy = (size.height - cell * ROWS) / 2f

                drawRoundRect(
                    color = boardColor,
                    topLeft = Offset(ox, oy),
                    size = Size(cell * COLS, cell * ROWS),
                    cornerRadius = CornerRadius(24f, 24f),
                )
                drawRoundRect(
                    color = foodColor,
                    topLeft = Offset(ox + food.x * cell + cell * 0.15f, oy + food.y * cell + cell * 0.15f),
                    size = Size(cell * 0.7f, cell * 0.7f),
                    cornerRadius = CornerRadius(cell / 2f, cell / 2f),
                )
                snake.forEachIndexed { i, c ->
                    drawRoundRect(
                        color = if (i == 0) headColor else snakeColor,
                        topLeft = Offset(ox + c.x * cell + cell * 0.06f, oy + c.y * cell + cell * 0.06f),
                        size = Size(cell * 0.88f, cell * 0.88f),
                        cornerRadius = CornerRadius(cell * 0.3f, cell * 0.3f),
                    )
                }
            }

            if (!running) {
                Surface(
                    shape = RoundedCornerShape(28.dp),
                    color = MaterialTheme.colorScheme.secondaryContainer,
                    tonalElevation = 6.dp,
                ) {
                    Column(
                        Modifier.padding(28.dp),
                        horizontalAlignment = Alignment.CenterHorizontally,
                    ) {
                        Text(
                            if (dead) "Game over" else "Snake",
                            style = MaterialTheme.typography.headlineSmall,
                            fontWeight = FontWeight.Bold,
                            color = MaterialTheme.colorScheme.onSecondaryContainer,
                        )
                        Spacer(Modifier.height(6.dp))
                        Text(
                            if (dead) "Score $score" else "Swipe to steer",
                            style = MaterialTheme.typography.bodyMedium,
                            color = MaterialTheme.colorScheme.onSecondaryContainer,
                        )
                        Spacer(Modifier.height(16.dp))
                        Button(onClick = { reset() }, shape = RoundedCornerShape(24.dp)) {
                            Icon(
                                if (dead) Icons.Rounded.Refresh else Icons.Rounded.PlayArrow,
                                null,
                                Modifier.size(18.dp),
                            )
                            Spacer(Modifier.width(8.dp))
                            Text(if (dead) "Play again" else "Play")
                        }
                    }
                }
            }
        }
    }
}
