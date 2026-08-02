//! Describes a five-field cron expression in words. Mirrors `Krate.Core.Cron`.
//!
//! Month and weekday names come from the active culture via `cultures.rs`, which is .NET's own
//! `DateTimeFormatInfo` data — so "January" is spelled exactly as the C# spells it in each of the
//! 17 languages.

use crate::civil::Civil;
use crate::cultures::CULTURES;
use crate::i18n;

fn culture() -> &'static crate::cultures::Culture {
    let tag = i18n::language();
    CULTURES
        .iter()
        .find(|c| c.tag == tag)
        // English is the fallback the string catalogue uses too.
        .unwrap_or_else(|| CULTURES.iter().find(|c| c.tag == "en").expect("en is always present"))
}

/// Splits on runs of spaces, dropping empties, as `Split(' ', RemoveEmptyEntries)` does.
fn fields(input: &str) -> Vec<&str> {
    input.trim().split(' ').filter(|f| !f.is_empty()).collect()
}

fn shortcut(name: &str) -> Result<Vec<&'static str>, String> {
    let expanded = match name.to_lowercase().as_str() {
        "@yearly" | "@annually" => "0 0 1 1 *",
        "@monthly" => "0 0 1 * *",
        "@weekly" => "0 0 * * 0",
        "@daily" | "@midnight" => "0 0 * * *",
        "@hourly" => "0 * * * *",
        _ => return Err(i18n::get("Error_CronUsage").to_string()),
    };
    Ok(expanded.split(' ').collect())
}

/// The five fields, with `@shortcut` already expanded.
fn resolve(input: &str) -> Result<Vec<String>, String> {
    let parts = fields(input);
    let parts: Vec<String> = match parts.as_slice() {
        [one] if one.starts_with('@') => {
            shortcut(one)?.into_iter().map(str::to_string).collect()
        }
        other => other.iter().map(|s| s.to_string()).collect(),
    };
    if parts.len() != 5 {
        return Err(i18n::get("Error_CronUsage").to_string());
    }
    Ok(parts)
}

/// `int.TryParse` with invariant rules — a whole field that is just a number.
fn single(field: &str) -> Option<i64> {
    let trimmed = field.trim();
    let body = trimmed.strip_prefix(['+', '-']).unwrap_or(trimmed);
    if body.is_empty() || !body.chars().all(|c| c.is_ascii_digit()) {
        return None;
    }
    trimmed.parse::<i64>().ok()
}

pub fn describe(input: &str) -> Result<String, String> {
    let parts = resolve(input)?;
    let (minute, hour, dom, month, dow) =
        (&parts[0], &parts[1], &parts[2], &parts[3], &parts[4]);
    let mut described: Vec<String> = Vec::new();

    // An exact minute and hour reads as a clock time; anything else is described field by field.
    match (single(minute), single(hour)) {
        (Some(m), Some(h)) => {
            described.push(i18n::format("Cron_AtTime", &[&format!("{h:02}:{m:02}")]));
        }
        _ => {
            described.push(describe_field(minute, i18n::get("Cron_Minute")));
            // A "*" hour adds nothing beside a minute rule.
            if hour != "*" {
                described.push(describe_field(hour, i18n::get("Cron_Hour")));
            }
        }
    }

    if dom != "*" {
        described.push(describe_field(dom, i18n::get("Cron_DayOfMonth")));
    }
    if month != "*" {
        let names = culture().months;
        described.push(i18n::format(
            "Cron_InMonths",
            &[&named(month, |i| names[(i - 1).clamp(0, 11) as usize])],
        ));
    }
    if dow != "*" {
        let names = culture().days;
        described.push(i18n::format(
            "Cron_OnDays",
            &[&named(dow, |i| names[if i == 7 { 0 } else { i.clamp(0, 6) as usize }])],
        ));
    }

    Ok(described.join(", "))
}

fn describe_field(field: &str, unit: &str) -> String {
    if field == "*" {
        return i18n::format("Cron_EveryUnit", &[unit]);
    }
    if let Some(rest) = field.strip_prefix("*/") {
        if let Some(step) = single(rest) {
            return i18n::format("Cron_EveryN", &[&step.to_string(), unit]);
        }
    }
    if field.contains('-') && !field.contains(',') {
        let mut range = field.splitn(2, '-');
        let low = range.next().unwrap_or("");
        let high = range.next().unwrap_or("");
        return i18n::format("Cron_Range", &[unit, low, high]);
    }
    if field.contains(',') {
        return i18n::format("Cron_List", &[unit, &field.replace(',', ", ")]);
    }
    i18n::format("Cron_At", &[unit, field])
}

