package app.krate

import app.krate.theme.KrateTheme

import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.compose.foundation.clickable
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.lazy.itemsIndexed
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.*
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.rounded.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.input.nestedscroll.nestedScroll
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.navigation.compose.NavHost
import androidx.navigation.compose.composable
import androidx.navigation.compose.rememberNavController
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import kotlinx.coroutines.launch
import android.os.Environment
import android.os.Build
import android.content.Intent
import android.net.Uri

class MainActivity : ComponentActivity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        enableEdgeToEdge()

        // Storage access is NOT requested here. Firing the settings intent from onCreate meant a
        // fresh install showed a system permission page before the app itself — a first run that
        // looks broken, and a Play review risk. The file browser asks for it when a file tool
        // actually needs it (see FileBrowser.kt); every other tool works without it.

        
        // Setup KRATE runtime string
        Core.setRuntime("Android 15") // Placeholder runtime
        // Setup language based on system locale
        val saved = Settings.language(this)
        Core.setLanguage(saved.ifEmpty { java.util.Locale.getDefault().toLanguageTag() })

        setContent {
            androidx.compose.runtime.CompositionLocalProvider(
                LocalHaptics provides Settings.haptics(this)
            ) {
                KrateApp()
            }
        }
    }
}

/**
 * Icon per raw category key. These are exactly the twelve keys in the core's catalogue — note
 * `Maths`, `Colors`, `Dates`, `Files`, `Images` are plural there; the singular spellings this once
 * used matched nothing and fell through to a wrench for all 140 tools.
 */
fun categoryIcon(key: String): androidx.compose.ui.graphics.vector.ImageVector = when (key) {
    "Text" -> androidx.compose.material.icons.Icons.Rounded.TextFields
    "Developer" -> androidx.compose.material.icons.Icons.Rounded.Code
    "Encoding" -> androidx.compose.material.icons.Icons.Rounded.SwapHoriz
    "Files" -> androidx.compose.material.icons.Icons.Rounded.Folder
    "Hashing" -> androidx.compose.material.icons.Icons.Rounded.Tag
    "Everyday" -> androidx.compose.material.icons.Icons.Rounded.Lightbulb
    "Random" -> androidx.compose.material.icons.Icons.Rounded.Casino
    "Maths" -> androidx.compose.material.icons.Icons.Rounded.Calculate
    "Conversions" -> androidx.compose.material.icons.Icons.Rounded.Straighten
    "Colors" -> androidx.compose.material.icons.Icons.Rounded.Palette
    "Dates" -> androidx.compose.material.icons.Icons.Rounded.Event
    "Images" -> androidx.compose.material.icons.Icons.Rounded.Image
    "Sensors" -> androidx.compose.material.icons.Icons.Rounded.Sensors
    else -> androidx.compose.material.icons.Icons.Rounded.Build
}

/**
 * [category] is the localized display name and is for *showing* only. Anything that branches on
 * category — icons, input-shape heuristics, grouping logic — must use [categoryKey], the raw
 * unlocalized key, or it silently stops matching the moment the app is not in English.
 */
data class ToolInfo(
    val index: Int,
    val id: String,
    val name: String,
    val category: String,
    val categoryKey: String,
)

/**
 * Which home screen to show. The original flat list is kept intact below the early return in the
 * "home" route — flip this to false to get it back, no other edit needed.
 */
const val USE_NEW_HOME = true

/** Catalogue entries with no Android implementation. Kept out of the list rather than stubbed. */
val UNIMPLEMENTED = emptySet<String>()

