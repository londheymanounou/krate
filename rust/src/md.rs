//! Markdown to HTML, and bulk file renaming. Mirrors `Markdown.ToHtml` and `Files.BulkRename`.

use crate::i18n;
use crate::tools::{html_encode, newline};
use std::path::{Path, PathBuf};

/// A heading: one to six `#` then whitespace then a non-empty rest. `^(#{1,6})\s+(.+)$`.
fn heading(line: &str) -> Option<(usize, &str)> {
    let hashes = line.len() - line.trim_start_matches('#').len();
    if !(1..=6).contains(&hashes) {
        return None;
    }
    let rest = &line[hashes..];
    let text = rest.trim_start_matches(|c: char| c.is_whitespace());
    // `\s+` must consume at least one character, and `(.+)` at least one more.
    if text.len() == rest.len() || text.is_empty() {
        return None;
    }
    Some((hashes, text))
}

/// `^(\*\s*){3,}$|^(-\s*){3,}$|^(_\s*){3,}$` — three or more of one marker, spaces allowed after
/// each. Note the marker must come *first* in each repetition, so `- - -` matches but ` - - -`
/// does not.
fn is_horizontal_rule(line: &str) -> bool {
    ['*', '-', '_'].iter().any(|&marker| {
        let mut count = 0;
        let mut chars = line.chars().peekable();
        while let Some(&c) = chars.peek() {
            if c != marker {
                return false;
            }
            chars.next();
            count += 1;
            while chars.peek().is_some_and(|c| c.is_whitespace()) {
                chars.next();
            }
        }
        count >= 3
    })
}

/// `^[-*]\s+(.+)$` and `^\d+\.\s+(.+)$`. Returns the content after the marker.
fn list_item(line: &str) -> Option<(bool, &str)> {
    let ordered = line
        .find('.')
        .filter(|&dot| dot > 0 && line[..dot].chars().all(|c| c.is_ascii_digit()))
        .map(|dot| dot + 1);
    let start = match ordered {
        Some(after) => after,
        None if line.starts_with(['-', '*']) => 1,
        None => return None,
    };
    let rest = &line[start..];
    let content = rest.trim_start_matches(|c: char| c.is_whitespace());
    if content.len() == rest.len() || content.is_empty() {
        return None;
    }
    Some((ordered.is_some(), content))
}

/// Block-level Markdown. Deliberately small — the same subset the C# handles, no more.
pub fn to_html(markdown: &str) -> String {
    let normalized = markdown.replace("\r\n", "\n");
    let nl = newline(); // AppendLine
    let mut html = String::new();
    let mut list_type: Option<&str> = None;
    let mut in_code = false;
    let mut paragraph: Vec<&str> = Vec::new();

    // Closures would need to borrow `html` mutably alongside the loop, so these stay macros.
    macro_rules! flush_paragraph {
        () => {
            if !paragraph.is_empty() {
                html.push_str(&format!("<p>{}</p>{nl}", inline(&paragraph.join(" "))));
                paragraph.clear();
            }
        };
    }
    macro_rules! close_list {
        () => {
            if let Some(kind) = list_type.take() {
                html.push_str(&format!("</{kind}>{nl}"));
            }
        };
    }

    for raw in normalized.split('\n') {
        let line = raw.trim_end();

        if line.starts_with("```") {
            flush_paragraph!();
            close_list!();
            html.push_str(if in_code { "</code></pre>" } else { "<pre><code>" });
            html.push_str(nl);
            in_code = !in_code;
            continue;
        }
        // Inside a fence the *untrimmed* line is kept, so trailing spaces in code survive.
        if in_code {
            html.push_str(&html_encode(raw));
            html.push_str(nl);
            continue;
        }
        if line.is_empty() {
            flush_paragraph!();
            close_list!();
            continue;
        }
        if let Some((level, text)) = heading(line) {
            flush_paragraph!();
            close_list!();
            html.push_str(&format!("<h{level}>{}</h{level}>{nl}", inline(text)));
            continue;
        }
        if is_horizontal_rule(line) {
            flush_paragraph!();
            close_list!();
            html.push_str(&format!("<hr>{nl}"));
            continue;
        }
        if let Some(quoted) = line.strip_prefix("> ") {
            flush_paragraph!();
            close_list!();
            html.push_str(&format!("<blockquote>{}</blockquote>{nl}", inline(quoted)));
            continue;
        }
        if let Some((ordered, content)) = list_item(line) {
            flush_paragraph!();
            let want = if ordered { "ol" } else { "ul" };
            if list_type != Some(want) {
                close_list!();
                html.push_str(&format!("<{want}>{nl}"));
                list_type = Some(want);
            }
            html.push_str(&format!("<li>{}</li>{nl}", inline(content)));
            continue;
        }

        close_list!();
        paragraph.push(line);
    }
    flush_paragraph!();
    close_list!();
    if in_code {
        // An unclosed fence is closed for the caller rather than emitting broken HTML.
        html.push_str(&format!("</code></pre>{nl}"));
    }
    html.trim_end_matches('\n').to_string()
}

