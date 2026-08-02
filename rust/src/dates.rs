//! Date parsing and formatting, and the tools built on them. Mirrors the parts of
//! `Krate.Core.Dates` that do not need a timezone database.
//!
//! Two pieces here, both deliberately hand-rolled against `cultures.rs`:
//!
//! * A renderer for .NET's custom date format strings, so `ToString("D", culture)` and friends
//!   come out with the culture's own pattern, month names and separators.
//! * A parser covering a **defined subset** of what `DateTimeOffset.TryParse` accepts. .NET's
//!   parser takes dozens of layouts per culture; reproducing all of it is a large surface with a
//!   lot to get subtly wrong. This takes ISO 8601, the culture's own numeric order (so `dd/MM/yyyy`
//!   for French, `M/d/yyyy` for English, exactly as .NET orders them), and month names. Anything
//!   more exotic is refused rather than guessed at — see `parse_date`.

use crate::civil::{days_in_month, Civil};
use crate::cultures::{Culture, CULTURES};
use crate::i18n;

pub(crate) fn culture() -> &'static Culture {
    let tag = i18n::language();
    CULTURES
        .iter()
        .find(|c| c.tag == tag)
        .unwrap_or_else(|| CULTURES.iter().find(|c| c.tag == "en").expect("en is always present"))
}

/// Renders a .NET custom date/time format string.
///
/// Supports the specifiers that appear in the 17 cultures' patterns plus the ones the tools use
/// directly: `d dd ddd dddd M MM MMM MMMM y yy yyyy h hh H HH m mm s ss t tt`, single-quoted
/// literals (Spanish's `'de'`), and `\` escapes. An unknown letter is emitted as itself.
pub(crate) fn format_pattern(when: Civil, pattern: &str, culture: &Culture) -> String {
    let chars: Vec<char> = pattern.chars().collect();
    // .NET switches MMMM to the genitive month name whenever the pattern also carries a standalone
    // day number, which is why Polish writes "1 stycznia" in a long date but "styczen" alone.
    let genitive = has_day_number(&chars);
    let mut out = String::with_capacity(pattern.len() + 8);
    let mut i = 0;
    while i < chars.len() {
        let c = chars[i];
        // How many of this same letter follow, which is what selects the width.
        let run = chars[i..].iter().take_while(|&&x| x == c).count();
        let hour12 = match when.hour % 12 {
            0 => 12,
            h => h,
        };
        match c {
            '\'' => {
                // A quoted literal, emitted verbatim.
                i += 1;
                while i < chars.len() && chars[i] != '\'' {
                    out.push(chars[i]);
                    i += 1;
                }
                i += 1; // the closing quote
                continue;
            }
            '\\' => {
                if let Some(&next) = chars.get(i + 1) {
                    out.push(next);
                }
                i += 2;
                continue;
            }
            'd' => {
                match run {
                    1 => out.push_str(&when.day.to_string()),
                    2 => out.push_str(&format!("{:02}", when.day)),
                    // Abbreviated day names are their own data, not a prefix of the full name:
                    // German gives "So", French "dim.", Chinese a different character entirely.
                    3 => out.push_str(culture.abbreviated_days[when.day_of_week() as usize]),
                    _ => out.push_str(culture.days[when.day_of_week() as usize]),
                }
                i += run;
                continue;
            }
            'M' => {
                let index = (when.month - 1) as usize;
                match run {
                    1 => out.push_str(&when.month.to_string()),
                    2 => out.push_str(&format!("{:02}", when.month)),
                    3 => out.push_str(culture.abbreviated_months[index]),
                    _ if genitive => out.push_str(culture.genitive_months[index]),
                    _ => out.push_str(culture.months[index]),
                }
                i += run;
                continue;
            }
            'y' => {
                match run {
                    1 => out.push_str(&(when.year % 100).to_string()),
                    2 => out.push_str(&format!("{:02}", when.year % 100)),
                    _ => out.push_str(&format!("{:0width$}", when.year, width = run)),
                }
                i += run;
                continue;
            }
            'H' => {
                out.push_str(&pad(when.hour, run));
                i += run;
                continue;
            }
            'h' => {
                out.push_str(&pad(hour12, run));
                i += run;
                continue;
            }
            'm' => {
                out.push_str(&pad(when.minute, run));
                i += run;
                continue;
            }
            's' => {
                out.push_str(&pad(when.second, run));
                i += run;
                continue;
            }
            't' => {
                let designator = if when.hour < 12 { culture.am } else { culture.pm };
                if run == 1 {
                    out.push_str(&designator.chars().take(1).collect::<String>());
                } else {
                    out.push_str(designator);
                }
                i += run;
                continue;
            }
            other => {
                out.push(other);
                i += 1;
            }
        }
    }
    out
}

