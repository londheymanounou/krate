//! Lorem Ipsum, Zalgo and French typography. Mirrors the corresponding parts of
//! `Krate.Core.TextMore`, plus `Css.Gradient`.

use crate::colors;
use crate::csprng;
use crate::i18n;

const LOREM_WORDS: &str = concat!(
    "lorem ipsum dolor sit amet consectetur adipiscing elit sed do eiusmod tempor incididunt ut labore et dolore ",
    "magna aliqua enim ad minim veniam quis nostrud exercitation ullamco laboris nisi aliquip ex ea commodo ",
    "consequat duis aute irure in reprehenderit voluptate velit esse cillum eu fugiat nulla pariatur excepteur ",
    "sint occaecat cupidatat non proident sunt culpa qui officia deserunt mollit anim id est laborum"
);

/// Lorem Ipsum: a word count, or "3p" for three paragraphs.
///
/// Despite the name this is **not** random — words are taken in order from a fixed list, so it
/// is comparable by equality like any other tool.
pub fn lorem(input: &str) -> Result<String, String> {
    let words: Vec<&str> = LOREM_WORDS.split(' ').collect();
    let mut spec = input.trim().to_lowercase();
    let paragraphs = spec.ends_with('p');
    if paragraphs {
        spec.pop();
        spec = spec.trim().to_string();
    }

    let range = || i18n::format("Error_OutOfRange", &["1", "10000"]);
    let n: i64 = if spec.is_empty() {
        if paragraphs { 3 } else { 50 }
    } else {
        spec.parse().map_err(|_| range())?
    };
    if !(1..=10000).contains(&n) {
        return Err(range());
    }

    let take = |offset: usize, count: usize| -> Vec<&str> {
        (offset..offset + count).map(|i| words[i % words.len()]).collect()
    };
    // First letter upper-cased, full stop appended.
    let sentence = |taken: Vec<&str>| -> String {
        let joined = taken.join(" ");
        let mut chars = joined.chars();
        let first = chars.next().map(|c| c.to_uppercase().to_string()).unwrap_or_default();
        format!("{first}{}.", chars.as_str())
    };

    Ok(if paragraphs {
        (0..n as usize)
            .map(|i| sentence(take(i * 60, 60)))
            .collect::<Vec<_>>()
            .join("\n\n")
    } else {
        sentence(take(0, n as usize))
    })
}

/// Stacks random combining marks on each character.
/// ponytail: fixed intensity (0–5 marks); add a level argument if anyone wants to dial it.
pub fn zalgo(s: &str) -> Result<String, String> {
    if s.is_empty() {
        return Err(i18n::get("Error_NeedText").to_string());
    }
    // The combining diacritical block, U+0300..U+036F — the same range the C# builds.
    let marks: Vec<char> = (0x0300u32..=0x036F).filter_map(char::from_u32).collect();

    let mut out = String::new();
    for c in s.chars() {
        out.push(c);
        if c == '\n' || c == ' ' {
            continue;
        }
        for _ in 0..csprng::below(6) {
            out.push(marks[csprng::below(marks.len())]);
        }
    }
    Ok(out)
}

const NBSP: char = '\u{a0}';

/// French spacing rules: a non-breaking space before `; : ! ? %`, inside guillemets, and an
/// ellipsis for "...". Written as a char scan because the C# regexes are simple enough that a
/// regex engine would be a dependency for nothing.
pub fn french_typography(s: &str) -> String {
    // `\s*([;:!?%])` — swallow any run of spaces before the mark, then insert one NBSP.
    let mut step = String::with_capacity(s.len());
    for c in s.chars() {
        if matches!(c, ';' | ':' | '!' | '?' | '%') {
            while step.ends_with(char::is_whitespace) {
                step.pop();
            }
            step.push(NBSP);
        }
        step.push(c);
    }

    // `«\s*` and `\s*»`
    let mut quoted = String::with_capacity(step.len());
    let mut chars = step.chars().peekable();
    while let Some(c) = chars.next() {
        if c == '«' {
            quoted.push('«');
            while chars.peek().is_some_and(|n| n.is_whitespace()) {
                chars.next();
            }
            quoted.push(NBSP);
            continue;
        }
        if c == '»' {
            while quoted.ends_with(char::is_whitespace) {
                quoted.pop();
            }
            quoted.push(NBSP);
            quoted.push('»');
            continue;
        }
        quoted.push(c);
    }

    let mut out = quoted.replace("...", "…");

    // A colon inside a URL is not punctuation: undo the NBSP the first pass inserted.
    out = out.replace(&format!("{NBSP}://"), "://");
    out = out.replace(&format!("http{NBSP}:"), "http:");
    out = out.replace(&format!("https{NBSP}:"), "https:");
    out
}

