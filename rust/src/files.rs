//! Filesystem tools. Mirrors `Krate.Core.Files`.
//!
//! Deliberately excluded from this module: `FileHash` (`Files.Describe`) formats creation and
//! modification times with `ToString("g", Strings.Culture)`, which is culture-aware date
//! formatting and therefore icu4x work rather than filesystem work.

use crate::hashing;
use crate::i18n;
use crate::physics::{human_size, parse_size};
use std::path::{Path, PathBuf};

fn directory(input: &str) -> Result<PathBuf, String> {
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

fn file(input: &str) -> Result<PathBuf, String> {
    let path = PathBuf::from(input.trim().trim_matches('"'));
    if path.is_file() {
        Ok(path)
    } else {
        Err(i18n::format("Error_NoFile", &[&path.to_string_lossy()]))
    }
}

fn refuse_to_overwrite(path: &Path) -> Result<(), String> {
    if path.exists() {
        return Err(i18n::format("Error_FileExists", &[&path.to_string_lossy()]));
    }
    Ok(())
}

/// Every file beneath a folder, recursively, skipping anything unreadable.
fn enumerate_files(root: &Path) -> Vec<(PathBuf, u64)> {
    let mut out = Vec::new();
    let mut stack = vec![root.to_path_buf()];
    while let Some(dir) = stack.pop() {
        let Ok(entries) = std::fs::read_dir(&dir) else { continue };
        for entry in entries.flatten() {
            let path = entry.path();
            match entry.file_type() {
                Ok(t) if t.is_dir() => stack.push(path),
                Ok(t) if t.is_file() => {
                    if let Ok(meta) = entry.metadata() {
                        out.push((path, meta.len()));
                    }
                }
                _ => {}
            }
        }
    }
    out
}

fn relative(root: &Path, path: &Path) -> String {
    path.strip_prefix(root)
        .unwrap_or(path)
        .to_string_lossy()
        .into_owned()
}

/// Two paths, one per line: are these the same file?
pub fn compare(input: &str) -> Result<String, String> {
    let paths: Vec<&str> = input
        .split('\n')
        .map(str::trim)
        .filter(|p| !p.is_empty())
        .collect();
    if paths.len() < 2 {
        return Err(i18n::get("Error_NeedTwoFiles").to_string());
    }
    let (a, b) = (file(paths[0])?, file(paths[1])?);

    let (size_a, size_b) = (
        std::fs::metadata(&a).map_err(|_| i18n::get("Error_NeedTwoFiles").to_string())?.len(),
        std::fs::metadata(&b).map_err(|_| i18n::get("Error_NeedTwoFiles").to_string())?.len(),
    );
    // Size first: hashing two files of different length to prove they differ is wasted work.
    let identical = size_a == size_b && hashing::sha256_file(&a)? == hashing::sha256_file(&b)?;

    Ok(if identical {
        i18n::get("Files_Identical").to_string()
    } else {
        i18n::format(
            "Files_Different",
            &[&human_size(size_a as i64), &human_size(size_b as i64)],
        )
    })
}

/// Total size of a folder, its largest files, and a breakdown by extension.
pub fn folder_size(input: &str) -> Result<String, String> {
    let root = directory(input)?;
    let mut files = enumerate_files(&root);
    if files.is_empty() {
        return Ok(i18n::get("Files_Empty").to_string());
    }

    let total: u64 = files.iter().map(|(_, size)| *size).sum();
    files.sort_by_key(|(_, size)| std::cmp::Reverse(*size));

    let biggest: Vec<String> = files
        .iter()
        .take(5)
        .map(|(path, size)| format!("  {:>10}  {}", human_size(*size as i64), relative(&root, path)))
        .collect();

    let mut by_extension: std::collections::HashMap<String, (u64, usize)> = std::collections::HashMap::new();
    for (path, size) in &files {
        let ext = path
            .extension()
            .map(|e| format!(".{}", e.to_string_lossy().to_lowercase()))
            .unwrap_or_else(|| "(none)".to_string());
        let entry = by_extension.entry(ext).or_insert((0, 0));
        entry.0 += size;
        entry.1 += 1;
    }
    let mut extensions: Vec<(String, (u64, usize))> = by_extension.into_iter().collect();
    extensions.sort_by_key(|(_, (size, _))| std::cmp::Reverse(*size));

    let by_type: Vec<String> = extensions
        .iter()
        .take(5)
        .map(|(ext, (size, count))| format!("  {:>10}  {ext} ({count})", human_size(*size as i64)))
        .collect();

    let mut lines = vec![i18n::format(
        "Files_Total",
        &[&human_size(total as i64), &files.len().to_string()],
    )];
    lines.push(String::new());
    lines.push(i18n::get("Files_Largest").to_string());
    lines.extend(biggest);
    lines.push(String::new());
    lines.push(i18n::get("Files_ByType").to_string());
    lines.extend(by_type);
    Ok(lines.join("\n"))
}

/// Duplicate files under a folder. Sizes are compared first, so only genuine candidates are ever
/// hashed — that is what makes this usable on a big folder.
pub fn duplicates(input: &str) -> Result<String, String> {
    let root = directory(input)?;
    let files: Vec<(PathBuf, u64)> = enumerate_files(&root).into_iter().filter(|(_, s)| *s > 0).collect();

    let mut by_size: std::collections::HashMap<u64, Vec<PathBuf>> = std::collections::HashMap::new();
    for (path, size) in files {
        by_size.entry(size).or_default().push(path);
    }

    let mut groups: Vec<(u64, Vec<PathBuf>)> = Vec::new();
    for (size, candidates) in by_size {
        if candidates.len() < 2 {
            continue;
        }
        let mut by_hash: std::collections::HashMap<String, Vec<PathBuf>> = std::collections::HashMap::new();
        for path in candidates {
            let Ok(digest) = hashing::sha256_file(&path) else { continue };
            by_hash.entry(digest).or_default().push(path);
        }
        for (_, mut matched) in by_hash {
            if matched.len() > 1 {
                matched.sort();
                groups.push((size, matched));
            }
        }
    }

    if groups.is_empty() {
        return Ok(i18n::get("Files_NoDuplicates").to_string());
    }
    groups.sort_by_key(|(size, _)| std::cmp::Reverse(*size));

    let wasted: u64 = groups.iter().map(|(size, g)| size * (g.len() as u64 - 1)).sum();
    let mut lines = vec![
        i18n::format(
            "Files_DuplicatesFound",
            &[&groups.len().to_string(), &human_size(wasted as i64)],
        ),
        String::new(),
    ];
    for (size, group) in &groups {
        lines.push(format!("{} × {}", human_size(*size as i64), group.len()));
        lines.extend(group.iter().map(|p| format!("  {}", relative(&root, p))));
        lines.push(String::new());
    }
    // Nothing is deleted: this reports, you decide.
    lines.push(i18n::get("Files_DuplicatesReadOnly").to_string());
    Ok(lines.join("\n"))
}

const COPY_BUFFER: usize = 1 << 20; // 1 MB: fast enough, and off the large-object heap in C#

/// "path 10MB" — splits a file into numbered parts beside it.
pub fn split(input: &str) -> Result<String, String> {
    let words: Vec<&str> = input.trim().split([' ', '\n']).filter(|w| !w.is_empty()).collect();
    if words.len() < 2 {
        return Err(i18n::get("Error_SplitUsage").to_string());
    }
    let size = parse_size(words[words.len() - 1])?;
    let path = file(&words[..words.len() - 1].join(" "))?;
    if size < 1024 {
        return Err(i18n::format("Error_BadSize", &[words[words.len() - 1]]));
    }

    let data = std::fs::read(&path).map_err(|e| e.to_string())?;
    let mut written: Vec<String> = Vec::new();
    for (index, chunk) in data.chunks(size as usize).enumerate() {
        let part = PathBuf::from(format!("{}.part{:03}", path.to_string_lossy(), index + 1));
        refuse_to_overwrite(&part)?;
        std::fs::write(&part, chunk).map_err(|e| e.to_string())?;
        written.push(part.file_name().unwrap().to_string_lossy().into_owned());
    }

    let mut lines = vec![i18n::format("Files_SplitDone", &[&written.len().to_string()])];
    lines.extend(written);
    Ok(lines.join("\n"))
}

/// Rejoins `name.part001`, `.part002`… back into `name`.
pub fn join(input: &str) -> Result<String, String> {
    let first = file(input)?;
    let text = first.to_string_lossy().into_owned();
    let cut = text
        .to_lowercase()
        .rfind(".part")
        .ok_or_else(|| i18n::get("Error_JoinUsage").to_string())?;
    let target = PathBuf::from(&text[..cut]);
    refuse_to_overwrite(&target)?;

    let dir = first.parent().unwrap_or(Path::new("."));
    let stem = target.file_name().unwrap().to_string_lossy().to_lowercase();
    let mut parts: Vec<PathBuf> = std::fs::read_dir(dir)
        .map_err(|e| e.to_string())?
        .flatten()
        .map(|e| e.path())
        .filter(|p| {
            p.file_name()
                .map(|n| n.to_string_lossy().to_lowercase())
                .is_some_and(|n| n.starts_with(&format!("{stem}.part")))
        })
        .collect();
    parts.sort_by_key(|p| p.to_string_lossy().to_lowercase());

    let mut out: Vec<u8> = Vec::with_capacity(COPY_BUFFER);
    for part in &parts {
        out.extend(std::fs::read(part).map_err(|e| e.to_string())?);
    }
    std::fs::write(&target, &out).map_err(|e| e.to_string())?;

    Ok(i18n::format(
        "Files_JoinDone",
        &[
            &target.file_name().unwrap().to_string_lossy(),
            &parts.len().to_string(),
            &human_size(out.len() as i64),
        ],
    ))
}

/// "path 100MB" — a file of exactly that size, for testing uploads and quotas.
pub fn test_file(input: &str) -> Result<String, String> {
    let words: Vec<&str> = input.trim().split(' ').filter(|w| !w.is_empty()).collect();
    if words.len() < 2 {
        return Err(i18n::get("Error_TestFileUsage").to_string());
    }
    let size = parse_size(words[words.len() - 1])?;
    let path = PathBuf::from(words[..words.len() - 1].join(" ").trim_matches('"'));
    refuse_to_overwrite(&path)?;

    // set_len leaves a sparse file, matching the C#'s SetLength: instant, correct size reported.
    let handle = std::fs::File::create(&path).map_err(|e| e.to_string())?;
    handle.set_len(size as u64).map_err(|e| e.to_string())?;
    Ok(i18n::format(
        "Files_TestFileDone",
        &[&path.to_string_lossy(), &human_size(size)],
    ))
}

#[cfg(test)]
mod tests {
    use super::*;

    struct Temp(PathBuf);

    impl Temp {
        fn new() -> Self {
            let dir = std::env::temp_dir().join(format!("krate-files-{}", crate::csprng::below(1_000_000)));
            std::fs::create_dir_all(&dir).unwrap();
            Temp(dir)
        }
        fn write(&self, name: &str, contents: &[u8]) -> PathBuf {
            let path = self.0.join(name);
            std::fs::create_dir_all(path.parent().unwrap()).unwrap();
            std::fs::write(&path, contents).unwrap();
            path
        }
    }

    impl Drop for Temp {
        fn drop(&mut self) {
            let _ = std::fs::remove_dir_all(&self.0);
        }
    }

    #[test]
    fn compare_matches_on_content_not_name() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        let t = Temp::new();
        let a = t.write("a.txt", b"hello");
        let b = t.write("b.txt", b"hello");
        let c = t.write("c.txt", b"different content");

        let same = compare(&format!("{}\n{}", a.display(), b.display())).unwrap();
        assert_eq!(same, i18n::get("Files_Identical"));
        let differ = compare(&format!("{}\n{}", a.display(), c.display())).unwrap();
        assert_ne!(differ, i18n::get("Files_Identical"));
        assert!(compare(&a.display().to_string()).is_err(), "needs two files");
    }

    #[test]
    fn folder_size_totals_and_groups_by_extension() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        let t = Temp::new();
        t.write("a.txt", &[0u8; 100]);
        t.write("b.txt", &[0u8; 200]);
        t.write("sub/c.bin", &[0u8; 5000]);

        let out = folder_size(&t.0.display().to_string()).unwrap();
        assert!(out.contains("5.3 KB"), "total of all three files: {out}");
        assert!(out.contains(".bin (1)"), "{out}");
        assert!(out.contains(".txt (2)"), "recurses into subfolders: {out}");
    }

    #[test]
    fn folder_size_reports_an_empty_folder() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        let t = Temp::new();
        assert_eq!(folder_size(&t.0.display().to_string()).unwrap(), i18n::get("Files_Empty"));
        assert!(folder_size("Z:\\definitely\\not\\here").is_err());
    }

    /// Same size but different content must not be reported as duplicates — that is the whole
    /// reason the sizes are only a pre-filter and the hash is authoritative.
    #[test]
    fn duplicates_hashes_only_same_size_candidates() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        let t = Temp::new();
        t.write("one.txt", b"same content here");
        t.write("two.txt", b"same content here");
        t.write("trap.txt", b"same length!!!!!!"); // identical length, different bytes
        t.write("solo.txt", b"unique");

        let out = duplicates(&t.0.display().to_string()).unwrap();
        assert!(out.contains("one.txt"), "{out}");
        assert!(out.contains("two.txt"), "{out}");
        assert!(!out.contains("trap.txt"), "same size is not the same file: {out}");
        assert!(!out.contains("solo.txt"), "{out}");
    }

    #[test]
    fn duplicates_reports_none_when_there_are_none() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        let t = Temp::new();
        t.write("a.txt", b"one");
        t.write("b.txt", b"two");
        assert_eq!(
            duplicates(&t.0.display().to_string()).unwrap(),
            i18n::get("Files_NoDuplicates")
        );
    }

    #[test]
    fn split_then_join_round_trips_the_bytes() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        let t = Temp::new();
        let original: Vec<u8> = (0..5000u32).map(|i| (i % 251) as u8).collect();
        let path = t.write("data.bin", &original);

        let out = split(&format!("{} 2k", path.display())).unwrap();
        assert!(out.contains("data.bin.part001"), "{out}");
        assert!(out.contains("data.bin.part003"), "5000 bytes at 2000 each is three parts: {out}");

        std::fs::remove_file(&path).unwrap(); // free the target name
        let joined = join(&format!("{}.part001", path.display())).unwrap();
        assert!(joined.contains("data.bin"), "{joined}");
        assert_eq!(std::fs::read(&path).unwrap(), original, "byte-for-byte identical");
    }

    #[test]
    fn split_refuses_a_useless_part_size() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        let t = Temp::new();
        let path = t.write("x.bin", &[0u8; 100]);
        assert!(split(&format!("{} 10", path.display())).is_err(), "under 1 KB");
        assert!(split(&path.display().to_string()).is_err(), "no size given");
    }

    #[test]
    fn test_file_creates_exactly_the_requested_size() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        let t = Temp::new();
        let path = t.0.join("filler.bin");
        test_file(&format!("{} 64k", path.display())).unwrap();
        assert_eq!(std::fs::metadata(&path).unwrap().len(), 64_000);
        // Never clobber an existing file.
        assert!(test_file(&format!("{} 1k", path.display())).is_err());
    }
}
