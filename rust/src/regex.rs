//! Regular-expression tester. Mirrors `Dev.RegexTest`.
//!
//! `fancy-regex` rather than the `regex` crate: user-supplied patterns routinely use lookaround and
//! backreferences, which `regex` does not support by design.
//!
//! The subtle part is **group numbering**. .NET numbers unnamed groups 1..n first and only then
//! numbers the named ones, so `(a)(?<x>b)(c)(?<y>d)` gives a=1, c=2, x=3, y=4 — and `Match.Groups`
//! enumerates in that order, which is *not* left-to-right. PCRE-style engines number every group
//! left to right. `group_order` scans the pattern and builds the mapping, verified against the
//! runtime rather than assumed.
//!
//! **Two divergences, both on invalid input only.** `fancy-regex` reads a reversed repetition
//! range like `a{2,1}` as literal text where .NET rejects it. And on catastrophic backtracking
//! .NET raises a timeout after two seconds while fancy-regex hits a backtrack limit — a
//! different failure, but the same outcome of abandoning the run rather than answering wrongly.
//!
//! **Known divergence:** an *invalid* pattern. .NET throws an `ArgumentException` carrying its own
//! English parser message ("Unterminated [] set."), which is a raw-message leak of the kind fixed
//! elsewhere in this port — but fixing it here would mean inventing a resource key and 17
//! translations, and would throw away the detail that tells the user what is wrong with their
//! pattern. Both sides reject the same patterns; only the wording differs, and the parity test
//! asserts rejection rather than the message.

use crate::i18n;

/// A capture group as it appears in the pattern, left to right.
enum Group {
    Unnamed,
    Named(String),
}

/// Capture groups in source order. Lookaround and non-capturing groups are excluded, and parens
/// inside a character class or behind a backslash are not groups at all.
fn scan_groups(pattern: &str) -> Vec<Group> {
    let chars: Vec<char> = pattern.chars().collect();
    let mut groups = Vec::new();
    let mut i = 0;
    let mut in_class = false;
    while i < chars.len() {
        match chars[i] {
            '\\' => {
                i += 2; // the escaped character cannot open anything
                continue;
            }
            '[' if !in_class => {
                in_class = true;
                // A ']' immediately after '[' or '[^' is a literal, not the end of the class.
                i += 1;
                if chars.get(i) == Some(&'^') {
                    i += 1;
                }
                if chars.get(i) == Some(&']') {
                    i += 1;
                }
                continue;
            }
            ']' if in_class => {
                in_class = false;
                i += 1;
                continue;
            }
            '(' if !in_class => {
                if chars.get(i + 1) == Some(&'?') {
                    // (?<name>  and  (?'name'  capture; (?<=  and  (?<!  are lookbehind.
                    let named = match chars.get(i + 2) {
                        Some('<') => !matches!(chars.get(i + 3), Some('=') | Some('!')),
                        Some('\'') => true,
                        // (?P<name> as some engines spell it.
                        Some('P') => chars.get(i + 3) == Some(&'<'),
                        _ => false,
                    };
                    if named {
                        let start = if chars.get(i + 2) == Some(&'P') { i + 4 } else { i + 3 };
                        let close = if chars.get(start - 1) == Some(&'\'') { '\'' } else { '>' };
                        let mut end = start;
                        while end < chars.len() && chars[end] != close {
                            end += 1;
                        }
                        groups.push(Group::Named(chars[start..end].iter().collect()));
                    }
                    // Anything else beginning "(?" captures nothing.
                } else {
                    groups.push(Group::Unnamed);
                }
                i += 1;
                continue;
            }
            _ => i += 1,
        }
    }
    groups
}

/// `(dotnet name, engine group index)` in .NET's enumeration order: unnamed groups by number, then
/// named groups in source order.
fn group_order(pattern: &str) -> Vec<(String, usize)> {
    let groups = scan_groups(pattern);
    let mut order = Vec::new();
    let mut number = 0;
    for (index, group) in groups.iter().enumerate() {
        if matches!(group, Group::Unnamed) {
            number += 1;
            order.push((number.to_string(), index + 1));
        }
    }
    for (index, group) in groups.iter().enumerate() {
        if let Group::Named(name) = group {
            order.push((name.clone(), index + 1));
        }
    }
    order
}

/// Strips a pasted `/pattern/flags` wrapper, returning the pattern and the inline flag letters.
fn split_delimited(pattern: &str) -> (String, String) {
    let chars: Vec<char> = pattern.chars().collect();
    if chars.len() > 2 && chars[0] == '/' {
        if let Some(end) = chars.iter().rposition(|c| *c == '/').filter(|e| *e > 0) {
            let mut flags = String::new();
            for flag in &chars[end + 1..] {
                // Anything else is ignored, as RegexOptions.None in the C#'s switch.
                if matches!(flag, 'i' | 'm' | 's' | 'x') {
                    flags.push(*flag);
                }
            }
            return (chars[1..end].iter().collect(), flags);
        }
    }
    (pattern.to_string(), String::new())
}

