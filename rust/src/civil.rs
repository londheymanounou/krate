//! Proleptic Gregorian calendar arithmetic, shared by everything date-shaped.
//!
//! Hand-rolled rather than a date crate: the port needs exactly these operations, they are exact
//! over the whole range .NET's `DateTime` covers, and a crate would bring a competing idea of what
//! a local time is. Howard Hinnant's days-from-civil / civil-from-days algorithms.

/// Days since 1970-01-01 for a civil date. Exact for any year the caller can represent.
pub fn days_from_civil(year: i64, month: u32, day: u32) -> i64 {
    let y = if month <= 2 { year - 1 } else { year };
    let era = if y >= 0 { y } else { y - 399 } / 400;
    let yoe = (y - era * 400) as u64; // [0, 399]
    let mp = if month > 2 { month - 3 } else { month + 9 } as u64; // March-based
    let doy = (153 * mp + 2) / 5 + day as u64 - 1; // [0, 365]
    let doe = yoe * 365 + yoe / 4 - yoe / 100 + doy; // [0, 146096]
    era * 146_097 + doe as i64 - 719_468
}

/// The inverse of [`days_from_civil`].
pub fn civil_from_days(days: i64) -> (i64, u32, u32) {
    let z = days + 719_468;
    let era = if z >= 0 { z } else { z - 146_096 } / 146_097;
    let doe = (z - era * 146_097) as u64; // [0, 146096]
    let yoe = (doe - doe / 1460 + doe / 36524 - doe / 146_096) / 365; // [0, 399]
    let y = yoe as i64 + era * 400;
    let doy = doe - (365 * yoe + yoe / 4 - yoe / 100); // [0, 365]
    let mp = (5 * doy + 2) / 153; // [0, 11]
    let d = (doy - (153 * mp + 2) / 5 + 1) as u32; // [1, 31]
    let m = if mp < 10 { mp + 3 } else { mp - 9 } as u32; // [1, 12]
    (if m <= 2 { y + 1 } else { y }, m, d)
}

pub fn is_leap_year(year: i64) -> bool {
    year % 4 == 0 && (year % 100 != 0 || year % 400 == 0)
}

pub fn days_in_month(year: i64, month: u32) -> u32 {
    match month {
        1 | 3 | 5 | 7 | 8 | 10 | 12 => 31,
        4 | 6 | 9 | 11 => 30,
        2 if is_leap_year(year) => 29,
        2 => 28,
        _ => 0,
    }
}

/// A civil date and time with no zone attached, like .NET's `DateTime` with `Unspecified` kind.
#[derive(Clone, Copy, PartialEq, Eq, PartialOrd, Ord, Debug)]
pub struct Civil {
    pub year: i64,
    pub month: u32,
    pub day: u32,
    pub hour: u32,
    pub minute: u32,
    pub second: u32,
}

impl Civil {
    pub fn date(year: i64, month: u32, day: u32) -> Self {
        Self { year, month, day, hour: 0, minute: 0, second: 0 }
    }

    /// From a Unix timestamp in seconds.
    pub fn from_unix_seconds(seconds: i64) -> Self {
        let days = seconds.div_euclid(86_400);
        let rest = seconds.rem_euclid(86_400);
        let (year, month, day) = civil_from_days(days);
        Self {
            year,
            month,
            day,
            hour: (rest / 3600) as u32,
            minute: ((rest % 3600) / 60) as u32,
            second: (rest % 60) as u32,
        }
    }

    pub fn to_unix_seconds(self) -> i64 {
        days_from_civil(self.year, self.month, self.day) * 86_400
            + self.hour as i64 * 3600
            + self.minute as i64 * 60
            + self.second as i64
    }

    /// 0 = Sunday, matching .NET's `DayOfWeek`. 1970-01-01 was a Thursday.
    pub fn day_of_week(self) -> u32 {
        (days_from_civil(self.year, self.month, self.day) + 4).rem_euclid(7) as u32
    }

    pub fn day_of_year(self) -> u32 {
        (days_from_civil(self.year, self.month, self.day)
            - days_from_civil(self.year, 1, 1)) as u32
            + 1
    }

    pub fn add_seconds(self, seconds: i64) -> Self {
        Self::from_unix_seconds(self.to_unix_seconds() + seconds)
    }

    pub fn add_days(self, days: i64) -> Self {
        self.add_seconds(days * 86_400)
    }

    /// Clamps the day, as `DateTime.AddMonths` does: 31 January plus one month is 28 February.
    pub fn add_months(self, months: i64) -> Self {
        let total = self.year * 12 + (self.month as i64 - 1) + months;
        let year = total.div_euclid(12);
        let month = total.rem_euclid(12) as u32 + 1;
        let day = self.day.min(days_in_month(year, month));
        Self { year, month, day, ..self }
    }

    pub fn add_years(self, years: i64) -> Self {
        self.add_months(years * 12)
    }

    /// Midnight on the same date.
    pub fn date_only(self) -> Self {
        Self { hour: 0, minute: 0, second: 0, ..self }
    }