/// Whether a pattern contains a standalone `d` or `dd` day-of-month token, ignoring quoted
/// literals. `ddd` and `dddd` are day *names* and do not count.
fn has_day_number(chars: &[char]) -> bool {
    let mut i = 0;
    while i < chars.len() {
        match chars[i] {
            '\'' => {
                i += 1;
                while i < chars.len() && chars[i] != '\'' {
                    i += 1;
                }
                i += 1;
            }
            '\\' => i += 2,
            'd' => {
                let run = chars[i..].iter().take_while(|&&c| c == 'd').count();
                if run <= 2 {
                    return true;
                }
                i += run;
            }
            _ => i += 1,
        }
    }
    false
}

fn pad(value: u32, width: usize) -> String {
    if width == 1 {
        value.to_string()
    } else {
        format!("{value:02}")
    }
}

/// `ToString("D", culture)` — the long date.
pub(crate) fn long_date(when: Civil, culture: &Culture) -> String {
    format_pattern(when, culture.long_date, culture)
}

/// `ToString("F", culture)` — long date, a space, long time.
pub(crate) fn full_date_time(when: Civil, culture: &Culture) -> String {
    format!(
        "{} {}",
        format_pattern(when, culture.long_date, culture),
        format_pattern(when, culture.long_time, culture)
    )
}

/// `ToString("g", culture)` — short date, a space, short time.
pub(crate) fn general_date_time(when: Civil, culture: &Culture) -> String {
    format!(
        "{} {}",
        format_pattern(when, culture.short_date, culture),
        format_pattern(when, culture.short_time, culture)
    )
}

/// Formats an integer with the culture's group separator, as `ToString("N0", culture)` does.
///
/// Group sizes are a list, not a single number: Hindi groups as `12,34,567` with sizes `[3, 2]`.
pub(crate) fn group_number(value: i64, culture: &Culture) -> String {
    let negative = value < 0;
    let digits = value.unsigned_abs().to_string();
    let sizes = culture.number_group_sizes;
    if sizes.is_empty() || sizes[0] == 0 {
        return if negative { format!("-{digits}") } else { digits };
    }

    let bytes: Vec<char> = digits.chars().collect();
    let mut groups: Vec<String> = Vec::new();
    let mut remaining = bytes.len();
    let mut which = 0usize;
    while remaining > 0 {
        // The last size repeats for every further group, which is how [3, 2] gives 12,34,567.
        let size = *sizes.get(which).unwrap_or(sizes.last().unwrap()) as usize;
        let size = if size == 0 { remaining } else { size.min(remaining) };
        groups.push(bytes[remaining - size..remaining].iter().collect());
        remaining -= size;
        which += 1;
    }
    groups.reverse();
    let joined = groups.join(culture.number_group_separator);
    if negative {
        format!("-{joined}")
    } else {
        joined
    }
}

/// Splits input into runs of digits and runs of letters, dropping separators.
fn number_tokens(text: &str) -> Vec<String> {
    let mut tokens = Vec::new();
    let mut current = String::new();
    let mut current_is_digit = false;
    for c in text.chars() {
        let kind = if c.is_ascii_digit() {
            Some(true)
        } else if c.is_alphabetic() {
            Some(false)
        } else {
            None
        };
        match kind {
            Some(is_digit) if !current.is_empty() && is_digit == current_is_digit => current.push(c),
            Some(is_digit) => {
                if !current.is_empty() {
                    tokens.push(std::mem::take(&mut current));
                }
                current_is_digit = is_digit;
                current.push(c);
            }
            None => {
                if !current.is_empty() {
                    tokens.push(std::mem::take(&mut current));
                }
            }
        }
    }
    if !current.is_empty() {
        tokens.push(current);
    }
    tokens
}

/// Which field a numeric date leads with.
#[derive(Clone, Copy, PartialEq, Eq, Debug)]
enum Order {
    YearMonthDay,
    DayMonthYear,
    MonthDayYear,
}

