//! Numbers written out in words. Mirrors `Krate.Core.Words`.
//!
//! Deliberately hand-written per language, exactly as the C# side is — this is the tool I
//! expected to need icu4x for, and it turns out not to. Only English and French are supported;
//! anything else is rejected rather than silently falling back.

use crate::i18n;

const EN_UNITS: [&str; 20] = [
    "zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten",
    "eleven", "twelve", "thirteen", "fourteen", "fifteen", "sixteen", "seventeen", "eighteen",
    "nineteen",
];
const EN_TENS: [&str; 10] = [
    "", "", "twenty", "thirty", "forty", "fifty", "sixty", "seventy", "eighty", "ninety",
];
const EN_SCALES: [&str; 7] = [
    "", " thousand", " million", " billion", " trillion", " quadrillion", " quintillion",
];

const FR_UNITS: [&str; 20] = [
    "zéro", "un", "deux", "trois", "quatre", "cinq", "six", "sept", "huit", "neuf", "dix",
    "onze", "douze", "treize", "quatorze", "quinze", "seize", "dix-sept", "dix-huit", "dix-neuf",
];
const FR_TENS: [&str; 10] = [
    "", "", "vingt", "trente", "quarante", "cinquante", "soixante", "soixante", "quatre-vingt",
    "quatre-vingt",
];
const FR_SCALES: [&str; 7] = [
    "", "mille", "million", "milliard", "billion", "billiard", "trillion",
];

/// Splits a number into groups of three digits, least significant first.
fn groups(mut value: i64) -> Vec<i32> {
    let mut out = Vec::new();
    while value > 0 {
        out.push((value % 1000) as i32);
        value /= 1000;
    }
    out
}

fn en_hundreds(mut n: i32) -> String {
    let mut s = String::new();
    if n >= 100 {
        s.push_str(EN_UNITS[(n / 100) as usize]);
        s.push_str(" hundred");
        n %= 100;
        if n > 0 {
            s.push(' ');
        }
    }
    if n >= 20 {
        s.push_str(EN_TENS[(n / 10) as usize]);
        if n % 10 > 0 {
            s.push('-');
            s.push_str(EN_UNITS[(n % 10) as usize]);
        }
    } else if n > 0 {
        s.push_str(EN_UNITS[n as usize]);
    }
    s
}

pub fn english(value: i64) -> String {
    if value == 0 {
        return EN_UNITS[0].to_string();
    }
    if value < 0 {
        return format!("minus {}", english(-value));
    }
    let groups = groups(value);
    let mut s = String::new();
    for i in (0..groups.len()).rev() {
        if groups[i] == 0 {
            continue;
        }
        if !s.is_empty() {
            s.push(' ');
        }
        s.push_str(&en_hundreds(groups[i]));
        s.push_str(EN_SCALES[i]);
    }
    s
}

fn fr_hundreds(mut n: i32) -> String {
    let mut s = String::new();
    if n >= 100 {
        let hundreds = n / 100;
        if hundreds > 1 {
            s.push_str(FR_UNITS[hundreds as usize]);
            s.push_str(" cent");
        } else {
            s.push_str("cent");
        }
        n %= 100;
        // "deux cents" but "deux cent un": the s only survives when nothing follows.
        if hundreds > 1 && n == 0 {
            s.push('s');
        }
        if n > 0 {
            s.push(' ');
        }
    }
    if n >= 20 {
        let (tens, mut units) = (n / 10, n % 10);
        s.push_str(FR_TENS[tens as usize]);
        // 70 and 90 are built as 60+10 and 80+10, so the unit part runs from 10 to 19.
        if tens == 7 || tens == 9 {
            units += 10;
        }
        if tens == 8 && units == 0 {
            s.push('s'); // quatre-vingts
        }
        // "et" only in 21…61 and 71 — never in 81 or 91.
        let et = ((2..=6).contains(&tens) && units == 1) || (tens == 7 && units == 11);
        if units > 0 {
            s.push_str(if et { " et " } else { "-" });
            s.push_str(FR_UNITS[units as usize]);
        }
    } else if n > 0 {
        s.push_str(FR_UNITS[n as usize]);
    }
    s
}