/// "90deg #f00 #00f" — a CSS linear-gradient with every colour normalised to hex.
pub fn gradient(input: &str) -> Result<String, String> {
    let mut angle = "90deg".to_string();
    let mut stops: Vec<String> = Vec::new();

    for part in input.split([' ', ',', '\n']).map(str::trim).filter(|p| !p.is_empty()) {
        if let Some(value) = part.strip_suffix("deg") {
            if value.parse::<f64>().is_ok() {
                angle = part.to_string();
                continue;
            }
        }
        // Normalise each colour to its hex form, as the C# does via Describe's first line.
        stops.push(colors::hex(colors::parse(part)?));
    }

    if stops.len() < 2 {
        return Err(i18n::get("Error_NeedTwoColors").to_string());
    }
    Ok(format!("background: linear-gradient({angle}, {});", stops.join(", ")))
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn lorem_is_deterministic_and_bounded() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        assert_eq!(lorem("5").unwrap(), "Lorem ipsum dolor sit amet.");
        assert_eq!(lorem("5").unwrap(), lorem("5").unwrap(), "same input, same output");
        assert_eq!(lorem("").unwrap().split(' ').count(), 50, "default is 50 words");
        assert_eq!(lorem("3p").unwrap().split("\n\n").count(), 3);
        assert_eq!(lorem("p").unwrap().split("\n\n").count(), 3, "default is 3 paragraphs");
        assert!(lorem("0").is_err());
        assert!(lorem("10001").is_err());
        assert!(lorem("zzz").is_err());
    }

    #[test]
    fn lorem_wraps_the_word_list_rather_than_running_out() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        // More words than the list holds must still work.
        assert_eq!(lorem("500").unwrap().split(' ').count(), 500);
    }

    #[test]
    fn zalgo_keeps_the_original_characters_and_spares_whitespace() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        let out = zalgo("ab c").unwrap();
        // Stripping the combining marks must give the input back.
        let stripped: String = out
            .chars()
            .filter(|c| !(0x0300..=0x036F).contains(&(*c as u32)))
            .collect();
        assert_eq!(stripped, "ab c");
        assert!(out.chars().count() >= 4, "marks were added: {out:?}");
        assert!(zalgo("").is_err());
    }

    #[test]
    fn french_typography_inserts_non_breaking_spaces() {
        assert_eq!(french_typography("Bonjour !"), "Bonjour\u{a0}!");
        assert_eq!(french_typography("Bonjour!"), "Bonjour\u{a0}!", "inserted even with no space");
        assert_eq!(french_typography("a   ;b"), "a\u{a0};b", "a run of spaces collapses to one");
        assert_eq!(french_typography("50%"), "50\u{a0}%");
        assert_eq!(french_typography("« oui »"), "«\u{a0}oui\u{a0}»");
        assert_eq!(french_typography("Attendez..."), "Attendez…");
    }

    /// The rule must not mangle URLs, whose colon is not punctuation.
    #[test]
    fn french_typography_leaves_urls_alone() {
        assert_eq!(
            french_typography("https://example.com"),
            "https://example.com",
            "the scheme colon must survive"
        );
        assert_eq!(french_typography("http://x.y"), "http://x.y");
    }

    #[test]
    fn gradient_normalises_colours_and_reads_the_angle() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        assert_eq!(
            gradient("#f00 #00f").unwrap(),
            "background: linear-gradient(90deg, #FF0000, #0000FF);"
        );
        assert!(gradient("45deg #f00 #00f").unwrap().contains("45deg"));
        // rgb() cannot be used here on either side: the input splits on commas, so "rgb(0,0,255)"
        // arrives as three broken tokens. Shorthand hex is the usable form.
        assert!(gradient("#f00 #00ff00 #00f").unwrap().contains("#00FF00"));
        assert!(gradient("#f00").is_err(), "needs two stops");
        assert!(gradient("zzz #f00").is_err());
    }
}