    /// Whole days from self to `other`, as `(b - a).Days` gives.
    pub fn days_until(self, other: Self) -> i64 {
        (other.to_unix_seconds() - self.to_unix_seconds()) / 86_400
    }
}

/// ISO 8601 week number and the year that week belongs to, matching `System.Globalization.ISOWeek`.
///
/// A week belongs to the year containing its Thursday, so 31 December can be week 1 of the next
/// year and 1 January can be week 52 or 53 of the previous one.
pub fn iso_week(date: Civil) -> (i64, u32) {
    let days = days_from_civil(date.year, date.month, date.day);
    // ISO weekday: Monday = 1 .. Sunday = 7.
    let iso_dow = (days + 3).rem_euclid(7) + 1;
    // The Thursday of this week decides the year.
    let thursday = days - iso_dow + 4;
    let (year, _, _) = civil_from_days(thursday);
    let january_first = days_from_civil(year, 1, 1);
    let week = ((thursday - january_first) / 7 + 1) as u32;
    (year, week)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn the_two_conversions_are_inverses() {
        for days in [-800_000i64, -1, 0, 1, 19_000, 100_000, 800_000] {
            let (y, m, d) = civil_from_days(days);
            assert_eq!(days_from_civil(y, m, d), days, "{days}");
        }
        // Every day across a leap year and a century boundary.
        for days in days_from_civil(1899, 12, 1)..days_from_civil(1901, 2, 1) {
            let (y, m, d) = civil_from_days(days);
            assert_eq!(days_from_civil(y, m, d), days);
        }
    }

    #[test]
    fn known_dates_land_where_they_should() {
        assert_eq!(civil_from_days(0), (1970, 1, 1));
        assert_eq!(days_from_civil(1970, 1, 1), 0);
        assert_eq!(days_from_civil(2000, 3, 1) - days_from_civil(2000, 2, 28), 2, "2000 was a leap year");
        assert_eq!(days_from_civil(1900, 3, 1) - days_from_civil(1900, 2, 28), 1, "1900 was not");
    }

    #[test]
    fn day_of_week_matches_dotnet_numbering() {
        // 1970-01-01 was a Thursday, which .NET numbers 4.
        assert_eq!(Civil::date(1970, 1, 1).day_of_week(), 4);
        assert_eq!(Civil::date(2026, 7, 30).day_of_week(), 4);
        assert_eq!(Civil::date(2026, 8, 2).day_of_week(), 0, "Sunday is 0");
        assert_eq!(Civil::date(2026, 8, 1).day_of_week(), 6, "Saturday is 6");
    }

    /// The trap AddMonths exists to avoid: 31 January plus a month is not 3 March.
    #[test]
    fn add_months_clamps_the_day() {
        assert_eq!(Civil::date(2024, 1, 31).add_months(1), Civil::date(2024, 2, 29));
        assert_eq!(Civil::date(2023, 1, 31).add_months(1), Civil::date(2023, 2, 28));
        assert_eq!(Civil::date(2024, 3, 31).add_months(-1), Civil::date(2024, 2, 29));
        assert_eq!(Civil::date(2024, 2, 29).add_years(1), Civil::date(2025, 2, 28));
        assert_eq!(Civil::date(2024, 12, 15).add_months(1), Civil::date(2025, 1, 15));
        assert_eq!(Civil::date(2024, 1, 15).add_months(-1), Civil::date(2023, 12, 15));
    }

    #[test]
    fn day_of_year_counts_from_one() {
        assert_eq!(Civil::date(2024, 1, 1).day_of_year(), 1);
        assert_eq!(Civil::date(2024, 12, 31).day_of_year(), 366, "2024 is a leap year");
        assert_eq!(Civil::date(2023, 12, 31).day_of_year(), 365);
    }

    /// The awkward year boundaries are the whole point of ISO weeks.
    #[test]
    fn iso_weeks_match_the_standard() {
        // 2021-01-01 was a Friday, so it belongs to week 53 of 2020.
        assert_eq!(iso_week(Civil::date(2021, 1, 1)), (2020, 53));
        // 2019-12-30 was a Monday, week 1 of 2020.
        assert_eq!(iso_week(Civil::date(2019, 12, 30)), (2020, 1));
        assert_eq!(iso_week(Civil::date(2026, 1, 1)), (2026, 1));
        assert_eq!(iso_week(Civil::date(2024, 12, 31)), (2025, 1));
        assert_eq!(iso_week(Civil::date(2024, 6, 15)), (2024, 24));
    }

    #[test]
    fn seconds_round_trip_through_the_epoch() {
        for seconds in [-86_401i64, -1, 0, 1, 1_600_000_000, 1_767_225_600] {
            assert_eq!(Civil::from_unix_seconds(seconds).to_unix_seconds(), seconds, "{seconds}");
        }
        assert_eq!(
            Civil::from_unix_seconds(1_600_000_000),
            Civil { year: 2020, month: 9, day: 13, hour: 12, minute: 26, second: 40 }
        );
    }
}
