package app.krate

import androidx.compose.foundation.layout.*
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp

@Composable
fun PercentScreen(modifier: Modifier = Modifier) {
    var a by remember { mutableStateOf("") }
    var b by remember { mutableStateOf("") }
    val result = remember(a, b) { Core.run("Percent", "$a $b") }

    Column(modifier = modifier.fillMaxSize().padding(16.dp).verticalScroll(rememberScrollState()), verticalArrangement = Arrangement.spacedBy(16.dp)) {
        Row(horizontalArrangement = Arrangement.spacedBy(16.dp)) {
            OutlinedTextField(
                value = a, onValueChange = { a = it }, label = { Text("Value A") },
                keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Decimal),
                modifier = Modifier.weight(1f), shape = CircleShape,
                colors = TextFieldDefaults.colors(focusedIndicatorColor = androidx.compose.ui.graphics.Color.Transparent, unfocusedIndicatorColor = androidx.compose.ui.graphics.Color.Transparent)
            )
            OutlinedTextField(
                value = b, onValueChange = { b = it }, label = { Text("Value B") },
                keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Decimal),
                modifier = Modifier.weight(1f), shape = CircleShape,
                colors = TextFieldDefaults.colors(focusedIndicatorColor = androidx.compose.ui.graphics.Color.Transparent, unfocusedIndicatorColor = androidx.compose.ui.graphics.Color.Transparent)
            )
        }
        ResultPanel(result)
    }
}

@Composable
fun SolveScreen(modifier: Modifier = Modifier) {
    var a by remember { mutableStateOf("") }
    var b by remember { mutableStateOf("") }
    var c by remember { mutableStateOf("") }
    val result = remember(a, b, c) { Core.run("Solve", "$a $b $c") }

    Column(modifier = modifier.fillMaxSize().padding(16.dp).verticalScroll(rememberScrollState()), verticalArrangement = Arrangement.spacedBy(16.dp)) {
        Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            OutlinedTextField(
                value = a, onValueChange = { a = it }, label = { Text("a") },
                keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Decimal),
                modifier = Modifier.weight(1f), shape = CircleShape,
                colors = TextFieldDefaults.colors(focusedIndicatorColor = androidx.compose.ui.graphics.Color.Transparent, unfocusedIndicatorColor = androidx.compose.ui.graphics.Color.Transparent)
            )
            OutlinedTextField(
                value = b, onValueChange = { b = it }, label = { Text("b") },
                keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Decimal),
                modifier = Modifier.weight(1f), shape = CircleShape,
                colors = TextFieldDefaults.colors(focusedIndicatorColor = androidx.compose.ui.graphics.Color.Transparent, unfocusedIndicatorColor = androidx.compose.ui.graphics.Color.Transparent)
            )
            OutlinedTextField(
                value = c, onValueChange = { c = it }, label = { Text("c") },
                keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Decimal),
                modifier = Modifier.weight(1f), shape = CircleShape,
                colors = TextFieldDefaults.colors(focusedIndicatorColor = androidx.compose.ui.graphics.Color.Transparent, unfocusedIndicatorColor = androidx.compose.ui.graphics.Color.Transparent)
            )
        }
        ResultPanel(result)
    }
}

@Composable
fun FactorScreen(modifier: Modifier = Modifier) {
    var a by remember { mutableStateOf("") }
    val result = remember(a) { Core.run("Factor", a) }

    Column(modifier = modifier.fillMaxSize().padding(16.dp).verticalScroll(rememberScrollState()), verticalArrangement = Arrangement.spacedBy(16.dp)) {
        OutlinedTextField(
            value = a, onValueChange = { a = it }, label = { Text("Number") },
            keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
            modifier = Modifier.fillMaxWidth(), shape = CircleShape,
            colors = TextFieldDefaults.colors(focusedIndicatorColor = androidx.compose.ui.graphics.Color.Transparent, unfocusedIndicatorColor = androidx.compose.ui.graphics.Color.Transparent)
        )
        ResultPanel(result)
    }
}

@Composable
fun FractionScreen(modifier: Modifier = Modifier) {
    var a by remember { mutableStateOf("") }
    val result = remember(a) { Core.run("Fraction", a) }

    Column(modifier = modifier.fillMaxSize().padding(16.dp).verticalScroll(rememberScrollState()), verticalArrangement = Arrangement.spacedBy(16.dp)) {
        OutlinedTextField(
            value = a, onValueChange = { a = it }, label = { Text("Decimal or Fraction") },
            keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Text), // Fractions might need '/'
            modifier = Modifier.fillMaxWidth(), shape = CircleShape,
            colors = TextFieldDefaults.colors(focusedIndicatorColor = androidx.compose.ui.graphics.Color.Transparent, unfocusedIndicatorColor = androidx.compose.ui.graphics.Color.Transparent)
        )
        ResultPanel(result)
    }
}

@Composable
fun StatisticsScreen(modifier: Modifier = Modifier) {
    var a by remember { mutableStateOf("") }
    val result = remember(a) { Core.run("Statistics", a) }

    Column(modifier = modifier.fillMaxSize().padding(16.dp).verticalScroll(rememberScrollState()), verticalArrangement = Arrangement.spacedBy(16.dp)) {
        OutlinedTextField(
            value = a, onValueChange = { a = it }, label = { Text("Numbers (comma or space separated)") },
            keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Text),
            modifier = Modifier.fillMaxWidth(), shape = RoundedCornerShape(24.dp),
            minLines = 3,
            colors = TextFieldDefaults.colors(focusedIndicatorColor = androidx.compose.ui.graphics.Color.Transparent, unfocusedIndicatorColor = androidx.compose.ui.graphics.Color.Transparent)
        )
        ResultPanel(result)
    }
}

@Composable
fun ResultPanel(result: Core.Result) {
    Surface(
        shape = RoundedCornerShape(32.dp),
        color = if (!result.ok && result.text.isNotEmpty()) MaterialTheme.colorScheme.errorContainer else MaterialTheme.colorScheme.secondaryContainer,
        modifier = Modifier.fillMaxWidth().heightIn(min = 200.dp)
    ) {
        Box(modifier = Modifier.padding(32.dp), contentAlignment = Alignment.Center) {
            Text(
                text = result.text.ifEmpty { "Result" },
                style = MaterialTheme.typography.titleLarge,
                color = if (!result.ok && result.text.isNotEmpty()) MaterialTheme.colorScheme.onErrorContainer else MaterialTheme.colorScheme.onSecondaryContainer,
                textAlign = TextAlign.Center
            )
        }
    }
}
