//! Archive creation and extraction. Mirrors `Files.Compress` and `Files.Extract`.
//!
//! | format | create | extract | backend |
//! |--------|--------|---------|---------|
//! | zip    | yes    | yes     | `zip` + `flate2`/miniz_oxide |
//! | tar    | yes    | yes     | `tar` |
//! | tar.gz / tgz / gz | yes | yes | `flate2` |
//! | tar.bz2 / bz2 | yes | yes | `bzip2` on `libbz2-rs-sys` |
//! | 7z     | yes    | yes     | `sevenz-rust2` |
//! | xz     | no (nor in the C#) | yes | `lzma-rs` |
//! | rar    | no (nor in the C#) | **no** | needs the C unrar library |
//!
//! Every backend is pure Rust: this machine has no C compiler, so `bzip2-sys` and `xz2` are out and
//! their pure reimplementations are used instead. Two gaps remain against the C#:
//!
//! * **rar extraction** needs unrar, which is C and licence-encumbered.
//! * **7z passwords**. `sevenz-rust2`'s `aes256` feature pulls in `getrandom`, which cannot link
//!   here (the same `dlltool` failure that made `csprng.rs` necessary), so 7z archives are created
//!   and read unencrypted. The C#'s advanced 7z options — dictionary size, solid, multithread —
//!   are not exposed by the crate either.
//!
//! Compressed *sizes* will not match .NET byte for byte: these are different implementations of the
//! same formats. So parity here is interoperability — each side reads what the other wrote — not
//! string equality of the reported size.

use crate::i18n;
use crate::physics::human_size;
use std::fs::File;
use std::io::{Read, Write};
use std::path::{Path, PathBuf};

fn refuse_to_overwrite(path: &Path) -> Result<(), String> {
    if path.exists() {
        return Err(i18n::format("Error_FileExists", &[&path.to_string_lossy()]));
    }
    Ok(())
}

fn file_name(path: &Path) -> String {
    path.file_name().unwrap_or_default().to_string_lossy().into_owned()
}

/// What `Files.Compress` accepts as a format, and what this build can actually produce.
enum Target {
    Zip,
    Tar,
    TarGz,
    TarBz2,
    SevenZip,
    /// Extractable but not creatable, in the C# too.
    NotCreatable(String),
    Unknown(String),
}

fn target_for(format: &str) -> Target {
    match format {
        "zip" | "" => Target::Zip,
        "tar" => Target::Tar,
        "tgz" | "targz" | "tar.gz" | "gz" => Target::TarGz,
        "tbz2" | "tbz" | "tarbz2" | "tar.bz2" | "bz2" => Target::TarBz2,
        "7z" => Target::SevenZip,
        "rar" | "xz" => Target::NotCreatable(format.to_string()),
        other => Target::Unknown(other.to_string()),
    }
}

/// `path | format`, format defaulting to zip.
pub fn compress(input: &str) -> Result<String, String> {
    match input.split_once('|') {
        Some((path, format)) => compress_to(path, format.trim()),
        None => compress_to(input, "zip"),
    }
}

