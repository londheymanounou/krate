//! Markdown helpers. Mirrors the `Toc` and `MarkdownTable` parts of `Krate.Core.TextMore`.

use crate::i18n;
use crate::text::deaccent;
use std::collections::HashMap;

/// GitHub anchor: lower-cased, spaces to hyphens, punctuation dropped, duplicates suffixed
/// `-1`, `-2` and so on.
fn anchor(title: &str, seen: &mut HashMap<String, usize>) -> String {
    let slug: String = deaccent(title)
        .to_lowercase()
        .chars()
        .filter(|c| c.is_alphanumeric() || *c == ' ' || *c == '-')
        .map(|c| if c == ' ' { '-' } else { c })
        .collect();

    match seen.get_mut(&slug) {
        Some(count) => {
            *count += 1;
            format!("{slug}-{count}")
        }
        None => {
            seen.insert(slug.clone(), 0);
            slug
        }
    }
}

/// Table of contents from Markdown headings, indented by level.
pub fn toc(markdown: &str) -> String {
    let normalized = markdown.replace("\r\n", "\n");
    let mut items: Vec<String> = Vec::new();
    let mut seen: HashMap<String, usize> = HashMap::new();
    let mut in_code = false;

    for raw in normalized.split('\n') {
        let line = raw.trim_end();
        if line.starts_with("```") {
            in_code = !in_code; // a '#' inside a fenced block is not a heading
            continue;
        }
        if in_code {
            continue;
        }

        // Mirrors `^(#{1,6})\s+(.+)$`.
        let hashes = line.chars().take_while(|c| *c == '#').count();
        if !(1..=6).contains(&hashes) {
            continue;
        }
        let rest = &line[hashes..];
        if !rest.starts_with(char::is_whitespace) {
            continue;
        }
        let title = rest.trim();
        if title.is_empty() {
            continue;
        }

        let link = anchor(title, &mut seen);
        items.push(format!("{}- [{title}](#{link})", " ".repeat((hashes - 1) * 2)));
    }

    if items.is_empty() {
        i18n::get("Text_NoHeadings").to_string()
    } else {
        items.join("\n")
    }
}

/// Turns CSV or TSV rows (first row is the header) into an aligned Markdown table.
pub fn markdown_table(input: &str) -> Result<String, String> {
    let normalized = input.replace("\r\n", "\n");
    let rows: Vec<&str> = normalized.split('\n').filter(|r| !r.is_empty()).collect();
    if rows.is_empty() {
        return Err(i18n::get("Error_NeedText").to_string());
    }

    // Tab wins if present, since values often contain commas.
    let delimiter = if rows[0].contains('\t') { '\t' } else { ',' };
    let cells: Vec<Vec<&str>> = rows
        .iter()
        .map(|r| r.split(delimiter).map(str::trim).collect())
        .collect();
    let columns = cells.iter().map(Vec::len).max().unwrap_or(0);

    // Width is in chars, matching C#'s string.Length for the ASCII-ish input this handles.
    let mut width = vec![3usize; columns]; // "---" needs room
    for row in &cells {
        for (i, cell) in row.iter().enumerate() {
            width[i] = width[i].max(cell.chars().count());
        }
    }

    let line = |row: &Vec<&str>| {
        let padded: Vec<String> = (0..columns)
            .map(|i| {
                let text = row.get(i).copied().unwrap_or("");
                format!("{text}{}", " ".repeat(width[i].saturating_sub(text.chars().count())))
            })
            .collect();
        format!("| {} |", padded.join(" | "))
    };

    let mut out = vec![
        line(&cells[0]),
        format!("| {} |", width.iter().map(|w| "-".repeat(*w)).collect::<Vec<_>>().join(" | ")),
    ];
    out.extend(cells[1..].iter().map(line));
    Ok(out.join("\n"))
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn toc_indents_by_heading_level() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        let result = toc("# One\n## Two\n### Three");
        assert_eq!(
            result,
            "- [One](#one)\n  - [Two](#two)\n    - [Three](#three)"
        );
    }

    #[test]
    fn toc_ignores_hashes_inside_code_fences() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        let result = toc("# Real\n```\n# Not a heading\n```\n## Also real");
        assert!(result.contains("[Real]"), "{result}");
        assert!(result.contains("[Also real]"), "{result}");
        assert!(!result.contains("Not a heading"), "{result}");
    }

    #[test]
    fn toc_suffixes_duplicate_anchors() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        let result = toc("# Same\n# Same\n# Same");
        assert!(result.contains("(#same)"), "{result}");
        assert!(result.contains("(#same-1)"), "{result}");
        assert!(result.contains("(#same-2)"), "{result}");
    }

    #[test]
    fn toc_anchors_drop_punctuation_and_accents() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        assert!(toc("# Crème, brûlée!").contains("(#creme-brulee)"));
        // Not a heading: needs a space after the hashes, and at most six.
        assert_eq!(toc("#NoSpace"), i18n::get("Text_NoHeadings"));
        assert_eq!(toc("####### Seven"), i18n::get("Text_NoHeadings"));
        assert_eq!(toc("plain text"), i18n::get("Text_NoHeadings"));
    }

    #[test]
    fn markdown_table_aligns_columns() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        let result = markdown_table("a,bb\n1,2").unwrap();
        let lines: Vec<&str> = result.lines().collect();
        assert_eq!(lines[0], "| a   | bb  |", "{result}");
        assert_eq!(lines[1], "| --- | --- |", "columns are at least three wide");
        assert_eq!(lines[2], "| 1   | 2   |");
    }

    #[test]
    fn markdown_table_prefers_tabs_and_pads_ragged_rows() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        // A tab wins over the comma, so "a,b" stays one cell.
        let tabbed = markdown_table("a,b\tc\n1\t2").unwrap();
        assert!(tabbed.lines().next().unwrap().starts_with("| a,b "), "{tabbed}");
        // A short row is padded out to the full column count.
        let ragged = markdown_table("a,b,c\n1").unwrap();
        assert_eq!(ragged.lines().last().unwrap(), "| 1   |     |     |");
        assert!(markdown_table("").is_err());
    }
}
