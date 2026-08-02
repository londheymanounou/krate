@file:OptIn(androidx.compose.material3.ExperimentalMaterial3ExpressiveApi::class)

package app.krate

import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.rounded.ContentCopy
import androidx.compose.material.icons.rounded.Refresh
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalClipboardManager
import androidx.compose.ui.text.AnnotatedString
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import kotlin.math.roundToInt

@Composable
fun PasswordScreen(modifier: Modifier = Modifier) {
    var length by remember { mutableFloatStateOf(20f) }
    var upper by remember { mutableStateOf(true) }
    var lower by remember { mutableStateOf(true) }
    var digits by remember { mutableStateOf(true) }
    var symbols by remember { mutableStateOf(true) }
    var password by remember { mutableStateOf("") }
    val clipboard = LocalClipboardManager.current

    // At least one class must stay on, or the core refuses and the screen would just show an error
    // the user cannot act on. Guarding here keeps the last enabled toggle from turning itself off.
    val enabledCount = listOf(upper, lower, digits, symbols).count { it }
    fun toggleGuard(current: Boolean) = !(current && enabledCount == 1)

    fun regenerate() {
        password = Core.password(length.roundToInt(), upper, lower, digits, symbols) ?: ""
    }

    LaunchedEffect(length, upper, lower, digits, symbols) { regenerate() }

    Column(
        modifier = modifier.fillMaxSize().padding(24.dp),
        verticalArrangement = Arrangement.spacedBy(20.dp),
    ) {
        Surface(
            shape = RoundedCornerShape(28.dp),
            color = MaterialTheme.colorScheme.secondaryContainer,
            modifier = Modifier.fillMaxWidth(),
        ) {
            Column(Modifier.padding(24.dp), horizontalAlignment = Alignment.CenterHorizontally) {
                Text(
                    text = password.ifEmpty { "…" },
                    style = MaterialTheme.typography.titleLarge,
                    fontFamily = FontFamily.Monospace,
                    textAlign = TextAlign.Center,
                    color = MaterialTheme.colorScheme.onSecondaryContainer,
                )
                Spacer(Modifier.height(16.dp))
                Row(horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                    FilledTonalButton(onClick = { regenerate() }) {
                        Icon(Icons.Rounded.Refresh, null, Modifier.size(18.dp))
                        Spacer(Modifier.width(8.dp))
                        Text("Regenerate")
                    }
                    FilledTonalButton(
                        onClick = { if (password.isNotEmpty()) clipboard.setText(AnnotatedString(password)) }
                    ) {
                        Icon(Icons.Rounded.ContentCopy, null, Modifier.size(18.dp))
                        Spacer(Modifier.width(8.dp))
                        Text("Copy")
                    }
                }
            }
        }

        Row(
            Modifier.fillMaxWidth(),
            horizontalArrangement = Arrangement.SpaceBetween,
            verticalAlignment = Alignment.CenterVertically,
        ) {
            Text("Length", style = MaterialTheme.typography.titleMedium)
            Text(
                length.roundToInt().toString(),
                style = MaterialTheme.typography.titleMedium,
                color = MaterialTheme.colorScheme.primary,
            )
        }
        Slider(
            value = length,
            onValueChange = { length = it },
            valueRange = 4f..64f,
            modifier = Modifier.fillMaxWidth(),
        )

        Text("Characters", style = MaterialTheme.typography.titleMedium)
        CharToggle("Uppercase  A–Z", upper) { if (toggleGuard(upper)) upper = !upper }
        CharToggle("Lowercase  a–z", lower) { if (toggleGuard(lower)) lower = !lower }
        CharToggle("Digits  0–9", digits) { if (toggleGuard(digits)) digits = !digits }
        CharToggle("Symbols  !@#\$", symbols) { if (toggleGuard(symbols)) symbols = !symbols }
    }
}

@Composable
private fun CharToggle(label: String, checked: Boolean, onToggle: () -> Unit) {
    Row(
        Modifier.fillMaxWidth(),
        horizontalArrangement = Arrangement.SpaceBetween,
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Text(label, style = MaterialTheme.typography.bodyLarge)
        Switch(checked = checked, onCheckedChange = { onToggle() })
    }
}
