package app.krate

import androidx.compose.foundation.layout.*
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.drawscope.DrawScope
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.background
import androidx.compose.foundation.shape.RoundedCornerShape
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext

/**
 * The core renders a QR as Unicode half-blocks — two module rows per character — which is right for
 * a terminal and unreadable on a phone: a proportional font stretches it, and no scanner will ever
 * read text. So the block art is decoded back to the module matrix and drawn as real squares.
 *
 * The decode is lossless because the encoding is a bijection over the four (top, bottom) states.
 * Note the inversion: the core prints for a *light* background, where a dark module is the gap, so
 * `'█'` (both light) means both modules are dark. Getting this backwards produces a QR that looks
 * plausible and scans as nothing.
 */
private fun decodeBlocks(art: String): List<List<Boolean>> {
    val rows = art.split("\n").filter { it.isNotEmpty() }
    val out = mutableListOf<List<Boolean>>()
    for (line in rows) {
        val top = mutableListOf<Boolean>()
        val bottom = mutableListOf<Boolean>()
        for (ch in line) {
            when (ch) {
                '█' -> { top += false; bottom += false } // full block -> both modules LIGHT
                '▀' -> { top += false; bottom += true }  // upper half -> bottom module dark
                '▄' -> { top += true; bottom += false }  // lower half -> top module dark
                else -> { top += true; bottom += true }  // space      -> both modules DARK
            }
        }
        out += top
        out += bottom
    }
    return out
}

/** Draws the matrix to fill [size], snapping to whole pixels so modules stay crisp and equal. */
private fun DrawScope.drawQr(matrix: List<List<Boolean>>, dark: Color) {
    if (matrix.isEmpty()) return
    val side = minOf(size.width, size.height)
    val n = matrix.size
    // Integer module size: a fractional one leaves seams between modules that scanners read as noise.
    val module = kotlin.math.floor(side / n)
    if (module < 1f) return
    val drawn = module * n
    val ox = (size.width - drawn) / 2f
    val oy = (size.height - drawn) / 2f
    for (y in 0 until n) {
        val row = matrix[y]
        for (x in row.indices) {
            if (row[x]) {
                drawRect(
                    color = dark,
                    topLeft = Offset(ox + x * module, oy + y * module),
                    size = Size(module, module),
                )
            }
        }
    }
}

@Composable
fun QrScreen(modifier: Modifier = Modifier) {
    var text by remember { mutableStateOf("") }
    var matrix by remember { mutableStateOf<List<List<Boolean>>>(emptyList()) }
    var error by remember { mutableStateOf("") }
    val scope = rememberCoroutineScope()

    Column(
        modifier = modifier.fillMaxSize().padding(24.dp),
        verticalArrangement = Arrangement.spacedBy(20.dp),
        horizontalAlignment = Alignment.CenterHorizontally,
    ) {
        OutlinedTextField(
            value = text,
            onValueChange = { value ->
                text = value
                scope.launch {
                    if (value.isBlank()) {
                        matrix = emptyList()
                        error = ""
                        return@launch
                    }
                    val res = withContext(Dispatchers.IO) { Core.run("Qr", value) }
                    if (res.ok) {
                        matrix = decodeBlocks(res.text)
                        error = ""
                    } else {
                        matrix = emptyList()
                        error = res.text
                    }
                }
            },
            label = { Text("Text or URL") },
            modifier = Modifier.fillMaxWidth(),
            shape = RoundedCornerShape(28.dp),
        )

        Box(
            modifier = Modifier
                .fillMaxWidth()
                .weight(1f),
            contentAlignment = Alignment.Center,
        ) {
            when {
                matrix.isNotEmpty() -> {
                    // A QR must be drawn dark-on-light regardless of the app theme: scanners expect
                    // that contrast, and an inverted code is a code most readers refuse.
                    Box(
                        modifier = Modifier
                            .fillMaxWidth()
                            .aspectRatio(1f)
                            .clip(RoundedCornerShape(28.dp))
                            .background(Color.White)
                            .padding(16.dp),
                    ) {
                        Canvas(modifier = Modifier.fillMaxSize()) {
                            drawQr(matrix, Color.Black)
                        }
                    }
                }
                error.isNotEmpty() -> Text(
                    error,
                    color = MaterialTheme.colorScheme.error,
                    style = MaterialTheme.typography.bodyLarge,
                    textAlign = TextAlign.Center,
                )
                else -> Text(
                    "Type something to generate a QR code",
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                    style = MaterialTheme.typography.bodyLarge,
                    textAlign = TextAlign.Center,
                )
            }
        }
    }
}
