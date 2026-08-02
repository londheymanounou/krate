//! The local wall clock, from Windows.
//!
//! `DateTime.Today` and `DateTime.Now` are local, not UTC, so a tool that defaults to "today" has
//! to ask the OS what today is there. `std::time` only offers UTC, and a date crate would either
//! bundle its own timezone database or shell out to the same API — so this calls it directly.

use crate::civil::Civil;

#[cfg(windows)]
#[repr(C)]
#[derive(Default)]
struct SystemTime {
    year: u16,
    month: u16,
    day_of_week: u16,
    day: u16,
    hour: u16,
    minute: u16,
    second: u16,
    milliseconds: u16,
}

#[cfg(windows)]
#[link(name = "kernel32")]
unsafe extern "system" {
    /// Current local time, with the timezone and DST rules in force right now.
    fn GetLocalTime(system_time: *mut SystemTime);

    /// Converts a UTC time to local using the rules in force **at that instant**, which is what
    /// .NET does — not today's DST offset applied to a historical date.
    fn SystemTimeToTzSpecificLocalTime(
        time_zone: *const core::ffi::c_void,
        universal_time: *const SystemTime,
        local_time: *mut SystemTime,
    ) -> i32;

    /// The other direction, for input with no offset: `DateTimeOffset.TryParse` treats a bare date
    /// as local, so turning it into a Unix timestamp means going local to UTC first.
    fn TzSpecificLocalTimeToSystemTime(
        time_zone: *const core::ffi::c_void,
        local_time: *const SystemTime,
        universal_time: *mut SystemTime,
    ) -> i32;
}

#[cfg(windows)]
impl From<Civil> for SystemTime {
    fn from(c: Civil) -> Self {
        SystemTime {
            year: c.year as u16,
            month: c.month as u16,
            day_of_week: 0, // ignored on input
            day: c.day as u16,
            hour: c.hour as u16,
            minute: c.minute as u16,
            second: c.second as u16,
            milliseconds: 0,
        }
    }
}

#[cfg(windows)]
impl From<&SystemTime> for Civil {
    fn from(t: &SystemTime) -> Self {
        Civil {
            year: t.year as i64,
            month: t.month as u32,
            day: t.day as u32,
            hour: t.hour as u32,
            minute: t.minute as u32,
            second: t.second as u32,
        }
    }
}

/// Local date and time now.
#[cfg(windows)]
pub fn now() -> Civil {
    let mut local = SystemTime::default();
    // SAFETY: the OS writes a fully initialised SYSTEMTIME into a struct we own.
    unsafe { GetLocalTime(&mut local) };
    Civil::from(&local)
}

/// Local date and time now, via libc's `localtime_r`.
#[cfg(not(windows))]
pub fn now() -> Civil {
    to_local(utc_now())
}

/// Local midnight today, as `DateTime.Today` gives.
pub fn today() -> Civil {
    now().date_only()
}

/// A UTC instant in local time, using the offset that applied at that instant.
#[cfg(windows)]
pub fn to_local(utc: Civil) -> Civil {
    let universal = SystemTime::from(utc);
    let mut local = SystemTime::default();
    // SAFETY: a null timezone means the machine's current zone; both structs are ours.
    let ok = unsafe {
        SystemTimeToTzSpecificLocalTime(std::ptr::null(), &universal, &mut local)
    };
    if ok == 0 {
        // Outside the range Windows will convert (year 1601 and earlier, mainly). Leaving the
        // instant as UTC is wrong by an offset, but inventing one would be worse.
        return utc;
    }
    Civil::from(&local)
}

/// A local wall-clock time as UTC. `DateTimeOffset.TryParse` on input with no offset gives it the
/// local one, so this is what turns a typed-in date into a Unix timestamp.
#[cfg(windows)]
pub fn to_utc(local: Civil) -> Civil {
    let wall = SystemTime::from(local);
    let mut universal = SystemTime::default();
    // SAFETY: a null timezone means the machine's current zone; both structs are ours.
    let ok = unsafe { TzSpecificLocalTimeToSystemTime(std::ptr::null(), &wall, &mut universal) };
    if ok == 0 {
        return local;
    }
    Civil::from(&universal)
}

