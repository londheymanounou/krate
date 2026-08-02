package app.krate

import android.app.Application
import android.content.Context
import android.net.Uri
import android.os.Environment
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.animation.AnimatedVisibility
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.rounded.*
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import com.yausername.ffmpeg.FFmpeg
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import java.io.File

data class MediaFormat(val id: String, val category: String, val extension: String, val args: List<String>)

val mediaFormats = listOf(
    // Audio
    MediaFormat("mp3", "audio", ".mp3", listOf("-vn", "-c:a", "libmp3lame", "-q:a", "2")),
    MediaFormat("wav", "audio", ".wav", listOf("-vn", "-c:a", "pcm_s16le")),
    MediaFormat("flac", "audio", ".flac", listOf("-vn", "-c:a", "flac")),
    MediaFormat("ogg", "audio", ".ogg", listOf("-vn", "-c:a", "libopus", "-b:a", "128k")),
    MediaFormat("aac", "audio", ".m4a", listOf("-vn", "-c:a", "aac", "-b:a", "192k")),
    // Video
    MediaFormat("mp4", "video", ".mp4", listOf("-c:v", "libx264", "-crf", "23", "-preset", "medium", "-pix_fmt", "yuv420p", "-c:a", "aac", "-b:a", "192k")),
    MediaFormat("mkv", "video", ".mkv", listOf("-c:v", "libx264", "-crf", "23", "-preset", "medium", "-pix_fmt", "yuv420p", "-c:a", "aac", "-b:a", "192k")),
    MediaFormat("webm", "video", ".webm", listOf("-c:v", "libvpx-vp9", "-b:v", "0", "-crf", "32", "-c:a", "libopus")),
    MediaFormat("avi", "video", ".avi", listOf("-c:v", "mpeg4", "-q:v", "4", "-c:a", "libmp3lame", "-q:a", "3")),
    MediaFormat("gif", "video", ".gif", listOf("-vf", "fps=12,scale=480:-1:flags=lanczos")),
    // Image
    MediaFormat("png", "image", ".png", listOf("-frames:v", "1")),
    MediaFormat("jpg", "image", ".jpg", listOf("-frames:v", "1", "-q:v", "3")),
    MediaFormat("webp", "image", ".webp", listOf("-frames:v", "1", "-c:v", "libwebp", "-quality", "80")),
    MediaFormat("avif", "image", ".avif", listOf("-frames:v", "1", "-c:v", "libaom-av1", "-crf", "30", "-still-picture", "1"))
)

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun ConverterScreen(modifier: Modifier = Modifier) {
    val context = LocalContext.current
    var selectedFilePath by remember { mutableStateOf<String?>(null) }
    var selectedFileName by remember { mutableStateOf("") }
    var selectedFormat by remember { mutableStateOf(mediaFormats.first()) }
    var isDropdownExpanded by remember { mutableStateOf(false) }

    var status by remember { mutableStateOf("") }
    var progress by remember { mutableFloatStateOf(0f) }
    var busy by remember { mutableStateOf(false) }
    var error by remember { mutableStateOf(false) }
    var savedPath by remember { mutableStateOf<String?>(null) }
    val scope = rememberCoroutineScope()

    val pickFile = rememberFilePicker { path ->
        selectedFilePath = path
        selectedFileName = File(path).name
    }

    fun convert() {
        val inputPath = selectedFilePath ?: return
        if (busy) return

        scope.launch {
            busy = true
            error = false
            progress = 0f
            status = "Initializing FFmpeg…"

            runCatching {
                withContext(Dispatchers.IO) {
                    val app = context.applicationContext as Application
                    FFmpeg.getInstance().init(app)
                }

                status = "Converting to ${selectedFormat.id.uppercase()}…"

                val inputFile = File(inputPath)
                var dir = inputFile.parentFile
                if (dir == null || dir.absolutePath.startsWith(context.cacheDir.absolutePath)) {
                    dir = File(Environment.getExternalStoragePublicDirectory(Environment.DIRECTORY_DOWNLOADS), "KRATE")
                    dir.mkdirs()
                }
                
                val nameWithoutExt = selectedFileName.substringBeforeLast(".")
                var outputFile = File(dir, "$nameWithoutExt${selectedFormat.extension}")
                var counter = 1
                while (outputFile.exists()) {
                    outputFile = File(dir, "$nameWithoutExt ($counter)${selectedFormat.extension}")
                    counter++
                }

                val nativeDir = context.applicationInfo.nativeLibraryDir
                val ffmpegBin = File(nativeDir, "libffmpeg.so")
                
                if (!ffmpegBin.exists()) {
                    throw Exception("FFmpeg binary not found at: ${ffmpegBin.absolutePath}")
                }
                
                // Add extracted ffmpeg libraries to LD_LIBRARY_PATH
                val ffmpegLibsDir = File(context.noBackupFilesDir, "youtubedl-android/packages/ffmpeg/usr/lib")
                
                val cmd = mutableListOf<String>()
                cmd.add(ffmpegBin.absolutePath)
                cmd.add("-y")
                cmd.add("-i")
                cmd.add(inputFile.absolutePath)
                cmd.addAll(selectedFormat.args)
                cmd.add(outputFile.absolutePath)

                withContext(Dispatchers.IO) {
                    val pb = ProcessBuilder(cmd)
                    val env = pb.environment()
                    if (ffmpegLibsDir.exists()) {
                        val currentLd = env["LD_LIBRARY_PATH"] ?: ""
                        env["LD_LIBRARY_PATH"] = "${ffmpegLibsDir.absolutePath}:$currentLd"
                    }
                    
                    pb.redirectErrorStream(true)
                    val process = pb.start()
                    val output = process.inputStream.bufferedReader().readText()
                    process.waitFor()
                    if (process.exitValue() != 0) {
                        // Extract only the bottom of the output which contains the actual error
                        val lines = output.lines()
                        val relevantError = if (lines.size > 20) {
                            "..." + lines.takeLast(20).joinToString("\n")
                        } else {
                            output
                        }
                        throw Exception("FFmpeg failed: $relevantError")
                    }
                }
                
                outputFile.absolutePath
            }.onSuccess { path ->
                savedPath = path
                status = "Saved to:\n$path"
                progress = 1f
            }.onFailure {
                error = true
                status = it.message ?: "Conversion failed"
            }
            
            busy = false
        }
    }

    Column(
        modifier = modifier
            .fillMaxSize()
            .verticalScroll(rememberScrollState())
            .padding(24.dp),
        verticalArrangement = Arrangement.spacedBy(24.dp)
    ) {
        // Source File Section
        Surface(
            shape = RoundedCornerShape(24.dp),
            color = MaterialTheme.colorScheme.surfaceContainerHigh,
            onClick = { if (!busy) pickFile(arrayOf("*/*")) },
            modifier = Modifier.fillMaxWidth()
        ) {
            Row(
                modifier = Modifier.padding(20.dp),
                verticalAlignment = Alignment.CenterVertically,
                horizontalArrangement = Arrangement.spacedBy(16.dp)
            ) {
                Surface(
                    shape = RoundedCornerShape(16.dp),
                    color = MaterialTheme.colorScheme.primaryContainer,
                    modifier = Modifier.size(56.dp)
                ) {
                    Icon(
                        imageVector = if (selectedFilePath != null) Icons.Rounded.FilePresent else Icons.Rounded.UploadFile,
                        contentDescription = null,
                        modifier = Modifier
                            .padding(16.dp)
                            .fillMaxSize(),
                        tint = MaterialTheme.colorScheme.onPrimaryContainer
                    )
                }
                
                Column(modifier = Modifier.weight(1f)) {
                    Text(
                        text = if (selectedFilePath != null) "Source file" else "Select a file",
                        style = MaterialTheme.typography.labelMedium,
                        color = MaterialTheme.colorScheme.primary
                    )
                    Text(
                        text = if (selectedFilePath != null) selectedFileName else "Tap to browse",
                        style = MaterialTheme.typography.titleMedium,
                        color = MaterialTheme.colorScheme.onSurface,
                        maxLines = 1,
                        overflow = TextOverflow.Ellipsis
                    )
                }
            }
        }

        // Format Selection Section
        AnimatedVisibility(visible = selectedFilePath != null) {
            Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
                Text(
                    text = "Convert to",
                    style = MaterialTheme.typography.labelLarge,
                    color = MaterialTheme.colorScheme.primary,
                    modifier = Modifier.padding(horizontal = 8.dp)
                )
                
                ExposedDropdownMenuBox(
                    expanded = isDropdownExpanded,
                    onExpandedChange = { if (!busy) isDropdownExpanded = it }
                ) {
                    OutlinedTextField(
                        value = "${selectedFormat.id.uppercase()} · ${selectedFormat.category.replaceFirstChar { it.uppercase() }}",
                        onValueChange = {},
                        readOnly = true,
                        trailingIcon = { ExposedDropdownMenuDefaults.TrailingIcon(expanded = isDropdownExpanded) },
                        colors = ExposedDropdownMenuDefaults.outlinedTextFieldColors(),
                        shape = RoundedCornerShape(20.dp),
                        modifier = Modifier
                            .fillMaxWidth()
                            .menuAnchor()
                    )

                    ExposedDropdownMenu(
                        expanded = isDropdownExpanded,
                        onDismissRequest = { isDropdownExpanded = false },
                        modifier = Modifier.fillMaxWidth()
                    ) {
                        val categories = mediaFormats.groupBy { it.category }
                        categories.forEach { (category, formats) ->
                            DropdownMenuItem(
                                text = { 
                                    Text(
                                        category.replaceFirstChar { it.uppercase() }, 
                                        color = MaterialTheme.colorScheme.primary,
                                        style = MaterialTheme.typography.labelMedium
                                    )
                                },
                                onClick = {},
                                enabled = false
                            )
                            formats.forEach { format ->
                                DropdownMenuItem(
                                    text = { Text(format.id.uppercase()) },
                                    onClick = {
                                        selectedFormat = format
                                        isDropdownExpanded = false
                                    }
                                )
                            }
                        }
                    }
                }
            }
        }

        // Convert Button
        AnimatedVisibility(visible = selectedFilePath != null) {
            Button(
                onClick = { convert() },
                enabled = !busy,
                modifier = Modifier
                    .fillMaxWidth()
                    .height(64.dp),
                shape = RoundedCornerShape(32.dp),
                colors = ButtonDefaults.buttonColors(
                    containerColor = MaterialTheme.colorScheme.primary,
                    contentColor = MaterialTheme.colorScheme.onPrimary
                )
            ) {
                if (busy) {
                    CircularProgressIndicator(
                        modifier = Modifier.size(24.dp),
                        color = MaterialTheme.colorScheme.onPrimary,
                        strokeWidth = 2.5.dp
                    )
                    Spacer(Modifier.width(12.dp))
                    Text("Converting…", style = MaterialTheme.typography.titleMedium)
                } else {
                    Icon(Icons.Rounded.Transform, null, Modifier.size(24.dp))
                    Spacer(Modifier.width(12.dp))
                    Text("Convert File", style = MaterialTheme.typography.titleMedium)
                }
            }
        }

        // Status Area
        AnimatedVisibility(visible = busy || progress > 0f || status.isNotEmpty()) {
            Surface(
                shape = RoundedCornerShape(24.dp),
                color = if (error) MaterialTheme.colorScheme.errorContainer
                else MaterialTheme.colorScheme.surfaceContainer,
                modifier = Modifier.fillMaxWidth()
            ) {
                Column(
                    modifier = Modifier.padding(20.dp),
                    horizontalAlignment = Alignment.CenterHorizontally,
                    verticalArrangement = Arrangement.spacedBy(16.dp)
                ) {
                    if (busy || progress > 0f) {
                        LinearProgressIndicator(
                            progress = { if (busy && progress == 0f) 0.5f else progress }, // fake progress or real if available
                            modifier = Modifier
                                .fillMaxWidth()
                                .height(6.dp)
                                .clip(RoundedCornerShape(3.dp)),
                            color = if (error) MaterialTheme.colorScheme.error else MaterialTheme.colorScheme.primary
                        )
                    }
                    
                    if (status.isNotEmpty()) {
                        Text(
                            text = status,
                            style = MaterialTheme.typography.bodyMedium,
                            textAlign = TextAlign.Center,
                            color = if (error) MaterialTheme.colorScheme.onErrorContainer
                            else MaterialTheme.colorScheme.onSurfaceVariant
                        )
                    }
                    if (savedPath != null) {
                        Button(
                            onClick = {
                                val intent = android.content.Intent(android.app.DownloadManager.ACTION_VIEW_DOWNLOADS)
                                intent.addFlags(android.content.Intent.FLAG_ACTIVITY_NEW_TASK)
                                try {
                                    context.startActivity(intent)
                                } catch (e: Exception) {
                                    android.widget.Toast.makeText(context, "Could not open file manager", android.widget.Toast.LENGTH_SHORT).show()
                                }
                            }
                        ) {
                            Icon(Icons.Rounded.Folder, contentDescription = null)
                            Spacer(Modifier.width(8.dp))
                            Text("Open File Location")
                        }
                    }
                }
            }
        }
    }
}
