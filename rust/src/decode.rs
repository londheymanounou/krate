//! HTML entity decoding, JSON→YAML and the SQL formatter. Mirrors `Encodings.HtmlDecode`,
//! `Data.JsonToYaml` and `Dev.SqlFormat`.

use crate::entities::ENTITIES;
use crate::i18n;

/// Decodes HTML named and numeric character references, matching `WebUtility.HtmlDecode`.
///
/// The scan rule is the subtle part: from just after an `&`, .NET looks for the first `;` **or**
/// `&`, and only decodes when it lands on `;`. So `&amp&amp;` yields `&amp&` — the first `&` is
/// literal because another `&` comes before any `;`.
pub fn html_decode(s: &str) -> String {
    let chars: Vec<char> = s.chars().collect();
    let mut out = String::with_capacity(s.len());
    let mut i = 0;
    while i < chars.len() {
        if chars[i] != '&' {
            out.push(chars[i]);
            i += 1;
            continue;
        }
        // First ';' or '&' after the ampersand; anything else means no entity here.
        let terminator = chars[i + 1..].iter().position(|&c| c == ';' || c == '&');
        let Some(offset) = terminator.filter(|&o| chars[i + 1 + o] == ';') else {
            out.push('&');
            i += 1;
            continue;
        };
        let body: String = chars[i + 1..i + 1 + offset].iter().collect();
        match decode_entity(&body) {
            Some(text) => out.push_str(&text),
            // Unknown entities are left exactly as written, delimiters included.
            None => {
                out.push('&');
                out.push_str(&body);
                out.push(';');
            }
        }
        i += 1 + offset + 1;
    }
    out
}

/// The text between `&` and `;`, or None to leave it literal.
fn decode_entity(body: &str) -> Option<String> {
    // A bare "#" is not numeric; it falls through to the (failing) named lookup.
    if body.len() > 1 && body.starts_with('#') {
        let value = if matches!(body.as_bytes()[1], b'x' | b'X') {
            // NumberStyles.AllowHexSpecifier: hex digits only, no sign, no whitespace.
            let digits = &body[2..];
            if digits.is_empty() || !digits.chars().all(|c| c.is_ascii_hexdigit()) {
                return None;
            }
            u32::from_str_radix(digits, 16).ok()?
        } else {
            // NumberStyles.Integer allows surrounding whitespace and a leading '+' — so
            // "&# 65;" and "&#+65;" both decode, while "&#-65;" does not.
            let digits = body[1..].trim_matches(|c: char| c.is_whitespace());
            let digits = digits.strip_prefix('+').unwrap_or(digits);
            if digits.is_empty() || !digits.chars().all(|c| c.is_ascii_digit()) {
                return None;
            }
            digits.parse::<u32>().ok()?
        };
        // Surrogate halves are rejected outright, as is anything past the last plane.
        return char::from_u32(value).map(|c| c.to_string());
    }

    ENTITIES
        .binary_search_by_key(&body, |(name, _)| name)
        .ok()
        .and_then(|index| char::from_u32(ENTITIES[index].1))
        .map(|c| c.to_string())
}

/// JSON → YAML in block style.
pub fn json_to_yaml(input: &str) -> Result<String, String> {
    // Reuses json.rs's location mapping so the "invalid JSON" message is worded and numbered
    // identically here — the C# routes both through the same Json_Invalid string.
    let value: serde_json::Value =
        serde_json::from_str(input).map_err(|e| crate::json::locate_error(&e))?;

    // Numbers go out verbatim, so the writer needs the source spelling, not serde's.
    let mut numbers = crate::json::RawNumbers::scan(input);
    let mut out = String::new();
    if has_children(&value) {
        emit_yaml(&value, &mut out, 0, &mut numbers);
    } else {
        // A bare scalar document.
        out.push_str(&yaml_scalar(&value, &mut numbers));
        out.push('\n');
    }
    Ok(out.trim_end_matches('\n').to_string())
}

/// Only a non-empty container gets block treatment; `{}` and `[]` stay inline.
fn has_children(value: &serde_json::Value) -> bool {
    match value {
        serde_json::Value::Object(map) => !map.is_empty(),
        serde_json::Value::Array(items) => !items.is_empty(),
        _ => false,
    }
}

