//! Exchange rates: the one online tool. Mirrors `Krate.Core.Currency`.
//!
//! On Windows it fetches over **WinHTTP**, not a Rust HTTP crate. Every rustls crypto provider (`ring`,
//! `aws-lc-rs`) needs a C compiler, which this machine does not have — and WinHTTP is the better
//! answer anyway: it is the same SChannel TLS stack `HttpClient` uses on the C# side, it needs no
//! dependency at all, and it picks up the machine's proxy and certificate configuration for free.
//!
//! Rates are cached in `%APPDATA%/KRATE`, in the same files and the same JSON the C# writes, so the
//! two implementations share one cache and agree without either having to hit the network.
//!
//! **Off Windows the core does not fetch at all.** There is no portable twin for WinHTTP worth
//! binding, and on Android the platform's HTTP stack lives in Java. The host fetches and calls
//! [`store_rates`]; the core reads the same cache either way.

use crate::i18n;
use std::path::PathBuf;

const TTL_SECONDS: u64 = 3600;
#[cfg(windows)]
const TIMEOUT_MS: u32 = 8000;

fn cache_dir() -> PathBuf {
    // Environment.SpecialFolder.ApplicationData is %APPDATA%.
    let base = std::env::var("APPDATA").unwrap_or_default();
    PathBuf::from(base).join("KRATE")
}

// ---------------------------------------------------------------------------------------------
// WinHTTP
// ---------------------------------------------------------------------------------------------

#[cfg(windows)]
type Handle = *mut core::ffi::c_void;

#[cfg(windows)]
#[link(name = "winhttp")]
unsafe extern "system" {
    fn WinHttpOpen(
        agent: *const u16,
        access_type: u32,
        proxy: *const u16,
        bypass: *const u16,
        flags: u32,
    ) -> Handle;
    fn WinHttpConnect(session: Handle, server: *const u16, port: u16, reserved: u32) -> Handle;
    fn WinHttpOpenRequest(
        connection: Handle,
        verb: *const u16,
        object: *const u16,
        version: *const u16,
        referrer: *const u16,
        accept_types: *const *const u16,
        flags: u32,
    ) -> Handle;
    fn WinHttpSetTimeouts(
        handle: Handle,
        resolve: i32,
        connect: i32,
        send: i32,
        receive: i32,
    ) -> i32;
    fn WinHttpSendRequest(
        request: Handle,
        headers: *const u16,
        headers_length: u32,
        optional: *const u8,
        optional_length: u32,
        total_length: u32,
        context: usize,
    ) -> i32;
    fn WinHttpReceiveResponse(request: Handle, reserved: *mut core::ffi::c_void) -> i32;
    fn WinHttpQueryDataAvailable(request: Handle, available: *mut u32) -> i32;
    fn WinHttpReadData(
        request: Handle,
        buffer: *mut u8,
        to_read: u32,
        read: *mut u32,
    ) -> i32;
    fn WinHttpCloseHandle(handle: Handle) -> i32;
}

#[cfg(windows)]
const ACCESS_TYPE_AUTOMATIC_PROXY: u32 = 4; // WINHTTP_ACCESS_TYPE_AUTOMATIC_PROXY
#[cfg(windows)]
const FLAG_SECURE: u32 = 0x0080_0000; // WINHTTP_FLAG_SECURE
#[cfg(windows)]
const HTTPS_PORT: u16 = 443;

#[cfg(windows)]
fn wide(text: &str) -> Vec<u16> {
    text.encode_utf16().chain(std::iter::once(0)).collect()
}

/// Closes a WinHTTP handle when it goes out of scope, so an early return cannot leak one.
#[cfg(windows)]
struct Owned(Handle);

#[cfg(windows)]
impl Drop for Owned {
    fn drop(&mut self) {
        if !self.0.is_null() {
            // SAFETY: the handle came from WinHTTP and is closed exactly once.
            unsafe { WinHttpCloseHandle(self.0) };
        }
    }
}

