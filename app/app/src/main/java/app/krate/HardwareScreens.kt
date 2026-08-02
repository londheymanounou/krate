package app.krate

import android.Manifest
import android.content.Context
import android.content.pm.PackageManager
import android.location.Location
import android.location.LocationListener
import android.location.LocationManager
import android.media.MediaRecorder
import android.os.Bundle
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.animation.core.animateFloatAsState
import androidx.compose.animation.core.spring
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.drawscope.rotate
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.core.content.ContextCompat
import kotlinx.coroutines.delay
import android.hardware.Sensor
import android.hardware.SensorManager

@Composable
fun CompassAndGpsScreen(modifier: Modifier = Modifier) {
    // 1. Compass Logic (Copied and adapted from CompassScreen)
    val v = rememberSensor(Sensor.TYPE_ROTATION_VECTOR) ?: return MissingSensor("compass", modifier)
    val heading = remember(v) {
        val matrix = FloatArray(9)
        SensorManager.getRotationMatrixFromVector(matrix, v)
        val orientation = FloatArray(3)
        SensorManager.getOrientation(matrix, orientation)
        ((Math.toDegrees(orientation[0].toDouble()) + 360.0) % 360.0).toFloat()
    }
    val angle by animateFloatAsState(targetValue = -heading, animationSpec = spring(dampingRatio = 0.7f, stiffness = 200f), label = "")

    // 2. GPS Logic
    val context = LocalContext.current
    var location by remember { mutableStateOf<Location?>(null) }
    var hasPermission by remember { mutableStateOf(ContextCompat.checkSelfPermission(context, Manifest.permission.ACCESS_FINE_LOCATION) == PackageManager.PERMISSION_GRANTED) }
    
    val launcher = rememberLauncherForActivityResult(ActivityResultContracts.RequestPermission()) { granted ->
        hasPermission = granted
    }

    DisposableEffect(hasPermission) {
        val locationManager = context.getSystemService(Context.LOCATION_SERVICE) as? LocationManager
        val listener = object : LocationListener {
            override fun onLocationChanged(loc: Location) { location = loc }
            override fun onStatusChanged(provider: String?, status: Int, extras: Bundle?) {}
        }
        if (hasPermission && locationManager != null) {
            try {
                locationManager.requestLocationUpdates(LocationManager.GPS_PROVIDER, 1000L, 1f, listener)
                locationManager.requestLocationUpdates(LocationManager.NETWORK_PROVIDER, 1000L, 1f, listener)
            } catch (e: SecurityException) { }
        } else if (!hasPermission) {
            launcher.launch(Manifest.permission.ACCESS_FINE_LOCATION)
        }
        onDispose {
            locationManager?.removeUpdates(listener)
        }
    }

    Column(modifier.fillMaxSize().padding(24.dp), verticalArrangement = Arrangement.Center, horizontalAlignment = Alignment.CenterHorizontally) {
        // Compass UI
        Box(Modifier.fillMaxWidth().aspectRatio(1f), contentAlignment = Alignment.Center) {
            Canvas(Modifier.fillMaxSize()) {
                val r = size.minDimension / 2f
                val c = Offset(size.width / 2f, size.height / 2f)
                drawCircle(Color.Gray, r, c, alpha = 0.2f)
                rotate(angle, c) {
                    drawRoundRect(color = Color.Red, topLeft = Offset(c.x - r * 0.05f, c.y - r * 0.78f), size = androidx.compose.ui.geometry.Size(r * 0.1f, r * 0.78f))
                    drawRoundRect(color = Color.DarkGray, topLeft = Offset(c.x - r * 0.05f, c.y), size = androidx.compose.ui.geometry.Size(r * 0.1f, r * 0.78f))
                }
                drawCircle(Color.Black, r * 0.06f, c)
            }
        }
        Spacer(Modifier.height(32.dp))
        
        // GPS UI
        Surface(shape = RoundedCornerShape(24.dp), color = MaterialTheme.colorScheme.surfaceVariant, modifier = Modifier.fillMaxWidth()) {
            Column(Modifier.padding(24.dp), horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.spacedBy(8.dp)) {
                if (hasPermission) {
                    if (location != null) {
                        Text("Lat: ${String.format("%.5f", location!!.latitude)}", style = MaterialTheme.typography.titleLarge)
                        Text("Lon: ${String.format("%.5f", location!!.longitude)}", style = MaterialTheme.typography.titleLarge)
                        Text("Altitude: ${String.format("%.1f", location!!.altitude)}m", style = MaterialTheme.typography.bodyLarge)
                        Text("Accuracy: ${String.format("%.1f", location!!.accuracy)}m", style = MaterialTheme.typography.bodyMedium, color = MaterialTheme.colorScheme.onSurfaceVariant)
                    } else {
                        Text("Acquiring GPS signal...", style = MaterialTheme.typography.bodyLarge)
                    }
                } else {
                    Text("GPS permission required", style = MaterialTheme.typography.bodyLarge)
                    Button(onClick = { launcher.launch(Manifest.permission.ACCESS_FINE_LOCATION) }) { Text("Grant Permission") }
                }
            }
        }
    }
}

