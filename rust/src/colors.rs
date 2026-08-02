//! Colour notations, harmonies and WCAG contrast. Mirrors `Krate.Core.Colors`.

use crate::i18n;
use crate::tools::round_half_even;

pub type Rgb = (i32, i32, i32);

fn clamp_channel(v: f64) -> i32 {
    round_half_even(v).clamp(0.0, 255.0) as i32
}

/// Reads `#rgb`, `#rrggbb`, `rgb(...)` or `hsl(...)`, in any casing and with spaces anywhere.
pub fn parse(input: &str) -> Result<Rgb, String> {
    let s = input.trim().to_lowercase().replace(' ', "");
    let bad = || i18n::format("Error_BadColor", &[input]);

    if s.starts_with("rgb") || s.starts_with("hsl") {
        let n = inner_numbers(&s).ok_or_else(bad)?;
        if n.len() < 3 {
            return Err(bad());
        }
        return Ok(if s.starts_with("rgb") {
            (clamp_channel(n[0]), clamp_channel(n[1]), clamp_channel(n[2]))
        } else {
            from_hsl(n[0], n[1] / 100.0, n[2] / 100.0)
        });
    }

    let mut hex = s.trim_start_matches('#').to_string();
    if hex.len() == 3 {
        hex = hex.chars().flat_map(|c| [c, c]).collect(); // #3af -> #33aaff
    }
    if hex.len() != 6 {
        return Err(bad());
    }
    let v = u32::from_str_radix(&hex, 16).map_err(|_| bad())?;
    Ok((
        (v >> 16 & 0xFF) as i32,
        (v >> 8 & 0xFF) as i32,
        (v & 0xFF) as i32,
    ))
}

fn inner_numbers(s: &str) -> Option<Vec<f64>> {
    let open = s.find('(')?;
    let close = s.rfind(')')?;
    Some(
        s[open + 1..close]
            .split([',', '/'])
            .filter(|p| !p.trim().is_empty())
            .filter_map(|p| p.trim().trim_end_matches('%').parse::<f64>().ok())
            .collect(),
    )
}

pub fn to_hsl(c: Rgb) -> (f64, f64, f64) {
    let (r, g, b) = (c.0 as f64 / 255.0, c.1 as f64 / 255.0, c.2 as f64 / 255.0);
    let max = r.max(g).max(b);
    let min = r.min(g).min(b);
    let l = (max + min) / 2.0;
    if max == min {
        return (0.0, 0.0, l); // grey: hue is undefined, report 0
    }
    let d = max - min;
    let s = if l > 0.5 { d / (2.0 - max - min) } else { d / (max + min) };
    let h = if max == r {
        (g - b) / d + if g < b { 6.0 } else { 0.0 }
    } else if max == g {
        (b - r) / d + 2.0
    } else {
        (r - g) / d + 4.0
    };
    (h * 60.0, s, l)
}

fn channel(p: f64, q: f64, t: f64) -> i32 {
    let t = (t % 1.0 + 1.0) % 1.0;
    let v = if t < 1.0 / 6.0 {
        p + (q - p) * 6.0 * t
    } else if t < 1.0 / 2.0 {
        q
    } else if t < 2.0 / 3.0 {
        p + (q - p) * (2.0 / 3.0 - t) * 6.0
    } else {
        p
    };
    round_half_even(v * 255.0) as i32
}

pub fn from_hsl(h: f64, s: f64, l: f64) -> Rgb {
    let h = (h % 360.0 + 360.0) % 360.0 / 360.0;
    let s = s.clamp(0.0, 1.0);
    let l = l.clamp(0.0, 1.0);
    let q = if l < 0.5 { l * (1.0 + s) } else { l + s - l * s };
    let p = 2.0 * l - q;
    (
        channel(p, q, h + 1.0 / 3.0),
        channel(p, q, h),
        channel(p, q, h - 1.0 / 3.0),
    )
}

pub fn hex(c: Rgb) -> String {
    format!("#{:02X}{:02X}{:02X}", c.0, c.1, c.2)
}

/// The same colour in every notation — what the tool is actually for.
pub fn describe_rgb(c: Rgb) -> String {
    let (h, s, l) = to_hsl(c);
    [
        format!("HEX  #{:02X}{:02X}{:02X}", c.0, c.1, c.2),
        format!("RGB  rgb({}, {}, {})", c.0, c.1, c.2),
        format!(
            "HSL  hsl({}, {}%, {}%)",
            round_half_away(h),
            round_half_away(s * 100.0),
            round_half_away(l * 100.0)
        ),
    ]
    .join("\n")
}

/// The C# side renders HSL with the "0" format, which rounds midpoints to even.
fn round_half_away(v: f64) -> i64 {
    round_half_even(v) as i64
}

pub fn describe(input: &str) -> Result<String, String> {
    Ok(describe_rgb(parse(input)?))
}

