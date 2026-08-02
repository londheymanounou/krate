//! Size, ratio and colour-temperature conversions. Mirrors `Krate.Core.Css`, `Sizes`,
//! `Images` and the temperature/colour-blindness parts of `Colors`.

use crate::colors::{self, Rgb};
use crate::i18n;
use crate::tools::{gcd, round_half_even};

// ---------- CSS units ----------

const ROOT_PX: f64 = 16.0; // the CSS default root font size
const PX_PER_PT: f64 = 96.0 / 72.0; // 1pt = 1/72in, 1in = 96px

/// Matches C#'s `Math.Round(v, 4).ToString(InvariantCulture)` — banker's rounding to four
/// decimals, then the shortest representation.
fn num(v: f64) -> String {
    let rounded = round_to(v, 4);
    let mut s = format!("{rounded:.4}");
    if s.contains('.') {
        s = s.trim_end_matches('0').trim_end_matches('.').to_string();
    }
    if s == "-0" { "0".to_string() } else { s }
}

/// `Math.Round(v, 4)` — banker's rounding at four decimals.
fn round_to(v: f64, decimals: i32) -> f64 {
    let factor = 10f64.powi(decimals);
    round_half_even(v * factor) / factor
}

/// "16px", "1.5rem", "12pt" to the same size in px, rem, em and pt. em and rem both assume the
/// 16px default root, since there is no document context here.
pub fn css_units(input: &str) -> Result<String, String> {
    let s = input.trim().to_lowercase().replace(' ', "");
    let bad = || i18n::format("Error_BadCssUnit", &[input]);

    let digits = s
        .chars()
        .take_while(|c| c.is_ascii_digit() || *c == '.' || *c == '-')
        .count();
    if digits == 0 {
        return Err(bad());
    }
    let value: f64 = s[..digits].parse().map_err(|_| bad())?;
    let unit = &s[digits..];

    // Everything goes through pixels as the common ground.
    let px = match unit {
        "px" | "" => value,
        "rem" | "em" => value * ROOT_PX,
        "pt" => value * PX_PER_PT,
        "%" => value / 100.0 * ROOT_PX,
        _ => return Err(bad()),
    };

    Ok([
        format!("{}px", num(px)),
        format!("{}rem", num(px / ROOT_PX)),
        format!("{}em", num(px / ROOT_PX)),
        format!("{}pt", num(px / PX_PER_PT)),
    ]
    .join("\n"))
}

// ---------- aspect ratio ----------

pub(crate) fn reduce_ratio(w: i64, h: i64) -> (i64, i64) {
    let g = gcd(w, h);
    if g == 0 { (w, h) } else { (w / g, h / g) }
}

// 16:10 reduces to 8:5, so both spellings map to the same name.
pub(crate) fn ratio_name(w: i64, h: i64) -> Option<&'static str> {
    Some(match (w, h) {
        (16, 9) => "16:9",
        (4, 3) => "4:3",
        (3, 2) => "3:2",
        (8, 5) => "16:10",
        (21, 9) | (7, 3) => "21:9",
        (1, 1) => "1:1",
        (5, 4) => "5:4",
        _ => return None,
    })
}

/// "1920x1080" gives its reduced ratio; "16:9 1920" fills in the missing dimension.
pub fn aspect_ratio(input: &str) -> Result<String, String> {
    let s = input.trim().to_lowercase();
    let parts: Vec<&str> = s.split([' ', ',']).filter(|p| !p.is_empty()).collect();
    let bad = || i18n::get("Error_RatioUsage").to_string();

    if parts.len() == 2 && parts[0].contains(':') {
        if let Ok(known) = parts[1].parse::<i64>() {
            let (rw, rh) = parts[0].split_once(':').ok_or_else(bad)?;
            let rw: i64 = rw.parse().map_err(|_| bad())?;
            let rh: i64 = rh.parse().map_err(|_| bad())?;
            if rw == 0 || rh == 0 {
                return Err(bad());
            }
            return Ok([
                format!("{known} × {} px  ({})", known * rh / rw, i18n::get("Images_FromWidth")),
                format!("{} × {known} px  ({})", known * rw / rh, i18n::get("Images_FromHeight")),
            ]
            .join("\n"));
        }
    }

    let wh: Vec<&str> = s.split(['x', ':', '×']).filter(|p| !p.is_empty()).collect();
    if wh.len() != 2 {
        return Err(bad());
    }
    let w: i64 = wh[0].trim().parse().map_err(|_| bad())?;
    let h: i64 = wh[1].trim().parse().map_err(|_| bad())?;
    if w <= 0 || h <= 0 {
        return Err(bad());
    }
    let (a, c) = reduce_ratio(w, h);
    Ok(match ratio_name(a, c) {
        Some(name) => format!("{a}:{c}  ({name})"),
        None => format!("{a}:{c}"),
    })
}