pub fn compress_to(path_input: &str, format: &str) -> Result<String, String> {
    let trimmed = path_input.trim().trim_matches('"');
    let path = PathBuf::from(trimmed.trim_end_matches(['\\', '/']));
    let is_dir = path.is_dir();
    if !is_dir && !path.is_file() {
        return Err(i18n::format("Error_NoFile", &[&path.to_string_lossy()]));
    }

    // Only the first field is the format; the 7z path in the C# takes more, and this build cannot
    // produce 7z at all.
    let first = format.split('|').next().unwrap_or("").trim().to_lowercase();
    let (extension, target) = match target_for(first.trim_start_matches('.')) {
        Target::Zip => (".zip", Target::Zip),
        Target::Tar => (".tar", Target::Tar),
        Target::TarGz => (".tar.gz", Target::TarGz),
        Target::TarBz2 => (".tar.bz2", Target::TarBz2),
        Target::SevenZip => (".7z", Target::SevenZip),
        Target::NotCreatable(f) => return Err(i18n::format("Error_CannotCreate", &[&f])),
        Target::Unknown(f) => return Err(i18n::format("Error_UnknownFormat", &[&f])),
    };

    let mut out_path = path.clone().into_os_string();
    out_path.push(extension);
    let out_path = PathBuf::from(out_path);
    refuse_to_overwrite(&out_path)?;

    let written = match target {
        Target::Zip => write_zip(&path, &out_path, is_dir),
        Target::Tar => write_tar(&path, &out_path, Compression::None, is_dir),
        Target::TarGz => write_tar(&path, &out_path, Compression::Gzip, is_dir),
        Target::TarBz2 => write_tar(&path, &out_path, Compression::Bzip2, is_dir),
        Target::SevenZip => write_seven_zip(&path, &out_path),
        _ => unreachable!("the other targets returned above"),
    };
    if let Err(e) = written {
        // Never leave a half-written archive to be mistaken for a real one.
        std::fs::remove_file(&out_path).ok();
        return Err(e);
    }

    let size = std::fs::metadata(&out_path).map(|m| m.len()).unwrap_or(0);
    Ok(i18n::format(
        "Archive_Zipped",
        &[&file_name(&out_path), &human_size(size as i64)],
    ))
}

/// Every file under a folder, with the archive-relative path each should carry.
fn entries_under(root: &Path) -> Vec<(PathBuf, String)> {
    let mut found = Vec::new();
    let mut stack = vec![root.to_path_buf()];
    while let Some(dir) = stack.pop() {
        let Ok(read) = std::fs::read_dir(&dir) else { continue };
        let mut children: Vec<PathBuf> = read.flatten().map(|e| e.path()).collect();
        // Sorted so an archive of the same tree is built in the same order twice.
        children.sort();
        for child in children {
            if child.is_dir() {
                stack.push(child);
            } else if child.is_file() {
                // Archive paths use '/', whatever the platform.
                let relative = child
                    .strip_prefix(root)
                    .unwrap_or(&child)
                    .to_string_lossy()
                    .replace('\\', "/");
                found.push((child, relative));
            }
        }
    }
    found.sort_by(|a, b| a.1.cmp(&b.1));
    found
}

fn write_zip(path: &Path, out_path: &Path, is_dir: bool) -> Result<(), String> {
    let file = File::create(out_path).map_err(|e| e.to_string())?;
    let mut zip = zip::ZipWriter::new(file);
    let options: zip::write::FileOptions<'_, ()> =
        zip::write::FileOptions::default().compression_method(zip::CompressionMethod::Deflated);

    let items = if is_dir {
        entries_under(path)
    } else {
        vec![(path.to_path_buf(), file_name(path))]
    };
    for (source, name) in items {
        zip.start_file(name, options).map_err(|e| e.to_string())?;
        let mut reader = File::open(&source).map_err(|e| e.to_string())?;
        std::io::copy(&mut reader, &mut zip).map_err(|e| e.to_string())?;
    }
    zip.finish().map_err(|e| e.to_string())?;
    Ok(())
}

/// Which stream compressor wraps a tar.
#[derive(Clone, Copy, PartialEq, Eq)]
enum Compression {
    None,
    Gzip,
    Bzip2,
}

