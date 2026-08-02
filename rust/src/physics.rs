//! Speed/distance/time and transfer-time estimates. Mirrors `Krate.Core.Physics`,
//! `Krate.Core.Transfer` and the size helpers in `Krate.Core.Files`.

use crate::duration::{breakdown, parse_duration};
use crate::i18n;

// ---------- speed, distance, time ----------

/// Unit to (kind, multiplier to base). Base units are metres, seconds and metres per second.
/// Matched case-insensitively, as the C# dictionary is.
const SDT_UNITS: &[(&str, &str, f64)] = &[
    ("m", "dist", 1.0), ("km", "dist", 1000.0), ("cm", "dist", 0.01),
    ("mi", "dist", 1609.344), ("ft", "dist", 0.3048), ("yd", "dist", 0.9144), ("nmi", "dist", 1852.0),
    ("s", "time", 1.0), ("sec", "time", 1.0), ("min", "time", 60.0),
    ("h", "time", 3600.0), ("hr", "time", 3600.0), ("d", "time", 86400.0),
    ("m/s", "speed", 1.0), ("km/h", "speed", 1.0 / 3.6), ("kmh", "speed", 1.0 / 3.6),
    ("mph", "speed", 0.44704), ("kn", "speed", 0.514444), ("kt", "speed", 0.514444),
];

fn split_number(token: &str, extra: &[char]) -> (usize, Option<f64>) {
    let chars: Vec<char> = token.chars().collect();
    let mut i = 0;
    while i < chars.len() && (chars[i].is_ascii_digit() || extra.contains(&chars[i])) {
        i += 1;
    }
    if i == 0 {
        return (0, None);
    }
    let text: String = chars[..i].iter().collect::<String>().replace(',', ".");
    let bytes = chars[..i].iter().map(|c| c.len_utf8()).sum();
    (bytes, text.parse().ok())
}

fn parse_sdt(token: &str) -> Result<(&'static str, f64), String> {
    let (split, value) = split_number(token, &['.', ',', '-', '+']);
    let value = value.ok_or_else(|| i18n::format("Error_SdtToken", &[token]))?;
    let unit = &token[split..];
    let found = SDT_UNITS
        .iter()
        .find(|(u, _, _)| u.eq_ignore_ascii_case(unit))
        .ok_or_else(|| i18n::format("Error_SdtUnit", &[unit]))?;
    Ok((found.1, value * found.2))
}

