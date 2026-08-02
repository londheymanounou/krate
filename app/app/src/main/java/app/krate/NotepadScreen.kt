package app.krate

import android.content.Context
import android.net.Uri
import android.provider.OpenableColumns
import android.view.ViewGroup
import android.webkit.WebView
import android.webkit.WebViewClient
import android.widget.Toast
import androidx.activity.compose.BackHandler
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.animation.AnimatedContent
import androidx.compose.animation.core.tween
import androidx.compose.animation.fadeIn
import androidx.compose.animation.fadeOut
import androidx.compose.animation.togetherWith
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.staggeredgrid.LazyVerticalStaggeredGrid
import androidx.compose.foundation.lazy.staggeredgrid.StaggeredGridCells
import androidx.compose.foundation.lazy.staggeredgrid.items
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.rounded.ArrowBack
import androidx.compose.material.icons.rounded.Add
import androidx.compose.material.icons.rounded.Code
import androidx.compose.material.icons.rounded.Delete
import androidx.compose.material.icons.rounded.FileDownload
import androidx.compose.material.icons.rounded.FileOpen
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.viewinterop.AndroidView
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import org.json.JSONArray
import org.json.JSONObject
import java.io.File
import java.util.UUID

data class Note(val id: String, var text: String)

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun NotepadScreen(modifier: Modifier = Modifier) {
    val context = LocalContext.current
    val sharedPrefs = remember { context.getSharedPreferences("notepad_prefs", Context.MODE_PRIVATE) }
    val coroutineScope = rememberCoroutineScope()
    
    // Load notes
    var notes by remember {
        mutableStateOf(
            try {
                val jsonArray = JSONArray(sharedPrefs.getString("notes_array", "[]"))
                val list = mutableListOf<Note>()
                for (i in 0 until jsonArray.length()) {
                    val obj = jsonArray.getJSONObject(i)
                    list.add(Note(obj.getString("id"), obj.getString("text")))
                }
                list.toList()
            } catch (e: Exception) {
                emptyList<Note>()
            }
        )
    }

    val saveNotes = { newNotes: List<Note> ->
        notes = newNotes
        val jsonArray = JSONArray()
        newNotes.forEach { note ->
            val obj = JSONObject().apply {
                put("id", note.id)
                put("text", note.text)
            }
            jsonArray.put(obj)
        }
        sharedPrefs.edit().putString("notes_array", jsonArray.toString()).apply()
    }

    var editingNote by remember { mutableStateOf<Note?>(null) }
    var exportTarget by remember { mutableStateOf<Note?>(null) }
    
    var showPasswordDialogForFile by remember { mutableStateOf<File?>(null) }
    var filePassword by remember { mutableStateOf("") }

    // SAF Exporters
    val exportSingleLauncher = rememberLauncherForActivityResult(ActivityResultContracts.CreateDocument("text/plain")) { uri: Uri? ->
        uri?.let { u ->
            exportTarget?.let { note ->
                context.contentResolver.openOutputStream(u)?.use { out ->
                    out.write(note.text.toByteArray())
                }
            }
        }
        exportTarget = null
    }

    val exportAllLauncher = rememberLauncherForActivityResult(ActivityResultContracts.CreateDocument("text/plain")) { uri: Uri? ->
        uri?.let { u ->
            context.contentResolver.openOutputStream(u)?.use { out ->
                val combined = notes.joinToString("\n\n---\n\n") { it.text }
                out.write(combined.toByteArray())
            }
        }
    }
    
    // Helper to get file name from Uri
    fun getFileName(uri: Uri): String {
        var result: String? = null
        if (uri.scheme == "content") {
            context.contentResolver.query(uri, null, null, null, null)?.use { cursor ->
                if (cursor.moveToFirst()) {
                    val index = cursor.getColumnIndex(OpenableColumns.DISPLAY_NAME)
                    if (index >= 0) result = cursor.getString(index)
                }
            }
        }
        return result ?: uri.path?.substringAfterLast('/') ?: "unknown"
    }

    val importLauncher = rememberLauncherForActivityResult(ActivityResultContracts.GetContent()) { uri: Uri? ->
        uri?.let { u ->
            coroutineScope.launch {
                withContext(Dispatchers.IO) {
                    try {
                        val fileName = getFileName(u)
                        val isEncrypted = fileName.lowercase().endsWith(".krate")
                        
                        val bytes = context.contentResolver.openInputStream(u)?.use { it.readBytes() } ?: return@withContext
                        
                        if (isEncrypted) {
                            val tempEnc = File(context.cacheDir, "temp_${UUID.randomUUID()}.krate")
                            tempEnc.writeBytes(bytes)
                            showPasswordDialogForFile = tempEnc
                        } else {
                            if (bytes.contains(0.toByte())) {
                                withContext(Dispatchers.Main) {
                                    Toast.makeText(context, "Not a text file", Toast.LENGTH_SHORT).show()
                                }
                            } else {
                                val text = String(bytes, Charsets.UTF_8)
                                withContext(Dispatchers.Main) {
                                    val newNote = Note(UUID.randomUUID().toString(), text)
                                    saveNotes(listOf(newNote) + notes)
                                    editingNote = newNote
                                }
                            }
                        }
                    } catch (e: Exception) {
                        e.printStackTrace()
                    }
                }
            }
        }
    }

    if (showPasswordDialogForFile != null) {
        AlertDialog(
            onDismissRequest = { 
                showPasswordDialogForFile?.delete()
                showPasswordDialogForFile = null 
                filePassword = ""
            },
            title = { Text("Decrypt File") },
            text = {
                OutlinedTextField(
                    value = filePassword,
                    onValueChange = { filePassword = it },
                    label = { Text("Password") },
                    visualTransformation = PasswordVisualTransformation(),
                    singleLine = true
                )
            },
            confirmButton = {
                Button(onClick = {
                    coroutineScope.launch {
                        val tempEnc = showPasswordDialogForFile!!
                        val pwd = filePassword
                        
                        showPasswordDialogForFile = null
                        filePassword = ""
                        
                        withContext(Dispatchers.IO) {
                            val res = Core.run("Decrypt", "${tempEnc.absolutePath} | $pwd")
                            if (res.ok) {
                                val outPath = tempEnc.absolutePath.dropLast(6)
                                val decFile = File(outPath)
                                if (decFile.exists()) {
                                    val bytes = decFile.readBytes()
                                    decFile.delete()
                                    if (bytes.contains(0.toByte())) {
                                        withContext(Dispatchers.Main) {
                                            Toast.makeText(context, "Decrypted file is not a text file", Toast.LENGTH_SHORT).show()
                                        }
                                    } else {
                                        val text = String(bytes, Charsets.UTF_8)
                                        withContext(Dispatchers.Main) {
                                            val newNote = Note(UUID.randomUUID().toString(), text)
                                            saveNotes(listOf(newNote) + notes)
                                            editingNote = newNote
                                        }
                                    }
                                }
                            } else {
                                withContext(Dispatchers.Main) {
                                    Toast.makeText(context, "Decryption failed: ${res.text}", Toast.LENGTH_LONG).show()
                                }
                            }
                            tempEnc.delete()
                        }
                    }
                }) { Text("Decrypt") }
            },
            dismissButton = {
                TextButton(onClick = { 
                    showPasswordDialogForFile?.delete()
                    showPasswordDialogForFile = null 
                    filePassword = ""
                }) { Text("Cancel") }
            }
        )
    }

    BackHandler(enabled = editingNote != null) {
        editingNote = null
    }

    AnimatedContent(
        targetState = editingNote,
        transitionSpec = { fadeIn(tween(200)) togetherWith fadeOut(tween(200)) },
        label = "Notepad Navigation"
    ) { currentEditingNote ->
        if (currentEditingNote == null) {
            Scaffold(
                modifier = modifier.fillMaxSize(),
                floatingActionButton = {
                    FloatingActionButton(onClick = {
                        val newNote = Note(UUID.randomUUID().toString(), "")
                        editingNote = newNote
                        saveNotes(listOf(newNote) + notes)
                    }) {
                        Icon(Icons.Rounded.Add, contentDescription = "Add Note")
                    }
                },
                containerColor = Color.Transparent
            ) { padding ->
                Column(modifier = Modifier.padding(padding).fillMaxSize()) {
                    Row(
                        modifier = Modifier.fillMaxWidth().padding(horizontal = 16.dp, vertical = 8.dp),
                        horizontalArrangement = Arrangement.SpaceBetween
                    ) {
                        TextButton(onClick = {
                            importLauncher.launch("*/*")
                        }) {
                            Icon(Icons.Rounded.FileOpen, contentDescription = null, modifier = Modifier.size(18.dp))
                            Spacer(Modifier.width(8.dp))
                            Text("Open File")
                        }
                        
                        TextButton(onClick = {
                            exportAllLauncher.launch("All_Notes.txt")
                        }) {
                            Icon(Icons.Rounded.FileDownload, contentDescription = null, modifier = Modifier.size(18.dp))
                            Spacer(Modifier.width(8.dp))
                            Text("Export All")
                        }
                    }

                    if (notes.isEmpty()) {
                        Box(modifier = Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
                            Text("No notes yet. Tap + to add one.", color = MaterialTheme.colorScheme.onSurfaceVariant)
                        }
                    } else {
                        LazyVerticalStaggeredGrid(
                            columns = StaggeredGridCells.Adaptive(150.dp),
                            modifier = Modifier.fillMaxSize().padding(horizontal = 12.dp),
                            horizontalArrangement = Arrangement.spacedBy(8.dp),
                            verticalItemSpacing = 8.dp
                        ) {
                            items(notes, key = { it.id }) { note ->
                                Surface(
                                    modifier = Modifier
                                        .fillMaxWidth()
                                        .clip(RoundedCornerShape(16.dp))
                                        .clickable { editingNote = note },
                                    color = MaterialTheme.colorScheme.surfaceVariant,
                                    tonalElevation = 2.dp
                                ) {
                                    Text(
                                        text = note.text.ifBlank { "Empty note" },
                                        style = MaterialTheme.typography.bodyMedium,
                                        maxLines = 8,
                                        overflow = TextOverflow.Ellipsis,
                                        modifier = Modifier.padding(16.dp)
                                    )
                                }
                            }
                        }
                    }
                }
            }
        } else {
            var textState by remember { mutableStateOf(currentEditingNote.text) }
            var showMarkdown by remember { mutableStateOf(false) }

            LaunchedEffect(textState) {
                if (textState != currentEditingNote.text) {
                    val updatedNotes = notes.map { if (it.id == currentEditingNote.id) it.copy(text = textState) else it }
                    saveNotes(updatedNotes)
                    currentEditingNote.text = textState
                }
            }

            Column(modifier = modifier.fillMaxSize().padding(16.dp)) {
                Row(
                    modifier = Modifier.fillMaxWidth().padding(bottom = 8.dp),
                    horizontalArrangement = Arrangement.SpaceBetween,
                    verticalAlignment = Alignment.CenterVertically
                ) {
                    IconButton(onClick = { editingNote = null }) {
                        Icon(Icons.AutoMirrored.Rounded.ArrowBack, contentDescription = "Back")
                    }
                    Row {
                        IconButton(onClick = {
                            exportTarget = currentEditingNote
                            exportSingleLauncher.launch("Note_${currentEditingNote.id.take(6)}.txt")
                        }) {
                            Icon(Icons.Rounded.FileDownload, contentDescription = "Export TXT")
                        }
                        IconButton(onClick = { showMarkdown = !showMarkdown }) {
                            Icon(Icons.Rounded.Code, contentDescription = "Toggle Markdown")
                        }
                        IconButton(onClick = {
                            val updatedNotes = notes.filter { it.id != currentEditingNote.id }
                            saveNotes(updatedNotes)
                            editingNote = null
                        }) {
                            Icon(Icons.Rounded.Delete, contentDescription = "Delete", tint = MaterialTheme.colorScheme.error)
                        }
                    }
                }

                if (showMarkdown) {
                    val htmlResult = remember(textState) {
                        if (textState.isBlank()) "" 
                        else {
                            val res = Core.run("MarkdownToHtml", textState)
                            res.text
                        }
                    }
                    val fullHtml = """
                        <html>
                        <head>
                            <style>
                                body { font-family: sans-serif; padding: 16px; color: #333; background: #fff; }
                                pre { background: #f4f4f4; padding: 12px; border-radius: 8px; overflow-x: auto; }
                                code { font-family: monospace; }
                                blockquote { border-left: 4px solid #ccc; margin: 0; padding-left: 16px; color: #666; }
                            </style>
                        </head>
                        <body>${htmlResult}</body>
                        </html>
                    """.trimIndent()
                    
                    AndroidView(
                        factory = { ctx ->
                            WebView(ctx).apply {
                                layoutParams = ViewGroup.LayoutParams(
                                    ViewGroup.LayoutParams.MATCH_PARENT,
                                    ViewGroup.LayoutParams.MATCH_PARENT
                                )
                                webViewClient = WebViewClient()
                            }
                        },
                        update = { webView ->
                            webView.loadDataWithBaseURL(null, fullHtml, "text/html", "UTF-8", null)
                        },
                        modifier = Modifier.fillMaxWidth().weight(1f).clip(RoundedCornerShape(8.dp))
                    )
                } else {
                    OutlinedTextField(
                        value = textState,
                        onValueChange = { textState = it },
                        modifier = Modifier.fillMaxWidth().weight(1f),
                        textStyle = MaterialTheme.typography.bodyLarge,
                        placeholder = { Text("Note content...") },
                        colors = OutlinedTextFieldDefaults.colors(
                            focusedBorderColor = Color.Transparent,
                            unfocusedBorderColor = Color.Transparent,
                            focusedContainerColor = MaterialTheme.colorScheme.surfaceVariant.copy(alpha = 0.2f),
                            unfocusedContainerColor = MaterialTheme.colorScheme.surfaceVariant.copy(alpha = 0.2f)
                        )
                    )
                }
            }
        }
    }
}
