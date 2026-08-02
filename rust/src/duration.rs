//! Duration parsing and breakdown. Mirrors the duration half of `Krate.Core.Dates`.
//!
//! No date arithmetic here — that is the part needing a calendar library and is still on the
//! C# side. This is pure elapsed-time work, so it ports cleanly.

use crate::i18n;
use crate::tools::round_half_even;

pub struct TimeUnit {
    pub label: &'static str,
    pub aliases: &'static [&'static str],
    pub seconds: f64,
}

pub const TIME_UNITS: &[TimeUnit] = &[
    TimeUnit { label: "milliseconds", aliases: &["ms", "milli", "millis", "millisecond", "milliseconds"], seconds: 0.001 },
    TimeUnit { label: "seconds", aliases: &["s", "sec", "secs", "second", "seconds"], seconds: 1.0 },
    TimeUnit { label: "minutes", aliases: &["m", "min", "mins", "minute", "minutes"], seconds: 60.0 },
    TimeUnit { label: "hours", aliases: &["h", "hr", "hrs", "hour", "hours"], seconds: 3600.0 },
    TimeUnit { label: "days", aliases: &["d", "day", "days"], seconds: 86400.0 },
    TimeUnit { label: "weeks", aliases: &["w", "wk", "wks", "week", "weeks"], seconds: 604800.0 },
    // average month = year / 12
    TimeUnit { label: "months", aliases: &["mo", "mon", "month", "months"], seconds: 2629800.0 },
    // Julian year, 365.25 days
    TimeUnit { label: "years", aliases: &["y", "yr", "yrs", "year", "years"], seconds: 31557600.0 },
    TimeUnit { label: "decades", aliases: &["decade", "decades"], seconds: 315576000.0 },
    TimeUnit { label: "centuries", aliases: &["century", "centuries"], seconds: 3155760000.0 },
    TimeUnit { label: "light-years", aliases: &["ly", "lightyear", "lightyears", "light-year", "light-years"], seconds: 31557600.0 },
];

fn try_unit(token: &str) -> Option<&'static TimeUnit> {
    TIME_UNITS
        .iter()
        .find(|u| u.aliases.iter().any(|a| a.eq_ignore_ascii_case(token)))
}

/// Mirrors C#'s "0.######": up to six decimals, trailing zeros dropped.
fn n(v: f64) -> String {
    let mut s = format!("{v:.6}");
    if s.contains('.') {
        s = s.trim_end_matches('0').trim_end_matches('.').to_string();
    }
    s
}

/// A bare number of seconds ("90000"), unit tokens ("1d 2h 30m", "1.5h", "500ms"), or a clock
/// ("2:30:00" = h:m:s, "90:00" = m:s).
pub fn parse_duration(input: &str) -> Result<f64, String> {
    let s = input.trim().to_lowercase();
    if s.is_empty() {
        return Err(i18n::get("Error_NeedNumber").to_string());
    }

    if s.contains(':') {
        let parts: Vec<f64> = s
            .split(':')
            .map(|x| x.parse::<f64>().map_err(|_| i18n::get("Error_DurationUsage").to_string()))
            .collect::<Result<_, _>>()?;
        return match parts.len() {
            3 => Ok(parts[0] * 3600.0 + parts[1] * 60.0 + parts[2]),
            2 => Ok(parts[0] * 60.0 + parts[1]),
            _ => Err(i18n::get("Error_DurationUsage").to_string()),
        };
    }

    // The C# side uses the regex `(\d+\.?\d*)\s*([a-z-]+)`; scanned by hand here to avoid
    // pulling in a regex engine for one pattern.
    let chars: Vec<char> = s.chars().collect();
    let mut total = 0.0;
    let mut found = false;
    let mut i = 0;
    while i < chars.len() {
        if !chars[i].is_ascii_digit() {
            i += 1;
            continue;
        }
        let start = i;
        while i < chars.len() && (chars[i].is_ascii_digit() || chars[i] == '.') {
            i += 1;
        }
        let value: f64 = chars[start..i]
            .iter()
            .collect::<String>()
            .parse()
            .map_err(|_| i18n::get("Error_NeedNumber").to_string())?;

        while i < chars.len() && chars[i].is_whitespace() {
            i += 1;
        }
        let unit_start = i;
        while i < chars.len() && (chars[i].is_ascii_lowercase() || chars[i] == '-') {
            i += 1;
        }
        if unit_start == i {
            continue; // a number with no unit is not a match for this pattern
        }
        let token: String = chars[unit_start..i].iter().collect();
        let unit = try_unit(&token).ok_or_else(|| i18n::format("Error_UnknownUnit", &[&token]))?;
        total += value * unit.seconds;
        found = true;
    }
    if found {
        return Ok(total);
    }

    // Bare number = seconds.
    s.parse::<f64>().map_err(|_| i18n::get("Error_NeedNumber").to_string())
}