/// Inline spans, in the C#'s order: code spans are lifted out first so their contents are not
/// parsed further, then the text is escaped, then links, bold and italic.
fn inline(text: &str) -> String {
    // `` `([^`]+)` `` — the placeholder uses NUL, which cannot appear in the escaped text.
    let mut codes: Vec<String> = Vec::new();
    let mut lifted = String::with_capacity(text.len());
    let chars: Vec<char> = text.chars().collect();
    let mut i = 0;
    while i < chars.len() {
        if chars[i] == '`' {
            // `[^`]+` needs at least one character before the closing backtick.
            if let Some(end) = chars[i + 1..].iter().position(|&c| c == '`').filter(|&e| e > 0) {
                let body: String = chars[i + 1..i + 1 + end].iter().collect();
                codes.push(html_encode(&body));
                lifted.push_str(&format!("\u{0}{}\u{0}", codes.len() - 1));
                i += end + 2;
                continue;
            }
        }
        lifted.push(chars[i]);
        i += 1;
    }

    let mut out = html_encode(&lifted);
    out = links(&out);
    out = emphasis(&out, "**", "__", "strong");
    out = emphasis(&out, "*", "_", "em");

    restore_code_spans(&out, &codes)
}

/// `\[([^\]]+)\]\(([^)]+)\)`. The URL is substituted without escaping, but the whole text was
/// already escaped before this runs, so an `&` in a URL still comes out as `&amp;`.
fn links(text: &str) -> String {
    let chars: Vec<char> = text.chars().collect();
    let mut out = String::with_capacity(text.len());
    let mut i = 0;
    while i < chars.len() {
        if chars[i] == '[' {
            if let Some(label_end) = chars[i + 1..].iter().position(|&c| c == ']').filter(|&e| e > 0)
            {
                let after = i + 1 + label_end + 1;
                if chars.get(after) == Some(&'(') {
                    if let Some(url_end) =
                        chars[after + 1..].iter().position(|&c| c == ')').filter(|&e| e > 0)
                    {
                        let label: String = chars[i + 1..i + 1 + label_end].iter().collect();
                        let url: String =
                            chars[after + 1..after + 1 + url_end].iter().collect();
                        out.push_str(&format!("<a href=\"{url}\">{label}</a>"));
                        i = after + 1 + url_end + 1;
                        continue;
                    }
                }
            }
        }
        out.push(chars[i]);
        i += 1;
    }
    out
}

/// `\*\*([^*]+)\*\*|__([^_]+)__` and its single-marker sibling: two alternatives, whichever
/// matches first at a given position wins, and the body may not contain the marker character.
fn emphasis(text: &str, first: &str, second: &str, tag: &str) -> String {
    let chars: Vec<char> = text.chars().collect();
    let mut out = String::with_capacity(text.len());
    let mut i = 0;
    'outer: while i < chars.len() {
        for marker in [first, second] {
            let width = marker.chars().count();
            let marker_char = marker.chars().next().unwrap();
            if !chars[i..].starts_with(&marker.chars().collect::<Vec<_>>()) {
                continue;
            }
            // `[^*]+`: one or more non-marker characters, then the closing marker.
            let body_start = i + width;
            let mut end = body_start;
            while end < chars.len() && chars[end] != marker_char {
                end += 1;
            }
            let closes = chars[end..].starts_with(&marker.chars().collect::<Vec<_>>());
            if end > body_start && closes {
                let body: String = chars[body_start..end].iter().collect();
                out.push_str(&format!("<{tag}>{body}</{tag}>"));
                i = end + width;
                continue 'outer;
            }
        }
        out.push(chars[i]);
        i += 1;
    }
    out
}

