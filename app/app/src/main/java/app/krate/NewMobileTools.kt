package app.krate

import android.graphics.Bitmap
import android.graphics.BitmapFactory
import android.net.Uri
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.animation.AnimatedContent
import androidx.compose.animation.AnimatedVisibility
import androidx.compose.animation.animateColorAsState
import androidx.compose.animation.core.animateFloatAsState
import androidx.compose.animation.fadeIn
import androidx.compose.animation.fadeOut
import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.gestures.detectTapGestures
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyRow
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.selection.SelectionContainer
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.rounded.ContentCopy
import androidx.compose.material.icons.rounded.ImageSearch
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.platform.LocalClipboardManager
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.AnnotatedString
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext

@Composable
fun JwtScreen(modifier: Modifier = Modifier) {
    var token by remember { mutableStateOf("") }
    var result by remember { mutableStateOf("") }
    var isError by remember { mutableStateOf(false) }
    val scope = rememberCoroutineScope()
    val clipboardManager = LocalClipboardManager.current

    LaunchedEffect(token) {
        if (token.isBlank()) { result = ""; isError = false; return@LaunchedEffect }
        scope.launch {
            val res = withContext(Dispatchers.IO) { Core.run("Jwt", token.trim()) }
            result = res.text
            isError = !res.ok
        }
    }

    Column(modifier.fillMaxSize().padding(20.dp), verticalArrangement = Arrangement.spacedBy(16.dp)) {
        OutlinedTextField(
            value = token,
            onValueChange = { token = it },
            label = { Text("JWT Token") },
            placeholder = { Text("eyJhbG...") },
            modifier = Modifier.fillMaxWidth(),
            shape = RoundedCornerShape(24.dp),
            minLines = 3,
            maxLines = 5,
            colors = OutlinedTextFieldDefaults.colors(
                focusedBorderColor = MaterialTheme.colorScheme.primary,
                unfocusedBorderColor = MaterialTheme.colorScheme.outline
            )
        )
        
        AnimatedVisibility(visible = result.isNotEmpty(), enter = fadeIn(), exit = fadeOut()) {
            Card(
                shape = RoundedCornerShape(28.dp),
                colors = CardDefaults.cardColors(
                    containerColor = if (isError) MaterialTheme.colorScheme.errorContainer else MaterialTheme.colorScheme.surfaceVariant
                ),
                modifier = Modifier.fillMaxWidth().weight(1f, fill = false),
            ) {
                Column(Modifier.fillMaxSize()) {
                    Row(
                        modifier = Modifier.fillMaxWidth().padding(start = 24.dp, top = 12.dp, end = 12.dp),
                        horizontalArrangement = Arrangement.SpaceBetween,
                        verticalAlignment = Alignment.CenterVertically
                    ) {
                        Text("Decoded Payload", style = MaterialTheme.typography.titleMedium, color = if (isError) MaterialTheme.colorScheme.onErrorContainer else MaterialTheme.colorScheme.onSurfaceVariant)
                        if (!isError && result.isNotEmpty()) {
                            IconButton(onClick = { clipboardManager.setText(AnnotatedString(result)) }) {
                                Icon(Icons.Rounded.ContentCopy, contentDescription = "Copy", tint = MaterialTheme.colorScheme.primary)
                            }
                        }
                    }
                    val scroll = rememberScrollState()
                    Box(Modifier.fillMaxWidth().weight(1f).verticalScroll(scroll).padding(horizontal = 24.dp, vertical = 12.dp)) {
                        SelectionContainer {
                            Text(
                                result,
                                style = androidx.compose.ui.text.TextStyle(fontFamily = FontFamily.Monospace),
                                color = if (isError) MaterialTheme.colorScheme.onErrorContainer else MaterialTheme.colorScheme.onSurfaceVariant,
                            )
                        }
                    }
                }
            }
        }
    }
}

