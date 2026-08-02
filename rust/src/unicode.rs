//! The character inspector and the PII masker. Mirrors `Text.Inspector` and `Text.Mask`.
//!
//! Both lean on `categories.rs`, which is .NET's own Unicode table dumped from the runtime — see
//! that file for why a Rust crate's table would not do.

use crate::categories::{CATEGORY_NAMES, CATEGORY_RUNS};
use crate::tools::newline;

/// Category index for a UTF-16 code unit. The runs cover 0x0000..=0xFFFF with no gaps, so this
/// always finds one.
fn category_index(unit: u16) -> u8 {
    let found = CATEGORY_RUNS
        .binary_search_by(|(first, last, _)| {
            if unit < *first {
                std::cmp::Ordering::Greater
            } else if unit > *last {
                std::cmp::Ordering::Less
            } else {
                std::cmp::Ordering::Equal
            }
        })
        .expect("the category runs cover every code unit");
    CATEGORY_RUNS[found].2
}

fn category_name(unit: u16) -> &'static str {
    CATEGORY_NAMES[category_index(unit) as usize]
}

// Indices into CATEGORY_NAMES, which is .NET's UnicodeCategory enum order.
const UPPERCASE_LETTER: u8 = 0;
const OTHER_LETTER: u8 = 4;
const NON_SPACING_MARK: u8 = 5;
const DECIMAL_DIGIT_NUMBER: u8 = 8;
const SPACE_SEPARATOR: u8 = 11;
const PARAGRAPH_SEPARATOR: u8 = 13;
const CONTROL: u8 = 14;
const CONNECTOR_PUNCTUATION: u8 = 18;

/// `char.IsControl`.
fn is_control(unit: u16) -> bool {
    category_index(unit) == CONTROL
}

/// `char.IsWhiteSpace`: the three separator categories, plus the ASCII control whitespace and NEL,
/// which are Control but still whitespace.
fn is_whitespace(unit: u16) -> bool {
    (SPACE_SEPARATOR..=PARAGRAPH_SEPARATOR).contains(&category_index(unit))
        || (0x09..=0x0D).contains(&unit)
        || unit == 0x85
}

/// .NET's `\w` outside ECMAScript mode: `[\p{L}\p{Mn}\p{Nd}\p{Pc}]`.
fn is_word(c: char) -> bool {
    let Ok(unit) = u16::try_from(c as u32) else {
        // Astral: every assigned plane-1+ character .NET's \w matches is a letter or digit, and
        // the table only covers the BMP, so fall back to Rust's own classification there.
        return c.is_alphanumeric() || c == '_';
    };
    let category = category_index(unit);
    (UPPERCASE_LETTER..=OTHER_LETTER).contains(&category)
        || category == NON_SPACING_MARK
        || category == DECIMAL_DIGIT_NUMBER
        || category == CONNECTOR_PUNCTUATION
}

/// Lists every character that is not plain printable ASCII, with its code point and category.
///
/// Iterates **UTF-16 code units**, as `foreach (var c in input)` does, so an astral character is
/// reported as its two surrogate halves.
pub fn inspector(input: &str) -> String {
    let nl = newline(); // AppendLine
    let mut out = String::new();
    for unit in input.encode_utf16() {
        if !(is_control(unit) || is_whitespace(unit) || unit > 127) {
            continue;
        }
        // Controls are unprintable, so they show as a dot instead of themselves.
        let shown = if is_control(unit) {
            '.'
        } else {
            // A lone surrogate is not a Rust char; U+FFFD is also what .NET's own UTF-8
            // encoder produces for one, so the two agree once the text crosses the boundary.
            char::from_u32(unit as u32).unwrap_or('\u{fffd}')
        };
        out.push_str(&format!("'{shown}'  U+{unit:04X}  {}{nl}", category_name(unit)));
    }
    if out.is_empty() {
        // Not localized in the C# either — a hardcoded English literal.
        return "All basic ASCII characters.".to_string();
    }
    out.trim().to_string()
}

/// Replaces emails, phone numbers and long digit runs with placeholders so a log can be shared.
///
/// Applied in the C#'s order, which matters: an email is masked before its digits could be taken
/// for a phone number.
pub fn mask(s: &str) -> String {
    let masked = replace_emails(s);
    let masked = replace_phones(&masked);
    replace_long_numbers(&masked)
}

