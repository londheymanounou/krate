//! Removes metadata from a file. Mirrors `Files.StripMetadata`.
//!
//! **This does it losslessly, which the C# does not.** ImageSharp decodes and re-encodes, so a JPEG
//! comes out recompressed and slightly degraded every time metadata is stripped. Rewriting the
//! container instead — dropping the metadata segments and copying the image data through byte for
//! byte — is both faster and lossless, and needs no image codec at all.
//!
//! That makes the output bytes differ from the C#'s by design. What is compared is the message and
//! the properties that matter: no metadata left, dimensions unchanged, still a valid file.
//!
//! Video and audio go to ffmpeg exactly as the C# does, with the same arguments.

use crate::i18n;
use std::path::{Path, PathBuf};

/// JPEG markers that carry metadata rather than image data.
///
/// APP1 holds EXIF and XMP, APP13 holds IPTC/Photoshop, and COM is a free-text comment. The other
/// APPn segments are left alone: APP0 is JFIF and APP2 can hold the ICC colour profile, which is
/// not metadata in the privacy sense and whose removal would change how the image looks.
fn is_metadata_marker(marker: u8) -> bool {
    matches!(marker, 0xE1 | 0xED | 0xFE)
}

/// Rewrites a JPEG without its metadata segments. `None` if it is not a JPEG.
fn strip_jpeg(bytes: &[u8]) -> Option<Vec<u8>> {
    if bytes.len() < 4 || bytes[0] != 0xFF || bytes[1] != 0xD8 {
        return None;
    }
    let mut out = Vec::with_capacity(bytes.len());
    out.extend_from_slice(&bytes[..2]); // SOI

    let mut i = 2;
    while i + 1 < bytes.len() {
        if bytes[i] != 0xFF {
            // Not at a marker: copy the rest verbatim rather than guessing.
            out.extend_from_slice(&bytes[i..]);
            return Some(out);
        }
        let marker = bytes[i + 1];
        // Start of scan: everything from here is compressed image data.
        if marker == 0xDA {
            out.extend_from_slice(&bytes[i..]);
            return Some(out);
        }
        // Standalone markers carry no length.
        if marker == 0xD8 || marker == 0xD9 || (0xD0..=0xD7).contains(&marker) || marker == 0x01 {
            out.extend_from_slice(&bytes[i..i + 2]);
            i += 2;
            continue;
        }
        if i + 4 > bytes.len() {
            out.extend_from_slice(&bytes[i..]);
            return Some(out);
        }
        let length = ((bytes[i + 2] as usize) << 8) | bytes[i + 3] as usize;
        let end = (i + 2 + length).min(bytes.len());
        if !is_metadata_marker(marker) {
            out.extend_from_slice(&bytes[i..end]);
        }
        i = end;
    }
    Some(out)
}

/// PNG chunks that are safe and desirable to drop: text, EXIF, timestamps.
///
/// Everything else is kept — including `gAMA`, `cHRM` and `iCCP`, which affect rendering.
fn is_metadata_chunk(kind: &[u8]) -> bool {
    matches!(kind, b"tEXt" | b"zTXt" | b"iTXt" | b"eXIf" | b"tIME" | b"dSIG")
}

/// Rewrites a PNG without its metadata chunks. `None` if it is not a PNG.
fn strip_png(bytes: &[u8]) -> Option<Vec<u8>> {
    const SIGNATURE: [u8; 8] = [0x89, b'P', b'N', b'G', 0x0D, 0x0A, 0x1A, 0x0A];
    if bytes.len() < 8 || bytes[..8] != SIGNATURE {
        return None;
    }
    let mut out = Vec::with_capacity(bytes.len());
    out.extend_from_slice(&SIGNATURE);

    let mut i = 8;
    while i + 8 <= bytes.len() {
        let length = u32::from_be_bytes([bytes[i], bytes[i + 1], bytes[i + 2], bytes[i + 3]]) as usize;
        let kind = &bytes[i + 4..i + 8];
        // Length, type, data, CRC.
        let end = i.saturating_add(12).saturating_add(length).min(bytes.len());
        if !is_metadata_chunk(kind) {
            out.extend_from_slice(&bytes[i..end]);
        }
        let done = kind == b"IEND";
        i = end;
        if done {
            break;
        }
    }
    Some(out)
}

/// `-map_metadata -1 -c copy`, the same arguments the C# passes.
fn strip_with_ffmpeg(input: &Path, output: &Path) -> Result<(), String> {
    let ffmpeg = find_ffmpeg().ok_or_else(|| i18n::get("Media_NoFfmpeg").to_string())?;
    let status = std::process::Command::new(ffmpeg)
        .args(["-hide_banner", "-y", "-i"])
        .arg(input)
        .args(["-map_metadata", "-1", "-c", "copy"])
        .arg(output)
        .stdout(std::process::Stdio::null())
        .stderr(std::process::Stdio::null())
        .status()
        .map_err(|_| i18n::get("Media_NoFfmpeg").to_string())?;
    if !status.success() || !output.is_file() {
        return Err(i18n::get("Media_NoOutput").to_string());
    }
    Ok(())
}

