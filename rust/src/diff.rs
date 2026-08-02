//! Line diff and directory tree. Mirrors `TextMore.Diff` and `Files.Tree`.

use crate::i18n;
use std::path::Path;

/// Unified-ish line diff of two blocks separated by a `---` line.
///
/// Longest-common-subsequence table, filled from the end, then walked forward — the same
/// construction the C# uses, so the tie-breaking matches. A different-but-valid LCS walk would
/// produce a different (equally correct) diff, which is why this mirrors the original exactly
/// rather than reaching for a diff crate.
pub fn diff(input: &str) -> Result<String, String> {
    let normalized = input.replace("\r\n", "\n");

    // Mirrors `Regex.Split(..., "^---\s*$", Multiline)`: a line that is exactly "---" plus
    // optional trailing whitespace.
    let mut halves: Vec<String> = Vec::new();
    let mut current: Vec<&str> = Vec::new();
    for line in normalized.split('\n') {
        if line.trim_end() == "---" && line.starts_with("---") {
            halves.push(current.join("\n"));
            current.clear();
        } else {
            current.push(line);
        }
    }
    halves.push(current.join("\n"));

    if halves.len() < 2 {
        return Err(i18n::get("Error_DiffUsage").to_string());
    }
    let a: Vec<&str> = halves[0].trim_matches('\n').split('\n').collect();
    let b: Vec<&str> = halves[1].trim_matches('\n').split('\n').collect();

    // lcs[i][j] = length of the longest common subsequence of a[i..] and b[j..].
    let mut lcs = vec![vec![0usize; b.len() + 1]; a.len() + 1];
    for i in (0..a.len()).rev() {
        for j in (0..b.len()).rev() {
            lcs[i][j] = if a[i] == b[j] {
                lcs[i + 1][j + 1] + 1
            } else {
                lcs[i + 1][j].max(lcs[i][j + 1])
            };
        }
    }

    let mut output: Vec<String> = Vec::new();
    let (mut x, mut y) = (0usize, 0usize);
    while x < a.len() || y < b.len() {
        if x < a.len() && y < b.len() && a[x] == b[y] {
            output.push(format!("  {}", a[x]));
            x += 1;
            y += 1;
        } else if y < b.len() && (x == a.len() || lcs[x][y + 1] >= lcs[x + 1][y]) {
            output.push(format!("+ {}", b[y]));
            y += 1;
        } else {
            output.push(format!("- {}", a[x]));
            x += 1;
        }
    }

    let changed = output.iter().any(|l| !l.starts_with(' '));
    Ok(if changed {
        output.join("\n")
    } else {
        i18n::get("Diff_Identical").to_string()
    })
}

const MAX_TREE_ENTRIES: usize = 5000;

/// "path [depth]" — the folder tree as text, ready to paste into a README.
pub fn tree(input: &str) -> Result<String, String> {
    // A trailing number is the depth limit.
    let trimmed = input.trim().trim_matches('"');
    let words: Vec<&str> = trimmed.split(' ').collect();
    let (path, max_depth) = if words.len() > 1 {
        match words[words.len() - 1].parse::<usize>() {
            Ok(n) => (words[..words.len() - 1].join(" "), n),
            Err(_) => (trimmed.to_string(), 3),
        }
    } else {
        (trimmed.to_string(), 3)
    };

    let root = if path.trim().is_empty() {
        std::env::current_dir().map_err(|_| i18n::format("Error_NoFolder", &[&path]))?
    } else {
        std::path::PathBuf::from(path.trim())
    };
    if !root.is_dir() {
        return Err(i18n::format("Error_NoFolder", &[&root.to_string_lossy()]));
    }

    let name = root
        .file_name()
        .map(|n| n.to_string_lossy().into_owned())
        .unwrap_or_else(|| root.to_string_lossy().trim_end_matches(['\\', '/']).to_string());
    let mut lines = vec![format!("{name}/")];
    let truncated = walk(&root, "", 0, max_depth, &mut lines);
    if truncated {
        lines.push(i18n::format("Files_TreeTruncated", &[&MAX_TREE_ENTRIES.to_string()]));
    }
    Ok(lines.join("\n"))
}

