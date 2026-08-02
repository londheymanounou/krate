@file:OptIn(androidx.compose.material3.ExperimentalMaterial3Api::class)

package app.krate

import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.rounded.ContentCopy
import androidx.compose.material.icons.rounded.SwapHoriz
import androidx.compose.foundation.layout.*
import androidx.compose.ui.draw.clip
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import kotlin.math.roundToInt
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext

/*
 * Structured forms for tools whose core input is a composed string.
 *
 * The CLI takes "100km 5min" because on a CLI that is faster than any form. On a phone it is a
 * guess: nothing tells you the unit attaches to the number with no space, or that you supply
 * exactly two of the three quantities. These screens give each quantity its own field and unit
 * picker and assemble the string themselves.
 *
 * The core contract is untouched — each screen emits exactly what the CLI would accept, so parity
 * still holds and there is no second implementation of the maths.
 */

/** A number field with a unit dropdown beside it. The pair most of these tools are made of. */
@Composable
fun NumberWithUnit(
    label: String,
    value: String,
    onValueChange: (String) -> Unit,
    unit: String,
    units: List<String>,
    onUnitChange: (String) -> Unit,
    modifier: Modifier = Modifier,
) {
    Row(modifier, horizontalArrangement = Arrangement.spacedBy(10.dp)) {
        OutlinedTextField(
            value = value,
            onValueChange = onValueChange,
            label = { Text(label) },
            keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Decimal),
            singleLine = true,
            shape = RoundedCornerShape(24.dp),
            modifier = Modifier.weight(1.6f),
        )
        var open by remember { mutableStateOf(false) }
        ExposedDropdownMenuBox(
            expanded = open,
            onExpandedChange = { open = it },
            modifier = Modifier.weight(1f),
        ) {
            OutlinedTextField(
                value = unit,
                onValueChange = {},
                readOnly = true,
                singleLine = true,
                label = { Text("Unit") },
                textStyle = MaterialTheme.typography.bodyMedium,
                trailingIcon = { ExposedDropdownMenuDefaults.TrailingIcon(open) },
                shape = RoundedCornerShape(24.dp),
                modifier = Modifier.menuAnchor(MenuAnchorType.PrimaryNotEditable).fillMaxWidth(),
            )
            ExposedDropdownMenu(expanded = open, onDismissRequest = { open = false }) {
                units.forEach {
                    DropdownMenuItem(text = { Text(it) }, onClick = { onUnitChange(it); open = false })
                }
            }
        }
    }
}

@Composable
private fun ResultCard(text: String, isError: Boolean, placeholder: String, modifier: Modifier) {
    Surface(
        shape = RoundedCornerShape(28.dp),
        color = if (isError) MaterialTheme.colorScheme.errorContainer
        else MaterialTheme.colorScheme.primaryContainer,
        modifier = modifier,
    ) {
        Box(Modifier.fillMaxSize().padding(24.dp), contentAlignment = Alignment.Center) {
            Text(
                text.ifEmpty { placeholder },
                style = MaterialTheme.typography.titleLarge,
                fontWeight = FontWeight.Medium,
                textAlign = TextAlign.Center,
                color = if (isError) MaterialTheme.colorScheme.onErrorContainer
                else MaterialTheme.colorScheme.onPrimaryContainer,
            )
        }
    }
}

/**
 * Speed / distance / time. Fill any two and the third is produced.
 *
 * The core wants exactly two tokens, so only non-empty rows are sent; supplying all three is
 * rejected by the core rather than silently ignoring one. Units are exactly `SDT_UNITS` from
 * `physics.rs` — the token is `value + unit` with no space, which is precisely the rule a user
 * could not have guessed.
 */
@Composable
fun SpeedDistanceTimeScreen(modifier: Modifier = Modifier) {
    var dist by remember { mutableStateOf("") }
    var distUnit by remember { mutableStateOf("km") }
    var time by remember { mutableStateOf("") }
    var timeUnit by remember { mutableStateOf("min") }
    var speed by remember { mutableStateOf("") }
    var speedUnit by remember { mutableStateOf("km/h") }
    var result by remember { mutableStateOf("") }
    var isError by remember { mutableStateOf(false) }
    val scope = rememberCoroutineScope()

    val filled = listOf(dist, time, speed).count { it.isNotBlank() }

    LaunchedEffect(dist, distUnit, time, timeUnit, speed, speedUnit) {
        if (filled != 2) { result = ""; isError = false; return@LaunchedEffect }
        val tokens = buildList {
            if (dist.isNotBlank()) add("${dist.trim()}$distUnit")
            if (time.isNotBlank()) add("${time.trim()}$timeUnit")
            if (speed.isNotBlank()) add("${speed.trim()}$speedUnit")
        }
        scope.launch {
            val res = withContext(Dispatchers.IO) {
                Core.run("SpeedDistanceTime", tokens.joinToString(" "))
            }
            result = res.text
            isError = !res.ok
        }
    }

    Column(
        modifier.fillMaxSize().padding(20.dp),
        verticalArrangement = Arrangement.spacedBy(14.dp),
    ) {
        Text(
            "Fill in any two — the third is worked out.",
            style = MaterialTheme.typography.bodyMedium,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
        )
        NumberWithUnit(
            "Distance", dist, { dist = it }, distUnit,
            listOf("m", "km", "cm", "mi", "ft", "yd", "nmi"), { distUnit = it },
            Modifier.fillMaxWidth(),
        )
        NumberWithUnit(
            "Time", time, { time = it }, timeUnit,
            listOf("s", "min", "h", "d"), { timeUnit = it },
            Modifier.fillMaxWidth(),
        )
        NumberWithUnit(
            "Speed", speed, { speed = it }, speedUnit,
            listOf("m/s", "km/h", "mph", "kn"), { speedUnit = it },
            Modifier.fillMaxWidth(),
        )
        ResultCard(
            result,
            isError,
            if (filled > 2) "Clear one field — give exactly two" else "Enter two values",
            Modifier.fillMaxWidth().weight(1f),
        )
    }
}

/**
 * BMI from weight and height.
 *
 * Units are only those the core already understands (kg, and cm/m distinguished by magnitude).
 * Offering pounds and feet would mean converting in Kotlin, which is a second implementation of
 * arithmetic the core owns — the kind of drift the parity tests exist to prevent.
 */
