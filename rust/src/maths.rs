//! Number crunching. Mirrors `Krate.Core.Maths`, including its output formatting — the parity
//! tests compare rendered text, so spacing and rounding are part of the contract.

use crate::i18n;
use crate::tools::{fmt, numbers};
use num_bigint::BigInt;

fn factorial(n: i64) -> BigInt {
    let mut result = BigInt::from(1);
    for i in 2..=n {
        result *= i;
    }
    result
}

/// C(n,k) via the multiplicative formula — avoids ever building the full n!.
pub fn combinations(n: i64, k: i64) -> BigInt {
    let k = k.min(n - k); // C(n,k) == C(n,n-k); pick the cheaper side
    let mut result = BigInt::from(1);
    for i in 0..k {
        result = result * (n - i) / (i + 1);
    }
    result
}

pub fn permutations(n: i64, k: i64) -> BigInt {
    let mut result = BigInt::from(1);
    for i in 0..k {
        result *= n - i;
    }
    result
}

/// "n" gives n!; "n k" gives combinations C(n,k) and permutations P(n,k).
pub fn combinatorics(input: &str) -> Result<String, String> {
    let parts: Vec<&str> = input
        .split([' ', ',', '\n', '\t'])
        .map(str::trim)
        .filter(|p| !p.is_empty())
        .collect();

    let n: i64 = parts
        .first()
        .and_then(|p| p.parse().ok())
        .filter(|n| *n >= 0)
        .ok_or_else(|| i18n::get("Error_NeedNumber").to_string())?;
    if n > 100_000 {
        return Err(i18n::get("Error_TooLarge").to_string());
    }

    if parts.len() == 1 {
        if n > 5000 {
            // 5000! already has ~16k digits.
            return Err(i18n::get("Error_TooLarge").to_string());
        }
        return Ok(format!("{n}! = {}", factorial(n)));
    }

    let k: i64 = parts[1]
        .parse()
        .ok()
        .filter(|k| *k >= 0 && *k <= n)
        .ok_or_else(|| i18n::get("Error_CombinatoricsUsage").to_string())?;

    Ok([
        format!("C({n},{k}) = {}", combinations(n, k)), // order does not matter
        format!("P({n},{k}) = {}", permutations(n, k)), // order matters
    ]
    .join("\n"))
}

/// Solves ax² + bx + c = 0, or ax + b = 0 when a is 0.
pub fn solve(input: &str) -> Result<String, String> {
    let c = numbers(input)?;
    if c.len() == 2 {
        return Ok(if c[0] == 0.0 {
            i18n::get("Math_NoSolution").to_string()
        } else {
            format!("x = {}", fmt(-c[1] / c[0]))
        });
    }
    if c.len() != 3 {
        return Err(i18n::get("Error_SolveUsage").to_string());
    }
    if c[0] == 0.0 {
        return Ok(if c[1] == 0.0 {
            i18n::get("Math_NoSolution").to_string()
        } else {
            format!("x = {}", fmt(-c[2] / c[1]))
        });
    }

    let delta = c[1] * c[1] - 4.0 * c[0] * c[2];
    let mut lines = vec![format!("Δ = {}", fmt(delta))];
    if delta > 0.0 {
        lines.push(format!("x₁ = {}", fmt((-c[1] - delta.sqrt()) / (2.0 * c[0]))));
        lines.push(format!("x₂ = {}", fmt((-c[1] + delta.sqrt()) / (2.0 * c[0]))));
    } else if delta == 0.0 {
        lines.push(format!("x = {}", fmt(-c[1] / (2.0 * c[0]))));
    } else {
        let (re, im) = (-c[1] / (2.0 * c[0]), (-delta).sqrt() / (2.0 * c[0]));
        lines.push(i18n::get("Math_ComplexRoots").to_string());
        lines.push(format!("x₁ = {} - {}i", fmt(re), fmt(im)));
        lines.push(format!("x₂ = {} + {}i", fmt(re), fmt(im)));
    }
    Ok(lines.join("\n"))
}