/// The order the culture's own short pattern uses.
fn culture_order(culture: &Culture) -> Order {
    for c in culture.short_date.chars() {
        match c {
            'y' => return Order::YearMonthDay,
            'd' => return Order::DayMonthYear,
            'M' => return Order::MonthDayYear,
            _ => {}
        }
    }
    Order::MonthDayYear
}

/// Reads three numbers in a given order, or None if they do not make a real date.
///
/// `Dates.ParseDate` tries the culture and then the invariant culture, and the invariant order is
/// month-day-year — which is why `01/15/2024` parses in French (day-month fails, invariant saves
/// it) while `15/01/2024` is refused in English (both orders want 15 as the month).
fn read_three(a: i64, b: i64, c: i64, order: Order) -> Option<(i64, u32, i64)> {
    let (year, month, day) = match order {
        Order::YearMonthDay => (expand_year(a), b, c),
        Order::DayMonthYear => (expand_year(c), b, a),
        Order::MonthDayYear => (expand_year(c), a, b),
    };
    let month = u32::try_from(month).ok()?;
    if !(1..=12).contains(&month) || day < 1 || u32::try_from(day).ok()? > days_in_month(year, month) {
        return None;
    }
    Some((year, month, day))
}

fn month_from_name(token: &str, culture: &Culture) -> Option<u32> {
    let lower = token.to_lowercase();
    let matches = |name: &str| {
        let name = name.to_lowercase();
        !name.is_empty() && (name == lower || name.starts_with(&lower) && lower.len() >= 3)
    };
    culture
        .months
        .iter()
        .position(|m| matches(m))
        .or_else(|| culture.abbreviated_months.iter().position(|m| matches(m)))
        // English names are accepted too, since the C# falls back to the invariant culture.
        .or_else(|| {
            const ENGLISH: [&str; 12] = [
                "january", "february", "march", "april", "may", "june", "july", "august",
                "september", "october", "november", "december",
            ];
            ENGLISH.iter().position(|m| {
                *m == lower || (lower.len() >= 3 && m.starts_with(&lower))
            })
        })
        .map(|index| index as u32 + 1)
}

/// Parses a date, and optionally a time, from human input.
///
/// The accepted subset, tried in order:
///
/// * ISO 8601 — `yyyy-MM-dd`, optionally `THH:mm[:ss]`, optionally a trailing `Z`.
/// * Three numbers in the culture's own order, separated by `/`, `-` or `.` — so `03/05/2024` is
///   5 March in English and 3 May in French, exactly as .NET reads them.
/// * A four-digit year anywhere in a three-number group pins the year regardless of position.
/// * A month name with a day and year in any order (`15 January 2024`, `January 15, 2024`).
/// * A bare `yyyy-MM` or `yyyy`, which start at the first day.
///
/// Anything else is refused with the same localized message the C# uses. This is narrower than
/// `DateTimeOffset.TryParse`, which is documented at the top of this module.
pub fn parse_date(input: &str) -> Result<Civil, String> {
    parse_date_with(input, culture())
}

/// The same parser against the invariant culture, for the callers that ask .NET for that
/// explicitly — `Dates.Timezone` parses its time token with `CultureInfo.InvariantCulture`.
///
/// The invariant short pattern is `MM/dd/yyyy` and its month names are English, which is what the
/// `en` entry carries, so it stands in exactly.
pub fn parse_date_invariant(input: &str) -> Result<Civil, String> {
    let invariant = CULTURES
        .iter()
        .find(|c| c.tag == "en")
        .expect("en is always present");
    parse_date_with(input, invariant)
}