fn walk(folder: &Path, prefix: &str, depth: usize, max_depth: usize, lines: &mut Vec<String>) -> bool {
    if depth >= max_depth {
        return false;
    }
    let Ok(entries) = std::fs::read_dir(folder) else {
        lines.push(format!("{prefix}└── {}", i18n::get("Files_AccessDenied")));
        return false;
    };

    let mut folders: Vec<std::path::PathBuf> = Vec::new();
    let mut files: Vec<std::path::PathBuf> = Vec::new();
    for entry in entries.flatten() {
        match entry.file_type() {
            Ok(t) if t.is_dir() => folders.push(entry.path()),
            Ok(t) if t.is_file() => files.push(entry.path()),
            _ => {}
        }
    }
    let by_name = |p: &std::path::PathBuf| {
        p.file_name().map(|n| n.to_string_lossy().to_lowercase()).unwrap_or_default()
    };
    folders.sort_by_key(by_name);
    files.sort_by_key(by_name);

    let total = folders.len() + files.len();
    for (index, path) in folders.iter().chain(files.iter()).enumerate() {
        // Counts emitted lines, header included, exactly as the C# does.
        if lines.len() >= MAX_TREE_ENTRIES {
            return true;
        }
        let last = index == total - 1;
        let branch = if last { "└── " } else { "├── " };
        let is_dir = index < folders.len();
        let name = path.file_name().map(|n| n.to_string_lossy().into_owned()).unwrap_or_default();
        lines.push(format!("{prefix}{branch}{name}{}", if is_dir { "/" } else { "" }));

        if is_dir {
            let child_prefix = format!("{prefix}{}", if last { "    " } else { "│   " });
            if walk(path, &child_prefix, depth + 1, max_depth, lines) {
                return true;
            }
        }
    }
    false
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn diff_marks_additions_removals_and_context() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        let out = diff("a\nb\nc\n---\na\nx\nc").unwrap();
        let lines: Vec<&str> = out.lines().collect();
        assert_eq!(lines[0], "  a", "unchanged lines are prefixed with two spaces");
        assert!(lines.contains(&"- b"), "{out}");
        assert!(lines.contains(&"+ x"), "{out}");
        assert_eq!(lines[lines.len() - 1], "  c");
    }

    #[test]
    fn diff_reports_identical_input_rather_than_a_wall_of_context() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        assert_eq!(diff("a\nb\n---\na\nb").unwrap(), i18n::get("Diff_Identical"));
        assert!(diff("no separator here").is_err());
    }

    #[test]
    fn diff_handles_pure_insertion_and_deletion() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        let added = diff("a\n---\na\nb").unwrap();
        assert!(added.contains("+ b"), "{added}");
        let removed = diff("a\nb\n---\na").unwrap();
        assert!(removed.contains("- b"), "{removed}");
    }

    #[test]
    fn tree_lists_folders_before_files_and_honours_depth() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        let root = std::env::temp_dir().join(format!("krate-tree-{}", crate::csprng::below(1_000_000)));
        std::fs::create_dir_all(root.join("zsub/deeper")).unwrap();
        std::fs::write(root.join("a.txt"), b"x").unwrap();
        std::fs::write(root.join("zsub/inner.txt"), b"x").unwrap();

        let out = tree(&root.display().to_string()).unwrap();
        let folder_at = out.find("zsub/").unwrap();
        let file_at = out.find("a.txt").unwrap();
        assert!(folder_at < file_at, "folders come first:\n{out}");
        assert!(out.contains("inner.txt"), "recurses by default:\n{out}");

        // Depth 1 shows the immediate children only.
        let shallow = tree(&format!("{} 1", root.display())).unwrap();
        assert!(shallow.contains("zsub/"), "{shallow}");
        assert!(!shallow.contains("inner.txt"), "depth 1 must not descend:\n{shallow}");

        assert!(tree("Z:\\definitely\\not\\here").is_err());
        std::fs::remove_dir_all(&root).ok();
    }
}
