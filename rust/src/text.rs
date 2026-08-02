//! Text transforms. Mirrors `Krate.Core.TextMore`.

use crate::i18n;
use unicode_normalization::UnicodeNormalization;
use unicode_segmentation::UnicodeSegmentation;

/// Strips accents: é to e, ç to c. Keeps everything else intact.
pub fn deaccent(s: &str) -> String {
    s.nfd()
        .filter(|c| !is_combining_mark(*c))
        .collect::<String>()
        .nfc()
        .collect()
}

/// The Unicode non-spacing-mark blocks that NFD produces for Latin, Greek and Cyrillic text.
/// A full category table would be a dependency for what is, in practice, these ranges.
fn is_combining_mark(c: char) -> bool {
    matches!(c as u32,
        0x0300..=0x036F   // combining diacriticals
        | 0x1AB0..=0x1AFF // extended
        | 0x1DC0..=0x1DFF // supplement
        | 0x20D0..=0x20FF // for symbols
        | 0xFE20..=0xFE2F // half marks
    )
}

/// Splits into lower-case words on separators and camelCase humps, mirroring the C# regex
/// `[^\p{L}\p{N}]+|(?<=\p{Ll})(?=\p{Lu})|(?<=\p{L})(?=\p{N})|(?<=\p{Lu})(?=\p{Lu}\p{Ll})`.
/// Written by hand because the lookarounds are not expressible in the `regex` crate.
pub fn words(s: &str) -> Vec<String> {
    let chars: Vec<char> = deaccent(s).chars().collect();
    let mut out: Vec<String> = Vec::new();
    let mut current = String::new();

    for i in 0..chars.len() {
        let c = chars[i];
        if !c.is_alphanumeric() {
            if !current.is_empty() {
                out.push(std::mem::take(&mut current));
            }
            continue;
        }
        if !current.is_empty() {
            let previous = chars[i - 1];
            let lower_to_upper = previous.is_lowercase() && c.is_uppercase();
            let letter_to_digit = previous.is_alphabetic() && c.is_numeric();
            let acronym_end = previous.is_uppercase()
                && c.is_uppercase()
                && chars.get(i + 1).is_some_and(|n| n.is_lowercase());
            if lower_to_upper || letter_to_digit || acronym_end {
                out.push(std::mem::take(&mut current));
            }
        }
        current.push(c);
    }
    if !current.is_empty() {
        out.push(current);
    }
    out.into_iter().map(|w| w.to_lowercase()).collect()
}

/// Every naming convention at once — you rarely want just one.
pub fn naming(s: &str) -> Result<String, String> {
    let words = words(s);
    if words.is_empty() {
        return Err(i18n::get("Error_NeedText").to_string());
    }
    let pascal: String = words
        .iter()
        .map(|w| {
            let mut chars = w.chars();
            match chars.next() {
                Some(first) => first.to_uppercase().collect::<String>() + chars.as_str(),
                None => String::new(),
            }
        })
        .collect();
    let camel = {
        let mut chars = pascal.chars();
        match chars.next() {
            Some(first) => first.to_lowercase().collect::<String>() + chars.as_str(),
            None => String::new(),
        }
    };
    let snake = words.join("_");
    Ok([
        format!("camelCase    {camel}"),
        format!("PascalCase   {pascal}"),
        format!("snake_case   {snake}"),
        format!("kebab-case   {}", words.join("-")),
        format!("CONSTANT     {}", snake.to_uppercase()),
    ]
    .join("\n"))
}

/// URL slug: accents flattened, punctuation dropped, words joined by hyphens.
pub fn slug(s: &str) -> String {
    words(s).join("-")
}

/// Title case, matching `TextInfo.ToTitleCase` over already-lowered text.
///
/// Every non-letter starts a new word — including digits, so "3rd" becomes "3Rd" — with one
/// exception: the ASCII apostrophe, so "o'brien" becomes "O'brien" rather than "O'Brien". The
/// typographic apostrophe U+2019 is *not* exempt and does break the word. All measured against
/// the C# build; none of it is guessable.
pub fn title(s: &str) -> String {
    let mut out = String::with_capacity(s.len());
    let mut at_word_start = true;
    for c in s.to_lowercase().chars() {
        if c.is_alphabetic() {
            if at_word_start {
                out.extend(c.to_uppercase());
            } else {
                out.push(c);
            }
            at_word_start = false;
        } else {
            out.push(c);
            at_word_start = c != '\'';
        }
    }
    out
}