@Composable
fun BmiScreen(modifier: Modifier = Modifier) {
    var kg by remember { mutableStateOf("") }
    var height by remember { mutableStateOf("") }
    var heightUnit by remember { mutableStateOf("cm") }
    var result by remember { mutableStateOf("") }
    var isError by remember { mutableStateOf(false) }
    val scope = rememberCoroutineScope()

    LaunchedEffect(kg, height, heightUnit) {
        if (kg.isBlank() || height.isBlank()) { result = ""; isError = false; return@LaunchedEffect }
        // The core reads the second number as centimetres when it is over 3, metres otherwise.
        val h = height.trim()
        scope.launch {
            val res = withContext(Dispatchers.IO) { Core.run("Bmi", "${kg.trim()} $h") }
            result = res.text
            isError = !res.ok
        }
    }

    Column(
        modifier.fillMaxSize().padding(20.dp),
        verticalArrangement = Arrangement.spacedBy(14.dp),
    ) {
        NumberWithUnit(
            "Weight", kg, { kg = it }, "kg", listOf("kg"), {},
            Modifier.fillMaxWidth(),
        )
        NumberWithUnit(
            "Height", height, { height = it }, heightUnit,
            listOf("cm", "m"), { heightUnit = it },
            Modifier.fillMaxWidth(),
        )
        ResultCard(result, isError, "Enter weight and height", Modifier.fillMaxWidth().weight(1f))
    }
}

/** Aspect ratio from a width and height, instead of a typed "1920x1080". */
@Composable
fun AspectRatioScreen(modifier: Modifier = Modifier) {
    var w by remember { mutableStateOf("") }
    var h by remember { mutableStateOf("") }
    var result by remember { mutableStateOf("") }
    var isError by remember { mutableStateOf(false) }
    val scope = rememberCoroutineScope()

    LaunchedEffect(w, h) {
        if (w.isBlank() || h.isBlank()) { result = ""; isError = false; return@LaunchedEffect }
        scope.launch {
            val res = withContext(Dispatchers.IO) { Core.run("AspectRatio", "${w.trim()}x${h.trim()}") }
            result = res.text
            isError = !res.ok
        }
    }

    Column(
        modifier.fillMaxSize().padding(20.dp),
        verticalArrangement = Arrangement.spacedBy(14.dp),
    ) {
        Row(horizontalArrangement = Arrangement.spacedBy(12.dp)) {
            OutlinedTextField(
                value = w, onValueChange = { w = it }, label = { Text("Width") },
                keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
                singleLine = true, shape = RoundedCornerShape(24.dp), modifier = Modifier.weight(1f),
            )
            OutlinedTextField(
                value = h, onValueChange = { h = it }, label = { Text("Height") },
                keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
                singleLine = true, shape = RoundedCornerShape(24.dp), modifier = Modifier.weight(1f),
            )
        }
        ResultCard(result, isError, "Enter width and height", Modifier.fillMaxWidth().weight(1f))
    }
}

/**
 * Transfer time: a file size and a connection speed, each with its own unit.
 *
 * `physics.rs` wants exactly two tokens and works out which is which by looking for a rate — a rule
 * nobody could infer from "700MB 20Mbps". Splitting it into two fields makes the rule invisible.
 */
@Composable
fun TransferTimeScreen(modifier: Modifier = Modifier) {
    var size by remember { mutableStateOf("") }
    var sizeUnit by remember { mutableStateOf("MB") }
    var speed by remember { mutableStateOf("") }
    var speedUnit by remember { mutableStateOf("Mbps") }
    var out by remember { mutableStateOf("") }
    var err by remember { mutableStateOf(false) }
    val scope = rememberCoroutineScope()

    LaunchedEffect(size, sizeUnit, speed, speedUnit) {
        if (size.isBlank() || speed.isBlank()) { out = ""; err = false; return@LaunchedEffect }
        scope.launch {
            val res = withContext(Dispatchers.IO) {
                Core.run("TransferTime", "${size.trim()}$sizeUnit ${speed.trim()}$speedUnit")
            }
            out = res.text
            err = !res.ok
        }
    }

    Column(
        modifier.fillMaxSize().padding(20.dp),
        verticalArrangement = Arrangement.spacedBy(14.dp),
    ) {
        NumberWithUnit(
            "File size", size, { size = it }, sizeUnit,
            listOf("KB", "MB", "GB", "TB", "KiB", "MiB", "GiB"), { sizeUnit = it },
            Modifier.fillMaxWidth(),
        )
        NumberWithUnit(
            "Connection speed", speed, { speed = it }, speedUnit,
            listOf("Kbps", "Mbps", "Gbps", "KB/s", "MB/s"), { speedUnit = it },
            Modifier.fillMaxWidth(),
        )
        ResultCard(out, err, "Enter a size and a speed", Modifier.fillMaxWidth().weight(1f))
    }
}

/** Bill, tip percentage and split. The core reads three bare numbers in that order. */
@Composable
fun TipScreen(modifier: Modifier = Modifier) {
    var bill by remember { mutableStateOf("") }
    var percent by remember { mutableFloatStateOf(15f) }
    var people by remember { mutableFloatStateOf(1f) }
    var out by remember { mutableStateOf("") }
    var err by remember { mutableStateOf(false) }
    val scope = rememberCoroutineScope()

    LaunchedEffect(bill, percent, people) {
        if (bill.isBlank()) { out = ""; err = false; return@LaunchedEffect }
        scope.launch {
            val res = withContext(Dispatchers.IO) {
                Core.run("Tip", "${bill.trim()} ${percent.roundToInt()} ${people.roundToInt()}")
            }
            out = res.text
            err = !res.ok
        }
    }

    Column(
        modifier.fillMaxSize().padding(20.dp),
        verticalArrangement = Arrangement.spacedBy(12.dp),
    ) {
        OutlinedTextField(
            value = bill, onValueChange = { bill = it }, label = { Text("Bill total") },
            keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Decimal),
            singleLine = true, shape = RoundedCornerShape(24.dp),
            modifier = Modifier.fillMaxWidth(),
        )
        LabelledSlider("Tip", "${percent.roundToInt()}%", percent, 0f..30f, 30) { percent = it }
        LabelledSlider("Split between", "${people.roundToInt()}", people, 1f..12f, 10) { people = it }
        ResultCard(out, err, "Enter the bill", Modifier.fillMaxWidth().weight(1f))
    }
}

/** Principal, annual rate and years — three bare numbers, in that order. */
@Composable
fun LoanScreen(modifier: Modifier = Modifier) {
    var amount by remember { mutableStateOf("") }
    var rate by remember { mutableStateOf("") }
    var years by remember { mutableFloatStateOf(5f) }
    var out by remember { mutableStateOf("") }
    var err by remember { mutableStateOf(false) }
    val scope = rememberCoroutineScope()

    LaunchedEffect(amount, rate, years) {
        if (amount.isBlank() || rate.isBlank()) { out = ""; err = false; return@LaunchedEffect }
        scope.launch {
            val res = withContext(Dispatchers.IO) {
                Core.run("Loan", "${amount.trim()} ${rate.trim()} ${years.roundToInt()}")
            }
            out = res.text
            err = !res.ok
        }
    }

    Column(
        modifier.fillMaxSize().padding(20.dp),
        verticalArrangement = Arrangement.spacedBy(12.dp),
    ) {
        OutlinedTextField(
            value = amount, onValueChange = { amount = it }, label = { Text("Amount borrowed") },
            keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Decimal),
            singleLine = true, shape = RoundedCornerShape(24.dp),
            modifier = Modifier.fillMaxWidth(),
        )
        OutlinedTextField(
            value = rate, onValueChange = { rate = it }, label = { Text("Annual interest rate (%)") },
            keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Decimal),
            singleLine = true, shape = RoundedCornerShape(24.dp),
            modifier = Modifier.fillMaxWidth(),
        )
        LabelledSlider("Term", "${years.roundToInt()} years", years, 1f..30f, 28) { years = it }
        ResultCard(out, err, "Enter amount and rate", Modifier.fillMaxWidth().weight(1f))
    }
}

