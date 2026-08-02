package app.krate

import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.rounded.Add
import androidx.compose.material.icons.rounded.Clear
import androidx.compose.material.icons.rounded.Public
import androidx.compose.material.icons.rounded.Schedule
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import java.util.Calendar

data class TimezoneResult(val id: String, val datetime: String, val offset: String)

val ALL_ZONES = listOf(
    "utc", "london", "paris", "berlin", "madrid", "rome", "moscow",
    "nyc", "chicago", "denver", "losangeles", "tokyo", "shanghai",
    "hongkong", "singapore", "seoul", "dubai", "mumbai", "sydney", "auckland"
)

@OptIn(ExperimentalMaterial3Api::class, ExperimentalLayoutApi::class)
@Composable
fun TimezoneScreen(modifier: Modifier = Modifier) {
    val initialTime = remember { Calendar.getInstance() }
    val initialHour = initialTime.get(Calendar.HOUR_OF_DAY)
    val initialMinute = initialTime.get(Calendar.MINUTE)

    var timeToken by remember { 
        mutableStateOf(String.format("%02d:%02d", initialHour, initialMinute)) 
    }
    var sourceZone by remember { mutableStateOf("utc") }
    var targetZones by remember { mutableStateOf(listOf("nyc", "london", "tokyo")) }
    
    var results by remember { mutableStateOf<List<TimezoneResult>>(emptyList()) }
    var errorMsg by remember { mutableStateOf("") }
    
    var showTimePicker by remember { mutableStateOf(false) }
    val timePickerState = rememberTimePickerState(initialHour = initialHour, initialMinute = initialMinute, is24Hour = true)

    LaunchedEffect(timeToken, sourceZone, targetZones) {
        if (targetZones.isEmpty()) {
            results = emptyList()
            return@LaunchedEffect
        }
        val input = "${timeToken.trim()} ${sourceZone.trim()} ${targetZones.joinToString(" ")}".trim()
        
        withContext(Dispatchers.IO) {
            val res = Core.run("Timezone", input)
            if (res.ok) {
                val parsed = res.text.lines().mapNotNull { line ->
                    if (line.length >= 35) {
                        val id = line.substring(0, 20).trim()
                        val dt = line.substring(21, 37).trim()
                        val off = line.substring(38).trim()
                        TimezoneResult(id, dt, off)
                    } else null
                }
                results = parsed
                errorMsg = ""
            } else {
                errorMsg = res.text
                results = emptyList()
            }
        }
    }

    if (showTimePicker) {
        AlertDialog(
            onDismissRequest = { showTimePicker = false },
            confirmButton = {
                TextButton(onClick = { 
                    showTimePicker = false
                    val h = timePickerState.hour.toString().padStart(2, '0')
                    val m = timePickerState.minute.toString().padStart(2, '0')
                    timeToken = "$h:$m"
                }) { Text("OK") }
            },
            dismissButton = {
                TextButton(onClick = { showTimePicker = false }) { Text("Cancel") }
            },
            text = {
                TimePicker(state = timePickerState)
            }
        )
    }

    Column(
        modifier = modifier.fillMaxSize().padding(16.dp),
        verticalArrangement = Arrangement.spacedBy(16.dp)
    ) {
        Surface(
            shape = RoundedCornerShape(32.dp),
            color = MaterialTheme.colorScheme.surfaceContainerHigh,
            modifier = Modifier.fillMaxWidth()
        ) {
            Column(
                modifier = Modifier.padding(24.dp),
                verticalArrangement = Arrangement.spacedBy(16.dp)
            ) {
                // Time Picker Button
                OutlinedButton(
                    onClick = { showTimePicker = true },
                    modifier = Modifier.fillMaxWidth(),
                    shape = RoundedCornerShape(16.dp),
                    contentPadding = PaddingValues(16.dp)
                ) {
                    Icon(Icons.Rounded.Schedule, null)
                    Spacer(Modifier.width(8.dp))
                    Text(timeToken, style = MaterialTheme.typography.titleLarge)
                }

                Row(horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                    // Source Zone Dropdown
                    var sourceExpanded by remember { mutableStateOf(false) }
                    ExposedDropdownMenuBox(
                        expanded = sourceExpanded,
                        onExpandedChange = { sourceExpanded = it },
                        modifier = Modifier.weight(1f)
                    ) {
                        OutlinedTextField(
                            value = sourceZone.uppercase(),
                            onValueChange = {},
                            readOnly = true,
                            label = { Text("From") },
                            trailingIcon = { ExposedDropdownMenuDefaults.TrailingIcon(expanded = sourceExpanded) },
                            modifier = Modifier.menuAnchor().fillMaxWidth(),
                            shape = RoundedCornerShape(16.dp),
                            colors = ExposedDropdownMenuDefaults.outlinedTextFieldColors()
                        )
                        ExposedDropdownMenu(
                            expanded = sourceExpanded,
                            onDismissRequest = { sourceExpanded = false }
                        ) {
                            ALL_ZONES.forEach { zone ->
                                DropdownMenuItem(
                                    text = { Text(zone.uppercase()) },
                                    onClick = {
                                        sourceZone = zone
                                        sourceExpanded = false
                                    }
                                )
                            }
                        }
                    }

                    // Add Target Zone Dropdown
                    var targetExpanded by remember { mutableStateOf(false) }
                    ExposedDropdownMenuBox(
                        expanded = targetExpanded,
                        onExpandedChange = { targetExpanded = it },
                        modifier = Modifier.weight(1f)
                    ) {
                        OutlinedTextField(
                            value = "Add Zone",
                            onValueChange = {},
                            readOnly = true,
                            label = { Text("To") },
                            trailingIcon = { Icon(Icons.Rounded.Add, null) },
                            modifier = Modifier.menuAnchor().fillMaxWidth(),
                            shape = RoundedCornerShape(16.dp),
                            colors = ExposedDropdownMenuDefaults.outlinedTextFieldColors()
                        )
                        ExposedDropdownMenu(
                            expanded = targetExpanded,
                            onDismissRequest = { targetExpanded = false }
                        ) {
                            ALL_ZONES.filter { it !in targetZones }.forEach { zone ->
                                DropdownMenuItem(
                                    text = { Text(zone.uppercase()) },
                                    onClick = {
                                        targetZones = targetZones + zone
                                        targetExpanded = false
                                    }
                                )
                            }
                        }
                    }
                }

                // Target Zones Chips
                if (targetZones.isNotEmpty()) {
                    FlowRow(
                        horizontalArrangement = Arrangement.spacedBy(8.dp),
                        verticalArrangement = Arrangement.spacedBy(8.dp),
                        modifier = Modifier.fillMaxWidth()
                    ) {
                        targetZones.forEach { zone ->
                            InputChip(
                                selected = false,
                                onClick = { },
                                label = { Text(zone.uppercase()) },
                                trailingIcon = {
                                    IconButton(
                                        onClick = { targetZones = targetZones - zone },
                                        modifier = Modifier.size(16.dp)
                                    ) {
                                        Icon(Icons.Rounded.Clear, contentDescription = "Remove", modifier = Modifier.size(14.dp))
                                    }
                                }
                            )
                        }
                    }
                }
            }
        }

        if (errorMsg.isNotEmpty()) {
            Surface(
                shape = RoundedCornerShape(24.dp),
                color = MaterialTheme.colorScheme.errorContainer,
                modifier = Modifier.fillMaxWidth()
            ) {
                Text(
                    text = errorMsg,
                    color = MaterialTheme.colorScheme.onErrorContainer,
                    modifier = Modifier.padding(24.dp),
                    style = MaterialTheme.typography.bodyLarge
                )
            }
        } else {
            LazyColumn(
                verticalArrangement = Arrangement.spacedBy(12.dp),
                modifier = Modifier.fillMaxWidth().weight(1f)
            ) {
                items(results) { tz ->
                    Surface(
                        shape = RoundedCornerShape(24.dp),
                        color = MaterialTheme.colorScheme.secondaryContainer,
                        modifier = Modifier.fillMaxWidth()
                    ) {
                        Row(
                            modifier = Modifier.padding(24.dp),
                            verticalAlignment = Alignment.CenterVertically,
                            horizontalArrangement = Arrangement.SpaceBetween
                        ) {
                            Column {
                                Text(
                                    text = tz.id,
                                    style = MaterialTheme.typography.titleMedium,
                                    color = MaterialTheme.colorScheme.onSecondaryContainer.copy(alpha = 0.7f)
                                )
                                Text(
                                    text = tz.datetime.substringBefore(" "),
                                    style = MaterialTheme.typography.bodyMedium,
                                    color = MaterialTheme.colorScheme.onSecondaryContainer.copy(alpha = 0.6f)
                                )
                            }
                            Column(horizontalAlignment = Alignment.End) {
                                Text(
                                    text = tz.datetime.substringAfter(" "),
                                    style = MaterialTheme.typography.displaySmall,
                                    fontWeight = FontWeight.Bold,
                                    color = MaterialTheme.colorScheme.onSecondaryContainer
                                )
                                Text(
                                    text = "UTC ${tz.offset}",
                                    style = MaterialTheme.typography.labelLarge,
                                    color = MaterialTheme.colorScheme.onSecondaryContainer.copy(alpha = 0.6f)
                                )
                            }
                        }
                    }
                }
            }
        }
    }
}