@Composable
fun PomodoroScreen(modifier: Modifier = Modifier) {
    var totalTime by remember { mutableIntStateOf(25 * 60) }
    var timeLeft by remember { mutableIntStateOf(25 * 60) }
    var isRunning by remember { mutableStateOf(false) }
    var currentMode by remember { mutableStateOf("Work (25m)") }

    LaunchedEffect(isRunning, timeLeft) {
        if (isRunning && timeLeft > 0) {
            delay(1000)
            timeLeft--
        } else if (timeLeft == 0) {
            isRunning = false
        }
    }

    val progress by animateFloatAsState(
        targetValue = timeLeft.toFloat() / totalTime.toFloat(),
        animationSpec = androidx.compose.animation.core.tween(1000, easing = androidx.compose.animation.core.LinearEasing),
        label = "progress"
    )

    Column(
        modifier = modifier.fillMaxSize().padding(24.dp),
        horizontalAlignment = Alignment.CenterHorizontally,
        verticalArrangement = Arrangement.Center
    ) {
        Row(horizontalArrangement = Arrangement.spacedBy(12.dp)) {
            FilterChip(
                selected = currentMode == "Work (25m)",
                onClick = { currentMode = "Work (25m)"; totalTime = 25 * 60; timeLeft = totalTime; isRunning = false },
                label = { Text("Work") },
                shape = RoundedCornerShape(16.dp)
            )
            FilterChip(
                selected = currentMode == "Break (5m)",
                onClick = { currentMode = "Break (5m)"; totalTime = 5 * 60; timeLeft = totalTime; isRunning = false },
                label = { Text("Break") },
                shape = RoundedCornerShape(16.dp)
            )
        }
        Spacer(Modifier.height(48.dp))
        
        Box(contentAlignment = Alignment.Center, modifier = Modifier.size(280.dp)) {
            CircularProgressIndicator(
                progress = { progress },
                modifier = Modifier.fillMaxSize(),
                color = MaterialTheme.colorScheme.primary,
                trackColor = MaterialTheme.colorScheme.surfaceVariant,
                strokeWidth = 12.dp,
                strokeCap = androidx.compose.ui.graphics.StrokeCap.Round
            )
            val m = timeLeft / 60
            val s = timeLeft % 60
            Text(
                text = String.format("%02d:%02d", m, s),
                style = MaterialTheme.typography.displayLarge.copy(fontWeight = FontWeight.Bold),
                color = MaterialTheme.colorScheme.onSurface
            )
        }
        
        Spacer(Modifier.height(56.dp))
        Row(horizontalArrangement = Arrangement.spacedBy(16.dp), modifier = Modifier.fillMaxWidth()) {
            Button(
                onClick = { isRunning = !isRunning },
                modifier = Modifier.weight(1f).height(64.dp),
                shape = CircleShape
            ) {
                Text(if (isRunning) "Pause" else "Start", style = MaterialTheme.typography.titleLarge)
            }
            FilledTonalButton(
                onClick = { 
                    isRunning = false
                    timeLeft = totalTime
                },
                modifier = Modifier.weight(1f).height(64.dp),
                shape = CircleShape
            ) {
                Text("Reset", style = MaterialTheme.typography.titleLarge)
            }
        }
    }
}

