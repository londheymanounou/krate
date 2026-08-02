//! Wall-clock time across timezones. Mirrors `Dates.Timezone`.
//!
//! The zone data comes from `chrono-tz`, which compiles the IANA tzdb in — pure Rust, and it adds
//! only ~65 KB to the library. .NET on Windows accepts the same IANA ids and resolves them through
//! ICU, so the two agree on present-day offsets. Historical dates are where two tzdb snapshots can
//! disagree; the parity test covers a spread of years so any such gap is visible rather than
//! assumed away.

use crate::civil::Civil;
use crate::i18n;
use chrono::{Offset, TimeZone};

/// City names → IANA ids, so nobody has to know "Europe/Paris". Raw IANA ids work directly.
const ALIASES: &[(&str, &str)] = &[
    ("utc", "UTC"), ("gmt", "UTC"), ("z", "UTC"),
    ("paris", "Europe/Paris"), ("london", "Europe/London"), ("berlin", "Europe/Berlin"),
    ("madrid", "Europe/Madrid"), ("rome", "Europe/Rome"), ("moscow", "Europe/Moscow"),
    ("newyork", "America/New_York"), ("nyc", "America/New_York"), ("ny", "America/New_York"),
    ("losangeles", "America/Los_Angeles"), ("la", "America/Los_Angeles"), ("sf", "America/Los_Angeles"),
    ("chicago", "America/Chicago"), ("denver", "America/Denver"), ("toronto", "America/Toronto"),
    ("saopaulo", "America/Sao_Paulo"), ("mexicocity", "America/Mexico_City"),
    ("tokyo", "Asia/Tokyo"), ("shanghai", "Asia/Shanghai"), ("beijing", "Asia/Shanghai"),
    ("hongkong", "Asia/Hong_Kong"), ("singapore", "Asia/Singapore"), ("seoul", "Asia/Seoul"),
    ("dubai", "Asia/Dubai"), ("mumbai", "Asia/Kolkata"), ("delhi", "Asia/Kolkata"),
    ("kolkata", "Asia/Kolkata"),
    ("sydney", "Australia/Sydney"), ("auckland", "Pacific/Auckland"),
];

const DEFAULT_ZONES: [&str; 5] =
    ["UTC", "America/New_York", "Europe/London", "Europe/Paris", "Asia/Tokyo"];

/// Resolves a name to `(id as printed, zone)`.
///
/// The alias lookup strips spaces and underscores and ignores case, but the **id printed** is the
/// original name when it is not an alias — so "America/New_York" prints as written.
fn zone(name: &str) -> Result<(String, chrono_tz::Tz), String> {
    let key = name.replace([' ', '_'], "").to_lowercase();
    let id = match ALIASES.iter().find(|(alias, _)| *alias == key) {
        Some((_, mapped)) => (*mapped).to_string(),
        None => name.to_string(),
    };
    match id.parse::<chrono_tz::Tz>() {
        Ok(tz) => Ok((id, tz)),
        Err(_) => Err(i18n::format("Error_UnknownZone", &[name])),
    }
}

/// `TimeSpan.TryParse` with invariant rules, as far as this tool needs it: `[-][d.]hh[:mm[:ss]]`,
/// and a bare number is a count of **days**, which is what .NET does.
fn parse_timespan(text: &str) -> Option<i64> {
    let text = text.trim();
    let (negative, rest) = match text.strip_prefix('-') {
        Some(rest) => (true, rest),
        None => (false, text),
    };
    if rest.is_empty() {
        return None;
    }

    // An optional leading "d." before the clock part.
    let (days, clock) = match rest.split_once('.') {
        Some((d, c)) if !c.contains('.') && c.contains(':') => (d.parse::<i64>().ok()?, c),
        _ => (0, rest),
    };

    let seconds = if clock.contains(':') {
        let parts: Vec<&str> = clock.split(':').collect();
        if parts.len() > 3 {
            return None;
        }
        let hours: i64 = parts[0].parse().ok()?;
        let minutes: i64 = parts.get(1).map_or(Ok(0), |p| p.parse()).ok()?;
        let secs: i64 = parts.get(2).map_or(Ok(0), |p| p.parse()).ok()?;
        if !(0..60).contains(&minutes) || !(0..60).contains(&secs) {
            return None;
        }
        hours * 3600 + minutes * 60 + secs
    } else {
        // A bare number is days, not hours.
        return clock.parse::<i64>().ok().map(|d| {
            let total = (days + d) * 86_400;
            if negative { -total } else { total }
        });
    };

    let total = days * 86_400 + seconds;
    Some(if negative { -total } else { total })
}

