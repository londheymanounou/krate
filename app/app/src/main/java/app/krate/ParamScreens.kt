package app.krate

import androidx.compose.foundation.layout.*
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.compose.foundation.text.KeyboardOptions
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import kotlin.math.roundToInt

/**
 * Screens for tools whose core input is a *composed string* — `"1 100"`, `"3; alice, bob"`.
 *
 * That format is fine on a CLI where it is documented and typed once. On a phone it is a guess:
 * nothing on screen says a space separates the bounds, or that the leading number is the team
 * count. These build the string from real controls instead. The core contract is untouched — the
 * screen composes the same text the CLI would take, so parity holds.
 */

/** Shared result panel, so these screens stay short. */
@Composable
private fun ResultPanel(text: String, isError: Boolean, placeholder: String, modifier: Modifier = Modifier) {
    Surface(
        shape = RoundedCornerShape(28.dp),
        color = if (isError) MaterialTheme.colorScheme.errorContainer
        else MaterialTheme.colorScheme.surfaceContainerHigh,
        modifier = modifier,
    ) {
        Box(Modifier.fillMaxSize().padding(24.dp), contentAlignment = Alignment.Center) {
            Text(
                text.ifEmpty { placeholder },
                style = MaterialTheme.typography.titleLarge,
                textAlign = TextAlign.Center,
                color = if (isError) MaterialTheme.colorScheme.onErrorContainer
                else MaterialTheme.colorScheme.onSurface,
                modifier = Modifier.verticalScroll(rememberScrollState()),
            )
        }
    }
}

/** `Random` takes "min max"; two number fields beat asking the user to know that. */
@Composable
fun RandomNumberScreen(modifier: Modifier = Modifier) {
    var min by remember { mutableStateOf("1") }
    var max by remember { mutableStateOf("100") }
    var out by remember { mutableStateOf("") }
    var err by remember { mutableStateOf(false) }
    val scope = rememberCoroutineScope()

    fun roll() = scope.launch {
        val res = withContext(Dispatchers.IO) { Core.run("Random", "${min.trim()} ${max.trim()}") }
        out = res.text; err = !res.ok
    }
    LaunchedEffect(Unit) { roll() }

    Column(
        modifier.fillMaxSize().padding(24.dp),
        verticalArrangement = Arrangement.spacedBy(20.dp),
    ) {
        ResultPanel(out, err, "—", Modifier.fillMaxWidth().weight(1f))
        Row(horizontalArrangement = Arrangement.spacedBy(12.dp)) {
            OutlinedTextField(
                value = min, onValueChange = { min = it }, label = { Text("Minimum") },
                keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
                singleLine = true, shape = RoundedCornerShape(24.dp), modifier = Modifier.weight(1f),
            )
            OutlinedTextField(
                value = max, onValueChange = { max = it }, label = { Text("Maximum") },
                keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
                singleLine = true, shape = RoundedCornerShape(24.dp), modifier = Modifier.weight(1f),
            )
        }
        Button(
            onClick = { roll() },
            modifier = Modifier.fillMaxWidth().height(56.dp),
            shape = RoundedCornerShape(28.dp),
        ) { Text("Generate", style = MaterialTheme.typography.titleMedium) }
    }
}

/**
 * `Teams` takes "3; alice, bob, carol" — a lone number anywhere is the team count. Splitting the
 * count onto a slider removes the one genuinely surprising rule: that a name which happens to be a
 * number would be eaten as the team count.
 */
@Composable
fun TeamsScreen(modifier: Modifier = Modifier) {
    var names by remember { mutableStateOf("") }
    var count by remember { mutableStateOf("2") }
    var out by remember { mutableStateOf("") }
    var err by remember { mutableStateOf(false) }
    val scope = rememberCoroutineScope()

    fun split() = scope.launch {
        val n = count.toIntOrNull() ?: 2
        val res = withContext(Dispatchers.IO) { Core.run("Teams", "$n; ${names.trim()}") }
        out = res.text; err = !res.ok
    }

    Column(
        modifier.fillMaxSize().padding(24.dp),
        verticalArrangement = Arrangement.spacedBy(16.dp),
    ) {
        OutlinedTextField(
            value = names,
            onValueChange = { names = it },
            label = { Text("Names, one per line or comma-separated") },
            modifier = Modifier.fillMaxWidth().height(140.dp),
            shape = RoundedCornerShape(24.dp),
        )
        // ponytail: replaced slider with a minimum viable textfield for integer input
        OutlinedTextField(
            value = count,
            onValueChange = { count = it.filter { c -> c.isDigit() } },
            label = { Text("Number of teams") },
            modifier = Modifier.fillMaxWidth(),
            shape = RoundedCornerShape(24.dp),
            keyboardOptions = androidx.compose.foundation.text.KeyboardOptions(keyboardType = androidx.compose.ui.text.input.KeyboardType.Number)
        )
        Button(
            onClick = { split() },
            enabled = names.isNotBlank(),
            modifier = Modifier.fillMaxWidth().height(56.dp),
            shape = RoundedCornerShape(28.dp),
        ) { Text("Split", style = MaterialTheme.typography.titleMedium) }
        ResultPanel(out, err, "Teams appear here", Modifier.fillMaxWidth().weight(1f))
    }
}

/** `Pick` and `Shuffle` both take a list; the only difference is which one is run. */
@Composable
fun ListToolScreen(id: String, modifier: Modifier = Modifier) {
    var items by remember { mutableStateOf("") }
    var out by remember { mutableStateOf("") }
    var err by remember { mutableStateOf(false) }
    val scope = rememberCoroutineScope()

    fun run() = scope.launch {
        val res = withContext(Dispatchers.IO) { Core.run(id, items.trim()) }
        out = res.text; err = !res.ok
    }

    Column(
        modifier.fillMaxSize().padding(24.dp),
        verticalArrangement = Arrangement.spacedBy(16.dp),
    ) {
        OutlinedTextField(
            value = items,
            onValueChange = { items = it },
            label = { Text("Items, one per line or comma-separated") },
            modifier = Modifier.fillMaxWidth().height(160.dp),
            shape = RoundedCornerShape(24.dp),
        )
        Button(
            onClick = { run() },
            enabled = items.isNotBlank(),
            modifier = Modifier.fillMaxWidth().height(56.dp),
            shape = RoundedCornerShape(28.dp),
        ) {
            Text(
                if (id == "Pick") "Pick one" else "Shuffle",
                style = MaterialTheme.typography.titleMedium,
            )
        }
        ResultPanel(out, err, "Result", Modifier.fillMaxWidth().weight(1f))
    }
}