/// Statistics over a pasted list. Standard deviation is the sample (n-1) form: a pasted list
/// is nearly always a sample rather than a whole population.
pub fn statistics(input: &str) -> Result<String, String> {
    let mut values = numbers(input)?;
    values.sort_by(|a, b| a.partial_cmp(b).unwrap_or(std::cmp::Ordering::Equal));

    let count = values.len();
    let sum: f64 = values.iter().sum();
    let mean = sum / count as f64;
    let variance = if count > 1 {
        values.iter().map(|v| (v - mean) * (v - mean)).sum::<f64>() / (count - 1) as f64
    } else {
        0.0
    };
    let median = if count % 2 == 1 {
        values[count / 2]
    } else {
        (values[count / 2 - 1] + values[count / 2]) / 2.0
    };

    Ok([
        format!("COUNT   {count}"),
        format!("SUM     {}", fmt(sum)),
        format!("MEAN    {}", fmt(mean)),
        format!("MEDIAN  {}", fmt(median)),
        format!("MIN     {}", fmt(values[0])),
        format!("MAX     {}", fmt(values[count - 1])),
        format!("RANGE   {}", fmt(values[count - 1] - values[0])),
        format!("STDDEV  {}", fmt(variance.sqrt())),
    ]
    .join("\n"))
}

pub fn fibonacci(count: usize) -> Vec<i64> {
    let (mut a, mut b) = (0i64, 1i64);
    (0..count)
        .map(|_| {
            let current = a;
            (a, b) = (b, a.saturating_add(b));
            current
        })
        .collect()
}

pub fn primes(count: usize) -> Vec<i64> {
    let mut found = Vec::with_capacity(count);
    let mut n = 2i64;
    while found.len() < count {
        if crate::tools::prime_factors(n).len() == 1 {
            found.push(n);
        }
        n += 1;
    }
    found
}

/// "fib 10", "arith 2 3 10" (start, step, count), "geom 2 3 10" (start, ratio, count).
pub fn sequence(input: &str) -> Result<String, String> {
    let parts: Vec<&str> = input.trim().split([' ', ',']).filter(|p| !p.is_empty()).collect();
    let usage = || i18n::get("Error_SequenceUsage").to_string();
    let kind = parts.first().ok_or_else(usage)?.to_lowercase();

    let n: Vec<f64> = if parts.len() > 1 { numbers(&parts[1..].join(" "))? } else { Vec::new() };
    let count = |index: usize, fallback: usize| -> usize {
        n.get(index).map_or(fallback, |v| (*v as i64).clamp(1, 1000) as usize)
    };

    let joined = match kind.as_str() {
        "fib" | "fibonacci" => fibonacci(count(0, 20))
            .iter()
            .map(i64::to_string)
            .collect::<Vec<_>>()
            .join(", "),
        "prime" | "primes" => primes(count(0, 20))
            .iter()
            .map(i64::to_string)
            .collect::<Vec<_>>()
            .join(", "),
        "arith" | "arithmetic" if n.len() >= 2 => (0..count(2, 10))
            .map(|i| fmt(n[0] + i as f64 * n[1]))
            .collect::<Vec<_>>()
            .join(", "),
        "geom" | "geometric" if n.len() >= 2 => (0..count(2, 10))
            .map(|i| fmt(n[0] * n[1].powi(i as i32)))
            .collect::<Vec<_>>()
            .join(", "),
        _ => return Err(usage()),
    };
    Ok(joined)
}

/// Continued-fraction expansion, matching the C# tolerance and iteration cap exactly so the
/// two builds pick the same approximation.
pub fn to_fraction(value: f64) -> (i64, i64) {
    let tolerance = 1e-9;
    let sign = if value < 0.0 { -1i64 } else { 1 };
    let value = value.abs();
    let (mut low_n, mut low_d, mut high_n, mut high_d) = (0i64, 1i64, 1i64, 0i64);

    for _ in 0..10_000 {
        let mid_n = low_n + high_n;
        let mid_d = low_d + high_d;
        if mid_d == 0 {
            break;
        }
        let mid = mid_n as f64 / mid_d as f64;
        if (mid - value).abs() < tolerance {
            return (sign * mid_n, mid_d);
        }
        if mid < value {
            (low_n, low_d) = (mid_n, mid_d);
        } else {
            (high_n, high_d) = (mid_n, mid_d);
        }
    }
    (sign * (value * 1_000_000.0).round() as i64, 1_000_000)
}