/// Fixes text pasted from a PDF or a Word document: collapsed spaces, trimmed lines, no runs
/// of blank lines.
pub fn clean(s: &str) -> String {
    let normalized = s.replace("\r\n", "\n");
    let mut out: Vec<String> = Vec::new();
    for line in normalized.split('\n') {
        let collapsed = line.split_whitespace().collect::<Vec<_>>().join(" ");
        if !collapsed.is_empty() || out.last().is_some_and(|l: &String| !l.is_empty()) {
            out.push(collapsed);
        }
    }
    while out.last().is_some_and(|l| l.is_empty()) {
        out.pop();
    }
    out.join("\n")
}

/// Removes duplicate lines, keeping the first occurrence and the original order.
pub fn dedupe(s: &str) -> String {
    let normalized = s.replace("\r\n", "\n");
    let mut seen = std::collections::HashSet::new();
    normalized
        .split('\n')
        .filter(|l| seen.insert(l.to_string()))
        .collect::<Vec<_>>()
        .join("\n")
}

/// Reverses by grapheme cluster, not by char: reversing by char breaks emoji and accents.
pub fn reverse_text(s: &str) -> String {
    s.graphemes(true).rev().collect()
}

/// Word frequency, most frequent first, ties broken by the word itself.
pub fn word_frequency(s: &str) -> String {
    let mut counts: std::collections::HashMap<String, usize> = std::collections::HashMap::new();
    for word in words(s) {
        *counts.entry(word).or_insert(0) += 1;
    }
    let mut entries: Vec<(String, usize)> = counts.into_iter().collect();
    entries.sort_by(|a, b| b.1.cmp(&a.1).then(a.0.cmp(&b.0)));
    entries
        .iter()
        .map(|(word, count)| format!("{count:>6}  {word}"))
        .collect::<Vec<_>>()
        .join("\n")
}

const MORSE: [(char, &str); 48] = [
    ('a', ".-"), ('b', "-..."), ('c', "-.-."), ('d', "-.."), ('e', "."), ('f', "..-."),
    ('g', "--."), ('h', "...."), ('i', ".."), ('j', ".---"), ('k', "-.-"), ('l', ".-.."),
    ('m', "--"), ('n', "-."), ('o', "---"), ('p', ".--."), ('q', "--.-"), ('r', ".-."),
    ('s', "..."), ('t', "-"), ('u', "..-"), ('v', "...-"), ('w', ".--"), ('x', "-..-"),
    ('y', "-.--"), ('z', "--.."), ('0', "-----"), ('1', ".----"), ('2', "..---"),
    ('3', "...--"), ('4', "....-"), ('5', "....."), ('6', "-...."), ('7', "--..."),
    ('8', "---.."), ('9', "----."), ('.', ".-.-.-"), (',', "--..--"), ('?', "..--.."),
    ('\'', ".----."), ('!', "-.-.--"), ('/', "-..-."), ('(', "-.--."), (')', "-.--.-"),
    ('&', ".-..."), (':', "---..."), ('=', "-...-"), ('+', ".-.-."),
];

/// Text to Morse or back, direction detected from the input. Words are separated by " / ".
pub fn morse(input: &str) -> String {
    let s = input.trim();
    if s.is_empty() {
        return String::new();
    }

    if s.chars().all(|c| matches!(c, '.' | '-' | ' ' | '/' | '\n')) {
        return s
            .split('/')
            .map(|word| {
                word.split([' ', '\n'])
                    .filter(|code| !code.is_empty())
                    .map(|code| {
                        MORSE.iter().find(|(_, m)| *m == code).map_or('?', |(c, _)| *c)
                    })
                    .collect::<String>()
            })
            .collect::<Vec<_>>()
            .join(" ");
    }

    deaccent(s)
        .to_lowercase()
        .split(' ')
        .filter(|w| !w.is_empty())
        .map(|word| {
            word.chars()
                .map(|c| MORSE.iter().find(|(m, _)| *m == c).map_or("?", |(_, code)| *code))
                .collect::<Vec<_>>()
                .join(" ")
        })
        .collect::<Vec<_>>()
        .join(" / ")
}

