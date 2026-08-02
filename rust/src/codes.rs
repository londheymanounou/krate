//! QR codes and Code 128 barcodes, rendered as Unicode blocks. Mirrors `Krate.Core.Qr` and
//! `Krate.Core.Barcode`.
//!
//! The barcode encoding is hand-rolled on both sides — start code, per-symbol widths, mod-103
//! checksum, stop — so it ports directly. The QR encoding is not hand-rolled anywhere: the C# uses
//! QRCoder, this uses the `qrcode` crate. Both follow the spec, which pins version selection, mode
//! selection and the mask choice (lowest penalty score), so the module matrices agree — the parity
//! test is what proves that rather than assumption.
//!
//! One thing to know: QRCoder's `ModuleMatrix` **includes a 4-module quiet zone**, so a version-1
//! code is 29x29 there and 21x21 from the `qrcode` crate. The border is added here to match.
//!
//! **KNOWN DIVERGENCE — the mask pattern.** The two libraries pick the same version, the same
//! encoding mode and the same error correction, but not always the same mask: `hi` and `A` come out
//! module-for-module identical, while `HELLO` and `12345` differ in about 13% of modules. The spec
//! says to choose the mask with the lowest penalty score, and the two implementations score
//! differently when it is close. Both results are valid, scannable QR codes carrying the same data.
//!
//! Rather than match QRCoder's scoring — which would mean copying whichever of the two is wrong —
//! the tests decode the output with an independent decoder and assert it reads back as the input.
//! That proves correctness, which is stronger than proving sameness. `Qr` is therefore absent from
//! the byte-parity row; `RustParityTests.Qr_AgreesOnShape` compares version and dimensions.

use crate::i18n;

/// The 107 Code 128 bar/space width patterns, indexed by symbol value, then the 7-module stop.
const PATTERNS: [&str; 107] = [
    "212222", "222122", "222221", "121223", "121322", "131222", "122213", "122312", "132212", "221213",
    "221312", "231212", "112232", "122132", "122231", "113222", "123122", "123221", "223211", "221132",
    "221231", "213212", "223112", "312131", "311222", "321122", "321221", "312212", "322112", "322211",
    "212123", "212321", "232121", "111323", "131123", "131321", "112313", "132113", "132311", "211313",
    "231113", "231311", "112133", "112331", "132131", "113123", "113321", "133121", "313121", "211331",
    "231131", "213113", "213311", "213131", "311123", "311321", "331121", "312113", "312311", "332111",
    "314111", "221411", "431111", "111224", "111422", "121124", "121421", "141122", "141221", "112214",
    "112412", "122114", "122411", "142112", "142211", "241211", "221114", "413111", "241112", "134111",
    "111242", "121142", "121241", "114212", "124112", "124211", "411212", "421112", "421211", "212141",
    "214121", "412121", "111143", "111341", "131141", "114113", "114311", "411113", "411311", "113141",
    "114131", "311141", "411131", "211412", "211214", "211232", "2331112",
];

const START_B: i32 = 104;
const STOP: i32 = 106;

/// The mod-103 checksum symbol value, Code 128 subset B.
pub fn checksum(text: &str) -> i32 {
    let mut sum = START_B;
    for (index, c) in text.chars().enumerate() {
        sum += (c as i32 - 32) * (index as i32 + 1);
    }
    sum % 103
}

/// Start, data, checksum, stop.
pub fn symbols(text: &str) -> Vec<i32> {
    let mut values = vec![START_B];
    values.extend(text.chars().map(|c| c as i32 - 32));
    values.push(checksum(text));
    values.push(STOP);
    values
}

/// Code 128 (subset B) as Unicode block rows with a quiet zone, scannable in a monospace font.
pub fn code128(text: &str) -> Result<String, String> {
    if text.is_empty() {
        return Err(i18n::get("Error_NeedText").to_string());
    }
    // Subset B covers printable ASCII only.
    if text.chars().any(|c| !(' '..='~').contains(&c)) {
        return Err(i18n::get("Error_BarcodeAscii").to_string());
    }

    let mut modules = String::new();
    modules.push_str(&" ".repeat(10)); // quiet zone
    for symbol in symbols(text) {
        // Every pattern starts with a bar, then alternates.
        let mut bar = true;
        for width in PATTERNS[symbol as usize].chars() {
            let count = width as usize - '0' as usize;
            modules.push_str(&(if bar { '\u{2588}' } else { ' ' }).to_string().repeat(count));
            bar = !bar;
        }
    }
    modules.push_str(&" ".repeat(10));

    // Four identical rows, to give it height.
    Ok(vec![modules; 4].join("\n"))
}

/// The module matrix including the 4-module quiet zone, true meaning a dark module.
fn matrix(text: &str) -> Result<Vec<Vec<bool>>, String> {
    // ECC level M, as the C# asks QRCoder for.
    let code = qrcode::QrCode::with_error_correction_level(text.as_bytes(), qrcode::EcLevel::M)
        .map_err(|_| i18n::get("Error_NeedText").to_string())?;
    let width = code.width();
    let dark: Vec<bool> = code
        .to_colors()
        .into_iter()
        .map(|c| c == qrcode::types::Color::Dark)
        .collect();

    const QUIET: usize = 4;
    let side = width + QUIET * 2;
    let mut out = vec![vec![false; side]; side];
    for y in 0..width {
        for x in 0..width {
            out[y + QUIET][x + QUIET] = dark[y * width + x];
        }
    }
    Ok(out)
}

