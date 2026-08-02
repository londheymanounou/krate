//! Localized text. Mirrors `Krate.Core.Strings`: a missing key returns the key itself, so a gap
//! is visible in the UI but never throws and never renders empty.

include!(concat!(env!("OUT_DIR"), "/strings.rs"));

use std::sync::RwLock;

static CULTURE: RwLock<String> = RwLock::new(String::new());

/// Sets the interface language. Unknown languages fall back to English at lookup time, so
/// callers never have to validate first.
///
/// The tag is **normalized to one this build actually ships** before being stored, because
/// `table` matches exactly. Callers hand over whatever the platform gives them, and real platforms
/// give region-qualified tags: Android's `Locale.toLanguageTag()` returns `ja-JP`, `fr-FR`,
/// `de-DE`. Stored verbatim those match nothing and every one of them silently rendered English —
/// only `pt-BR`, `zh-CN` and `zh-TW` worked, since those are the three shipped *with* a region.
pub fn set_language(language: &str) {
    *CULTURE.write().unwrap() = normalize(language);
}

/// Resolves a BCP-47 tag to a shipped language, most specific match first.
///
/// Script subtags are skipped rather than parsed: `zh-Hans-CN` has to reach `zh-CN`, and comparing
/// primary subtag plus region gets there without teaching this a full tag grammar.
fn normalize(tag: &str) -> String {
    if tag.is_empty() {
        return String::new();
    }
    let lower = tag.to_ascii_lowercase();
    let eq = |a: &str, b: &str| a.eq_ignore_ascii_case(b);

    if let Some(exact) = LANGUAGES.iter().find(|l| eq(l, &lower)) {
        return (*exact).to_string();
    }

    let parts: Vec<&str> = lower.split(['-', '_']).collect();
    let primary = parts.first().copied().unwrap_or_default();
    // Last subtag that looks like a region (2 letters or 3 digits), skipping any script.
    let region = parts[1..]
        .iter()
        .rev()
        .find(|p| {
            (p.len() == 2 && p.chars().all(|c| c.is_ascii_alphabetic()))
                || (p.len() == 3 && p.chars().all(|c| c.is_ascii_digit()))
        })
        .copied();

    if let Some(region) = region {
        let want = format!("{primary}-{region}");
        if let Some(hit) = LANGUAGES.iter().find(|l| eq(l, &want)) {
            return (*hit).to_string();
        }
    }
    if let Some(hit) = LANGUAGES.iter().find(|l| eq(l, primary)) {
        return (*hit).to_string();
    }
    // Same language, different region than any we ship (pt-PT -> pt-BR) beats falling to English.
    if let Some(hit) = LANGUAGES
        .iter()
        .find(|l| l.split('-').next().is_some_and(|p| eq(p, primary)))
    {
        return (*hit).to_string();
    }
    tag.to_string()
}

pub fn language() -> String {
    let current = CULTURE.read().unwrap();
    if current.is_empty() { "en".to_string() } else { current.clone() }
}

/// Looks a key up in the current language, falling back to English, then to the key itself.
///
/// The C# side echoes the key back for a miss and the GUI/CLI rely on that being non-empty —
/// keep the behaviour identical while both implementations are live.
pub fn get(key: &str) -> &'static str {
    let language = language();
    lookup(table(&language), key)
        .or_else(|| lookup(table("en"), key))
        .unwrap_or_else(|| leak(key))
}

/// Substitutes `{0}`, `{1}`… the way .NET's `string.Format` does, so the same resx values work
/// unchanged from both implementations.
pub fn format(key: &str, args: &[&str]) -> String {
    let mut text = get(key).to_string();
    for (index, value) in args.iter().enumerate() {
        text = text.replace(&format!("{{{index}}}"), value);
    }
    text
}

/// Serialises tests that change the interface language.
///
/// The language is process-global (it has to be — the FFI sets it once for the whole shell),
/// and `cargo test` runs tests in parallel, so without this one test can swap the language out
/// from under another mid-assertion. That produced a failure that vanished when the test was
/// run on its own, which is the worst kind.
#[cfg(test)]
pub(crate) fn test_lock() -> std::sync::MutexGuard<'static, ()> {
    static LOCK: std::sync::Mutex<()> = std::sync::Mutex::new(());
    // A panic in one test poisons the mutex; the language is just a string, so recover rather
    // than cascade the failure into every other test.
    LOCK.lock().unwrap_or_else(|poisoned| poisoned.into_inner())
}

