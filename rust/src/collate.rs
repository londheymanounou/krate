//! Culture-aware line sorting. Mirrors `Text.SortLines`.
//!
//! `StringComparer.Create(culture, ignoreCase: false)` is ICU collation with the culture's own
//! tailoring — .NET has used ICU for globalization since .NET 5. So this uses ICU4X, the same
//! algorithm and the same CLDR data, at default (tertiary) strength.
//!
//! It is the most expensive dependency in the port: ~1.4 MB of collation data and 31 transitive
//! crates for one tool. Justified only because nothing cheaper can be correct — a codepoint sort
//! puts "Z" before "a" and files "éclair" after "zebra", which is wrong in every language.

use crate::i18n;
use icu_collator::{options::CollatorOptions, Collator, CollatorBorrowed, CollatorPreferences};

/// The collator for the active language, or the root locale if the tag is not one ICU knows.
fn collator() -> CollatorBorrowed<'static> {
    let tag = i18n::language();
    let preferences: CollatorPreferences = tag
        .parse::<icu_locale_core::Locale>()
        .map(CollatorPreferences::from)
        .unwrap_or_default();
    Collator::try_new(preferences, CollatorOptions::default())
        .unwrap_or_else(|_| {
            Collator::try_new(CollatorPreferences::default(), CollatorOptions::default())
                .expect("the root collator is always available")
        })
}

/// Sorts lines with the active culture's collation.
///
/// `OrderBy` is a **stable** sort, so lines that compare equal keep their original order. That
/// matters for input like "a" and "A" under a case-insensitive strength, and for exact duplicates.
pub fn sort_lines(input: &str) -> String {
    let normalized = input.replace("\r\n", "\n");
    let mut lines: Vec<&str> = normalized.split('\n').collect();
    let collator = collator();
    lines.sort_by(|a, b| collator.compare(a, b));
    lines.join("\n")
}

#[cfg(test)]
mod tests {
    use super::*;

    fn sorted(input: &str) -> Vec<String> {
        sort_lines(input).lines().map(str::to_string).collect()
    }

    #[test]
    fn letters_sort_before_case_distinctions() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        // Collation groups a with A and orders them by case only as a tiebreak.
        assert_eq!(sorted("b\nA\na\nB"), ["a", "A", "b", "B"]);
    }

    /// The point of using collation at all: an accent is a tiebreak, not a different letter.
    #[test]
    fn accents_sort_next_to_their_base_letter() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        let out = sorted("zebra\n\u{e9}clair\napple\ncafe\ncaf\u{e9}");
        assert_eq!(out, ["apple", "cafe", "caf\u{e9}", "\u{e9}clair", "zebra"], "{out:?}");
        // A codepoint sort would have put éclair last, after zebra.
    }

    #[test]
    fn digits_and_punctuation_come_before_letters() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        let out = sorted("apple\n1one\n_under");
        assert_eq!(out, ["_under", "1one", "apple"], "{out:?}");
    }

    #[test]
    fn equal_lines_keep_their_order() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        // A stable sort, as OrderBy is: identical lines cannot be reordered observably, but the
        // count must survive.
        assert_eq!(sorted("b\na\nb\na").len(), 4);
        assert_eq!(sorted("x\nx\nx"), ["x", "x", "x"]);
    }

    #[test]
    fn empty_and_blank_input_is_survivable() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        assert_eq!(sort_lines(""), "");
        // Blank lines sort first and are kept, since Split does not remove them.
        assert_eq!(sorted("b\n\na").len(), 3);
        assert_eq!(sort_lines("only"), "only");
    }

    #[test]
    fn crlf_input_is_normalised() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        assert_eq!(sort_lines("b\r\na"), "a\nb");
    }

    /// Swedish files å, ä and ö after z; German does not. If the culture were ignored these would
    /// come out the same.
    #[test]
    fn the_culture_changes_the_order() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("de");
        let german = sorted("z\n\u{e4}\na");
        i18n::set_language("en");
        // German treats ä as a variant of a, so it sorts before z.
        assert_eq!(german, ["a", "\u{e4}", "z"], "{german:?}");
    }
}