/// QR rendered with Unicode half-blocks: two module rows per character, so it stays roughly square
/// in a monospace font.
pub fn unicode(text: &str) -> Result<String, String> {
    if text.is_empty() {
        return Err(i18n::get("Error_NeedText").to_string());
    }
    let modules = matrix(text)?;
    let mut out = String::new();
    for y in (0..modules.len()).step_by(2) {
        for x in 0..modules[y].len() {
            let top = modules[y][x];
            let bottom = y + 1 < modules.len() && modules[y + 1][x];
            // Printed on a light background, so a dark module is a gap, not a block.
            out.push(match (top, bottom) {
                (false, false) => '\u{2588}', // full block
                (false, true) => '\u{2580}',  // upper half
                (true, false) => '\u{2584}',  // lower half
                (true, true) => ' ',
            });
        }
        out.push('\n');
    }
    Ok(out.trim_end_matches('\n').to_string())
}

#[cfg(test)]
mod tests {
    use super::*;

    fn english() -> std::sync::MutexGuard<'static, ()> {
        let guard = crate::i18n::test_lock();
        i18n::set_language("en");
        guard
    }

    /// Every pattern must be a valid width string, or a symbol renders the wrong number of modules.
    #[test]
    fn the_pattern_table_is_well_formed() {
        assert_eq!(PATTERNS.len(), 107);
        for (index, pattern) in PATTERNS.iter().enumerate() {
            assert!(pattern.chars().all(|c| ('1'..='4').contains(&c)), "{index}: {pattern}");
            let widths: usize = pattern.chars().map(|c| c as usize - '0' as usize).sum();
            // Every symbol is 11 modules wide; the stop pattern is 13.
            let expected = if index == 106 { 13 } else { 11 };
            assert_eq!(widths, expected, "symbol {index} spans {widths} modules");
        }
    }

    /// Known values, so a transcription slip in the table cannot pass unnoticed.
    #[test]
    fn the_checksum_matches_worked_examples() {
        // "HI": start 104 + (72-32)*1 + (73-32)*2 = 104 + 40 + 82 = 226; 226 % 103 = 20.
        assert_eq!(checksum("HI"), 20);
        // A single space is symbol 0, so the sum is just the start code.
        assert_eq!(checksum(" "), 104 % 103);
        assert_eq!(checksum(""), 104 % 103);
    }

    #[test]
    fn the_symbol_sequence_is_start_data_checksum_stop() {
        let values = symbols("AB");
        assert_eq!(values[0], START_B);
        assert_eq!(values[1], 'A' as i32 - 32);
        assert_eq!(values[2], 'B' as i32 - 32);
        assert_eq!(values[3], checksum("AB"));
        assert_eq!(*values.last().unwrap(), STOP);
        assert_eq!(values.len(), 5);
    }

    #[test]
    fn a_barcode_has_four_identical_rows_and_quiet_zones() {
        let _guard = english();
        let rendered = code128("HI").unwrap();
        let rows: Vec<&str> = rendered.lines().collect();
        assert_eq!(rows.len(), 4);
        assert!(rows.iter().all(|r| *r == rows[0]), "the rows must be identical");
        assert!(rows[0].starts_with(&" ".repeat(10)), "leading quiet zone");
        assert!(rows[0].ends_with(&" ".repeat(10)), "trailing quiet zone");

        // 10 + 10 quiet, four symbols at 11 modules, plus the 13-module stop.
        let expected = 20 + 4 * 11 + 13;
        assert_eq!(rows[0].chars().count(), expected, "{}", rows[0].chars().count());
    }

    #[test]
    fn a_barcode_refuses_what_subset_b_cannot_encode() {
        let _guard = english();
        assert!(code128("").is_err());
        for bad in ["caf\u{e9}", "tab\there", "\u{1f600}", "line\nbreak"] {
            assert!(code128(bad).is_err(), "{bad:?}");
        }
        // The full printable ASCII range is fine.
        let all: String = (32u8..=126).map(|b| b as char).collect();
        assert!(code128(&all).is_ok());
    }

    #[test]
    fn a_qr_is_square_and_bordered() {
        let _guard = english();
        let rendered = unicode("hi").unwrap();
        let rows: Vec<&str> = rendered.lines().collect();
        // Version 1 is 21 modules, plus a 4-module quiet zone each side: 29 columns, 15 half-rows.
        assert_eq!(rows[0].chars().count(), 29, "{}", rows[0]);
        assert_eq!(rows.len(), 15);
        // The border renders as full blocks, since a light module prints solid.
        assert!(rows[0].chars().all(|c| c == '\u{2588}'), "top border: {}", rows[0]);
        assert!(rows[1].chars().all(|c| c == '\u{2588}'), "second border row: {}", rows[1]);
    }

    /// Longer input needs a bigger version, which must still come out square.
    #[test]
    fn a_qr_grows_with_its_content() {
        let _guard = english();
        let small = unicode("hi").unwrap();
        let large = unicode(&"longer content ".repeat(10)).unwrap();
        let small_width = small.lines().next().unwrap().chars().count();
        let large_width = large.lines().next().unwrap().chars().count();
        assert!(large_width > small_width, "{large_width} vs {small_width}");
        // Still square: width equals the module count, height is half of it, rounded up.
        for rendered in [&small, &large] {
            let rows: Vec<&str> = rendered.lines().collect();
            let width = rows[0].chars().count();
            assert_eq!(rows.len(), width.div_ceil(2));
            assert!(rows.iter().all(|r| r.chars().count() == width), "ragged rows");
        }
    }

    #[test]
    fn a_qr_needs_text() {
        let _guard = english();
        assert!(unicode("").is_err());
        // Non-ASCII is fine for QR, unlike the barcode.
        assert!(unicode("caf\u{e9} \u{4e2d}").is_ok());
        assert!(unicode("\u{1f600}").is_ok());
    }

    /// Renders the module matrix as a bitmap a decoder can read: 8 pixels per module, dark
    /// modules black. Scaling up matters — `rqrr` needs a few pixels per module to lock on.
    #[cfg(test)]
    fn bitmap(modules: &[Vec<bool>]) -> (usize, usize, Vec<u8>) {
        const SCALE: usize = 8;
        let side = modules.len() * SCALE;
        let mut pixels = vec![255u8; side * side];
        for (y, row) in modules.iter().enumerate() {
            for (x, dark) in row.iter().enumerate() {
                if !*dark {
                    continue;
                }
                for dy in 0..SCALE {
                    for dx in 0..SCALE {
                        pixels[(y * SCALE + dy) * side + x * SCALE + dx] = 0;
                    }
                }
            }
        }
        (side, side, pixels)
    }

    /// Turns a rendered half-block string back into a module grid, so a rendering produced
    /// elsewhere can be decoded too.
    #[cfg(test)]
    fn unrender(rendered: &str) -> Vec<Vec<bool>> {
        let mut modules = Vec::new();
        for line in rendered.lines() {
            let mut top = Vec::new();
            let mut bottom = Vec::new();
            for c in line.chars() {
                // Dark modules print as gaps, so a full block means both rows are light.
                let (t, b) = match c {
                    '\u{2588}' => (false, false),
                    '\u{2580}' => (false, true),
                    '\u{2584}' => (true, false),
                    _ => (true, true),
                };
                top.push(t);
                bottom.push(b);
            }
            modules.push(top);
            modules.push(bottom);
        }
        modules
    }

    #[cfg(test)]
    fn decode(modules: &[Vec<bool>]) -> Option<String> {
        let (width, height, pixels) = bitmap(modules);
        let mut image = rqrr::PreparedImage::prepare_from_greyscale(width, height, |x, y| {
            pixels[y * width + x]
        });
        let grids = image.detect_grids();
        let (_meta, content) = grids.first()?.decode().ok()?;
        Some(content)
    }

    /// The real check on the QR path: an independent decoder must read back exactly what went in.
    /// This is what makes the mask divergence safe to accept.
    #[test]
    fn every_qr_decodes_back_to_its_input() {
        let _guard = english();
        for text in [
            "hi",
            "HELLO",
            "12345",
            "A",
            "HELLO WORLD",
            "0123456789",
            "https://example.com",
            "The quick brown fox jumps over the lazy dog",
        ] {
            let modules = matrix(text).unwrap();
            assert_eq!(decode(&modules).as_deref(), Some(text), "{text:?} did not decode back");
        }
    }

    /// The rendered text form must be decodable too, which proves the half-block rendering is
    /// lossless. The same path was run once against renderings captured from the built C# CLI,
    /// and all three decoded to their input — which is how the mask difference was shown to be
    /// cosmetic rather than a broken code on either side.
    #[test]
    fn the_rendered_text_form_is_still_decodable() {
        let _guard = english();
        for text in ["HELLO", "12345", "hi", "https://example.com"] {
            let rendered = unicode(text).unwrap();
            let modules = unrender(&rendered);
            assert_eq!(decode(&modules).as_deref(), Some(text), "{text:?} via the rendered form");
        }
    }

    /// The half-block mapping is the whole rendering, so pin it against a known module pattern.
    #[test]
    fn half_blocks_encode_both_rows_of_each_character() {
        let _guard = english();
        let rendered = unicode("hi").unwrap();
        // Every character must be one of the four the mapping can produce.
        for c in rendered.chars().filter(|c| *c != '\n') {
            assert!(
                matches!(c, '\u{2588}' | '\u{2580}' | '\u{2584}' | ' '),
                "unexpected glyph {c:?}"
            );
        }
        // A finder pattern guarantees a mix, so a stuck mapping would show up here.
        assert!(rendered.contains('\u{2588}') && rendered.contains(' '), "no contrast");
    }
}
