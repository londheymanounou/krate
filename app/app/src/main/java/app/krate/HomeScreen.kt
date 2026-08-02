@file:OptIn(
    androidx.compose.material3.ExperimentalMaterial3Api::class,
    androidx.compose.material3.ExperimentalMaterial3ExpressiveApi::class,
)

package app.krate

import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.rememberLazyListState
import androidx.compose.foundation.lazy.itemsIndexed
import androidx.compose.foundation.lazy.LazyRow
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.lazy.grid.GridCells
import androidx.compose.foundation.lazy.grid.GridItemSpan
import androidx.compose.foundation.lazy.grid.LazyVerticalGrid
import androidx.compose.foundation.lazy.grid.items
import androidx.compose.foundation.lazy.grid.itemsIndexed
import androidx.compose.animation.core.animateFloatAsState
import androidx.compose.animation.core.animateDpAsState
import androidx.compose.animation.core.spring
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.interaction.collectIsPressedAsState
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.rounded.ArrowBack
import androidx.compose.material.icons.rounded.Clear
import androidx.compose.material.icons.rounded.MoreVert
import androidx.compose.material.icons.rounded.QueryStats
import androidx.compose.material.icons.rounded.Info
import androidx.compose.material.icons.rounded.Search
import androidx.compose.material.icons.rounded.Settings
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp

/**
 * Thumb-first home.
 *
 * The classic list ([KrateApp]'s original body, still available) puts a 140-row flat scroll under a
 * search box pinned to the very top — the two things you reach for most are the two furthest from
 * your thumb on a modern phone. This inverts that: categories are a reachable grid, and the search
 * field is docked to the *bottom*, inside the navigation-bar inset.
 *
 * Twelve categories also means the first screen is a whole map of the app rather than the first 6%
 * of an alphabetical list.
 */