pub fn french(value: i64) -> String {
    if value == 0 {
        return FR_UNITS[0].to_string();
    }
    if value < 0 {
        return format!("moins {}", french(-value));
    }
    let groups = groups(value);
    let mut parts: Vec<String> = Vec::new();
    for i in (0..groups.len()).rev() {
        if groups[i] == 0 {
            continue;
        }
        let text = fr_hundreds(groups[i]);
        if i == 1 {
            // "mille", never "un mille"
            parts.push(if groups[i] == 1 { "mille".to_string() } else { format!("{text} mille") });
        } else if i > 1 {
            // millions are nouns: they take an s
            let plural = if groups[i] > 1 { "s" } else { "" };
            parts.push(format!("{text} {}{plural}", FR_SCALES[i]));
        } else {
            parts.push(text);
        }
    }
    parts.join(" ")
}

/// "1234" or "1234 fr". Falls back to the interface language.
pub fn spell(input: &str) -> Result<String, String> {
    let parts: Vec<&str> = input.trim().split([' ', ',']).filter(|p| !p.is_empty()).collect();
    if parts.is_empty() {
        return Err(i18n::get("Error_NeedNumber").to_string());
    }

    let language = if parts.len() > 1 {
        parts[parts.len() - 1].to_lowercase()
    } else {
        // Two-letter ISO name, matching Strings.Culture.TwoLetterISOLanguageName.
        i18n::language().split('-').next().unwrap_or("en").to_string()
    };

    let digits: String = parts
        .iter()
        .filter(|p| p.chars().any(|c| c.is_ascii_digit()))
        .copied()
        .collect();
    let value: i64 = digits
        .parse()
        .map_err(|_| i18n::get("Error_NeedNumber").to_string())?;

    match language.as_str() {
        "fr" => Ok(french(value)),
        "en" => Ok(english(value)),
        other => Err(i18n::format("Error_UnsupportedLanguage", &[other])),
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn english_covers_the_awkward_ranges() {
        assert_eq!(english(0), "zero");
        assert_eq!(english(7), "seven");
        assert_eq!(english(13), "thirteen");
        assert_eq!(english(21), "twenty-one");
        assert_eq!(english(100), "one hundred");
        assert_eq!(english(101), "one hundred one");
        assert_eq!(english(1000), "one thousand");
        assert_eq!(english(1234), "one thousand two hundred thirty-four");
        assert_eq!(english(-5), "minus five");
    }

    /// French numbers are where a naive implementation falls over: 70 and 90 are built from
    /// 60 and 80, "et" appears only in some of the twenties-to-seventies, and the plural s on
    /// "cent" and "quatre-vingt" survives only when nothing follows.
    #[test]
    fn french_handles_its_notorious_cases() {
        assert_eq!(french(0), "zéro");
        assert_eq!(french(21), "vingt et un");
        assert_eq!(french(31), "trente et un");
        assert_eq!(french(70), "soixante-dix");
        assert_eq!(french(71), "soixante et onze");
        assert_eq!(french(80), "quatre-vingts");
        assert_eq!(french(81), "quatre-vingt-un", "no 'et' at 81");
        assert_eq!(french(91), "quatre-vingt-onze", "no 'et' at 91");
        assert_eq!(french(99), "quatre-vingt-dix-neuf");
        assert_eq!(french(200), "deux cents");
        assert_eq!(french(201), "deux cent un", "the s drops when something follows");
        assert_eq!(french(1000), "mille", "never 'un mille'");
        assert_eq!(french(2000), "deux mille");
        assert_eq!(french(2_000_000), "deux millions", "scales take an s");
        assert_eq!(french(-5), "moins cinq");
    }

    #[test]
    fn spell_picks_the_language_from_the_input() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        assert_eq!(spell("1234").unwrap(), "one thousand two hundred thirty-four");
        assert_eq!(spell("21 fr").unwrap(), "vingt et un");
        i18n::set_language("fr");
        assert_eq!(spell("21").unwrap(), "vingt et un", "falls back to the interface language");
        i18n::set_language("de");
        assert!(spell("21").is_err(), "unsupported languages are rejected, not guessed");
        i18n::set_language("en");
        assert!(spell("").is_err());
    }
}