fn write_tar(path: &Path, out_path: &Path, how: Compression, is_dir: bool) -> Result<(), String> {
    let file = File::create(out_path).map_err(|e| e.to_string())?;
    // The writer is generic over the sink, so the gzip case wraps the same code.
    fn fill<W: Write>(builder: &mut tar::Builder<W>, path: &Path, is_dir: bool) -> Result<(), String> {
        let items = if is_dir {
            entries_under(path)
        } else {
            vec![(path.to_path_buf(), file_name(path))]
        };
        for (source, name) in items {
            let mut reader = File::open(&source).map_err(|e| e.to_string())?;
            builder.append_file(&name, &mut reader).map_err(|e| e.to_string())?;
        }
        Ok(())
    }

    match how {
        Compression::None => {
            let mut builder = tar::Builder::new(file);
            fill(&mut builder, path, is_dir)?;
            builder.finish().map_err(|e| e.to_string())?;
        }
        Compression::Gzip => {
            let encoder = flate2::write::GzEncoder::new(file, flate2::Compression::default());
            let mut builder = tar::Builder::new(encoder);
            fill(&mut builder, path, is_dir)?;
            builder.into_inner().map_err(|e| e.to_string())?.finish().map_err(|e| e.to_string())?;
        }
        Compression::Bzip2 => {
            let encoder = bzip2::write::BzEncoder::new(file, bzip2::Compression::default());
            let mut builder = tar::Builder::new(encoder);
            fill(&mut builder, path, is_dir)?;
            builder.into_inner().map_err(|e| e.to_string())?.finish().map_err(|e| e.to_string())?;
        }
    }
    Ok(())
}

/// 7z, without a password: see the module docs for why encryption is unavailable here.
fn write_seven_zip(path: &Path, out_path: &Path) -> Result<(), String> {
    sevenz_rust2::compress_to_path(path, out_path).map_err(|e| e.to_string())
}

/// Refuses an entry that would escape the destination — the zip-slip guard the C# has on both of
/// its paths, and the reason extraction cannot just trust the archive's own names.
fn safe_target(dest: &Path, name: &str) -> Option<PathBuf> {
    let root = dest.canonicalize().ok()?;
    let mut target = root.clone();
    for part in name.split(['/', '\\']) {
        match part {
            "" | "." => continue,
            ".." => {
                target.pop();
                // Popping out of the destination is exactly what must not be allowed.
                if !target.starts_with(&root) {
                    return None;
                }
            }
            part => target.push(part),
        }
    }
    if target.starts_with(&root) && target != root {
        Some(target)
    } else {
        None
    }
}

fn write_entry(dest: &Path, name: &str, reader: &mut impl Read) -> Result<bool, String> {
    // A name ending in a separator is a directory entry and carries no content.
    if name.ends_with('/') || name.ends_with('\\') {
        return Ok(false);
    }
    let Some(target) = safe_target(dest, name) else {
        // Silently skipping would extract a partial archive; refusing says what happened.
        return Err(i18n::format("Error_NotArchive", &[name]));
    };
    if let Some(parent) = target.parent() {
        std::fs::create_dir_all(parent).map_err(|e| e.to_string())?;
    }
    let mut out = File::create(&target).map_err(|e| e.to_string())?;
    std::io::copy(reader, &mut out).map_err(|e| e.to_string())?;
    Ok(true)
}

