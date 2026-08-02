//! Random generators. Mirrors `Krate.Core.Generators`.
//!
//! The C# uses `System.Security.Cryptography.RandomNumberGenerator` throughout — not `Random` —
//! so this draws from the OS entropy source in `csprng`, not a seeded PRNG. Passwords and dice
//! need real entropy, and matching that matters more than being reproducible.
//!
//! **These tools cannot be parity-tested by equality**: two correct implementations never agree
//! on random output. They are verified by *property* instead — see `RandomParityTests` on the C#
//! side, which checks shape, range, character set and permutation invariants against both.

use crate::csprng;
use crate::i18n;

const LOWER: &str = "abcdefghijkmnopqrstuvwxyz"; // no l
const UPPER: &str = "ABCDEFGHJKLMNPQRSTUVWXYZ"; // no I, O
const DIGITS: &str = "23456789"; // no 0, 1
const SYMBOLS: &str = "!@#$%^&*-_=+?";

const RANKS: [&str; 13] = ["A", "2", "3", "4", "5", "6", "7", "8", "9", "10", "J", "Q", "K"];
const SUITS: [&str; 4] = ["♠", "♥", "♦", "♣"];

fn parse_count(input: &str, max: i64, fallback: i64) -> Result<i64, String> {
    let t = input.trim();
    if t.is_empty() {
        return Ok(fallback);
    }
    let n: i64 = t
        .parse()
        .map_err(|_| i18n::format("Error_OutOfRange", &["1", &max.to_string()]))?;
    if n < 1 || n > max {
        return Err(i18n::format("Error_OutOfRange", &["1", &max.to_string()]));
    }
    Ok(n)
}

fn parse_range(input: &str) -> Result<(i64, i64), String> {
    let parts: Vec<&str> = input
        .split([' ', ',', '-', '\n'])
        .map(str::trim)
        .filter(|p| !p.is_empty())
        .collect();
    let bad = || i18n::get("Error_NeedNumber").to_string();
    match parts.len() {
        0 => Ok((1, 100)),
        1 => Ok((1, parts[0].parse().map_err(|_| bad())?)),
        _ => {
            let (a, b): (i64, i64) = (
                parts[0].parse().map_err(|_| bad())?,
                parts[1].parse().map_err(|_| bad())?,
            );
            Ok(if a <= b { (a, b) } else { (b, a) })
        }
    }
}

/// Version-4 UUIDs, lower-case and hyphenated, matching `Guid.NewGuid().ToString()`.
pub fn uuid(input: &str) -> Result<String, String> {
    let count = parse_count(input, 1000, 1)?;
    Ok((0..count)
        .map(|_| {
            let mut bytes = [0u8; 16];
            csprng::fill(&mut bytes);
            bytes[6] = (bytes[6] & 0x0F) | 0x40; // version 4
            bytes[8] = (bytes[8] & 0x3F) | 0x80; // RFC 4122 variant
            let hex: String = bytes.iter().map(|b| format!("{b:02x}")).collect();
            format!(
                "{}-{}-{}-{}-{}",
                &hex[0..8], &hex[8..12], &hex[12..16], &hex[16..20], &hex[20..32]
            )
        })
        .collect::<Vec<_>>()
        .join("\n"))
}

/// Password from the chosen character classes. At least one class must be on.
pub fn password_from(length: i64, upper: bool, lower: bool, digits: bool, symbols: bool) -> Result<String, String> {
    if !(1..=4096).contains(&length) {
        return Err(i18n::format("Error_OutOfRange", &["1", "4096"]));
    }
    let mut pool = String::new();
    if lower {
        pool.push_str(LOWER);
    }
    if upper {
        pool.push_str(UPPER);
    }
    if digits {
        pool.push_str(DIGITS);
    }
    if symbols {
        pool.push_str(SYMBOLS);
    }
    if pool.is_empty() {
        return Err(i18n::get("Error_NoCharset").to_string());
    }
    let chars: Vec<char> = pool.chars().collect();
    Ok((0..length).map(|_| chars[csprng::below(chars.len())]).collect())
}

/// Ambiguous glyphs are excluded so a password stays transcribable from a screen.
pub fn password(input: &str) -> Result<String, String> {
    let length = parse_count(input, 4096, 20)?;
    password_from(length, true, true, true, true)
}

/// "1 100" gives an integer in that range; empty gives 1..100.
pub fn random_number(input: &str) -> Result<String, String> {
    let (min, max) = parse_range(input)?;
    Ok(csprng::range_inclusive(min, max).to_string())
}

