//! URL parsing, CSS minifying and the environment listing. Mirrors `Dev.UrlParse`,
//! `Dev.EnvVars` and `Css.Minify`.

use crate::i18n;
use crate::tools::url_decode;

/// Default ports .NET's `Uri.IsDefaultPort` knows about, so they are omitted from the output.
const DEFAULT_PORTS: &[(&str, u16)] = &[
    ("http", 80), ("https", 443), ("ftp", 21), ("ftps", 990),
    ("ws", 80), ("wss", 443), ("gopher", 70), ("telnet", 23),
    ("nntp", 119), ("news", 119), ("ldap", 389), ("smtp", 25),
];

struct Url {
    scheme: String,
    host: String,
    port: Option<u16>,
    path: String,
    query: String,
    fragment: String,
}

/// Absolute URLs only, matching `Uri.TryCreate(..., UriKind.Absolute, ...)`.
fn parse_url(text: &str) -> Option<Url> {
    let text = text.trim();
    let (scheme, rest) = text.split_once("://")?;
    if scheme.is_empty() || !scheme.chars().all(|c| c.is_ascii_alphanumeric() || matches!(c, '+' | '-' | '.')) {
        return None;
    }

    // Fragment first, then query: both are stripped from the right.
    let (rest, fragment) = match rest.split_once('#') {
        Some((before, after)) => (before, format!("#{after}")),
        None => (rest, String::new()),
    };
    let (authority_and_path, query) = match rest.split_once('?') {
        Some((before, after)) => (before, after.to_string()),
        None => (rest, String::new()),
    };

    let (authority, path) = match authority_and_path.find('/') {
        Some(i) => (&authority_and_path[..i], authority_and_path[i..].to_string()),
        // An authority with no path still reports "/", as Uri.AbsolutePath does.
        None => (authority_and_path, "/".to_string()),
    };
    if authority.is_empty() {
        return None;
    }

    let (host, port) = match authority.rsplit_once(':') {
        Some((h, p)) => match p.parse::<u16>() {
            Ok(n) => (h.to_string(), Some(n)),
            Err(_) => (authority.to_string(), None),
        },
        None => (authority.to_string(), None),
    };
    if host.is_empty() {
        return None;
    }

    Some(Url {
        scheme: scheme.to_lowercase(),
        host: host.to_lowercase(),
        port,
        path: if path.is_empty() { "/".to_string() } else { path },
        query,
        fragment,
    })
}

fn is_default_port(scheme: &str, port: u16) -> bool {
    DEFAULT_PORTS
        .iter()
        .any(|(s, p)| *s == scheme && *p == port)
}

pub fn url_parse(input: &str) -> Result<String, String> {
    let url = parse_url(input).ok_or_else(|| i18n::get("Error_UrlUsage").to_string())?;

    // Built with AppendLine on the C# side, so every line ends with Environment.NewLine.
    // The one exception is deliberate and matched below.
    let nl = crate::tools::newline();
    let mut out = String::new();
    out.push_str(&format!("Scheme: {}{nl}", url.scheme));
    out.push_str(&format!("Host:   {}{nl}", url.host));
    if let Some(port) = url.port {
        if !is_default_port(&url.scheme, port) {
            out.push_str(&format!("Port:   {port}{nl}"));
        }
    }
    out.push_str(&format!("Path:   {}{nl}", url.path));

    if !url.query.is_empty() {
        out.push_str(nl); // a bare AppendLine()
        out.push_str(&format!("Query Parameters:{nl}"));
        for pair in url.query.split('&').filter(|p| !p.is_empty()) {
            let (key, value) = match pair.split_once('=') {
                // '+' means space in a query value.
                Some((k, v)) => (url_decode(k), url_decode(&v.replace('+', " "))),
                None => (url_decode(pair), String::new()),
            };
            out.push_str(&format!("- {key}: {value}{nl}"));
        }
    }
    if !url.fragment.is_empty() {
        // The C# writes AppendLine("\nFragment: ..."), so the *separator* here is a bare LF even
        // on Windows while the line terminator is still CRLF. Inconsistent, but it is the output.
        out.push_str(&format!("\nFragment: {}{nl}", url.fragment));
    }
    Ok(out.trim().to_string())
}