/** Slider with its name and current value on one line above it. */
@Composable
private fun LabelledSlider(
    label: String,
    display: String,
    value: Float,
    range: ClosedFloatingPointRange<Float>,
    steps: Int,
    onChange: (Float) -> Unit,
) {
    Column(Modifier.fillMaxWidth()) {
        Row(
            Modifier.fillMaxWidth(),
            horizontalArrangement = Arrangement.SpaceBetween,
            verticalAlignment = Alignment.CenterVertically,
        ) {
            Text(label, style = MaterialTheme.typography.titleMedium)
            Text(
                display,
                style = MaterialTheme.typography.titleMedium,
                fontWeight = FontWeight.Bold,
                color = MaterialTheme.colorScheme.primary,
            )
        }
        Slider(value = value, onValueChange = onChange, valueRange = range, steps = steps)
    }
}

/** A hex colour field with a live swatch. Not a colour wheel — the core takes text either way. */
@Composable
private fun ColourField(
    label: String,
    value: String,
    onValueChange: (String) -> Unit,
    modifier: Modifier = Modifier,
) {
    val parsed = runCatching {
        val hex = value.trim().removePrefix("#")
        if (hex.length == 6) androidx.compose.ui.graphics.Color(("ff" + hex).toLong(16)) else null
    }.getOrNull()

    OutlinedTextField(
        value = value,
        onValueChange = onValueChange,
        label = { Text(label) },
        placeholder = { Text("#3366ff") },
        singleLine = true,
        shape = RoundedCornerShape(24.dp),
        modifier = modifier,
        leadingIcon = {
            Box(
                Modifier
                    .padding(start = 4.dp)
                    .size(24.dp)
                    .clip(androidx.compose.foundation.shape.CircleShape)
                    .background(parsed ?: MaterialTheme.colorScheme.surfaceContainerHighest),
            )
        },
    )
}

/** `combinatorics` reads "n k" — two numbers, so two fields. */
@Composable
fun CombinatoricsScreen(modifier: Modifier = Modifier) {
    var n by remember { mutableStateOf("") }
    var k by remember { mutableStateOf("") }
    var out by remember { mutableStateOf("") }
    var err by remember { mutableStateOf(false) }
    val scope = rememberCoroutineScope()

    LaunchedEffect(n, k) {
        if (n.isBlank()) { out = ""; err = false; return@LaunchedEffect }
        scope.launch {
            val res = withContext(Dispatchers.IO) {
                Core.run("Combinatorics", (n.trim() + " " + k.trim()).trim())
            }
            out = res.text
            err = !res.ok
        }
    }

    Column(modifier.fillMaxSize().padding(20.dp), verticalArrangement = Arrangement.spacedBy(14.dp)) {
        Row(horizontalArrangement = Arrangement.spacedBy(12.dp)) {
            OutlinedTextField(
                value = n, onValueChange = { n = it }, label = { Text("n (total)") },
                keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
                singleLine = true, shape = RoundedCornerShape(24.dp), modifier = Modifier.weight(1f),
            )
            OutlinedTextField(
                value = k, onValueChange = { k = it }, label = { Text("k (chosen)") },
                keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
                singleLine = true, shape = RoundedCornerShape(24.dp), modifier = Modifier.weight(1f),
            )
        }
        ResultCard(out, err, "Enter n and k", Modifier.fillMaxWidth().weight(1f))
    }
}

