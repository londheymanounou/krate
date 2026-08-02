@file:OptIn(androidx.compose.material3.ExperimentalMaterial3Api::class)

package app.krate

import android.app.Activity
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.rounded.ArrowBack
import androidx.compose.material.icons.rounded.Check
import androidx.compose.material.icons.rounded.Favorite
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp

/**
 * Settings: interface language and haptics.
 *
 * Changing the language **recreates the activity**. The tool catalogue is read from the core once
 * per process and cached, and every tool name in it is already localized, so there is no way to
 * relabel 137 entries in place — the cache has to be dropped and the catalogue re-read.
 */
@Composable
fun SettingsScreen(onBack: () -> Unit, modifier: Modifier = Modifier) {
    val context = LocalContext.current
    var language by remember { mutableStateOf(Settings.language(context)) }
    var haptics by remember { mutableStateOf(Settings.haptics(context)) }
    var pickingLanguage by remember { mutableStateOf(false) }

    Scaffold(
        modifier = modifier,
        containerColor = MaterialTheme.colorScheme.surface,
        topBar = {
            TopAppBar(
                title = { Text("Settings", fontWeight = FontWeight.Medium) },
                navigationIcon = {
                    IconButton(onClick = onBack) { Icon(Icons.Rounded.ArrowBack, "Back") }
                },
                colors = TopAppBarDefaults.topAppBarColors(
                    containerColor = MaterialTheme.colorScheme.surface
                ),
            )
        },
    ) { pad ->
        Column(
            Modifier.fillMaxSize().padding(pad).padding(16.dp),
            verticalArrangement = Arrangement.spacedBy(2.dp),
        ) {
            Surface(
                shape = RoundedCornerShape(topStart = 24.dp, topEnd = 24.dp, bottomStart = 4.dp, bottomEnd = 4.dp),
                color = MaterialTheme.colorScheme.surfaceContainerLow,
                modifier = Modifier.fillMaxWidth().clickable { pickingLanguage = true },
            ) {
                ListItem(
                    headlineContent = { Text("Language") },
                    supportingContent = {
                        Text(LANGUAGES.firstOrNull { it.first == language }?.second ?: "System default")
                    },
                    colors = ListItemDefaults.colors(
                        containerColor = androidx.compose.ui.graphics.Color.Transparent
                    ),
                )
            }

            Surface(
                shape = RoundedCornerShape(4.dp),
                color = MaterialTheme.colorScheme.surfaceContainerLow,
                modifier = Modifier.fillMaxWidth(),
            ) {
                ListItem(
                    headlineContent = { Text("Vibration") },
                    supportingContent = { Text("Haptic feedback on calculator and keypad keys") },
                    trailingContent = {
                        Switch(
                            checked = haptics,
                            onCheckedChange = {
                                haptics = it
                                Settings.setHaptics(context, it)
                            },
                        )
                    },
                    colors = ListItemDefaults.colors(
                        containerColor = androidx.compose.ui.graphics.Color.Transparent
                    ),
                )
            }

            Spacer(Modifier.height(10.dp))

            // Support, in a tonal container so it reads as an invitation rather than a setting.
            Surface(
                shape = RoundedCornerShape(topStart = 24.dp, topEnd = 24.dp, bottomStart = 4.dp, bottomEnd = 4.dp),
                color = MaterialTheme.colorScheme.tertiaryContainer,
                modifier = Modifier.fillMaxWidth().clickable {
                    context.startActivity(
                        android.content.Intent(
                            android.content.Intent.ACTION_VIEW,
                            android.net.Uri.parse("https://ko-fi.com/londhey"),
                        )
                    )
                },
            ) {
                ListItem(
                    headlineContent = { Text("Support KRATE") },
                    supportingContent = { Text("Buy me a coffee on Ko-fi") },
                    leadingContent = {
                        Icon(
                            androidx.compose.material.icons.Icons.Rounded.Favorite,
                            null,
                            tint = MaterialTheme.colorScheme.onTertiaryContainer,
                        )
                    },
                    colors = ListItemDefaults.colors(
                        containerColor = androidx.compose.ui.graphics.Color.Transparent,
                        headlineColor = MaterialTheme.colorScheme.onTertiaryContainer,
                        supportingColor = MaterialTheme.colorScheme.onTertiaryContainer,
                    ),
                )
            }

            Surface(
                shape = RoundedCornerShape(topStart = 4.dp, topEnd = 4.dp, bottomStart = 24.dp, bottomEnd = 24.dp),
                color = MaterialTheme.colorScheme.tertiaryContainer,
                modifier = Modifier.fillMaxWidth().clickable {
                    context.startActivity(
                        android.content.Intent(
                            android.content.Intent.ACTION_VIEW,
                            android.net.Uri.parse("https://github.com/londheymanounou"),
                        )
                    )
                },
            ) {
                ListItem(
                    headlineContent = { Text("Developed by") },
                    supportingContent = { Text("Londhey Manounou") },
                    leadingContent = {
                        Icon(
                            androidx.compose.material.icons.Icons.Rounded.Favorite,
                            null,
                            tint = MaterialTheme.colorScheme.onTertiaryContainer,
                        )
                    },
                    colors = ListItemDefaults.colors(
                        containerColor = androidx.compose.ui.graphics.Color.Transparent,
                        headlineColor = MaterialTheme.colorScheme.onTertiaryContainer,
                        supportingColor = MaterialTheme.colorScheme.onTertiaryContainer,
                    ),
                )
            }
        }
    }

    if (pickingLanguage) {
        ModalBottomSheet(onDismissRequest = { pickingLanguage = false }) {
            LazyColumn(Modifier.fillMaxWidth().heightIn(max = 560.dp)) {
                items(LANGUAGES, key = { it.first.ifEmpty { "system" } }) { (tag, name) ->
                    ListItem(
                        headlineContent = { Text(name) },
                        trailingContent = {
                            if (tag == language) Icon(Icons.Rounded.Check, "Selected")
                        },
                        modifier = Modifier.clickable {
                            Settings.setLanguage(context, tag)
                            language = tag
                            pickingLanguage = false
                            // Drop the cached catalogue and rebuild the UI in the new language.
                            Catalog.invalidate()
                            (context as? Activity)?.recreate()
                        },
                    )
                }
            }
        }
    }
}