/// `[\w.+-]+@[\w-]+\.[\w.-]+`
fn replace_emails(s: &str) -> String {
    let chars: Vec<char> = s.chars().collect();
    let local = |c: char| is_word(c) || matches!(c, '.' | '+' | '-');
    let domain = |c: char| is_word(c) || c == '-';
    let tail = |c: char| is_word(c) || matches!(c, '.' | '-');

    let mut out = String::with_capacity(s.len());
    let mut i = 0;
    while i < chars.len() {
        // The regex is unanchored and leftmost-first, so find the earliest start that matches.
        if local(chars[i]) {
            let mut j = i;
            while j < chars.len() && local(chars[j]) {
                j += 1;
            }
            // `+` is greedy but must leave the '@'; the local part cannot be empty.
            if j > i && chars.get(j) == Some(&'@') {
                let mut k = j + 1;
                while k < chars.len() && domain(chars[k]) {
                    k += 1;
                }
                if k > j + 1 && chars.get(k) == Some(&'.') {
                    let mut m = k + 1;
                    while m < chars.len() && tail(chars[m]) {
                        m += 1;
                    }
                    if m > k + 1 {
                        out.push_str("[EMAIL]");
                        i = m;
                        continue;
                    }
                }
            }
            // No match here: emit one character and retry from the next position, as the regex
            // engine's leftmost scan does.
        }
        out.push(chars[i]);
        i += 1;
    }
    out
}

/// `(?<![\w.])\+?\d{1,4}(?:[ .-]\(?\d{1,4}\)?){2,6}(?![\w.])`
///
/// The lookbehind is why this could not use the regex crate; hand-rolling it is a few lines.
fn replace_phones(s: &str) -> String {
    let chars: Vec<char> = s.chars().collect();
    let mut out = String::with_capacity(s.len());
    let mut i = 0;
    while i < chars.len() {
        if let Some(end) = match_phone(&chars, i, &out) {
            out.push_str("[PHONE]");
            i = end;
            continue;
        }
        out.push(chars[i]);
        i += 1;
    }
    out
}

/// The character before the match position, taken from what has been emitted so far so that an
/// earlier `[EMAIL]` counts as context the same way the original text would.
fn preceding(emitted: &str) -> Option<char> {
    emitted.chars().next_back()
}

fn match_phone(chars: &[char], start: usize, emitted: &str) -> Option<usize> {
    // (?<![\w.])
    if preceding(emitted).is_some_and(|c| is_word(c) || c == '.') {
        return None;
    }
    let mut i = start;
    if chars.get(i) == Some(&'+') {
        i += 1;
    }
    // \d{1,4}
    let digits = run_of_digits(chars, i, 4)?;
    i = digits;

    // (?:[ .-]\(?\d{1,4}\)?){2,6} — greedy, so take as many groups as possible, then give them
    // back one at a time until the trailing lookahead is satisfied.
    let mut ends = Vec::new();
    let mut j = i;
    while ends.len() < 6 {
        let Some(next) = match_phone_group(chars, j) else { break };
        j = next;
        ends.push(j);
    }
    while ends.len() >= 2 {
        let end = *ends.last().unwrap();
        // (?![\w.])
        let blocked = chars.get(end).is_some_and(|&c| is_word(c) || c == '.');
        if !blocked {
            return Some(end);
        }
        ends.pop();
    }
    None
}

/// `[ .-]\(?\d{1,4}\)?`
fn match_phone_group(chars: &[char], start: usize) -> Option<usize> {
    let mut i = start;
    if !matches!(chars.get(i), Some(' ' | '.' | '-')) {
        return None;
    }
    i += 1;
    if chars.get(i) == Some(&'(') {
        i += 1;
    }
    i = run_of_digits(chars, i, 4)?;
    if chars.get(i) == Some(&')') {
        i += 1;
    }
    Some(i)
}

/// One to `max` ASCII digits, greedily. `\d` in .NET matches Unicode decimal digits, so this uses
/// the same DecimalDigitNumber test the table gives.
fn run_of_digits(chars: &[char], start: usize, max: usize) -> Option<usize> {
    let mut i = start;
    while i < chars.len() && i - start < max && is_digit(chars[i]) {
        i += 1;
    }
    if i == start { None } else { Some(i) }
}

fn is_digit(c: char) -> bool {
    match u16::try_from(c as u32) {
        Ok(unit) => category_index(unit) == DECIMAL_DIGIT_NUMBER,
        Err(_) => c.is_numeric(),
    }
}

/// `(?<![\w.])\d{6,}(?!\d)`
fn replace_long_numbers(s: &str) -> String {
    let chars: Vec<char> = s.chars().collect();
    let mut out = String::with_capacity(s.len());
    let mut i = 0;
    while i < chars.len() {
        let blocked = preceding(&out).is_some_and(|c| is_word(c) || c == '.');
        if !blocked && is_digit(chars[i]) {
            let mut j = i;
            while j < chars.len() && is_digit(chars[j]) {
                j += 1;
            }
            // \d{6,} is greedy and (?!\d) is then automatically satisfied.
            if j - i >= 6 {
                out.push_str("[NUMBER]");
                i = j;
                continue;
            }
        }
        out.push(chars[i]);
        i += 1;
    }
    out
}