// ---------- shoe sizes ----------

type ShoeRow = (f64, f64, f64, f64); // EU, UK, US, CM

const MEN: [ShoeRow; 11] = [
    (38.0, 5.0, 5.5, 24.0), (39.0, 6.0, 6.5, 24.7), (40.0, 6.5, 7.0, 25.4), (41.0, 7.5, 8.0, 26.0),
    (42.0, 8.0, 8.5, 26.7), (43.0, 9.0, 9.5, 27.3), (44.0, 9.5, 10.0, 28.0), (45.0, 10.5, 11.0, 28.6),
    (46.0, 11.0, 11.5, 29.3), (47.0, 12.0, 12.5, 29.8), (48.0, 13.0, 13.5, 30.5),
];

const WOMEN: [ShoeRow; 10] = [
    (35.0, 2.5, 4.5, 22.0), (36.0, 3.5, 5.5, 22.7), (37.0, 4.0, 6.0, 23.3), (38.0, 5.0, 7.0, 24.0),
    (39.0, 6.0, 8.0, 24.7), (40.0, 6.5, 9.0, 25.4), (41.0, 7.5, 9.5, 26.0), (42.0, 8.0, 10.5, 26.7),
    (43.0, 9.0, 11.0, 27.3), (44.0, 9.5, 12.0, 28.0),
];

pub fn shoe(input: &str) -> Result<String, String> {
    let s = input.trim().to_lowercase();
    let women = s.contains('w') || s.contains("women") || s.contains('f');
    let table: &[ShoeRow] = if women { &WOMEN } else { &MEN };

    // System keyword: cm before us/uk/eu so it is not shadowed.
    let system = if s.contains("cm") { 3 } else if s.contains("uk") { 1 } else if s.contains("us") { 2 } else { 0 };

    let digits: String = s.chars().filter(|c| c.is_ascii_digit() || *c == '.' || *c == ',').collect();
    if digits.is_empty() {
        return Err(i18n::get("Error_ShoeUsage").to_string());
    }
    let value: f64 = digits
        .replace(',', ".")
        .parse()
        .map_err(|_| i18n::get("Error_ShoeUsage").to_string())?;

    let field = |r: &ShoeRow, i: usize| match i {
        1 => r.1,
        2 => r.2,
        3 => r.3,
        _ => r.0,
    };
    let row = table
        .iter()
        .min_by(|a, b| {
            (field(a, system) - value)
                .abs()
                .partial_cmp(&(field(b, system) - value).abs())
                .unwrap_or(std::cmp::Ordering::Equal)
        })
        .expect("tables are never empty");

    let n = |v: f64| {
        let s = format!("{v:.1}");
        s.trim_end_matches('0').trim_end_matches('.').to_string()
    };
    Ok([
        i18n::get(if women { "Shoe_Women" } else { "Shoe_Men" }).to_string(),
        format!("EU  {}", n(row.0)),
        format!("UK  {}", n(row.1)),
        format!("US  {}", n(row.2)),
        format!("CM  {}", n(row.3)),
        i18n::get("Shoe_Note").to_string(),
    ]
    .join("\n"))
}

// ---------- colour temperature and colour blindness ----------

fn clamp_channel(v: f64) -> i32 {
    round_half_even(v).clamp(0.0, 255.0) as i32
}

/// Tanner Helland's approximation, as used by the C# side.
pub fn kelvin_to_rgb(kelvin: f64) -> Rgb {
    let t = kelvin / 100.0;
    let (r, g);
    if t <= 66.0 {
        r = 255.0;
        g = 99.4708025861 * t.ln() - 161.1195681661;
    } else {
        r = 329.698727446 * (t - 60.0).powf(-0.1332047592);
        g = 288.1221695283 * (t - 60.0).powf(-0.0755148492);
    }
    let b = if t >= 66.0 {
        255.0
    } else if t <= 19.0 {
        0.0
    } else {
        138.5177312231 * (t - 10.0).ln() - 305.0447927307
    };
    (clamp_channel(r), clamp_channel(g), clamp_channel(b))
}

/// Colour temperature in Kelvin to the approximate RGB of that white point (candlelight
/// ~1900K, daylight ~6500K).
pub fn color_temp(input: &str) -> Result<String, String> {
    let kelvin: f64 = input
        .trim()
        .trim_end_matches(['k', 'K'])
        .parse()
        .map_err(|_| i18n::get("Error_NeedNumber").to_string())?;
    if !(1000.0..=40000.0).contains(&kelvin) {
        return Err(i18n::get("Error_KelvinRange").to_string());
    }
    Ok(colors::describe_rgb(kelvin_to_rgb(kelvin)))
}