/// Renders a month or weekday field with names instead of numbers. A token that is not a number
/// is passed through, so "MON" stays "MON".
fn named(field: &str, name_of: impl Fn(i64) -> &'static str) -> String {
    if let Some(rest) = field.strip_prefix("*/") {
        if let Some(step) = single(rest) {
            return i18n::format("Cron_EveryNBare", &[&step.to_string()]);
        }
    }
    let name = |token: &str| match single(token) {
        Some(i) => name_of(i).to_string(),
        None => token.to_string(),
    };

    if field.contains('-') {
        let mut range = field.splitn(2, '-');
        let low = name(range.next().unwrap_or(""));
        let high = name(range.next().unwrap_or(""));
        // An en dash, as the C# writes it.
        return format!("{low}\u{2013}{high}");
    }
    field.split(',').map(name).collect::<Vec<_>>().join(", ")
}

/// Expands one field ("*", "*/5", "1-3", "1,4", "7") into a match set over `[min, max]`.
///
/// The C# uses `int.Parse` here and would throw a raw exception on a malformed field; this is only
/// reachable from `next_runs`, and returning None keeps the raw panic out of the FFI.
fn expand(field: &str, min: usize, max: usize) -> Option<Vec<bool>> {
    let mut set = vec![false; max + 1];
    for part in field.split(',') {
        let (range, step) = match part.split_once('/') {
            Some((r, s)) => (r, single(s)? as usize),
            None => (part, 1),
        };
        if step == 0 {
            return None;
        }
        let (low, high) = if range == "*" {
            (min, max)
        } else if let Some((a, b)) = range.split_once('-') {
            (single(a)? as usize, single(b)? as usize)
        } else {
            let v = single(range)? as usize;
            (v, v)
        };
        let mut v = low;
        while v <= high {
            if v >= min && v <= max {
                set[v] = true;
            }
            v += step;
        }
    }
    Some(set)
}