/// `DateTime.TryParse(s, InvariantCulture)` — the invariant subset `dates.rs` already covers.
fn parse_invariant_datetime(text: &str) -> Option<Civil> {
    crate::dates::parse_date_invariant(text).ok()
}

fn looks_like_time(text: &str) -> bool {
    text.eq_ignore_ascii_case("now")
        || parse_timespan(text).is_some()
        || parse_invariant_datetime(text).is_some()
}

/// The UTC instant a time token refers to.
///
/// "now" is absolute; a clock time is that time **today in the source zone**; a full date and time
/// is likewise read as local to the source zone.
fn resolve_instant(token: &str, source: chrono_tz::Tz) -> Result<chrono::DateTime<chrono::Utc>, String> {
    if token.eq_ignore_ascii_case("now") {
        return Ok(utc_now());
    }
    let bad = || i18n::format("Error_BadDate", &[token]);

    if let Some(offset) = parse_timespan(token) {
        // Midnight today in the source zone, plus the offset.
        let there = utc_now().with_timezone(&source);
        let midnight = there
            .date_naive()
            .and_hms_opt(0, 0, 0)
            .ok_or_else(bad)?;
        let local = midnight + chrono::TimeDelta::seconds(offset);
        return local_to_utc(local, source).ok_or_else(bad);
    }

    let parsed = parse_invariant_datetime(token).ok_or_else(bad)?;
    let naive = chrono::NaiveDate::from_ymd_opt(parsed.year as i32, parsed.month, parsed.day)
        .and_then(|d| d.and_hms_opt(parsed.hour, parsed.minute, parsed.second))
        .ok_or_else(bad)?;
    local_to_utc(naive, source).ok_or_else(bad)
}

/// The current instant, from `clock.rs` rather than chrono's own clock.
///
/// chrono's `clock` feature pulls in `iana-time-zone`, which cannot link here — the same `dlltool`
/// failure that made `csprng.rs` necessary. The system clock is already available without it.
fn utc_now() -> chrono::DateTime<chrono::Utc> {
    let seconds = crate::clock::utc_now().to_unix_seconds();
    chrono::DateTime::from_timestamp(seconds, 0).unwrap_or_default()
}

/// A wall-clock time in a zone as a UTC instant.
///
/// `TimeZoneInfo.GetUtcOffset` picks the standard offset for a time that does not exist (the spring
/// gap) and the *earlier* of the two for an ambiguous time (the autumn overlap); `from_local_datetime`
/// is asked for the same.
fn local_to_utc(
    local: chrono::NaiveDateTime,
    zone: chrono_tz::Tz,
) -> Option<chrono::DateTime<chrono::Utc>> {
    match zone.from_local_datetime(&local) {
        chrono::LocalResult::Single(t) => Some(t.with_timezone(&chrono::Utc)),
        // Ambiguous: the earlier of the two, which is the one still on daylight time.
        chrono::LocalResult::Ambiguous(earlier, _) => Some(earlier.with_timezone(&chrono::Utc)),
        // Nonexistent: shift by the gap, giving the standard-offset reading.
        chrono::LocalResult::None => {
            let shifted = local + chrono::TimeDelta::hours(1);
            match zone.from_local_datetime(&shifted) {
                chrono::LocalResult::Single(t) => Some(t.with_timezone(&chrono::Utc)),
                chrono::LocalResult::Ambiguous(earlier, _) => Some(earlier.with_timezone(&chrono::Utc)),
                chrono::LocalResult::None => None,
            }
        }
    }
}

