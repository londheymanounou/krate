//! Unicode "fancy text" styles. Mirrors `Krate.Core.Fancy`.
//!
//! The mathematical alphanumeric blocks have **holes**: a few letters were already encoded
//! elsewhere in Unicode, so their slot in the block is reserved and unassigned. Italic `h` and
//! seven double-struck capitals have to be redirected, or the output contains unassigned code
//! points that render as boxes.

use crate::i18n;

/// Italic `h` lives at U+210E (Planck constant), not in the italic block.
const ITALIC_HOLES: &[(char, u32)] = &[('h', 0x210E)];

/// The double-struck capitals that predate the block, as letterlike symbols.
const DOUBLE_HOLES: &[(char, u32)] = &[
    ('C', 0x2102), ('H', 0x210D), ('N', 0x2115), ('P', 0x2119),
    ('Q', 0x211A), ('R', 0x211D), ('Z', 0x2124),
];

/// Maps A to `upper_base`, a to `lower_base`, 0 to `digit_base` when the style has digits,
/// applying hole overrides first so reserved code points become the real letter.
fn map(text: &str, upper_base: u32, lower_base: u32, digit_base: Option<u32>, holes: &[(char, u32)]) -> String {
    text.chars()
        .map(|c| {
            if let Some((_, replacement)) = holes.iter().find(|(hole, _)| *hole == c) {
                return char::from_u32(*replacement).unwrap_or(c);
            }
            let mapped = match c {
                'A'..='Z' => Some(upper_base + (c as u32 - 'A' as u32)),
                'a'..='z' => Some(lower_base + (c as u32 - 'a' as u32)),
                '0'..='9' => digit_base.map(|d| d + (c as u32 - '0' as u32)),
                _ => None,
            };
            mapped.and_then(char::from_u32).unwrap_or(c)
        })
        .collect()
}

/// Circled letters, and circled digits 1-9 — there is no circled zero in this range, so `0`
/// passes through unchanged.
fn circled(text: &str) -> String {
    text.chars()
        .map(|c| {
            let mapped = match c {
                'A'..='Z' => Some(0x24B6 + (c as u32 - 'A' as u32)),
                'a'..='z' => Some(0x24D0 + (c as u32 - 'a' as u32)),
                '1'..='9' => Some(0x2460 + (c as u32 - '1' as u32)),
                _ => None,
            };
            mapped.and_then(char::from_u32).unwrap_or(c)
        })
        .collect()
}

/// Fullwidth forms — the "ａｅｓｔｈｅｔｉｃ" look. ASCII 0x21..0x7E map to U+FF01 onwards, and a
/// plain space becomes the ideographic space.
fn fullwidth(text: &str) -> String {
    text.chars()
        .map(|c| {
            if c == ' ' {
                return '\u{3000}';
            }
            match c as u32 {
                0x21..=0x7E => char::from_u32(0xFF01 + (c as u32 - 0x21)).unwrap_or(c),
                _ => c,
            }
        })
        .collect()
}

pub fn convert(text: &str) -> Result<String, String> {
    if text.is_empty() {
        return Err(i18n::get("Error_NeedText").to_string());
    }
    Ok([
        format!("{:<12} {}", i18n::get("Fancy_Bold"), map(text, 0x1D400, 0x1D41A, Some(0x1D7CE), &[])),
        format!("{:<12} {}", i18n::get("Fancy_Italic"), map(text, 0x1D434, 0x1D44E, None, ITALIC_HOLES)),
        // bold script and bold fraktur are gap-free
        format!("{:<12} {}", i18n::get("Fancy_Script"), map(text, 0x1D4D0, 0x1D4EA, None, &[])),
        format!("{:<12} {}", i18n::get("Fancy_Fraktur"), map(text, 0x1D56C, 0x1D586, None, &[])),
        format!("{:<12} {}", i18n::get("Fancy_Mono"), map(text, 0x1D670, 0x1D68A, Some(0x1D7F6), &[])),
        format!("{:<12} {}", i18n::get("Fancy_Double"), map(text, 0x1D538, 0x1D552, Some(0x1D7D8), DOUBLE_HOLES)),
        format!("{:<12} {}", i18n::get("Fancy_Circled"), circled(text)),
        format!("{:<12} {}", i18n::get("Fancy_Wide"), fullwidth(text)),
    ]
    .join("\n"))
}

#[cfg(test)]
mod tests {
    use super::*;

    fn style(output: &str, index: usize) -> String {
        // Each line is "Label       <text>"; the label is padded to 12 then a space.
        output.lines().nth(index).unwrap()[13..].to_string()
    }

    #[test]
    fn every_style_maps_letters_and_the_right_digits() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        let out = convert("ABZabz059").unwrap();
        assert_eq!(out.lines().count(), 8);
        assert_eq!(style(&out, 0), "𝐀𝐁𝐙𝐚𝐛𝐳𝟎𝟓𝟗", "bold has digits");
        assert_eq!(style(&out, 1), "𝐴𝐵𝑍𝑎𝑏𝑧059", "italic has no digits");
        assert_eq!(style(&out, 2), "𝓐𝓑𝓩𝓪𝓫𝔃059");
        assert_eq!(style(&out, 3), "𝕬𝕭𝖅𝖆𝖇𝖟059");
        assert_eq!(style(&out, 4), "𝙰𝙱𝚉𝚊𝚋𝚣𝟶𝟻𝟿");
        assert_eq!(style(&out, 6), "ⒶⒷⓏⓐⓑⓩ0⑤⑨", "no circled zero, so 0 passes through");
        assert_eq!(style(&out, 7), "ＡＢＺａｂｚ０５９");
    }

    /// The reserved slots are the whole reason this module is not a one-line offset.
    #[test]
    fn reserved_code_points_are_redirected() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        // Italic h is the Planck constant, U+210E.
        let italic = style(&convert("gh").unwrap(), 1);
        assert_eq!(italic.chars().nth(1), Some('\u{210E}'), "{italic}");
        // Double-struck Z is U+2124, not a slot in the block.
        let double = style(&convert("YZ").unwrap(), 5);
        assert_eq!(double.chars().nth(1), Some('\u{2124}'), "{double}");
        // Every double-struck capital that has a hole must land on a real character.
        // Written as escapes: these are visually near-identical to the script and fraktur
        // letterlike symbols, and a typo picks the wrong one silently.
        let all = style(&convert("CHNPQRZ").unwrap(), 5);
        assert_eq!(all, "\u{2102}\u{210D}\u{2115}\u{2119}\u{211A}\u{211D}\u{2124}");
    }

    #[test]
    fn unmapped_characters_pass_through() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        let out = convert("a-é!").unwrap();
        // Accents and punctuation are not in the alphabets, so they survive as themselves.
        assert!(style(&out, 0).contains('é'), "{}", style(&out, 0));
        assert!(style(&out, 0).contains('-'));
        // Fullwidth does map ASCII punctuation, though.
        assert!(style(&out, 7).contains('\u{FF01}'), "! becomes fullwidth");
        assert!(convert("").is_err());
    }

    #[test]
    fn fullwidth_uses_the_ideographic_space() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        assert!(style(&convert("a b").unwrap(), 7).contains('\u{3000}'));
    }
}