/// The next `count` times the expression fires, at or after `from`.
///
/// Used by the GUI's cron page rather than by the tool itself.
pub fn next_runs(expr: &str, count: usize, from: Civil) -> Result<Vec<Civil>, String> {
    let parts = resolve(expr)?;
    let bad = || i18n::get("Error_CronUsage").to_string();
    let minute = expand(&parts[0], 0, 59).ok_or_else(bad)?;
    let hour = expand(&parts[1], 0, 23).ok_or_else(bad)?;
    let dom = expand(&parts[2], 1, 31).ok_or_else(bad)?;
    let month = expand(&parts[3], 1, 12).ok_or_else(bad)?;
    let dow_raw = expand(&parts[4], 0, 7).ok_or_else(bad)?;

    // 0 and 7 both mean Sunday.
    let mut dow = [false; 7];
    for (i, matched) in dow_raw.iter().enumerate() {
        if *matched {
            dow[i % 7] = true;
        }
    }
    let dom_restricted = parts[2] != "*";
    let dow_restricted = parts[4] != "*";

    let mut results = Vec::new();
    let mut t = Civil { second: 0, ..from }.add_seconds(60);
    // Guard against an expression that can never fire, such as 30 February.
    let limit = t.add_years(5);
    while results.len() < count && t < limit {
        if minute[t.minute as usize] && hour[t.hour as usize] && month[t.month as usize] {
            let dom_match = dom[t.day as usize];
            let dow_match = dow[t.day_of_week() as usize];
            // Standard cron: OR when both day fields are restricted, AND otherwise.
            let matched = if dom_restricted && dow_restricted {
                dom_match || dow_match
            } else {
                dom_match && dow_match
            };
            if matched {
                results.push(t);
            }
        }
        t = t.add_seconds(60);
    }
    Ok(results)
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
    fn an_exact_time_reads_as_a_clock() {
        let _guard = english();
        assert_eq!(describe("30 4 * * *").unwrap(), "at 04:30");
        assert_eq!(describe("0 0 * * *").unwrap(), "at 00:00");
    }

    #[test]
    fn shortcuts_expand_before_anything_else() {
        let _guard = english();
        assert_eq!(describe("@daily").unwrap(), describe("0 0 * * *").unwrap());
        assert_eq!(describe("@midnight").unwrap(), describe("0 0 * * *").unwrap());
        assert_eq!(describe("@hourly").unwrap(), describe("0 * * * *").unwrap());
        assert_eq!(describe("@yearly").unwrap(), describe("0 0 1 1 *").unwrap());
        assert_eq!(describe("@annually").unwrap(), describe("@yearly").unwrap());
        assert_eq!(describe("@monthly").unwrap(), describe("0 0 1 * *").unwrap());
        assert_eq!(describe("@weekly").unwrap(), describe("0 0 * * 0").unwrap());
        assert!(describe("@nonsense").is_err());
    }

    #[test]
    fn wrong_field_counts_are_refused() {
        let _guard = english();
        for bad in ["", "* * *", "* * * * * *", "@", "one two three four five six"] {
            assert!(describe(bad).is_err(), "{bad:?}");
        }
    }

    #[test]
    fn steps_ranges_and_lists_are_each_described() {
        let _guard = english();
        let every15 = describe("*/15 * * * *").unwrap();
        assert!(every15.contains("15"), "{every15}");
        let range = describe("0 9-17 * * *").unwrap();
        assert!(range.contains('9') && range.contains("17"), "{range}");
        let list = describe("0,30 * * * *").unwrap();
        assert!(list.contains("0, 30"), "the list is respaced: {list}");
    }

    /// A `*` hour beside a minute rule would read as noise, so it is left out.
    #[test]
    fn a_star_hour_is_not_mentioned_next_to_a_minute_rule() {
        let _guard = english();
        let out = describe("*/15 * * * *").unwrap();
        assert_eq!(out.split(", ").count(), 1, "{out}");
    }

    #[test]
    fn months_and_days_are_named_not_numbered() {
        let _guard = english();
        let out = describe("0 0 * 1 *").unwrap();
        assert!(out.contains("January"), "{out}");
        let days = describe("0 0 * * 1").unwrap();
        assert!(days.contains("Monday"), "{days}");
        // 0 and 7 are both Sunday.
        assert!(describe("0 0 * * 0").unwrap().contains("Sunday"));
        assert!(describe("0 0 * * 7").unwrap().contains("Sunday"));
        // A range uses an en dash.
        let range = describe("0 0 * * 1-5").unwrap();
        assert!(range.contains("Monday\u{2013}Friday"), "{range}");
        // A non-numeric token passes through untouched.
        assert!(describe("0 0 * * MON").unwrap().contains("MON"));
    }

    /// The names must come from the culture, not a hardcoded English list.
    #[test]
    fn month_names_follow_the_active_language() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("fr");
        let out = describe("0 0 * 1 *").unwrap();
        assert!(out.contains("janvier"), "{out}");
        i18n::set_language("de");
        assert!(describe("0 0 * 1 *").unwrap().contains("Januar"));
        i18n::set_language("en");
    }

    #[test]
    fn next_runs_lands_on_the_matching_minutes() {
        let _guard = english();
        let from = Civil { year: 2026, month: 7, day: 30, hour: 10, minute: 30, second: 45 };
        let runs = next_runs("0 * * * *", 3, from).unwrap();
        assert_eq!(runs.len(), 3);
        assert_eq!(runs[0], Civil { year: 2026, month: 7, day: 30, hour: 11, minute: 0, second: 0 });
        assert_eq!(runs[1].hour, 12);
        assert_eq!(runs[2].hour, 13);
    }

    #[test]
    fn next_runs_crosses_a_day_and_a_month() {
        let _guard = english();
        let late = Civil { year: 2026, month: 7, day: 31, hour: 23, minute: 59, second: 0 };
        let runs = next_runs("0 0 * * *", 1, late).unwrap();
        assert_eq!(runs[0], Civil::date(2026, 8, 1));
    }

    /// Both day fields restricted means OR, which is the rule everyone gets wrong.
    #[test]
    fn both_day_fields_restricted_is_or() {
        let _guard = english();
        // The 1st of the month, or any Monday.
        let from = Civil::date(2026, 7, 1);
        let runs = next_runs("0 0 1 * 1", 5, from).unwrap();
        for run in &runs {
            assert!(run.day == 1 || run.day_of_week() == 1, "{run:?}");
        }
        // With only one restricted it is AND: Mondays that are also the 1st.
        let only_dom = next_runs("0 0 1 * *", 3, from).unwrap();
        assert!(only_dom.iter().all(|r| r.day == 1));
    }

    #[test]
    fn an_impossible_expression_returns_nothing_rather_than_looping() {
        let _guard = english();
        // 30 February never happens.
        let runs = next_runs("0 0 30 2 *", 1, Civil::date(2026, 1, 1)).unwrap();
        assert!(runs.is_empty());
        // A malformed field is an error, not a panic.
        assert!(next_runs("0 0 x * *", 1, Civil::date(2026, 1, 1)).is_err());
        assert!(next_runs("*/0 * * * *", 1, Civil::date(2026, 1, 1)).is_err());
    }
}