/// Give any two of speed, distance and time; it produces the third.
pub fn speed_distance_time(input: &str) -> Result<String, String> {
    let mut given: Vec<(&'static str, f64)> = Vec::new();
    for token in input.split([' ', ',', '\n', '\t']).map(str::trim).filter(|t| !t.is_empty()) {
        let (kind, value) = parse_sdt(token)?;
        if given.iter().any(|(k, _)| *k == kind) {
            return Err(i18n::format("Error_SdtDuplicate", &[kind]));
        }
        given.push((kind, value));
    }
    if given.len() != 2 {
        return Err(i18n::get("Error_SdtUsage").to_string());
    }
    let get = |kind: &str| given.iter().find(|(k, _)| *k == kind).map(|(_, v)| *v);

    let (dist, time, speed) = match (get("dist"), get("time"), get("speed")) {
        (None, Some(time), Some(speed)) => (speed * time, time, speed),
        (Some(dist), None, Some(speed)) => {
            if speed == 0.0 {
                return Err(i18n::get("Error_SdtZeroSpeed").to_string());
            }
            (dist, dist / speed, speed)
        }
        (Some(dist), Some(time), _) => {
            if time == 0.0 {
                return Err(i18n::get("Error_SdtZeroTime").to_string());
            }
            (dist, time, dist / time)
        }
        _ => return Err(i18n::get("Error_SdtUsage").to_string()),
    };

    let time_text = breakdown(time).lines().next().unwrap_or_default().to_string();
    Ok([
        format!("{}  {} km  ({} m)", i18n::get("Sdt_Distance"), round4(dist / 1000.0), round2(dist)),
        format!("{}  {time_text}", i18n::get("Sdt_Time")),
        format!("{}  {} km/h  ({} m/s)", i18n::get("Sdt_Speed"), round4(speed * 3.6), round4(speed)),
    ]
    .join("\n"))
}

/// C#'s "0.####" and "0.##": fixed decimals with trailing zeros dropped.
fn round_to_text(v: f64, decimals: usize) -> String {
    let mut s = format!("{v:.decimals$}");
    if s.contains('.') {
        s = s.trim_end_matches('0').trim_end_matches('.').to_string();
    }
    s
}

fn round4(v: f64) -> String {
    round_to_text(v, 4)
}

fn round2(v: f64) -> String {
    round_to_text(v, 2)
}

// ---------- file sizes ----------

/// "10MB", "512k", "2 GiB" to a byte count.
pub fn parse_size(input: &str) -> Result<i64, String> {
    let s = input.trim().replace(' ', "");
    let bad = || i18n::format("Error_BadSize", &[input]);
    let (split, value) = split_number(&s, &['.', ',']);
    let value = value.ok_or_else(bad)?;

    let multiplier: i64 = match s[split..].to_lowercase().as_str() {
        "" | "b" => 1,
        "k" | "kb" => 1_000,
        "m" | "mb" => 1_000_000,
        "g" | "gb" => 1_000_000_000,
        "kib" => 1024,
        "mib" => 1024 * 1024,
        "gib" => 1024 * 1024 * 1024,
        _ => return Err(bad()),
    };
    Ok((value * multiplier as f64) as i64)
}

pub fn human_size(bytes: i64) -> String {
    const UNITS: [&str; 6] = ["B", "KB", "MB", "GB", "TB", "PB"];
    let mut value = bytes as f64;
    let mut unit = 0;
    while value >= 1000.0 && unit < UNITS.len() - 1 {
        value /= 1000.0;
        unit += 1;
    }
    format!("{} {}", round2(value), UNITS[unit])
}

// ---------- transfer time ----------

fn is_bandwidth(token: &str) -> bool {
    token.to_lowercase().contains("bps") || token.contains("/s")
}

/// Bandwidth token to bits per second. Case matters: "Mbps" is megabits, "MB/s" is megabytes,
/// so this must never fold the case.
pub fn parse_bandwidth(token: &str) -> Result<f64, String> {
    let bad = || i18n::format("Error_TransferRate", &[token]);
    let (split, value) = split_number(token, &['.', ',']);
    let value = value.ok_or_else(bad)?;

    let bits_per_unit = match &token[split..] {
        "bps" | "bit/s" => 1.0,
        "kbps" | "Kbps" => 1e3,
        "Mbps" => 1e6,
        "Gbps" => 1e9,
        "Tbps" => 1e12,
        "B/s" | "Bps" => 8.0,
        "kB/s" | "KB/s" => 8e3,
        "MB/s" => 8e6,
        "GB/s" => 8e9,
        "KiB/s" => 8.0 * 1024.0,
        "MiB/s" => 8.0 * 1024.0 * 1024.0,
        "GiB/s" => 8.0 * 1024.0 * 1024.0 * 1024.0,
        _ => return Err(bad()),
    };
    Ok(value * bits_per_unit)
}

/// "1.5GB 100Mbps" — how long that transfer takes.
pub fn transfer_time(input: &str) -> Result<String, String> {
    let tokens: Vec<&str> = input
        .split([' ', ',', '\n', '\t'])
        .map(str::trim)
        .filter(|t| !t.is_empty())
        .collect();
    if tokens.len() != 2 {
        return Err(i18n::get("Error_TransferUsage").to_string());
    }

    // The bandwidth token is the one carrying a rate ("bps" or "/s"); the other is the size.
    let bw_index = tokens
        .iter()
        .position(|t| is_bandwidth(t))
        .ok_or_else(|| i18n::get("Error_TransferUsage").to_string())?;
    let bits_per_second = parse_bandwidth(tokens[bw_index])?;
    let bytes = parse_size(tokens[1 - bw_index])?;

    let seconds = bytes as f64 * 8.0 / bits_per_second;
    let time_text = breakdown(parse_duration(&round_to_text(seconds, 3))?)
        .lines()
        .next()
        .unwrap_or_default()
        .to_string();

    Ok([
        format!("{}  {} ({} bits)", i18n::get("Transfer_Size"), human_size(bytes), thousands(bytes * 8)),
        format!("{}  {} Mbps", i18n::get("Transfer_Rate"), round_to_text(bits_per_second / 1e6, 3)),
        format!("{}  {time_text}", i18n::get("Transfer_TimeLabel")),
    ]
    .join("\n"))
}

/// C#'s "N0" — group separators every three digits, invariant culture.
pub fn thousands(v: i64) -> String {
    let digits = v.abs().to_string();
    let mut out = String::new();
    for (i, c) in digits.chars().enumerate() {
        if i > 0 && (digits.len() - i).is_multiple_of(3) {
            out.push(',');
        }
        out.push(c);
    }
    if v < 0 { format!("-{out}") } else { out }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn solves_for_whichever_value_is_missing() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        // 100 km at 50 km/h takes 2 hours.
        let result = speed_distance_time("100km 50km/h").unwrap();
        assert!(result.contains("2h"), "{result}");
        // 100 km in 2 h is 50 km/h.
        assert!(speed_distance_time("100km 2h").unwrap().contains("50 km/h"));
        // 50 km/h for 2 h covers 100 km.
        assert!(speed_distance_time("50km/h 2h").unwrap().contains("100 km"));
    }

    #[test]
    fn sdt_rejects_what_it_cannot_solve() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        assert!(speed_distance_time("100km").is_err(), "needs two of the three");
        assert!(speed_distance_time("100km 50km 2h").is_err(), "duplicate kind");
        assert!(speed_distance_time("100 furlongs 2h").is_err(), "unknown unit");
        assert!(speed_distance_time("100km 0km/h").is_err(), "zero speed never arrives");
        assert!(speed_distance_time("100km 0h").is_err(), "zero time is infinite speed");
    }

    #[test]
    fn parse_size_reads_both_decimal_and_binary_prefixes() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        assert_eq!(parse_size("1000").unwrap(), 1000);
        assert_eq!(parse_size("10MB").unwrap(), 10_000_000);
        assert_eq!(parse_size("512k").unwrap(), 512_000);
        assert_eq!(parse_size("2 GiB").unwrap(), 2_147_483_648);
        assert!(parse_size("big").is_err());
        assert!(parse_size("10 furlongs").is_err());
    }

    #[test]
    fn human_size_steps_through_the_units() {
        assert_eq!(human_size(999), "999 B");
        assert_eq!(human_size(1000), "1 KB");
        assert_eq!(human_size(10_000_000), "10 MB");
    }

    /// Case is load-bearing here: Mbps is megabits, MB/s is megabytes — an eightfold difference.
    #[test]
    fn bandwidth_case_is_never_folded() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        assert_eq!(parse_bandwidth("100Mbps").unwrap(), 1e8);
        assert_eq!(parse_bandwidth("100MB/s").unwrap(), 8e8);
        assert_eq!(parse_bandwidth("1Gbps").unwrap(), 1e9);
        assert!(parse_bandwidth("100mbps").is_err(), "lower-case m is not a known rate");
    }

    #[test]
    fn transfer_time_pairs_a_size_with_a_rate_in_either_order() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        let a = transfer_time("1GB 100Mbps").unwrap();
        let b = transfer_time("100Mbps 1GB").unwrap();
        assert_eq!(a, b, "order must not matter");
        assert!(a.contains("1 GB"), "{a}");
        assert!(transfer_time("1GB").is_err(), "needs both");
        assert!(transfer_time("1GB 2GB").is_err(), "neither token is a rate");
    }

    #[test]
    fn thousands_groups_like_dotnets_n0() {
        assert_eq!(thousands(0), "0");
        assert_eq!(thousands(999), "999");
        assert_eq!(thousands(1000), "1,000");
        assert_eq!(thousands(8_000_000_000), "8,000,000,000");
        assert_eq!(thousands(-1234), "-1,234");
    }
}
