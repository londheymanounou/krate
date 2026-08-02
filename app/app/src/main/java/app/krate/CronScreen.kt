package app.krate

import androidx.compose.foundation.layout.*
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Modifier
import androidx.compose.foundation.verticalScroll
import androidx.compose.foundation.rememberScrollState
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.Alignment
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.unit.dp
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext

@Composable
fun CronScreen(modifier: Modifier = Modifier) {
    var cronExpression by remember { mutableStateOf("*/15 * * * *") }
    var output by remember { mutableStateOf("") }
    var isError by remember { mutableStateOf(false) }
    
    val coroutineScope = rememberCoroutineScope()

    Column(
        modifier = modifier.fillMaxSize().padding(24.dp),
        verticalArrangement = Arrangement.spacedBy(24.dp)
    ) {
        androidx.compose.material3.OutlinedTextField(
            value = cronExpression,
            onValueChange = { newValue ->
                cronExpression = newValue
                coroutineScope.launch {
                    val res = withContext(Dispatchers.IO) {
                        Core.run("Cron", newValue.trim())
                    }
                    output = res.text
                    isError = !res.ok
                }
            },
            label = { Text("Cron Expression (e.g. '0 12 * * *')") },
            modifier = Modifier.fillMaxWidth(),
            shape = androidx.compose.foundation.shape.CircleShape,
            textStyle = MaterialTheme.typography.headlineSmall.copy(textAlign = TextAlign.Center),
            colors = TextFieldDefaults.colors(
                focusedContainerColor = MaterialTheme.colorScheme.surfaceVariant,
                unfocusedContainerColor = MaterialTheme.colorScheme.surfaceVariant,
                focusedIndicatorColor = androidx.compose.ui.graphics.Color.Transparent,
                unfocusedIndicatorColor = androidx.compose.ui.graphics.Color.Transparent
            )
        )

        Surface(
            shape = androidx.compose.foundation.shape.RoundedCornerShape(32.dp),
            color = if (isError) MaterialTheme.colorScheme.errorContainer else MaterialTheme.colorScheme.secondaryContainer,
            modifier = Modifier.fillMaxWidth().weight(1f)
        ) {
            Box(modifier = Modifier.fillMaxSize().padding(32.dp), contentAlignment = Alignment.Center) {
                Text(
                    text = output.ifEmpty { "Result" },
                    style = MaterialTheme.typography.titleLarge,
                    color = if (isError) MaterialTheme.colorScheme.onErrorContainer else MaterialTheme.colorScheme.onSecondaryContainer,
                    modifier = Modifier.verticalScroll(rememberScrollState()),
                    textAlign = TextAlign.Center
                )
            }
        }
    }
}