#[cfg(test)]
mod tests {
    use super::*;

    fn lines(input: &str) -> Vec<String> {
        inspector(input).lines().map(str::to_string).collect()
    }

    #[test]
    fn inspector_says_nothing_about_plain_ascii() {
        assert_eq!(inspector("hello"), "All basic ASCII characters.");
        assert_eq!(inspector(""), "All basic ASCII characters.");
        assert_eq!(inspector("abc123!@#"), "All basic ASCII characters.");
    }

    #[test]
    fn inspector_names_the_dotnet_category() {
        assert_eq!(lines("é"), ["'é'  U+00E9  LowercaseLetter"]);
        assert_eq!(lines("É"), ["'É'  U+00C9  UppercaseLetter"]);
        assert_eq!(lines("中"), ["'中'  U+4E2D  OtherLetter"]);
        // A space is reported because it is whitespace, even though it is ASCII.
        assert_eq!(lines(" "), ["' '  U+0020  SpaceSeparator"]);
        assert_eq!(lines("\u{a0}"), ["'\u{a0}'  U+00A0  SpaceSeparator"]);
    }

    #[test]
    fn inspector_shows_controls_as_a_dot() {
        assert_eq!(lines("\n"), ["'.'  U+000A  Control"]);
        assert_eq!(lines("\t"), ["'.'  U+0009  Control"]);
        assert_eq!(lines("\u{0}"), ["'.'  U+0000  Control"]);
        // Zero-width characters are Format, not Control, so they print themselves.
        assert_eq!(lines("\u{200b}"), ["'\u{200b}'  U+200B  Format"]);
    }

    /// The loop walks code units, so one emoji produces two lines.
    #[test]
    fn inspector_splits_astral_characters_into_surrogates() {
        let out = lines("😀");
        assert_eq!(out.len(), 2, "{out:?}");
        assert!(out[0].contains("U+D83D") && out[0].contains("Surrogate"), "{out:?}");
        assert!(out[1].contains("U+DE00") && out[1].contains("Surrogate"), "{out:?}");
    }

    #[test]
    fn inspector_reports_every_offending_character_in_order() {
        assert_eq!(
            lines("aébc\u{a0}"),
            ["'é'  U+00E9  LowercaseLetter", "'\u{a0}'  U+00A0  SpaceSeparator"]
        );
    }

    #[test]
    fn mask_replaces_emails() {
        assert_eq!(mask("write to a.b+c@example.co.uk now"), "write to [EMAIL] now");
        assert_eq!(mask("x@y.z"), "[EMAIL]");
        // Not emails: no dot in the domain, nothing before the @, nothing after the dot.
        assert_eq!(mask("a@b"), "a@b");
        assert_eq!(mask("@b.c"), "@b.c");
        assert_eq!(mask("a@b."), "a@b.");
    }

    #[test]
    fn mask_replaces_phone_numbers() {
        assert_eq!(mask("call +33 6 12 34 56 78 today"), "call [PHONE] today");
        assert_eq!(mask("555-123-4567"), "[PHONE]");
        assert_eq!(mask("555 123-4567"), "[PHONE]");
        // Two groups minimum: one separator is not enough.
        assert_eq!(mask("12-34"), "12-34");
    }

    #[test]
    fn mask_replaces_long_digit_runs() {
        assert_eq!(mask("id 123456 here"), "id [NUMBER] here");
        assert_eq!(mask("12345"), "12345", "five digits is just a number");
        assert_eq!(mask("1234567890"), "[NUMBER]");
    }

    /// The lookbehind is the point: a digit run glued to a word is not a phone or an id.
    #[test]
    fn mask_respects_the_lookbehind_and_lookahead() {
        assert_eq!(mask("abc123456"), "abc123456", "preceded by a word character");
        assert_eq!(mask("v1.123456"), "v1.123456", "preceded by a dot");
        assert_eq!(mask("x 123456"), "x [NUMBER]", "a space is not word context");
        // The long-number lookahead is only (?!\d), unlike the phone pattern's (?![\w.]) — so a
        // trailing letter does not block it. Verified against the C# CLI.
        assert_eq!(mask("123456abc"), "[NUMBER]abc");
        assert_eq!(mask("ref 12.345678"), "ref 12.345678");
    }

    #[test]
    fn mask_leaves_ordinary_text_alone() {
        assert_eq!(mask(""), "");
        assert_eq!(mask("nothing to hide"), "nothing to hide");
        assert_eq!(mask("year 2024 and 1999"), "year 2024 and 1999");
    }
}
