package app.krate

import android.app.Application
import android.content.Context
import android.os.Environment
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.rounded.Download
import androidx.compose.material.icons.rounded.Folder
import androidx.compose.material.icons.rounded.MusicNote
import androidx.compose.material.icons.rounded.MusicNote
import androidx.compose.material.icons.rounded.Movie
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import com.yausername.youtubedl_android.YoutubeDL
import com.yausername.youtubedl_android.YoutubeDLRequest
import com.yausername.ffmpeg.FFmpeg
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import java.io.File

/**
 * Media downloader, in the shape Seal uses: paste a URL, choose video or audio, download.
 *
 * yt-dlp is Python, so this pulls in a bundled interpreter (`youtubedl-android`) — that is the
 * dependency that makes the APK large, and the reason this tool exists here rather than shelling
 * out to a binary the way the desktop does.
 *
 * `YoutubeDL.init` unpacks that interpreter on first use and takes seconds, so it happens off the
 * main thread and only once per process.
 */
object Downloader {
    @Volatile private var ready = false

    suspend fun ensureReady(context: Context) {
        if (ready) return
        withContext(Dispatchers.IO) {
            synchronized(this@Downloader) {
                if (!ready) {
                    val app = context.applicationContext as Application
                    YoutubeDL.getInstance().init(app)
                    // ffmpeg is what merges separate video and audio streams, and what extracts
                    // audio-only output. Without it the best formats silently fail to combine.
                    FFmpeg.getInstance().init(app)
                    ready = true
                }
            }
        }
    }

    /**
     * Pulls the current yt-dlp.
     *
     * The version bundled in the library is months old, and YouTube rejects it outright
     * ("Precondition check failed", HTTP 400) — extraction breaks constantly and only a current
     * yt-dlp keeps working. Seal does the same thing. Failure is swallowed: an outdated yt-dlp that
     * might still work beats refusing to start because the update server was unreachable.
     */
    suspend fun update(context: Context): Boolean = withContext(Dispatchers.IO) {
        runCatching {
            YoutubeDL.getInstance().updateYoutubeDL(
                context.applicationContext as Application,
                YoutubeDL.UpdateChannel.STABLE,
            )
            true
        }.getOrDefault(false)
    }

    /** Where Android expects downloads to land, so other apps can see them. */
    fun destination(): File =
        File(Environment.getExternalStoragePublicDirectory(Environment.DIRECTORY_DOWNLOADS), "KRATE")
            .apply { mkdirs() }
}