/// Decimal to fraction or back, direction detected from the input.
pub fn fraction(input: &str) -> Result<String, String> {
    let s = input.trim();
    let bad = || i18n::get("Error_NeedNumber").to_string();

    if let Some((left, right)) = s.split_once('/') {
        let num: f64 = left.trim().parse().map_err(|_| bad())?;
        let den: f64 = right.trim().parse().map_err(|_| bad())?;
        if den == 0.0 {
            return Err(i18n::get("Error_DivideByZero").to_string());
        }
        return Ok(fmt(num / den));
    }

    let value: f64 = s.parse().map_err(|_| bad())?;
    let (n, d) = to_fraction(value);
    let whole = n / d;
    let mixed = if whole != 0 && n.abs() > d {
        format!("{whole} {}/{d}", (n % d).abs())
    } else {
        format!("{n}/{d}")
    };
    Ok([format!("{n}/{d}"), mixed, fmt(value)].join("\n"))
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn combinatorics_gives_factorials_and_both_counts() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        assert_eq!(combinatorics("5").unwrap(), "5! = 120");
        assert_eq!(combinatorics("0").unwrap(), "0! = 1");
        let both = combinatorics("5 2").unwrap();
        assert!(both.contains("C(5,2) = 10"), "{both}");
        assert!(both.contains("P(5,2) = 20"), "{both}");
        assert_eq!(combinations(5, 2).to_string(), "10");
        assert_eq!(permutations(5, 2).to_string(), "20");
    }

    #[test]
    fn combinatorics_refuses_what_would_not_fit() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        assert!(combinatorics("6000").is_err(), "factorial guard");
        assert!(combinatorics("200000").is_err(), "hard cap");
        assert!(combinatorics("-1").is_err());
        assert!(combinatorics("5 9").is_err(), "k cannot exceed n");
        assert!(combinatorics("zzz").is_err());
    }

    /// 100! is 158 digits — well past any fixed-width integer, which is why this uses BigInt.
    #[test]
    fn factorials_go_beyond_machine_integers() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        let result = combinatorics("100").unwrap();
        let digits = result.split(" = ").nth(1).unwrap();
        assert_eq!(digits.len(), 158, "100! should have 158 digits");
        assert!(digits.ends_with("0000"), "100! ends in many zeros: {digits}");
    }

    #[test]
    fn solve_handles_both_degrees_and_complex_roots() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        // x^2 - 3x + 2 = 0 has roots 1 and 2.
        let quadratic = solve("1 -3 2").unwrap();
        assert!(quadratic.contains("x₁ = 1"), "{quadratic}");
        assert!(quadratic.contains("x₂ = 2"), "{quadratic}");
        // A single repeated root.
        assert!(solve("1 -2 1").unwrap().contains("x = 1"));
        // Negative discriminant: complex pair.
        assert!(solve("1 0 1").unwrap().contains('i'));
        // Linear fallback.
        assert!(solve("2 -4").unwrap().contains("x = 2"));
        assert!(solve("1 2 3 4").is_err());
    }

    #[test]
    fn statistics_reports_the_usual_summary() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        let result = statistics("1 2 3 4 5").unwrap();
        assert!(result.contains("COUNT   5"), "{result}");
        assert!(result.contains("SUM     15"), "{result}");
        assert!(result.contains("MEAN    3"), "{result}");
        assert!(result.contains("MEDIAN  3"), "{result}");
        assert!(result.contains("RANGE   4"), "{result}");
    }

    #[test]
    fn statistics_medians_an_even_count_by_averaging_the_middle_pair() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        assert!(statistics("1 2 3 4").unwrap().contains("MEDIAN  2.5"));
    }

    #[test]
    fn statistics_of_one_value_has_no_spread() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        let result = statistics("7").unwrap();
        assert!(result.contains("STDDEV  0"), "{result}");
        assert!(result.contains("RANGE   0"), "{result}");
    }

    #[test]
    fn fibonacci_and_primes_start_where_the_csharp_side_starts() {
        assert_eq!(fibonacci(8), vec![0, 1, 1, 2, 3, 5, 8, 13]);
        assert_eq!(primes(8), vec![2, 3, 5, 7, 11, 13, 17, 19]);
        assert!(fibonacci(0).is_empty());
    }

    #[test]
    fn sequence_handles_every_kind() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        assert_eq!(sequence("fib 5").unwrap(), "0, 1, 1, 2, 3");
        assert_eq!(sequence("prime 5").unwrap(), "2, 3, 5, 7, 11");
        assert_eq!(sequence("arith 2 3 4").unwrap(), "2, 5, 8, 11");
        assert_eq!(sequence("geom 2 3 4").unwrap(), "2, 6, 18, 54");
        assert!(sequence("nonsense").is_err());
        assert!(sequence("").is_err());
    }

    #[test]
    fn fraction_converts_both_ways() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        assert_eq!(fraction("3/4").unwrap(), "0.75");
        assert_eq!(to_fraction(0.75), (3, 4));
        assert_eq!(to_fraction(0.5), (1, 2));
        assert_eq!(to_fraction(-0.25), (-1, 4));
        assert!(fraction("1/0").is_err(), "divide by zero");
        assert!(fraction("zzz").is_err());
    }

    #[test]
    fn fraction_shows_a_mixed_number_when_improper() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        let result = fraction("1.5").unwrap();
        assert!(result.starts_with("3/2"), "{result}");
        assert!(result.contains("1 1/2"), "{result}");
    }
}
