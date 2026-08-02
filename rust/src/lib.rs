//! Krate Toolkit — core logic.
//!
//! Named "krate" rather than "crate" because `crate` is a reserved keyword in Rust and would be
//! unusable as a package name. No UI in here: the WinUI shell reaches it through `ffi`, and
//! Android will reach the same functions over JNI.

pub mod calc;
pub mod collate;
pub mod colors;
pub mod convert;
pub mod csprng;
pub mod data;
pub mod dev;
pub mod diff;
pub mod duration;
pub mod everyday;
pub mod escapes;
pub mod ffi;
pub mod fancy;
pub mod files;
pub mod generators;
pub mod hashing;
pub mod http;
pub mod i18n;
pub mod images;
pub mod json;
pub mod markdown;
pub mod maths;
pub mod net;
pub mod numbers;
pub mod pdf;
pub mod physics;
pub mod regex;
pub mod security;
pub mod strip;
pub mod system;
pub mod text;
pub mod timezone;
pub mod tokens;
pub mod tools;
pub mod typography;
pub mod unicode;
pub mod units;
mod categories;
mod cultures;
pub mod archives;
pub mod civil;
pub mod clock;
pub mod codes;
pub mod cron;
pub mod crypt;
pub mod currency;
pub mod dates;
pub mod decode;
mod entities;
pub mod exif;
pub mod md;
pub mod web;
pub mod words;
pub mod xml;

pub use tools::{Tool, catalog, find, run};

#[cfg(test)]
mod source_hygiene {
    /// Windows PowerShell's `Get-Content`/`Set-Content` decode UTF-8 as the ANSI codepage unless
    /// told otherwise, silently double-encoding every non-ASCII character. That corrupted 5331
    /// strings in the C# resources once, and mangled this crate's comments once more during the
    /// port. Neither time did anything fail to build — the text just rendered as noise forever.
    ///
    /// Markers are written as escapes rather than literals so this file does not trip its own
    /// check: they are the leading bytes UTF-8 produces for accented, Cyrillic and Turkish text
    /// when it is read back through cp1252.
    #[test]
    fn sources_are_utf8_without_bom_or_double_encoding() {
        let markers = ["\u{c3}\u{a9}", "\u{e2}\u{20ac}", "\u{d0}\u{9f}", "\u{c4}\u{b0}"];

        for entry in std::fs::read_dir("src").expect("src must be readable") {
            let path = entry.unwrap().path();
            if path.extension().is_none_or(|e| e != "rs") {
                continue;
            }
            let bytes = std::fs::read(&path).unwrap();
            assert_ne!(&bytes[..3.min(bytes.len())], b"\xef\xbb\xbf", "{path:?} has a UTF-8 BOM");

            let text = String::from_utf8(bytes).unwrap_or_else(|e| panic!("{path:?} is not UTF-8: {e}"));
            for marker in markers {
                assert!(
                    !text.contains(marker),
                    "{path:?} looks double-encoded (found {:?})",
                    marker.escape_unicode().to_string()
                );
            }
        }
    }
}