/// An HTTPS GET, returning the body. `None` for any failure — the caller falls back to the cache,
/// so the reason does not change what happens next.
#[cfg(windows)]
fn https_get(host: &str, path: &str) -> Option<String> {
    // SAFETY throughout: every handle is checked for null before use and owned by `Owned`, and
    // every buffer passed out is ours with its length given as the API documents.
    let session = Owned(unsafe {
        WinHttpOpen(
            wide("KRATE").as_ptr(),
            ACCESS_TYPE_AUTOMATIC_PROXY,
            std::ptr::null(),
            std::ptr::null(),
            0,
        )
    });
    if session.0.is_null() {
        return None;
    }
    let timeout = TIMEOUT_MS as i32;
    unsafe { WinHttpSetTimeouts(session.0, timeout, timeout, timeout, timeout) };

    let connection = Owned(unsafe {
        WinHttpConnect(session.0, wide(host).as_ptr(), HTTPS_PORT, 0)
    });
    if connection.0.is_null() {
        return None;
    }

    let request = Owned(unsafe {
        WinHttpOpenRequest(
            connection.0,
            wide("GET").as_ptr(),
            wide(path).as_ptr(),
            std::ptr::null(),
            std::ptr::null(),
            std::ptr::null(),
            FLAG_SECURE,
        )
    });
    if request.0.is_null() {
        return None;
    }

    let sent = unsafe {
        WinHttpSendRequest(request.0, std::ptr::null(), 0, std::ptr::null(), 0, 0, 0)
    };
    if sent == 0 {
        return None;
    }
    if unsafe { WinHttpReceiveResponse(request.0, std::ptr::null_mut()) } == 0 {
        return None;
    }

    let mut body = Vec::new();
    loop {
        let mut available: u32 = 0;
        if unsafe { WinHttpQueryDataAvailable(request.0, &mut available) } == 0 {
            return None;
        }
        if available == 0 {
            break;
        }
        let mut chunk = vec![0u8; available as usize];
        let mut read: u32 = 0;
        if unsafe { WinHttpReadData(request.0, chunk.as_mut_ptr(), available, &mut read) } == 0 {
            return None;
        }
        if read == 0 {
            break;
        }
        chunk.truncate(read as usize);
        body.extend_from_slice(&chunk);
    }
    String::from_utf8(body).ok()
}

/// No HTTP client in the core off Windows — deliberately.
///
/// WinHTTP has no portable twin worth binding: on Android the platform's own stack lives in Java,
/// and pulling a Rust TLS client into the core would add a large dependency to reimplement what the
/// host already has. So the core reads its cache, and the host fills that cache through
/// [`store_rates`] (exposed over the FFI as `krate_currency_store_rates`).
///
/// The user-visible effect is exactly the documented offline path: cached rates, flagged as such.
#[cfg(not(windows))]
fn https_get(_host: &str, _path: &str) -> Option<String> {
    None
}

/// Stores a rate table fetched by the host, in the same file and format the Windows fetch writes.
///
/// This is how a non-Windows shell keeps Currency current: fetch
/// `https://open.er-api.com/v6/latest/<BASE>` however the platform prefers, hand the body here, and
/// the next conversion uses it. Returns an error if the body is not a successful response, so a
/// caller cannot poison the cache with an error page.
pub fn store_rates(base: &str, json: &str) -> Result<(), String> {
    let probe: serde_json::Value =
        serde_json::from_str(json).map_err(|_| i18n::format("Error_UnknownCurrency", &[base]))?;
    if probe.get("result").and_then(|r| r.as_str()) != Some("success") {
        return Err(i18n::format("Error_UnknownCurrency", &[base]));
    }
    std::fs::create_dir_all(cache_dir()).map_err(|e| e.to_string())?;
    let upper = base.to_uppercase();
    std::fs::write(cache_dir().join(format!("rates_{upper}.json")), json)
        .map_err(|e| e.to_string())
}

// ---------------------------------------------------------------------------------------------
// Rates
// ---------------------------------------------------------------------------------------------

struct Rates {
    rates: Vec<(String, f64)>,
    date: String,
    offline: bool,
}