/** Tools implemented by the Android shell alone: id, display name, raw category key. */
val LOCAL_TOOLS = listOf(
    Triple("Clock", "Clock", "Dates"),
    Triple("TimerStopwatch", "Timer/Stopwatch", "Dates"),
    Triple("Compass", "Compass", "Sensors"),
    Triple("Accelerometer", "Accelerometer", "Sensors"),
    Triple("Gyroscope", "Gyroscope", "Sensors"),
    Triple("Magnetometer", "Magnetometer", "Sensors"),
    Triple("Ruler", "Ruler", "Sensors"),
    Triple("Gamepad", "Gamepad tester", "Sensors"),
    Triple("SoundTester", "Sound tester", "Sensors"),
    Triple("SoundMeter", "Sound Meter", "Sensors"),
    Triple("Tally", "Tally counter", "Everyday"),
    Triple("QrScanner", "QR Scanner", "Everyday"),
    Triple("Pomodoro", "Pomodoro Timer", "Dates"),
    Triple("ColorPicker", "Color Picker", "Colors"),
    Triple("Downloader", "Media downloader", "Files"),
    Triple("FileConverter", "File converter", "Files"),
    Triple("Notepad", "Notepad", "Text"),
    Triple("MarkdownPreview", "Markdown preview", "Text"),
    Triple("BinaryText", "Binary/Text Converter", "Text"),
    Triple("Watermark", "Watermark", "Images")
)

/**
 * Copy affordance for a result panel. Overlaid top-end rather than placed in the flow so it does
 * not shift the text, and hidden while there is nothing to copy — a dead button on an empty panel
 * reads as broken.
 *
 * Every tool in the catalogue produces text and none of it could be copied before this; on a phone
 * that made most of them read-only curiosities.
 */
@Composable
fun BoxScope.CopyResultButton(text: String, onError: Boolean) {
    if (text.isBlank()) return
    val clipboard = androidx.compose.ui.platform.LocalClipboardManager.current
    var copied by remember(text) { mutableStateOf(false) }
    FilledTonalIconButton(
        onClick = {
            clipboard.setText(androidx.compose.ui.text.AnnotatedString(text))
            copied = true
        },
        modifier = Modifier.align(Alignment.TopEnd),
    ) {
        Icon(
            if (copied) Icons.Rounded.Check else Icons.Rounded.ContentCopy,
            contentDescription = if (copied) "Copied" else "Copy result",
        )
    }
}


/** The tool catalogue, read from the core once per process. */
object Catalog {
    @Volatile private var cached: List<ToolInfo>? = null

    fun tools(): List<ToolInfo> = cached ?: synchronized(this) {
        cached ?: build().also { cached = it }
    }

    /** Every name in the cache is localized, so a language change has to rebuild it. */
    fun invalidate() = synchronized(this) { cached = null }