/** `subnet` splits on a slash, so the prefix is a slider rather than something to remember to type. */
@Composable
fun SubnetScreen(modifier: Modifier = Modifier) {
    var address by remember { mutableStateOf("192.168.1.0") }
    var prefix by remember { mutableFloatStateOf(24f) }
    var out by remember { mutableStateOf("") }
    var err by remember { mutableStateOf(false) }
    val scope = rememberCoroutineScope()

    LaunchedEffect(address, prefix) {
        if (address.isBlank()) { out = ""; err = false; return@LaunchedEffect }
        scope.launch {
            val res = withContext(Dispatchers.IO) {
                Core.run("Subnet", address.trim() + "/" + prefix.roundToInt())
            }
            out = res.text
            err = !res.ok
        }
    }

    Column(modifier.fillMaxSize().padding(20.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {
        OutlinedTextField(
            value = address, onValueChange = { address = it }, label = { Text("IPv4 address") },
            singleLine = true, shape = RoundedCornerShape(24.dp), modifier = Modifier.fillMaxWidth(),
        )
        LabelledSlider("Prefix", "/" + prefix.roundToInt(), prefix, 0f..32f, 31) { prefix = it }
        ResultCard(out, err, "Enter an address", Modifier.fillMaxWidth().weight(1f))
    }
}

/**
 * `bases` infers the input base from a 0b / 0o / 0x prefix. Chips add the prefix so nobody has to
 * know that rule, and decimal sends the digits bare.
 */
@Composable
fun BasesScreen(modifier: Modifier = Modifier) {
    var digits by remember { mutableStateOf("") }
    var base by remember { mutableStateOf("Decimal") }
    var out by remember { mutableStateOf("") }
    var err by remember { mutableStateOf(false) }
    val scope = rememberCoroutineScope()

    val prefix = when (base) {
        "Binary" -> "0b"
        "Octal" -> "0o"
        "Hex" -> "0x"
        else -> ""
    }

    LaunchedEffect(digits, base) {
        if (digits.isBlank()) { out = ""; err = false; return@LaunchedEffect }
        scope.launch {
            val res = withContext(Dispatchers.IO) { Core.run("Bases", prefix + digits.trim()) }
            out = res.text
            err = !res.ok
        }
    }

    Column(modifier.fillMaxSize().padding(20.dp), verticalArrangement = Arrangement.spacedBy(14.dp)) {
        Text("Input base", style = MaterialTheme.typography.titleMedium)
        Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            listOf("Decimal", "Binary", "Octal", "Hex").forEach {
                FilterChip(selected = base == it, onClick = { base = it }, label = { Text(it) })
            }
        }
        OutlinedTextField(
            value = digits, onValueChange = { digits = it }, label = { Text("Value") },
            singleLine = true, shape = RoundedCornerShape(24.dp), modifier = Modifier.fillMaxWidth(),
        )
        ResultCard(out, err, "Enter a value", Modifier.fillMaxWidth().weight(1f))
    }
}

/** `contrast` wants one colour per line. Two fields with swatches beat remembering that. */
@Composable
fun ContrastScreen(modifier: Modifier = Modifier) {
    var a by remember { mutableStateOf("#ffffff") }
    var b by remember { mutableStateOf("#767676") }
    var out by remember { mutableStateOf("") }
    var err by remember { mutableStateOf(false) }
    val scope = rememberCoroutineScope()

    LaunchedEffect(a, b) {
        scope.launch {
            val res = withContext(Dispatchers.IO) {
                Core.run("Contrast", a.trim() + "\n" + b.trim())
            }
            out = res.text
            err = !res.ok
        }
    }

    Column(modifier.fillMaxSize().padding(20.dp), verticalArrangement = Arrangement.spacedBy(14.dp)) {
        ColourPickerField("Foreground", a, { a = it }, Modifier.fillMaxWidth())
        ColourPickerField("Background", b, { b = it }, Modifier.fillMaxWidth())
        ResultCard(out, err, "Enter two colours", Modifier.fillMaxWidth().weight(1f))
    }
}

/** `gradient` takes colour stops plus an optional angle in degrees, so the angle gets a slider. */
@Composable
fun GradientScreen(modifier: Modifier = Modifier) {
    var a by remember { mutableStateOf("#ff0000") }
    var b by remember { mutableStateOf("#0000ff") }
    var angle by remember { mutableFloatStateOf(90f) }
    var out by remember { mutableStateOf("") }
    var err by remember { mutableStateOf(false) }
    val scope = rememberCoroutineScope()

    LaunchedEffect(a, b, angle) {
        scope.launch {
            val res = withContext(Dispatchers.IO) {
                Core.run("Gradient", angle.roundToInt().toString() + "deg " + a.trim() + " " + b.trim())
            }
            out = res.text
            err = !res.ok
        }
    }

    Column(modifier.fillMaxSize().padding(20.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {
        ColourPickerField("From", a, { a = it }, Modifier.fillMaxWidth())
        ColourPickerField("To", b, { b = it }, Modifier.fillMaxWidth())
        LabelledSlider("Angle", angle.roundToInt().toString() + "°", angle, 0f..360f, 35) { angle = it }
        // ponytail: Minimum viable visual preview by parsing the hex inputs natively
        Box(
            modifier = Modifier
                .fillMaxWidth()
                .height(80.dp)
                .clip(RoundedCornerShape(24.dp))
                .background(androidx.compose.ui.graphics.Brush.linearGradient(
                    listOf(
                        try { androidx.compose.ui.graphics.Color(android.graphics.Color.parseColor(a)) } catch (e: Exception) { androidx.compose.ui.graphics.Color.Transparent },
                        try { androidx.compose.ui.graphics.Color(android.graphics.Color.parseColor(b)) } catch (e: Exception) { androidx.compose.ui.graphics.Color.Transparent }
                    )
                ))
        )
        ResultCard(out, err, "Pick two colours", Modifier.fillMaxWidth().weight(1f))
    }
}

/**
 * `sequence` reads a *kind* as its first token, then numbers. Nothing on screen ever said that, so
 * the tool was unusable without reading `maths.rs` — the kind is now a chip row, and the number
 * fields change to match what each kind actually needs.
 */
@Composable
fun SequenceScreen(modifier: Modifier = Modifier) {
    val kinds = listOf(
        "Fibonacci" to "fib",
        "Primes" to "prime",
        "Arithmetic" to "arith",
        "Geometric" to "geom",
    )
    var selected by remember { mutableStateOf(kinds.first()) }
    val kind = selected.second
    // A text field, not a slider: the core accepts up to 1000 terms and a slider silently caps it.
    var count by remember { mutableStateOf("20") }
    var start by remember { mutableStateOf("1") }
    var step by remember { mutableStateOf("2") }
    var out by remember { mutableStateOf("") }
    var err by remember { mutableStateOf(false) }
    val scope = rememberCoroutineScope()

    // arith/geom take "start step count"; fib/prime take just a count.
    val needsTwo = kind == "arith" || kind == "geom"

    LaunchedEffect(kind, count, start, step) {
        if (count.isBlank()) { out = ""; err = false; return@LaunchedEffect }
        scope.launch {
            val input = if (needsTwo) {
                kind + " " + start.trim() + " " + step.trim() + " " + count.trim()
            } else {
                kind + " " + count.trim()
            }
            val res = withContext(Dispatchers.IO) { Core.run("Sequence", input) }
            out = res.text
            err = !res.ok
        }
    }

    Column(modifier.fillMaxSize().padding(20.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {
        UnitDropdown("Sequence", selected, kinds, Modifier.fillMaxWidth()) { selected = it }

        if (needsTwo) {
            Row(horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                OutlinedTextField(
                    value = start, onValueChange = { start = it }, label = { Text("Start") },
                    keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Decimal),
                    singleLine = true, shape = RoundedCornerShape(24.dp),
                    modifier = Modifier.weight(1f),
                )
                OutlinedTextField(
                    value = step, onValueChange = { step = it },
                    label = { Text(if (kind == "geom") "Ratio" else "Step") },
                    keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Decimal),
                    singleLine = true, shape = RoundedCornerShape(24.dp),
                    modifier = Modifier.weight(1f),
                )
            }
        }

        OutlinedTextField(
            value = count, onValueChange = { count = it }, label = { Text("How many terms") },
            keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
            singleLine = true, shape = RoundedCornerShape(24.dp), modifier = Modifier.fillMaxWidth(),
        )
        ResultCard(out, err, "Pick a sequence", Modifier.fillMaxWidth().weight(1f))
    }
}

/** `duration` takes "<value> <from> <to>" — the same shape as the unit converter, so same controls. */
@Composable
fun DurationScreen(modifier: Modifier = Modifier) {
    val units = listOf(
        "Milliseconds" to "ms",
        "Seconds" to "s",
        "Minutes" to "min",
        "Hours" to "h",
        "Days" to "d",
        "Weeks" to "wk",
        "Months" to "mo",
        "Years" to "y",
        "Decades" to "decade",
        "Centuries" to "century",
    )
    var value by remember { mutableStateOf("90") }
    var from by remember { mutableStateOf(units[2]) }
    var to by remember { mutableStateOf(units[3]) }
    var out by remember { mutableStateOf("") }
    var err by remember { mutableStateOf(false) }
    val scope = rememberCoroutineScope()

    LaunchedEffect(value, from, to) {
        if (value.isBlank()) { out = ""; err = false; return@LaunchedEffect }
        scope.launch {
            val res = withContext(Dispatchers.IO) {
                Core.run("Duration", value.trim() + " " + from.second + " " + to.second)
            }
            out = res.text
            err = !res.ok
        }
    }

    Column(modifier.fillMaxSize().padding(20.dp), verticalArrangement = Arrangement.spacedBy(14.dp)) {
        OutlinedTextField(
            value = value, onValueChange = { value = it }, label = { Text("Value") },
            keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Decimal),
            singleLine = true, shape = RoundedCornerShape(24.dp), modifier = Modifier.fillMaxWidth(),
        )
        Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(12.dp)) {
            UnitDropdown("From", from, units, Modifier.weight(1f)) { from = it }
            FilledTonalIconButton(onClick = { val t = from; from = to; to = t }) {
                Icon(Icons.Rounded.SwapHoriz, "Swap")
            }
            UnitDropdown("To", to, units, Modifier.weight(1f)) { to = it }
        }
        ResultCard(out, err, "Enter a value", Modifier.fillMaxWidth().weight(1f))
    }
}

/** Named-unit dropdown; the code goes to the core, the name is what a person picks from. */
@Composable
fun UnitDropdown(
    label: String,
    selected: Pair<String, String>,
    options: List<Pair<String, String>>,
    modifier: Modifier = Modifier,
    onSelect: (Pair<String, String>) -> Unit,
) {
    var open by remember { mutableStateOf(false) }
    ExposedDropdownMenuBox(expanded = open, onExpandedChange = { open = it }, modifier = modifier) {
        OutlinedTextField(
            value = selected.first,
            onValueChange = {},
            readOnly = true,
            singleLine = true,
            label = { Text(label) },
            textStyle = MaterialTheme.typography.bodyMedium,
            trailingIcon = { ExposedDropdownMenuDefaults.TrailingIcon(open) },
            shape = RoundedCornerShape(24.dp),
            modifier = Modifier.menuAnchor(MenuAnchorType.PrimaryNotEditable).fillMaxWidth(),
        )
        ExposedDropdownMenu(expanded = open, onDismissRequest = { open = false }) {
            options.forEach { unit ->
                DropdownMenuItem(
                    text = { Text(unit.first) },
                    onClick = { onSelect(unit); open = false },
                )
            }
        }
    }
}

/**
 * The currencies the rate feed carries, name first.
 *
 * A bare "From" text field expected the user to know the ISO code — and to type it in capitals,
 * since the core uppercases but the feed is keyed on three letters. Nobody should have to know
 * that "Swiss franc" is CHF to convert money.
 */
val CURRENCIES: List<Pair<String, String>> = listOf(
    "US dollar" to "USD",
    "Euro" to "EUR",
    "British pound" to "GBP",
    "Japanese yen" to "JPY",
    "Swiss franc" to "CHF",
    "Canadian dollar" to "CAD",
    "Australian dollar" to "AUD",
    "New Zealand dollar" to "NZD",
    "Chinese yuan" to "CNY",
    "Hong Kong dollar" to "HKD",
    "Singapore dollar" to "SGD",
    "Indian rupee" to "INR",
    "South Korean won" to "KRW",
    "Brazilian real" to "BRL",
    "Mexican peso" to "MXN",
    "South African rand" to "ZAR",
    "Swedish krona" to "SEK",
    "Norwegian krone" to "NOK",
    "Danish krone" to "DKK",
    "Polish zloty" to "PLN",
    "Czech koruna" to "CZK",
    "Hungarian forint" to "HUF",
    "Turkish lira" to "TRY",
    "Russian ruble" to "RUB",
    "Israeli shekel" to "ILS",
    "Thai baht" to "THB",
    "Indonesian rupiah" to "IDR",
    "Malaysian ringgit" to "MYR",
    "Philippine peso" to "PHP",
    "Vietnamese dong" to "VND",
    "UAE dirham" to "AED",
    "Saudi riyal" to "SAR",
    "Nigerian naira" to "NGN",
    "Egyptian pound" to "EGP",
    "Argentine peso" to "ARS",
    "Chilean peso" to "CLP",
    "Colombian peso" to "COP",
    "Ukrainian hryvnia" to "UAH",
    "Romanian leu" to "RON",
    "Bulgarian lev" to "BGN",
    "Icelandic krona" to "ISK",
)

/**
 * Any tool whose whole input is one colour: the picker *is* the interface, not a decoration beside
 * a hex field. Covers Palette, the colour converter and the colour-blindness simulator.
 */
@Composable
fun SingleColourScreen(id: String, modifier: Modifier = Modifier) {
    var colour by remember { mutableStateOf("#3366ff") }
    var out by remember { mutableStateOf("") }
    var err by remember { mutableStateOf(false) }
    val scope = rememberCoroutineScope()

    LaunchedEffect(colour) {
        if (colour.isBlank()) { out = ""; err = false; return@LaunchedEffect }
        scope.launch {
            val res = withContext(Dispatchers.IO) { Core.run(id, colour.trim()) }
            out = res.text
            err = !res.ok
        }
    }

    Column(
        modifier.fillMaxSize().padding(20.dp),
        verticalArrangement = Arrangement.spacedBy(16.dp),
    ) {
        ColourPickerField("Colour", colour, { colour = it }, Modifier.fillMaxWidth())
        ResultCard(out, err, "Pick a colour", Modifier.fillMaxWidth().weight(1f))
    }
}

/**
 * Renders a colour tool's output as swatches instead of hex text.
 *
 * The core emits lines like `Deuteranopia        #4a7fd0` — perfectly readable to a developer and
 * useless for the actual question, which is "what does this look like". Any trailing hex on a line
 * becomes a filled row; lines without one fall back to plain text, so a tool that mixes prose and
 * colours still renders sensibly.
 */
private val HEX = Regex("#[0-9a-fA-F]{6}\\b")

@Composable
fun SwatchList(output: String, modifier: Modifier = Modifier) {
    val rows = remember(output) {
        output.lines().filter { it.isNotBlank() }.map { line ->
            val match = HEX.find(line)
            val hex = match?.value
            val label = if (match != null) line.removeRange(match.range).trim() else line.trim()
            label to hex
        }
    }

    Column(
        modifier.verticalScroll(rememberScrollState()),
        verticalArrangement = Arrangement.spacedBy(8.dp),
    ) {
        rows.forEach { (label, hex) ->
            if (hex == null) {
                Text(label, style = MaterialTheme.typography.bodyMedium)
            } else {
                val colour = androidx.compose.ui.graphics.Color(
                    ("ff" + hex.removePrefix("#")).toLong(16)
                )
                Surface(
                    shape = RoundedCornerShape(20.dp),
                    color = colour,
                    modifier = Modifier.fillMaxWidth().height(72.dp),
                ) {
                    Row(
                        Modifier.fillMaxSize().padding(horizontal = 18.dp),
                        verticalAlignment = Alignment.CenterVertically,
                        horizontalArrangement = Arrangement.SpaceBetween,
                    ) {
                        // Label ink flips with the swatch's luminance, or half of them vanish.
                        val ink = if (
                            colour.red * 0.299f + colour.green * 0.587f + colour.blue * 0.114f > 0.6f
                        ) {
                            androidx.compose.ui.graphics.Color.Black
                        } else {
                            androidx.compose.ui.graphics.Color.White
                        }
                        Text(
                            label.ifEmpty { hex },
                            style = MaterialTheme.typography.titleMedium,
                            fontWeight = FontWeight.Medium,
                            color = ink,
                        )
                        Text(
                            hex,
                            style = MaterialTheme.typography.bodyMedium,
                            fontFamily = androidx.compose.ui.text.font.FontFamily.Monospace,
                            color = ink.copy(alpha = 0.8f),
                        )
                    }
                }
            }
        }
    }
}

/** Single-colour tools whose output is a list of colours: show them, do not spell them. */
@Composable
fun ColourOutputScreen(id: String, modifier: Modifier = Modifier) {
    var colour by remember { mutableStateOf("#3366ff") }
    var out by remember { mutableStateOf("") }
    var err by remember { mutableStateOf(false) }
    val scope = rememberCoroutineScope()

    LaunchedEffect(colour) {
        if (colour.isBlank()) { out = ""; err = false; return@LaunchedEffect }
        scope.launch {
            val res = withContext(Dispatchers.IO) { Core.run(id, colour.trim()) }
            out = res.text
            err = !res.ok
        }
    }

    Column(
        modifier.fillMaxSize().padding(20.dp),
        verticalArrangement = Arrangement.spacedBy(16.dp),
    ) {
        ColourPickerField("Colour", colour, { colour = it }, Modifier.fillMaxWidth())
        if (err) {
            ResultCard(out, true, "Pick a colour", Modifier.fillMaxWidth().weight(1f))
        } else {
            SwatchList(out, Modifier.fillMaxWidth().weight(1f))
        }
    }
}

/**
 * Unix timestamp, in the three shapes `dates.rs::timestamp` actually accepts: empty means now, a
 * whole number is an epoch value, anything else is parsed as a date.
 *
 * The text box exposed none of that. Seconds versus milliseconds is resolved by the core from
 * magnitude, so the unit chips here only scale what gets sent — they do not change the contract.
 */
@Composable
fun TimestampScreen(modifier: Modifier = Modifier) {
    var mode by remember { mutableStateOf("now") }
    var epoch by remember { mutableStateOf("") }
    var millis by remember { mutableStateOf(false) }
    val cal = remember { java.util.Calendar.getInstance() }
    var year by remember { mutableStateOf(cal.get(java.util.Calendar.YEAR).toString()) }
    var month by remember { mutableStateOf((cal.get(java.util.Calendar.MONTH) + 1).toString()) }
    var day by remember { mutableStateOf(cal.get(java.util.Calendar.DAY_OF_MONTH).toString()) }
    var hour by remember { mutableStateOf("0") }
    var minute by remember { mutableStateOf("0") }
    var second by remember { mutableStateOf("0") }
    // Composed here so the effect below has one thing to watch.
    val date = "%04d-%02d-%02d %02d:%02d:%02d".format(
        year.toIntOrNull() ?: 0, month.toIntOrNull() ?: 1, day.toIntOrNull() ?: 1,
        hour.toIntOrNull() ?: 0, minute.toIntOrNull() ?: 0, second.toIntOrNull() ?: 0,
    )
    var out by remember { mutableStateOf("") }
    var err by remember { mutableStateOf(false) }
    var tick by remember { mutableIntStateOf(0) }
    val scope = rememberCoroutineScope()
    val clipboard = androidx.compose.ui.platform.LocalClipboardManager.current

    // "Now" is only interesting if it keeps moving; a frozen clock looks broken.
    LaunchedEffect(mode) {
        while (mode == "now") {
            tick++
            kotlinx.coroutines.delay(1000)
        }
    }

    LaunchedEffect(mode, epoch, millis, date, tick) {
        val input = when (mode) {
            "now" -> ""
            "epoch" -> epoch.trim()
            else -> date.trim()
        }
        if (mode != "now" && input.isEmpty()) { out = ""; err = false; return@LaunchedEffect }
        scope.launch {
            val res = withContext(Dispatchers.IO) { Core.run("Timestamp", input) }
            out = res.text
            err = !res.ok
        }
    }

    Column(
        modifier.fillMaxSize().padding(20.dp),
        verticalArrangement = Arrangement.spacedBy(14.dp),
    ) {
        Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            listOf("now" to "Now", "epoch" to "From timestamp", "date" to "From date").forEach {
                FilterChip(
                    selected = mode == it.first,
                    onClick = { mode = it.first },
                    label = { Text(it.second) },
                )
            }
        }

        when (mode) {
            "epoch" -> {
                OutlinedTextField(
                    value = epoch,
                    onValueChange = { epoch = it },
                    label = { Text("Unix timestamp") },
                    placeholder = { Text("1700000000") },
                    keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
                    singleLine = true,
                    shape = RoundedCornerShape(24.dp),
                    modifier = Modifier.fillMaxWidth(),
                    trailingIcon = {
                        TextButton(onClick = {
                            val nowSec = System.currentTimeMillis() / 1000
                            epoch = if (millis) (nowSec * 1000).toString() else nowSec.toString()
                        }) { Text("Now") }
                    },
                )
                Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    FilterChip(
                        selected = !millis,
                        onClick = {
                            // Rescale what is already typed rather than silently reinterpreting it.
                            if (millis) epoch.trim().toLongOrNull()?.let { epoch = (it / 1000).toString() }
                            millis = false
                        },
                        label = { Text("Seconds") },
                    )
                    FilterChip(
                        selected = millis,
                        onClick = {
                            if (!millis) epoch.trim().toLongOrNull()?.let { epoch = (it * 1000).toString() }
                            millis = true
                        },
                        label = { Text("Milliseconds") },
                    )
                }
            }

            "date" -> Column(verticalArrangement = Arrangement.spacedBy(10.dp)) {
                // Six explicit fields rather than one free-text box: this is the "I have a date and
                // want the epoch" case, and typing a format from memory is exactly the guesswork
                // the rest of this app has been removing. They compose into the ISO-ish string
                // `parse_date` already accepts, so the core contract is untouched.
                Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    DatePart("Year", year, 4, Modifier.weight(1.4f)) { year = it }
                    DatePart("Month", month, 2, Modifier.weight(1f)) { month = it }
                    DatePart("Day", day, 2, Modifier.weight(1f)) { day = it }
                }
                Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    DatePart("Hour", hour, 2, Modifier.weight(1f)) { hour = it }
                    DatePart("Minute", minute, 2, Modifier.weight(1f)) { minute = it }
                    DatePart("Second", second, 2, Modifier.weight(1f)) { second = it }
                }
                TextButton(onClick = {
                    val c = java.util.Calendar.getInstance()
                    year = c.get(java.util.Calendar.YEAR).toString()
                    month = (c.get(java.util.Calendar.MONTH) + 1).toString()
                    day = c.get(java.util.Calendar.DAY_OF_MONTH).toString()
                    hour = c.get(java.util.Calendar.HOUR_OF_DAY).toString()
                    minute = c.get(java.util.Calendar.MINUTE).toString()
                    second = c.get(java.util.Calendar.SECOND).toString()
                }) { Text("Fill with now") }
            }
        }

        Box(Modifier.fillMaxWidth().weight(1f)) {
            Surface(
                shape = RoundedCornerShape(28.dp),
                color = if (err) MaterialTheme.colorScheme.errorContainer
                else MaterialTheme.colorScheme.surfaceContainerHigh,
                modifier = Modifier.fillMaxSize(),
            ) {
                Box(Modifier.fillMaxSize().padding(20.dp)) {
                    Text(
                        out.ifEmpty { "Enter a value" },
                        style = MaterialTheme.typography.bodyLarge,
                        fontFamily = androidx.compose.ui.text.font.FontFamily.Monospace,
                        color = if (err) MaterialTheme.colorScheme.onErrorContainer
                        else MaterialTheme.colorScheme.onSurface,
                        modifier = Modifier.verticalScroll(rememberScrollState()),
                    )
                }
            }
            if (out.isNotBlank()) {
                FilledTonalIconButton(
                    onClick = {
                        clipboard.setText(androidx.compose.ui.text.AnnotatedString(out))
                    },
                    modifier = Modifier.align(Alignment.TopEnd).padding(8.dp),
                ) {
                    Icon(Icons.Rounded.ContentCopy, "Copy")
                }
            }
        }
    }
}