fn emit_yaml(
    value: &serde_json::Value,
    out: &mut String,
    indent: usize,
    numbers: &mut crate::json::RawNumbers,
) {
    let pad = "  ".repeat(indent);
    match value {
        serde_json::Value::Object(map) => {
            for (key, child) in map {
                out.push_str(&pad);
                out.push_str(&yaml_string(key));
                out.push(':');
                emit_child(child, out, indent, numbers);
            }
        }
        serde_json::Value::Array(items) => {
            for item in items {
                out.push_str(&pad);
                out.push('-');
                emit_child(item, out, indent, numbers);
            }
        }
        _ => {}
    }
}

fn emit_child(
    value: &serde_json::Value,
    out: &mut String,
    indent: usize,
    numbers: &mut crate::json::RawNumbers,
) {
    if has_children(value) {
        out.push('\n');
        emit_yaml(value, out, indent + 1, numbers);
    } else {
        match value {
            serde_json::Value::Object(_) => out.push_str(" {}\n"),
            serde_json::Value::Array(_) => out.push_str(" []\n"),
            _ => {
                out.push(' ');
                out.push_str(&yaml_scalar(value, numbers));
                out.push('\n');
            }
        }
    }
}

fn yaml_scalar(value: &serde_json::Value, numbers: &mut crate::json::RawNumbers) -> String {
    match value {
        serde_json::Value::String(s) => yaml_string(s),
        serde_json::Value::Bool(true) => "true".to_string(),
        serde_json::Value::Bool(false) => "false".to_string(),
        serde_json::Value::Null => "null".to_string(),
        // Numbers go out verbatim, as GetRawText does.
        other => numbers.take_next().unwrap_or_else(|| other.to_string()),
    }
}

/// Quotes a scalar when leaving it plain would make YAML read it as something else.
fn yaml_string(s: &str) -> String {
    const FLOW: &str = "!&*?|>%@`\"'#,-:[]{}";
    let first = s.chars().next();
    let needs_quote = s.is_empty()
        || s != s.trim()
        || first.is_some_and(|c| FLOW.contains(c))
        || s.contains(": ")
        || s.contains(" #")
        || s.contains('\n')
        || s.contains('\t')
        // bool.TryParse is case-insensitive.
        || s.eq_ignore_ascii_case("true")
        || s.eq_ignore_ascii_case("false")
        || looks_like_a_number(s)
        || matches!(s, "null" | "~" | "yes" | "no" | "on" | "off");
    if needs_quote {
        format!("\"{}\"", s.replace('\\', "\\\\").replace('"', "\\\""))
    } else {
        s.to_string()
    }
}

/// Mirrors `double.TryParse(s, NumberStyles.Any, InvariantCulture, out _)`.
///
/// `NumberStyles.Any` is wider than it looks and `f64::from_str` is wider in different places, so
/// neither delegating to Rust's parser nor a naive digit check would agree. Each rule below was
/// probed against the runtime: `(5)`, `5-`, `5,` and `¤5` are numbers; `$5`, `( 5 )`, `,5`, `inf`,
/// `1_0` and `0x41` are not.
fn looks_like_a_number(s: &str) -> bool {
    let t = s.trim();
    // AllowParentheses — with no room for whitespace inside them.
    let t = t.strip_prefix('(').and_then(|r| r.strip_suffix(')')).unwrap_or(t);
    // AllowCurrencySymbol: InvariantCulture's symbol is "¤", not "$".
    let t = t.strip_prefix('¤').unwrap_or(t);
    // AllowLeadingSign and AllowTrailingSign, at most one of each.
    let t = t.strip_prefix(['+', '-']).unwrap_or(t);
    let t = t.strip_suffix(['+', '-']).unwrap_or(t);

    // The invariant NaN/Infinity symbols, matched case-insensitively. "inf" is not one of them.
    if t.eq_ignore_ascii_case("nan") || t.eq_ignore_ascii_case("infinity") {
        return true;
    }
    // AllowThousands drops separators without checking group sizes, but one may not lead.
    if t.starts_with(',') {
        return false;
    }
    is_plain_decimal(&t.replace(',', ""))
}