pub fn roll(count: i64, faces: i64) -> Result<Vec<i64>, String> {
    if !(1..=1000).contains(&count) || faces < 2 {
        return Err(i18n::get("Error_BadDice").to_string());
    }
    Ok((0..count).map(|_| csprng::range_inclusive(1, faces)).collect())
}

/// Dice in "2d6" notation; "d20", "6" and "" also work.
pub fn dice(input: &str) -> Result<String, String> {
    let spec = input.trim().to_lowercase();
    let spec = if spec.is_empty() { "1d6".to_string() } else { spec };
    let parts: Vec<&str> = spec.split('d').map(str::trim).collect();
    let bad = || i18n::get("Error_BadDice").to_string();

    let count: i64 = if parts.len() > 1 && !parts[0].is_empty() {
        parts[0].parse().map_err(|_| bad())?
    } else {
        1
    };
    let faces: i64 = parts[parts.len() - 1].parse().map_err(|_| bad())?;

    let rolls = roll(count, faces)?;
    Ok(if count == 1 {
        rolls[0].to_string()
    } else {
        let sum: i64 = rolls.iter().sum();
        format!(
            "{} = {sum}",
            rolls.iter().map(i64::to_string).collect::<Vec<_>>().join(" + ")
        )
    })
}

pub fn coin(_: &str) -> String {
    i18n::get(if csprng::below(2) == 0 { "Random_Heads" } else { "Random_Tails" }).to_string()
}

pub fn random_color(_: &str) -> String {
    crate::colors::describe_rgb((
        csprng::below(256) as i32,
        csprng::below(256) as i32,
        csprng::below(256) as i32,
    ))
}

fn items(input: &str) -> Vec<String> {
    input
        .split([',', '\n'])
        .map(str::trim)
        .filter(|i| !i.is_empty())
        .map(str::to_string)
        .collect()
}

/// Picks one entry at random from a comma- or newline-separated list.
pub fn pick(input: &str) -> Result<String, String> {
    let list = items(input);
    if list.is_empty() {
        return Err(i18n::get("Error_EmptyList").to_string());
    }
    Ok(list[csprng::below(list.len())].clone())
}

/// Shuffles the list — also covers "put this in a random order".
pub fn shuffle(input: &str) -> String {
    let mut list = items(input);
    csprng::shuffle(&mut list);
    list.join("\n")
}

/// Draws N distinct cards from a shuffled 52-card deck.
pub fn cards(input: &str) -> Result<String, String> {
    let count = parse_count(input, 52, 1)? as usize;
    let mut deck: Vec<String> = SUITS
        .iter()
        .flat_map(|s| RANKS.iter().map(move |r| format!("{r}{s}")))
        .collect();
    csprng::shuffle(&mut deck);
    Ok(deck[..count].join(" "))
}