pub fn breakdown(seconds: f64) -> String {
    let negative = seconds < 0.0;
    let total_ms = round_half_even(seconds.abs() * 1000.0) as i64;

    let w = total_ms / 604_800_000;
    let mut rem = total_ms % 604_800_000;
    let d = rem / 86_400_000;
    rem %= 86_400_000;
    let h = rem / 3_600_000;
    rem %= 3_600_000;
    let mi = rem / 60_000;
    rem %= 60_000;
    let (se, ms) = (rem / 1000, rem % 1000);

    // Compact: drop leading zero units, keep everything once the first non-zero appears.
    let mut comps: Vec<String> = Vec::new();
    for (v, label) in [(w, "w"), (d, "d"), (h, "h"), (mi, "m"), (se, "s")] {
        if v > 0 || !comps.is_empty() {
            comps.push(format!("{v}{label}"));
        }
    }
    if ms > 0 {
        comps.push(format!("{ms}ms"));
    }
    if comps.is_empty() {
        comps.push("0s".to_string());
    }
    let compact = format!("{}{}", if negative { "-" } else { "" }, comps.join(" "));

    // ISO 8601: weeks fold into days so the date and time parts can coexist (P…T…).
    let iso_days = w * 7 + d;
    let sec_frac = se as f64 + ms as f64 / 1000.0;
    let mut iso = format!("P{}", if iso_days > 0 { format!("{iso_days}D") } else { String::new() });
    if h > 0 || mi > 0 || sec_frac > 0.0 || iso_days == 0 {
        iso.push('T');
        if h > 0 {
            iso.push_str(&format!("{h}H"));
        }
        if mi > 0 {
            iso.push_str(&format!("{mi}M"));
        }
        if sec_frac > 0.0 || (h == 0 && mi == 0) {
            iso.push_str(&format!("{}S", n(sec_frac)));
        }
    }
    if negative {
        iso = format!("-{iso}");
    }

    let mut lines = vec![compact, iso];
    for u in TIME_UNITS {
        lines.push(format!("{:<13} {}", u.label.to_uppercase(), n(seconds / u.seconds)));
    }
    lines.join("\n")
}

/// "5 h s" converts 5 hours to seconds; "5 h", a bare number, "1d 2h 30m" or "2:30:00" all show
/// a compact breakdown, the ISO-8601 form, and every unit.
pub fn duration(input: &str) -> Result<String, String> {
    let tokens: Vec<&str> = input
        .trim()
        .split([' ', ',', '\t'])
        .map(str::trim)
        .filter(|t| !t.is_empty())
        .collect();

    // "<value> <from> <to>" is one direct conversion.
    if tokens.len() == 3 {
        if let Ok(value) = tokens[0].parse::<f64>() {
            if let (Some(from), Some(to)) = (try_unit(tokens[1]), try_unit(tokens[2])) {
                return Ok(format!(
                    "{} {} = {} {}",
                    n(value),
                    from.label,
                    n(value * from.seconds / to.seconds),
                    to.label
                ));
            }
        }
    }
    Ok(breakdown(parse_duration(input)?))
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn parses_every_accepted_spelling() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        assert_eq!(parse_duration("90").unwrap(), 90.0, "bare number is seconds");
        assert_eq!(parse_duration("1.5h").unwrap(), 5400.0);
        assert_eq!(parse_duration("1d 2h 30m").unwrap(), 86400.0 + 7200.0 + 1800.0);
        assert_eq!(parse_duration("500ms").unwrap(), 0.5);
        assert_eq!(parse_duration("2:30:00").unwrap(), 9000.0, "h:m:s");
        assert_eq!(parse_duration("90:00").unwrap(), 5400.0, "m:s");
        assert!(parse_duration("").is_err());
        assert!(parse_duration("5 furlongs").is_err(), "unknown unit is rejected");
    }

    #[test]
    fn breakdown_drops_leading_zero_units_only() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        let first = breakdown(9000.0);
        let compact = first.lines().next().unwrap();
        assert_eq!(compact, "2h 30m 0s", "zero seconds survive once hours appeared");
        assert_eq!(breakdown(0.0).lines().next().unwrap(), "0s");
        assert_eq!(breakdown(-90.0).lines().next().unwrap(), "-1m 30s");
    }

    #[test]
    fn breakdown_emits_valid_iso8601() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        assert_eq!(breakdown(9000.0).lines().nth(1).unwrap(), "PT2H30M");
        assert_eq!(breakdown(0.0).lines().nth(1).unwrap(), "PT0S");
        assert_eq!(breakdown(86400.0).lines().nth(1).unwrap(), "P1D");
        assert!(breakdown(-90.0).lines().nth(1).unwrap().starts_with('-'));
    }

    #[test]
    fn direct_conversion_when_two_units_are_given() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        assert_eq!(duration("5 h s").unwrap(), "5 hours = 18000 seconds");
        assert_eq!(duration("1 day h").unwrap(), "1 days = 24 hours");
        // Only two units means a breakdown, not a conversion.
        assert!(duration("5 h").unwrap().lines().count() > 3);
    }

    #[test]
    fn every_unit_is_listed_in_the_breakdown() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        let lines = breakdown(3600.0);
        assert!(lines.contains("HOURS         1"), "{lines}");
        assert!(lines.contains("MINUTES       60"), "{lines}");
        assert!(lines.contains("SECONDS       3600"), "{lines}");
        assert_eq!(lines.lines().count(), 2 + TIME_UNITS.len());
    }
}
