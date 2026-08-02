package app.krate

import androidx.compose.animation.*
import androidx.compose.animation.core.*
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.itemsIndexed
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.rounded.*
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import kotlinx.coroutines.delay
import java.text.SimpleDateFormat
import java.util.*
import kotlin.math.PI
import kotlin.math.cos
import kotlin.math.sin
import androidx.compose.foundation.Canvas
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.StrokeCap
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.unit.sp
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.ui.text.input.KeyboardType

@Composable
fun ClockScreen(modifier: Modifier = Modifier) {
    var currentTime by remember { mutableStateOf(System.currentTimeMillis()) }
    
    LaunchedEffect(Unit) {
        while (true) {
            currentTime = System.currentTimeMillis()
            delay(16L)
        }
    }
    
    val timeFormatter = remember { SimpleDateFormat("HH:mm", Locale.getDefault()) }
    val secondsFormatter = remember { SimpleDateFormat("ss", Locale.getDefault()) }
    val dateFormatter = remember { SimpleDateFormat("EEEE, MMMM d", Locale.getDefault()) }
    
    val calendar = Calendar.getInstance()
    calendar.timeInMillis = currentTime
    val millis = calendar.get(Calendar.MILLISECOND)
    val seconds = calendar.get(Calendar.SECOND)
    val minutes = calendar.get(Calendar.MINUTE)
    val hours = calendar.get(Calendar.HOUR_OF_DAY)
    
    val secondsProgress = (seconds * 1000f + millis) / 60000f

    val color = MaterialTheme.colorScheme.primary
    val trackColor = MaterialTheme.colorScheme.primaryContainer

    Column(
        modifier = modifier.fillMaxSize().verticalScroll(rememberScrollState()).padding(24.dp),
        horizontalAlignment = Alignment.CenterHorizontally,
        verticalArrangement = Arrangement.Center
    ) {
        Box(
            contentAlignment = Alignment.Center,
            modifier = Modifier.size(320.dp).padding(16.dp)
        ) {
            Canvas(modifier = Modifier.fillMaxSize()) {
                val radius = size.minDimension / 2
                val center = Offset(size.width / 2, size.height / 2)
                
                for (i in 0 until 60) {
                    val angle = i * 6f * (PI / 180f).toFloat()
                    val isHour = i % 5 == 0
                    val lineLength = if (isHour) 16.dp.toPx() else 8.dp.toPx()
                    val strokeWidth = if (isHour) 4.dp.toPx() else 2.dp.toPx()
                    val start = Offset(
                        x = center.x + (radius - lineLength) * sin(angle),
                        y = center.y - (radius - lineLength) * cos(angle)
                    )
                    val end = Offset(
                        x = center.x + radius * sin(angle),
                        y = center.y - radius * cos(angle)
                    )
                    drawLine(
                        color = if (isHour) color else trackColor,
                        start = start,
                        end = end,
                        strokeWidth = strokeWidth,
                        cap = StrokeCap.Round
                    )
                }
                
                val secAngle = secondsProgress * 360f * (PI / 180f).toFloat()
                val minProgress = (minutes * 60f + seconds) / 3600f
                val minAngle = minProgress * 360f * (PI / 180f).toFloat()
                val hourProgress = ((hours % 12) * 3600f + minutes * 60f + seconds) / (12 * 3600f)
                val hourAngle = hourProgress * 360f * (PI / 180f).toFloat()
                
                drawLine(
                    color = color,
                    start = center,
                    end = Offset(
                        x = center.x + (radius * 0.5f) * sin(hourAngle),
                        y = center.y - (radius * 0.5f) * cos(hourAngle)
                    ),
                    strokeWidth = 8.dp.toPx(),
                    cap = StrokeCap.Round
                )
                drawLine(
                    color = color,
                    start = center,
                    end = Offset(
                        x = center.x + (radius * 0.75f) * sin(minAngle),
                        y = center.y - (radius * 0.75f) * cos(minAngle)
                    ),
                    strokeWidth = 6.dp.toPx(),
                    cap = StrokeCap.Round
                )
                val errorColor = androidx.compose.ui.graphics.Color(0xFFE53935)
                drawLine(
                    color = errorColor,
                    start = Offset(
                        x = center.x - (radius * 0.15f) * sin(secAngle),
                        y = center.y + (radius * 0.15f) * cos(secAngle)
                    ),
                    end = Offset(
                        x = center.x + (radius * 0.8f) * sin(secAngle),
                        y = center.y - (radius * 0.8f) * cos(secAngle)
                    ),
                    strokeWidth = 3.dp.toPx(),
                    cap = StrokeCap.Round
                )
                drawCircle(
                    color = errorColor,
                    radius = 6.dp.toPx(),
                    center = center
                )
            }
        }
        
        Spacer(Modifier.height(48.dp))
        
        Column(horizontalAlignment = Alignment.CenterHorizontally) {
            Row(verticalAlignment = Alignment.Bottom) {
                Text(
                    text = timeFormatter.format(Date(currentTime)),
                    style = MaterialTheme.typography.displayLarge.copy(fontSize = 72.sp),
                    fontWeight = FontWeight.Bold,
                    color = MaterialTheme.colorScheme.onSurface
                )
                Spacer(Modifier.width(8.dp))
                Text(
                    text = secondsFormatter.format(Date(currentTime)),
                    style = MaterialTheme.typography.headlineLarge,
                    color = MaterialTheme.colorScheme.primary,
                    modifier = Modifier.padding(bottom = 12.dp)
                )
            }
            Spacer(Modifier.height(8.dp))
            Text(
                text = dateFormatter.format(Date(currentTime)),
                style = MaterialTheme.typography.titleLarge,
                color = MaterialTheme.colorScheme.onSurfaceVariant
            )
        }
    }
}

