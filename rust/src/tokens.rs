//! JWT decoding and .gitignore templates. Mirrors `Escapes.Jwt` and `Dev.Gitignore`.

use crate::i18n;
use crate::json;
use base64::Engine as _;

/// Days-to-civil-date, Howard Hinnant's algorithm. Hand-rolled rather than pulling in a date
/// crate: this is the only calendar arithmetic the module needs, and it is exact for all the
/// range a JWT can express.
fn civil_from_days(days: i64) -> (i64, u32, u32) {
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

/// Formats a Unix timestamp as `yyyy-MM-dd HH:mm:ss` in UTC.
pub fn utc_timestamp(seconds: i64) -> String {
    let days = seconds.div_euclid(86_400);
    let rest = seconds.rem_euclid(86_400);
    let (year, month, day) = civil_from_days(days);
    let (h, mi, s) = (rest / 3600, (rest % 3600) / 60, rest % 60);
    format!("{year:04}-{month:02}-{day:02} {h:02}:{mi:02}:{s:02}")
}

fn now_seconds() -> i64 {
    std::time::SystemTime::now()
        .duration_since(std::time::UNIX_EPOCH)
        .map(|d| d.as_secs() as i64)
        .unwrap_or(0)
}

/// base64url without padding, as JWT segments are encoded.
fn from_base64url(segment: &str) -> Result<String, String> {
    let bad = || i18n::get("Error_BadJwt").to_string();
    let bytes = base64::engine::general_purpose::URL_SAFE_NO_PAD
        .decode(segment.trim_end_matches('='))
        .map_err(|_| bad())?;
    String::from_utf8(bytes).map_err(|_| bad())
}

/// Decodes a JWT's header and payload and dates its claims. **Never verifies the signature** —
/// that needs the key, and pretending otherwise would be worse than saying so.
pub fn jwt(input: &str) -> Result<String, String> {
    let parts: Vec<&str> = input.trim().split('.').collect();
    if parts.len() < 2 {
        return Err(i18n::get("Error_BadJwt").to_string());
    }

    let header = json::format(&from_base64url(parts[0])?)?;
    let payload_text = from_base64url(parts[1])?;
    let claims = json::format(&payload_text)?;

    let mut dates: Vec<String> = Vec::new();
    if let Ok(payload) = serde_json::from_str::<serde_json::Value>(&payload_text) {
        for claim in ["iat", "nbf", "exp"] {
            let Some(value) = payload.get(claim).and_then(|v| v.as_i64()) else { continue };
            let note = if claim == "exp" {
                i18n::get(if value < now_seconds() { "Jwt_Expired" } else { "Jwt_Valid" })
            } else {
                ""
            };
            dates.push(format!("{claim}  {}Z  {note}", utc_timestamp(value)).trim_end().to_string());
        }
    }

    let mut lines = vec![
        i18n::get("Jwt_Header").to_string(),
        header,
        String::new(),
        i18n::get("Jwt_Payload").to_string(),
        claims,
    ];
    if !dates.is_empty() {
        lines.push(String::new());
        lines.push(i18n::get("Jwt_Dates").to_string());
        lines.extend(dates);
    }
    lines.push(String::new());
    lines.push(i18n::get("Jwt_NotVerified").to_string());
    Ok(lines.join("\n"))
}

/// Insertion-ordered so the error message lists the names in the same order as the C# dictionary.
const TEMPLATES: &[(&str, &[&str])] = &[
    ("node", &["node_modules/", "npm-debug.log*", "yarn-error.log", ".npm/", "dist/", ".env", ".env.local"]),
    ("python", &["__pycache__/", "*.py[cod]", ".venv/", "venv/", "*.egg-info/", ".pytest_cache/", ".mypy_cache/", "build/", "dist/"]),
    ("dotnet", &["bin/", "obj/", "*.user", ".vs/", "*.suo", "TestResults/"]),
    ("csharp", &["bin/", "obj/", "*.user", ".vs/"]),
    ("rust", &["/target/", "Cargo.lock", "**/*.rs.bk"]),
    ("go", &["*.exe", "*.test", "*.out", "/vendor/", "/bin/"]),
    ("java", &["*.class", "target/", "*.jar", ".gradle/", "build/"]),
    ("macos", &[".DS_Store", ".AppleDouble", "._*", ".Spotlight-V100", ".Trashes"]),
    ("windows", &["Thumbs.db", "Desktop.ini", "$RECYCLE.BIN/", "*.lnk"]),
    ("visualstudio", &[".vs/", "*.user", "bin/", "obj/"]),
    ("jetbrains", &[".idea/", "*.iml", "out/"]),
    ("vscode", &[".vscode/*", "!.vscode/settings.json", "!.vscode/extensions.json"]),
];

fn template_names() -> String {
    TEMPLATES.iter().map(|(name, _)| *name).collect::<Vec<_>>().join(", ")
}

/// A .gitignore for the named tools or languages, each as a labelled block.
pub fn gitignore(input: &str) -> Result<String, String> {
    let names: Vec<&str> = input
        .split([',', ' ', '\n'])
        .map(str::trim)
        .filter(|n| !n.is_empty())
        .collect();
    if names.is_empty() {
        return Err(i18n::format("Error_GitignoreUsage", &[&template_names()]));
    }

    let mut blocks: Vec<String> = Vec::new();
    let mut unknown: Vec<&str> = Vec::new();
    for name in names {
        // Matched case-insensitively, as the C# dictionary is.
        match TEMPLATES.iter().find(|(key, _)| key.eq_ignore_ascii_case(name)) {
            Some((_, lines)) => blocks.push(format!("# {name}\n{}", lines.join("\n"))),
            None => unknown.push(name),
        }
    }
    if blocks.is_empty() {
        return Err(i18n::format(
            "Error_GitignoreUnknown",
            &[&unknown.join(", "), &template_names()],
        ));
    }
    Ok(blocks.join("\n\n"))
}

#[cfg(test)]
mod tests {
    use super::*;

    /// Checked against known epochs rather than a library, since this replaces one.
    #[test]
    fn civil_dates_are_correct_at_the_awkward_points() {
        assert_eq!(utc_timestamp(0), "1970-01-01 00:00:00");
        assert_eq!(utc_timestamp(1_600_000_000), "2020-09-13 12:26:40");
        // Leap day, and the day after.
        assert_eq!(utc_timestamp(1_582_934_400), "2020-02-29 00:00:00");
        assert_eq!(utc_timestamp(1_583_020_800), "2020-03-01 00:00:00");
        // 1900 was not a leap year, 2000 was.
        assert_eq!(utc_timestamp(951_782_400), "2000-02-29 00:00:00");
        // Before the epoch.
        assert_eq!(utc_timestamp(-1), "1969-12-31 23:59:59");
        assert_eq!(utc_timestamp(-86_400), "1969-12-31 00:00:00");
    }

    #[test]
    fn jwt_decodes_both_segments_and_dates_the_claims() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        // {"alg":"HS256"} . {"sub":"1","iat":1600000000,"exp":1600003600} . sig
        let token = "eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxIiwiaWF0IjoxNjAwMDAwMDAwLCJleHAiOjE2MDAwMDM2MDB9.sig";
        let out = jwt(token).unwrap();
        assert!(out.contains("\"alg\": \"HS256\""), "{out}");
        assert!(out.contains("\"sub\": \"1\""), "{out}");
        assert!(out.contains("iat  2020-09-13 12:26:40Z"), "{out}");
        assert!(out.contains("exp  2020-09-13 13:26:40Z"), "{out}");
        // A 2020 expiry is long past.
        assert!(out.contains(i18n::get("Jwt_Expired")), "{out}");
        assert!(out.contains(i18n::get("Jwt_NotVerified")), "the signature caveat must always show");
    }

    #[test]
    fn jwt_rejects_what_is_not_a_token() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        assert!(jwt("notatoken").is_err(), "needs at least two segments");
        assert!(jwt("").is_err());
        assert!(jwt("!!!.!!!").is_err(), "segments must be base64url");
    }

    #[test]
    fn gitignore_emits_labelled_blocks() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        let out = gitignore("rust").unwrap();
        assert_eq!(out, "# rust\n/target/\nCargo.lock\n**/*.rs.bk");

        let two = gitignore("node, python").unwrap();
        assert_eq!(two.split("\n\n").count(), 2, "{two}");
        assert!(two.starts_with("# node"), "{two}");
    }

    #[test]
    fn gitignore_is_case_insensitive_and_reports_unknown_names() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        assert_eq!(gitignore("RUST").unwrap(), gitignore("rust").unwrap().replace("# rust", "# RUST"));
        // A mix keeps what it knows and silently drops the rest, as the C# does.
        assert!(gitignore("rust nonsense").unwrap().contains("/target/"));
        // All unknown is an error listing the available names.
        let err = gitignore("nonsense").unwrap_err();
        assert!(err.contains("rust"), "the error lists the templates: {err}");
        assert!(gitignore("").is_err());
    }
}