@Composable
fun DownloaderScreen(modifier: Modifier = Modifier) {
    val context = androidx.compose.ui.platform.LocalContext.current
    var url by remember { mutableStateOf("") }
    var isAudio by remember { mutableStateOf(false) }
    var videoFormat by remember { mutableStateOf("mp4") }
    var audioFormat by remember { mutableStateOf("mp3") }
    var videoTitle by remember { mutableStateOf("") }
    var videoThumbnail by remember { mutableStateOf("") }
    var videoUploader by remember { mutableStateOf("") }
    var status by remember { mutableStateOf("") }
    var progress by remember { mutableFloatStateOf(0f) }
    var busy by remember { mutableStateOf(false) }
    var error by remember { mutableStateOf(false) }
    var savedPath by remember { mutableStateOf<String?>(null) }
    // Update once per screen visit, not per download.
    var updated by remember { mutableStateOf(false) }
    val scope = rememberCoroutineScope()

    fun download() {
        if (url.isBlank() || busy) return
        scope.launch {
            busy = true
            error = false
            savedPath = null
            progress = 0f
            status = "Preparing…"
            val result = runCatching {
                Downloader.ensureReady(context)
                if (!updated) {
                    Downloader.update(context)
                    updated = true
                }
                status = "Fetching metadata…"
                try {
                    val infoReq = YoutubeDLRequest(url.trim()).apply {
                        addOption("--dump-json")
                        addOption("--no-playlist")
                        addOption("--extractor-args", "youtube:player_client=tv,ios,android")
                    }
                    val res = withContext(Dispatchers.IO) { YoutubeDL.getInstance().execute(infoReq) }
                    val json = org.json.JSONObject(res.out)
                    videoTitle = json.optString("title", "")
                    videoThumbnail = json.optString("thumbnail", "")
                    videoUploader = json.optString("uploader", "")
                } catch (e: Exception) {
                    // Ignore metadata fetch errors and proceed
                }
                
                status = "Downloading…"
                val dir = Downloader.destination()
                val request = YoutubeDLRequest(url.trim()).apply {
                    addOption("-o", dir.absolutePath + "/%(title)s.%(ext)s")
                    addOption("--no-mtime")
                    // YouTube's default web client needs a JavaScript engine to solve the `n`
                    // challenge, and Android has none ("No supported JavaScript runtime").
                    // The tv/ios/android clients serve URLs that need no such deciphering.
                    addOption("--extractor-args", "youtube:player_client=tv,ios,android")
                    if (isAudio) {
                        addOption("-x")
                        addOption("--audio-format", audioFormat)
                    } else {
                        // Cap at 1080p: on a phone the 4K variants are enormous and merge slowly.
                        addOption("-f", "bestvideo[height<=1080]+bestaudio/best")
                        addOption("--remux-video", videoFormat)
                    }
                }
                withContext(Dispatchers.IO) {
                    YoutubeDL.getInstance().execute(request) { percent, _, line ->
                        progress = (percent / 100f).coerceIn(0f, 1f)
                        if (line.isNotBlank()) status = line.take(90)
                    }
                }
                dir.absolutePath
            }
            result.onSuccess {
                savedPath = it
                status = "Saved to: $it"
                progress = 1f
            }.onFailure {
                error = true
                status = it.message?.take(200) ?: "Download failed"
            }
            busy = false
        }
    }
    
    // Clear metadata when url changes significantly
    LaunchedEffect(url) {
        if (url.isBlank()) {
            videoTitle = ""
            videoThumbnail = ""
            videoUploader = ""
            status = ""
            progress = 0f
            error = false
        }
    }

    Column(
        modifier.fillMaxSize().padding(20.dp),
        verticalArrangement = Arrangement.spacedBy(16.dp),
    ) {
        OutlinedTextField(
            value = url,
            onValueChange = { url = it },
            label = { Text("Video or playlist URL") },
            placeholder = { Text("https://…") },
            singleLine = true,
            shape = RoundedCornerShape(28.dp),
            modifier = Modifier.fillMaxWidth(),
        )

        // Two buttons rather than a switch: "video or audio" is a choice, not a setting.
        Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
            FilterChip(
                selected = !isAudio,
                onClick = { isAudio = false },
                label = { Text("Video") },
                leadingIcon = { Icon(Icons.Rounded.Movie, null, Modifier.size(18.dp)) },
            )
            FilterChip(
                selected = isAudio,
                onClick = { isAudio = true },
                label = { Text("Audio") },
                leadingIcon = { Icon(Icons.Rounded.MusicNote, null, Modifier.size(18.dp)) },
            )
        }

        // Format Selection
        androidx.compose.foundation.lazy.LazyRow(
            horizontalArrangement = Arrangement.spacedBy(8.dp),
            modifier = Modifier.fillMaxWidth()
        ) {
            val formats = if (isAudio) listOf("mp3", "m4a", "wav", "flac") else listOf("mp4", "mkv", "webm")
            items(formats.size, key = { formats[it] }) { index ->
                val format = formats[index]
                val isSelected = if (isAudio) audioFormat == format else videoFormat == format
                FilterChip(
                    selected = isSelected,
                    onClick = { if (isAudio) audioFormat = format else videoFormat = format },
                    label = { Text(format.uppercase()) }
                )
            }
        }

        Button(
            onClick = { download() },
            enabled = url.isNotBlank() && !busy,
            modifier = Modifier.fillMaxWidth().height(56.dp),
            shape = RoundedCornerShape(28.dp),
        ) {
            Icon(Icons.Rounded.Download, null, Modifier.size(20.dp))
            Spacer(Modifier.width(8.dp))
            Text(if (busy) "Downloading…" else "Download", style = MaterialTheme.typography.titleMedium)
        }

        if (busy || progress > 0f) {
            LinearProgressIndicator(
                progress = { progress },
                modifier = Modifier.fillMaxWidth().height(8.dp).clip(RoundedCornerShape(4.dp)),
            )
        }

        Surface(
            shape = RoundedCornerShape(24.dp),
            color = if (error) MaterialTheme.colorScheme.errorContainer
            else MaterialTheme.colorScheme.surfaceContainerHigh,
            modifier = Modifier.fillMaxWidth().weight(1f),
        ) {
            Box(Modifier.fillMaxSize().padding(20.dp), contentAlignment = Alignment.Center) {
                if (videoTitle.isNotEmpty()) {
                    Column(
                        horizontalAlignment = Alignment.CenterHorizontally,
                        verticalArrangement = Arrangement.spacedBy(16.dp),
                        modifier = Modifier.fillMaxSize()
                    ) {
                        if (videoThumbnail.isNotEmpty()) {
                            coil.compose.AsyncImage(
                                model = videoThumbnail,
                                contentDescription = "Thumbnail",
                                modifier = Modifier.fillMaxWidth().weight(1f).clip(RoundedCornerShape(12.dp)),
                                contentScale = androidx.compose.ui.layout.ContentScale.Crop
                            )
                        } else {
                            Spacer(Modifier.weight(1f))
                        }
                        Text(
                            text = videoTitle,
                            style = MaterialTheme.typography.titleMedium,
                            color = MaterialTheme.colorScheme.onSurface,
                            textAlign = androidx.compose.ui.text.style.TextAlign.Center,
                            maxLines = 2,
                            overflow = androidx.compose.ui.text.style.TextOverflow.Ellipsis
                        )
                        if (videoUploader.isNotEmpty()) {
                            Text(
                                text = videoUploader,
                                style = MaterialTheme.typography.bodyMedium,
                                color = MaterialTheme.colorScheme.onSurfaceVariant
                            )
                        }
                        Text(
                            text = status.ifEmpty { "Downloading..." },
                            style = MaterialTheme.typography.bodySmall,
                            color = MaterialTheme.colorScheme.onSurfaceVariant,
                            textAlign = androidx.compose.ui.text.style.TextAlign.Center
                        )
                        if (savedPath != null) {
                            Button(
                                onClick = {
                                    val intent = android.content.Intent(android.app.DownloadManager.ACTION_VIEW_DOWNLOADS)
                                    intent.addFlags(android.content.Intent.FLAG_ACTIVITY_NEW_TASK)
                                    try {
                                        context.startActivity(intent)
                                    } catch (e: Exception) {
                                        // Some devices might not have a DownloadManager UI component
                                        android.widget.Toast.makeText(context, "Could not open Downloads", android.widget.Toast.LENGTH_SHORT).show()
                                    }
                                }
                            ) {
                                Icon(Icons.Rounded.Folder, contentDescription = null)
                                Spacer(Modifier.width(8.dp))
                                Text("Open Downloads Folder")
                            }
                        }
                    }
                } else {
                    Text(
                        status.ifEmpty {
                            "Paste a link and choose a format.\n\nDownloads go to Download/KRATE."
                    },
                    style = MaterialTheme.typography.bodyMedium,
                    textAlign = TextAlign.Center,
                    color = if (error) MaterialTheme.colorScheme.onErrorContainer
                    else MaterialTheme.colorScheme.onSurfaceVariant,
                )
                }
            }
        }
        Text(
            "Uses yt-dlp. This tool goes online.",
            style = MaterialTheme.typography.bodySmall,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
            fontWeight = FontWeight.Medium,
        )
    }
}
