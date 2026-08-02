@file:OptIn(androidx.compose.material3.ExperimentalMaterial3ExpressiveApi::class)

package app.krate

import androidx.compose.material3.LoadingIndicator

import androidx.compose.foundation.layout.*
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Modifier
import androidx.compose.foundation.verticalScroll
import androidx.compose.foundation.rememberScrollState
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.rounded.SwapHoriz
import androidx.compose.ui.Alignment
import androidx.compose.ui.unit.dp
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import java.net.HttpURLConnection
import java.net.URL

@Composable
fun CurrencyScreen(modifier: Modifier = Modifier) {
    var amount by remember { mutableStateOf("1") }
    var fromPair by remember { mutableStateOf(CURRENCIES.first()) }
    var toPair by remember { mutableStateOf(CURRENCIES[1]) }
    val from = fromPair.second
    val to = toPair.second
    var output by remember { mutableStateOf("") }
    var isError by remember { mutableStateOf(false) }
    var isLoading by remember { mutableStateOf(false) }
    
    val coroutineScope = rememberCoroutineScope()

    Column(
        modifier = modifier.fillMaxSize().padding(24.dp),
        verticalArrangement = Arrangement.spacedBy(24.dp)
    ) {
        androidx.compose.material3.OutlinedTextField(
            value = amount,
            onValueChange = { amount = it },
            label = { Text("Amount") },
            singleLine = true,
            keyboardOptions = androidx.compose.foundation.text.KeyboardOptions(
                keyboardType = androidx.compose.ui.text.input.KeyboardType.Decimal
            ),
            modifier = Modifier.fillMaxWidth(),
            shape = androidx.compose.foundation.shape.RoundedCornerShape(24.dp),
        )
        // Named dropdowns, not code fields: nobody should have to know "Swiss franc" is CHF, or
        // that the code has to be typed in capitals.
        Row(
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(12.dp),
        ) {
            UnitDropdown("From", fromPair, CURRENCIES, Modifier.weight(1f)) { fromPair = it }
            androidx.compose.material3.FilledTonalIconButton(onClick = {
                val t = fromPair; fromPair = toPair; toPair = t
            }) {
                Icon(Icons.Rounded.SwapHoriz, "Swap")
            }
            UnitDropdown("To", toPair, CURRENCIES, Modifier.weight(1f)) { toPair = it }
        }

        Button(
            onClick = {
                val input = "$amount $from $to"
                isLoading = true
                coroutineScope.launch {
                    val base = from.trim().uppercase()
                    if (base.length == 3) {
                        fetchRates(base)
                    }
                    val res = Core.run("Currency", input)
                    output = res.text
                    isError = !res.ok
                    isLoading = false
                }
            },
            modifier = Modifier.fillMaxWidth().height(64.dp),
            shape = androidx.compose.foundation.shape.CircleShape
        ) {
            if (isLoading) {
                LoadingIndicator(modifier = Modifier.size(24.dp), color = MaterialTheme.colorScheme.onPrimary)
            } else {
                Text("Convert", style = MaterialTheme.typography.titleLarge)
            }
        }

        Surface(
            shape = androidx.compose.foundation.shape.RoundedCornerShape(32.dp),
            color = if (isError) MaterialTheme.colorScheme.errorContainer else MaterialTheme.colorScheme.secondaryContainer,
            modifier = Modifier.fillMaxWidth().weight(1f)
        ) {
            Box(modifier = Modifier.fillMaxSize().padding(32.dp), contentAlignment = Alignment.Center) {
                Text(
                    text = output.ifEmpty { "Result" },
                    style = MaterialTheme.typography.headlineLarge,
                    color = if (isError) MaterialTheme.colorScheme.onErrorContainer else MaterialTheme.colorScheme.onSecondaryContainer,
                    modifier = Modifier.verticalScroll(rememberScrollState()),
                    textAlign = TextAlign.Center
                )
            }
        }
    }
}

suspend fun fetchRates(base: String) {
    withContext(Dispatchers.IO) {
        try {
            val url = URL("https://open.er-api.com/v6/latest/$base")
            val connection = url.openConnection() as HttpURLConnection
            connection.requestMethod = "GET"
            connection.connectTimeout = 5000
            connection.readTimeout = 5000
            if (connection.responseCode == 200) {
                val json = connection.inputStream.bufferedReader().readText()
                Core.currencyStoreRates(base, json)
            }
            connection.disconnect()
        } catch (e: Exception) {
            // Fails silently, core will fall back to cache or report offline
        }
    }
}