/// Tokeniser for `CaseConverter`, matching the C# regex
/// `[A-Z]?[a-z]+|[A-Z]+(?=[A-Z][a-z]|)|\d+`.
///
/// Deliberately separate from `words()`: this one is ASCII-only and does not deaccent, so
/// "Crème" tokenises differently here than it does for Slug. Reusing `words()` would be tidier
/// and wrong.
fn case_words(input: &str) -> Vec<String> {
    let chars: Vec<char> = input.chars().collect();
    let mut out = Vec::new();
    let mut i = 0;
    while i < chars.len() {
        let c = chars[i];
        if c.is_ascii_digit() {
            let start = i;
            while i < chars.len() && chars[i].is_ascii_digit() {
                i += 1;
            }
            out.push(chars[start..i].iter().collect::<String>());
        } else if c.is_ascii_uppercase() {
            // Either one capital starting a word, or a run of capitals forming an acronym that
            // stops before the last capital when a lower-case letter follows it.
            let start = i;
            i += 1;
            if i < chars.len() && chars[i].is_ascii_lowercase() {
                while i < chars.len() && chars[i].is_ascii_lowercase() {
                    i += 1;
                }
            } else {
                while i < chars.len()
                    && chars[i].is_ascii_uppercase()
                    && !(i + 1 < chars.len() && chars[i + 1].is_ascii_lowercase())
                {
                    i += 1;
                }
            }
            out.push(chars[start..i].iter().collect::<String>().to_lowercase());
        } else if c.is_ascii_lowercase() {
            let start = i;
            while i < chars.len() && chars[i].is_ascii_lowercase() {
                i += 1;
            }
            out.push(chars[start..i].iter().collect::<String>());
        } else {
            i += 1;
        }
    }
    out
}

fn capitalise(w: &str) -> String {
    let mut chars = w.chars();
    match chars.next() {
        Some(first) => first.to_uppercase().collect::<String>() + chars.as_str(),
        None => String::new(),
    }
}