/// Rates for a base currency: fetched when the cache is missing or stale, read from cache otherwise;
/// on any network failure the last cached rates are used and flagged offline.
///
/// Note the C#'s `offline` is `!fetched`, so a **fresh cache also reads as offline** — no fetch
/// happened. That is the behaviour, faithfully reproduced.
fn get_rates(base: &str) -> Result<Rates, String> {
    let cache_path = cache_dir().join(format!("rates_{base}.json"));
    let fresh = std::fs::metadata(&cache_path)
        .and_then(|m| m.modified())
        .map(|modified| {
            modified
                .elapsed()
                .map(|age| age.as_secs() < TTL_SECONDS)
                .unwrap_or(true) // a clock skew into the future counts as fresh
        })
        .unwrap_or(false);

    let mut fetched = false;
    if !fresh {
        if let Some(json) = https_get("open.er-api.com", &format!("/v6/latest/{base}")) {
            // Unparseable is treated as offline, as the C#'s catch-all does.
            if let Ok(probe) = serde_json::from_str::<serde_json::Value>(&json) {
                if probe.get("result").and_then(|r| r.as_str()) != Some("success") {
                    return Err(i18n::format("Error_UnknownCurrency", &[base]));
                }
                std::fs::create_dir_all(cache_dir()).ok();
                if std::fs::write(&cache_path, &json).is_ok() {
                    fetched = true;
                }
            }
        }
    }

    let Ok(text) = std::fs::read_to_string(&cache_path) else {
        return Err(i18n::get("Error_NoRates").to_string());
    };
    let document: serde_json::Value =
        serde_json::from_str(&text).map_err(|_| i18n::get("Error_NoRates").to_string())?;
    let rates = document
        .get("rates")
        .and_then(|r| r.as_object())
        .ok_or_else(|| i18n::get("Error_NoRates").to_string())?
        .iter()
        .filter_map(|(code, value)| value.as_f64().map(|v| (code.clone(), v)))
        .collect();
    let date = document
        .get("time_last_update_utc")
        .and_then(|d| d.as_str())
        .unwrap_or("")
        .to_string();
    Ok(Rates { rates, date, offline: !fetched })
}

/// `double.TryParse(t, NumberStyles.Number, InvariantCulture)` — no exponent, but group separators
/// and a leading or trailing sign are allowed.
fn parse_amount(token: &str) -> Option<f64> {
    let t = token.trim();
    if t.is_empty() {
        return None;
    }
    let t = t.strip_suffix(['+', '-']).unwrap_or(t);
    let (negative, t) = match t.strip_prefix('-') {
        Some(rest) => (true, rest),
        None => (false, t.strip_prefix('+').unwrap_or(t)),
    };
    if t.starts_with(',') {
        return None;
    }
    let digits = t.replace(',', "");
    if digits.is_empty() || !digits.chars().all(|c| c.is_ascii_digit() || c == '.') {
        return None;
    }
    if digits.chars().filter(|c| *c == '.').count() > 1 || !digits.chars().any(|c| c.is_ascii_digit())
    {
        return None;
    }
    digits.parse::<f64>().ok().map(|v| if negative { -v } else { v })
}

/// `{value:0.##}` / `{value:0.####}` — at most N decimals, trailing zeros dropped, rounded half
/// away from zero as .NET's custom formats do.
fn trimmed(value: f64, decimals: u32) -> String {
    let scale = 10f64.powi(decimals as i32);
    let rounded = (value.abs() * scale).round() / scale * value.signum();
    let mut text = format!("{rounded:.*}", decimals as usize);
    if text.contains('.') {
        text = text.trim_end_matches('0').trim_end_matches('.').to_string();
    }
    text
}

/// Pure conversion given a rate table — the testable core.
pub fn compute(amount: f64, rates: &[(String, f64)], to: &str) -> Result<f64, String> {
    rates
        .iter()
        .find(|(code, _)| code == to)
        .map(|(_, rate)| amount * rate)
        .ok_or_else(|| i18n::format("Error_UnknownCurrency", &[to]))
}

/// "100 USD EUR" — the amount is optional and defaults to 1.
pub fn convert(input: &str) -> Result<String, String> {
    let mut amount = 1.0;
    let mut codes: Vec<String> = Vec::new();
    for token in input.split([' ', ',', '\t', '\n']).map(str::trim).filter(|t| !t.is_empty()) {
        if let Some(value) = parse_amount(token) {
            amount = value;
        } else if token.chars().count() == 3 && token.chars().all(|c| c.is_alphabetic()) {
            codes.push(token.to_uppercase());
        }
    }
    if codes.len() < 2 {
        return Err(i18n::get("Error_CurrencyUsage").to_string());
    }
    let (from, to) = (&codes[0], &codes[1]);

    let table = get_rates(from)?;
    let rate = table
        .rates
        .iter()
        .find(|(code, _)| code == to)
        .map(|(_, rate)| *rate)
        .ok_or_else(|| i18n::format("Error_UnknownCurrency", &[to]))?;

    let offline_note = if table.offline {
        format!("  {}", i18n::get("Cur_Offline"))
    } else {
        String::new()
    };
    Ok([
        format!(
            "{} {from} = {} {to}",
            trimmed(amount, 2),
            trimmed(amount * rate, 2)
        ),
        format!(
            "{}  1 {from} = {} {to}",
            i18n::get("Cur_Rate"),
            trimmed(rate, 4)
        ),
        format!("{}  {}{offline_note}", i18n::get("Cur_Updated"), table.date),
    ]
    .join("\n"))
}