/// Extracts into a folder of the same name beside the archive.
pub fn extract(input: &str) -> Result<String, String> {
    let path = PathBuf::from(input.trim().trim_matches('"'));
    if !path.is_file() {
        return Err(i18n::format("Error_NoFile", &[&path.to_string_lossy()]));
    }

    // "x.tar.gz" extracts to "x", not "x.tar".
    let stem = path.file_stem().unwrap_or_default().to_string_lossy().into_owned();
    let stem = if stem.to_lowercase().ends_with(".tar") {
        stem[..stem.len() - 4].to_string()
    } else {
        stem
    };
    let dest = path.parent().unwrap_or(Path::new(".")).join(&stem);
    if dest.exists() {
        return Err(i18n::format("Error_FileExists", &[&dest.to_string_lossy()]));
    }

    let extension = path
        .extension()
        .unwrap_or_default()
        .to_string_lossy()
        .to_lowercase();
    let not_archive = || i18n::format("Error_NotArchive", &[&file_name(&path)]);

    // The destination has to exist before entries can be resolved against it.
    std::fs::create_dir_all(&dest).map_err(|e| e.to_string())?;
    let count = match extension.as_str() {
        "zip" => extract_zip(&path, &dest),
        "tar" => extract_tar_reader(File::open(&path).map_err(|e| e.to_string())?, &dest),
        "gz" | "tgz" => extract_stream(&path, &dest, Compression::Gzip, &stem),
        "bz2" | "tbz2" | "tbz" => extract_stream(&path, &dest, Compression::Bzip2, &stem),
        "xz" => extract_xz(&path, &dest, &stem),
        "7z" => extract_seven_zip(&path, &dest),
        // Only rar is left, and it needs the C unrar library.
        _ => Err(not_archive()),
    };

    match count {
        Ok(count) => Ok(i18n::format(
            "Archive_Extracted",
            &[&file_name(&dest), &count.to_string()],
        )),
        Err(e) => {
            // Leave nothing behind, so retrying after installing the missing piece is possible.
            std::fs::remove_dir_all(&dest).ok();
            Err(e)
        }
    }
}

fn extract_zip(path: &Path, dest: &Path) -> Result<usize, String> {
    let file = File::open(path).map_err(|e| e.to_string())?;
    let mut archive = zip::ZipArchive::new(file)
        .map_err(|_| i18n::format("Error_NotArchive", &[&file_name(path)]))?;
    let mut count = 0;
    for index in 0..archive.len() {
        let mut entry = archive.by_index(index).map_err(|e| e.to_string())?;
        let name = entry.name().to_string();
        if entry.is_dir() {
            continue;
        }
        if write_entry(dest, &name, &mut entry)? {
            count += 1;
        }
    }
    Ok(count)
}

fn extract_tar_reader<R: Read>(reader: R, dest: &Path) -> Result<usize, String> {
    let mut archive = tar::Archive::new(reader);
    let mut count = 0;
    let entries = archive.entries().map_err(|e| e.to_string())?;
    for entry in entries {
        let mut entry = entry.map_err(|e| e.to_string())?;
        let name = entry
            .path()
            .map_err(|e| e.to_string())?
            .to_string_lossy()
            .into_owned();
        if entry.header().entry_type().is_dir() {
            continue;
        }
        if write_entry(dest, &name, &mut entry)? {
            count += 1;
        }
    }
    Ok(count)
}

/// A `.gz` or `.bz2` may hold a tar or a single file — SharpCompress detects which, so this does
/// too: decompress, try to read it as a tar, and fall back to writing one file named after the
/// archive. A plain `file.txt.gz` is common and would otherwise fail outright.
fn extract_stream(path: &Path, dest: &Path, how: Compression, stem: &str) -> Result<usize, String> {
    let file = File::open(path).map_err(|e| e.to_string())?;
    let mut plain = Vec::new();
    match how {
        Compression::Gzip => {
            flate2::read::GzDecoder::new(file)
                .read_to_end(&mut plain)
                .map_err(|_| i18n::format("Error_NotArchive", &[&file_name(path)]))?;
        }
        Compression::Bzip2 => {
            bzip2::read::BzDecoder::new(file)
                .read_to_end(&mut plain)
                .map_err(|_| i18n::format("Error_NotArchive", &[&file_name(path)]))?;
        }
        Compression::None => return Err(i18n::format("Error_NotArchive", &[&file_name(path)])),
    }
    unpack_tar_or_single(plain, dest, stem)
}

fn extract_xz(path: &Path, dest: &Path, stem: &str) -> Result<usize, String> {
    let file = File::open(path).map_err(|e| e.to_string())?;
    let mut reader = std::io::BufReader::new(file);
    let mut plain = Vec::new();
    lzma_rs::xz_decompress(&mut reader, &mut plain)
        .map_err(|_| i18n::format("Error_NotArchive", &[&file_name(path)]))?;
    unpack_tar_or_single(plain, dest, stem)
}

