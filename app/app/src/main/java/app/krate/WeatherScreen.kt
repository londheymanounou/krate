package app.krate

import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyRow
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.rounded.Search
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import org.json.JSONObject
import java.net.URL
import java.net.URLEncoder

/**
 * Weather, from Open-Meteo.
 *
 * **This is the only tool in KRATE that touches the network.** Everything else runs offline in the
 * Rust core; this one sends a place name to a geocoding endpoint and coordinates to a forecast one.
 * That is stated on screen rather than buried, because the About dialog promises the rest of the
 * app sends nothing anywhere and a silent exception to that would be a lie.
 *
 * Open-Meteo needs no API key and no account, which is why it is used here — a tool that demands
 * the user register somewhere is not a tool most people will ever run.
 */
private data class Forecast(
    val place: String,
    val temperature: Double,
    val apparent: Double,
    val code: Int,
    val wind: Double,
    val humidity: Int,
    val days: List<Triple<String, Double, Double>>,
)

/** WMO weather codes, grouped to the distinctions a person actually cares about. */
private fun describe(code: Int): Pair<String, String> = when (code) {
    0 -> "Clear" to "☀️"
    1, 2 -> "Mostly clear" to "🌤️"
    3 -> "Overcast" to "☁️"
    45, 48 -> "Fog" to "🌫️"
    51, 53, 55, 56, 57 -> "Drizzle" to "🌦️"
    61, 63, 65, 66, 67 -> "Rain" to "🌧️"
    71, 73, 75, 77 -> "Snow" to "🌨️"
    80, 81, 82 -> "Showers" to "🌦️"
    85, 86 -> "Snow showers" to "🌨️"
    95, 96, 99 -> "Thunderstorm" to "⛈️"
    else -> "Unknown" to "🌡️"
}

private fun fetch(place: String): Forecast {
    val query = URLEncoder.encode(place, "UTF-8")
    val geo = JSONObject(
        URL("https://geocoding-api.open-meteo.com/v1/search?name=$query&count=1").readText()
    )
    val results = geo.optJSONArray("results")
        ?: throw IllegalArgumentException("No place called \"$place\"")
    if (results.length() == 0) throw IllegalArgumentException("No place called \"$place\"")
    val first = results.getJSONObject(0)
    val lat = first.getDouble("latitude")
    val lon = first.getDouble("longitude")
    val name = buildString {
        append(first.getString("name"))
        first.optString("country").takeIf { it.isNotEmpty() }?.let { append(", ").append(it) }
    }

    val body = JSONObject(
        URL(
            "https://api.open-meteo.com/v1/forecast?latitude=$lat&longitude=$lon" +
                "&current=temperature_2m,apparent_temperature,relative_humidity_2m,weather_code,wind_speed_10m" +
                "&daily=weather_code,temperature_2m_max,temperature_2m_min&forecast_days=5&timezone=auto"
        ).readText()
    )
    val current = body.getJSONObject("current")
    val daily = body.getJSONObject("daily")
    val dates = daily.getJSONArray("time")
    val highs = daily.getJSONArray("temperature_2m_max")
    val lows = daily.getJSONArray("temperature_2m_min")

    return Forecast(
        place = name,
        temperature = current.getDouble("temperature_2m"),
        apparent = current.getDouble("apparent_temperature"),
        code = current.getInt("weather_code"),
        wind = current.getDouble("wind_speed_10m"),
        humidity = current.getInt("relative_humidity_2m"),
        days = (0 until dates.length()).map {
            Triple(dates.getString(it).takeLast(5), highs.getDouble(it), lows.getDouble(it))
        },
    )
}

