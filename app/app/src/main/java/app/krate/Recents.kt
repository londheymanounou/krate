package app.krate

import android.content.Context

/**
 * What the user has actually used: recency order and a per-tool run count.
 *
 * ponytail: SharedPreferences, not Room. Recents is at most [MAX] short ids rewritten whole, and
 * the counts are one small map — a database would be more schema than data. Revisit if this ever
 * needs querying by date. Everything is keyed on the tool **id**, the catalogue's stable
 * unlocalized key, so it survives a language change.
 */
object Recents {
    private const val PREFS = "krate"
    private const val KEY = "recent_tools"
    private const val COUNTS = "tool_counts"
    private const val MAX = 6

    private fun prefs(context: Context) =
        context.getSharedPreferences(PREFS, Context.MODE_PRIVATE)

    fun get(context: Context): List<String> =
        prefs(context).getString(KEY, "").orEmpty()
            .split(',').filter { it.isNotBlank() }

    /** Tool id -> times opened, unordered. */
    fun counts(context: Context): Map<String, Int> =
        prefs(context).getString(COUNTS, "").orEmpty()
            .split(',')
            .mapNotNull { entry ->
                // "id:n" — a malformed entry is dropped rather than crashing the stats screen.
                val i = entry.lastIndexOf(':')
                if (i <= 0) return@mapNotNull null
                val n = entry.substring(i + 1).toIntOrNull() ?: return@mapNotNull null
                entry.substring(0, i) to n
            }
            .toMap()

    fun totalRuns(context: Context): Int = counts(context).values.sum()

    fun record(context: Context, id: String) {
        // Re-opening a tool moves it to the front rather than duplicating it.
        val next = (listOf(id) + get(context).filter { it != id }).take(MAX)
        val tally = counts(context).toMutableMap()
        tally[id] = (tally[id] ?: 0) + 1
        prefs(context).edit()
            .putString(KEY, next.joinToString(","))
            .putString(COUNTS, tally.entries.joinToString(",") { "${it.key}:${it.value}" })
            .apply()
    }

    fun clear(context: Context) {
        prefs(context).edit().remove(KEY).remove(COUNTS).apply()
    }
}