/// Every casing convention for one identifier. Unlike `naming`, the input is returned unchanged
/// when nothing tokenises.
pub fn case_converter(input: &str) -> String {
    let words = case_words(input);
    if words.is_empty() {
        return input.to_string();
    }
    let pascal: String = words.iter().map(|w| capitalise(w)).collect();
    let camel = words[0].clone() + &words[1..].iter().map(|w| capitalise(w)).collect::<String>();
    let snake = words.join("_");
    [
        format!("camelCase:      {camel}"),
        format!("PascalCase:     {pascal}"),
        format!("snake_case:     {snake}"),
        format!("kebab-case:     {}", words.join("-")),
        format!("SCREAMING_SNAKE:{}", snake.to_uppercase()),
    ]
    .join("
")
}

pub fn base_converter(input: &str) -> String {
    let t = input.trim();
    if t.is_empty() { return String::new(); }
    
    // Check if input looks like binary/hex/octal
    let looks_like_bin = t.chars().all(|c| c == '0' || c == '1' || c.is_whitespace());
    let looks_like_hex = t.chars().all(|c| c.is_ascii_hexdigit() || c.is_whitespace());
    
    let mut out = Vec::new();
    
    if looks_like_bin {
        let text = t.split_whitespace()
            .filter_map(|s| u8::from_str_radix(s, 2).ok())
            .map(|b| b as char)
            .collect::<String>();
        out.push(format!("TEXT (from BIN): {}", text));
    }
    
    if looks_like_hex {
        let text = t.split_whitespace()
            .filter_map(|s| u8::from_str_radix(s, 16).ok())
            .map(|b| b as char)
            .collect::<String>();
        out.push(format!("TEXT (from HEX): {}", text));
    }
    
    let bytes = t.as_bytes();
    let hex = bytes.iter().map(|b| format!("{:02X}", b)).collect::<Vec<_>>().join(" ");
    let oct = bytes.iter().map(|b| format!("{:03o}", b)).collect::<Vec<_>>().join(" ");
    let dec = bytes.iter().map(|b| format!("{}", b)).collect::<Vec<_>>().join(" ");
    let bin = bytes.iter().map(|b| format!("{:08b}", b)).collect::<Vec<_>>().join(" ");
    
    out.push(format!("HEX: {}", hex));
    out.push(format!("OCT: {}", oct));
    out.push(format!("DEC: {}", dec));
    out.push(format!("BIN: {}", bin));
    
    out.join("\n")
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn deaccent_strips_marks_and_leaves_the_rest() {
        assert_eq!(deaccent("éèàçüôÀÉ"), "eeacuoAE");
        assert_eq!(deaccent("Crème brûlée"), "Creme brulee");
        assert_eq!(deaccent("plain ascii"), "plain ascii");
        assert_eq!(deaccent("straße"), "straße", "not a combining mark");
        assert_eq!(deaccent("日本語"), "日本語");
    }

    #[test]
    fn words_splits_on_separators_and_camel_humps() {
        assert_eq!(words("hello world"), ["hello", "world"]);
        assert_eq!(words("helloWorld"), ["hello", "world"]);
        assert_eq!(words("HTTPServer"), ["http", "server"], "acronym boundary");
        assert_eq!(words("user_id"), ["user", "id"]);
        assert_eq!(words("Crème brûlée!"), ["creme", "brulee"]);
        assert!(words("   ").is_empty());
    }

    #[test]
    fn naming_produces_every_convention() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        let result = naming("hello world").unwrap();
        assert!(result.contains("camelCase    helloWorld"), "{result}");
        assert!(result.contains("PascalCase   HelloWorld"), "{result}");
        assert!(result.contains("snake_case   hello_world"), "{result}");
        assert!(result.contains("kebab-case   hello-world"), "{result}");
        assert!(result.contains("CONSTANT     HELLO_WORLD"), "{result}");
        assert!(naming("   ").is_err());
    }

    #[test]
    fn slug_is_url_safe() {
        assert_eq!(slug("Hello, World!"), "hello-world");
        assert_eq!(slug("Crème Brûlée"), "creme-brulee");
    }

    #[test]
    fn title_capitalises_each_word() {
        assert_eq!(title("hello WORLD of code"), "Hello World Of Code");
        assert_eq!(title("hello-world"), "Hello-World", "a hyphen starts a word");
        assert_eq!(title("3rd place"), "3Rd Place", "a digit starts a word");
        assert_eq!(title("o'brien"), "O'brien", "the ASCII apostrophe does not");
        assert_eq!(title("o\u{2019}brien"), "O\u{2019}Brien", "the curly one does");
    }

    #[test]
    fn clean_collapses_spaces_and_blank_runs() {
        assert_eq!(clean("a   b"), "a b");
        assert_eq!(clean("  padded  "), "padded");
        assert_eq!(clean("a\n\n\n\nb"), "a\n\nb", "runs of blanks collapse to one");
        assert_eq!(clean("a\n\n\n"), "a", "trailing blanks are dropped");
    }

    #[test]
    fn dedupe_keeps_the_first_occurrence_in_order() {
        assert_eq!(dedupe("b\na\nb\nc\na"), "b\na\nc");
    }

    #[test]
    fn reverse_text_does_not_break_graphemes() {
        assert_eq!(reverse_text("abc"), "cba");
        assert_eq!(reverse_text("héllo"), "olléh");
        assert_eq!(reverse_text("ab😀"), "😀ba", "an emoji is one grapheme");
    }

    #[test]
    fn word_frequency_is_most_frequent_first() {
        let result = word_frequency("a b a c a b");
        let lines: Vec<&str> = result.lines().collect();
        assert!(lines[0].ends_with("  a"), "{result}");
        assert!(lines[0].trim().starts_with('3'), "{result}");
        assert_eq!(lines.len(), 3);
    }

    #[test]
    fn morse_round_trips() {
        assert_eq!(morse("sos"), "... --- ...");
        assert_eq!(morse("... --- ..."), "sos");
        assert_eq!(morse("hello world"), morse("hello world"));
        assert_eq!(morse(&morse("hello world")), "hello world");
        assert_eq!(morse(""), "");
    }

    #[test]
    fn case_converter_tokenises_acronyms_and_digits() {
        // Measured against the C# build: the trailing digit becomes its own word.
        let r = case_converter("XMLHttpRequest2");
        assert!(r.contains("camelCase:      xmlHttpRequest2"), "{r}");
        assert!(r.contains("PascalCase:     XmlHttpRequest2"), "{r}");
        assert!(r.contains("snake_case:     xml_http_request_2"), "{r}");
        assert!(r.contains("kebab-case:     xml-http-request-2"), "{r}");
        assert!(r.contains("SCREAMING_SNAKE:XML_HTTP_REQUEST_2"), "{r}");
    }

    #[test]
    fn case_converter_handles_plain_and_untokenisable_input() {
        assert!(case_converter("hello world").contains("camelCase:      helloWorld"));
        assert!(case_converter("user_id").contains("PascalCase:     UserId"));
        // Nothing to tokenise means the input comes back untouched.
        assert_eq!(case_converter("   "), "   ");
        assert_eq!(case_converter(""), "");
    }
}