/** One zero-padded component of a date. Rejects non-digits so the composed string stays parseable. */
@Composable
private fun DatePart(
    label: String,
    value: String,
    maxLength: Int,
    modifier: Modifier = Modifier,
    onChange: (String) -> Unit,
) {
    OutlinedTextField(
        value = value,
        onValueChange = { text ->
            if (text.length <= maxLength && text.all { it.isDigit() }) onChange(text)
        },
        label = { Text(label) },
        keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
        singleLine = true,
        shape = RoundedCornerShape(20.dp),
        textStyle = MaterialTheme.typography.bodyLarge,
        modifier = modifier,
    )
}

/**
 * Random colour: the colour itself is the answer, so it fills the screen. HEX / RGB / HSL sit
 * underneath because those are what you paste somewhere — but reading three text lines to find out
 * what colour came up was the wrong way round.
 */
@Composable
fun RandomColourScreen(modifier: Modifier = Modifier) {
    var out by remember { mutableStateOf("") }
    val scope = rememberCoroutineScope()
    val clipboard = androidx.compose.ui.platform.LocalClipboardManager.current

    fun roll() = scope.launch {
        val res = withContext(Dispatchers.IO) { Core.run("RandomColor", "") }
        if (res.ok) out = res.text
    }
    LaunchedEffect(Unit) { roll() }

    val swatch = remember(out) {
        Regex("#[0-9a-fA-F]{6}").find(out)?.value?.let {
            androidx.compose.ui.graphics.Color(("ff" + it.removePrefix("#")).toLong(16))
        }
    }

    Column(
        modifier.fillMaxSize().padding(20.dp),
        verticalArrangement = Arrangement.spacedBy(14.dp),
    ) {
        Surface(
            shape = RoundedCornerShape(32.dp),
            color = swatch ?: MaterialTheme.colorScheme.surfaceContainerHigh,
            modifier = Modifier.fillMaxWidth().weight(1f),
        ) {}

        out.lines().filter { it.isNotBlank() }.forEach { line ->
            // "HEX  #AABBCC" -> label and value, each copyable on its own.
            val label = line.substringBefore("  ").trim()
            val value = line.substringAfter("  ").trim()
            Surface(
                shape = RoundedCornerShape(20.dp),
                color = MaterialTheme.colorScheme.surfaceContainerLow,
                modifier = Modifier.fillMaxWidth().clickable {
                    clipboard.setText(androidx.compose.ui.text.AnnotatedString(value))
                },
            ) {
                Row(
                    Modifier.fillMaxWidth().padding(horizontal = 18.dp, vertical = 14.dp),
                    horizontalArrangement = Arrangement.SpaceBetween,
                ) {
                    Text(label, style = MaterialTheme.typography.labelLarge,
                        color = MaterialTheme.colorScheme.onSurfaceVariant)
                    Text(
                        value,
                        style = MaterialTheme.typography.bodyLarge,
                        fontFamily = androidx.compose.ui.text.font.FontFamily.Monospace,
                    )
                }
            }
        }

        Button(
            onClick = { roll() },
            modifier = Modifier.fillMaxWidth().height(56.dp),
            shape = RoundedCornerShape(28.dp),
        ) { Text("New colour", style = MaterialTheme.typography.titleMedium) }
    }
}

