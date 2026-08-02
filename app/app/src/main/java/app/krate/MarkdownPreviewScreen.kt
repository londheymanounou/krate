package app.krate

import android.view.ViewGroup
import android.webkit.WebView
import android.webkit.WebViewClient
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.OutlinedTextFieldDefaults
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.unit.dp
import androidx.compose.ui.viewinterop.AndroidView

@Composable
fun MarkdownPreviewScreen(modifier: Modifier = Modifier, initialText: String = "") {
    var text by remember { mutableStateOf(initialText) }
    
    // We get the HTML by calling the core Tool if we want, or just basic markdown.
    // For simplicity, we can use the Rust core MarkdownToHtml tool:
    val htmlResult = remember(text) {
        if (text.isBlank()) "" 
        else {
            val res = Core.run("MarkdownToHtml", text)
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

    Column(modifier = modifier.fillMaxSize().padding(16.dp)) {
        OutlinedTextField(
            value = text,
            onValueChange = { text = it },
            modifier = Modifier.fillMaxWidth().weight(1f),
            textStyle = MaterialTheme.typography.bodyLarge,
            placeholder = { Text("Enter markdown here...") },
            colors = OutlinedTextFieldDefaults.colors(
                focusedContainerColor = MaterialTheme.colorScheme.surfaceVariant.copy(alpha = 0.3f),
                unfocusedContainerColor = MaterialTheme.colorScheme.surfaceVariant.copy(alpha = 0.3f)
            )
        )
        
        AndroidView(
            factory = { context ->
                WebView(context).apply {
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
            modifier = Modifier.fillMaxWidth().weight(1f).padding(top = 16.dp)
        )
    }
}
