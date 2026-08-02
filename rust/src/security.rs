//! Password strength estimation. Mirrors `Krate.Core.Security`.

use crate::i18n;
use crate::physics::thousands;

/// True if the password contains three sequential or repeated characters (abc, 123, aaa).
pub fn has_run(s: &str) -> bool {
    let chars: Vec<u32> = s.chars().map(|c| c as u32).collect();
    chars.windows(3).any(|w| {
        let (a, b, c) = (w[0], w[1], w[2]);
        (b.wrapping_sub(a) == 1 && c.wrapping_sub(b) == 1) || (a == b && b == c)
    })
}

fn distinct_count(s: &str) -> usize {
    let mut seen: Vec<char> = Vec::new();
    for c in s.chars() {
        if !seen.contains(&c) {
            seen.push(c);
        }
    }
    seen.len()
}

/// Estimated entropy in bits, with a penalty for repeats and sequences.
///
/// Length here is `chars().count()`, matching the C# `string.Length` for the ASCII-ish input a
/// password realistically is; the pool sizes are the C# character classes.
pub fn entropy(password: &str) -> f64 {
    if password.is_empty() {
        return 0.0;
    }
    // Charset size the attacker must assume, from the classes actually used.
    let mut pool = 0;
    if password.chars().any(|c| c.is_ascii_lowercase()) {
        pool += 26;
    }
    if password.chars().any(|c| c.is_ascii_uppercase()) {
        pool += 26;
    }
    if password.chars().any(|c| c.is_ascii_digit()) {
        pool += 10;
    }
    if password.chars().any(|c| !c.is_ascii_alphanumeric()) {
        pool += 33;
    }

    let length = password.chars().count();
    let bits = length as f64 * (pool.max(1) as f64).log2();

    // A repeated or sequential password has far less real entropy than its length suggests.
    let mut penalty = 0.0;
    if has_run(password) {
        penalty += 0.25;
    }
    if distinct_count(password) <= length / 2 {
        penalty += 0.25;
    }
    bits * (1.0 - penalty)
}

/// Band key for an entropy value — drives the meter colour and the rating label.
pub fn band(bits: f64) -> &'static str {
    if bits < 28.0 {
        "Pw_VeryWeak"
    } else if bits < 36.0 {
        "Pw_Weak"
    } else if bits < 60.0 {
        "Pw_Reasonable"
    } else if bits < 128.0 {
        "Pw_Strong"
    } else {
        "Pw_VeryStrong"
    }
}

fn human_time(seconds: f64) -> String {
    if seconds < 1.0 {
        return i18n::get("Time_Instant").to_string();
    }
    const UNITS: [(f64, &str); 6] = [
        (60.0, "Time_Seconds"),
        (3600.0, "Time_Minutes"),
        (86400.0, "Time_Hours"),
        (2_592_000.0, "Time_Days"),
        (31_536_000.0, "Time_Months"),
        (3_153_600_000.0, "Time_Years"),
    ];
    let mut divisor = 1.0;
    for (limit, key) in UNITS {
        if seconds < limit {
            // Invariant grouping. The C# used a plain interpolated "N0", which grouped with the
            // OS locale rather than the app language.
            return i18n::format(key, &[&thousands((seconds / divisor).round() as i64)]);
        }
        divisor = limit;
    }
    i18n::get("Time_Centuries").to_string()
}

pub fn strength(password: &str) -> Result<String, String> {
    if password.is_empty() {
        return Err(i18n::get("Error_NeedText").to_string());
    }
    let bits = entropy(password);
    let penalised = has_run(password) || distinct_count(password) <= password.chars().count() / 2;

    // Guesses at 10^10/s (a modern offline attack on a fast hash) — order of magnitude, not a
    // promise.
    let seconds = 2f64.powf(bits) / 2.0 / 1e10;

    let mut lines = vec![
        i18n::format("Pw_Entropy", &[&format!("{:.0}", bits)]),
        i18n::format("Pw_Rating", &[i18n::get(band(bits))]),
        i18n::format("Pw_Crack", &[&human_time(seconds)]),
    ];
    if penalised {
        lines.push(i18n::get("Pw_Note").to_string());
    }
    Ok(lines.join("\n"))
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn runs_are_detected_in_both_directions() {
        assert!(has_run("abc"), "sequential");
        assert!(has_run("123"), "sequential digits");
        assert!(has_run("aaa"), "repeated");
        assert!(has_run("xxabcyy"), "run in the middle");
        assert!(!has_run("acb"), "not sequential");
        assert!(!has_run("ab"), "too short to contain a run");
        assert!(!has_run(""));
    }

    #[test]
    fn entropy_grows_with_the_character_pool() {
        assert_eq!(entropy(""), 0.0);
        // Same length, larger pool, more bits.
        assert!(entropy("qwty") < entropy("Qwty"));
        assert!(entropy("Qwty") < entropy("Qwt1"));
        assert!(entropy("Qwt1") < entropy("Qwt!"));
    }

    #[test]
    fn entropy_is_penalised_for_runs_and_repetition() {
        // "abcdefgh" is sequential, so it scores below a scrambled equivalent.
        assert!(entropy("abcdefgh") < entropy("hxdbfage"));
        // Half or fewer distinct characters is penalised too.
        assert!(entropy("aabbaabb") < entropy("mxqrtyvz"));
    }

    #[test]
    fn bands_cover_the_whole_range() {
        assert_eq!(band(0.0), "Pw_VeryWeak");
        assert_eq!(band(27.9), "Pw_VeryWeak");
        assert_eq!(band(28.0), "Pw_Weak");
        assert_eq!(band(36.0), "Pw_Reasonable");
        assert_eq!(band(60.0), "Pw_Strong");
        assert_eq!(band(128.0), "Pw_VeryStrong");
    }

    #[test]
    fn strength_reports_a_note_only_when_penalised() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        let weak = strength("aaaaaaaa").unwrap();
        assert_eq!(weak.lines().count(), 4, "the note line is present: {weak}");
        let strong = strength("Tr0ub4dor&3xK").unwrap();
        assert_eq!(strong.lines().count(), 3, "no note: {strong}");
        assert!(strength("").is_err());
    }
}