@Composable
fun ShoeSizeScreen(modifier: Modifier = Modifier) {
    val genders = listOf("Men" to "", "Women" to "w")
    val systems = listOf("EU" to "eu", "US" to "us", "UK" to "uk", "CM" to "cm")

    var gender by remember { mutableStateOf(genders[0]) }
    var system by remember { mutableStateOf(systems[0]) }
    var size by remember { mutableStateOf("") }
    var out by remember { mutableStateOf("") }
    var err by remember { mutableStateOf(false) }
    val scope = rememberCoroutineScope()

    LaunchedEffect(gender, system, size) {
        if (size.isBlank()) { out = ""; err = false; return@LaunchedEffect }
        scope.launch {
            val res = withContext(Dispatchers.IO) { Core.run("ShoeSize", "${size.trim()} ${gender.second} ${system.second}") }
            out = res.text; err = !res.ok
        }
    }

    Column(modifier.fillMaxSize().padding(20.dp), verticalArrangement = Arrangement.spacedBy(14.dp)) {
        OutlinedTextField(
            value = size,
            onValueChange = { size = it },
            label = { Text("Size") },
            modifier = Modifier.fillMaxWidth(),
            shape = RoundedCornerShape(24.dp),
            keyboardOptions = androidx.compose.foundation.text.KeyboardOptions(keyboardType = androidx.compose.ui.text.input.KeyboardType.Number)
        )
        Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(12.dp)) {
            UnitDropdown("Gender", gender, genders, Modifier.weight(1f)) { gender = it }
            UnitDropdown("System", system, systems, Modifier.weight(1f)) { system = it }
        }
        ResultCard(out, err, "Enter a size to convert", Modifier.fillMaxWidth().weight(1f))
    }
}