fn parse_date_with(input: &str, culture: &Culture) -> Result<Civil, String> {
    let text = input.trim();
    let bad = || i18n::format("Error_BadDate", &[text]);
    if text.is_empty() {
        return Err(bad());
    }

    // Split off a time part if there is one.
    let (date_part, time_part) = split_time(text);
    let (hour, minute, second) = match time_part {
        Some(t) => parse_time(&t).ok_or_else(bad)?,
        None => (0, 0, 0),
    };

    let tokens = number_tokens(&date_part);
    let numbers: Vec<&String> = tokens.iter().filter(|t| t.chars().all(|c| c.is_ascii_digit())).collect();
    let words: Vec<&String> = tokens.iter().filter(|t| t.chars().any(|c| c.is_alphabetic())).collect();

    let (year, month, day) = if let Some(word) = words.first() {
        // A month name plus a day and a year.
        let month = month_from_name(word, culture).ok_or_else(bad)?;
        let (day, year) = match numbers.as_slice() {
            [a, b] => {
                let (a, b) = (a.parse::<i64>().map_err(|_| bad())?, b.parse::<i64>().map_err(|_| bad())?);
                // Whichever looks like a year is the year.
                if a > 31 { (b, a) } else { (a, b) }
            }
            [a] => (a.parse::<i64>().map_err(|_| bad())?, -1),
            _ => return Err(bad()),
        };
        if year < 0 {
            return Err(bad());
        }
        (year, month, day)
    } else {
        match numbers.as_slice() {
            [a, b, c] => {
                let n = |s: &String| s.parse::<i64>().map_err(|_| bad());
                let (first, second_, third) = (n(a)?, n(b)?, n(c)?);
                // A leading four-digit group is unambiguously a year, whatever the culture does.
                let mut orders = Vec::new();
                if a.len() == 4 {
                    orders.push(Order::YearMonthDay);
                }
                orders.push(culture_order(culture));
                orders.push(Order::MonthDayYear); // the invariant fallback
                orders
                    .iter()
                    .find_map(|&order| read_three(first, second_, third, order))
                    .ok_or_else(bad)?
            }
            // yyyy-MM starts at the first of the month.
            [a, b] if a.len() == 4 => {
                (a.parse::<i64>().map_err(|_| bad())?, b.parse::<u32>().map_err(|_| bad())?, 1)
            }
            // A bare year is NOT a date to .NET, and is refused here too.
            _ => return Err(bad()),
        }
    };

    // Reject anything the calendar does not contain, rather than silently rolling over.
    if !(1..=12).contains(&month) || day < 1 || day as u32 > days_in_month(year, month) {
        return Err(bad());
    }
    Ok(Civil { year, month, day: day as u32, hour, minute, second })
}

/// Two-digit years, the way .NET's Gregorian calendar does: `TwoDigitYearMax` is 2049, so 00-49
/// is 20xx and 50-99 is 19xx. Probed, not assumed — `1/1/30` really is 2030, not 1930.
fn expand_year(year: i64) -> i64 {
    match year {
        0..=49 => 2000 + year,
        50..=99 => 1900 + year,
        other => other,
    }
}

/// Separates `2024-01-15T10:30` or `2024-01-15 10:30` into its two halves.
fn split_time(text: &str) -> (String, Option<String>) {
    if let Some((date, time)) = text.split_once(['T', 't']) {
        // Only a real ISO 'T' between a date and a time, not the T of a month name.
        if time.starts_with(|c: char| c.is_ascii_digit()) && date.chars().any(|c| c.is_ascii_digit()) {
            return (date.to_string(), Some(time.to_string()));
        }
    }
    // Otherwise the time is whatever follows the last space and contains a colon.
    if let Some(index) = text.rfind(' ') {
        let tail = &text[index + 1..];
        if tail.contains(':') {
            return (text[..index].to_string(), Some(tail.to_string()));
        }
    }
    (text.to_string(), None)
}

fn parse_time(text: &str) -> Option<(u32, u32, u32)> {
    let text = text.trim().trim_end_matches(['Z', 'z']);
    let mut parts = text.split(':');
    let hour: u32 = parts.next()?.trim().parse().ok()?;
    let minute: u32 = parts.next().unwrap_or("0").trim().parse().ok()?;
    // Fractional seconds are accepted and dropped, as the tools only show whole seconds.
    let second_text = parts.next().unwrap_or("0");
    let second: u32 = second_text.split('.').next()?.trim().parse().ok()?;
    if hour > 23 || minute > 59 || second > 59 {
        return None;
    }
    Some((hour, minute, second))
}

/// Empty means now; a number is a Unix timestamp; anything else is a date. One box, both ways.
pub fn timestamp(input: &str) -> Result<String, String> {
    let text = input.trim();
    let utc = if text.is_empty() {
        crate::clock::utc_now()
    } else if let Some(number) = whole_number(text) {
        // Ten digits is seconds, thirteen is milliseconds — resolved by magnitude, as the C# does.
        if number.abs() > 100_000_000_000 {
            Civil::from_unix_seconds(number.div_euclid(1000))
        } else {
            Civil::from_unix_seconds(number)
        }
    } else {
        // A parsed date has no offset, so .NET treats it as local time.
        crate::clock::to_utc(parse_date(text)?)
    };
    Ok(describe_instant(utc))
}