@Composable
fun SoundMeterScreen(modifier: Modifier = Modifier) {
    val context = LocalContext.current
    var hasPermission by remember { mutableStateOf(ContextCompat.checkSelfPermission(context, Manifest.permission.RECORD_AUDIO) == PackageManager.PERMISSION_GRANTED) }
    var decibels by remember { mutableIntStateOf(0) }
    
    val launcher = rememberLauncherForActivityResult(ActivityResultContracts.RequestPermission()) { granted ->
        hasPermission = granted
    }

    LaunchedEffect(hasPermission) {
        if (!hasPermission) {
            launcher.launch(Manifest.permission.RECORD_AUDIO)
            return@LaunchedEffect
        }
        
        var recorder: MediaRecorder? = null
        try {
            @Suppress("DEPRECATION")
            recorder = MediaRecorder().apply {
                setAudioSource(MediaRecorder.AudioSource.MIC)
                setOutputFormat(MediaRecorder.OutputFormat.THREE_GPP)
                setAudioEncoder(MediaRecorder.AudioEncoder.AMR_NB)
                setOutputFile(context.cacheDir.absolutePath + "/sound_meter_tmp.3gp")
                prepare()
                start()
            }
            
            while(true) {
                delay(100)
                val amp = recorder.maxAmplitude
                if (amp > 0) {
                    val db = 20 * Math.log10(amp.toDouble())
                    decibels = db.toInt()
                }
            }
        } catch (e: Exception) {
            e.printStackTrace()
        } finally {
            try {
                recorder?.stop()
                recorder?.release()
            } catch (e: Exception) {}
        }
    }

    Column(modifier.fillMaxSize().padding(24.dp), verticalArrangement = Arrangement.Center, horizontalAlignment = Alignment.CenterHorizontally) {
        if (!hasPermission) {
            Text("Microphone permission required")
            Button(onClick = { launcher.launch(Manifest.permission.RECORD_AUDIO) }) { Text("Grant Permission") }
        } else {
            Box(Modifier.size(250.dp), contentAlignment = Alignment.Center) {
                CircularProgressIndicator(
                    progress = { (decibels / 120f).coerceIn(0f, 1f) },
                    modifier = Modifier.fillMaxSize(),
                    color = if (decibels > 85) Color.Red else if (decibels > 60) Color.Yellow else Color.Green,
                    strokeWidth = 24.dp,
                    trackColor = MaterialTheme.colorScheme.surfaceVariant
                )
                Column(horizontalAlignment = Alignment.CenterHorizontally) {
                    Text("$decibels", style = MaterialTheme.typography.displayLarge)
                    Text("dB", style = MaterialTheme.typography.titleLarge, color = MaterialTheme.colorScheme.onSurfaceVariant)
                }
            }
        }
    }
}

@Composable
fun QrScannerScreen(modifier: Modifier = Modifier) {
    val context = LocalContext.current
    var hasPermission by remember { mutableStateOf(ContextCompat.checkSelfPermission(context, Manifest.permission.CAMERA) == PackageManager.PERMISSION_GRANTED) }
    var scannedText by remember { mutableStateOf("") }
    
    val launcher = rememberLauncherForActivityResult(ActivityResultContracts.RequestPermission()) { granted ->
        hasPermission = granted
    }

    LaunchedEffect(hasPermission) {
        if (!hasPermission) {
            launcher.launch(Manifest.permission.CAMERA)
        }
    }

    Column(modifier.fillMaxSize().padding(24.dp), verticalArrangement = Arrangement.spacedBy(16.dp), horizontalAlignment = Alignment.CenterHorizontally) {
        if (!hasPermission) {
            Box(Modifier.weight(1f), contentAlignment = Alignment.Center) {
                Column(horizontalAlignment = Alignment.CenterHorizontally) {
                    Text("Camera permission required", style = MaterialTheme.typography.bodyLarge)
                    Spacer(Modifier.height(16.dp))
                    Button(onClick = { launcher.launch(Manifest.permission.CAMERA) }) { Text("Grant Permission") }
                }
            }
        } else {
            Surface(
                modifier = Modifier.fillMaxWidth().weight(1f),
                shape = RoundedCornerShape(24.dp),
                color = Color.Black
            ) {
                androidx.compose.ui.viewinterop.AndroidView(
                    factory = { ctx ->
                        com.journeyapps.barcodescanner.CompoundBarcodeView(ctx).apply {
                            decodeContinuous { result ->
                                scannedText = result.text
                            }
                            resume()
                        }
                    },
                    modifier = Modifier.fillMaxSize(),
                    onRelease = { view ->
                        view.pause()
                    }
                )
            }
        }
        
        Surface(
            shape = RoundedCornerShape(24.dp),
            color = MaterialTheme.colorScheme.surfaceVariant,
            modifier = Modifier.fillMaxWidth().heightIn(min = 100.dp)
        ) {
            Box(Modifier.padding(24.dp), contentAlignment = Alignment.Center) {
                Text(
                    scannedText.ifEmpty { "Point camera at a QR code or barcode" },
                    style = MaterialTheme.typography.bodyLarge,
                    color = if (scannedText.isEmpty()) MaterialTheme.colorScheme.onSurfaceVariant else MaterialTheme.colorScheme.onSurface,
                    textAlign = TextAlign.Center
                )
            }
        }
    }
}