    private fun build(): List<ToolInfo> {
        val count = Core.toolCount()
        val list = ArrayList<ToolInfo>(count)
        for (i in 0 until count) {
            val id = Core.toolId(i) ?: continue
            val name = Core.toolName(i) ?: continue
            val cat = Core.toolCategoryName(i) ?: continue
            val key = Core.toolCategory(i) ?: continue
            if (id !in UNIMPLEMENTED) list.add(ToolInfo(i, id, name, cat, key))
        }
        // Android-only tools. These have no Rust counterpart because the core is pure computation
        // and never touches hardware, so they are appended here rather than coming over the FFI.
        // Index is negative to make it obvious they are not catalogue positions.
        LOCAL_TOOLS.forEachIndexed { n, (id, name, key) ->
            // Reuse the core's localized category name where the category exists there, so a local
            // tool sits under the same heading as the core tools beside it in every language.
            val display = list.firstOrNull { it.categoryKey == key }?.category
                ?: "Sensors & measure"
            list.add(ToolInfo(-1 - n, id, name, display, key))
        }
        return list
    }
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun KrateApp() {
    KrateTheme {
        val navController = rememberNavController()

        // Process-level, not per-composition: building this is 4 FFI round trips per tool
        // (~550 calls, each allocating a String in Rust and freeing it) and the catalogue cannot
        // change while the app is running. `remember` alone would rebuild it after a config change
        // such as a rotation or a theme switch.
        val tools = remember { Catalog.tools() }

        var searchQuery by remember { mutableStateOf("") }
        val filteredTools = remember(tools, searchQuery) {
            if (searchQuery.isBlank()) {
                tools
            } else {
                val q = searchQuery.trim()
                // Unranked `contains` buried "Unit converter" below "Convert to CRLF" and
                // "Case Converter" when searching "convert" — the tool you want was off-screen.
                // Rank by how well the *name* matches before falling back to id/category hits.
                fun score(t: ToolInfo): Int = when {
                    t.name.equals(q, true) -> 0
                    t.name.startsWith(q, true) -> 1
                    t.name.split(' ').any { it.startsWith(q, true) } -> 2
                    t.name.contains(q, true) -> 3
                    t.id.startsWith(q, true) -> 4
                    t.id.contains(q, true) -> 5
                    t.category.contains(q, true) -> 6
                    else -> Int.MAX_VALUE
                }
                tools.map { it to score(it) }
                    .filter { it.second != Int.MAX_VALUE }
                    .sortedWith(compareBy({ it.second }, { it.first.name }))
                    .map { it.first }
            }
        }
        val groupedTools = remember(filteredTools) { filteredTools.groupBy { it.category } }

        NavHost(navController, startDestination = "home", modifier = Modifier.background(MaterialTheme.colorScheme.surface)) {
            composable("home") {
                if (USE_NEW_HOME) {
                    val ctx = androidx.compose.ui.platform.LocalContext.current
                    HomeScreen(
                        tools = tools,
                        onOpenTool = { Recents.record(ctx, it); navController.navigate("tool/$it") },
                        onOpenStats = { navController.navigate("stats") },
                        onOpenSettings = { navController.navigate("settings") },
                        modifier = Modifier.fillMaxSize(),
                    )
                    return@composable
                }
                val scrollBehavior = TopAppBarDefaults.exitUntilCollapsedScrollBehavior()
                Scaffold(
                    modifier = Modifier.nestedScroll(scrollBehavior.nestedScrollConnection),
                    topBar = {
                        LargeTopAppBar(
                            title = { Text("Krate", fontWeight = FontWeight.Medium) },
                            scrollBehavior = scrollBehavior,
                            colors = TopAppBarDefaults.largeTopAppBarColors(
                                containerColor = MaterialTheme.colorScheme.surface,
                                scrolledContainerColor = MaterialTheme.colorScheme.surface
                            )
                        )
                    },
                    containerColor = MaterialTheme.colorScheme.surface
                ) { innerPadding ->
                    LazyColumn(
                        contentPadding = PaddingValues(
                            top = innerPadding.calculateTopPadding(),
                            bottom = innerPadding.calculateBottomPadding() + 80.dp,
                        ),
                        modifier = Modifier.fillMaxSize(),
                    ) {
                        item {
                            androidx.compose.material3.OutlinedTextField(
                                value = searchQuery,
                                onValueChange = { searchQuery = it },
                                modifier = Modifier
                                    .fillMaxWidth()
                                    .padding(horizontal = 16.dp)
                                    .padding(bottom = 8.dp),
                                placeholder = { Text("Search tools...") },
                                leadingIcon = { Icon(androidx.compose.material.icons.Icons.Rounded.Search, contentDescription = "Search") },
                                trailingIcon = {
                                    if (searchQuery.isNotEmpty()) {
                                        IconButton(onClick = { searchQuery = "" }) {
                                            Icon(androidx.compose.material.icons.Icons.Rounded.Clear, contentDescription = "Clear")
                                        }
                                    }
                                },
                                shape = CircleShape,
                                colors = TextFieldDefaults.colors(
                                    focusedContainerColor = MaterialTheme.colorScheme.surfaceVariant,
                                    unfocusedContainerColor = MaterialTheme.colorScheme.surfaceVariant,
                                    focusedIndicatorColor = androidx.compose.ui.graphics.Color.Transparent,
                                    unfocusedIndicatorColor = androidx.compose.ui.graphics.Color.Transparent
                                ),
                                singleLine = true
                            )
                        }

                        groupedTools.forEach { (category, categoryTools) ->
                            item(key = "header_$category") {
                                Text(
                                    text = category,
                                    style = MaterialTheme.typography.titleSmall,
                                    color = MaterialTheme.colorScheme.primary,
                                    fontWeight = FontWeight.Bold,
                                    modifier = Modifier.padding(start = 24.dp, top = 24.dp, bottom = 8.dp)
                                )
                            }
                            itemsIndexed(categoryTools, key = { _, t -> t.id }) { index, tool ->
                                // MD3E grouped list: a category reads as one container, not a stack
                                // of detached pills. Outer edges of the group get the large radius,
                                // inner edges a tight one, with a 2dp seam between rows.
                                val first = index == 0
                                val last = index == categoryTools.lastIndex
                                val big = 24.dp
                                val small = 4.dp
                                Surface(
                                    modifier = Modifier
                                        .animateItem()
                                        .fillMaxWidth()
                                        .padding(horizontal = 16.dp)
                                        .padding(bottom = if (last) 0.dp else 2.dp)
                                        .clip(
                                            RoundedCornerShape(
                                                topStart = if (first) big else small,
                                                topEnd = if (first) big else small,
                                                bottomStart = if (last) big else small,
                                                bottomEnd = if (last) big else small,
                                            )
                                        )
                                        .clickable { navController.navigate("tool/${tool.id}") },
                                    color = MaterialTheme.colorScheme.surfaceContainerLow
                                ) {
                                    ListItem(
                                        headlineContent = { 
                                            Text(tool.name, style = MaterialTheme.typography.titleMedium, fontWeight = FontWeight.Medium) 
                                        },
                                        leadingContent = {
                                            Box(
                                                modifier = Modifier
                                                    .size(48.dp)
                                                    .clip(CircleShape)
                                                    .background(MaterialTheme.colorScheme.secondaryContainer),
                                                contentAlignment = Alignment.Center
                                            ) {
                                                val icon = toolIcon(tool.id, tool.categoryKey)
                                                Icon(
                                                    imageVector = icon,
                                                    contentDescription = null,
                                                    tint = MaterialTheme.colorScheme.onSecondaryContainer
                                                )
                                            }
                                        },
                                        colors = ListItemDefaults.colors(containerColor = androidx.compose.ui.graphics.Color.Transparent)
                                    )
                                }
                            }
                        }
                    }
                }
            }

            composable("settings") {
                SettingsScreen(
                    onBack = { navController.popBackStack() },
                    modifier = Modifier.fillMaxSize(),
                )
            }

            composable("stats") {
                StatsScreen(tools, onBack = { navController.popBackStack() }, modifier = Modifier.fillMaxSize())
            }

            composable("tool/{id}") { backStackEntry ->
                val id = backStackEntry.arguments?.getString("id") ?: ""
                val tool = tools.find { it.id == id }

                Scaffold(
                    topBar = {
                        TopAppBar(
                            title = { Text(tool?.name ?: id, fontWeight = FontWeight.Medium) },
                            navigationIcon = {
                                TextButton(onClick = { navController.navigateUp() }) {
                                    Text("Back", style = MaterialTheme.typography.titleMedium)
                                }
                            },
                            colors = TopAppBarDefaults.topAppBarColors(
                                containerColor = MaterialTheme.colorScheme.surface
                            )
                        )
                    },
                    containerColor = MaterialTheme.colorScheme.surface
                ) { innerPadding ->
                    when (id) {
                        "Currency" -> CurrencyScreen(Modifier.padding(innerPadding))
                        "Timezone" -> TimezoneScreen(Modifier.padding(innerPadding))
                        "Encrypt", "Decrypt" -> EncryptDecryptScreen(id, Modifier.padding(innerPadding))
                        "Zip", "Unzip" -> ZipUnzipScreen(id, Modifier.padding(innerPadding))
                        "Cron" -> CronScreen(Modifier.padding(innerPadding))
                        "Compass" -> CompassAndGpsScreen(Modifier.padding(innerPadding))
                        "Accelerometer" -> AccelerometerScreen(Modifier.padding(innerPadding))
                        "Gyroscope" -> GyroscopeScreen(Modifier.padding(innerPadding))
                        "Magnetometer" -> MagnetometerScreen(Modifier.padding(innerPadding))
                        "Ruler" -> RulerScreen(Modifier.padding(innerPadding))
                        "Gamepad" -> GamepadScreen(Modifier.padding(innerPadding))
                        "Downloader" -> DownloaderScreen(Modifier.padding(innerPadding))
                        "Tally" -> TallyScreen(Modifier.padding(innerPadding))
                        "Qr" -> QrScreen(Modifier.padding(innerPadding))
                        "SpeedDistanceTime" -> SpeedDistanceTimeScreen(Modifier.padding(innerPadding))
                        "Bmi" -> BmiScreen(Modifier.padding(innerPadding))
                        "TransferTime" -> TransferTimeScreen(Modifier.padding(innerPadding))
                        "Tip" -> TipScreen(Modifier.padding(innerPadding))
                        "Loan" -> LoanScreen(Modifier.padding(innerPadding))
                        "Combinatorics" -> CombinatoricsScreen(Modifier.padding(innerPadding))
                        "Subnet" -> SubnetScreen(Modifier.padding(innerPadding))
                        "Bases" -> BasesScreen(Modifier.padding(innerPadding))
                        "Contrast" -> ContrastScreen(Modifier.padding(innerPadding))
                        "Gradient" -> GradientScreen(Modifier.padding(innerPadding))
                        "ColorBlind", "Palette" ->
                            ColourOutputScreen(id, Modifier.padding(innerPadding))
                        "Color" -> SingleColourScreen(id, Modifier.padding(innerPadding))
                        "Sequence" -> SequenceScreen(Modifier.padding(innerPadding))
                        "Duration" -> DurationScreen(Modifier.padding(innerPadding))
                        "Timestamp" -> TimestampScreen(Modifier.padding(innerPadding))
                        "AspectRatio" -> AspectRatioScreen(Modifier.padding(innerPadding))
                        "Calc" -> CalcScreen(Modifier.padding(innerPadding))
                        "Convert" -> ConvertScreen(Modifier.padding(innerPadding))
                        "Barcode" -> BarcodeScreen(Modifier.padding(innerPadding))
                        "Random" -> RandomNumberScreen(Modifier.padding(innerPadding))
                        "Teams" -> TeamsScreen(Modifier.padding(innerPadding))
                        "RandomColor" -> RandomColourScreen(Modifier.padding(innerPadding))
                        "Pick" -> WheelScreen(Modifier.padding(innerPadding))
                        "Shuffle" -> ListToolScreen(id, Modifier.padding(innerPadding))
                        "Password" -> PasswordScreen(Modifier.padding(innerPadding))
                        "Dice" -> DiceScreen(Modifier.padding(innerPadding))
                        "Coin" -> CoinScreen(Modifier.padding(innerPadding))
                        "Cards" -> CardsScreen(Modifier.padding(innerPadding))
                        "Rename" -> RenameScreen(Modifier.padding(innerPadding))
                        "StripMetadata" -> StripMetadataScreen(Modifier.padding(innerPadding))
                        "PdfMerge" -> PdfMergeScreen(Modifier.padding(innerPadding))
                        "PdfSplit" -> PdfSplitScreen(Modifier.padding(innerPadding))
                        "Snake" -> SnakeScreen(Modifier.padding(innerPadding))
                        "Game2048" -> Game2048Screen(Modifier.padding(innerPadding))
                        "Tetris" -> TetrisScreen(Modifier.padding(innerPadding))
                        "Weather" -> WeatherScreen(Modifier.padding(innerPadding))
                        "ShoeSize" -> ShoeSizeScreen(Modifier.padding(innerPadding))
                        "SoundTester" -> SoundTesterScreen(Modifier.padding(innerPadding))
                        "Clock" -> ClockScreen(Modifier.padding(innerPadding))
                        "TimerStopwatch" -> TimerStopwatchScreen(Modifier.padding(innerPadding))
                        "FileConverter" -> ConverterScreen(Modifier.padding(innerPadding))
                        "Notepad" -> NotepadScreen(Modifier.padding(innerPadding))
                        "MarkdownPreview" -> MarkdownPreviewScreen(Modifier.padding(innerPadding))
                        "Percent" -> PercentScreen(Modifier.padding(innerPadding))
                        "Solve" -> SolveScreen(Modifier.padding(innerPadding))
                        "Factor" -> FactorScreen(Modifier.padding(innerPadding))
                        "Fraction" -> FractionScreen(Modifier.padding(innerPadding))
                        "Statistics" -> StatisticsScreen(Modifier.padding(innerPadding))
                        "DateDiff" -> DateDiffScreen(Modifier.padding(innerPadding))
                        "Regex" -> RegexScreen(Modifier.padding(innerPadding))
                        "Diff" -> DiffScreen(Modifier.padding(innerPadding))
                        "Encrypt", "Decrypt" -> CryptScreen(id, Modifier.padding(innerPadding))
                        "Jwt" -> JwtScreen(Modifier.padding(innerPadding))
                        "Pomodoro" -> PomodoroScreen(Modifier.padding(innerPadding))
                        "ColorPicker" -> ColorPickerScreen(Modifier.padding(innerPadding))
                        "BinaryText" -> BaseTextScreen(Modifier.padding(innerPadding))
                        "Downloader" -> DownloaderScreen(Modifier.padding(innerPadding))
                        "SoundMeter" -> SoundMeterScreen(Modifier.padding(innerPadding))
                        "QrScanner" -> QrScannerScreen(Modifier.padding(innerPadding))
                        "Watermark" -> WatermarkScreen(Modifier.padding(innerPadding))
                        else -> ToolScreen(id, tool?.name ?: id, tool?.categoryKey ?: "", Modifier.padding(innerPadding))
                    }
                }
            }
        }
    }
}

@Composable
fun ToolScreen(id: String, name: String, categoryKey: String, modifier: Modifier = Modifier) {
    var input by remember { mutableStateOf(if (id == "Lorem") "50" else "") }
    var output by remember { mutableStateOf("") }
    var isError by remember { mutableStateOf(false) }
    
    val coroutineScope = rememberCoroutineScope()
    
    val runTool = { text: String ->
        coroutineScope.launch {
            val res = withContext(Dispatchers.IO) {
                Core.run(id, text)
            }
            output = res.text
            isError = !res.ok
        }
    }

    // Uuid and RandomColor generate; they were asking for input they never read.
    val noInput = categoryKey == "Random" || id in listOf(
        "SysInfo", "Coin", "Dice", "Password", "Uuid", "RandomColor",
    )
    // Tools whose input is a path get the system picker instead of a text box. Typing a path by
    // hand on a phone is not a real option — there is no shell to copy one from.
    // PathConvert transforms a path *string* and reads no file, so the picker was wrong for it.
    val fileInput = (categoryKey == "Files" || categoryKey == "Images") &&
        id !in listOf("PathConvert", "FilenameClean")
    val folderInput = id in listOf("FolderSize", "Tree", "Duplicates", "FilenameClean")

    // Only run on open when there is something to run. Firing with an empty string made every
    // validating tool greet the user with a red error before they had typed anything ("Unreadable
    // size: . Try 16px..."). Generators legitimately take no input, so they still run immediately.
    LaunchedEffect(id) {
        if (input.isNotBlank() || noInput) runTool(input)
    }

    // Hashing dropped: a hash takes arbitrary text, and a single-line field truncates the view of
    // anything longer than a few words. Those get the full text area instead.
    val smallInput = categoryKey in listOf("Maths", "Colors", "Conversions", "Dates") ||
        id in listOf("MimeType", "PortLookup", "PasswordStrength", "HttpStatus", "Roman", "Spell")

    Column(modifier = modifier.fillMaxSize()) {
        if (noInput) {
            // Generator layout
            Column(
                modifier = Modifier.fillMaxSize().padding(24.dp),
                verticalArrangement = Arrangement.Center,
                horizontalAlignment = Alignment.CenterHorizontally
            ) {
                Surface(
                    shape = RoundedCornerShape(32.dp),
                    color = if (isError) MaterialTheme.colorScheme.errorContainer else MaterialTheme.colorScheme.secondaryContainer,
                    modifier = Modifier.fillMaxWidth().weight(1f)
                ) {
                    Box(modifier = Modifier.fillMaxSize().padding(32.dp), contentAlignment = Alignment.Center) {
                        androidx.compose.foundation.text.selection.SelectionContainer {
                            Text(
                                text = output.ifEmpty { "Result" },
                                style = MaterialTheme.typography.displayMedium,
                                color = if (isError) MaterialTheme.colorScheme.onErrorContainer else MaterialTheme.colorScheme.onSecondaryContainer,
                                modifier = Modifier.verticalScroll(rememberScrollState()),
                                textAlign = TextAlign.Center
                            )
                        }
                        CopyResultButton(output, isError)
                    }
                }
                Spacer(modifier = Modifier.height(24.dp))
                androidx.compose.material3.Button(
                    onClick = { runTool("") },
                    modifier = Modifier.fillMaxWidth().height(64.dp),
                    shape = CircleShape
                ) {
                    Text("Generate", style = MaterialTheme.typography.titleLarge)
                }
            }
        } else if (fileInput) {
            Column(
                modifier = Modifier.fillMaxSize().padding(24.dp),
                verticalArrangement = Arrangement.spacedBy(20.dp)
            ) {
                FilePathField(
                    value = input,
                    onValueChange = { input = it },
                    label = if (folderInput) "Folder" else "File",
                    modifier = Modifier.fillMaxWidth(),
                    folder = folderInput,
                )
                androidx.compose.material3.Button(
                    onClick = { runTool(input) },
                    enabled = input.isNotBlank(),
                    modifier = Modifier.fillMaxWidth().height(56.dp),
                    shape = CircleShape
                ) {
                    Text("Run", style = MaterialTheme.typography.titleMedium)
                }
                Surface(
                    shape = RoundedCornerShape(28.dp),
                    color = if (isError) MaterialTheme.colorScheme.errorContainer else MaterialTheme.colorScheme.surfaceContainerHigh,
                    modifier = Modifier.fillMaxWidth().weight(1f)
                ) {
                    Box(modifier = Modifier.fillMaxSize().padding(20.dp)) {
                        androidx.compose.foundation.text.selection.SelectionContainer {
                            Text(
                                text = output.ifEmpty { "Pick a file to begin" },
                                style = MaterialTheme.typography.bodyLarge,
                                color = if (isError) MaterialTheme.colorScheme.onErrorContainer else MaterialTheme.colorScheme.onSurface,
                                modifier = Modifier.verticalScroll(rememberScrollState())
                            )
                        }
                        CopyResultButton(output, isError)
                    }
                }
            }
        } else if (smallInput) {
            // Calculator/Converter layout
            Column(
                modifier = Modifier.fillMaxSize().padding(24.dp),
                verticalArrangement = Arrangement.spacedBy(24.dp)
            ) {
                androidx.compose.material3.OutlinedTextField(
                    value = input,
                    onValueChange = { newValue ->
                        input = newValue
                        runTool(newValue)
                    },
                    placeholder = { Text(toolHint(id)) },
                    textStyle = MaterialTheme.typography.headlineMedium.copy(textAlign = TextAlign.Center),
                    modifier = Modifier.fillMaxWidth(),
                    shape = CircleShape,
                    singleLine = true,
                    colors = TextFieldDefaults.colors(
                        focusedContainerColor = MaterialTheme.colorScheme.surfaceVariant,
                        unfocusedContainerColor = MaterialTheme.colorScheme.surfaceVariant,
                        focusedIndicatorColor = androidx.compose.ui.graphics.Color.Transparent,
                        unfocusedIndicatorColor = androidx.compose.ui.graphics.Color.Transparent
                    )
                )

                Surface(
                    shape = RoundedCornerShape(32.dp),
                    color = if (isError) MaterialTheme.colorScheme.errorContainer else MaterialTheme.colorScheme.secondaryContainer,
                    modifier = Modifier.fillMaxWidth().weight(1f)
                ) {
                    Box(modifier = Modifier.fillMaxSize().padding(32.dp), contentAlignment = Alignment.Center) {
                        androidx.compose.foundation.text.selection.SelectionContainer {
                            Text(
                                text = output.ifEmpty { "Result" },
                                style = MaterialTheme.typography.headlineLarge,
                                color = if (isError) MaterialTheme.colorScheme.onErrorContainer else MaterialTheme.colorScheme.onSecondaryContainer,
                                modifier = Modifier.verticalScroll(rememberScrollState()),
                                textAlign = TextAlign.Center
                            )
                        }
                        CopyResultButton(output, isError)
                    }
                }
            }
        } else {
            // Premium Card-based Split Layout
            Column(
                modifier = Modifier.fillMaxSize().padding(16.dp),
                verticalArrangement = Arrangement.spacedBy(16.dp)
            ) {
                // Input Card
                Surface(
                    shape = RoundedCornerShape(32.dp),
                    color = MaterialTheme.colorScheme.surfaceContainerHigh,
                    modifier = Modifier.fillMaxWidth().weight(1f)
                ) {
                    Box(modifier = Modifier.fillMaxSize()) {
                        TextField(
                            value = input,
                            onValueChange = { newValue ->
                                input = newValue
                                runTool(newValue)
                            },
                            placeholder = { 
                                Text(
                                    toolHint(id), 
                                    style = MaterialTheme.typography.titleLarge,
                                    color = MaterialTheme.colorScheme.onSurfaceVariant.copy(alpha = 0.7f)
                                ) 
                            },
                            textStyle = MaterialTheme.typography.titleLarge,
                            modifier = Modifier.fillMaxSize().padding(16.dp),
                            colors = TextFieldDefaults.colors(
                                focusedContainerColor = androidx.compose.ui.graphics.Color.Transparent,
                                unfocusedContainerColor = androidx.compose.ui.graphics.Color.Transparent,
                                focusedIndicatorColor = androidx.compose.ui.graphics.Color.Transparent,
                                unfocusedIndicatorColor = androidx.compose.ui.graphics.Color.Transparent,
                                cursorColor = MaterialTheme.colorScheme.primary
                            )
                        )
                        if (input.isNotEmpty()) {
                            IconButton(
                                onClick = { 
                                    input = ""
                                    runTool("")
                                },
                                modifier = Modifier.align(Alignment.TopEnd).padding(16.dp)
                            ) {
                                Icon(
                                    androidx.compose.material.icons.Icons.Rounded.Clear,
                                    contentDescription = "Clear text",
                                    tint = MaterialTheme.colorScheme.onSurfaceVariant
                                )
                            }
                        }
                    }
                }

                // Output Card
                Surface(
                    shape = RoundedCornerShape(32.dp),
                    color = if (isError) MaterialTheme.colorScheme.errorContainer else MaterialTheme.colorScheme.secondaryContainer,
                    modifier = Modifier.fillMaxWidth().weight(1f)
                ) {
                    Box(modifier = Modifier.fillMaxSize().padding(32.dp)) {
                        androidx.compose.foundation.text.selection.SelectionContainer {
                            Text(
                                text = output.ifEmpty { "Result" },
                                style = MaterialTheme.typography.titleLarge,
                                color = if (isError) MaterialTheme.colorScheme.onErrorContainer else MaterialTheme.colorScheme.onSecondaryContainer,
                                modifier = Modifier.verticalScroll(rememberScrollState())
                            )
                        }
                        CopyResultButton(output, isError)
                    }
                }
            }
        }
    }
}