/// Treats decompressed bytes as a tar if they look like one, otherwise as a single file.
fn unpack_tar_or_single(plain: Vec<u8>, dest: &Path, stem: &str) -> Result<usize, String> {
    if looks_like_tar(&plain) {
        return extract_tar_reader(std::io::Cursor::new(plain), dest);
    }
    let name = if stem.is_empty() { "extracted" } else { stem };
    let mut cursor = std::io::Cursor::new(plain);
    if write_entry(dest, name, &mut cursor)? {
        Ok(1)
    } else {
        Ok(0)
    }
}

/// Whether decompressed bytes are a tar, decided by actually reading the first header.
///
/// A magic-bytes check on "ustar" at offset 257 is not enough: SharpCompress's TarWriter emits
/// headers without it, so the C#'s own tar.gz files would be misread as single files. The tar
/// crate validates the header checksum, which is a real test.
fn looks_like_tar(plain: &[u8]) -> bool {
    if plain.len() < 512 {
        return false;
    }
    let mut probe = tar::Archive::new(std::io::Cursor::new(plain));
    match probe.entries() {
        Ok(mut entries) => matches!(entries.next(), Some(Ok(entry)) if entry.path().is_ok()),
        Err(_) => false,
    }
}

fn extract_seven_zip(path: &Path, dest: &Path) -> Result<usize, String> {
    sevenz_rust2::decompress_file(path, dest)
        .map_err(|_| i18n::format("Error_NotArchive", &[&file_name(path)]))?;
    // The crate writes the tree itself, so count what landed rather than tracking entries.
    Ok(count_files(dest))
}

fn count_files(root: &Path) -> usize {
    let mut count = 0;
    let mut stack = vec![root.to_path_buf()];
    while let Some(dir) = stack.pop() {
        let Ok(read) = std::fs::read_dir(&dir) else { continue };
        for entry in read.flatten() {
            let path = entry.path();
            if path.is_dir() {
                stack.push(path);
            } else if path.is_file() {
                count += 1;
            }
        }
    }
    count
}