@Composable
fun TimerStopwatchScreen(modifier: Modifier = Modifier) {
    var selectedTab by remember { mutableIntStateOf(0) }
    
    Column(modifier = modifier.fillMaxSize()) {
        TabRow(selectedTabIndex = selectedTab) {
            Tab(
                selected = selectedTab == 0,
                onClick = { selectedTab = 0 },
                text = { Text("Timer", style = MaterialTheme.typography.titleMedium) }
            )
            Tab(
                selected = selectedTab == 1,
                onClick = { selectedTab = 1 },
                text = { Text("Stopwatch", style = MaterialTheme.typography.titleMedium) }
            )
        }
        
        Box(modifier = Modifier.weight(1f)) {
            if (selectedTab == 0) {
                TimerSubScreen()
            } else {
                StopwatchSubScreen()
            }
        }
    }
}

@Composable
private fun TimerSubScreen() {
    var timeLeft by remember { mutableStateOf(60000L) }
    var isRunning by remember { mutableStateOf(false) }
    var totalTime by remember { mutableStateOf(60000L) }
    
    var showCustomDialog by remember { mutableStateOf(false) }
    var customMinInput by remember { mutableStateOf("") }
    var customSecInput by remember { mutableStateOf("") }
    
    LaunchedEffect(isRunning) {
        if (isRunning) {
            var lastTime = System.currentTimeMillis()
            while (timeLeft > 0) {
                delay(16L)
                val now = System.currentTimeMillis()
                timeLeft = (timeLeft - (now - lastTime)).coerceAtLeast(0L)
                lastTime = now
            }
            isRunning = false
        }
    }
    
    if (showCustomDialog) {
        AlertDialog(
            onDismissRequest = { showCustomDialog = false },
            title = { Text("Set Custom Time") },
            text = {
                Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    OutlinedTextField(
                        value = customMinInput,
                        onValueChange = { customMinInput = it },
                        label = { Text("Min") },
                        keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
                        modifier = Modifier.weight(1f)
                    )
                    OutlinedTextField(
                        value = customSecInput,
                        onValueChange = { customSecInput = it },
                        label = { Text("Sec") },
                        keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
                        modifier = Modifier.weight(1f)
                    )
                }
            },
            confirmButton = {
                TextButton(onClick = {
                    val m = customMinInput.toLongOrNull() ?: 0L
                    val s = customSecInput.toLongOrNull() ?: 0L
                    val newTotal = (m * 60 + s) * 1000L
                    if (newTotal > 0) {
                        totalTime = newTotal
                        timeLeft = newTotal
                        isRunning = false
                    }
                    showCustomDialog = false
                }) {
                    Text("Set")
                }
            },
            dismissButton = {
                TextButton(onClick = { showCustomDialog = false }) {
                    Text("Cancel")
                }
            }
        )
    }
    
    val progress = if (totalTime > 0) timeLeft.toFloat() / totalTime else 0f
    
    val minutes = (timeLeft / 60000).toInt()
    val seconds = ((timeLeft % 60000) / 1000).toInt()
    val millis = ((timeLeft % 1000) / 10).toInt()
    
    Column(
        modifier = Modifier.fillMaxSize().verticalScroll(rememberScrollState()).padding(24.dp),
        horizontalAlignment = Alignment.CenterHorizontally,
        verticalArrangement = Arrangement.Center
    ) {
        Box(
            contentAlignment = Alignment.Center,
            modifier = Modifier.size(320.dp)
        ) {
            CircularProgressIndicator(
                progress = { progress },
                modifier = Modifier.fillMaxSize(),
                color = if (timeLeft == 0L) MaterialTheme.colorScheme.error else MaterialTheme.colorScheme.primary,
                trackColor = MaterialTheme.colorScheme.primaryContainer,
                strokeWidth = 16.dp,
                strokeCap = StrokeCap.Round
            )
            Column(horizontalAlignment = Alignment.CenterHorizontally) {
                Text(
                    text = String.format("%02d:%02d", minutes, seconds),
                    style = MaterialTheme.typography.displayLarge.copy(fontSize = 72.sp),
                    fontWeight = FontWeight.Bold,
                    color = MaterialTheme.colorScheme.onSurface
                )
                Text(
                    text = String.format(".%02d", millis),
                    style = MaterialTheme.typography.headlineLarge,
                    color = MaterialTheme.colorScheme.onSurfaceVariant
                )
            }
        }
        
        Spacer(Modifier.height(48.dp))
        
        Row(
            modifier = Modifier.fillMaxWidth(),
            horizontalArrangement = Arrangement.SpaceEvenly,
            verticalAlignment = Alignment.CenterVertically
        ) {
            FilledTonalIconButton(
                onClick = { 
                    isRunning = false
                    timeLeft = totalTime 
                },
                modifier = Modifier.size(64.dp)
            ) {
                Icon(Icons.Rounded.Refresh, null, modifier = Modifier.size(28.dp))
            }
            
            FloatingActionButton(
                onClick = { 
                    if (timeLeft == 0L) {
                        timeLeft = totalTime
                    }
                    isRunning = !isRunning 
                },
                modifier = Modifier.size(88.dp),
                containerColor = if (isRunning) MaterialTheme.colorScheme.errorContainer else MaterialTheme.colorScheme.primaryContainer
            ) {
                Icon(
                    if (isRunning) Icons.Rounded.Pause else Icons.Rounded.PlayArrow, 
                    null, 
                    modifier = Modifier.size(40.dp)
                )
            }
            
            FilledTonalIconButton(
                onClick = { 
                    customMinInput = (totalTime / 60000).toString()
                    customSecInput = ((totalTime % 60000) / 1000).toString()
                    showCustomDialog = true
                },
                modifier = Modifier.size(64.dp)
            ) {
                Icon(Icons.Rounded.Edit, null, modifier = Modifier.size(28.dp))
            }
        }
    }
}

