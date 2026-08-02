@file:OptIn(ExperimentalMaterial3Api::class)
package app.krate

import android.content.ContentValues
import android.graphics.Bitmap
import android.graphics.BitmapFactory
import android.graphics.Canvas
import android.graphics.Color
import android.graphics.Paint
import android.graphics.Typeface
import android.net.Uri
import android.provider.MediaStore
import android.widget.Toast
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.Image
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.rounded.*
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.unit.dp
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext

@Composable
fun WatermarkScreen(modifier: Modifier = Modifier) {
    var imageUri by remember { mutableStateOf<Uri?>(null) }
    var watermarkText by remember { mutableStateOf("© KRATE") }
    var opacity by remember { mutableStateOf(50f) }
    var position by remember { mutableStateOf(0) }
    
    val context = LocalContext.current
    var previewBitmap by remember { mutableStateOf<Bitmap?>(null) }
    var originalBitmap by remember { mutableStateOf<Bitmap?>(null) }
    
    val launcher = rememberLauncherForActivityResult(ActivityResultContracts.GetContent()) { uri: Uri? ->
        imageUri = uri
        uri?.let {
            try {
                val inputStream = context.contentResolver.openInputStream(it)
                val bitmap = BitmapFactory.decodeStream(inputStream)
                inputStream?.close()
                originalBitmap = bitmap
            } catch (e: Exception) {
                e.printStackTrace()
            }
        }
    }
    
    LaunchedEffect(originalBitmap, watermarkText, opacity, position) {
        val og = originalBitmap ?: return@LaunchedEffect
        withContext(Dispatchers.Default) {
            val maxDim = 2048f
            val scale = (maxDim / maxOf(og.width, og.height)).coerceAtMost(1f)
            val w = (og.width * scale).toInt().coerceAtLeast(1)
            val h = (og.height * scale).toInt().coerceAtLeast(1)
            
            val scaled = Bitmap.createScaledBitmap(og, w, h, true)
            val result = scaled.copy(Bitmap.Config.ARGB_8888, true)
            val canvas = Canvas(result)
            val paint = Paint(Paint.ANTI_ALIAS_FLAG).apply {
                color = Color.WHITE
                alpha = ((opacity / 100f) * 255).toInt()
                textSize = (result.height * 0.05f).coerceAtLeast(14f)
                typeface = Typeface.DEFAULT_BOLD
            }
            
            val bounds = android.graphics.Rect()
            paint.getTextBounds(watermarkText, 0, watermarkText.length, bounds)
            
            val margin = result.height * 0.03f
            val x = when (position) {
                0 -> result.width - bounds.width() - margin // Bottom Right
                1 -> margin // Bottom Left
                2 -> result.width - bounds.width() - margin // Top Right
                3 -> margin // Top Left
                else -> (result.width - bounds.width()) / 2f // Center
            }
            val y = when (position) {
                0 -> result.height - margin // Bottom Right
                1 -> result.height - margin // Bottom Left
                2 -> margin + bounds.height() // Top Right
                3 -> margin + bounds.height() // Top Left
                else -> (result.height + bounds.height()) / 2f // Center
            }
            
            canvas.drawText(watermarkText, x, y, paint)
            previewBitmap = result
        }
    }
    
    Column(modifier = modifier.fillMaxSize().padding(16.dp), verticalArrangement = Arrangement.spacedBy(16.dp)) {
        if (previewBitmap == null) {
            Surface(
                onClick = { launcher.launch("image/*") },
                shape = RoundedCornerShape(32.dp),
                color = MaterialTheme.colorScheme.surfaceContainerHigh,
                modifier = Modifier.fillMaxWidth().weight(1f)
            ) {
                Column(
                    modifier = Modifier.fillMaxSize(),
                    verticalArrangement = Arrangement.Center,
                    horizontalAlignment = Alignment.CenterHorizontally
                ) {
                    Icon(Icons.Rounded.AddPhotoAlternate, null, modifier = Modifier.size(64.dp), tint = MaterialTheme.colorScheme.primary)
                    Spacer(Modifier.height(16.dp))
                    Text("Select Image", style = MaterialTheme.typography.titleLarge)
                }
            }
        } else {
            Surface(
                shape = RoundedCornerShape(32.dp),
                color = MaterialTheme.colorScheme.surfaceContainerLow,
                modifier = Modifier.fillMaxWidth().weight(1f)
            ) {
                Box(contentAlignment = Alignment.Center) {
                    Image(
                        bitmap = previewBitmap!!.asImageBitmap(),
                        contentDescription = "Preview",
                        modifier = Modifier.fillMaxSize().padding(16.dp)
                    )
                    IconButton(
                        onClick = { launcher.launch("image/*") },
                        modifier = Modifier.align(Alignment.TopEnd).padding(16.dp)
                    ) {
                        Icon(Icons.Rounded.Edit, "Change image")
                    }
                }
            }
            
            Surface(
                shape = RoundedCornerShape(32.dp),
                color = MaterialTheme.colorScheme.surfaceContainerHigh,
                modifier = Modifier.fillMaxWidth()
            ) {
                Column(modifier = Modifier.padding(24.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {
                    OutlinedTextField(
                        value = watermarkText,
                        onValueChange = { watermarkText = it },
                        label = { Text("Watermark Text") },
                        modifier = Modifier.fillMaxWidth(),
                        shape = RoundedCornerShape(16.dp),
                        singleLine = true
                    )
                    
                    var expanded by remember { mutableStateOf(false) }
                    val positions = listOf("Bottom Right", "Bottom Left", "Top Right", "Top Left", "Center")
                    
                    ExposedDropdownMenuBox(
                        expanded = expanded,
                        onExpandedChange = { expanded = it }
                    ) {
                        OutlinedTextField(
                            value = positions[position],
                            onValueChange = {},
                            readOnly = true,
                            label = { Text("Position") },
                            trailingIcon = { ExposedDropdownMenuDefaults.TrailingIcon(expanded) },
                            modifier = Modifier.menuAnchor().fillMaxWidth(),
                            shape = RoundedCornerShape(16.dp),
                            colors = ExposedDropdownMenuDefaults.outlinedTextFieldColors()
                        )
                        ExposedDropdownMenu(
                            expanded = expanded,
                            onDismissRequest = { expanded = false }
                        ) {
                            positions.forEachIndexed { index, pos ->
                                DropdownMenuItem(
                                    text = { Text(pos) },
                                    onClick = { position = index; expanded = false }
                                )
                            }
                        }
                    }
                    
                    Text("Opacity: ${opacity.toInt()}%", style = MaterialTheme.typography.labelLarge)
                    Slider(
                        value = opacity,
                        onValueChange = { opacity = it },
                        valueRange = 0f..100f
                    )
                    
                    Button(
                        onClick = {
                            val bmp = previewBitmap
                            if (bmp != null) {
                                val filename = "watermark_${System.currentTimeMillis()}.png"
                                val contentValues = ContentValues().apply {
                                    put(MediaStore.MediaColumns.DISPLAY_NAME, filename)
                                    put(MediaStore.MediaColumns.MIME_TYPE, "image/png")
                                    put(MediaStore.MediaColumns.RELATIVE_PATH, android.os.Environment.DIRECTORY_PICTURES + "/KRATE")
                                }
                                val uri = context.contentResolver.insert(MediaStore.Images.Media.EXTERNAL_CONTENT_URI, contentValues)
                                if (uri != null) {
                                    context.contentResolver.openOutputStream(uri)?.use { out ->
                                        bmp.compress(Bitmap.CompressFormat.PNG, 100, out)
                                    }
                                    Toast.makeText(context, "Saved to Pictures/KRATE", Toast.LENGTH_SHORT).show()
                                } else {
                                    Toast.makeText(context, "Failed to save", Toast.LENGTH_SHORT).show()
                                }
                            }
                        },
                        modifier = Modifier.fillMaxWidth().height(56.dp),
                        shape = RoundedCornerShape(16.dp)
                    ) {
                        Icon(Icons.Rounded.Save, null)
                        Spacer(Modifier.width(8.dp))
                        Text("Save to Gallery")
                    }
                }
            }
        }
    }
}