/// `$"{id,-20} {there:yyyy-MM-dd HH:mm} {there:zzz}"`.
fn line(id: &str, zone: chrono_tz::Tz, instant: chrono::DateTime<chrono::Utc>) -> String {
    let there = instant.with_timezone(&zone);
    let total = there.offset().fix().local_minus_utc();
    let sign = if total < 0 { '-' } else { '+' };
    let hours = total.abs() / 3600;
    let minutes = (total.abs() % 3600) / 60;
    format!(
        "{id:<20} {} {sign}{hours:02}:{minutes:02}",
        there.format("%Y-%m-%d %H:%M")
    )
}

/// "14:30 paris tokyo" — a wall-clock time in one zone shown in others. "now nyc london", or just
/// "tokyo" (meaning now) also work.
pub fn timezone(input: &str) -> Result<String, String> {
    let tokens: Vec<&str> = input
        .split([' ', ',', '\t', '\n'])
        .map(str::trim)
        .filter(|t| !t.is_empty())
        .collect();
    if tokens.is_empty() {
        return Err(i18n::get("Error_TimezoneUsage").to_string());
    }

    // The first token is the time only if it reads as one; otherwise everything is a zone.
    let (time_token, zone_names): (&str, &[&str]) = if looks_like_time(tokens[0]) {
        (tokens[0], &tokens[1..])
    } else {
        ("now", &tokens[..])
    };
    if zone_names.is_empty() {
        return Err(i18n::get("Error_TimezoneUsage").to_string());
    }

    let (source_id, source) = zone(zone_names[0])?;
    let instant = resolve_instant(time_token, source)?;

    let mut lines = vec![line(&source_id, source, instant)];
    let targets: Vec<&str> = if zone_names.len() > 1 {
        zone_names[1..].to_vec()
    } else {
        DEFAULT_ZONES.to_vec()
    };
    for name in targets {
        let (id, tz) = zone(name)?;
        lines.push(line(&id, tz, instant));
    }
    Ok(lines.join("\n"))
}

#[cfg(test)]
mod tests {
    use super::*;

