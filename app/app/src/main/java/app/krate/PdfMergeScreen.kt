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
import androidx.compose.ui.Alignment
import androidx.compose.ui.unit.dp
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext

@Composable
fun PdfMergeScreen(modifier: Modifier = Modifier) {
    var paths by remember { mutableStateOf("") }
    var output by remember { mutableStateOf("") }
    var isError by remember { mutableStateOf(false) }
    var isLoading by remember { mutableStateOf(false) }
    
    val coroutineScope = rememberCoroutineScope()

    Column(
        modifier = modifier.fillMaxSize().padding(24.dp),
        verticalArrangement = Arrangement.spacedBy(16.dp)
    ) {
        androidx.compose.material3.OutlinedTextField(
            value = paths,
            onValueChange = { paths = it },
            label = { Text("PDF Files (Absolute paths, one per line)") },
            modifier = Modifier.fillMaxWidth().weight(0.5f),
            shape = androidx.compose.foundation.shape.RoundedCornerShape(32.dp),
            colors = TextFieldDefaults.colors(
                focusedContainerColor = MaterialTheme.colorScheme.surfaceVariant,
                unfocusedContainerColor = MaterialTheme.colorScheme.surfaceVariant,
                focusedIndicatorColor = androidx.compose.ui.graphics.Color.Transparent,
                unfocusedIndicatorColor = androidx.compose.ui.graphics.Color.Transparent
            )
        )

        Spacer(modifier = Modifier.height(8.dp))
        
        Button(
            onClick = {
                val input = paths.trim()
                isLoading = true
                coroutineScope.launch {
                    val res = withContext(Dispatchers.IO) {
                        Core.run("PdfMerge", input)
                    }
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
                Text("Merge PDFs", style = MaterialTheme.typography.titleLarge)
            }
        }

        Spacer(modifier = Modifier.height(8.dp))

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
