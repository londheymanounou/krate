//! Unit conversion. Mirrors `Krate.Core.Units.Convert`.
//!
//! Lookup is **case-sensitive first**, falling back to a case-insensitive match only when it is
//! unambiguous. That is deliberate in the original and must be preserved: `mb` is megabit and
//! `MB` is megabyte, so silently folding case would be a factor-of-eight error in the user's
//! favour or against, at random.

use crate::i18n;
use crate::tools::format_decimal;

/// (unit, dimension, factor to the dimension's base unit).
/// Bases: metre, gram, second, byte, metre/second, square metre, litre, degree.
const TABLE: &[(&str, &str, f64)] = &[
    // length (metre)
    ("mm", "length", 0.001), ("cm", "length", 0.01), ("m", "length", 1.0),
    ("km", "length", 1000.0), ("in", "length", 0.0254), ("ft", "length", 0.3048),
    ("yd", "length", 0.9144), ("mi", "length", 1609.344), ("nmi", "length", 1852.0),
    // mass (gram)
    ("mg", "mass", 0.001), ("g", "mass", 1.0), ("kg", "mass", 1000.0), ("t", "mass", 1_000_000.0),
    ("oz", "mass", 28.349523125), ("lb", "mass", 453.59237), ("st", "mass", 6350.29318),
    // time (second)
    ("ms", "time", 0.001), ("s", "time", 1.0), ("min", "time", 60.0), ("h", "time", 3600.0),
    ("d", "time", 86400.0), ("wk", "time", 604800.0),
    // data (byte) — decimal and binary kept distinct, because that is the whole point
    ("b", "data", 0.125), ("kb", "data", 125.0), ("mb", "data", 125_000.0),
    ("byte", "data", 1.0), ("B", "data", 1.0), ("kB", "data", 1000.0), ("MB", "data", 1e6),
    ("GB", "data", 1e9), ("TB", "data", 1e12),
    ("KiB", "data", 1024.0), ("MiB", "data", 1048576.0), ("GiB", "data", 1073741824.0),
    ("TiB", "data", 1099511627776.0),
    // speed (metre/second)
    ("mps", "speed", 1.0), ("kmh", "speed", 1.0 / 3.6), ("mph", "speed", 0.44704), ("kn", "speed", 0.514444),
    // area (square metre)
    ("m2", "area", 1.0), ("km2", "area", 1e6), ("ha", "area", 10000.0),
    ("ft2", "area", 0.09290304), ("acre", "area", 4046.8564224),
    // volume (litre)
    ("ml", "volume", 0.001), ("l", "volume", 1.0), ("m3", "volume", 1000.0),
    ("gal", "volume", 3.785411784), ("pt", "volume", 0.473176473), ("floz", "volume", 0.0295735295625),
    // angle (degree)
    ("deg", "angle", 1.0), ("rad", "angle", 180.0 / std::f64::consts::PI),
    ("grad", "angle", 0.9), ("turn", "angle", 360.0),
];

const TEMPERATURES: [&str; 3] = ["c", "f", "k"];

fn lookup(unit: &str) -> Result<(&'static str, f64), String> {
    if let Some((_, dim, factor)) = TABLE.iter().find(|(u, _, _)| *u == unit) {
        return Ok((dim, *factor));
    }
    let candidates: Vec<&(&str, &str, f64)> = TABLE
        .iter()
        .filter(|(u, _, _)| u.eq_ignore_ascii_case(unit))
        .collect();
    match candidates.as_slice() {
        [only] => Ok((only.1, only.2)),
        // "Mb" could mean megabit or megabyte — refuse rather than be wrong by a factor of 8.
        [_, ..] => Err(i18n::format(
            "Error_AmbiguousUnit",
            &[unit, &candidates.iter().map(|c| c.0).collect::<Vec<_>>().join(", ")],
        )),
        [] => Err(i18n::format("Error_UnknownUnit", &[unit])),
    }
}

pub fn temperature(value: f64, from: &str, to: &str) -> f64 {
    let celsius = match from.to_lowercase().as_str() {
        "f" => (value - 32.0) * 5.0 / 9.0,
        "k" => value - 273.15,
        _ => value,
    };
    match to.to_lowercase().as_str() {
        "f" => celsius * 9.0 / 5.0 + 32.0,
        "k" => celsius + 273.15,
        _ => celsius,
    }
}

/// "10 km mi", "100 f c", "5 GiB MB".
pub fn convert(input: &str) -> Result<String, String> {
    let parts: Vec<&str> = input
        .split([' ', ',', '\t', '\n', '>'])
        .map(str::trim)
        .filter(|p| !p.is_empty())
        .collect();
    if parts.len() < 3 {
        return Err(i18n::get("Error_ConvertUsage").to_string());
    }

    let value: f64 = parts[0]
        .parse()
        .map_err(|_| i18n::get("Error_NeedNumber").to_string())?;
    let (from, to) = (parts[1], parts[parts.len() - 1]);

    let format = |v: f64, unit: &str| format!("{} {unit}", format_decimal(v, 10));

    if TEMPERATURES.iter().any(|t| t.eq_ignore_ascii_case(from)) {
        return Ok(format(temperature(value, from, to), to));
    }

    let (from_dim, from_factor) = lookup(from)?;
    let (to_dim, to_factor) = lookup(to)?;
    if from_dim != to_dim {
        return Err(i18n::format("Error_DimensionMismatch", &[from, to]));
    }
    Ok(format(value * from_factor / to_factor, to))
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn converts_within_a_dimension() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        assert_eq!(convert("10 km mi").unwrap(), "6.2137119224 mi");
        assert_eq!(convert("1 GiB MiB").unwrap(), "1024 MiB");
        assert_eq!(convert("1 h min").unwrap(), "60 min");
    }

    #[test]
    fn temperature_is_handled_separately_because_it_has_an_offset() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        assert_eq!(convert("100 F C").unwrap(), "37.7777777778 C");
        assert_eq!(convert("0 C K").unwrap(), "273.15 K");
        assert_eq!(convert("32 f c").unwrap(), "0 c");
    }

    /// The case rule is load-bearing: "mb" is megabit, "MB" is megabyte, and "Mb" matches both
    /// case-insensitively so it must be refused rather than guessed.
    #[test]
    fn ambiguous_case_is_refused_not_guessed() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        assert!(convert("1 Mb kB").is_err(), "Mb is ambiguous");
        // Unambiguous casing still resolves.
        assert_eq!(convert("1 KM m").unwrap(), "1000 m");
        // Exact matches win over the case-insensitive fallback.
        assert_eq!(convert("1 MB kB").unwrap(), "1000 kB");
        assert_eq!(convert("8 b B").unwrap(), "1 B", "lower-case b is bits");
    }

    #[test]
    fn mismatched_dimensions_and_unknown_units_are_rejected() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        assert!(convert("1 km kg").is_err(), "length is not mass");
        assert!(convert("1 xyz m").is_err(), "unknown unit");
        assert!(convert("10 km").is_err(), "needs value, from and to");
        assert!(convert("zzz km mi").is_err(), "value must be a number");
    }
}