/// UTF-16 code units before a byte offset — `Match.Index` counts those, not bytes or characters.
fn utf16_index(subject: &str, byte_offset: usize) -> usize {
    subject[..byte_offset].chars().map(char::len_utf16).sum()
}

pub fn test(input: &str) -> Result<String, String> {
    let normalized = input.replace("\r\n", "\n");
    let (first, subject) = match normalized.split_once('\n') {
        Some((first, rest)) => (first, rest),
        None => return Err(i18n::get("Error_RegexUsage").to_string()),
    };
    if first.trim().is_empty() {
        return Err(i18n::get("Error_RegexUsage").to_string());
    }

    let (pattern, flags) = split_delimited(first.trim());
    // Flags are applied by wrapping rather than by a leading (?i), so they cannot leak past a
    // top-level alternation. A non-capturing group does not shift any group number.
    let effective = if flags.is_empty() {
        pattern.clone()
    } else {
        format!("(?{flags}:{pattern})")
    };

    let regex = fancy_regex::Regex::new(&effective)
        // .NET raises its own English parser message here; see the module docs.
        .map_err(|e| format!("{e}"))?;

    let order = group_order(&pattern);
    let mut matches = Vec::new();
    for found in regex.captures_iter(subject) {
        match found {
            Ok(captures) => matches.push(captures),
            // A pattern that backtracks past the limit. .NET raises a timeout instead; either way
            // the run is abandoned rather than answered wrongly.
            Err(e) => return Err(format!("{e}")),
        }
    }

    if matches.is_empty() {
        return Ok(i18n::get("Regex_NoMatch").to_string());
    }

    let mut lines = vec![i18n::format("Regex_MatchCount", &[&matches.len().to_string()])];
    for captures in &matches {
        let whole = captures.get(0).expect("group 0 always participates");
        lines.push(format!(
            "@{:<5} {}",
            utf16_index(subject, whole.start()),
            whole.as_str()
        ));
        for (name, index) in &order {
            // Only groups that took part are listed, as `g.Success` gates the C#.
            if let Some(group) = captures.get(*index) {
                lines.push(format!("        {name} = {}", group.as_str()));
            }
        }
    }
    Ok(lines.join("\n"))
}

/// The four catalogue placeholders. The GUI has a page for each and the CLI handles them directly;
/// the entry exists so they stay searchable. `NotSupportedException` on the C# side.
pub fn not_supported(_: &str) -> Result<String, String> {
    Err("This tool is provided by the interface, not the core.".to_string())
}

#[cfg(test)]
mod tests {
    use super::*;