@Composable
fun HomeScreen(
    tools: List<ToolInfo>,
    onOpenTool: (String) -> Unit,
    onOpenStats: () -> Unit,
    onOpenSettings: () -> Unit,
    modifier: Modifier = Modifier,
) {
    var menuOpen by remember { mutableStateOf(false) }
    var aboutOpen by remember { mutableStateOf(false) }
    var query by rememberSaveable { mutableStateOf("") }
    val listState = rememberLazyListState()
    var openCategory by rememberSaveable { mutableStateOf<String?>(null) }

    val searching = query.isNotBlank()
    val results = remember(tools, query) {
        if (query.isBlank()) {
            emptyList()
        } else {
            val q = query.trim()
            // Unranked `contains` buried "Unit converter" under nine other "...converter" tools when
            // searching "convert". Rank on how well the *name* matches before id/category hits, and
            // prefer the shortest name at equal score so "Unit converter" beats "Shoe size converter".
            fun score(t: ToolInfo): Int = when {
                t.name.equals(q, true) -> 0
                t.name.startsWith(q, true) -> 1
                t.name.split(' ').any { w -> w.startsWith(q, true) } -> 2
                t.name.contains(q, true) -> 3
                t.id.startsWith(q, true) -> 4
                t.id.contains(q, true) -> 5
                t.category.contains(q, true) -> 6
                else -> Int.MAX_VALUE
            }
            tools.map { it to score(it) }
                .filter { it.second != Int.MAX_VALUE }
                .sortedWith(compareBy({ it.second }, { it.first.name.length }, { it.first.name }))
                .map { it.first }
        }
    }

    // Category order follows the catalogue, so it matches the desktop and never reshuffles.
    val categories = remember(tools) {
        tools.map { it.categoryKey to it.category }.distinct()
    }
    val counts = remember(tools) { tools.groupingBy { it.categoryKey }.eachCount() }

    val context = LocalContext.current
    // Read once per composition, not on every recomposition: this is a SharedPreferences hit plus
    // a split and a 137-item lookup, and it was running on every keystroke typed into search.
    val recentIds = remember { Recents.get(context) }
    val byId = remember(tools) { tools.associateBy { it.id } }
    val recent = remember(recentIds, byId) { recentIds.mapNotNull { byId[it] } }

    // A LazyColumn keeps its scroll offset when its contents change, so after scrolling one set of
    // results the next query rendered mid-list — the best match was ranked first and invisible.
    // Back should unwind the screen's own state before exiting the app: an open category first,
    // then a live search. Without these the gesture quit the app from a filtered list, which reads
    // as the app closing at random.
    androidx.activity.compose.BackHandler(enabled = openCategory != null) { openCategory = null }
    androidx.activity.compose.BackHandler(enabled = openCategory == null && query.isNotBlank()) {
        query = ""
    }

    // NOTE: an automatic scroll-to-top on new results is DELIBERATELY absent. Two attempts made
    // things worse: keyed on the query it fires before the new list composes (LazyColumn then
    // restores an offset and hides the top hit), and keyed on the results it calls scrollToItem on
    // a LazyListState that is unattached whenever the category grid is showing — which suspends
    // instead of throwing and leaves the list blank. Fix properly before re-adding.

    Scaffold(
        modifier = modifier.imePadding(),
        containerColor = MaterialTheme.colorScheme.surface,
        topBar = {
            TopAppBar(
                title = {
                    Text(
                        openCategory?.let { key -> tools.first { it.categoryKey == key }.category }
                            ?: "Krate",
                        fontWeight = FontWeight.Medium,
                    )
                },
                navigationIcon = {
                    if (openCategory != null) {
                        IconButton(onClick = { openCategory = null }) {
                            Icon(Icons.Rounded.ArrowBack, "Back")
                        }
                    }
                },
                actions = {
                    IconButton(onClick = { menuOpen = true }) {
                        Icon(Icons.Rounded.MoreVert, "Menu")
                    }
                    DropdownMenu(expanded = menuOpen, onDismissRequest = { menuOpen = false }) {
                        DropdownMenuItem(
                            text = { Text("Usage statistics") },
                            leadingIcon = { Icon(Icons.Rounded.QueryStats, null) },
                            onClick = { menuOpen = false; onOpenStats() },
                        )
                        DropdownMenuItem(
                            text = { Text("Settings") },
                            leadingIcon = { Icon(Icons.Rounded.Settings, null) },
                            onClick = { menuOpen = false; onOpenSettings() },
                        )
                        DropdownMenuItem(
                            text = { Text("About") },
                            leadingIcon = { Icon(Icons.Rounded.Info, null) },
                            onClick = { menuOpen = false; aboutOpen = true },
                        )
                    }
                },
                colors = TopAppBarDefaults.topAppBarColors(
                    containerColor = MaterialTheme.colorScheme.surface,
                ),
            )
        },
        bottomBar = {
            // Docked at the bottom and inset-aware: this is the control the user touches most.
            Surface(color = MaterialTheme.colorScheme.surface) {
                OutlinedTextField(
                    value = query,
                    onValueChange = { query = it },
                    modifier = Modifier
                        .fillMaxWidth()
                        .windowInsetsPadding(WindowInsets.navigationBars)
                        .padding(horizontal = 16.dp, vertical = 8.dp),
                    placeholder = { Text("Search ${tools.size} tools") },
                    leadingIcon = { Icon(Icons.Rounded.Search, null) },
                    trailingIcon = {
                        if (searching) {
                            IconButton(onClick = { query = "" }) { Icon(Icons.Rounded.Clear, "Clear") }
                        }
                    },
                    singleLine = true,
                    shape = CircleShape,
                    colors = OutlinedTextFieldDefaults.colors(
                        focusedBorderColor = MaterialTheme.colorScheme.primary,
                        unfocusedBorderColor = MaterialTheme.colorScheme.outlineVariant,
                    ),
                )
            }
        },
    ) { pad ->
        if (aboutOpen) AboutDialog(tools.size) { aboutOpen = false }
        val shown = when {
            searching -> results
            openCategory != null -> tools.filter { it.categoryKey == openCategory }
            else -> emptyList()
        }

        if (shown.isEmpty() && !searching && openCategory == null) {
            LazyVerticalGrid(
                columns = GridCells.Fixed(2),
                modifier = Modifier.fillMaxSize().padding(pad),
                contentPadding = PaddingValues(16.dp),
                horizontalArrangement = Arrangement.spacedBy(12.dp),
                verticalArrangement = Arrangement.spacedBy(12.dp),
            ) {
                if (recent.isNotEmpty()) {
                    item(span = { GridItemSpan(2) }) {
                        Text(
                            "Recent",
                            style = MaterialTheme.typography.titleSmall,
                            fontWeight = FontWeight.Bold,
                            color = MaterialTheme.colorScheme.primary,
                            modifier = Modifier.padding(bottom = 4.dp),
                        )
                    }
                    item(span = { GridItemSpan(2) }) {
                        LazyRow(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                            items(recent, key = { it.id }) { tool ->
                                Surface(
                                    shape = CircleShape,
                                    color = MaterialTheme.colorScheme.secondaryContainer,
                                    modifier = Modifier.clickable { onOpenTool(tool.id) },
                                ) {
                                    Row(
                                        Modifier.padding(horizontal = 16.dp, vertical = 10.dp),
                                        verticalAlignment = Alignment.CenterVertically,
                                    ) {
                                        Icon(
                                            toolIcon(tool.id, tool.categoryKey),
                                            null,
                                            Modifier.size(18.dp),
                                            tint = MaterialTheme.colorScheme.onSecondaryContainer,
                                        )
                                        Spacer(Modifier.width(8.dp))
                                        Text(
                                            tool.name,
                                            style = MaterialTheme.typography.labelLarge,
                                            color = MaterialTheme.colorScheme.onSecondaryContainer,
                                        )
                                    }
                                }
                            }
                        }
                    }
                    item(span = { GridItemSpan(2) }) {
                        Text(
                            "Categories",
                            style = MaterialTheme.typography.titleSmall,
                            fontWeight = FontWeight.Bold,
                            color = MaterialTheme.colorScheme.primary,
                            modifier = Modifier.padding(top = 8.dp, bottom = 4.dp),
                        )
                    }
                }
                itemsIndexed(categories, key = { _, c -> c.first }) { i, (key, label) ->
                    CategoryCard(key, label, counts[key] ?: 0, i) { openCategory = key }
                }
            }
        } else {
            LazyColumn(
                state = listState,
                modifier = Modifier.fillMaxSize().padding(pad),
                contentPadding = PaddingValues(16.dp),
                verticalArrangement = Arrangement.spacedBy(2.dp),
            ) {
                if (shown.isEmpty()) {
                    item {
                        Box(Modifier.fillMaxWidth().padding(48.dp), contentAlignment = Alignment.Center) {
                            Text(
                                "No tools match \"$query\"",
                                style = MaterialTheme.typography.bodyLarge,
                                color = MaterialTheme.colorScheme.onSurfaceVariant,
                            )
                        }
                    }
                }
                itemsIndexed(shown, key = { _, t -> t.id }) { i, tool ->
                    val big = 24.dp
                    val small = 4.dp
                    Surface(
                        modifier = Modifier
                            .fillMaxWidth()
                            .clip(
                                RoundedCornerShape(
                                    topStart = if (i == 0) big else small,
                                    topEnd = if (i == 0) big else small,
                                    bottomStart = if (i == shown.lastIndex) big else small,
                                    bottomEnd = if (i == shown.lastIndex) big else small,
                                )
                            )
                            .clickable { onOpenTool(tool.id) },
                        color = MaterialTheme.colorScheme.surfaceContainerLow,
                    ) {
                        ListItem(
                            headlineContent = { Text(tool.name, style = MaterialTheme.typography.titleMedium) },
                            supportingContent = if (searching) {
                                { Text(tool.category, style = MaterialTheme.typography.bodySmall) }
                            } else null,
                            leadingContent = {
                                Box(
                                    Modifier.size(40.dp).clip(CircleShape)
                                        .background(MaterialTheme.colorScheme.secondaryContainer),
                                    contentAlignment = Alignment.Center,
                                ) {
                                    Icon(
                                        toolIcon(tool.id, tool.categoryKey),
                                        null,
                                        Modifier.size(22.dp),
                                        tint = MaterialTheme.colorScheme.onSecondaryContainer,
                                    )
                                }
                            },
                            colors = ListItemDefaults.colors(
                                containerColor = androidx.compose.ui.graphics.Color.Transparent
                            ),
                        )
                    }
                }
            }
        }
    }
}