/// Digits with at most one point and an optional exponent. Accepts `5.`, `.5` and `1.2E+10`;
/// rejects `5e`, `e5`, `1.2.3` and anything non-ASCII-digit.
fn is_plain_decimal(t: &str) -> bool {
    let (mantissa, exponent) = match t.split_once(['e', 'E']) {
        Some((m, e)) => (m, Some(e)),
        None => (t, None),
    };
    let mut digits = 0;
    let mut points = 0;
    for c in mantissa.chars() {
        match c {
            '0'..='9' => digits += 1,
            '.' => points += 1,
            _ => return false,
        }
    }
    if digits == 0 || points > 1 {
        return false;
    }
    match exponent {
        None => true,
        Some(e) => {
            let e = e.strip_prefix(['+', '-']).unwrap_or(e);
            !e.is_empty() && e.chars().all(|c| c.is_ascii_digit())
        }
    }
}

/// Major clauses that start a new line. Longest-first within a family so "INNER JOIN" is matched
/// before a bare "JOIN", and "UNION ALL" before "UNION" — the C# array order carries that.
const SQL_CLAUSES: &[&str] = &[
    "SELECT", "FROM", "WHERE", "INNER JOIN", "LEFT JOIN", "RIGHT JOIN", "FULL JOIN", "CROSS JOIN",
    "JOIN", "GROUP BY", "ORDER BY", "HAVING", "LIMIT", "OFFSET", "UNION ALL", "UNION",
    "INSERT INTO", "VALUES", "UPDATE", "SET", "DELETE FROM",
];
const SQL_KEYWORDS: &[&str] = &[
    "AND", "OR", "NOT", "IN", "AS", "ON", "IS", "NULL", "LIKE", "BETWEEN", "DISTINCT", "COUNT",
    "ASC", "DESC", "INT", "TRUE", "FALSE", "EXISTS", "CASE", "WHEN", "THEN", "ELSE", "END",
];

/// `\b` in .NET: a boundary between a word character and a non-word character.
fn is_word_char(c: char) -> bool {
    c.is_alphanumeric() || c == '_'
}

/// Case-insensitive whole-word replacement, where a keyword's internal space matches `\s+`.
/// Left to right, non-overlapping, like `Regex.Replace`.
fn replace_word(haystack: &str, word: &str, replacement: &str) -> String {
    let chars: Vec<char> = haystack.chars().collect();
    let parts: Vec<&str> = word.split(' ').collect();
    let mut out = String::with_capacity(haystack.len());
    let mut i = 0;
    'outer: while i < chars.len() {
        // A word may only start at a boundary.
        if !(i == 0 || !is_word_char(chars[i - 1])) || !is_word_char(chars[i]) {
            out.push(chars[i]);
            i += 1;
            continue;
        }
        let mut j = i;
        for (index, part) in parts.iter().enumerate() {
            if index > 0 {
                // `\s+` between the parts: at least one whitespace character.
                let before = j;
                while j < chars.len() && chars[j].is_whitespace() {
                    j += 1;
                }
                if j == before {
                    out.push(chars[i]);
                    i += 1;
                    continue 'outer;
                }
            }
            let matches = part
                .chars()
                .enumerate()
                .all(|(k, c)| chars.get(j + k).is_some_and(|h| h.eq_ignore_ascii_case(&c)));
            if !matches {
                out.push(chars[i]);
                i += 1;
                continue 'outer;
            }
            j += part.chars().count();
        }
        // ...and may only end at one.
        if j < chars.len() && is_word_char(chars[j]) {
            out.push(chars[i]);
            i += 1;
            continue;
        }
        out.push_str(replacement);
        i = j;
    }
    out
}

/// Light SQL formatter: uppercases keywords and starts each major clause on its own line.
///
/// Token level, no parser — nested subqueries are not reindented, matching the C#.
pub fn sql_format(sql: &str) -> Result<String, String> {
    // Collapse whitespace first so the clause breaks are the only line breaks.
    let mut flat = String::with_capacity(sql.len());
    let mut in_space = false;
    for c in sql.trim().chars() {
        if c.is_whitespace() {
            if !in_space {
                flat.push(' ');
                in_space = true;
            }
        } else {
            flat.push(c);
            in_space = false;
        }
    }
    if flat.is_empty() {
        return Err(i18n::get("Error_NeedText").to_string());
    }

    for word in SQL_CLAUSES.iter().chain(SQL_KEYWORDS) {
        flat = replace_word(&flat, word, word);
    }

    // Break before each major clause. `\s+CLAUSE\b` — so a clause at the very start, with no
    // leading whitespace, is left alone and does not gain a line break.
    for clause in SQL_CLAUSES {
        flat = break_before(&flat, clause);
    }

    Ok(flat.split('\n').map(str::trim).collect::<Vec<_>>().join("\n").trim().to_string())
}