@Composable
private fun StopwatchSubScreen() {
    var isRunning by remember { mutableStateOf(false) }
    var elapsedTime by remember { mutableStateOf(0L) }
    var laps by remember { mutableStateOf(listOf<Long>()) }
    
    LaunchedEffect(isRunning) {
        if (isRunning) {
            var lastTime = System.currentTimeMillis()
            while (isRunning) {
                delay(16L)
                val now = System.currentTimeMillis()
                elapsedTime += (now - lastTime)
                lastTime = now
            }
        }
    }
    
    val formatTime = { time: Long ->
        val m = (time / 60000).toInt()
        val s = ((time % 60000) / 1000).toInt()
        val ms = ((time % 1000) / 10).toInt()
        String.format("%02d:%02d.%02d", m, s, ms)
    }
    
    val minutes = (elapsedTime / 60000).toInt()
    val seconds = ((elapsedTime % 60000) / 1000).toInt()
    val millis = ((elapsedTime % 1000) / 10).toInt()
    
    Column(
        modifier = Modifier.fillMaxSize().padding(horizontal = 24.dp),
        horizontalAlignment = Alignment.CenterHorizontally
    ) {
        Spacer(Modifier.height(32.dp))
        
        Text(
            text = String.format("%02d:%02d", minutes, seconds),
            style = MaterialTheme.typography.displayLarge.copy(fontSize = 88.sp),
            fontWeight = FontWeight.Bold,
            color = MaterialTheme.colorScheme.onSurface
        )
        Text(
            text = String.format(".%02d", millis),
            style = MaterialTheme.typography.displaySmall,
            color = MaterialTheme.colorScheme.primary
        )
        
        Spacer(Modifier.height(32.dp))
        
        Row(
            modifier = Modifier.fillMaxWidth().padding(bottom = 32.dp),
            horizontalArrangement = Arrangement.SpaceEvenly,
            verticalAlignment = Alignment.CenterVertically
        ) {
            FilledTonalIconButton(
                onClick = { 
                    if (isRunning) {
                        laps = laps + elapsedTime
                    } else {
                        elapsedTime = 0L
                        laps = emptyList()
                    }
                },
                modifier = Modifier.size(64.dp)
            ) {
                Icon(
                    if (isRunning) Icons.Rounded.Flag else Icons.Rounded.Refresh, 
                    null, 
                    modifier = Modifier.size(28.dp)
                )
            }
            
            FloatingActionButton(
                onClick = { isRunning = !isRunning },
                modifier = Modifier.size(88.dp),
                containerColor = if (isRunning) MaterialTheme.colorScheme.errorContainer else MaterialTheme.colorScheme.primaryContainer
            ) {
                Icon(
                    if (isRunning) Icons.Rounded.Pause else Icons.Rounded.PlayArrow, 
                    null, 
                    modifier = Modifier.size(40.dp)
                )
            }
            
            Spacer(modifier = Modifier.size(64.dp)) // layout balance
        }
        
        Divider(color = MaterialTheme.colorScheme.surfaceVariant)
        
        LazyColumn(
            modifier = Modifier.fillMaxWidth().weight(1f),
            contentPadding = PaddingValues(vertical = 16.dp)
        ) {
            itemsIndexed(laps.reversed()) { index, lapTime ->
                val actualIndex = laps.size - index
                val prevLapTime = if (actualIndex > 1) laps[actualIndex - 2] else 0L
                val diff = lapTime - prevLapTime
                
                Row(
                    modifier = Modifier.fillMaxWidth().padding(vertical = 12.dp, horizontal = 8.dp),
                    horizontalArrangement = Arrangement.SpaceBetween,
                    verticalAlignment = Alignment.CenterVertically
                ) {
                    Text(
                        text = "Lap $actualIndex",
                        style = MaterialTheme.typography.titleMedium,
                        color = MaterialTheme.colorScheme.onSurfaceVariant
                    )
                    Row(horizontalArrangement = Arrangement.spacedBy(16.dp)) {
                        Text(
                            text = "+${formatTime(diff)}",
                            style = MaterialTheme.typography.bodyLarge,
                            color = MaterialTheme.colorScheme.onSurfaceVariant
                        )
                        Text(
                            text = formatTime(lapTime),
                            style = MaterialTheme.typography.titleMedium,
                            color = MaterialTheme.colorScheme.onSurface
                        )
                    }
                }
            }
        }
    }
}