    fn english() -> std::sync::MutexGuard<'static, ()> {
        let guard = crate::i18n::test_lock();
        i18n::set_language("en");
        guard
    }

    #[test]
    fn aliases_resolve_and_print_their_iana_id() {
        let _guard = english();
        assert_eq!(zone("paris").unwrap().0, "Europe/Paris");
        assert_eq!(zone("NYC").unwrap().0, "America/New_York");
        assert_eq!(zone("New York").unwrap().0, "America/New_York");
        assert_eq!(zone("new_york").unwrap().0, "America/New_York");
        assert_eq!(zone("utc").unwrap().0, "UTC");
        // A raw IANA id prints exactly as written, underscores and all.
        assert_eq!(zone("America/New_York").unwrap().0, "America/New_York");
        assert_eq!(zone("Asia/Tokyo").unwrap().0, "Asia/Tokyo");
        assert!(zone("Nowhere/Fictional").is_err());
        assert!(zone("").is_err());
    }

    /// A bare number is days to `TimeSpan.Parse`, which is surprising but is the behaviour.
    #[test]
    fn timespans_follow_dotnet_rules() {
        assert_eq!(parse_timespan("14:30"), Some(14 * 3600 + 30 * 60));
        assert_eq!(parse_timespan("1:02:03"), Some(3600 + 2 * 60 + 3));
        assert_eq!(parse_timespan("0:00"), Some(0));
        assert_eq!(parse_timespan("5"), Some(5 * 86_400), "a bare number is days");
        assert_eq!(parse_timespan("-1:00"), Some(-3600));
        assert_eq!(parse_timespan("2.03:00"), Some(2 * 86_400 + 3 * 3600));
        // Out of range components are not a TimeSpan.
        assert_eq!(parse_timespan("1:60"), None);
        assert_eq!(parse_timespan("1:00:60"), None);
        assert_eq!(parse_timespan("paris"), None);
        assert_eq!(parse_timespan(""), None);
    }

    #[test]
    fn a_zone_name_is_not_mistaken_for_a_time() {
        assert!(looks_like_time("now"));
        assert!(looks_like_time("NOW"));
        assert!(looks_like_time("14:30"));
        assert!(looks_like_time("2024-01-15"));
        assert!(!looks_like_time("paris"));
        assert!(!looks_like_time("Europe/Paris"));
        assert!(!looks_like_time("nyc"));
    }

    #[test]
    fn a_clock_time_is_read_in_the_source_zone() {
        let _guard = english();
        let out = timezone("14:30 paris tokyo").unwrap();
        let lines: Vec<&str> = out.lines().collect();
        assert_eq!(lines.len(), 2);
        assert!(lines[0].starts_with("Europe/Paris        "), "{}", lines[0]);
        assert!(lines[0].contains("14:30"), "the source shows the time asked for: {}", lines[0]);
        assert!(lines[1].starts_with("Asia/Tokyo          "), "{}", lines[1]);
        // Tokyo is +09:00 all year.
        assert!(lines[1].ends_with("+09:00"), "{}", lines[1]);
    }

    #[test]
    fn a_full_date_pins_the_offset_to_that_date() {
        let _guard = english();
        // Paris is +01:00 in January and +02:00 in July.
        let winter = timezone("2024-01-15 paris").unwrap();
        assert!(winter.lines().next().unwrap().ends_with("+01:00"), "{winter}");
        let summer = timezone("2024-07-15 paris").unwrap();
        assert!(summer.lines().next().unwrap().ends_with("+02:00"), "{summer}");
        // UTC never shifts.
        assert!(timezone("2024-07-15 utc").unwrap().lines().next().unwrap().ends_with("+00:00"));
    }

    #[test]
    fn no_targets_falls_back_to_the_default_list() {
        let _guard = english();
        let out = timezone("tokyo").unwrap();
        let lines: Vec<&str> = out.lines().collect();
        // The source, then the five defaults.
        assert_eq!(lines.len(), 6, "{out}");
        assert!(lines[0].starts_with("Asia/Tokyo"), "{out}");
        assert!(lines[1].starts_with("UTC"), "{out}");
        assert!(lines[5].starts_with("Asia/Tokyo"), "{out}");
    }

    #[test]
    fn every_line_is_shaped_the_same() {
        let _guard = english();
        for line in timezone("now utc paris tokyo").unwrap().lines() {
            // Twenty columns of id, then "yyyy-MM-dd HH:mm", then the offset.
            assert_eq!(&line[20..21], " ", "{line}");
            let rest = &line[21..];
            assert_eq!(rest.len(), "2024-01-15 14:30 +01:00".len(), "{line}");
            assert!(rest.as_bytes()[4] == b'-' && rest.as_bytes()[7] == b'-', "{line}");
            assert!(rest.ends_with(":00") || rest.ends_with(":30") || rest.ends_with(":45"), "{line}");
        }
    }

    #[test]
    fn bad_requests_are_refused() {
        let _guard = english();
        assert!(timezone("").is_err());
        assert!(timezone("   ").is_err());
        assert!(timezone("14:30").is_err(), "a time with no zone is not a request");
        assert!(timezone("now").is_err());
        assert!(timezone("paris nowhere").is_err());
        assert!(timezone("nowhere").is_err());
    }

    /// Zones on a half-hour or three-quarter-hour offset are where a naive formatter goes wrong.
    #[test]
    fn fractional_offsets_are_printed_correctly() {
        let _guard = english();
        let out = timezone("2024-01-15 utc Asia/Kolkata Australia/Eucla Pacific/Chatham").unwrap();
        assert!(out.contains("+05:30"), "Kolkata is +05:30:\n{out}");
        assert!(out.contains("+08:45"), "Eucla is +08:45:\n{out}");
        assert!(out.contains("+13:45"), "Chatham is +13:45 in January:\n{out}");
    }

    /// A negative offset must print its sign, not wrap.
    #[test]
    fn western_zones_print_a_negative_offset() {
        let _guard = english();
        let out = timezone("2024-01-15 utc America/New_York America/Los_Angeles").unwrap();
        assert!(out.contains("-05:00"), "New York in January:\n{out}");
        assert!(out.contains("-08:00"), "Los Angeles in January:\n{out}");
    }
}
