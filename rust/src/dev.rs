//! Developer utilities. Mirrors the corresponding parts of `Krate.Core.Dev` and `Escapes`.

use crate::i18n;
use crate::tools::url_decode;

/// Normalises to CRLF regardless of what the input already used.
pub fn to_crlf(s: &str) -> String {
    s.replace("\r\n", "\n").replace('\n', "\r\n")
}

pub fn to_lf(s: &str) -> String {
    s.replace("\r\n", "\n")
}

/// Octal to symbolic, or symbolic back to octal — direction detected from the input.
pub fn chmod(input: &str) -> Result<String, String> {
    let s = input.trim();

    let is_octal = (3..=4).contains(&s.len()) && s.chars().all(|c| ('0'..='7').contains(&c));
    if is_octal {
        let value = i32::from_str_radix(s, 8).map_err(|_| i18n::get("Error_ChmodUsage").to_string())?;
        let mut out = String::new();
        if s.len() == 4 {
            if value & 0o4000 != 0 {
                out.push_str("suid ");
            }
            if value & 0o2000 != 0 {
                out.push_str("sgid ");
            }
            if value & 0o1000 != 0 {
                out.push_str("sticky ");
            }
        }
        let perm = |v: i32| {
            format!(
                "{}{}{}",
                if v & 4 != 0 { "r" } else { "-" },
                if v & 2 != 0 { "w" } else { "-" },
                if v & 1 != 0 { "x" } else { "-" }
            )
        };
        out.push_str(&perm((value >> 6) & 7));
        out.push_str(&perm((value >> 3) & 7));
        out.push_str(&perm(value & 7));
        return Ok(out.trim().to_string());
    }

    // Symbolic: exactly three rwx triples.
    let chars: Vec<char> = s.chars().collect();
    let symbolic = chars.len() == 9
        && chars.chunks(3).all(|c| {
            matches!(c[0], 'r' | '-') && matches!(c[1], 'w' | '-') && matches!(c[2], 'x' | '-')
        });
    if symbolic {
        let mut value = 0;
        for (i, c) in chars.iter().enumerate() {
            if *c != '-' {
                value |= 1 << (8 - i);
            }
        }
        return Ok(format!("{value:03o}"));
    }

    Err(i18n::get("Error_ChmodUsage").to_string())
}

/// Splits a query string (or a whole URL) into its decoded key/value pairs.
pub fn query_string(input: &str) -> String {
    let s = input.trim();
    let start = s.find('?');
    let query = match start {
        Some(i) => &s[i + 1..],
        None => s,
    };

    let pairs = query
        .split(['&', ';'])
        .filter(|p| !p.is_empty())
        .map(|pair| match pair.split_once('=') {
            // '+' means space in a query string, but only in the value, matching the C# side.
            Some((k, v)) => format!("{}  =  {}", url_decode(k), url_decode(&v.replace('+', " "))),
            None => format!("{}  =  ", url_decode(pair)),
        });

    // The URL header only appears when there was something before the '?'.
    let mut lines: Vec<String> = Vec::new();
    if let Some(i) = start {
        if i > 0 {
            lines.push(format!("URL   {}", &s[..i]));
            lines.push(String::new());
        }
    }
    lines.extend(pairs);
    lines.join("\n")
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn line_endings_normalise_in_both_directions() {
        assert_eq!(to_crlf("a\nb"), "a\r\nb");
        assert_eq!(to_crlf("a\r\nb"), "a\r\nb", "already CRLF stays CRLF, not CRCRLF");
        assert_eq!(to_lf("a\r\nb"), "a\nb");
        assert_eq!(to_lf(&to_crlf("a\nb\nc")), "a\nb\nc");
    }

    #[test]
    fn chmod_converts_octal_to_symbolic() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        assert_eq!(chmod("755").unwrap(), "rwxr-xr-x");
        assert_eq!(chmod("644").unwrap(), "rw-r--r--");
        assert_eq!(chmod("000").unwrap(), "---------");
        assert_eq!(chmod("777").unwrap(), "rwxrwxrwx");
    }

    #[test]
    fn chmod_reads_the_special_bits_from_a_four_digit_mode() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        assert_eq!(chmod("4755").unwrap(), "suid rwxr-xr-x");
        assert_eq!(chmod("1777").unwrap(), "sticky rwxrwxrwx");
    }

    #[test]
    fn chmod_converts_symbolic_back_to_octal() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        assert_eq!(chmod("rwxr-xr-x").unwrap(), "755");
        assert_eq!(chmod("rw-r--r--").unwrap(), "644");
        assert_eq!(chmod("---------").unwrap(), "000");
    }

    #[test]
    fn chmod_rejects_anything_else() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        for bad in ["", "8", "999", "rwx", "zzzzzzzzz", "12345"] {
            assert!(chmod(bad).is_err(), "{bad:?} should not parse");
        }
    }

    #[test]
    fn query_string_decodes_pairs() {
        let result = query_string("a=1&b=hello%20world");
        assert!(result.contains("a  =  1"), "{result}");
        assert!(result.contains("b  =  hello world"), "{result}");
    }

    #[test]
    fn query_string_shows_the_url_only_when_there_is_one() {
        let with_url = query_string("https://x.com/p?a=1");
        assert!(with_url.starts_with("URL   https://x.com/p"), "{with_url}");
        let bare = query_string("a=1&b=2");
        assert!(!bare.contains("URL"), "{bare}");
        // '+' is a space in a query value.
        assert!(query_string("q=a+b").contains("q  =  a b"));
    }
}