/// The POSIX side of the same three conversions.
///
/// `localtime_r` resolves the zone from `TZ`/the system database and reports the offset that
/// applied **at that instant**, which is the property the Windows side gets from
/// `SystemTimeToTzSpecificLocalTime` — historical dates keep their own DST state rather than
/// today's. `timegm` is the inverse that treats the fields as UTC, so local -> UTC is done by
/// asking what offset that local time carries and subtracting it.
#[cfg(not(windows))]
mod posix {
    use super::Civil;

    fn to_tm(seconds: i64) -> libc::tm {
        let mut out: libc::tm = unsafe { std::mem::zeroed() };
        let t = seconds as libc::time_t;
        // SAFETY: `out` is ours and fully written by localtime_r, which is the reentrant form.
        unsafe { libc::localtime_r(&t, &mut out) };
        out
    }

    fn from_tm(tm: &libc::tm) -> Civil {
        Civil {
            year: tm.tm_year as i64 + 1900,
            month: tm.tm_mon as u32 + 1,
            day: tm.tm_mday as u32,
            hour: tm.tm_hour as u32,
            minute: tm.tm_min as u32,
            second: tm.tm_sec as u32,
        }
    }

    pub fn to_local(utc: Civil) -> Civil {
        from_tm(&to_tm(utc.to_unix_seconds()))
    }

    pub fn to_utc(local: Civil) -> Civil {
        // The offset in force at that local time, found by asking what UTC instant reports it.
        let naive = local.to_unix_seconds();
        let tm = to_tm(naive);
        let offset = from_tm(&tm).to_unix_seconds() - naive;
        Civil::from_unix_seconds(naive - offset)
    }
}

#[cfg(not(windows))]
pub fn to_local(utc: Civil) -> Civil {
    posix::to_local(utc)
}

#[cfg(not(windows))]
pub fn to_utc(local: Civil) -> Civil {
    posix::to_utc(local)
}

/// Current UTC time, from the system clock rather than the OS's local view.
pub fn utc_now() -> Civil {
    let seconds = std::time::SystemTime::now()
        .duration_since(std::time::UNIX_EPOCH)
        .map(|d| d.as_secs() as i64)
        .unwrap_or(0);
    Civil::from_unix_seconds(seconds)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn the_clock_returns_a_plausible_date() {
        let n = now();
        // Not a real assertion about the time, just that the OS filled the struct in.
        assert!((2020..=2100).contains(&n.year), "{n:?}");
        assert!((1..=12).contains(&n.month), "{n:?}");
        assert!((1..=31).contains(&n.day), "{n:?}");
        assert!(n.hour < 24 && n.minute < 60 && n.second < 60, "{n:?}");
    }

    #[test]
    fn today_is_midnight_on_the_same_date() {
        let (n, t) = (now(), today());
        assert_eq!((t.hour, t.minute, t.second), (0, 0, 0));
        // Tolerates the race across midnight rather than failing once a year.
        assert!(t.day == n.day || t.day == now().day, "{t:?} vs {n:?}");
    }

    /// The conversion must use the offset in force at that instant, so a January date and a July
    /// date in a DST-observing zone do not get the same shift.
    #[test]
    fn local_conversion_is_reversible_in_magnitude() {
        let winter = Civil { year: 2024, month: 1, day: 15, hour: 12, minute: 0, second: 0 };
        let summer = Civil { year: 2024, month: 7, day: 15, hour: 12, minute: 0, second: 0 };
        for utc in [winter, summer] {
            let local = to_local(utc);
            let shift = local.to_unix_seconds() - utc.to_unix_seconds();
            // Every real zone is within +/- 14 hours of UTC.
            assert!(shift.abs() <= 14 * 3600, "{utc:?} -> {local:?}");
            // And the trip back lands where it started.
            assert_eq!(to_utc(local), utc, "{utc:?} did not round-trip via {local:?}");
        }
    }

    #[test]
    fn utc_now_and_local_now_describe_the_same_instant() {
        let shift = now().to_unix_seconds() - utc_now().to_unix_seconds();
        assert!(shift.abs() <= 14 * 3600 + 2, "shift of {shift}s is not a real offset");
    }
}