@Composable
fun DateDiffScreen(modifier: Modifier = Modifier) {
    var date1 by remember { mutableStateOf("") }
    var date2 by remember { mutableStateOf("") }
    var result by remember { mutableStateOf("") }
    var isError by remember { mutableStateOf(false) }
    val scope = rememberCoroutineScope()

    LaunchedEffect(date1, date2) {
        if (date1.isBlank() && date2.isBlank()) { result = ""; isError = false; return@LaunchedEffect }
        val d1 = date1.trim()
        val d2 = date2.trim()
        val query = if (d2.isBlank()) d1 else "$d1 $d2"
        scope.launch {
            val res = withContext(Dispatchers.IO) { Core.run("DateDiff", query) }
            result = res.text
            isError = !res.ok
        }
    }

    Column(modifier.fillMaxSize().padding(20.dp), verticalArrangement = Arrangement.spacedBy(14.dp)) {
        OutlinedTextField(
            value = date1,
            onValueChange = { date1 = it },
            label = { Text("Start Date") },
            placeholder = { Text("YYYY-MM-DD") },
            modifier = Modifier.fillMaxWidth(),
            shape = RoundedCornerShape(24.dp),
            singleLine = true
        )
        OutlinedTextField(
            value = date2,
            onValueChange = { date2 = it },
            label = { Text("End Date (optional)") },
            placeholder = { Text("YYYY-MM-DD") },
            modifier = Modifier.fillMaxWidth(),
            shape = RoundedCornerShape(24.dp),
            singleLine = true
        )
        ResultCard(result, isError, "Enter dates to compare", Modifier.fillMaxWidth().weight(1f))
    }
}