/// `\x00(\d+)\x00` back to `<code>…</code>`.
fn restore_code_spans(text: &str, codes: &[String]) -> String {
    let chars: Vec<char> = text.chars().collect();
    let mut out = String::with_capacity(text.len());
    let mut i = 0;
    while i < chars.len() {
        if chars[i] == '\u{0}' {
            let digits_end = chars[i + 1..]
                .iter()
                .position(|c| !c.is_ascii_digit())
                .map(|p| i + 1 + p);
            if let Some(end) = digits_end.filter(|&e| e > i + 1 && chars[e] == '\u{0}') {
                let index: String = chars[i + 1..end].iter().collect();
                if let Some(code) = index.parse::<usize>().ok().and_then(|n| codes.get(n)) {
                    out.push_str(&format!("<code>{code}</code>"));
                    i = end + 1;
                    continue;
                }
            }
        }
        out.push(chars[i]);
        i += 1;
    }
    out
}

/// Resolves a folder argument the way `Files.Directory_` does: empty means the working directory.
fn folder_argument(input: &str) -> Result<PathBuf, String> {
    let path = input.trim().trim_matches('"');
    let path = if path.is_empty() {
        std::env::current_dir().map_err(|_| i18n::format("Error_NoFolder", &[path]))?
    } else {
        PathBuf::from(path)
    };
    if path.is_dir() {
        Ok(path)
    } else {
        Err(i18n::format("Error_NoFolder", &[&path.to_string_lossy()]))
    }
}

/// The renames a "folder | find | replace" request would make, in enumeration order.
fn rename_plan(folder: &Path, find: &str, replace: &str) -> Vec<(PathBuf, PathBuf)> {
    let Ok(entries) = std::fs::read_dir(folder) else { return Vec::new() };
    let mut plan = Vec::new();
    for entry in entries.flatten() {
        // Directory.GetFiles lists files only.
        if !entry.file_type().is_ok_and(|t| t.is_file()) {
            continue;
        }
        let name = entry.file_name().to_string_lossy().into_owned();
        if !name.contains(find) {
            continue;
        }
        let renamed = name.replace(find, replace);
        if renamed != name && !renamed.is_empty() {
            plan.push((entry.path(), folder.join(renamed)));
        }
    }
    plan
}

/// `folder | find | replace [| apply]`. Previews by default — the GUI runs tools as you type, so
/// a bulk rename must never fire on a keystroke.
pub fn bulk_rename(input: &str) -> Result<String, String> {
    let parts: Vec<&str> = input.split('|').map(str::trim).collect();
    if parts.len() < 3 || parts[1].is_empty() {
        return Err(i18n::get("Error_RenameUsage").to_string());
    }
    let folder = folder_argument(parts[0])?;
    let (find, replace) = (parts[1], parts[2]);
    let apply = parts.len() > 3 && parts[3].eq_ignore_ascii_case("apply");

    let plan = rename_plan(&folder, find, replace);
    if plan.is_empty() {
        return Ok(i18n::format("Rename_NoMatch", &[find]));
    }

    if !apply {
        let mut lines = vec![i18n::format("Rename_Preview", &[&plan.len().to_string()])];
        for (old, new) in &plan {
            lines.push(format!(
                "  {}  →  {}",
                old.file_name().unwrap_or_default().to_string_lossy(),
                new.file_name().unwrap_or_default().to_string_lossy()
            ));
        }
        lines.push(i18n::get("Rename_ApplyHint").to_string());
        return Ok(lines.join("\n"));
    }

    // Refuse the whole batch if any target exists — a partial rename is worse than none.
    for (_, new) in &plan {
        if new.is_file() {
            return Err(i18n::format(
                "Error_FileExists",
                &[&new.file_name().unwrap_or_default().to_string_lossy()],
            ));
        }
    }
    for (old, new) in &plan {
        std::fs::rename(old, new).map_err(|e| e.to_string())?;
    }
    Ok(i18n::format("Rename_Done", &[&plan.len().to_string()]))
}