const PROTANOPIA: [f64; 9] = [0.567, 0.433, 0.0, 0.558, 0.442, 0.0, 0.0, 0.242, 0.758];
const DEUTERANOPIA: [f64; 9] = [0.625, 0.375, 0.0, 0.70, 0.30, 0.0, 0.0, 0.30, 0.70];
const TRITANOPIA: [f64; 9] = [0.95, 0.05, 0.0, 0.0, 0.433, 0.567, 0.0, 0.475, 0.525];

pub fn simulate(c: Rgb, m: &[f64; 9]) -> Rgb {
    let (r, g, b) = (c.0 as f64, c.1 as f64, c.2 as f64);
    (
        clamp_channel(m[0] * r + m[1] * g + m[2] * b),
        clamp_channel(m[3] * r + m[4] * g + m[5] * b),
        clamp_channel(m[6] * r + m[7] * g + m[8] * b),
    )
}

/// How a colour appears under the common colour-blindness types — for checking a palette stays
/// distinguishable.
pub fn color_blind(input: &str) -> Result<String, String> {
    let c = colors::parse(input)?;
    let gray = clamp_channel(0.299 * c.0 as f64 + 0.587 * c.1 as f64 + 0.114 * c.2 as f64);
    Ok([
        format!("{:<18} {}", i18n::get("Cvd_Normal"), colors::hex(c)),
        format!("{:<18} {}", i18n::get("Cvd_Protan"), colors::hex(simulate(c, &PROTANOPIA))),
        format!("{:<18} {}", i18n::get("Cvd_Deuter"), colors::hex(simulate(c, &DEUTERANOPIA))),
        format!("{:<18} {}", i18n::get("Cvd_Tritan"), colors::hex(simulate(c, &TRITANOPIA))),
        format!("{:<18} {}", i18n::get("Cvd_Achroma"), colors::hex((gray, gray, gray))),
    ]
    .join("\n"))
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn css_units_convert_through_pixels() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        let result = css_units("16px").unwrap();
        assert!(result.contains("16px"), "{result}");
        assert!(result.contains("1rem"), "{result}");
        assert!(result.contains("12pt"), "{result}");
        assert_eq!(css_units("1rem").unwrap(), css_units("16px").unwrap());
        assert!(css_units("zzz").is_err());
        assert!(css_units("10furlongs").is_err());
    }

    #[test]
    fn aspect_ratio_reduces_and_names() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        assert_eq!(aspect_ratio("1920x1080").unwrap(), "16:9  (16:9)");
        assert_eq!(aspect_ratio("1024x768").unwrap(), "4:3  (4:3)");
        assert_eq!(aspect_ratio("1920x1200").unwrap(), "8:5  (16:10)");
        assert!(aspect_ratio("1000x999").unwrap().starts_with("1000:999"), "unnamed ratios still reduce");
        assert!(aspect_ratio("zzz").is_err());
        assert!(aspect_ratio("0x100").is_err());
    }

    #[test]
    fn aspect_ratio_fills_in_a_missing_dimension() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        let result = aspect_ratio("16:9 1920").unwrap();
        assert!(result.contains("1920 × 1080"), "{result}");
    }

    #[test]
    fn shoe_finds_the_closest_row_in_the_right_table() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        let men = shoe("42").unwrap();
        assert!(men.contains("EU  42"), "{men}");
        let women = shoe("38w").unwrap();
        assert!(women.contains("EU  38"), "{women}");
        assert!(women.contains("US  7"), "women's 38 is US 7: {women}");
        assert!(shoe("no digits").is_err());
    }

    #[test]
    fn kelvin_maps_to_a_plausible_white_point() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        // Low temperatures are warm (red-dominant), high ones are cool (blue-dominant).
        let warm = kelvin_to_rgb(1900.0);
        assert!(warm.0 > warm.2, "1900K should be red-dominant, got {warm:?}");
        let cool = kelvin_to_rgb(20000.0);
        assert!(cool.2 >= cool.0, "20000K should be blue-dominant, got {cool:?}");
        assert!(color_temp("6500K").is_ok());
        assert!(color_temp("500").is_err(), "below the supported range");
        assert!(color_temp("50000").is_err(), "above the supported range");
    }

    #[test]
    fn colour_blindness_keeps_grey_grey() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        let result = color_blind("#808080").unwrap();
        assert_eq!(result.lines().count(), 5);
        // Achromatopsia of a grey is the same grey.
        assert!(result.lines().last().unwrap().contains("#808080"), "{result}");
        assert!(color_blind("zzz").is_err());
    }
}