#[cfg(test)]
mod tests {
    use super::*;

    fn english() -> std::sync::MutexGuard<'static, ()> {
        let guard = crate::i18n::test_lock();
        i18n::set_language("en");
        guard
    }

    /// Amounts follow NumberStyles.Number: separators and signs yes, exponents no.
    #[test]
    fn amounts_parse_like_number_styles_number() {
        assert_eq!(parse_amount("100"), Some(100.0));
        assert_eq!(parse_amount("1.5"), Some(1.5));
        assert_eq!(parse_amount("1,000"), Some(1000.0));
        assert_eq!(parse_amount("-5"), Some(-5.0));
        assert_eq!(parse_amount("+5"), Some(5.0));
        assert_eq!(parse_amount("0"), Some(0.0));
        // Not numbers to NumberStyles.Number.
        assert_eq!(parse_amount("1e5"), None, "no exponent");
        assert_eq!(parse_amount("USD"), None);
        assert_eq!(parse_amount(""), None);
        assert_eq!(parse_amount("1.2.3"), None);
        assert_eq!(parse_amount(",5"), None);
        assert_eq!(parse_amount("."), None);
    }

    #[test]
    fn the_formatter_drops_trailing_zeros() {
        assert_eq!(trimmed(1.0, 2), "1");
        assert_eq!(trimmed(1.5, 2), "1.5");
        assert_eq!(trimmed(1.234, 2), "1.23");
        assert_eq!(trimmed(1.235, 2), "1.24", "half away from zero");
        assert_eq!(trimmed(0.918273, 4), "0.9183");
        assert_eq!(trimmed(100.0, 4), "100");
        assert_eq!(trimmed(-2.5, 2), "-2.5");
    }

    #[test]
    fn compute_needs_a_known_code() {
        let _guard = english();
        let rates = vec![("EUR".to_string(), 0.9), ("GBP".to_string(), 0.8)];
        assert_eq!(compute(100.0, &rates, "EUR").unwrap(), 90.0);
        assert!(compute(1.0, &rates, "XYZ").is_err());
    }

    #[test]
    fn two_codes_are_required() {
        let _guard = english();
        assert!(convert("").is_err());
        assert!(convert("100").is_err());
        assert!(convert("USD").is_err());
        assert!(convert("100 USD").is_err());
        // Four-letter tokens are not codes.
        assert!(convert("USDX EURX").is_err());
    }

    /// The whole pipeline against a cache file written by hand, so no network is involved.
    #[test]
    fn a_cached_table_converts_without_a_network() {
        let _guard = english();
        // The cache is shared with the C#, so this writes the real file for a made-up base code.
        // Three letters, or it is not a currency code at all. Randomised so a stale file from a
        // previous run cannot make this pass.
        const LETTERS: &[u8] = b"ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        let base: String = (0..3)
            .map(|_| LETTERS[crate::csprng::below(LETTERS.len())] as char)
            .collect();
        let path = cache_dir().join(format!("rates_{base}.json"));
        std::fs::create_dir_all(cache_dir()).unwrap();
        std::fs::write(
            &path,
            r#"{"result":"success","time_last_update_utc":"Wed, 30 Jul 2026 00:02:31 +0000",
                "rates":{"EUR":0.918273,"GBP":0.8,"USD":1}}"#,
        )
        .unwrap();

        let out = convert(&format!("100 {base} EUR")).unwrap();
        let lines: Vec<&str> = out.lines().collect();
        assert_eq!(lines[0], format!("100 {base} = 91.83 EUR"), "{out}");
        assert!(lines[1].contains("1 ") && lines[1].contains("0.9183"), "{out}");
        assert!(lines[2].contains("Wed, 30 Jul 2026"), "{out}");
        // A fresh cache means no fetch happened, which the C# reports as offline.
        assert!(lines[2].contains(i18n::get("Cur_Offline")), "{out}");

        // The default amount is 1.
        let one = convert(&format!("{base} GBP")).unwrap();
        assert!(one.starts_with(&format!("1 {base} = 0.8 GBP")), "{one}");

        // An unknown target code is refused even with a good table.
        assert!(convert(&format!("10 {base} XYZ")).is_err());

        std::fs::remove_file(&path).ok();
    }

    // NOT tested here: the no-cache, no-network path. Exercising it would make a real request
    // with an eight-second timeout, which does not belong in a unit test. The C# parity test
    // covers the cached path, which is the one that has to agree.
}