/// "3; alice, bob, carol" splits the names into 3 random balanced teams. A lone number anywhere
/// is the team count (default 2); everything else is a name.
pub fn teams(input: &str) -> Result<String, String> {
    let mut tokens: Vec<String> = input
        .split([',', '\n', ';'])
        .map(str::trim)
        .filter(|t| !t.is_empty())
        .map(str::to_string)
        .collect();

    let mut team_count: usize = 2;
    if let Some(index) = tokens.iter().position(|t| t.parse::<i64>().is_ok()) {
        team_count = tokens[index].parse::<i64>().unwrap_or(2).max(1) as usize;
        tokens.remove(index);
    }
    if tokens.is_empty() {
        return Err(i18n::get("Error_EmptyList").to_string());
    }
    // More teams than people would leave some empty.
    team_count = team_count.clamp(1, tokens.len());

    csprng::shuffle(&mut tokens);
    let mut teams: Vec<Vec<String>> = vec![Vec::new(); team_count];
    for (i, name) in tokens.into_iter().enumerate() {
        teams[i % team_count].push(name);
    }
    Ok(teams
        .iter()
        .enumerate()
        .map(|(i, t)| format!("{}: {}", i18n::format("Teams_Label", &[&(i + 1).to_string()]), t.join(", ")))
        .collect::<Vec<_>>()
        .join("\n"))
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn uuids_are_v4_distinct_and_correctly_shaped() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        let out = uuid("100").unwrap();
        let ids: Vec<&str> = out.lines().collect();
        assert_eq!(ids.len(), 100);
        assert_eq!(ids.iter().collect::<std::collections::HashSet<_>>().len(), 100, "all distinct");
        for id in &ids {
            assert_eq!(id.len(), 36, "{id}");
            assert_eq!(id.as_bytes()[14], b'4', "version nibble: {id}");
            assert!(matches!(id.as_bytes()[19], b'8' | b'9' | b'a' | b'b'), "variant: {id}");
            assert!(id.chars().all(|c| c.is_ascii_hexdigit() || c == '-'), "{id}");
        }
        assert!(uuid("0").is_err());
        assert!(uuid("1001").is_err());
    }

    #[test]
    fn passwords_honour_length_and_exclude_ambiguous_glyphs() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        assert_eq!(password("").unwrap().chars().count(), 20, "default length");
        assert_eq!(password("64").unwrap().chars().count(), 64);
        let sample: String = (0..50).map(|_| password("64").unwrap()).collect();
        for ambiguous in ['l', 'I', 'O', '0', '1'] {
            assert!(!sample.contains(ambiguous), "{ambiguous} must be excluded");
        }
        assert!(password("0").is_err());
        assert!(password("5000").is_err());
    }

    #[test]
    fn password_classes_are_respected() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        let digits = password_from(30, false, false, true, false).unwrap();
        assert!(digits.chars().all(|c| ('2'..='9').contains(&c)), "{digits}");
        let lower = password_from(30, false, true, false, false).unwrap();
        assert!(lower.chars().all(|c| c.is_ascii_lowercase()), "{lower}");
        assert!(password_from(10, false, false, false, false).is_err(), "no classes on");
    }

    #[test]
    fn random_numbers_stay_in_range_and_cover_it() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        let mut seen = std::collections::HashSet::new();
        for _ in 0..300 {
            let n: i64 = random_number("1 3").unwrap().parse().unwrap();
            assert!((1..=3).contains(&n), "{n} out of range");
            seen.insert(n);
        }
        assert_eq!(seen.len(), 3, "the whole range should appear");
        assert_eq!(random_number("5 5").unwrap(), "5");
        // A reversed range is normalised, not rejected.
        assert!((1..=9).contains(&random_number("9 1").unwrap().parse::<i64>().unwrap()));
    }

    #[test]
    fn dice_parse_every_accepted_spelling() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        let single: i64 = dice("d6").unwrap().parse().unwrap();
        assert!((1..=6).contains(&single));
        let multi = dice("3d6").unwrap();
        assert!(multi.contains(" = "), "multiple dice show a total: {multi}");
        assert_eq!(multi.split(" + ").count(), 3);
        assert!((1..=6).contains(&dice("").unwrap().parse::<i64>().unwrap()), "default is 1d6");
        assert!(dice("1d1").is_err(), "a one-sided die is not a die");
        assert!(dice("2000d6").is_err());
        assert!(dice("zzz").is_err());
    }

    #[test]
    fn coin_and_random_colour_stay_within_their_domain() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        let faces: std::collections::HashSet<String> = (0..200).map(|_| coin("")).collect();
        assert_eq!(faces.len(), 2, "both faces appear: {faces:?}");
        for _ in 0..50 {
            let described = random_color("");
            let hex = described.lines().next().unwrap();
            assert!(hex.starts_with("HEX  #") && hex.len() == 12, "{hex}");
        }
    }

    #[test]
    fn pick_and_shuffle_preserve_the_list() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        let list = "a,b,c,d,e";
        for _ in 0..50 {
            // Draw once, then check membership — calling pick() inside the closure would
            // re-roll for every comparison and could never match.
            let picked = pick(list).unwrap();
            assert!(list.split(',').any(|i| i == picked), "picked {picked:?}");
        }
        let output = shuffle(list);
        let mut shuffled: Vec<&str> = output.lines().collect();
        shuffled.sort();
        assert_eq!(shuffled, ["a", "b", "c", "d", "e"], "shuffle keeps every item exactly once");
        assert!(pick("   ").is_err());
    }

    #[test]
    fn cards_are_drawn_without_replacement() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        let drawn = cards("52").unwrap();
        let hand: Vec<&str> = drawn.split(' ').collect();
        assert_eq!(hand.len(), 52);
        assert_eq!(hand.iter().collect::<std::collections::HashSet<_>>().len(), 52, "no duplicates");
        assert_eq!(cards("1").unwrap().split(' ').count(), 1);
        assert!(cards("53").is_err());
    }

    #[test]
    fn teams_split_everyone_exactly_once() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        let out = teams("3; alice, bob, carol, dave, eve").unwrap();
        assert_eq!(out.lines().count(), 3);
        let names: Vec<&str> = out
            .lines()
            .flat_map(|l| l.split(": ").nth(1).unwrap().split(", "))
            .collect();
        let mut sorted = names.clone();
        sorted.sort();
        assert_eq!(sorted, ["alice", "bob", "carol", "dave", "eve"]);
        // More teams than people collapses rather than leaving empties.
        assert_eq!(teams("9; a, b").unwrap().lines().count(), 2);
        assert!(teams("3").is_err(), "a count with no names");
    }
}
