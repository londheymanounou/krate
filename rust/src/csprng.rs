//! Cryptographic randomness, straight from the OS.
//!
//! Why not `rand`/`getrandom`: they reach `bcryptprimitives.dll` through `raw-dylib`, and the
//! GNU toolchain pinned here (see `rust-toolchain.toml`) cannot build that import library —
//! `dlltool` fails. Dropping to a seeded userspace PRNG was not an option, because these
//! functions generate passwords. So the OS entropy source is called directly.
//!
//! On Windows that is `RtlGenRandom` (exported as `SystemFunction036` from advapi32), the same
//! primitive .NET's `RandomNumberGenerator` ultimately uses. Elsewhere, `/dev/urandom`.

#[cfg(windows)]
#[link(name = "advapi32")]
unsafe extern "system" {
    #[link_name = "SystemFunction036"]
    fn rtl_gen_random(buffer: *mut u8, length: u32) -> u8;
}

/// Fills `buffer` with cryptographically secure bytes.
///
/// # Panics
/// If the OS refuses to supply entropy. There is no sensible fallback: silently returning
/// predictable bytes from a password generator would be far worse than stopping.
pub fn fill(buffer: &mut [u8]) {
    if buffer.is_empty() {
        return;
    }

    #[cfg(windows)]
    {
        let ok = unsafe { rtl_gen_random(buffer.as_mut_ptr(), buffer.len() as u32) };
        assert!(ok != 0, "the OS refused to supply entropy");
    }

    #[cfg(not(windows))]
    {
        use std::io::Read;
        let mut source = std::fs::File::open("/dev/urandom").expect("/dev/urandom is unavailable");
        source.read_exact(buffer).expect("could not read entropy");
    }
}

fn next_u64() -> u64 {
    let mut bytes = [0u8; 8];
    fill(&mut bytes);
    u64::from_le_bytes(bytes)
}

/// A uniform integer in `min..=max`, free of modulo bias.
///
/// The naive `value % range` skews towards the low end whenever the range does not divide the
/// generator's span. This rejects the unfair tail instead, which is what `RandomNumberGenerator
/// .GetInt32` does too.
pub fn range_inclusive(min: i64, max: i64) -> i64 {
    assert!(min <= max, "range_inclusive called with min > max");
    let span = (max as i128 - min as i128 + 1) as u128;
    if span == 1 {
        return min;
    }

    // Largest multiple of span that fits in u64; anything at or above it would bias the result.
    let limit = (u64::MAX as u128 + 1) / span * span;
    loop {
        let value = next_u64() as u128;
        if value < limit {
            return (min as i128 + (value % span) as i128) as i64;
        }
    }
}

/// Uniform index in `0..len`.
pub fn below(len: usize) -> usize {
    assert!(len > 0, "below called with an empty range");
    range_inclusive(0, len as i64 - 1) as usize
}

/// Fisher-Yates, drawing each index from the unbiased source above.
pub fn shuffle<T>(items: &mut [T]) {
    for i in (1..items.len()).rev() {
        items.swap(i, below(i + 1));
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn fill_produces_varied_bytes() {
        let mut a = [0u8; 32];
        let mut b = [0u8; 32];
        fill(&mut a);
        fill(&mut b);
        assert_ne!(a, b, "two draws should not match");
        assert!(a.iter().any(|byte| *byte != 0), "all zeroes is not plausible");
        // An empty buffer must be a no-op rather than a panic.
        fill(&mut []);
    }

    #[test]
    fn range_stays_within_bounds_and_covers_them() {
        let mut seen = std::collections::HashSet::new();
        for _ in 0..500 {
            let v = range_inclusive(1, 3);
            assert!((1..=3).contains(&v), "{v} out of range");
            seen.insert(v);
        }
        assert_eq!(seen.len(), 3, "every value in a small range should appear");
        assert_eq!(range_inclusive(7, 7), 7, "a single-value range is that value");
        assert!((-5..=-1).contains(&range_inclusive(-5, -1)), "negative ranges work");
    }

    /// A biased generator shows up as a lopsided distribution. With 6000 draws over 6 buckets the
    /// expected count is 1000 each; anything outside 800..1200 is far beyond normal variance.
    #[test]
    fn range_is_not_visibly_biased() {
        let mut counts = [0usize; 6];
        for _ in 0..6000 {
            counts[range_inclusive(0, 5) as usize] += 1;
        }
        for (face, count) in counts.iter().enumerate() {
            assert!(
                (800..=1200).contains(count),
                "face {face} came up {count} times in 6000 draws: {counts:?}"
            );
        }
    }

    #[test]
    fn shuffle_is_a_permutation() {
        let mut items: Vec<i32> = (0..100).collect();
        shuffle(&mut items);
        let mut sorted = items.clone();
        sorted.sort();
        assert_eq!(sorted, (0..100).collect::<Vec<_>>(), "every item survives exactly once");
        assert_ne!(items, sorted, "100 items should not come back in order");

        // Degenerate sizes must not panic.
        let mut one = [1];
        shuffle(&mut one);
        assert_eq!(one, [1]);
        shuffle(&mut [] as &mut [i32]);
    }
}