/// Entries in an archive, for the manager view: name, uncompressed size.
pub fn list(path_input: &str) -> Result<Vec<(String, u64)>, String> {
    let path = PathBuf::from(path_input.trim().trim_matches('"'));
    if !path.is_file() {
        return Err(i18n::format("Error_NoFile", &[&path.to_string_lossy()]));
    }
    let extension = path.extension().unwrap_or_default().to_string_lossy().to_lowercase();
    let not_archive = || i18n::format("Error_NotArchive", &[&file_name(&path)]);
    let file = File::open(&path).map_err(|e| e.to_string())?;

    match extension.as_str() {
        "zip" => {
            let mut archive = zip::ZipArchive::new(file).map_err(|_| not_archive())?;
            let mut out = Vec::new();
            for index in 0..archive.len() {
                let entry = archive.by_index(index).map_err(|e| e.to_string())?;
                if !entry.is_dir() {
                    out.push((entry.name().to_string(), entry.size()));
                }
            }
            Ok(out)
        }
        "tar" | "gz" | "tgz" | "bz2" | "tbz2" | "tbz" | "xz" => {
            fn collect<R: Read>(reader: R) -> Result<Vec<(String, u64)>, String> {
                let mut archive = tar::Archive::new(reader);
                let mut out = Vec::new();
                for entry in archive.entries().map_err(|e| e.to_string())? {
                    let entry = entry.map_err(|e| e.to_string())?;
                    if entry.header().entry_type().is_dir() {
                        continue;
                    }
                    let name = entry.path().map_err(|e| e.to_string())?.to_string_lossy().into_owned();
                    out.push((name, entry.header().size().unwrap_or(0)));
                }
                Ok(out)
            }
            match extension.as_str() {
                "tar" => collect(file),
                "gz" | "tgz" => collect(flate2::read::GzDecoder::new(file)),
                "bz2" | "tbz2" | "tbz" => collect(bzip2::read::BzDecoder::new(file)),
                _ => {
                    let mut plain = Vec::new();
                    let mut reader = std::io::BufReader::new(file);
                    lzma_rs::xz_decompress(&mut reader, &mut plain).map_err(|_| not_archive())?;
                    collect(std::io::Cursor::new(plain))
                }
            }
        }
        "7z" => {
            let reader = sevenz_rust2::ArchiveReader::new(
                std::io::BufReader::new(file),
                sevenz_rust2::Password::empty(),
            )
            .map_err(|_| not_archive())?;
            Ok(reader
                .archive()
                .files
                .iter()
                .filter(|f| f.has_stream)
                .map(|f| (f.name.clone(), f.size))
                .collect())
        }
        _ => Err(not_archive()),
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn scratch(tag: &str) -> PathBuf {
        let dir = std::env::temp_dir()
            .join(format!("krate-arch-{tag}-{}", crate::csprng::below(1_000_000)));
        std::fs::create_dir_all(&dir).unwrap();
        dir
    }

    /// A small tree with a nested folder, so relative entry names are exercised.
    fn make_tree(root: &Path) {
        std::fs::create_dir_all(root.join("sub/deeper")).unwrap();
        std::fs::write(root.join("a.txt"), b"first file").unwrap();
        std::fs::write(root.join("sub/b.txt"), b"second file").unwrap();
        std::fs::write(root.join("sub/deeper/c.bin"), vec![7u8; 5000]).unwrap();
    }

    #[test]
    fn a_single_file_round_trips_through_every_supported_format() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        for (format, extension) in [
            ("zip", ".zip"),
            ("tar", ".tar"),
            ("tgz", ".tar.gz"),
            ("bz2", ".tar.bz2"),
            ("7z", ".7z"),
        ] {
            let dir = scratch("single");
            let file = dir.join("note.txt");
            std::fs::write(&file, b"contents worth keeping").unwrap();

            let message = compress(&format!("{} | {format}", file.display())).unwrap();
            assert!(message.contains(&format!("note.txt{extension}")), "{message}");

            let archive = dir.join(format!("note.txt{extension}"));
            assert!(archive.is_file(), "{format}");
            // The destination is the archive name minus one extension, so "note.txt.zip" gives a
            // folder called "note.txt" — and ".tar.gz" loses both, giving the same name.
            std::fs::remove_file(&file).unwrap();
            let extracted = extract(&archive.display().to_string()).unwrap();
            assert!(extracted.contains('1'), "one file: {extracted}");
            assert_eq!(
                std::fs::read(dir.join("note.txt").join("note.txt")).unwrap(),
                b"contents worth keeping",
                "{format}"
            );
            std::fs::remove_dir_all(&dir).ok();
        }
    }

    #[test]
    fn a_folder_keeps_its_structure() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        for format in ["zip", "tar", "tgz", "bz2", "7z"] {
            let dir = scratch("tree");
            let tree = dir.join("project");
            make_tree(&tree);

            compress(&format!("{} | {format}", tree.display())).unwrap();
            std::fs::remove_dir_all(&tree).unwrap();

            let archive = std::fs::read_dir(&dir)
                .unwrap()
                .flatten()
                .map(|e| e.path())
                .find(|p| p.is_file())
                .expect("an archive was created");
            let message = extract(&archive.display().to_string()).unwrap();
            assert!(message.contains('3'), "three files: {message} ({format})");

            let out = dir.join("project");
            assert_eq!(std::fs::read(out.join("a.txt")).unwrap(), b"first file", "{format}");
            assert_eq!(std::fs::read(out.join("sub/b.txt")).unwrap(), b"second file", "{format}");
            assert_eq!(std::fs::read(out.join("sub/deeper/c.bin")).unwrap().len(), 5000, "{format}");
            std::fs::remove_dir_all(&dir).ok();
        }
    }

    #[test]
    fn listing_reports_names_and_uncompressed_sizes() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        let dir = scratch("list");
        let tree = dir.join("project");
        make_tree(&tree);
        compress(&format!("{} | zip", tree.display())).unwrap();

        let entries = list(&dir.join("project.zip").display().to_string()).unwrap();
        assert_eq!(entries.len(), 3, "{entries:?}");
        let big = entries.iter().find(|(n, _)| n.ends_with("c.bin")).expect("{entries:?}");
        assert_eq!(big.1, 5000, "the size is uncompressed, not the stored size");
        std::fs::remove_dir_all(&dir).ok();
    }

    /// Compression has to actually compress, or the deflate setting is not taking effect.
    #[test]
    fn compressible_content_gets_smaller() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        let dir = scratch("ratio");
        let file = dir.join("repetitive.txt");
        std::fs::write(&file, "the same line over and over\n".repeat(2000)).unwrap();
        let original = std::fs::metadata(&file).unwrap().len();

        compress(&format!("{} | zip", file.display())).unwrap();
        let compressed = std::fs::metadata(dir.join("repetitive.txt.zip")).unwrap().len();
        assert!(compressed < original / 4, "{compressed} vs {original}");
        std::fs::remove_dir_all(&dir).ok();
    }

    #[test]
    fn refusals_match_the_cs_messages() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        let dir = scratch("refuse");
        let file = dir.join("x.txt");
        std::fs::write(&file, b"x").unwrap();
        let target = file.display().to_string();

        // Extractable but not creatable, in the C# too.
        for format in ["rar", "xz"] {
            let err = compress(&format!("{target} | {format}")).unwrap_err();
            assert!(err.contains(format), "{err}");
        }
        // Unknown formats say so.
        assert!(compress(&format!("{target} | nonsense")).unwrap_err().contains("nonsense"));
        // Missing input.
        assert!(compress("Z:\\nope.txt | zip").is_err());
        // Never clobber.
        compress(&format!("{target} | zip")).unwrap();
        assert!(compress(&format!("{target} | zip")).is_err(), "the archive already exists");

        // "x.txt.zip" would extract into a folder called "x.txt", which is the source file — so
        // this is refused rather than overwriting it. The C# computes the same destination.
        assert!(extract(&dir.join("x.txt.zip").display().to_string()).is_err());

        // Extracting twice over the same folder is refused too.
        let pack = dir.join("pack");
        std::fs::create_dir_all(&pack).unwrap();
        std::fs::write(pack.join("inner.txt"), b"inner").unwrap();
        compress(&format!("{} | zip", pack.display())).unwrap();
        std::fs::remove_dir_all(&pack).unwrap();
        let archive = dir.join("pack.zip").display().to_string();
        extract(&archive).unwrap();
        assert!(extract(&archive).is_err(), "the destination folder already exists");

        // Not an archive at all.
        assert!(extract(&target).is_err());
        assert!(extract("Z:\\nope.zip").is_err());
        std::fs::remove_dir_all(&dir).ok();
    }

    /// rar and xz are the only formats that cannot be *created*, and the C# refuses them too.
    #[test]
    fn uncreatable_formats_leave_nothing_behind() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        let dir = scratch("unavail");
        let file = dir.join("y.txt");
        std::fs::write(&file, b"y").unwrap();

        for format in ["rar", "xz", "nonsense"] {
            assert!(compress(&format!("{} | {format}", file.display())).is_err(), "{format}");
        }
        // Only the source file remains.
        let left: Vec<_> = std::fs::read_dir(&dir).unwrap().flatten().map(|e| e.path()).collect();
        assert_eq!(left.len(), 1, "{left:?}");
        std::fs::remove_dir_all(&dir).ok();
    }

    /// A plain `.gz` holds one file, not a tar. SharpCompress detects which and so must this —
    /// a `notes.txt.gz` produced by gzip(1) is a completely ordinary thing to be handed.
    #[test]
    fn a_single_file_stream_is_not_mistaken_for_a_tar() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        let dir = scratch("plaingz");

        // gzip of a lone file, with no tar inside.
        let archive = dir.join("notes.txt.gz");
        {
            let out = File::create(&archive).unwrap();
            let mut encoder = flate2::write::GzEncoder::new(out, flate2::Compression::default());
            encoder.write_all(b"just some notes").unwrap();
            encoder.finish().unwrap();
        }
        let message = extract(&archive.display().to_string()).unwrap();
        assert!(message.contains('1'), "one file: {message}");
        assert_eq!(
            std::fs::read(dir.join("notes.txt").join("notes.txt")).unwrap(),
            b"just some notes"
        );

        // And the same for bz2.
        let bz = dir.join("memo.txt.bz2");
        {
            let out = File::create(&bz).unwrap();
            let mut encoder = bzip2::write::BzEncoder::new(out, bzip2::Compression::default());
            encoder.write_all(b"a memo").unwrap();
            encoder.finish().unwrap();
        }
        extract(&bz.display().to_string()).unwrap();
        assert_eq!(std::fs::read(dir.join("memo.txt").join("memo.txt")).unwrap(), b"a memo");
        std::fs::remove_dir_all(&dir).ok();
    }

    /// xz can be extracted but not created — the same asymmetry the C# has.
    #[test]
    fn xz_extracts_but_cannot_be_created() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        let dir = scratch("xz");
        let file = dir.join("z.txt");
        std::fs::write(&file, b"z").unwrap();
        assert!(compress(&format!("{} | xz", file.display())).is_err());

        // A hand-built xz stream, since nothing here can write one.
        let archive = dir.join("payload.txt.xz");
        {
            let mut compressed = Vec::new();
            lzma_rs::xz_compress(&mut std::io::Cursor::new(b"xz content".to_vec()), &mut compressed)
                .unwrap();
            std::fs::write(&archive, &compressed).unwrap();
        }
        extract(&archive.display().to_string()).unwrap();
        assert_eq!(
            std::fs::read(dir.join("payload.txt").join("payload.txt")).unwrap(),
            b"xz content"
        );
        std::fs::remove_dir_all(&dir).ok();
    }

    /// Zip-slip: an entry naming ../ must not be allowed to write outside the destination.
    #[test]
    fn an_escaping_entry_is_refused() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        let dir = scratch("slip");
        let archive = dir.join("evil.zip");

        // Hand-built, because no compressor would produce this.
        {
            let file = File::create(&archive).unwrap();
            let mut zip = zip::ZipWriter::new(file);
            let options: zip::write::FileOptions<'_, ()> = zip::write::FileOptions::default();
            zip.start_file("../escaped.txt", options).unwrap();
            zip.write_all(b"should never be written").unwrap();
            zip.finish().unwrap();
        }

        assert!(extract(&archive.display().to_string()).is_err(), "the escape must be refused");
        assert!(!dir.join("escaped.txt").exists(), "nothing may be written outside");
        assert!(!dir.join("evil").exists(), "the partial destination is cleaned up");
        std::fs::remove_dir_all(&dir).ok();
    }

    #[test]
    fn safe_target_accepts_the_ordinary_and_rejects_the_rest() {
        let dir = scratch("safe");
        assert!(safe_target(&dir, "file.txt").is_some());
        assert!(safe_target(&dir, "sub/file.txt").is_some());
        assert!(safe_target(&dir, "sub/../file.txt").is_some(), "staying inside is fine");
        assert!(safe_target(&dir, "../escaped").is_none());
        assert!(safe_target(&dir, "sub/../../escaped").is_none());
        assert!(safe_target(&dir, "").is_none());
        std::fs::remove_dir_all(&dir).ok();
    }
}