@Composable
fun WeatherScreen(modifier: Modifier = Modifier) {
    var place by remember { mutableStateOf("") }
    var forecast by remember { mutableStateOf<Forecast?>(null) }
    var error by remember { mutableStateOf("") }
    var loading by remember { mutableStateOf(false) }
    val scope = rememberCoroutineScope()

    fun load() {
        if (place.isBlank()) return
        scope.launch {
            loading = true
            error = ""
            // Network on IO, never the main thread — a slow lookup would otherwise freeze the UI.
            val result = withContext(Dispatchers.IO) { runCatching { fetch(place.trim()) } }
            result.onSuccess { forecast = it }
                .onFailure { error = it.message ?: "Could not reach the weather service" }
            loading = false
        }
    }

    Column(
        modifier.fillMaxSize().padding(20.dp),
        verticalArrangement = Arrangement.spacedBy(16.dp),
    ) {
        OutlinedTextField(
            value = place,
            onValueChange = { place = it },
            label = { Text("Town or city") },
            placeholder = { Text("Paris") },
            singleLine = true,
            shape = RoundedCornerShape(28.dp),
            modifier = Modifier.fillMaxWidth(),
            keyboardOptions = KeyboardOptions(imeAction = ImeAction.Search),
            keyboardActions = androidx.compose.foundation.text.KeyboardActions(onSearch = { load() }),
            trailingIcon = {
                FilledTonalIconButton(onClick = { load() }) { Icon(Icons.Rounded.Search, "Search") }
            },
        )

        when {
            loading -> Box(Modifier.fillMaxWidth().weight(1f), contentAlignment = Alignment.Center) {
                CircularProgressIndicator()
            }

            error.isNotEmpty() -> Surface(
                shape = RoundedCornerShape(28.dp),
                color = MaterialTheme.colorScheme.errorContainer,
                modifier = Modifier.fillMaxWidth().weight(1f),
            ) {
                Box(Modifier.fillMaxSize().padding(24.dp), contentAlignment = Alignment.Center) {
                    Text(
                        error,
                        color = MaterialTheme.colorScheme.onErrorContainer,
                        textAlign = TextAlign.Center,
                        style = MaterialTheme.typography.bodyLarge,
                    )
                }
            }

            forecast != null -> {
                val f = forecast!!
                val (label, glyph) = describe(f.code)
                Column(
                    Modifier.fillMaxWidth().weight(1f),
                    verticalArrangement = Arrangement.spacedBy(12.dp),
                ) {
                    Surface(
                        shape = RoundedCornerShape(32.dp),
                        color = MaterialTheme.colorScheme.primaryContainer,
                        modifier = Modifier.fillMaxWidth(),
                    ) {
                        Column(
                            Modifier.padding(28.dp),
                            horizontalAlignment = Alignment.CenterHorizontally,
                        ) {
                            Text(
                                f.place,
                                style = MaterialTheme.typography.titleMedium,
                                color = MaterialTheme.colorScheme.onPrimaryContainer,
                            )
                            Text(glyph, fontSize = 56.sp)
                            Text(
                                "${f.temperature.toInt()}°",
                                fontSize = 72.sp,
                                fontWeight = FontWeight.Light,
                                color = MaterialTheme.colorScheme.onPrimaryContainer,
                            )
                            Text(
                                label,
                                style = MaterialTheme.typography.titleMedium,
                                color = MaterialTheme.colorScheme.onPrimaryContainer,
                            )
                            Text(
                                "Feels like ${f.apparent.toInt()}°",
                                style = MaterialTheme.typography.bodyMedium,
                                color = MaterialTheme.colorScheme.onPrimaryContainer.copy(alpha = 0.7f),
                            )
                        }
                    }

                    Row(horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                        Stat("Wind", "${f.wind.toInt()} km/h", Modifier.weight(1f))
                        Stat("Humidity", "${f.humidity}%", Modifier.weight(1f))
                    }

                    Text("Next days", style = MaterialTheme.typography.titleSmall)
                    LazyRow(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                        items(f.days) { (date, high, low) ->
                            Surface(
                                shape = RoundedCornerShape(22.dp),
                                color = MaterialTheme.colorScheme.surfaceContainerHigh,
                            ) {
                                Column(
                                    Modifier.padding(horizontal = 18.dp, vertical = 14.dp),
                                    horizontalAlignment = Alignment.CenterHorizontally,
                                ) {
                                    Text(date, style = MaterialTheme.typography.labelMedium)
                                    Spacer(Modifier.height(6.dp))
                                    Text(
                                        "${high.toInt()}°",
                                        style = MaterialTheme.typography.titleMedium,
                                        fontWeight = FontWeight.Bold,
                                    )
                                    Text(
                                        "${low.toInt()}°",
                                        style = MaterialTheme.typography.bodyMedium,
                                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                                    )
                                }
                            }
                        }
                    }
                }
            }

            else -> Box(Modifier.fillMaxWidth().weight(1f), contentAlignment = Alignment.Center) {
                Text(
                    "Search for a place.\nThis is the only KRATE tool that uses the internet.",
                    style = MaterialTheme.typography.bodyLarge,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                    textAlign = TextAlign.Center,
                )
            }
        }
    }
}

@Composable
private fun Stat(label: String, value: String, modifier: Modifier = Modifier) {
    Surface(
        shape = RoundedCornerShape(22.dp),
        color = MaterialTheme.colorScheme.secondaryContainer,
        modifier = modifier,
    ) {
        Column(Modifier.padding(18.dp)) {
            Text(
                label,
                style = MaterialTheme.typography.labelMedium,
                color = MaterialTheme.colorScheme.onSecondaryContainer,
            )
            Text(
                value,
                style = MaterialTheme.typography.titleLarge,
                fontWeight = FontWeight.Bold,
                color = MaterialTheme.colorScheme.onSecondaryContainer,
            )
        }
    }
}