/// `long.TryParse` with invariant rules.
fn whole_number(text: &str) -> Option<i64> {
    let body = text.strip_prefix(['+', '-']).unwrap_or(text);
    if body.is_empty() || !body.chars().all(|c| c.is_ascii_digit()) {
        return None;
    }
    text.parse::<i64>().ok()
}

fn describe_instant(utc: Civil) -> String {
    let seconds = utc.to_unix_seconds();
    let local = crate::clock::to_local(utc);
    [
        format!("UNIX   {seconds}"),
        format!("MS     {}", seconds * 1000),
        format!(
            "ISO    {:04}-{:02}-{:02}T{:02}:{:02}:{:02}Z",
            utc.year, utc.month, utc.day, utc.hour, utc.minute, utc.second
        ),
        format!("LOCAL  {}", full_date_time(local, culture())),
    ]
    .join("\n")
}

/// Name, size, timestamps and SHA-256 of a file.
pub fn file_details(input: &str) -> Result<String, String> {
    let path = std::path::PathBuf::from(input.trim().trim_matches('"'));
    if !path.is_file() {
        return Err(i18n::format("Error_NoFile", &[&path.to_string_lossy()]));
    }
    let metadata = std::fs::metadata(&path).map_err(|e| e.to_string())?;
    let culture = culture();

    // The filesystem reports UTC; the C# shows local, using the offset in force at that instant.
    let stamp = |time: std::io::Result<std::time::SystemTime>| match time {
        Ok(t) => {
            let seconds = t
                .duration_since(std::time::UNIX_EPOCH)
                .map(|d| d.as_secs() as i64)
                .unwrap_or(0);
            let local = crate::clock::to_local(Civil::from_unix_seconds(seconds));
            general_date_time(local, culture)
        }
        // Some filesystems do not record a creation time; the C# would show the epoch.
        Err(_) => general_date_time(crate::clock::to_local(Civil::from_unix_seconds(0)), culture),
    };

    let size = metadata.len() as i64;
    Ok([
        format!(
            "{}  {}",
            i18n::get("Files_Name"),
            path.file_name().unwrap_or_default().to_string_lossy()
        ),
        format!(
            "{}  {} ({} B)",
            i18n::get("Files_Size"),
            crate::physics::human_size(size),
            group_number(size, culture)
        ),
        format!("{}  {}", i18n::get("Files_Created"), stamp(metadata.created())),
        format!("{}  {}", i18n::get("Files_Modified"), stamp(metadata.modified())),
        String::new(),
        format!("SHA-256  {}", crate::hashing::sha256_file(&path)?),
    ]
    .join("\n"))
}

/// Weekdays between two dates, end excluded. Public holidays are not counted: they differ per
/// country and would need a table per locale.
pub fn business_days(from: Civil, to: Civil) -> i64 {
    let mut days = 0;
    let mut d = from;
    while d < to {
        if !matches!(d.day_of_week(), 0 | 6) {
            days += 1;
        }
        d = d.add_days(1);
    }
    days
}

/// Two dates, or one to compare against today. Also answers "how old am I".
pub fn difference(input: &str) -> Result<String, String> {
    let parts: Vec<&str> = input
        .split([' ', '\n', '\t', ';'])
        .map(str::trim)
        .filter(|p| !p.is_empty())
        .collect();
    if parts.is_empty() {
        return Err(i18n::get("Error_NeedDate").to_string());
    }
    let mut a = parse_date(parts[0])?.date_only();
    let mut b = if parts.len() > 1 {
        parse_date(parts[1])?.date_only()
    } else {
        crate::clock::today()
    };
    if b < a {
        std::mem::swap(&mut a, &mut b);
    }

    // Calendar-correct: a month is what the calendar says. Walking forward is immune to the
    // 31 Jan -> 1 Mar traps that arithmetic on the parts falls into.
    let mut years = 0i64;
    while a.add_years(years + 1) <= b {
        years += 1;
    }
    let mut months = 0i64;
    while a.add_years(years).add_months(months + 1) <= b {
        months += 1;
    }
    let days = a.add_years(years).add_months(months).days_until(b);
    let total = a.days_until(b);

    Ok([
        i18n::format(
            "Dates_Exact",
            &[&years.to_string(), &months.to_string(), &days.to_string()],
        ),
        i18n::format("Dates_TotalDays", &[&total.to_string()]),
        i18n::format(
            "Dates_TotalWeeks",
            &[&(total / 7).to_string(), &(total % 7).to_string()],
        ),
        i18n::format("Dates_BusinessDays", &[&business_days(a, b).to_string()]),
    ]
    .join("\n"))
}

