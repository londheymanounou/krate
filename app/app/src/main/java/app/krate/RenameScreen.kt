@file:OptIn(androidx.compose.material3.ExperimentalMaterial3ExpressiveApi::class)

package app.krate

import androidx.compose.material3.LoadingIndicator
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.rounded.Folder

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
fun RenameScreen(modifier: Modifier = Modifier) {
    val context = androidx.compose.ui.platform.LocalContext.current
    var folderPath by remember { mutableStateOf("") }
    var findText by remember { mutableStateOf("") }
    var replaceText by remember { mutableStateOf("") }
    
    var output by remember { mutableStateOf("") }
    var isError by remember { mutableStateOf(false) }
    var isLoading by remember { mutableStateOf(false) }
    
    val coroutineScope = rememberCoroutineScope()

    Column(
        modifier = modifier.fillMaxSize().padding(24.dp),
        verticalArrangement = Arrangement.spacedBy(16.dp)
    ) {
        FilePathField(
            value = folderPath,
            onValueChange = { folderPath = it },
            label = "Folder Path",
            modifier = Modifier.fillMaxWidth(),
            folder = true,
        )
        Row(horizontalArrangement = Arrangement.spacedBy(16.dp)) {
            androidx.compose.material3.OutlinedTextField(
                value = findText,
                onValueChange = { findText = it },
                label = { Text("Find") },
                modifier = Modifier.weight(1f),
                shape = androidx.compose.foundation.shape.CircleShape,
                colors = TextFieldDefaults.colors(
                    focusedContainerColor = MaterialTheme.colorScheme.surfaceVariant,
                    unfocusedContainerColor = MaterialTheme.colorScheme.surfaceVariant,
                    focusedIndicatorColor = androidx.compose.ui.graphics.Color.Transparent,
                    unfocusedIndicatorColor = androidx.compose.ui.graphics.Color.Transparent
                )
            )
            androidx.compose.material3.OutlinedTextField(
                value = replaceText,
                onValueChange = { replaceText = it },
                label = { Text("Replace") },
                modifier = Modifier.weight(1f),
                shape = androidx.compose.foundation.shape.CircleShape,
                colors = TextFieldDefaults.colors(
                    focusedContainerColor = MaterialTheme.colorScheme.surfaceVariant,
                    unfocusedContainerColor = MaterialTheme.colorScheme.surfaceVariant,
                    focusedIndicatorColor = androidx.compose.ui.graphics.Color.Transparent,
                    unfocusedIndicatorColor = androidx.compose.ui.graphics.Color.Transparent
                )
            )
        }

        Spacer(modifier = Modifier.height(8.dp))
        
        Row(horizontalArrangement = Arrangement.spacedBy(16.dp)) {
            Button(
                onClick = {
                    val input = "${folderPath.trim()} | ${findText.trim()} | ${replaceText.trim()}"
                    isLoading = true
                    coroutineScope.launch {
                        val res = withContext(Dispatchers.IO) {
                            Core.run("Rename", input)
                        }
                        output = res.text
                        isError = !res.ok
                        isLoading = false
                    }
                },
                modifier = Modifier.weight(1f).height(64.dp),
                shape = androidx.compose.foundation.shape.CircleShape
            ) {
                if (isLoading) LoadingIndicator(modifier = Modifier.size(24.dp), color = MaterialTheme.colorScheme.onPrimary)
                else Text("Preview", style = MaterialTheme.typography.titleLarge)
            }
            Button(
                onClick = {
                    val input = "${folderPath.trim()} | ${findText.trim()} | ${replaceText.trim()} | apply"
                    isLoading = true
                    coroutineScope.launch {
                        val res = withContext(Dispatchers.IO) {
                            Core.run("Rename", input)
                        }
                        output = res.text
                        isError = !res.ok
                        isLoading = false
                    }
                },
                modifier = Modifier.weight(1f).height(64.dp),
                shape = androidx.compose.foundation.shape.CircleShape,
                colors = ButtonDefaults.buttonColors(containerColor = MaterialTheme.colorScheme.error)
            ) {
                if (isLoading) LoadingIndicator(modifier = Modifier.size(24.dp), color = MaterialTheme.colorScheme.onError)
                else Text("Apply", style = MaterialTheme.typography.titleLarge)
            }
        }

        Spacer(modifier = Modifier.height(8.dp))

        Surface(
            shape = androidx.compose.foundation.shape.RoundedCornerShape(32.dp),
            color = if (isError) MaterialTheme.colorScheme.errorContainer else MaterialTheme.colorScheme.secondaryContainer,
            modifier = Modifier.fillMaxWidth().weight(1f)
        ) {
            Box(modifier = Modifier.fillMaxSize().padding(32.dp), contentAlignment = Alignment.Center) {
                Column(horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.spacedBy(16.dp)) {
                    Text(
                        text = output.ifEmpty { "Result" },
                        style = MaterialTheme.typography.titleLarge,
                        color = if (isError) MaterialTheme.colorScheme.onErrorContainer else MaterialTheme.colorScheme.onSecondaryContainer,
                        modifier = Modifier.weight(1f, fill = false).verticalScroll(rememberScrollState()),
                        textAlign = TextAlign.Center
                    )
                    if (!isError && output.isNotEmpty()) {
                        Button(
                            onClick = {
                                val intent = android.content.Intent(android.app.DownloadManager.ACTION_VIEW_DOWNLOADS)
                                intent.addFlags(android.content.Intent.FLAG_ACTIVITY_NEW_TASK)
                                try {
                                    context.startActivity(intent)
                                } catch (e: Exception) {}
                            }
                        ) {
                            Icon(Icons.Rounded.Folder, contentDescription = null)
                            Spacer(Modifier.width(8.dp))
                            Text("Open Folder Location")
                        }
                    }
                }
            }
        }
    }
}
