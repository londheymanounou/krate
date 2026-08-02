//! JSON formatting, minifying and validation. Mirrors `Krate.Core.Json`.
//!
//! serde_json parses, but it does **not** serialise: `System.Text.Json` has two behaviours that
//! a stock serde round-trip would silently change, and both are user-visible.
//!
//!  * It escapes far more than the JSON spec requires — every non-ASCII character plus the
//!    HTML-sensitive set — as upper-case `\uXXXX`. serde emits raw UTF-8.
//!  * `JsonDocument.WriteTo` copies number tokens through **verbatim**, so `1e3` stays `1e3`
//!    and `1.0` stays `1.0`. `arbitrary_precision` keeps the significant digits, but serde still
//!    rewrites the spelling — `1e3` to `1e+3`, `-0` to `0` — and no API on `Value` gives the
//!    original back. `RawNumbers` recovers it from the source instead.
//!
//! So the writer below is hand-rolled to match. Parsing stays serde's job, which is the part
//! worth not reinventing.
//!
//! serde_json is built with **`preserve_order`**. Without it `Value`'s map is a BTreeMap and
//! object keys come back alphabetically — reordering the user's document. The original tests
//! missed this because their keys were already in alphabetical order.

use crate::i18n;
use crate::tools::newline;
use serde_json::Value;

/// The characters .NET's default JavaScriptEncoder escapes beyond the JSON minimum. Measured
/// against the C# build: `=`, `/`, `!`, `*`, `$` and `%` are *not* escaped, but these are.
fn needs_escape(c: char) -> bool {
    matches!(c, '"' | '\\' | '<' | '>' | '&' | '\'' | '+' | '`') || (c as u32) < 0x20 || (c as u32) > 0x7E
}

fn write_string(out: &mut String, s: &str) {
    out.push('"');
    for c in s.chars() {
        match c {
            '\\' => out.push_str("\\\\"),
            '\n' => out.push_str("\\n"),
            '\r' => out.push_str("\\r"),
            '\t' => out.push_str("\\t"),
            '\u{8}' => out.push_str("\\b"),
            '\u{c}' => out.push_str("\\f"),
            c if needs_escape(c) => {
                // Astral characters are escaped as the UTF-16 surrogate pair, as .NET does.
                let mut buffer = [0u16; 2];
                for unit in c.encode_utf16(&mut buffer) {
                    out.push_str(&format!("\\u{unit:04X}"));
                }
            }
            c => out.push(c),
        }
    }
    out.push('"');
}

/// The source's number tokens, in document order.
///
/// serde_json rewrites number literals as it parses — `1e3` becomes `1e+3`, `-0` becomes `0` —
/// and the original text is unrecoverable from a `Value`, `Number::as_str` included. .NET's
/// `JsonDocument.WriteTo` copies the token through byte for byte, so a formatter that used
/// serde's spelling would quietly edit the user's document.
///
/// Emission walks the document in order (objects keep theirs via `preserve_order`), so the k-th
/// number written is the k-th token found here. The scan only ever runs on input serde has
/// already accepted, which is what makes it safe to be this simple: in valid JSON a run of
/// `[-+0-9.eE]` starting at `-` or a digit, outside a string, is exactly one number.
pub(crate) struct RawNumbers {
    tokens: Vec<String>,
    next: usize,
}

impl RawNumbers {
    /// For callers with no source text — `data.rs` builds its values from CSV.
    pub(crate) fn none() -> Self {
        Self { tokens: Vec::new(), next: 0 }
    }

    pub(crate) fn scan(input: &str) -> Self {
        let bytes = input.as_bytes();
        let mut tokens = Vec::new();
        let mut i = 0;
        while i < bytes.len() {
            match bytes[i] {
                b'"' => {
                    // Skip the string, honouring escapes so an embedded quote does not end it.
                    i += 1;
                    while i < bytes.len() && bytes[i] != b'"' {
                        i += if bytes[i] == b'\\' { 2 } else { 1 };
                    }
                    i += 1;
                }
                b'-' | b'0'..=b'9' => {
                    let start = i;
                    while i < bytes.len()
                        && matches!(bytes[i], b'0'..=b'9' | b'-' | b'+' | b'.' | b'e' | b'E')
                    {
                        i += 1;
                    }
                    tokens.push(input[start..i].to_string());
                }
                _ => i += 1,
            }
        }
        Self { tokens, next: 0 }
    }