    fn english() -> std::sync::MutexGuard<'static, ()> {
        let guard = crate::i18n::test_lock();
        i18n::set_language("en");
        guard
    }

    #[test]
    fn unnamed_groups_are_numbered_before_named_ones() {
        // Verified against the runtime: a=1, c=2, x=3, y=4, enumerated in that order.
        let order = group_order("(a)(?<x>b)(c)(?<y>d)");
        assert_eq!(
            order,
            vec![
                ("1".to_string(), 1),
                ("2".to_string(), 3),
                ("x".to_string(), 2),
                ("y".to_string(), 4),
            ]
        );
    }

    #[test]
    fn non_capturing_constructs_are_not_groups() {
        assert_eq!(scan_groups("(?:no)(yes)").len(), 1);
        assert_eq!(scan_groups("(?=ahead)(a)").len(), 1);
        assert_eq!(scan_groups("(?!not)(a)").len(), 1);
        assert_eq!(scan_groups("(?<=behind)(a)").len(), 1, "lookbehind is not a named group");
        assert_eq!(scan_groups("(?<!behind)(a)").len(), 1);
        assert_eq!(scan_groups("(?i)(a)").len(), 1);
        // An escaped paren is a literal.
        assert_eq!(scan_groups(r"\(a\)").len(), 0);
        // Parens inside a character class are literal too.
        assert_eq!(scan_groups("[()]").len(), 0);
        assert_eq!(scan_groups("[]()](a)").len(), 1, "a ']' first in a class is literal");
    }

    #[test]
    fn named_groups_are_recognised_in_both_spellings() {
        let names: Vec<String> = scan_groups("(?<a>x)(?'b'y)(?P<c>z)")
            .into_iter()
            .filter_map(|g| match g {
                Group::Named(n) => Some(n),
                Group::Unnamed => None,
            })
            .collect();
        assert_eq!(names, vec!["a", "b", "c"]);
    }

    #[test]
    fn a_delimited_pattern_loses_its_slashes_and_keeps_its_flags() {
        assert_eq!(split_delimited("/abc/i"), ("abc".to_string(), "i".to_string()));
        assert_eq!(split_delimited("/a.b/is"), ("a.b".to_string(), "is".to_string()));
        assert_eq!(split_delimited("/x/"), ("x".to_string(), String::new()));
        // Unknown flags are dropped, as the C#'s switch maps them to None.
        assert_eq!(split_delimited("/x/gu"), ("x".to_string(), String::new()));
        // Not delimited: left alone.
        assert_eq!(split_delimited("abc"), ("abc".to_string(), String::new()));
        assert_eq!(split_delimited("a/b"), ("a/b".to_string(), String::new()));
    }

    #[test]
    fn matches_are_reported_with_their_index_and_groups() {
        let _guard = english();
        let out = test("(\\w+)@(\\w+)\na@b and c@d").unwrap();
        let lines: Vec<&str> = out.lines().collect();
        assert!(lines[0].contains('2'), "two matches: {}", lines[0]);
        assert_eq!(lines[1], "@0     a@b");
        assert_eq!(lines[2], "        1 = a");
        assert_eq!(lines[3], "        2 = b");
        assert_eq!(lines[4], "@8     c@d");
    }

    #[test]
    fn named_groups_are_labelled_by_name() {
        let _guard = english();
        let out = test("(?<user>\\w+)@(?<host>\\w+)\na@b").unwrap();
        assert!(out.contains("        user = a"), "{out}");
        assert!(out.contains("        host = b"), "{out}");
    }

    /// A group that did not take part is not listed.
    #[test]
    fn unmatched_groups_are_omitted() {
        let _guard = english();
        let out = test("(a)?(b)\nb").unwrap();
        assert!(out.contains("        2 = b"), "{out}");
        assert!(!out.contains("        1 = "), "group 1 did not participate: {out}");
    }

    #[test]
    fn no_match_says_so() {
        let _guard = english();
        assert_eq!(test("xyz\nabc").unwrap(), i18n::get("Regex_NoMatch"));
    }

    #[test]
    fn flags_take_effect() {
        let _guard = english();
        assert!(test("/ABC/i\nabc").unwrap().contains("abc"), "case-insensitive");
        assert_eq!(test("ABC\nabc").unwrap(), i18n::get("Regex_NoMatch"));
        // Singleline makes '.' cross a newline.
        assert!(test("/a.b/s\na\nb").unwrap().contains('a'));
        // Multiline anchors each line.
        let multiline = test("/^b$/m\na\nb").unwrap();
        assert!(!multiline.contains("No match"), "{multiline}");
    }

    /// Flags must not leak past a top-level alternation, which a leading `(?i)` would risk.
    #[test]
    fn flags_are_scoped_to_the_whole_pattern() {
        let _guard = english();
        let out = test("/A|B/i\nb").unwrap();
        assert!(out.contains('b'), "both alternatives are case-insensitive: {out}");
    }

    /// The index counts UTF-16 units, so an astral character before the match shifts it by two.
    #[test]
    fn the_index_is_a_utf16_offset() {
        let _guard = english();
        // An emoji is two UTF-16 units, so "x" sits at index 2.
        let out = test("x\n\u{1f600}x").unwrap();
        assert!(out.contains("@2 "), "{out}");
        // A BMP accented character is one unit.
        let out = test("x\n\u{e9}x").unwrap();
        assert!(out.contains("@1 "), "{out}");
    }

    #[test]
    fn usage_is_required() {
        let _guard = english();
        assert!(test("").is_err(), "no subject line");
        assert!(test("onlyapattern").is_err());
        assert!(test("\nsubject").is_err(), "an empty pattern is refused");
        assert!(test("   \nsubject").is_err());
    }

    #[test]
    fn an_invalid_pattern_is_refused() {
        let _guard = english();
        // The wording differs from .NET's; both refuse. See the module docs.
        for bad in ["[unterminated\nx", "(unclosed\nx", "*\nx",
                    "(?<\nx", "a**\nx"] {
            assert!(test(bad).is_err(), "{bad:?}");
        }
        // ACCEPTANCE DIFFERENCE: .NET rejects a reversed repetition range ("Illegal {x,y}
        // with x > y"); fancy-regex reads it as literal text and matches it. Neither gives a
        // wrong answer for a valid pattern, so this is a documented gap rather than something
        // pre-validated, and it is excluded from the parity row.
        assert!(test("a{2,1}\nsays a{2,1}").is_ok());
    }

    #[test]
    fn the_placeholders_report_rather_than_panic() {
        let _guard = english();
        assert!(not_supported("").is_err());
        assert!(not_supported("anything").is_err());
    }
}
