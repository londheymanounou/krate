package app.krate

import android.content.Context
import android.content.Intent
import android.net.Uri
import android.os.Environment
import android.provider.DocumentsContract
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.runtime.Composable
import androidx.compose.runtime.remember
import androidx.compose.ui.platform.LocalContext
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.rounded.AttachFile
import androidx.compose.material.icons.rounded.FolderOpen
import androidx.compose.material.icons.rounded.Storage
import androidx.compose.ui.unit.dp
import java.io.File

/**
 * The core takes **filesystem paths** — it reads and writes with `std::fs` and knows nothing about
 * `content://`. So a picked document is resolved back to a real path wherever possible, and only
 * copied into the cache as a fallback.
 *
 * The distinction matters for more than tidiness: tools that *write* (Zip, PdfMerge, StripMetadata)
 * put their output beside the input. Hand them a cache copy and the result lands somewhere the user
 * can never find. A real path keeps the output where they picked the file.
 */
private fun resolveToPath(context: Context, uri: Uri): String? {
    if (uri.scheme == "file") return uri.path

    if (DocumentsContract.isDocumentUri(context, uri)) {
        val id = runCatching { DocumentsContract.getDocumentId(uri) }.getOrNull()
        // externalstorage provider: "primary:Download/x.pdf" -> /storage/emulated/0/Download/x.pdf
        if (id != null && id.startsWith("primary:")) {
            val path = File(Environment.getExternalStorageDirectory(), id.removePrefix("primary:"))
            if (path.exists()) return path.absolutePath
        }
    }

    // Everything else — notably the MediaStore documents provider, which is what the picker's
    // "Recent" tab hands back. Its document id is an opaque row number ("document:1000000033"),
    // so there is nothing to parse; the row's _data column holds the real path.
    //
    // _data is deprecated and absent for cloud/virtual documents, hence the null-return rather than
    // a throw: the caller then copies to cache, which is correct for those.
    runCatching {
        context.contentResolver.query(uri, arrayOf("_data"), null, null, null)?.use { c ->
            if (c.moveToFirst()) {
                val path = c.getString(0)
                if (!path.isNullOrBlank() && File(path).exists()) return path
            }
        }
    }
    return null
}

/** Copies a document we could not resolve into the cache so the core has something to open. */
private fun copyToCache(context: Context, uri: Uri): String? = runCatching {
    val name = context.contentResolver.query(uri, null, null, null, null)?.use { c ->
        val i = c.getColumnIndex(android.provider.OpenableColumns.DISPLAY_NAME)
        if (i >= 0 && c.moveToFirst()) c.getString(i) else null
    } ?: "picked"
    val dest = File(context.cacheDir, name)
    context.contentResolver.openInputStream(uri)!!.use { input ->
        dest.outputStream().use { input.copyTo(it) }
    }
    dest.absolutePath
}.getOrNull()


/**
 * The system file explorer, opened at Internal storage.
 *
 * This matters for correctness, not taste. The picker's default landing tab is "Recent", which
 * serves documents from the MediaStore provider — an opaque row id with no resolvable path under
 * scoped storage, so [resolveToPath] fails and the file has to be copied into the cache. Landing on
 * the **externalstorage** provider instead yields ids like `primary:Download/x.txt`, which map
 * straight onto a real filesystem path — the only kind the Rust core can use.
 *
 * SHOW_ADVANCED reveals internal storage on devices that hide it by default.
 */
private class OpenAtInternalStorage : ActivityResultContracts.OpenDocument() {
    override fun createIntent(context: Context, input: Array<String>): Intent =
        super.createIntent(context, input).apply {
            putExtra(
                DocumentsContract.EXTRA_INITIAL_URI,
                DocumentsContract.buildDocumentUri(
                    "com.android.externalstorage.documents", "primary:",
                ),
            )
            putExtra("android.content.extra.SHOW_ADVANCED", true)
        }
}

/**
 * Returns a launcher that opens the system file picker and hands back a usable path.
 *
 * Call the returned lambda with the MIME types to accept; the wildcard type accepts anything.
 */
@Composable
fun rememberFilePicker(onPicked: (String) -> Unit): (Array<String>) -> Unit {
    val context = LocalContext.current
    var types = remember { arrayOf("*/*") }
    val launcher = rememberLauncherForActivityResult(
        OpenAtInternalStorage()
    ) { uri: Uri? ->
        if (uri != null) {
            val path = resolveToPath(context, uri) ?: copyToCache(context, uri)
            if (path != null) onPicked(path)
        }
    }
    return { mime ->
        types = mime
        launcher.launch(mime)
    }
}




/** Folder picker via the system explorer, for tools that operate on a directory. */
@Composable
fun rememberFolderPicker(onPicked: (String) -> Unit): () -> Unit {
    val launcher = rememberLauncherForActivityResult(
        ActivityResultContracts.OpenDocumentTree()
    ) { uri: Uri? ->
        if (uri == null) return@rememberLauncherForActivityResult
        val id = runCatching { DocumentsContract.getTreeDocumentId(uri) }.getOrNull()
        if (id != null && id.startsWith("primary:")) {
            val dir = File(Environment.getExternalStorageDirectory(), id.removePrefix("primary:"))
            if (dir.exists()) onPicked(dir.absolutePath)
        }
    }
    return { launcher.launch(null) }
}

/**
 * A path field with a Browse button. Typing is still allowed — the core accepts any path, and a
 * pasted one is sometimes faster than three taps through the picker.
 */
@androidx.compose.runtime.Composable
fun FilePathField(
    value: String,
    onValueChange: (String) -> Unit,
    label: String,
    modifier: androidx.compose.ui.Modifier = androidx.compose.ui.Modifier,
    mimeTypes: Array<String> = arrayOf("*/*"),
    folder: Boolean = false,
) {
    val browsing = androidx.compose.runtime.remember { androidx.compose.runtime.mutableStateOf(false) }
    val pickFile = rememberFilePicker { onValueChange(it) }
    val pickFolder = rememberFolderPicker { onValueChange(it) }
    androidx.compose.material3.OutlinedTextField(
        value = value,
        onValueChange = onValueChange,
        label = { androidx.compose.material3.Text(label) },
        modifier = modifier,
        singleLine = true,
        shape = androidx.compose.foundation.shape.RoundedCornerShape(28.dp),
        trailingIcon = {
            androidx.compose.foundation.layout.Row {
                // The system explorer is the primary route: it is what people know, and opening it
                // at internal storage means the pick usually resolves to a real path.
                androidx.compose.material3.FilledTonalIconButton(
                    onClick = { if (folder) pickFolder() else pickFile(mimeTypes) }
                ) {
                    androidx.compose.material3.Icon(
                        if (folder) Icons.Rounded.FolderOpen else Icons.Rounded.AttachFile,
                        contentDescription = "Browse files",
                    )
                }
                // Fallback for the cases the system picker cannot express as a path (a document
                // from Drive, or the Recent tab). Without this there is no way to reach those files
                // at all, since the core cannot take a content:// URI.
                androidx.compose.material3.IconButton(onClick = { browsing.value = true }) {
                    androidx.compose.material3.Icon(
                        Icons.Rounded.Storage,
                        contentDescription = "Browse device storage",
                    )
                }
            }
        },
    )
    if (browsing.value) {
        FileBrowserSheet(
            foldersOnly = folder,
            onPick = onValueChange,
            onDismiss = { browsing.value = false },
        )
    }
}