    /// The next token, or None to fall back to serde's spelling.
    fn take(&mut self) -> Option<&str> {
        let token = self.tokens.get(self.next)?;
        self.next += 1;
        Some(token)
    }

    /// Owned form, for callers building a string rather than appending to one.
    pub(crate) fn take_next(&mut self) -> Option<String> {
        self.take().map(str::to_string)
    }
}

/// Re-exported for `data.rs`: CSV-to-JSON must produce the same escaping and indentation as
/// every other JSON output, so it shares this writer rather than duplicating the rules.
pub fn write_value_public(out: &mut String, value: &Value, indent: Option<usize>, depth: usize) {
    write_value(out, value, indent, depth, &mut RawNumbers::none())
}

fn write_value(
    out: &mut String,
    value: &Value,
    indent: Option<usize>,
    depth: usize,
    numbers: &mut RawNumbers,
) {
    let pad = |out: &mut String, depth: usize| {
        if let Some(width) = indent {
            // Utf8JsonWriter's indented output uses Environment.NewLine, so it is CRLF on
            // Windows. Mirror the platform rather than hard-coding either one.
            out.push_str(newline());
            out.push_str(&" ".repeat(width * depth));
        }
    };

    match value {
        Value::Null => out.push_str("null"),
        Value::Bool(b) => out.push_str(if *b { "true" } else { "false" }),
        // The source token verbatim, exactly as JsonDocument.WriteTo copies it through.
        Value::Number(n) => match numbers.take() {
            Some(raw) => out.push_str(raw),
            None => out.push_str(&n.to_string()),
        },
        Value::String(s) => write_string(out, s),
        Value::Array(items) => {
            if items.is_empty() {
                out.push_str("[]");
                return;
            }
            out.push('[');
            for (i, item) in items.iter().enumerate() {
                if i > 0 {
                    out.push(',');
                }
                pad(out, depth + 1);
                write_value(out, item, indent, depth + 1, numbers);
            }
            pad(out, depth);
            out.push(']');
        }
        Value::Object(fields) => {
            if fields.is_empty() {
                out.push_str("{}");
                return;
            }
            out.push('{');
            for (i, (key, item)) in fields.iter().enumerate() {
                if i > 0 {
                    out.push(',');
                }
                pad(out, depth + 1);
                write_string(out, key);
                out.push(':');
                if indent.is_some() {
                    out.push(' ');
                }
                write_value(out, item, indent, depth + 1, numbers);
            }
            pad(out, depth);
            out.push('}');
        }
    }
}

fn parse(json: &str) -> Result<Value, serde_json::Error> {
    serde_json::from_str(json)
}

/// serde reports column 0 when the input ends before any token; .NET's BytePositionInLine+1
/// is always at least 1. Normalise so both describe the same place the same way.
pub(crate) fn locate_error(e: &serde_json::Error) -> String {
    locate(e)
}

fn locate(e: &serde_json::Error) -> String {
    i18n::format(
        "Json_Invalid",
        &[&e.line().max(1).to_string(), &e.column().max(1).to_string()],
    )
}

fn write(json: &str, indent: Option<usize>) -> Result<String, String> {
    let value = parse(json).map_err(|e| locate(&e))?;
    let mut out = String::with_capacity(json.len());
    write_value(&mut out, &value, indent, 0, &mut RawNumbers::scan(json));
    Ok(out)
}

pub fn format(json: &str) -> Result<String, String> {
    write(json, Some(2))
}

pub fn minify(json: &str) -> Result<String, String> {
    write(json, None)
}