@Composable
fun ColorPickerScreen(modifier: Modifier = Modifier) {
    val context = LocalContext.current
    var bitmap by remember { mutableStateOf<Bitmap?>(null) }
    var pickedColor by remember { mutableStateOf<Color?>(null) }
    var pickedHex by remember { mutableStateOf("") }
    var pickerOffset by remember { mutableStateOf<androidx.compose.ui.geometry.Offset?>(null) }
    
    val launcher = rememberLauncherForActivityResult(ActivityResultContracts.GetContent()) { uri: Uri? ->
        if (uri != null) {
            context.contentResolver.openInputStream(uri)?.use { stream ->
                bitmap = BitmapFactory.decodeStream(stream)
                pickedColor = null
                pickedHex = ""
                pickerOffset = null
            }
        }
    }

    val animatedColor by animateColorAsState(
        targetValue = pickedColor ?: MaterialTheme.colorScheme.surfaceVariant,
        label = "color"
    )

    Column(modifier.fillMaxSize().padding(20.dp), verticalArrangement = Arrangement.spacedBy(16.dp)) {
        Button(
            onClick = { launcher.launch("image/*") },
            modifier = Modifier.fillMaxWidth().height(56.dp),
            shape = RoundedCornerShape(28.dp)
        ) {
            Icon(Icons.Rounded.ImageSearch, contentDescription = null)
            Spacer(Modifier.width(12.dp))
            Text("Select Image", style = MaterialTheme.typography.titleMedium)
        }
        
        if (bitmap != null) {
            Surface(
                modifier = Modifier.fillMaxWidth().weight(1f).clip(RoundedCornerShape(24.dp)),
                color = MaterialTheme.colorScheme.surfaceVariant
            ) {
                Box(contentAlignment = Alignment.Center) {
                    Image(
                        bitmap = bitmap!!.asImageBitmap(),
                        contentDescription = "Selected Image",
                        modifier = Modifier
                            .fillMaxSize()
                            .pointerInput(Unit) {
                                detectTapGestures { offset ->
                                    val b = bitmap ?: return@detectTapGestures
                                    val imgRatio = b.width.toFloat() / b.height.toFloat()
                                    val viewRatio = size.width.toFloat() / size.height.toFloat()
                                    
                                    val x: Int
                                    val y: Int
                                    if (imgRatio > viewRatio) {
                                        val scaledHeight = size.width / imgRatio
                                        val yOffset = (size.height - scaledHeight) / 2
                                        if (offset.y < yOffset || offset.y > size.height - yOffset) return@detectTapGestures
                                        x = (offset.x * (b.width.toFloat() / size.width)).toInt()
                                        y = ((offset.y - yOffset) * (b.height.toFloat() / scaledHeight)).toInt()
                                    } else {
                                        val scaledWidth = size.height * imgRatio
                                        val xOffset = (size.width - scaledWidth) / 2
                                        if (offset.x < xOffset || offset.x > size.width - xOffset) return@detectTapGestures
                                        x = ((offset.x - xOffset) * (b.width.toFloat() / scaledWidth)).toInt()
                                        y = (offset.y * (b.height.toFloat() / size.height)).toInt()
                                    }
                                    
                                    if (x in 0 until b.width && y in 0 until b.height) {
                                        pickerOffset = offset
                                        val pixel = b.getPixel(x, y)
                                        pickedColor = Color(pixel)
                                        pickedHex = String.format("#%06X", (0xFFFFFF and pixel))
                                    }
                                }
                            }
                    )
                    pickerOffset?.let { offset ->
                        Box(
                            modifier = Modifier
                                .offset(x = with(androidx.compose.ui.platform.LocalDensity.current) { offset.x.toDp() - 16.dp }, y = with(androidx.compose.ui.platform.LocalDensity.current) { offset.y.toDp() - 16.dp })
                                .size(32.dp)
                                .border(2.dp, Color.White, CircleShape)
                                .border(4.dp, Color.Black.copy(alpha = 0.5f), CircleShape)
                        )
                    }
                }
            }
            
            AnimatedVisibility(visible = pickedColor != null) {
                Card(
                    modifier = Modifier.fillMaxWidth().height(100.dp),
                    shape = RoundedCornerShape(24.dp),
                    colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.surfaceContainer)
                ) {
                    Row(
                        modifier = Modifier.fillMaxSize().padding(16.dp),
                        horizontalArrangement = Arrangement.spacedBy(20.dp),
                        verticalAlignment = Alignment.CenterVertically
                    ) {
                        Box(
                            modifier = Modifier
                                .size(68.dp)
                                .clip(CircleShape)
                                .background(animatedColor)
                                .border(2.dp, MaterialTheme.colorScheme.outlineVariant, CircleShape)
                        )
                        Column(verticalArrangement = Arrangement.Center) {
                            Text(pickedHex, style = MaterialTheme.typography.headlineSmall, fontWeight = FontWeight.Bold)
                            if (pickedColor != null) {
                                Text(
                                    "RGB: ${(pickedColor!!.red*255).toInt()}, ${(pickedColor!!.green*255).toInt()}, ${(pickedColor!!.blue*255).toInt()}",
                                    style = MaterialTheme.typography.bodyMedium,
                                    color = MaterialTheme.colorScheme.onSurfaceVariant
                                )
                            }
                        }
                    }
                }
            }
        } else {
            Box(Modifier.fillMaxSize().weight(1f), contentAlignment = Alignment.Center) {
                Column(horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.spacedBy(16.dp)) {
                    Icon(Icons.Rounded.ImageSearch, contentDescription = null, modifier = Modifier.size(64.dp), tint = MaterialTheme.colorScheme.surfaceVariant)
                    Text("Pick an image to extract colors", style = MaterialTheme.typography.titleMedium, color = MaterialTheme.colorScheme.onSurfaceVariant)
                }
            }
        }
    }
}