/// English `DayOfWeek` names: `date.DayOfWeek.ToString()` is the enum name, which is English
/// whatever the culture, and the C# takes its first three characters.
const DAY_OF_WEEK_NAMES: [&str; 7] = [
    "Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday",
];

pub fn week_info(input: &str) -> Result<String, String> {
    let text = input.trim();
    let date = if text.is_empty() {
        crate::clock::today()
    } else {
        parse_date(text)?.date_only()
    };
    let culture = culture();
    let (iso_year, iso_week) = crate::civil::iso_week(date);
    let days_in_year = if crate::civil::is_leap_year(date.year) { 366 } else { 365 };
    let quarter = (date.month - 1) / 3 + 1;
    let weekend = matches!(date.day_of_week(), 0 | 6);

    Ok([
        format!("{}  {}", i18n::get("Week_Date"), long_date(date, culture)),
        format!(
            "{}  {iso_year}-W{iso_week:02} ({})",
            i18n::get("Week_Iso"),
            &DAY_OF_WEEK_NAMES[date.day_of_week() as usize][..3]
        ),
        format!(
            "{}  {} / {days_in_year}",
            i18n::get("Week_DayOfYear"),
            date.day_of_year()
        ),
        format!("{}  Q{quarter}", i18n::get("Week_Quarter")),
        format!(
            "{}  {}",
            i18n::get("Week_Weekend"),
            i18n::get(if weekend { "Week_Yes" } else { "Week_No" })
        ),
    ]
    .join("\n"))
}

#[cfg(test)]
mod tests {
    use super::*;