/// Says where the problem is — a validator that only says "invalid" is useless.
pub fn validate(json: &str) -> Result<String, String> {
    match parse(json) {
        Ok(_) => Ok(i18n::get("Json_Valid").to_string()),
        Err(e) => Ok(locate(&e)),
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn minify_strips_all_insignificant_space() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        assert_eq!(minify("{ \"a\": [ 1, 2 ] }").unwrap(), r#"{"a":[1,2]}"#);
        assert_eq!(minify("[]").unwrap(), "[]");
        assert_eq!(minify("{}").unwrap(), "{}");
    }

    /// Two-space indent, and the platform newline — `Utf8JsonWriter` follows
    /// `Environment.NewLine`, so indented output is CRLF on Windows.
    #[test]
    fn format_indents_two_spaces_with_the_platform_newline() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        let nl = if cfg!(windows) { "\r\n" } else { "\n" };
        assert_eq!(
            format(r#"{"a":[1,2],"b":{"c":true}}"#).unwrap(),
            format!("{{{nl}  \"a\": [{nl}    1,{nl}    2{nl}  ],{nl}  \"b\": {{{nl}    \"c\": true{nl}  }}{nl}}}")
        );
    }

    /// Without serde_json's `preserve_order` this returns the keys alphabetically, silently
    /// reordering the user's document. The earlier test used already-sorted keys and missed it.
    #[test]
    fn object_key_order_is_preserved() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        assert_eq!(minify(r#"{"zebra":1,"apple":2,"mango":3}"#).unwrap(),
                   r#"{"zebra":1,"apple":2,"mango":3}"#);
        let pretty = format(r#"{"z":1,"a":2}"#).unwrap();
        assert!(pretty.find("\"z\"").unwrap() < pretty.find("\"a\"").unwrap(), "{pretty}");
    }

    /// The whole reason this module has a hand-written writer.
    #[test]
    fn escaping_matches_dotnet_not_the_json_minimum() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        // Every one of these is escaped by System.Text.Json even though JSON does not require it.
        // Expected values are spelled with \u escapes because that is literally what .NET emits.
        assert_eq!(minify(r#"{"k":"café"}"#).unwrap(), "{\"k\":\"caf\\u00E9\"}");
        assert_eq!(minify(r#"{"k":"<b>"}"#).unwrap(), "{\"k\":\"\\u003Cb\\u003E\"}");
        assert_eq!(minify(r#"{"k":"a&b"}"#).unwrap(), "{\"k\":\"a\\u0026b\"}");
        assert_eq!(minify(r#"{"k":"a+b"}"#).unwrap(), "{\"k\":\"a\\u002Bb\"}");
        assert_eq!(minify("{\"k\":\"a`b\"}").unwrap(), "{\"k\":\"a\\u0060b\"}");
        assert_eq!(minify(r#"{"k":"日本"}"#).unwrap(), "{\"k\":\"\\u65E5\\u672C\"}");
        // These are deliberately left alone by .NET.
        assert_eq!(minify(r#"{"k":"a=b/c!d*e$f%g"}"#).unwrap(), r#"{"k":"a=b/c!d*e$f%g"}"#);
        assert_eq!(minify("{\"k\":\"tab\\there\"}").unwrap(), r#"{"k":"tab\there"}"#);
    }

    /// `JsonDocument.WriteTo` copies number tokens verbatim, so decimals, precision and the
    /// exact spelling all survive. This used to be a documented divergence — serde renders `1e3`
    /// as `1e+3` and `-0` as `0` — until `RawNumbers` started recovering the source token.
    #[test]
    fn numbers_keep_their_precision() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        assert_eq!(minify(r#"{"a":1.0,"c":0.1}"#).unwrap(), r#"{"a":1.0,"c":0.1}"#);
        assert_eq!(minify(r#"{"a":1.50,"b":100}"#).unwrap(), r#"{"a":1.50,"b":100}"#);
        assert_eq!(
            minify(r#"{"big":12345678901234567890}"#).unwrap(),
            r#"{"big":12345678901234567890}"#,
            "precision beyond f64 must not be rounded"
        );
        // The spellings serde would have rewritten.
        assert_eq!(minify(r#"{"e":1e3}"#).unwrap(), r#"{"e":1e3}"#);
        assert_eq!(minify(r#"{"e":1E3,"f":-0,"g":1.5e300}"#).unwrap(), r#"{"e":1E3,"f":-0,"g":1.5e300}"#);
        // A number-shaped string must not be mistaken for a token to substitute.
        assert_eq!(minify(r#"{"a":"1e3","b":2}"#).unwrap(), r#"{"a":"1e3","b":2}"#);
        // An escaped quote inside a string must not desynchronise the token cursor.
        assert!(minify(r#"{"a":"say \"1e9\"","b":-2.5e-3}"#).unwrap().ends_with(r#""b":-2.5e-3}"#));
    }

    #[test]
    fn validate_reports_valid_and_locates_errors() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        assert_eq!(validate(r#"{"a":1}"#).unwrap(), "Valid JSON.");
        let bad = validate(r#"{"a":}"#).unwrap();
        assert!(bad.contains("line 1"), "{bad}");
        assert!(!bad.contains("Json_Invalid"), "the key leaked instead of the text");
    }

    #[test]
    fn malformed_input_is_an_error_for_format_and_minify() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        assert!(minify("{").is_err());
        assert!(format("nonsense").is_err());
    }
}