@Composable
fun BaseTextScreen(modifier: Modifier = Modifier) {
    var inputMode by remember { mutableStateOf("Text") }
    val modes = listOf("Text", "Hex", "Decimal", "Octal", "Binary")
    var input by remember { mutableStateOf("") }
    
    data class OutputData(val label: String, val value: String)
    val outputs = remember(input, inputMode) {
        if (input.isBlank()) return@remember emptyList<OutputData>()
        try {
            val bytes = when (inputMode) {
                "Text" -> input.toByteArray()
                "Hex" -> input.split("\\s+".toRegex()).filter { it.isNotEmpty() }.map { it.toUByte(16).toByte() }.toByteArray()
                "Decimal" -> input.split("\\s+".toRegex()).filter { it.isNotEmpty() }.map { it.toUByte(10).toByte() }.toByteArray()
                "Octal" -> input.split("\\s+".toRegex()).filter { it.isNotEmpty() }.map { it.toUByte(8).toByte() }.toByteArray()
                "Binary" -> input.split("\\s+".toRegex()).filter { it.isNotEmpty() }.map { it.toUByte(2).toByte() }.toByteArray()
                else -> ByteArray(0)
            }
            
            listOf(
                OutputData("TEXT", String(bytes)),
                OutputData("HEX", bytes.joinToString(" ") { String.format("%02X", it) }),
                OutputData("DEC", bytes.joinToString(" ") { it.toUByte().toString() }),
                OutputData("OCT", bytes.joinToString(" ") { String.format("%03o", it) }),
                OutputData("BIN", bytes.joinToString(" ") { it.toUByte().toString(2).padStart(8, '0') })
            )
        } catch (e: Exception) {
            listOf(OutputData("ERROR", "Invalid input for selected mode"))
        }
    }

    Column(modifier.fillMaxSize().padding(16.dp), verticalArrangement = Arrangement.spacedBy(16.dp)) {
        LazyRow(
            modifier = Modifier.fillMaxWidth(),
            horizontalArrangement = Arrangement.spacedBy(8.dp),
            contentPadding = PaddingValues(horizontal = 8.dp)
        ) {
            items(modes) { m ->
                FilterChip(
                    selected = inputMode == m,
                    onClick = { inputMode = m },
                    label = { Text(m) },
                    shape = RoundedCornerShape(16.dp)
                )
            }
        }
        
        OutlinedTextField(
            value = input,
            onValueChange = { input = it },
            label = { Text("Enter $inputMode input") },
            modifier = Modifier.fillMaxWidth(),
            shape = RoundedCornerShape(24.dp),
            colors = OutlinedTextFieldDefaults.colors(
                focusedBorderColor = MaterialTheme.colorScheme.primary,
                unfocusedBorderColor = MaterialTheme.colorScheme.outline
            ),
            minLines = 2,
            maxLines = 4
        )
        
        val scrollState = rememberScrollState()
        Column(
            modifier = Modifier.fillMaxWidth().weight(1f).verticalScroll(scrollState),
            verticalArrangement = Arrangement.spacedBy(12.dp)
        ) {
            if (outputs.isEmpty()) {
                Box(Modifier.fillMaxSize().padding(32.dp), contentAlignment = Alignment.Center) {
                    Text("Output will appear here", style = MaterialTheme.typography.bodyLarge, color = MaterialTheme.colorScheme.onSurfaceVariant)
                }
            } else if (outputs[0].label == "ERROR") {
                Card(
                    modifier = Modifier.fillMaxWidth(),
                    colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.errorContainer)
                ) {
                    Text(outputs[0].value, color = MaterialTheme.colorScheme.onErrorContainer, modifier = Modifier.padding(16.dp))
                }
            } else {
                outputs.forEach { out ->
                    Card(
                        modifier = Modifier.fillMaxWidth(),
                        shape = RoundedCornerShape(16.dp),
                        colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.surfaceContainer)
                    ) {
                        Column(Modifier.padding(16.dp)) {
                            Text(out.label, style = MaterialTheme.typography.labelMedium, color = MaterialTheme.colorScheme.primary)
                            Spacer(Modifier.height(4.dp))
                            SelectionContainer {
                                Text(
                                    text = out.value,
                                    style = MaterialTheme.typography.bodyLarge.copy(fontFamily = FontFamily.Monospace),
                                    color = MaterialTheme.colorScheme.onSurface
                                )
                            }
                        }
                    }
                }
            }
        }
    }
}