/// ffmpeg beside the app first, then whatever is on PATH — the order the C# searches in.
fn find_ffmpeg() -> Option<PathBuf> {
    if let Ok(exe) = std::env::current_exe() {
        if let Some(dir) = exe.parent() {
            let beside = dir.join("ffmpeg.exe");
            if beside.is_file() {
                return Some(beside);
            }
        }
    }
    // Command::new resolves through PATH, so the bare name is enough if it is installed.
    Some(PathBuf::from("ffmpeg"))
}

/// `input | output`.
pub fn strip_metadata(input: &str) -> Result<String, String> {
    let parts: Vec<&str> = input
        .split('|')
        .map(|p| p.trim().trim_matches('"'))
        .filter(|p| !p.is_empty())
        .collect();
    if parts.len() != 2 {
        return Err(i18n::get("Error_StripMetadataUsage").to_string());
    }
    let (source, target) = (Path::new(parts[0]), PathBuf::from(parts[1]));
    if !source.is_file() {
        return Err(i18n::format("Error_NoFile", &[&source.to_string_lossy()]));
    }

    let extension = source
        .extension()
        .unwrap_or_default()
        .to_string_lossy()
        .to_lowercase();
    let is_image = matches!(extension.as_str(), "jpg" | "jpeg" | "png" | "webp");

    if is_image {
        let bytes = std::fs::read(source).map_err(|e| e.to_string())?;
        // WebP metadata lives in RIFF chunks; not handled, so it falls through to ffmpeg, which
        // can rewrite it. Saying so beats silently copying the file unchanged.
        let stripped = strip_jpeg(&bytes).or_else(|| strip_png(&bytes));
        match stripped {
            Some(clean) => {
                std::fs::write(&target, &clean).map_err(|e| e.to_string())?;
                return Ok(i18n::format("ImageMetadata_Success", &[&target.to_string_lossy()]));
            }
            None => strip_with_ffmpeg(source, &target)?,
        }
    } else {
        strip_with_ffmpeg(source, &target)?;
    }
    Ok(i18n::format("ImageMetadata_Success", &[&target.to_string_lossy()]))
}

#[cfg(test)]
mod tests {
    use super::*;