    fn with(tag: &str) -> std::sync::MutexGuard<'static, ()> {
        let guard = crate::i18n::test_lock();
        i18n::set_language(tag);
        guard
    }

    #[test]
    fn patterns_render_every_supported_specifier() {
        let _guard = with("en");
        let c = culture();
        let when = Civil { year: 2026, month: 7, day: 5, hour: 15, minute: 4, second: 9 };
        assert_eq!(format_pattern(when, "yyyy-MM-dd", c), "2026-07-05");
        assert_eq!(format_pattern(when, "d/M/yy", c), "5/7/26");
        assert_eq!(format_pattern(when, "dddd", c), "Sunday");
        assert_eq!(format_pattern(when, "ddd", c), "Sun");
        // Abbreviations come from the culture, not from truncating the full name.
        i18n::set_language("de");
        assert_eq!(format_pattern(when, "ddd", culture()), "So");
        i18n::set_language("fr");
        assert_eq!(format_pattern(when, "ddd", culture()), "dim.");
        i18n::set_language("en");
        assert_eq!(format_pattern(when, "MMMM", c), "July");
        assert_eq!(format_pattern(when, "MMM", c), "Jul");
        assert_eq!(format_pattern(when, "HH:mm:ss", c), "15:04:09");
        assert_eq!(format_pattern(when, "h:mm tt", c), "3:04 PM");
        assert_eq!(format_pattern(when, "H:m:s", c), "15:4:9");
    }

    /// Noon and midnight are where 12-hour clocks go wrong.
    #[test]
    fn the_twelve_hour_clock_handles_noon_and_midnight() {
        let _guard = with("en");
        let c = culture();
        let at = |hour| Civil { year: 2026, month: 1, day: 1, hour, minute: 0, second: 0 };
        assert_eq!(format_pattern(at(0), "h tt", c), "12 AM");
        assert_eq!(format_pattern(at(12), "h tt", c), "12 PM");
        assert_eq!(format_pattern(at(13), "h tt", c), "1 PM");
        assert_eq!(format_pattern(at(11), "h tt", c), "11 AM");
    }

    #[test]
    fn quoted_literals_survive() {
        let _guard = with("es");
        let c = culture();
        let when = Civil::date(2026, 7, 5);
        // Spanish's long date is "dddd, d 'de' MMMM 'de' yyyy".
        let out = long_date(when, c);
        assert!(out.contains(" de julio de 2026"), "{out}");
        assert!(!out.contains('\''), "the quotes are not output: {out}");
    }

    #[test]
    fn long_dates_use_the_culture_pattern_and_names() {
        let when = Civil::date(2026, 7, 5);
        let _guard = crate::i18n::test_lock();
        for (tag, expected) in [
            ("en", "Sunday, July 5, 2026"),
            ("fr", "dimanche 5 juillet 2026"),
            ("de", "Sonntag, 5. Juli 2026"),
        ] {
            i18n::set_language(tag);
            assert_eq!(long_date(when, culture()), expected, "{tag}");
        }
        i18n::set_language("en");
    }

    /// Hindi groups in lakhs, which a single "every three digits" rule would get wrong.
    #[test]
    fn number_grouping_follows_the_culture_group_sizes() {
        let _guard = crate::i18n::test_lock();
        for (tag, expected) in [
            ("en", "1,234,567"),
            ("hi", "12,34,567"),
            ("de", "1.234.567"),
        ] {
            i18n::set_language(tag);
            assert_eq!(group_number(1_234_567, culture()), expected, "{tag}");
        }
        i18n::set_language("en");
        assert_eq!(group_number(0, culture()), "0");
        assert_eq!(group_number(-1234, culture()), "-1,234");
        assert_eq!(group_number(999, culture()), "999");
    }

    #[test]
    fn iso_dates_parse_the_same_in_every_culture() {
        let _guard = crate::i18n::test_lock();
        for tag in ["en", "fr", "ja"] {
            i18n::set_language(tag);
            assert_eq!(parse_date("2024-01-15").unwrap(), Civil::date(2024, 1, 15), "{tag}");
            assert_eq!(
                parse_date("2024-01-15T10:30:45").unwrap(),
                Civil { year: 2024, month: 1, day: 15, hour: 10, minute: 30, second: 45 },
                "{tag}"
            );
            assert_eq!(parse_date("2024-01-15 10:30").unwrap().hour, 10, "{tag}");
        }
        i18n::set_language("en");
    }

    /// Every expectation here was probed against `DateTimeOffset.TryParse`, not assumed.
    #[test]
    fn ambiguous_numeric_dates_follow_the_culture_order() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("fr");
        assert_eq!(parse_date("03/05/2024").unwrap(), Civil::date(2024, 5, 3), "French is day first");
        // Day-month fails on 15 as a month, and the invariant month-first order rescues it.
        assert_eq!(parse_date("01/15/2024").unwrap(), Civil::date(2024, 1, 15));
        assert_eq!(parse_date("15/01/2024").unwrap(), Civil::date(2024, 1, 15));
        // The culture's own separator is not required.
        assert_eq!(parse_date("15.01.2024").unwrap(), Civil::date(2024, 1, 15));

        i18n::set_language("en");
        assert_eq!(parse_date("03/05/2024").unwrap(), Civil::date(2024, 3, 5), "English is month first");
        assert_eq!(parse_date("01/15/2024").unwrap(), Civil::date(2024, 1, 15));
        // Both the culture order and the invariant fallback want 15 as the month, so .NET refuses
        // this outright — and so does this.
        assert!(parse_date("15/01/2024").is_err(), "en has no day-first order to fall back on");
        assert!(parse_date("15.01.2024").is_err());
        // A leading four-digit group is a year whatever the culture.
        assert_eq!(parse_date("2024/01/15").unwrap(), Civil::date(2024, 1, 15));

        // Japanese leads with the year, so a short first token is a year there.
        i18n::set_language("ja");
        assert_eq!(parse_date("1/1/29").unwrap(), Civil::date(2001, 1, 29), "year first");
        // ...but only when it yields a real date; day 99 does not, so the fallback applies.
        assert_eq!(parse_date("1/1/99").unwrap(), Civil::date(1999, 1, 1));
        assert_eq!(parse_date("03/05/2024").unwrap(), Civil::date(2024, 3, 5));
        assert!(parse_date("15/01/2024").is_err());
        i18n::set_language("en");
    }

    #[test]
    fn month_names_parse_in_either_order() {
        // One lock for the whole test: test_lock is not reentrant.
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        assert_eq!(parse_date("15 January 2024").unwrap(), Civil::date(2024, 1, 15));
        assert_eq!(parse_date("January 15, 2024").unwrap(), Civil::date(2024, 1, 15));
        assert_eq!(parse_date("15 Jan 2024").unwrap(), Civil::date(2024, 1, 15));
        // A French month name is not an English one, and .NET refuses it under en too.
        assert!(parse_date("15 janvier 2024").is_err());

        i18n::set_language("fr");
        assert_eq!(parse_date("15 janvier 2024").unwrap(), Civil::date(2024, 1, 15));
        // English names still work elsewhere, which is what the invariant fallback provides.
        assert_eq!(parse_date("15 January 2024").unwrap(), Civil::date(2024, 1, 15));
        i18n::set_language("en");
    }

    #[test]
    fn impossible_dates_are_refused_rather_than_rolled_over() {
        let _guard = with("en");
        // "2024" alone is refused by .NET too, and "20240115" is not a date to it either.
        for bad in ["2024-02-30", "2023-02-29", "2024-13-01", "2024-00-10", "2024-01-00",
                    "nonsense", "", "   ", "99/99/9999", "2024-01-15T25:00", "2024", "20240115"] {
            assert!(parse_date(bad).is_err(), "{bad:?} should not parse");
        }
        // But a real leap day does parse.
        assert_eq!(parse_date("2024-02-29").unwrap(), Civil::date(2024, 2, 29));
    }

    /// TwoDigitYearMax is 2049, so the split is at 49 — `1/1/30` is 2030, not 1930. Probed.
    #[test]
    fn two_digit_years_split_at_forty_nine() {
        let _guard = with("en");
        assert_eq!(parse_date("1/1/29").unwrap().year, 2029);
        assert_eq!(parse_date("1/1/30").unwrap().year, 2030);
        assert_eq!(parse_date("1/1/49").unwrap().year, 2049);
        assert_eq!(parse_date("1/1/50").unwrap().year, 1950);
        assert_eq!(parse_date("1/1/99").unwrap().year, 1999);
    }

    #[test]
    fn difference_is_calendar_correct() {
        let _guard = with("en");
        let out = difference("2020-01-01 2024-03-05").unwrap();
        assert!(out.contains('4'), "four years: {out}");
        // A whole year is a year, not 365 days of drift.
        let exact = difference("2024-01-01 2025-01-01").unwrap();
        assert!(exact.contains("366") || exact.contains("365"), "{exact}");
        // Order does not matter.
        assert_eq!(
            difference("2024-03-05 2020-01-01").unwrap(),
            difference("2020-01-01 2024-03-05").unwrap()
        );
        assert!(difference("").is_err());
        assert!(difference("nonsense").is_err());
    }

    #[test]
    fn business_days_exclude_weekends() {
        // Monday 2026-07-27 to Monday 2026-08-03 is five working days.
        assert_eq!(business_days(Civil::date(2026, 7, 27), Civil::date(2026, 8, 3)), 5);
        // A single weekend day counts nothing.
        assert_eq!(business_days(Civil::date(2026, 8, 1), Civil::date(2026, 8, 3)), 0);
        assert_eq!(business_days(Civil::date(2026, 7, 27), Civil::date(2026, 7, 27)), 0);
    }

    #[test]
    fn week_info_reports_the_iso_week_and_quarter() {
        let _guard = with("en");
        let out = week_info("2026-07-05").unwrap();
        assert!(out.contains("Sunday, July 5, 2026"), "{out}");
        assert!(out.contains("2026-W27"), "{out}");
        assert!(out.contains("(Sun)"), "the abbreviation is English whatever the culture: {out}");
        assert!(out.contains("186 / 365"), "{out}");
        assert!(out.contains("Q3"), "{out}");

        // The awkward boundary: 2021-01-01 belongs to ISO week 53 of 2020.
        assert!(week_info("2021-01-01").unwrap().contains("2020-W53"));
        assert!(week_info("2024-12-31").unwrap().contains("2025-W01"));
        // A leap year has 366 days.
        assert!(week_info("2024-06-15").unwrap().contains("/ 366"));
        assert!(week_info("nonsense").is_err());
    }
}
