//! Quoting a value for another language's syntax. Mirrors `Krate.Core.Escapes`.

/// JSON string literal, quotes included.
pub fn json(s: &str) -> String {
    let mut out = String::with_capacity(s.len() + 2);
    out.push('"');
    for c in s.chars() {
        match c {
            '"' => out.push_str("\\u0022"),
            '\\' => out.push_str("\\\\"),
            '\n' => out.push_str("\\n"),
            '\r' => out.push_str("\\r"),
            '\t' => out.push_str("\\t"),
            '\u{8}' => out.push_str("\\b"),
            '\u{c}' => out.push_str("\\f"),
            // JsonEncodedText escapes the HTML-sensitive characters too, so the result is safe
            // to drop straight into a <script> block.
            '<' => out.push_str("\\u003C"),
            '>' => out.push_str("\\u003E"),
            '&' => out.push_str("\\u0026"),
            '\'' => out.push_str("\\u0027"),
            '+' => out.push_str("\\u002B"),
            c if (c as u32) < 0x20 => out.push_str(&format!("\\u{:04X}", c as u32)),
            c => out.push(c),
        }
    }
    out.push('"');
    out
}

/// SQL string literal: doubles the quotes. Escaping is not a substitute for parameterised
/// queries — this is for pasting a value into a console.
pub fn sql(s: &str) -> String {
    format!("'{}'", s.replace('\'', "''"))
}

/// POSIX shell literal: single quotes, with the standard '"'"' dance inside.
pub fn shell(s: &str) -> String {
    format!("'{}'", s.replace('\'', "'\"'\"'"))
}

/// Windows and Unix path separators, direction detected from the input.
pub fn path(s: &str) -> String {
    let t = s.trim();
    if t.contains('\\') { t.replace('\\', "/") } else { t.replace('/', "\\") }
}

const INVALID_FILENAME: [char; 9] = ['"', '<', '>', '|', ':', '*', '?', '\\', '/'];

/// Makes a filename Windows-safe: forbidden characters, trailing dots and reserved device
/// names (CON, NUL…) all handled.
pub fn filename(s: &str) -> String {
    let cleaned: String = s
        .trim()
        .chars()
        .map(|c| if INVALID_FILENAME.contains(&c) || (c as u32) < 0x20 { '_' } else { c })
        .collect();
    let name = cleaned.trim_end_matches([' ', '.']);
    if name.is_empty() {
        return "_".to_string();
    }

    let stem = name.rsplit_once('.').map_or(name, |(head, _)| head);
    let mut reserved = vec!["CON".to_string(), "PRN".to_string(), "AUX".to_string(), "NUL".to_string()];
    for i in 1..=9 {
        reserved.push(format!("COM{i}"));
        reserved.push(format!("LPT{i}"));
    }
    if reserved.iter().any(|r| r.eq_ignore_ascii_case(stem)) {
        format!("_{name}")
    } else {
        name.to_string()
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn json_quotes_and_escapes() {
        assert_eq!(json("hi"), "\"hi\"");
        assert_eq!(json("a\nb"), "\"a\\nb\"");
        assert_eq!(json("tab\there"), "\"tab\\there\"");
        assert!(json("</script>").contains("\\u003C"), "HTML-sensitive chars are escaped");
    }

    #[test]
    fn sql_doubles_quotes() {
        assert_eq!(sql("O'Brien"), "'O''Brien'");
        assert_eq!(sql("plain"), "'plain'");
    }

    #[test]
    fn shell_survives_embedded_quotes() {
        assert_eq!(shell("plain"), "'plain'");
        assert_eq!(shell("it's"), "'it'\"'\"'s'");
    }

    #[test]
    fn path_flips_whichever_way_it_came() {
        assert_eq!(path(r"C:\a\b"), "C:/a/b");
        assert_eq!(path("C:/a/b"), r"C:\a\b");
    }

    #[test]
    fn filename_strips_what_windows_rejects() {
        assert_eq!(filename("a:b*c?.txt"), "a_b_c_.txt");
        assert_eq!(filename("trailing..."), "trailing");
        assert_eq!(filename("   "), "_");
        assert_eq!(filename("CON"), "_CON", "reserved device name");
        assert_eq!(filename("con.txt"), "_con.txt", "reserved even with an extension");
        assert_eq!(filename("normal.txt"), "normal.txt");
    }
}