    fn english() -> std::sync::MutexGuard<'static, ()> {
        let guard = crate::i18n::test_lock();
        i18n::set_language("en");
        guard
    }

    fn scratch(tag: &str) -> PathBuf {
        let dir = std::env::temp_dir()
            .join(format!("krate-strip-{tag}-{}", crate::csprng::below(1_000_000)));
        std::fs::create_dir_all(&dir).unwrap();
        dir
    }

    /// A JPEG with an APP1/EXIF block, an APP0/JFIF block, a comment and a scan.
    fn jpeg_with_metadata() -> Vec<u8> {
        let mut jpeg = vec![0xFF, 0xD8];
        // APP0/JFIF — kept.
        jpeg.extend_from_slice(&[0xFF, 0xE0, 0x00, 0x10]);
        jpeg.extend_from_slice(b"JFIF\0");
        jpeg.extend_from_slice(&[0u8; 9]);
        // APP1/EXIF — dropped.
        let exif = b"Exif\0\0MM\0*\0\0\0\x08\0\0";
        jpeg.extend_from_slice(&[0xFF, 0xE1]);
        let length = exif.len() + 2;
        jpeg.push((length >> 8) as u8);
        jpeg.push((length & 0xFF) as u8);
        jpeg.extend_from_slice(exif);
        // COM — dropped. The length counts its own two bytes plus the payload, so 6 bytes of
        // comment means 8; getting this wrong desynchronises every later marker.
        jpeg.extend_from_slice(&[0xFF, 0xFE, 0x00, 0x08]);
        jpeg.extend_from_slice(b"secret");
        // SOF0 — kept, and it carries the dimensions.
        jpeg.extend_from_slice(&[0xFF, 0xC0, 0x00, 0x11, 0x08, 0x00, 0x64, 0x00, 0xC8]);
        jpeg.extend_from_slice(&[0u8; 8]);
        // SOS and image data — kept verbatim.
        jpeg.extend_from_slice(&[0xFF, 0xDA, 0x00, 0x08, 1, 1, 0, 0, 0x3F, 0x00]);
        jpeg.extend_from_slice(&[0x12, 0x34, 0x56, 0x78]);
        jpeg.extend_from_slice(&[0xFF, 0xD9]);
        jpeg
    }

    #[test]
    fn a_jpeg_loses_its_metadata_and_keeps_its_image() {
        let _guard = english();
        let original = jpeg_with_metadata();
        let clean = strip_jpeg(&original).unwrap();

        // The EXIF and the comment are gone.
        assert!(!clean.windows(4).any(|w| w == b"Exif"), "EXIF survived");
        assert!(!clean.windows(6).any(|w| w == b"secret"), "the comment survived");
        // JFIF and the scan data are untouched.
        assert!(clean.windows(4).any(|w| w == b"JFIF"), "JFIF was dropped");
        assert!(clean.windows(4).any(|w| w == [0x12, 0x34, 0x56, 0x78]), "image data was lost");
        // Still a JPEG, and smaller.
        assert_eq!(&clean[..2], &[0xFF, 0xD8]);
        assert!(clean.len() < original.len());

        // The dimensions still read the same, which is what "lossless" has to mean.
        assert_eq!(
            crate::images::read(&clean, "x.jpg").unwrap(),
            crate::images::read(&original, "x.jpg").unwrap()
        );
        // No EXIF tags are left to find. (The fixture's IFD is empty, so `parse` finds none in
        // either; the marker check above is what proves the segment itself is gone.)
        assert!(crate::exif::parse(&clean).is_empty());
    }

    /// Stripping twice must change nothing the second time.
    #[test]
    fn stripping_is_idempotent() {
        let _guard = english();
        let once = strip_jpeg(&jpeg_with_metadata()).unwrap();
        let twice = strip_jpeg(&once).unwrap();
        assert_eq!(once, twice);
    }

    #[test]
    fn a_png_loses_only_its_text_chunks() {
        let _guard = english();
        let mut png: Vec<u8> = vec![0x89, b'P', b'N', b'G', 0x0D, 0x0A, 0x1A, 0x0A];
        let chunk = |kind: &[u8], data: &[u8]| {
            let mut c = Vec::new();
            c.extend_from_slice(&(data.len() as u32).to_be_bytes());
            c.extend_from_slice(kind);
            c.extend_from_slice(data);
            c.extend_from_slice(&[0, 0, 0, 0]); // CRC, unchecked here
            c
        };
        png.extend_from_slice(&chunk(b"IHDR", &[0, 0, 0, 0x20, 0, 0, 0, 0x10, 8, 6, 0, 0, 0]));
        png.extend_from_slice(&chunk(b"tEXt", b"Comment\0secret"));
        png.extend_from_slice(&chunk(b"gAMA", &[0, 0, 0, 45]));
        png.extend_from_slice(&chunk(b"IDAT", &[1, 2, 3, 4]));
        png.extend_from_slice(&chunk(b"IEND", b""));

        let clean = strip_png(&png).unwrap();
        assert!(!clean.windows(6).any(|w| w == b"secret"), "the text chunk survived");
        assert!(clean.windows(4).any(|w| w == b"gAMA"), "gAMA affects rendering and must stay");
        assert!(clean.windows(4).any(|w| w == b"IDAT"), "image data was lost");
        assert!(clean.windows(4).any(|w| w == b"IEND"));
        // Dimensions unchanged.
        assert_eq!(
            crate::images::read(&clean, "x.png").unwrap(),
            crate::images::read(&png, "x.png").unwrap()
        );
    }

    #[test]
    fn the_wrong_shape_of_request_is_refused() {
        let _guard = english();
        let dir = scratch("bad");
        let file = dir.join("a.jpg");
        std::fs::write(&file, jpeg_with_metadata()).unwrap();

        assert!(strip_metadata("").is_err(), "two paths are required");
        assert!(strip_metadata("only.jpg").is_err());
        assert!(strip_metadata("a | b | c").is_err());
        assert!(strip_metadata("Z:\\nope.jpg | out.jpg").is_err());
        std::fs::remove_dir_all(&dir).ok();
    }

    #[test]
    fn the_whole_pipeline_writes_a_clean_file() {
        let _guard = english();
        let dir = scratch("pipe");
        let source = dir.join("photo.jpg");
        let target = dir.join("clean.jpg");
        std::fs::write(&source, jpeg_with_metadata()).unwrap();

        let message = strip_metadata(&format!("{} | {}", source.display(), target.display())).unwrap();
        assert!(message.contains("clean.jpg"), "{message}");
        let clean = std::fs::read(&target).unwrap();
        assert!(!clean.windows(4).any(|w| w == b"Exif"), "EXIF survived the pipeline");
        assert!(crate::exif::parse(&clean).is_empty());
        // The source is left alone.
        let untouched = std::fs::read(&source).unwrap();
        assert!(untouched.windows(4).any(|w| w == b"Exif"), "the source was modified");
        std::fs::remove_dir_all(&dir).ok();
    }
}
