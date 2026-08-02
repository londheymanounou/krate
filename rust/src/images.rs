//! Image dimensions read from magic bytes. Mirrors `Krate.Core.Images.Read`/`Dimensions`.
//!
//! No image library involved: every format here declares its size in the first few bytes, so
//! this is byte parsing rather than decoding. That is also why it ports exactly — an `image`
//! crate would give the same numbers but is a dependency for nothing.

use crate::convert::{ratio_name, reduce_ratio};
use crate::i18n;
use crate::tools::format_decimal;

const PNG_SIGNATURE: [u8; 8] = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

fn be32(b: &[u8]) -> i64 {
    i64::from(u32::from_be_bytes([b[0], b[1], b[2], b[3]]))
}

fn le16(b: &[u8]) -> i64 {
    i64::from(u16::from_le_bytes([b[0], b[1]]))
}

fn le32(b: &[u8]) -> i64 {
    i64::from(i32::from_le_bytes([b[0], b[1], b[2], b[3]]))
}

fn le24(b: &[u8]) -> i64 {
    i64::from(u32::from_le_bytes([b[0], b[1], b[2], 0]))
}

/// Reads (format, width, height) from the magic bytes.
pub fn read(bytes: &[u8], name: &str) -> Result<(&'static str, i64, i64), String> {
    let unknown = || i18n::format("Error_UnknownImage", &[name]);

    // PNG: 8-byte signature, then IHDR with big-endian width/height at offset 16.
    if bytes.len() >= 24 && bytes[..8] == PNG_SIGNATURE {
        return Ok(("PNG", be32(&bytes[16..]), be32(&bytes[20..])));
    }
    // GIF: "GIF87a"/"GIF89a", little-endian width/height at offset 6.
    if bytes.len() >= 10 && &bytes[..3] == b"GIF" {
        return Ok(("GIF", le16(&bytes[6..]), le16(&bytes[8..])));
    }
    // BMP: "BM", signed little-endian at offset 18; a negative height means top-down.
    if bytes.len() >= 26 && bytes[0] == b'B' && bytes[1] == b'M' {
        return Ok(("BMP", le32(&bytes[18..]), le32(&bytes[22..]).abs()));
    }
    // WebP: RIFF container tagged "WEBP".
    if bytes.len() >= 30 && &bytes[..4] == b"RIFF" && &bytes[8..12] == b"WEBP" {
        return webp(bytes);
    }
    // JPEG: FF D8, then walk the segments to the frame header.
    if bytes.len() >= 2 && bytes[0] == 0xFF && bytes[1] == 0xD8 {
        return jpeg(bytes);
    }
    Err(unknown())
}

fn webp(b: &[u8]) -> Result<(&'static str, i64, i64), String> {
    let chunk = &b[12..16];
    if chunk == b"VP8X" {
        // Extended: 24-bit canvas size, stored as value-1.
        return Ok(("WebP", 1 + le24(&b[24..]), 1 + le24(&b[27..])));
    }
    if chunk == b"VP8L" {
        // Lossless: 14 bits each after the 0x2F signature.
        let bits = u32::from_le_bytes([b[21], b[22], b[23], b[24]]);
        return Ok((
            "WebP",
            1 + i64::from(bits & 0x3FFF),
            1 + i64::from((bits >> 14) & 0x3FFF),
        ));
    }
    if chunk == b"VP8 " {
        // Lossy: 14-bit dimensions after the 0x9D012A start code.
        return Ok(("WebP", le16(&b[26..]) & 0x3FFF, le16(&b[28..]) & 0x3FFF));
    }
    Err("WebP".to_string())
}

/// Walks the segment chain to the start-of-frame header, which is the only place the dimensions
/// live. Segments are skipped by their declared length.
fn jpeg(bytes: &[u8]) -> Result<(&'static str, i64, i64), String> {
    let bad = || "JPEG".to_string();
    let mut pos = 2usize;

    while pos + 1 < bytes.len() {
        if bytes[pos] != 0xFF {
            return Err(bad());
        }
        let kind = bytes[pos + 1];
        pos += 2;

        // SOF0-3, 5-7, 9-11, 13-15 carry the dimensions; 0xC4/0xC8/0xCC do not.
        if (0xC0..=0xCF).contains(&kind) && !matches!(kind, 0xC4 | 0xC8 | 0xCC) {
            // length(2) + precision(1), then height then width, both big-endian.
            let at = pos + 3;
            if at + 4 > bytes.len() {
                return Err(bad());
            }
            let height = i64::from(u16::from_be_bytes([bytes[at], bytes[at + 1]]));
            let width = i64::from(u16::from_be_bytes([bytes[at + 2], bytes[at + 3]]));
            return Ok(("JPEG", width, height));
        }
        // Markers that carry no length payload.
        if matches!(kind, 0xD8 | 0xD9) || (0xD0..=0xD7).contains(&kind) {
            continue;
        }
        if pos + 2 > bytes.len() {
            return Err(bad());
        }
        let length = usize::from(u16::from_be_bytes([bytes[pos], bytes[pos + 1]]));
        if length < 2 {
            return Err(bad());
        }
        pos += length;
    }
    Err(bad())
}