@Composable
fun RegexScreen(modifier: Modifier = Modifier) {
    var pattern by remember { mutableStateOf("") }
    var subject by remember { mutableStateOf("") }
    var result by remember { mutableStateOf("") }
    var isError by remember { mutableStateOf(false) }
    val scope = rememberCoroutineScope()

    LaunchedEffect(pattern, subject) {
        if (pattern.isBlank() || subject.isBlank()) { result = ""; isError = false; return@LaunchedEffect }
        scope.launch {
            val res = withContext(Dispatchers.IO) { Core.run("Regex", "${pattern}\n${subject}") }
            result = res.text
            isError = !res.ok
        }
    }

    Column(modifier.fillMaxSize().padding(20.dp), verticalArrangement = Arrangement.spacedBy(14.dp)) {
        OutlinedTextField(
            value = pattern,
            onValueChange = { pattern = it },
            label = { Text("Regex Pattern") },
            placeholder = { Text("/[a-z]+/i") },
            modifier = Modifier.fillMaxWidth(),
            shape = RoundedCornerShape(24.dp),
            singleLine = true
        )
        OutlinedTextField(
            value = subject,
            onValueChange = { subject = it },
            label = { Text("Subject Text") },
            modifier = Modifier.fillMaxWidth(),
            shape = RoundedCornerShape(24.dp),
            minLines = 3,
            maxLines = 5
        )
        Surface(
            shape = RoundedCornerShape(28.dp),
            color = if (isError) MaterialTheme.colorScheme.errorContainer else MaterialTheme.colorScheme.surfaceVariant,
            modifier = Modifier.fillMaxWidth().weight(1f),
        ) {
            Box(Modifier.fillMaxSize().padding(24.dp)) {
                Text(
                    result.ifEmpty { "Enter pattern and subject" },
                    color = if (isError) MaterialTheme.colorScheme.onErrorContainer else MaterialTheme.colorScheme.onSurfaceVariant,
                )
            }
        }
    }
}

@Composable
fun DiffScreen(modifier: Modifier = Modifier) {
    var text1 by remember { mutableStateOf("") }
    var text2 by remember { mutableStateOf("") }
    var result by remember { mutableStateOf("") }
    var isError by remember { mutableStateOf(false) }
    val scope = rememberCoroutineScope()

    LaunchedEffect(text1, text2) {
        if (text1.isBlank() && text2.isBlank()) { result = ""; isError = false; return@LaunchedEffect }
        scope.launch {
            val res = withContext(Dispatchers.IO) { Core.run("Diff", "${text1}\n---\n${text2}") }
            result = res.text
            isError = !res.ok
        }
    }

    Column(modifier.fillMaxSize().padding(20.dp), verticalArrangement = Arrangement.spacedBy(14.dp)) {
        OutlinedTextField(
            value = text1,
            onValueChange = { text1 = it },
            label = { Text("Original Text") },
            modifier = Modifier.fillMaxWidth().weight(1f),
            shape = RoundedCornerShape(24.dp)
        )
        OutlinedTextField(
            value = text2,
            onValueChange = { text2 = it },
            label = { Text("New Text") },
            modifier = Modifier.fillMaxWidth().weight(1f),
            shape = RoundedCornerShape(24.dp)
        )
        Surface(
            shape = RoundedCornerShape(28.dp),
            color = if (isError) MaterialTheme.colorScheme.errorContainer else MaterialTheme.colorScheme.surfaceVariant,
            modifier = Modifier.fillMaxWidth().weight(1.5f),
        ) {
            val scroll = rememberScrollState()
            Box(Modifier.fillMaxSize().verticalScroll(scroll).padding(24.dp)) {
                Text(
                    result.ifEmpty { "Differences will appear here" },
                    style = androidx.compose.ui.text.TextStyle(fontFamily = androidx.compose.ui.text.font.FontFamily.Monospace),
                    color = if (isError) MaterialTheme.colorScheme.onErrorContainer else MaterialTheme.colorScheme.onSurfaceVariant,
                )
            }
        }
    }
}

@Composable
fun CryptScreen(id: String, modifier: Modifier = Modifier) {
    var path by remember { mutableStateOf("") }
    var password by remember { mutableStateOf("") }
    var result by remember { mutableStateOf("") }
    var isError by remember { mutableStateOf(false) }
    val scope = rememberCoroutineScope()
    
    Column(modifier.fillMaxSize().padding(20.dp), verticalArrangement = Arrangement.spacedBy(14.dp)) {
        FilePathField(
            value = path,
            onValueChange = { path = it },
            label = "File Path",
            modifier = Modifier.fillMaxWidth()
        )
        OutlinedTextField(
            value = password,
            onValueChange = { password = it },
            label = { Text("Password") },
            modifier = Modifier.fillMaxWidth(),
            shape = RoundedCornerShape(24.dp),
            singleLine = true,
            visualTransformation = androidx.compose.ui.text.input.PasswordVisualTransformation(),
            keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Password)
        )
        
        Button(
            onClick = {
                if (path.isNotBlank() && password.isNotBlank()) {
                    scope.launch {
                        val res = withContext(Dispatchers.IO) { Core.run(id, "\"$path\" $password") }
                        result = res.text
                        isError = !res.ok
                    }
                }
            },
            modifier = Modifier.fillMaxWidth().height(56.dp),
        ) {
            Text(if (id == "Encrypt") "Encrypt File" else "Decrypt File")
        }
        
        if (result.isNotBlank()) {
            Surface(
                shape = RoundedCornerShape(28.dp),
                color = if (isError) MaterialTheme.colorScheme.errorContainer else MaterialTheme.colorScheme.primaryContainer,
                modifier = Modifier.fillMaxWidth().weight(1f),
            ) {
                Box(Modifier.fillMaxSize().padding(24.dp), contentAlignment = Alignment.Center) {
                    Text(
                        result,
                        style = MaterialTheme.typography.titleLarge,
                        textAlign = TextAlign.Center,
                        color = if (isError) MaterialTheme.colorScheme.onErrorContainer else MaterialTheme.colorScheme.onPrimaryContainer,
                    )
                }
            }
        }
    }
}
