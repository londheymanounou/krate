//! HTTP status names. Mirrors `Krate.Core.Dev.HttpStatus`.
//!
//! The C# reads .NET's `HttpStatusCode` enum and splits the member name on camel humps, so the
//! set is whatever that enum happens to contain — 418 is absent, for instance. The table below
//! was produced by probing the C# build across 100..599 rather than from the RFCs, because the
//! RFC list and the enum are not the same set.

use crate::i18n;

const STATUSES: &[(u16, &str)] = &[
    (100, "Continue"),
    (101, "Switching Protocols"),
    (102, "Processing"),
    (103, "Early Hints"),
    (200, "OK"),
    (201, "Created"),
    (202, "Accepted"),
    (203, "Non Authoritative Information"),
    (204, "No Content"),
    (205, "Reset Content"),
    (206, "Partial Content"),
    (207, "Multi Status"),
    (208, "Already Reported"),
    (226, "IMUsed"),
    (300, "Multiple Choices"),
    (301, "Moved Permanently"),
    (302, "Found"),
    (303, "See Other"),
    (304, "Not Modified"),
    (305, "Use Proxy"),
    (306, "Unused"),
    (307, "Redirect Keep Verb"),
    (308, "Permanent Redirect"),
    (400, "Bad Request"),
    (401, "Unauthorized"),
    (402, "Payment Required"),
    (403, "Forbidden"),
    (404, "Not Found"),
    (405, "Method Not Allowed"),
    (406, "Not Acceptable"),
    (407, "Proxy Authentication Required"),
    (408, "Request Timeout"),
    (409, "Conflict"),
    (410, "Gone"),
    (411, "Length Required"),
    (412, "Precondition Failed"),
    (413, "Request Entity Too Large"),
    (414, "Request Uri Too Long"),
    (415, "Unsupported Media Type"),
    (416, "Requested Range Not Satisfiable"),
    (417, "Expectation Failed"),
    (421, "Misdirected Request"),
    (422, "Unprocessable Entity"),
    (423, "Locked"),
    (424, "Failed Dependency"),
    (426, "Upgrade Required"),
    (428, "Precondition Required"),
    (429, "Too Many Requests"),
    (431, "Request Header Fields Too Large"),
    (451, "Unavailable For Legal Reasons"),
    (500, "Internal Server Error"),
    (501, "Not Implemented"),
    (502, "Bad Gateway"),
    (503, "Service Unavailable"),
    (504, "Gateway Timeout"),
    (505, "Http Version Not Supported"),
    (506, "Variant Also Negotiates"),
    (507, "Insufficient Storage"),
    (508, "Loop Detected"),
    (510, "Not Extended"),
    (511, "Network Authentication Required"),
];

pub fn http_status(input: &str) -> Result<String, String> {
    let code: u16 = input
        .trim()
        .parse()
        .map_err(|_| i18n::get("Error_HttpStatusUsage").to_string())?;
    match STATUSES.iter().find(|(c, _)| *c == code) {
        Some((c, name)) => Ok(format!("{c} {name}")),
        None => Ok(i18n::get("HttpStatus_Unknown").to_string()),
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn known_codes_get_their_dotnet_name() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        assert_eq!(http_status("200").unwrap(), "200 OK");
        assert_eq!(http_status("404").unwrap(), "404 Not Found");
        assert_eq!(http_status("301").unwrap(), "301 Moved Permanently");
        assert_eq!(http_status("500").unwrap(), "500 Internal Server Error");
        assert_eq!(http_status(" 204 ").unwrap(), "204 No Content");
    }

    /// 418 is a real HTTP status but is not in .NET's enum, so the C# reports it as unknown.
    /// Matching that matters more than being right about teapots.
    #[test]
    fn codes_outside_the_dotnet_enum_are_unknown() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        for code in ["418", "599", "999", "0"] {
            assert_eq!(http_status(code).unwrap(), i18n::get("HttpStatus_Unknown"), "{code}");
        }
        assert!(http_status("zzz").is_err());
        assert!(http_status("").is_err());
    }

    #[test]
    fn the_table_covers_every_status_the_csharp_knows() {
        assert_eq!(STATUSES.len(), 61, "table was generated from the C# build");
    }
}