/**
 * Colour roles cycled across the grid. Twelve identical surface-coloured cards is the definition of
 * boring; Material You gives four container roles that are all guaranteed to be harmonious with the
 * wallpaper, so using one is leaving the palette on the table.
 */
private val CARD_ROLES = 4

@Composable
private fun CategoryCard(
    key: String,
    label: String,
    count: Int,
    role: Int,
    onClick: () -> Unit,
) {
    val container = when (role % CARD_ROLES) {
        0 -> MaterialTheme.colorScheme.primaryContainer
        1 -> MaterialTheme.colorScheme.tertiaryContainer
        2 -> MaterialTheme.colorScheme.secondaryContainer
        else -> MaterialTheme.colorScheme.surfaceContainerHighest
    }
    val onContainer = when (role % CARD_ROLES) {
        0 -> MaterialTheme.colorScheme.onPrimaryContainer
        1 -> MaterialTheme.colorScheme.onTertiaryContainer
        2 -> MaterialTheme.colorScheme.onSecondaryContainer
        else -> MaterialTheme.colorScheme.onSurface
    }

    val interaction = remember { MutableInteractionSource() }
    val pressed by interaction.collectIsPressedAsState()
    // Expressive motion: a spring that overshoots, not a linear fade. This is the difference
    // between Material You and Material You *Expressive*.
    val scale by animateFloatAsState(
        targetValue = if (pressed) 0.94f else 1f,
        animationSpec = spring(dampingRatio = 0.45f, stiffness = 900f),
        label = "press",
    )
    val corner by animateDpAsState(
        targetValue = if (pressed) 34.dp else 24.dp,
        animationSpec = spring(dampingRatio = 0.5f, stiffness = 700f),
        label = "corner",
    )

    Surface(
        shape = RoundedCornerShape(corner),
        color = container,
        contentColor = onContainer,
        modifier = Modifier
            .height(120.dp)
            .graphicsLayer { scaleX = scale; scaleY = scale }
            .clickable(interactionSource = interaction, indication = null, onClick = onClick),
    ) {
        Column(
            Modifier.fillMaxSize().padding(16.dp),
            verticalArrangement = Arrangement.SpaceBetween,
        ) {
            Box(
                Modifier.size(40.dp).clip(CircleShape)
                    .background(onContainer.copy(alpha = 0.14f)),
                contentAlignment = Alignment.Center,
            ) {
                Icon(categoryIcon(key), null, Modifier.size(22.dp), tint = onContainer)
            }
            Column {
                Text(label, style = MaterialTheme.typography.titleMedium, fontWeight = FontWeight.Medium)
                Text(
                    "$count",
                    style = MaterialTheme.typography.bodySmall,
                    color = onContainer.copy(alpha = 0.7f),
                )
            }
        }
    }
}

@Composable
private fun AboutDialog(toolCount: Int, onDismiss: () -> Unit) {
    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text("KRATE") },
        text = {
            Text(
                "$toolCount tools running in a native Rust core shared with the desktop app.",
            )
        },
        confirmButton = { TextButton(onClick = onDismiss) { Text("Close") } },
    )
}