#[cfg(test)]
mod tests {
    use super::*;

    /// Output goes through AppendLine, so lines are CRLF on Windows. The C# ends with
    /// `TrimEnd('\n')`, which strips the LF of the final CRLF and **leaves the CR** — so the last
    /// line really does end with a stray `\r`. Asserted once below, then trimmed here so the
    /// structural tests stay readable.
    fn lines(markdown: &str) -> Vec<String> {
        to_html(markdown)
            .trim_end_matches('\r')
            .lines()
            .map(str::to_string)
            .collect()
    }

    #[test]
    fn the_trailing_carriage_return_is_kept() {
        // Not a bug to fix here: TrimEnd('\n') on CRLF output is what the C# does.
        assert_eq!(to_html("# T"), format!("<h1>T</h1>{}", newline().trim_end_matches('\n')));
    }

    #[test]
    fn headings_take_one_to_six_hashes() {
        assert_eq!(lines("# Title"), ["<h1>Title</h1>"]);
        assert_eq!(lines("###### Six"), ["<h6>Six</h6>"]);
        // Seven is not a heading, and neither is a hash with no space.
        assert_eq!(lines("####### Seven"), ["<p>####### Seven</p>"]);
        assert_eq!(lines("#NoSpace"), ["<p>#NoSpace</p>"]);
    }

    #[test]
    fn paragraphs_join_on_blank_lines() {
        assert_eq!(lines("one\ntwo\n\nthree"), ["<p>one two</p>", "<p>three</p>"]);
        assert_eq!(to_html(""), "");
        assert_eq!(to_html("   "), "");
    }

    #[test]
    fn lists_open_and_close_and_switch_type() {
        assert_eq!(lines("- a\n- b"), ["<ul>", "<li>a</li>", "<li>b</li>", "</ul>"]);
        assert_eq!(lines("1. a\n2. b"), ["<ol>", "<li>a</li>", "<li>b</li>", "</ol>"]);
        // Switching type closes the old list first.
        assert_eq!(
            lines("- a\n1. b"),
            ["<ul>", "<li>a</li>", "</ul>", "<ol>", "<li>b</li>", "</ol>"]
        );
        // A trailing list is closed at the end of the document.
        assert_eq!(lines("* only"), ["<ul>", "<li>only</li>", "</ul>"]);
    }

    #[test]
    fn horizontal_rules_need_three_markers() {
        for rule in ["---", "***", "___", "- - -", "* * * *"] {
            assert_eq!(lines(rule), ["<hr>"], "{rule}");
        }
        // Two is not a rule, and a mixed run is not either.
        assert_eq!(lines("--"), ["<p>--</p>"]);
        assert_eq!(lines("-*-"), ["<p>-*-</p>"]);
    }

    #[test]
    fn blockquotes_need_the_space() {
        assert_eq!(lines("> quoted"), ["<blockquote>quoted</blockquote>"]);
        assert_eq!(lines(">nospace"), ["<p>&gt;nospace</p>"]);
    }

    /// A fence keeps its content verbatim apart from HTML escaping — no inline parsing inside.
    #[test]
    fn code_fences_escape_and_do_not_parse() {
        assert_eq!(
            lines("```\n**not bold** <b>\n```"),
            ["<pre><code>", "**not bold** &lt;b&gt;", "</code></pre>"]
        );
        // An unclosed fence is still closed.
        assert_eq!(lines("```\nx"), ["<pre><code>", "x", "</code></pre>"]);
    }