/// Strips comments and collapses whitespace to shrink CSS.
///
/// Four passes, matching the C# regexes: comments out, whitespace runs to one space, space
/// removed around the delimiters, then the redundant semicolon before a closing brace.
pub fn css_minify(css: &str) -> Result<String, String> {
    if css.trim().is_empty() {
        return Err(i18n::get("Error_EmptyInput").to_string());
    }

    // `/\*.*?\*/` with Singleline: non-greedy, so nested-looking comments end at the first `*/`.
    let mut without_comments = String::with_capacity(css.len());
    let bytes: Vec<char> = css.chars().collect();
    let mut i = 0;
    while i < bytes.len() {
        if bytes[i] == '/' && bytes.get(i + 1) == Some(&'*') {
            match css
                .char_indices()
                .skip(i + 2)
                .collect::<Vec<_>>()
                .windows(2)
                .position(|w| w[0].1 == '*' && w[1].1 == '/')
            {
                Some(offset) => {
                    i = i + 2 + offset + 2;
                    continue;
                }
                // An unterminated comment swallows the rest, as the regex does.
                None => break,
            }
        }
        without_comments.push(bytes[i]);
        i += 1;
    }

    // `\s+` to a single space.
    let mut collapsed = String::with_capacity(without_comments.len());
    let mut in_space = false;
    for c in without_comments.chars() {
        if c.is_whitespace() {
            if !in_space {
                collapsed.push(' ');
                in_space = true;
            }
        } else {
            collapsed.push(c);
            in_space = false;
        }
    }

    // `\s*([{}:;,>~+])\s*` to just the delimiter.
    const DELIMITERS: [char; 8] = ['{', '}', ':', ';', ',', '>', '~', '+'];
    let mut tightened = String::with_capacity(collapsed.len());
    let chars: Vec<char> = collapsed.chars().collect();
    let mut j = 0;
    while j < chars.len() {
        let c = chars[j];
        if DELIMITERS.contains(&c) {
            while tightened.ends_with(' ') {
                tightened.pop();
            }
            tightened.push(c);
            j += 1;
            while j < chars.len() && chars[j] == ' ' {
                j += 1;
            }
            continue;
        }
        tightened.push(c);
        j += 1;
    }

    // The last declaration's semicolon is redundant.
    Ok(tightened.replace(";}", "}").trim().to_string())
}

/// The process environment, sorted by name, with PATH expanded one entry per line.
pub fn env_vars(_: &str) -> String {
    let mut names: Vec<(String, String)> = std::env::vars().collect();
    names.sort_by(|a, b| a.0.cmp(&b.0));

    let mut out = String::new();
    for (key, value) in names {
        if key.eq_ignore_ascii_case("PATH") {
            // Split on the Windows separator, matching the C# which hard-codes ';'.
            out.push_str(&format!("{key}:\n  {}\n\n", value.replace(';', "\n  ")));
        } else {
            out.push_str(&format!("{key}: {value}\n"));
        }
    }
    out.trim().to_string()
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn url_parse_breaks_out_every_part() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        let out = url_parse("https://x.com/a/b?q=1&r=a+b#frag").unwrap();
        assert!(out.contains("Scheme: https"), "{out}");
        assert!(out.contains("Host:   x.com"), "{out}");
        assert!(out.contains("Path:   /a/b"), "{out}");
        assert!(out.contains("- q: 1"), "{out}");
        assert!(out.contains("- r: a b"), "'+' is a space in a query: {out}");
        assert!(out.contains("Fragment: #frag"), "the fragment keeps its hash: {out}");
    }

    /// A default port is omitted; a non-default one is shown. That is `Uri.IsDefaultPort`.
    #[test]
    fn url_parse_hides_only_default_ports() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        assert!(url_parse("http://x.com:8080/p").unwrap().contains("Port:   8080"));
        assert!(!url_parse("https://x.com:443/").unwrap().contains("Port:"), "443 is default for https");
        assert!(!url_parse("http://x.com:80/").unwrap().contains("Port:"), "80 is default for http");
        assert!(url_parse("https://x.com:80/").unwrap().contains("Port:   80"), "80 is not default for https");
    }

    #[test]
    fn url_parse_defaults_an_empty_path_to_slash() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        assert!(url_parse("https://x.com").unwrap().contains("Path:   /"));
        assert!(url_parse("ftp://a.b/c").unwrap().contains("Scheme: ftp"));
    }

    #[test]
    fn url_parse_requires_an_absolute_url() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        for bad in ["notaurl", "", "/just/a/path", "https://", "://x.com"] {
            assert!(url_parse(bad).is_err(), "{bad:?} should not parse");
        }
    }

    #[test]
    fn css_minify_strips_comments_and_space() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        assert_eq!(
            css_minify("a { color : red ; }").unwrap(),
            "a{color:red}",
            "the last semicolon goes too"
        );
        assert_eq!(css_minify("/* note */ a{b:c}").unwrap(), "a{b:c}");
        assert_eq!(
            css_minify("a > b ~ c + d { x : y }").unwrap(),
            "a>b~c+d{x:y}",
            "space around every delimiter is removed"
        );
    }

    #[test]
    fn css_minify_handles_multiline_comments_and_rejects_nothing() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        assert_eq!(css_minify("a{b:c}/* multi\nline\ncomment */").unwrap(), "a{b:c}");
        // Non-greedy: the first close ends the comment, so the second rule survives.
        assert_eq!(css_minify("/*x*/a{b:c}/*y*/d{e:f}").unwrap(), "a{b:c}d{e:f}");
        assert!(css_minify("").is_err());
        assert!(css_minify("   ").is_err());
    }

    #[test]
    fn env_vars_lists_sorted_and_expands_path() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        // SAFETY: single-threaded within this locked test; the guard serialises the suite.
        unsafe { std::env::set_var("KRATE_TEST_VAR", "sentinel-value") };
        let out = env_vars("");
        unsafe { std::env::remove_var("KRATE_TEST_VAR") };

        assert!(out.contains("KRATE_TEST_VAR: sentinel-value"), "the variable is listed");
        let names: Vec<&str> = out
            .lines()
            .filter(|l| l.contains(": ") && !l.starts_with("  "))
            .map(|l| l.split(':').next().unwrap())
            .collect();
        let mut sorted = names.clone();
        sorted.sort();
        assert_eq!(names, sorted, "output is sorted by name");
    }
}
