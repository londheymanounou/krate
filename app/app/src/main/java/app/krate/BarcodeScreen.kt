package app.krate

import androidx.compose.foundation.Canvas
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext

/**
 * Code 128 as a real image, for the same reason as [QrScreen]: the core emits Unicode blocks, which
 * a proportional font stretches into something no scanner will read.
 *
 * Note the convention is the **opposite** of the QR renderer: here `'█'` is literally the bar, and
 * a space is a space (`codes.rs` writes `if bar { '█' } else { ' ' }`). The QR path prints for a
 * light background where a dark module is the gap. Same file, two conventions — do not copy the
 * decode from one to the other.
 */
@Composable
fun BarcodeScreen(modifier: Modifier = Modifier) {
    var text by remember { mutableStateOf("") }
    var bars by remember { mutableStateOf<List<Boolean>>(emptyList()) }
    var error by remember { mutableStateOf("") }
    val scope = rememberCoroutineScope()

    Column(
        modifier = modifier.fillMaxSize().padding(24.dp),
        verticalArrangement = Arrangement.spacedBy(20.dp),
    ) {
        OutlinedTextField(
            value = text,
            onValueChange = { value ->
                text = value
                scope.launch {
                    if (value.isBlank()) {
                        bars = emptyList(); error = ""
                        return@launch
                    }
                    val res = withContext(Dispatchers.IO) { Core.run("Barcode", value) }
                    if (res.ok) {
                        bars = res.text.lineSequence().first().map { it == '█' }
                        error = ""
                    } else {
                        bars = emptyList(); error = res.text
                    }
                }
            },
            label = { Text("Text (printable ASCII)") },
            modifier = Modifier.fillMaxWidth(),
            singleLine = true,
            shape = RoundedCornerShape(28.dp),
        )

        Box(Modifier.fillMaxWidth().weight(1f), contentAlignment = Alignment.Center) {
            when {
                bars.isNotEmpty() -> Column(horizontalAlignment = Alignment.CenterHorizontally) {
                    Box(
                        Modifier.fillMaxWidth().height(160.dp)
                            .clip(RoundedCornerShape(16.dp))
                            .background(Color.White)
                            .padding(horizontal = 12.dp, vertical = 16.dp),
                    ) {
                        Canvas(Modifier.fillMaxSize()) {
                            val w = size.width / bars.size
                            bars.forEachIndexed { i, dark ->
                                if (dark) {
                                    drawRect(Color.Black, Offset(i * w, 0f), Size(w + 0.5f, size.height))
                                }
                            }
                        }
                    }
                    Spacer(Modifier.height(12.dp))
                    Text(text, style = MaterialTheme.typography.bodyMedium)
                }
                error.isNotEmpty() -> Text(
                    error,
                    color = MaterialTheme.colorScheme.error,
                    textAlign = TextAlign.Center,
                )
                else -> Text(
                    "Type something to generate a barcode",
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                    textAlign = TextAlign.Center,
                )
            }
        }
    }
}