/// Colour harmonies built by rotating the hue.
pub fn palette(input: &str) -> Result<String, String> {
    let (h, s, l) = to_hsl(parse(input)?);
    let at = |degrees: f64| hex(from_hsl(h + degrees, s, l));
    Ok([
        format!("{:<16} {}", i18n::get("Color_Base"), at(0.0)),
        format!("{:<16} {}", i18n::get("Color_Complementary"), at(180.0)),
        format!("{:<16} {}  {}", i18n::get("Color_Triadic"), at(120.0), at(240.0)),
        format!("{:<16} {}  {}", i18n::get("Color_Analogous"), at(-30.0), at(30.0)),
        format!("{:<16} {}  {}", i18n::get("Color_SplitComp"), at(150.0), at(210.0)),
        format!(
            "{:<16} {}  {}  {}",
            i18n::get("Color_Tetradic"),
            at(90.0),
            at(180.0),
            at(270.0)
        ),
    ]
    .join("\n"))
}

fn relative_luminance(c: Rgb) -> f64 {
    // sRGB to linear, per the WCAG definition, then the Rec.709 luminance weights.
    fn ch(v: i32) -> f64 {
        let s = v as f64 / 255.0;
        if s <= 0.03928 { s / 12.92 } else { ((s + 0.055) / 1.055).powf(2.4) }
    }
    0.2126 * ch(c.0) + 0.7152 * ch(c.1) + 0.0722 * ch(c.2)
}

pub fn contrast_ratio(a: Rgb, b: Rgb) -> f64 {
    let (la, lb) = (relative_luminance(a), relative_luminance(b));
    let (hi, lo) = (la.max(lb), la.min(lb));
    (hi + 0.05) / (lo + 0.05)
}

/// WCAG contrast ratio between two colours (one per line) and which levels it passes.
pub fn contrast(input: &str) -> Result<String, String> {
    let lines: Vec<&str> = input
        .split('\n')
        .map(str::trim)
        .filter(|l| !l.is_empty())
        .collect();
    if lines.len() < 2 {
        return Err(i18n::get("Error_NeedTwoColors").to_string());
    }
    let ratio = contrast_ratio(parse(lines[0])?, parse(lines[1])?);
    let verdict = |min: f64| {
        if ratio >= min { i18n::get("Color_Pass") } else { i18n::get("Color_Fail") }
    };
    let (normal, large) = (i18n::get("Color_NormalText"), i18n::get("Color_LargeText"));
    Ok([
        format!("{}  {ratio:.2}:1", i18n::get("Color_Ratio")),
        format!("AA  ({normal})   {}", verdict(4.5)),
        format!("AA  ({large})   {}", verdict(3.0)),
        format!("AAA ({normal})   {}", verdict(7.0)),
        format!("AAA ({large})   {}", verdict(4.5)),
    ]
    .join("\n"))
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn parses_every_notation_to_the_same_colour() {
        assert_eq!(parse("#FF0000").unwrap(), (255, 0, 0));
        assert_eq!(parse("#f00").unwrap(), (255, 0, 0), "shorthand expands");
        assert_eq!(parse("ff0000").unwrap(), (255, 0, 0), "hash is optional");
        assert_eq!(parse("rgb(255, 0, 0)").unwrap(), (255, 0, 0));
        assert_eq!(parse("hsl(0, 100%, 50%)").unwrap(), (255, 0, 0));
    }

    #[test]
    fn rejects_nonsense() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        for bad in ["", "#12345", "zzz", "#gggggg"] {
            assert!(parse(bad).is_err(), "{bad} should not parse");
        }
    }

    #[test]
    fn describes_a_colour_in_every_notation() {
        let red = describe_rgb((255, 0, 0));
        assert!(red.contains("HEX  #FF0000"), "{red}");
        assert!(red.contains("RGB  rgb(255, 0, 0)"), "{red}");
        assert!(red.contains("HSL  hsl(0, 100%, 50%)"), "{red}");
        assert!(describe_rgb((128, 128, 128)).contains("HSL  hsl(0, 0%, 50%)"), "grey has no hue");
    }

    #[test]
    fn hsl_round_trips_through_rgb() {
        for c in [(255, 0, 0), (0, 255, 0), (0, 0, 255), (18, 52, 86), (255, 255, 255)] {
            let (h, s, l) = to_hsl(c);
            assert_eq!(from_hsl(h, s, l), c, "round trip failed for {c:?}");
        }
    }

    /// Black on white is the reference maximum in WCAG.
    #[test]
    fn contrast_ratio_matches_the_wcag_reference() {
        let ratio = contrast_ratio((0, 0, 0), (255, 255, 255));
        assert!((ratio - 21.0).abs() < 0.01, "black on white should be 21:1, got {ratio}");
        assert!((contrast_ratio((255, 0, 0), (255, 0, 0)) - 1.0).abs() < 1e-9, "identical is 1:1");
    }

    #[test]
    fn contrast_needs_two_colours() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        assert!(contrast("#000000").is_err());
        assert!(contrast("#000000\n#FFFFFF").is_ok());
    }

    #[test]
    fn palette_rotates_the_hue() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        let result = palette("#FF0000").unwrap();
        assert_eq!(result.lines().count(), 6);
        assert!(result.contains("#FF0000"), "base colour is present: {result}");
        // The complement of red is cyan.
        assert!(result.contains("#00FFFF"), "{result}");
    }
}
