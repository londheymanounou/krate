@file:OptIn(androidx.compose.material3.ExperimentalMaterial3Api::class)

package app.krate

import android.content.Intent
import android.net.Uri
import android.os.Build
import android.os.Environment
import android.provider.Settings
import androidx.compose.material3.Button
import androidx.compose.ui.platform.LocalContext
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.rounded.ArrowUpward
import androidx.compose.material.icons.rounded.Folder
import androidx.compose.material.icons.rounded.InsertDriveFile
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import java.io.File

/**
 * An in-app file browser, replacing the system picker for this app's purposes.
 *
 * **Why not SAF.** The core is path-based — it reads and writes with `std::fs`. A SAF
 * `content://` URI cannot be turned back into a filesystem path on a modern Android: the
 * `_data` column that used to allow it is unavailable under scoped storage (verified failing on
 * API 36 — the picker's "Recent" tab returns a MediaStore document whose id is an opaque row
 * number, and the query yields nothing). The fallback was copying into the app cache, which
 * silently sends every write-tool's output somewhere the user cannot reach.
 *
 * This app already holds `MANAGE_EXTERNAL_STORAGE`, so it can enumerate shared storage directly
 * and hand the core a real path. Fewer moving parts than SAF, and it is the only option that
 * actually satisfies the core's contract.
 *
 * ponytail: no permission re-prompt here, no thumbnails, no sorting options. If the permission is
 * missing the list is simply empty; [MainActivity] is what asks for it.
 */
@Composable
fun FileBrowserSheet(
    foldersOnly: Boolean,
    onPick: (String) -> Unit,
    onDismiss: () -> Unit,
) {
    val root = remember { Environment.getExternalStorageDirectory() }
    var dir by remember { mutableStateOf(root) }

    val entries = remember(dir) {
        // Directories first, then files, each alphabetical — the ordering every file manager uses.
        dir.listFiles()
            ?.filter { !it.isHidden }
            ?.sortedWith(compareByDescending<File> { it.isDirectory }.thenBy { it.name.lowercase() })
            .orEmpty()
    }

    ModalBottomSheet(onDismissRequest = onDismiss) {
        Column(Modifier.fillMaxWidth().heightIn(max = 560.dp)) {
            Row(
                Modifier.fillMaxWidth().padding(horizontal = 16.dp, vertical = 8.dp),
                verticalAlignment = androidx.compose.ui.Alignment.CenterVertically,
            ) {
                IconButton(
                    onClick = { dir.parentFile?.let { if (it.canRead()) dir = it } },
                    enabled = dir != root,
                ) { Icon(Icons.Rounded.ArrowUpward, "Up") }
                Text(
                    dir.absolutePath.removePrefix(root.absolutePath).ifEmpty { "Internal storage" },
                    style = MaterialTheme.typography.titleSmall,
                    maxLines = 1,
                    overflow = TextOverflow.MiddleEllipsis,
                    modifier = Modifier.weight(1f),
                )
                if (foldersOnly) {
                    TextButton(onClick = { onPick(dir.absolutePath); onDismiss() }) {
                        Text("Use this folder")
                    }
                }
            }
            HorizontalDivider()
            val context = LocalContext.current
            val granted = Build.VERSION.SDK_INT < Build.VERSION_CODES.R ||
                Environment.isExternalStorageManager()

            if (!granted) {
                // Asked here, at the moment a file is actually needed, rather than on first launch.
                // The user has context for why, which is both better UX and what Play expects.
                Column(
                    Modifier.fillMaxWidth().padding(32.dp),
                    horizontalAlignment = androidx.compose.ui.Alignment.CenterHorizontally,
                ) {
                    Text(
                        "KRATE needs access to your files to read and write them. Everything stays on this device.",
                        style = MaterialTheme.typography.bodyMedium,
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                        textAlign = androidx.compose.ui.text.style.TextAlign.Center,
                    )
                    Spacer(Modifier.height(16.dp))
                    Button(onClick = {
                        context.startActivity(
                            Intent(Settings.ACTION_MANAGE_APP_ALL_FILES_ACCESS_PERMISSION).apply {
                                data = Uri.parse("package:" + context.packageName)
                            }
                        )
                    }) { Text("Grant access") }
                }
            } else if (entries.isEmpty()) {
                Box(Modifier.fillMaxWidth().padding(48.dp), contentAlignment = androidx.compose.ui.Alignment.Center) {
                    Text(
                        "This folder is empty",
                        style = MaterialTheme.typography.bodyMedium,
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                    )
                }
            }
            LazyColumn {
                items(entries, key = { it.absolutePath }) { entry ->
                    ListItem(
                        headlineContent = { Text(entry.name, maxLines = 1, overflow = TextOverflow.Ellipsis) },
                        supportingContent = if (entry.isFile) {
                            { Text(humanSize(entry.length()), style = MaterialTheme.typography.bodySmall) }
                        } else null,
                        leadingContent = {
                            Icon(
                                if (entry.isDirectory) Icons.Rounded.Folder else Icons.Rounded.InsertDriveFile,
                                null,
                                tint = MaterialTheme.colorScheme.primary,
                            )
                        },
                        modifier = Modifier.clickable {
                            if (entry.isDirectory) {
                                dir = entry
                            } else if (!foldersOnly) {
                                onPick(entry.absolutePath)
                                onDismiss()
                            }
                        },
                    )
                }
            }
        }
    }
}

private fun humanSize(bytes: Long): String = when {
    bytes < 1024 -> "$bytes B"
    bytes < 1024 * 1024 -> "${bytes / 1024} KB"
    bytes < 1024L * 1024 * 1024 -> "${bytes / (1024 * 1024)} MB"
    else -> "%.1f GB".format(bytes / (1024.0 * 1024 * 1024))
}