    #[test]
    fn inline_spans_nest_in_the_right_order() {
        assert_eq!(lines("**bold**"), ["<p><strong>bold</strong></p>"]);
        assert_eq!(lines("__bold__"), ["<p><strong>bold</strong></p>"]);
        assert_eq!(lines("*italic*"), ["<p><em>italic</em></p>"]);
        assert_eq!(lines("_italic_"), ["<p><em>italic</em></p>"]);
        assert_eq!(lines("`code`"), ["<p><code>code</code></p>"]);
        assert_eq!(
            lines("[text](http://x.com)"),
            ["<p><a href=\"http://x.com\">text</a></p>"]
        );
    }

    /// The whole point of lifting code spans out first: markup inside them stays literal.
    #[test]
    fn code_spans_are_not_parsed_further() {
        assert_eq!(lines("`**x**`"), ["<p><code>**x**</code></p>"]);
        assert_eq!(lines("`<b>`"), ["<p><code>&lt;b&gt;</code></p>"]);
        assert_eq!(lines("`a` and `b`"), ["<p><code>a</code> and <code>b</code></p>"]);
        // An unmatched backtick is literal text.
        assert_eq!(lines("`unclosed"), ["<p>`unclosed</p>"]);
        assert_eq!(lines("``"), ["<p>``</p>"], "an empty code span is not a span");
    }

    #[test]
    fn text_is_escaped_but_link_urls_are_not() {
        assert_eq!(lines("a < b & c"), ["<p>a &lt; b &amp; c</p>"]);
        // The URL is inserted without escaping, but escaping already happened to the whole text
        // before links were parsed — so the href ends up escaped anyway. Verified against the CLI.
        assert_eq!(lines("[x](a&b)"), ["<p><a href=\"a&amp;b\">x</a></p>"]);
    }

    #[test]
    fn rename_previews_by_default_and_reports_no_match() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        let dir = std::env::temp_dir().join(format!("krate-rename-{}", crate::csprng::below(1_000_000)));
        std::fs::create_dir_all(&dir).unwrap();
        std::fs::write(dir.join("draft_a.txt"), b"x").unwrap();
        std::fs::write(dir.join("draft_b.txt"), b"x").unwrap();
        let folder = dir.display().to_string();

        let preview = bulk_rename(&format!("{folder} | draft | final")).unwrap();
        assert!(preview.contains("draft_a.txt"), "{preview}");
        assert!(preview.contains("final_a.txt"), "{preview}");
        assert!(dir.join("draft_a.txt").exists(), "a preview must not rename anything");

        assert!(bulk_rename(&format!("{folder} | nothing | x")).unwrap().contains("nothing"));

        // Applying does the work.
        let done = bulk_rename(&format!("{folder} | draft | final | apply")).unwrap();
        assert!(!done.is_empty());
        assert!(dir.join("final_a.txt").exists(), "{done}");
        assert!(!dir.join("draft_a.txt").exists());

        std::fs::remove_dir_all(&dir).ok();
    }

    #[test]
    fn rename_refuses_the_batch_rather_than_clobbering() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        let dir = std::env::temp_dir().join(format!("krate-rename2-{}", crate::csprng::below(1_000_000)));
        std::fs::create_dir_all(&dir).unwrap();
        std::fs::write(dir.join("a_1.txt"), b"one").unwrap();
        std::fs::write(dir.join("b_1.txt"), b"two").unwrap();
        // Renaming "a_" to "b_" would land on an existing file.
        let err = bulk_rename(&format!("{} | a_ | b_ | apply", dir.display())).unwrap_err();
        assert!(err.contains("b_1.txt"), "{err}");
        assert!(dir.join("a_1.txt").exists(), "nothing may be renamed when the batch is refused");
        assert_eq!(std::fs::read(dir.join("b_1.txt")).unwrap(), b"two", "the target is untouched");

        assert!(bulk_rename("").is_err());
        assert!(bulk_rename("x | y").is_err(), "three fields are required");
        assert!(bulk_rename(&format!("{} |  | x", dir.display())).is_err(), "find may not be empty");
        assert!(bulk_rename("Z:\\nope | a | b").is_err());

        std::fs::remove_dir_all(&dir).ok();
    }
}