fn lookup(table: &'static [(&'static str, &'static str)], key: &str) -> Option<&'static str> {
    table
        .binary_search_by_key(&key, |(k, _)| k)
        .ok()
        .map(|i| table[i].1)
}

/// A missing key is a bug we want visible, and the signature promises `'static`. Misses are
/// bounded by the number of distinct keys in the source, so this cannot grow without bound.
fn leak(key: &str) -> &'static str {
    Box::leak(key.to_string().into_boxed_str())
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn every_shipped_language_is_present() {
        assert_eq!(LANGUAGES.len(), 17, "expected 17 languages, got {:?}", LANGUAGES);
        assert!(LANGUAGES.contains(&"en"));
        assert!(LANGUAGES.contains(&"fr"));
        assert!(LANGUAGES.contains(&"zh-TW"));
    }

    #[test]
    fn languages_are_translated_not_copied() {
        let _guard = crate::i18n::test_lock();
        set_language("fr");
        assert_eq!(get("Error_NoFile"), "Fichier introuvable : {0}");
        set_language("ru");
        assert_eq!(get("Error_NoFile"), "Файл не найден: {0}");
        set_language("ja");
        assert_eq!(get("Tool_Zip_Name"), "圧縮");
        set_language("en");
        assert_eq!(get("Error_NoFile"), "File not found: {0}");
    }

    #[test]
    fn an_unknown_language_falls_back_to_english() {
        let _guard = crate::i18n::test_lock();
        set_language("xx");
        assert_eq!(get("Error_NoFile"), "File not found: {0}");
        set_language("en");
    }

    #[test]
    fn a_missing_key_echoes_itself_like_the_csharp_side() {
        let _guard = crate::i18n::test_lock();
        set_language("en");
        assert_eq!(get("No_Such_Key"), "No_Such_Key");
    }

    #[test]
    fn format_substitutes_dotnet_style_placeholders() {
        let _guard = crate::i18n::test_lock();
        set_language("en");
        assert_eq!(format("Error_NoFile", &["C:\\x.txt"]), "File not found: C:\\x.txt");
    }

    /// The .resx carry XML-escaped text; if the build script failed to decode it the UI would
    /// show "&amp;" and "&lt;" literally.
    #[test]
    fn xml_entities_were_decoded() {
        let _guard = crate::i18n::test_lock();
        set_language("en");
        for language in LANGUAGES {
            for (key, value) in table(language) {
                assert!(
                    !value.contains("&lt;") && !value.contains("&amp;") && !value.contains("&quot;"),
                    "{language}/{key} still holds an XML entity: {value}"
                );
            }
        }
    }

    /// Binary search is only correct on sorted input.
    #[test]
    fn every_table_is_sorted_by_key() {
        for language in LANGUAGES {
            let entries = table(language);
            assert!(
                entries.windows(2).all(|w| w[0].0 < w[1].0),
                "{language} table is not sorted"
            );
        }
    }
}

#[cfg(test)]
mod normalize_tests {
    use super::*;

    #[test]
    fn resolves_platform_tags_to_shipped_languages() {
        // The Android case that rendered English for every region-qualified locale.
        assert_eq!(normalize("ja-JP"), "ja");
        assert_eq!(normalize("fr-FR"), "fr");
        assert_eq!(normalize("de-DE"), "de");
        // Shipped with a region: must stay exact, not collapse to a bare primary subtag.
        assert_eq!(normalize("pt-BR"), "pt-BR");
        assert_eq!(normalize("zh-CN"), "zh-CN");
        assert_eq!(normalize("zh-TW"), "zh-TW");
        // Script subtag skipped so the region still decides.
        assert_eq!(normalize("zh-Hans-CN"), "zh-CN");
        assert_eq!(normalize("zh-Hant-TW"), "zh-TW");
        // Case and separator variations platforms actually emit.
        assert_eq!(normalize("JA-jp"), "ja");
        assert_eq!(normalize("fr_FR"), "fr");
        // Region we do not ship falls back to the same language, not to English.
        assert_eq!(normalize("pt-PT"), "pt-BR");
        // Unknown language is left alone; lookup falls back to English.
        assert_eq!(normalize("xx-YY"), "xx-YY");
        assert_eq!(normalize(""), "");
    }
}