/// Format and dimensions of an image, plus megapixels and reduced aspect ratio.
pub fn dimensions(input: &str) -> Result<String, String> {
    let path = input.trim().trim_matches('"');
    let bytes = std::fs::read(path).map_err(|_| i18n::format("Error_NoFile", &[path]))?;
    let name = std::path::Path::new(path)
        .file_name()
        .map(|n| n.to_string_lossy().to_string())
        .unwrap_or_else(|| path.to_string());

    let (format, width, height) = read(&bytes, &name)?;
    let megapixels = width as f64 * height as f64 / 1_000_000.0;
    let (rw, rh) = reduce_ratio(width, height);
    let named = ratio_name(rw, rh);

    let orientation = if width > height {
        "Images_Landscape"
    } else if width < height {
        "Images_Portrait"
    } else {
        "Images_Square"
    };

    Ok([
        format!("{}  {format}", i18n::get("Images_Format")),
        format!("{}  {width} × {height} px", i18n::get("Images_Size")),
        format!("{}  {} MP", i18n::get("Images_Pixels"), format_decimal(megapixels, 2)),
        format!(
            "{}  {rw}:{rh}{}",
            i18n::get("Images_Ratio"),
            named.map(|n| format!(" ({n})")).unwrap_or_default()
        ),
        format!("{}  {}", i18n::get("Images_Orientation"), i18n::get(orientation)),
    ]
    .join("\n"))
}

#[cfg(test)]
mod tests {
    use super::*;

    /// Headers are hand-built, exactly as the C# test suite does — the magic bytes and offsets
    /// are what a real file carries, so no image files or decoder are needed.
    fn png(w: u32, h: u32) -> Vec<u8> {
        let mut b = vec![0u8; 24];
        b[..8].copy_from_slice(&PNG_SIGNATURE);
        b[16..20].copy_from_slice(&w.to_be_bytes());
        b[20..24].copy_from_slice(&h.to_be_bytes());
        b
    }

    fn gif(w: u16, h: u16) -> Vec<u8> {
        let mut b = vec![0u8; 10];
        b[..6].copy_from_slice(b"GIF89a");
        b[6..8].copy_from_slice(&w.to_le_bytes());
        b[8..10].copy_from_slice(&h.to_le_bytes());
        b
    }

    fn bmp(w: i32, h: i32) -> Vec<u8> {
        let mut b = vec![0u8; 26];
        b[0] = b'B';
        b[1] = b'M';
        b[18..22].copy_from_slice(&w.to_le_bytes());
        b[22..26].copy_from_slice(&h.to_le_bytes());
        b
    }

    fn jpeg_bytes(w: u16, h: u16) -> Vec<u8> {
        // SOI, then a dummy APP0 with a length, then SOF0 carrying the dimensions.
        let mut b = vec![0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x04, 0x00, 0x00, 0xFF, 0xC0, 0x00, 0x11, 0x08];
        b.extend_from_slice(&h.to_be_bytes());
        b.extend_from_slice(&w.to_be_bytes());
        b
    }

    #[test]
    fn reads_every_format_from_its_header() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        assert_eq!(read(&png(1920, 1080), "x").unwrap(), ("PNG", 1920, 1080));
        assert_eq!(read(&gif(320, 240), "x").unwrap(), ("GIF", 320, 240));
        assert_eq!(read(&bmp(640, 480), "x").unwrap(), ("BMP", 640, 480));
        assert_eq!(read(&jpeg_bytes(800, 600), "x").unwrap(), ("JPEG", 800, 600));
    }

    /// A BMP stores height negative when the rows run top-down; the magnitude is the height.
    #[test]
    fn bmp_top_down_height_is_absolute() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        assert_eq!(read(&bmp(640, -480), "x").unwrap(), ("BMP", 640, 480));
    }

    /// JPEG dimensions are height-then-width, and the parser must skip other segments to find
    /// the frame header rather than reading the first thing after SOI.
    #[test]
    fn jpeg_walks_past_other_segments() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        let (_, w, h) = read(&jpeg_bytes(1234, 567), "x").unwrap();
        assert_eq!((w, h), (1234, 567), "width and height must not be swapped");
    }

    #[test]
    fn unrecognised_and_truncated_input_is_rejected() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        assert!(read(b"not an image at all, really", "x.txt").is_err());
        assert!(read(&[], "empty").is_err());
        assert!(read(&PNG_SIGNATURE, "truncated.png").is_err(), "signature but no IHDR");
        // A JPEG that never reaches a frame header must fail rather than loop.
        assert!(read(&[0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x04, 0x00, 0x00], "x.jpg").is_err());
    }
}
