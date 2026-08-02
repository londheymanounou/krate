//! Plain / scientific / engineering notation, and hex dumps. Mirrors the numeric parts of
//! `Krate.Core.Escapes` and `Dev.HexDump`.

use crate::i18n;
use crate::tools::{format_decimal, newline};

/// .NET's `"0.##############E+0"`: mantissa to at most 14 decimals, then `E`, an explicit sign,
/// and **no zero padding** on the exponent — `1E+0`, `1.23E-4`, `6.022E+23`.
fn scientific_notation(v: f64) -> String {
    if v == 0.0 {
        return "0E+0".to_string();
    }
    let exponent = v.abs().log10().floor() as i32;
    let mantissa = v / 10f64.powi(exponent);
    let sign = if exponent < 0 { '-' } else { '+' };
    format!("{}E{sign}{}", format_decimal(mantissa, 14), exponent.abs())
}

/// Exponent forced to a multiple of three, lower-case `e`, no sign on the exponent.
fn engineering_notation(v: f64) -> String {
    if v == 0.0 {
        return "0e0".to_string();
    }
    let exponent = (v.abs().log10() / 3.0).floor() as i32 * 3;
    format!("{}e{exponent}", format_decimal(v / 10f64.powi(exponent), 10))
}

/// One number in plain, scientific and engineering notation.
pub fn scientific(input: &str) -> Result<String, String> {
    let v: f64 = input
        .trim()
        .replace(' ', "")
        .parse()
        .map_err(|_| i18n::get("Error_NeedNumber").to_string())?;
    Ok([
        format!("PLAIN       {}", format_decimal(v, 30)),
        format!("SCIENTIFIC  {}", scientific_notation(v)),
        format!("ENGINEERING {}", engineering_notation(v)),
    ]
    .join("\n"))
}

/// Turns a JSON string literal back into plain text. Quotes are optional on the way in.
pub fn json_unescape(s: &str) -> Result<String, String> {
    let mut t = s.trim().to_string();
    if !t.starts_with('"') {
        t = format!("\"{t}\"");
    }
    match serde_json::from_str::<String>(&t) {
        Ok(text) => Ok(text),
        Err(e) => Err(i18n::format(
            "Json_Invalid",
            &[&e.line().max(1).to_string(), &e.column().max(1).to_string()],
        )),
    }
}

/// Only the first chunk of a file is dumped; a hex view of a gigabyte helps nobody.
pub const HEX_DUMP_LIMIT: usize = 64 * 1024;

/// Classic `offset  hex  |ascii|` dump. A path is read as a file; anything else is dumped as
/// its own UTF-8 bytes.
pub fn hex_dump(input: &str) -> String {
    let trimmed = input.trim().trim_matches('"');
    let file = std::fs::read(trimmed);
    let truncated = file.as_ref().is_ok_and(|d| d.len() > HEX_DUMP_LIMIT);
    let bytes: Vec<u8> = match file {
        Ok(data) => data.into_iter().take(HEX_DUMP_LIMIT).collect(),
        Err(_) => input.as_bytes().to_vec(),
    };

    let mut out = String::new();
    for (offset, row) in bytes.chunks(16).enumerate().map(|(i, r)| (i * 16, r)) {
        let mut hex = String::new();
        let mut ascii = String::new();
        for i in 0..16 {
            match row.get(i) {
                Some(b) => {
                    hex.push_str(&format!("{b:02x} "));
                    ascii.push(if (0x20..0x7F).contains(b) { *b as char } else { '.' });
                }
                None => hex.push_str("   "),
            }
            if i == 7 {
                hex.push(' '); // the traditional gap after 8 bytes
            }
        }
        // The C# builds rows with AppendLine, i.e. Environment.NewLine, not a bare \n.
        out.push_str(&format!("{offset:08x}  {hex}|{ascii}|{}", newline()));
    }
    if truncated {
        out.push_str(&i18n::format("Files_TreeTruncated", &[&HEX_DUMP_LIMIT.to_string()]));
    }
    // The C# side trims the final newline, so the value has no trailing blank line.
    out.trim_end_matches(['\n', '\r']).to_string()
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn scientific_matches_the_dotnet_format_strings() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        let a = scientific("1234.5").unwrap();
        assert!(a.contains("PLAIN       1234.5"), "{a}");
        assert!(a.contains("SCIENTIFIC  1.2345E+3"), "{a}");
        assert!(a.contains("ENGINEERING 1.2345e3"), "{a}");
    }

    /// The exponent carries a sign but no zero padding, unlike the `\\uXXXX`-style formats
    /// elsewhere — `1E+0`, not `1E+00`.
    #[test]
    fn scientific_handles_zero_negatives_and_large_values() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        assert!(scientific("0").unwrap().contains("SCIENTIFIC  0E+0"));
        assert!(scientific("1").unwrap().contains("SCIENTIFIC  1E+0"));
        assert!(scientific("-42").unwrap().contains("SCIENTIFIC  -4.2E+1"));
        assert!(scientific("0.000123").unwrap().contains("SCIENTIFIC  1.23E-4"));
        assert!(scientific("zzz").is_err());
    }

    /// Engineering notation always lands on a multiple of three.
    #[test]
    fn engineering_uses_thousands_steps() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        assert!(scientific("0.000123").unwrap().contains("ENGINEERING 123e-6"));
        assert!(scientific("1e20").unwrap().contains("ENGINEERING 100e18"));
        assert!(scientific("6.022e23").unwrap().contains("ENGINEERING 602.2e21"));
        assert!(scientific("0").unwrap().contains("ENGINEERING 0e0"));
    }

    #[test]
    fn json_unescape_accepts_quoted_and_bare_input() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        assert_eq!(json_unescape(r#""a\nb""#).unwrap(), "a\nb");
        assert_eq!(json_unescape(r#"a\tb"#).unwrap(), "a\tb", "quotes are optional");
        assert_eq!(json_unescape(r#""café""#).unwrap(), "café");
        assert!(json_unescape(r#""unterminated"#).is_err());
    }

    #[test]
    fn hex_dump_lays_out_sixteen_bytes_a_row() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        let dump = hex_dump("hello");
        assert_eq!(dump.lines().count(), 1);
        assert!(!dump.ends_with('\n'), "no trailing newline, matching the C# TrimEnd");
        let line = dump.lines().next().unwrap();
        assert!(line.starts_with("00000000  68 65 6c 6c 6f"), "{line}");
        assert!(line.ends_with("|hello|"), "{line}");
    }

    #[test]
    fn hex_dump_pads_short_rows_and_dots_non_printables() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        let dump = hex_dump("0123456789abcdefX");
        assert_eq!(dump.lines().count(), 2, "17 bytes spills to a second row");
        // Every row is the same width, so the ascii columns line up.
        let widths: Vec<usize> = dump.lines().map(|l| l.find('|').unwrap()).collect();
        assert_eq!(widths[0], widths[1], "short rows must be padded");
        assert!(hex_dump("a\u{0}b").contains("|a.b|"), "control bytes show as dots");
    }
}
