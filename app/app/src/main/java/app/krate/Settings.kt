package app.krate

import android.content.Context
import android.view.HapticFeedbackConstants
import android.view.View
import androidx.compose.runtime.compositionLocalOf

/**
 * User preferences: interface language and whether haptics fire.
 *
 * ponytail: SharedPreferences again, two keys. Nothing here needs a store or a flow.
 */
object Settings {
    private const val PREFS = "krate"
    private const val LANG = "language"
    private const val HAPTICS = "haptics"

    /** Empty means "follow the system locale", which is the default and the right one. */
    fun language(context: Context): String =
        context.getSharedPreferences(PREFS, Context.MODE_PRIVATE).getString(LANG, "").orEmpty()

    fun setLanguage(context: Context, tag: String) {
        context.getSharedPreferences(PREFS, Context.MODE_PRIVATE).edit().putString(LANG, tag).apply()
    }

    fun haptics(context: Context): Boolean =
        context.getSharedPreferences(PREFS, Context.MODE_PRIVATE).getBoolean(HAPTICS, true)

    fun setHaptics(context: Context, on: Boolean) {
        context.getSharedPreferences(PREFS, Context.MODE_PRIVATE).edit().putBoolean(HAPTICS, on).apply()
    }
}

/**
 * Whether haptics are on, read once per composition tree rather than from prefs at every keypress.
 */
val LocalHaptics = compositionLocalOf { true }

/**
 * A keypress tick, if haptics are enabled.
 *
 * Deliberately **not** wired to every tappable thing. Vibration on ordinary navigation — opening a
 * tool, tapping a list row — is noise that the system already handles, and constant motor use is a
 * real battery cost. It belongs on repeated, eyes-down input: calculator and converter keys, and
 * committing a random draw. Those are the places a finger wants confirmation without looking.
 */
fun View.tick(enabled: Boolean) {
    if (enabled) performHapticFeedback(HapticFeedbackConstants.KEYBOARD_TAP)
}

/** The 17 languages the core ships, plus "follow the system". */
val LANGUAGES: List<Pair<String, String>> = listOf(
    "" to "System default",
    "en" to "English",
    "fr" to "Français",
    "es" to "Español",
    "de" to "Deutsch",
    "it" to "Italiano",
    "nl" to "Nederlands",
    "pl" to "Polski",
    "pt-BR" to "Português (Brasil)",
    "ru" to "Русский",
    "tr" to "Türkçe",
    "vi" to "Tiếng Việt",
    "id" to "Bahasa Indonesia",
    "hi" to "हिन्दी",
    "ja" to "日本語",
    "ko" to "한국어",
    "zh-CN" to "简体中文",
    "zh-TW" to "繁體中文",
)