/// `Regex.Replace(flat, @"\s+CLAUSE\b", "\nCLAUSE")`. The clause is already uppercase by now, so
/// this match is case-sensitive, exactly as `Regex.Escape(clause)` without IgnoreCase is.
fn break_before(haystack: &str, clause: &str) -> String {
    let chars: Vec<char> = haystack.chars().collect();
    let target: Vec<char> = clause.chars().collect();
    let mut out = String::with_capacity(haystack.len());
    let mut i = 0;
    while i < chars.len() {
        if !chars[i].is_whitespace() {
            out.push(chars[i]);
            i += 1;
            continue;
        }
        // Greedy `\s+`, then the clause must follow.
        let mut j = i;
        while j < chars.len() && chars[j].is_whitespace() {
            j += 1;
        }
        let hit = chars[j..].starts_with(&target)
            && chars.get(j + target.len()).is_none_or(|c| !is_word_char(*c));
        if hit {
            out.push('\n');
            out.push_str(clause);
            i = j + target.len();
        } else {
            out.push_str(&chars[i..j].iter().collect::<String>());
            i = j;
        }
    }
    out
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn html_decode_handles_named_numeric_and_hex() {
        assert_eq!(html_decode("&amp;"), "&");
        assert_eq!(html_decode("a&amp;b&lt;c"), "a&b<c");
        assert_eq!(html_decode("&#65;"), "A");
        assert_eq!(html_decode("&#x41;"), "A");
        assert_eq!(html_decode("&#X41;"), "A");
        assert_eq!(html_decode("&#x4a;"), "J");
        assert_eq!(html_decode("&nbsp;"), "\u{a0}");
        assert_eq!(html_decode("&hellip;"), "\u{2026}");
        assert_eq!(html_decode("&Alpha;&omega;"), "\u{391}\u{3c9}");
        assert_eq!(html_decode("&#x1F600;"), "\u{1f600}");
        assert_eq!(html_decode("&#128512;"), "\u{1f600}");
    }

    /// The lookup is case-sensitive and unknown names are left verbatim.
    #[test]
    fn html_decode_leaves_what_it_does_not_know() {
        for verbatim in ["&AMP;", "&Amp;", "&nosuch;", "&amp", "&", "&;", "&#;", "&#x;", "&verylongname;"] {
            assert_eq!(html_decode(verbatim), verbatim, "{verbatim}");
        }
        assert_eq!(html_decode(""), "");
        assert_eq!(html_decode("no entities here"), "no entities here");
    }

    /// From just after `&`, the first `;` *or* `&` ends the search, and only `;` decodes.
    #[test]
    fn html_decode_stops_scanning_at_the_next_ampersand() {
        assert_eq!(html_decode("&&amp;"), "&&");
        assert_eq!(html_decode("&amp&amp;"), "&amp&");
        assert_eq!(html_decode("&#65&#66;"), "&#65B");
        assert_eq!(html_decode("&lt&gt;"), "&lt>");
        assert_eq!(html_decode("&amp;amp;"), "&amp;");
    }

    /// `NumberStyles.Integer` on the decimal form, `AllowHexSpecifier` on the hex form.
    #[test]
    fn html_decode_matches_dotnet_number_parsing() {
        assert_eq!(html_decode("&# 65;"), "A", "leading whitespace is allowed");
        assert_eq!(html_decode("&#65 ;"), "A", "so is trailing");
        assert_eq!(html_decode("&#+65;"), "A", "and a leading plus");
        assert_eq!(html_decode("&#-65;"), "&#-65;", "but not a minus");
        assert_eq!(html_decode("&#0065;"), "A");
        // Hex takes no whitespace and no sign.
        assert_eq!(html_decode("&#x 41;"), "&#x 41;");
        assert_eq!(html_decode("&#x+41;"), "&#x+41;");
    }

    #[test]
    fn html_decode_rejects_surrogates_and_out_of_range() {
        for bad in ["&#xD800;", "&#xDFFF;", "&#x110000;", "&#1114112;", "&#4294967296;", "&#99999999999999;"] {
            assert_eq!(html_decode(bad), bad, "{bad}");
        }
        assert_eq!(html_decode("&#x10FFFF;"), "\u{10ffff}");
        assert_eq!(html_decode("&#1114111;"), "\u{10ffff}");
    }

    /// A NUL survives here; the FFI is what replaces it, deliberately.
    #[test]
    fn html_decode_can_produce_a_nul() {
        assert_eq!(html_decode("&#0;"), "\0");
    }

    #[test]
    fn json_to_yaml_emits_block_style() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        assert_eq!(json_to_yaml(r#"{"a":1,"b":"x"}"#).unwrap(), "a: 1\nb: x");
        assert_eq!(json_to_yaml(r#"{"a":{"b":[1,2]}}"#).unwrap(), "a:\n  b:\n    - 1\n    - 2");
        assert_eq!(json_to_yaml("[1,2]").unwrap(), "- 1\n- 2");
    }

    #[test]
    fn json_to_yaml_keeps_empty_containers_inline() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        assert_eq!(json_to_yaml(r#"{"a":{},"b":[]}"#).unwrap(), "a: {}\nb: []");
        // An empty container at the root is a scalar document, not a block.
        assert_eq!(json_to_yaml("{}").unwrap(), "{}");
        assert_eq!(json_to_yaml("[]").unwrap(), "[]");
        assert_eq!(json_to_yaml("42").unwrap(), "42");
        assert_eq!(json_to_yaml("null").unwrap(), "null");
        assert!(json_to_yaml("{oops").is_err());
    }

    /// Quoting is what keeps the YAML honest: a string that looks like a number must not
    /// round-trip as one.
    #[test]
    fn json_to_yaml_quotes_ambiguous_strings() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        for (json, expected) in [
            (r#"{"a":"1"}"#, "a: \"1\""),
            (r#"{"a":"true"}"#, "a: \"true\""),
            (r#"{"a":"null"}"#, "a: \"null\""),
            (r#"{"a":"yes"}"#, "a: \"yes\""),
            (r#"{"a":""}"#, "a: \"\""),
            (r#"{"a":" x"}"#, "a: \" x\""),
            (r#"{"a":"k: v"}"#, "a: \"k: v\""),
            (r#"{"a":"-x"}"#, "a: \"-x\""),
            (r#"{"a":"plain text"}"#, "a: plain text"),
            // Only the FIRST character is checked against the flow set, so an interior quote
            // does not trigger quoting. Legal YAML, and what the C# emits.
            (r#"{"a":"say \"hi\""}"#, "a: say \"hi\""),
        ] {
            assert_eq!(json_to_yaml(json).unwrap(), expected, "{json}");
        }
    }

    /// Each case here was taken from a probe of the real `double.TryParse`.
    #[test]
    fn the_number_rule_matches_number_styles_any() {
        for yes in ["5", "5.0", "1e5", "1E5", "1,000", "+5", "(5)", "5-", "5+", "¤5", ".5", "5.",
                    "1.2E+10", "NaN", "nan", "Infinity", "infinity", "-Infinity", "1,00,0", "5,",
                    "0", "007"] {
            assert!(looks_like_a_number(yes), "{yes:?} is a number to .NET");
        }
        for no in ["$5", "inf", "1_0", "0x41", "1 000", ",5", "--5", "()", "( 5 )", "+", "-", ".",
                   "e5", "5e", "1e", "1.2.3", "٥", "", "plain"] {
            assert!(!looks_like_a_number(no), "{no:?} is not a number to .NET");
        }
    }

    #[test]
    fn sql_format_uppercases_and_breaks_clauses() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        assert_eq!(
            sql_format("select a from t where b = 1").unwrap(),
            "SELECT a\nFROM t\nWHERE b = 1"
        );
        // "INNER JOIN" gets its line break, and then the bare "JOIN" clause pass matches the space
        // inside it and breaks again. A wart, verified against the C# CLI rather than assumed.
        assert_eq!(
            sql_format("select * from a inner join b on a.id = b.id order by x").unwrap(),
            "SELECT *\nFROM a\nINNER\nJOIN b ON a.id = b.id\nORDER BY x"
        );
        assert!(sql_format("").is_err());
        assert!(sql_format("   ").is_err());
    }

    /// Word boundaries matter: an identifier that merely contains a keyword is left alone.
    #[test]
    fn sql_format_respects_word_boundaries() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        let out = sql_format("select android from t").unwrap();
        assert!(out.contains("android"), "'and' inside a word is not a keyword: {out}");
        let out = sql_format("select a_from_b from t").unwrap();
        assert_eq!(out, "SELECT a_from_b\nFROM t", "underscore is a word character");
    }
}
