@file:OptIn(androidx.compose.material3.ExperimentalMaterial3Api::class)

package app.krate

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.itemsIndexed
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.rounded.ArrowBack
import androidx.compose.material.icons.rounded.DeleteSweep
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp

/** Which tools actually get used, most-used first. */
@Composable
fun StatsScreen(tools: List<ToolInfo>, onBack: () -> Unit, modifier: Modifier = Modifier) {
    val context = LocalContext.current
    var counts by remember { mutableStateOf(Recents.counts(context)) }
    var confirmClear by remember { mutableStateOf(false) }

    val rows = remember(counts, tools) {
        counts.entries
            .mapNotNull { (id, n) -> tools.firstOrNull { it.id == id }?.let { it to n } }
            .sortedByDescending { it.second }
    }
    val total = rows.sumOf { it.second }
    val top = rows.firstOrNull()?.second ?: 1

    Scaffold(
        modifier = modifier,
        containerColor = MaterialTheme.colorScheme.surface,
        topBar = {
            TopAppBar(
                title = { Text("Usage", fontWeight = FontWeight.Medium) },
                navigationIcon = {
                    IconButton(onClick = onBack) { Icon(Icons.Rounded.ArrowBack, "Back") }
                },
                actions = {
                    if (rows.isNotEmpty()) {
                        IconButton(onClick = { confirmClear = true }) {
                            Icon(Icons.Rounded.DeleteSweep, "Clear")
                        }
                    }
                },
                colors = TopAppBarDefaults.topAppBarColors(
                    containerColor = MaterialTheme.colorScheme.surface
                ),
            )
        },
    ) { pad ->
        if (rows.isEmpty()) {
            Box(Modifier.fillMaxSize().padding(pad), contentAlignment = Alignment.Center) {
                Text(
                    "No tools used yet",
                    style = MaterialTheme.typography.bodyLarge,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
            }
            return@Scaffold
        }

        LazyColumn(
            modifier = Modifier.fillMaxSize().padding(pad),
            contentPadding = PaddingValues(16.dp),
            verticalArrangement = Arrangement.spacedBy(2.dp),
        ) {
            item {
                Surface(
                    shape = RoundedCornerShape(24.dp),
                    color = MaterialTheme.colorScheme.primaryContainer,
                    modifier = Modifier.fillMaxWidth().padding(bottom = 12.dp),
                ) {
                    Row(
                        Modifier.padding(20.dp),
                        horizontalArrangement = Arrangement.spacedBy(32.dp),
                    ) {
                        Stat("$total", "runs")
                        Stat("${rows.size}", "tools used")
                        Stat("${tools.size - rows.size}", "untouched")
                    }
                }
            }
            itemsIndexed(rows, key = { _, r -> r.first.id }) { i, (tool, n) ->
                val big = 24.dp
                val small = 4.dp
                Surface(
                    modifier = Modifier.fillMaxWidth().clip(
                        RoundedCornerShape(
                            topStart = if (i == 0) big else small,
                            topEnd = if (i == 0) big else small,
                            bottomStart = if (i == rows.lastIndex) big else small,
                            bottomEnd = if (i == rows.lastIndex) big else small,
                        )
                    ),
                    color = MaterialTheme.colorScheme.surfaceContainerLow,
                ) {
                    Row(
                        Modifier.padding(horizontal = 16.dp, vertical = 12.dp),
                        verticalAlignment = Alignment.CenterVertically,
                    ) {
                        Box(
                            Modifier.size(36.dp).clip(CircleShape)
                                .background(MaterialTheme.colorScheme.secondaryContainer),
                            contentAlignment = Alignment.Center,
                        ) {
                            Icon(
                                toolIcon(tool.id, tool.categoryKey), null, Modifier.size(20.dp),
                                tint = MaterialTheme.colorScheme.onSecondaryContainer,
                            )
                        }
                        Spacer(Modifier.width(16.dp))
                        Column(Modifier.weight(1f)) {
                            Text(tool.name, style = MaterialTheme.typography.titleSmall)
                            Spacer(Modifier.height(6.dp))
                            // Bar is relative to the most-used tool, so the shape stays readable
                            // whether the top tool has been run 3 times or 300.
                            LinearProgressIndicator(
                                progress = { n.toFloat() / top },
                                modifier = Modifier.fillMaxWidth().height(6.dp),
                            )
                        }
                        Spacer(Modifier.width(16.dp))
                        Text(
                            "$n",
                            style = MaterialTheme.typography.titleMedium,
                            color = MaterialTheme.colorScheme.primary,
                        )
                    }
                }
            }
        }
    }

    if (confirmClear) {
        AlertDialog(
            onDismissRequest = { confirmClear = false },
            title = { Text("Clear usage data?") },
            text = { Text("Run counts and the recent list are deleted. This cannot be undone.") },
            confirmButton = {
                TextButton(onClick = {
                    Recents.clear(context)
                    counts = emptyMap()
                    confirmClear = false
                }) { Text("Clear") }
            },
            dismissButton = {
                TextButton(onClick = { confirmClear = false }) { Text("Cancel") }
            },
        )
    }
}

@Composable
private fun Stat(value: String, label: String) {
    Column {
        Text(
            value,
            style = MaterialTheme.typography.headlineSmall,
            fontWeight = FontWeight.Bold,
            color = MaterialTheme.colorScheme.onPrimaryContainer,
        )
        Text(
            label,
            style = MaterialTheme.typography.bodySmall,
            color = MaterialTheme.colorScheme.onPrimaryContainer,
        )
    }
}
