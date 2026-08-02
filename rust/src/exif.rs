//! Reads the common EXIF tags out of a JPEG. Mirrors `Krate.Core.Exif`.
//!
//! Hand-rolled on both sides — the APP1 segment, the TIFF header, the IFD entries and the Exif
//! sub-IFD are all parsed directly, with no imaging library. That makes this a straight port, and
//! the interesting part is faithfulness to the byte layout rather than to any API.
//!
//! The labels and the orientation names are hardcoded English in the C# too, not resource keys.

use crate::i18n;

/// Tag values in the order the C# prints them; only the ones present are shown.
const ORDER: [&str; 12] = [
    "Make", "Model", "LensModel", "Software", "Size", "DateTimeOriginal", "DateTime",
    "Orientation", "ExposureTime", "FNumber", "ISO", "FocalLength",
];

fn label(key: &str) -> &'static str {
    match key {
        "Make" => "Camera",
        "Model" => "Model",
        "LensModel" => "Lens",
        "Software" => "Software",
        "Size" => "Dimensions",
        "DateTimeOriginal" => "Taken",
        "DateTime" => "Modified",
        "Orientation" => "Orientation",
        "ExposureTime" => "Shutter",
        "FNumber" => "Aperture",
        "ISO" => "ISO",
        "FocalLength" => "Focal length",
        _ => "",
    }
}

fn tag_name(tag: u16) -> Option<&'static str> {
    Some(match tag {
        0x010F => "Make",
        0x0110 => "Model",
        0x0131 => "Software",
        0x0132 => "DateTime",
        0x0112 => "Orientation",
        0x829A => "ExposureTime",
        0x829D => "FNumber",
        0x8827 => "ISO",
        0x920A => "FocalLength",
        0x9003 => "DateTimeOriginal",
        0xA434 => "LensModel",
        _ => return None,
    })
}

fn orientation(value: u16) -> String {
    match value {
        1 => "Normal".to_string(),
        3 => "Rotated 180\u{b0}".to_string(),
        6 => "Rotated 90\u{b0} CW".to_string(),
        8 => "Rotated 90\u{b0} CCW".to_string(),
        2 => "Mirrored".to_string(),
        other => other.to_string(),
    }
}

/// Endian-aware readers. Out-of-range reads return 0 rather than panicking: EXIF in the wild is
/// frequently truncated, and the C# would simply read whatever it found.
fn u16_at(bytes: &[u8], at: usize, little: bool) -> u16 {
    if at + 2 > bytes.len() {
        return 0;
    }
    let pair = [bytes[at], bytes[at + 1]];
    if little { u16::from_le_bytes(pair) } else { u16::from_be_bytes(pair) }
}

fn u32_at(bytes: &[u8], at: usize, little: bool) -> u32 {
    if at + 4 > bytes.len() {
        return 0;
    }
    let quad = [bytes[at], bytes[at + 1], bytes[at + 2], bytes[at + 3]];
    if little { u32::from_le_bytes(quad) } else { u32::from_be_bytes(quad) }
}

/// The EXIF tags in a JPEG, as a name → value list in discovery order.
///
/// A `Vec` rather than a map because the C# uses a `Dictionary` whose only ordering that matters is
/// "last write wins" — and `ORDER` decides the output sequence anyway.
pub fn parse(bytes: &[u8]) -> Vec<(String, String)> {
    let mut found: Vec<(String, String)> = Vec::new();
    // Not a JPEG.
    if bytes.len() < 4 || bytes[0] != 0xFF || bytes[1] != 0xD8 {
        return found;
    }

    // Walk the segment chain looking for APP1 with an "Exif" identifier.
    let mut i = 2usize;
    while i + 4 <= bytes.len() && bytes[i] == 0xFF {
        let marker = bytes[i + 1];
        // Start of scan, or end of image: the metadata is all behind us.
        if marker == 0xDA || marker == 0xD9 {
            break;
        }
        let length = ((bytes[i + 2] as usize) << 8) | bytes[i + 3] as usize;
        if marker == 0xE1 && i + 10 <= bytes.len() && &bytes[i + 4..i + 8] == b"Exif" {
            parse_tiff(bytes, i + 10, &mut found); // past "Exif\0\0"
            break;
        }
        i += 2 + length;
    }
    found
}

fn parse_tiff(bytes: &[u8], tiff: usize, found: &mut Vec<(String, String)>) {
    if tiff + 8 > bytes.len() {
        return;
    }
    // 'I' for Intel (little-endian), 'M' for Motorola.
    let little = bytes[tiff] == b'I';
    let ifd0 = u32_at(bytes, tiff + 4, little) as usize;
    read_ifd(bytes, tiff, ifd0, little, found, true);
}

fn read_ifd(
    bytes: &[u8],
    tiff: usize,
    ifd: usize,
    little: bool,
    found: &mut Vec<(String, String)>,
    follow_sub_ifd: bool,
) {
    if ifd == 0 || tiff + ifd + 2 > bytes.len() {
        return;
    }
    let count = u16_at(bytes, tiff + ifd, little);
    let mut pos = tiff + ifd + 2;
    for _ in 0..count {
        if pos + 12 > bytes.len() {
            break;
        }
        let tag = u16_at(bytes, pos, little);
        let field_type = u16_at(bytes, pos + 2, little);
        let num = u32_at(bytes, pos + 4, little);
        // Bytes per component, by TIFF type.
        let unit: u32 = match field_type {
            1 | 2 | 7 => 1,
            3 => 2,
            4 => 4,
            5 | 10 => 8,
            _ => 1,
        };
        let size = num.saturating_mul(unit);
        // Four bytes or fewer live in the entry itself; anything longer is an offset.
        let value_pos = if size <= 4 {
            pos + 8
        } else {
            tiff + u32_at(bytes, pos + 8, little) as usize
        };
        if value_pos > bytes.len() {
            pos += 12;
            continue;
        }

        // The Exif sub-IFD holds exposure, lens and date tags. Followed once, never recursively.
        if tag == 0x8769 && follow_sub_ifd {
            let sub = u32_at(bytes, pos + 8, little) as usize;
            read_ifd(bytes, tiff, sub, little, found, false);
            pos += 12;
            continue;
        }

        if let Some(name) = tag_name(tag) {
            if let Some(value) = format_value(bytes, value_pos, field_type, num, little, tag) {
                // Dictionary assignment: a repeated tag overwrites.
                match found.iter_mut().find(|(existing, _)| existing == name) {
                    Some(slot) => slot.1 = value,
                    None => found.push((name.to_string(), value)),
                }
            }
        }
        pos += 12;
    }
}

/// `{value:0.#}` and `{value:0.##}` — at most one or two decimals, trailing zeros dropped.
///
/// .NET's custom numeric formats round half away from zero, unlike `Math.Round`, which is why this
/// does not use `round_half_even`.
fn trimmed(value: f64, decimals: u32) -> String {
    let scale = 10f64.powi(decimals as i32);
    let rounded = (value.abs() * scale).round() / scale * value.signum();
    let mut text = format!("{rounded:.*}", decimals as usize);
    if text.contains('.') {
        text = text.trim_end_matches('0').trim_end_matches('.').to_string();
    }
    text
}

/// `{value:0}` — no decimals, rounded away from zero.
fn whole(value: f64) -> String {
    format!("{:.0}", value.abs().round() * value.signum())
}

fn format_value(
    bytes: &[u8],
    pos: usize,
    field_type: u16,
    num: u32,
    little: bool,
    tag: u16,
) -> Option<String> {
    match field_type {
        // ASCII, NUL-terminated within its declared length.
        2 => {
            let mut end = pos;
            let limit = (pos + num as usize).min(bytes.len());
            while end < limit && bytes[end] != 0 {
                end += 1;
            }
            let text: String = bytes[pos..end].iter().map(|b| *b as char).collect();
            let text = text.trim().to_string();
            if text.is_empty() { None } else { Some(text) }
        }
        3 => {
            let short = u16_at(bytes, pos, little);
            Some(if tag == 0x0112 { orientation(short) } else { short.to_string() })
        }
        4 => Some(u32_at(bytes, pos, little).to_string()),
        // RATIONAL: numerator then denominator.
        5 => {
            if pos + 8 > bytes.len() {
                return None;
            }
            let n = u32_at(bytes, pos, little) as f64;
            let d = u32_at(bytes, pos + 4, little) as f64;
            if d == 0.0 {
                return None;
            }
            Some(match tag {
                // Shutter speeds below a second read as a fraction, as photographers write them.
                0x829A => {
                    if n / d < 1.0 {
                        format!("1/{}s", whole(d / n))
                    } else {
                        format!("{}s", trimmed(n / d, 1))
                    }
                }
                0x829D => format!("f/{}", trimmed(n / d, 1)),
                0x920A => format!("{} mm", trimmed(n / d, 1)),
                _ => trimmed(n / d, 2),
            })
        }
        _ => None,
    }
}

pub fn read(input: &str) -> Result<String, String> {
    let path = std::path::PathBuf::from(input.trim().trim_matches('"'));
    if !path.is_file() {
        return Err(i18n::format("Error_NoFile", &[&path.to_string_lossy()]));
    }
    let bytes = std::fs::read(&path).map_err(|e| e.to_string())?;
    let mut tags = parse(&bytes);

    // Dimensions come from the header reader, which works for every format it knows — so a JPEG
    // with no EXIF still reports its size. Added before the emptiness check, deliberately.
    let name = path.file_name().unwrap_or_default().to_string_lossy().into_owned();
    if let Ok((_, width, height)) = crate::images::read(&bytes, &name) {
        let size = format!("{width} \u{d7} {height} px");
        match tags.iter_mut().find(|(key, _)| key == "Size") {
            Some(slot) => slot.1 = size,
            None => tags.push(("Size".to_string(), size)),
        }
    }

    if tags.is_empty() {
        return Ok(i18n::get("Exif_None").to_string());
    }
    Ok(ORDER
        .iter()
        .filter_map(|key| {
            tags.iter()
                .find(|(name, _)| name == key)
                .map(|(_, value)| format!("{:<14} {value}", label(key)))
        })
        .collect::<Vec<_>>()
        .join("\n"))
}

#[cfg(test)]
mod tests {
    use super::*;

    fn english() -> std::sync::MutexGuard<'static, ()> {
        let guard = crate::i18n::test_lock();
        i18n::set_language("en");
        guard
    }

    /// Builds a JPEG with an APP1/EXIF block holding the given entries, so no binary fixture is
    /// needed. Each entry is (tag, type, count, four value bytes or an offset).
    fn jpeg_with_exif(little: bool, entries: &[(u16, u16, u32, [u8; 4])], trailer: &[u8]) -> Vec<u8> {
        let u16b = |v: u16| if little { v.to_le_bytes() } else { v.to_be_bytes() };
        let u32b = |v: u32| if little { v.to_le_bytes() } else { v.to_be_bytes() };

        let mut tiff = Vec::new();
        tiff.extend_from_slice(if little { b"II" } else { b"MM" });
        tiff.extend_from_slice(&u16b(42));
        tiff.extend_from_slice(&u32b(8)); // IFD0 starts right after the header
        tiff.extend_from_slice(&u16b(entries.len() as u16));
        for (tag, field_type, count, value) in entries {
            tiff.extend_from_slice(&u16b(*tag));
            tiff.extend_from_slice(&u16b(*field_type));
            tiff.extend_from_slice(&u32b(*count));
            tiff.extend_from_slice(value);
        }
        tiff.extend_from_slice(&u32b(0)); // no next IFD
        tiff.extend_from_slice(trailer); // out-of-line values

        let mut app1 = Vec::new();
        app1.extend_from_slice(b"Exif\0\0");
        app1.extend_from_slice(&tiff);

        let mut jpeg = vec![0xFF, 0xD8];
        jpeg.push(0xFF);
        jpeg.push(0xE1);
        let length = app1.len() + 2;
        jpeg.push((length >> 8) as u8);
        jpeg.push((length & 0xFF) as u8);
        jpeg.extend_from_slice(&app1);
        // A minimal SOF0 so the dimension reader finds a size, then end of image.
        jpeg.extend_from_slice(&[0xFF, 0xC0, 0x00, 0x11, 0x08, 0x00, 0x64, 0x00, 0xC8]);
        jpeg.extend_from_slice(&[0u8; 8]);
        jpeg.extend_from_slice(&[0xFF, 0xD9]);
        jpeg
    }

    /// The offset an out-of-line value sits at, measured from the TIFF header.
    fn trailer_offset(entries: usize) -> u32 {
        (8 + 2 + entries * 12 + 4) as u32
    }

    #[test]
    fn ascii_tags_are_read_in_both_byte_orders() {
        let _guard = english();
        for little in [true, false] {
            let offset = trailer_offset(1);
            let bytes = if little { offset.to_le_bytes() } else { offset.to_be_bytes() };
            // Make = "Canon", 6 bytes including the NUL, so it is stored out of line.
            let jpeg = jpeg_with_exif(little, &[(0x010F, 2, 6, bytes)], b"Canon\0");
            let tags = parse(&jpeg);
            assert_eq!(
                tags.iter().find(|(k, _)| k == "Make").map(|(_, v)| v.as_str()),
                Some("Canon"),
                "little={little}"
            );
        }
    }

    #[test]
    fn short_values_live_inside_the_entry() {
        let _guard = english();
        // Orientation is a SHORT, so its value sits in the first two bytes of the value field.
        for (raw, expected) in [(1u16, "Normal"), (3, "Rotated 180\u{b0}"), (6, "Rotated 90\u{b0} CW"),
                                (8, "Rotated 90\u{b0} CCW"), (2, "Mirrored"), (9, "9")] {
            let mut value = [0u8; 4];
            value[..2].copy_from_slice(&raw.to_le_bytes());
            let jpeg = jpeg_with_exif(true, &[(0x0112, 3, 1, value)], b"");
            let tags = parse(&jpeg);
            assert_eq!(
                tags.iter().find(|(k, _)| k == "Orientation").map(|(_, v)| v.as_str()),
                Some(expected),
                "orientation {raw}"
            );
        }
    }

    /// Rationals carry the photographer-facing formatting, which is the fiddly part.
    #[test]
    fn rationals_are_formatted_the_way_photographers_read_them() {
        let _guard = english();
        let cases: [(u16, u32, u32, &str); 7] = [
            (0x829A, 1, 250, "1/250s"),     // shutter under a second
            (0x829A, 1, 60, "1/60s"),
            (0x829A, 2, 1, "2s"),           // over a second
            (0x829A, 5, 2, "2.5s"),
            (0x829D, 28, 10, "f/2.8"),      // aperture
            (0x829D, 4, 1, "f/4"),
            (0x920A, 500, 10, "50 mm"),     // focal length
        ];
        for (tag, numerator, denominator, expected) in cases {
            let offset = trailer_offset(1);
            let mut trailer = Vec::new();
            trailer.extend_from_slice(&numerator.to_le_bytes());
            trailer.extend_from_slice(&denominator.to_le_bytes());
            let jpeg = jpeg_with_exif(true, &[(tag, 5, 1, offset.to_le_bytes())], &trailer);
            let tags = parse(&jpeg);
            let value = tags.first().map(|(_, v)| v.as_str());
            assert_eq!(value, Some(expected), "tag {tag:#x} {numerator}/{denominator}");
        }
    }

    /// A zero denominator is not a number; the C# drops the tag rather than dividing.
    #[test]
    fn a_zero_denominator_drops_the_tag() {
        let _guard = english();
        let offset = trailer_offset(1);
        let mut trailer = Vec::new();
        trailer.extend_from_slice(&1u32.to_le_bytes());
        trailer.extend_from_slice(&0u32.to_le_bytes());
        let jpeg = jpeg_with_exif(true, &[(0x829D, 5, 1, offset.to_le_bytes())], &trailer);
        assert!(parse(&jpeg).is_empty());
    }

    #[test]
    fn unknown_tags_and_types_are_ignored() {
        let _guard = english();
        // 0x1234 is not a tag the tool reports.
        let jpeg = jpeg_with_exif(true, &[(0x1234, 3, 1, [1, 0, 0, 0])], b"");
        assert!(parse(&jpeg).is_empty());
        // An unsupported field type yields nothing even for a known tag.
        let jpeg = jpeg_with_exif(true, &[(0x010F, 9, 1, [1, 0, 0, 0])], b"");
        assert!(parse(&jpeg).is_empty());
    }

    #[test]
    fn a_file_that_is_not_a_jpeg_has_no_tags() {
        let _guard = english();
        assert!(parse(b"").is_empty());
        assert!(parse(b"not a jpeg at all").is_empty());
        assert!(parse(&[0x89, b'P', b'N', b'G']).is_empty());
        // A JPEG with no APP1 segment.
        assert!(parse(&[0xFF, 0xD8, 0xFF, 0xD9]).is_empty());
    }

    /// Truncated EXIF is common in the wild and must not panic.
    #[test]
    fn truncated_input_is_survivable() {
        let _guard = english();
        let jpeg = jpeg_with_exif(true, &[(0x010F, 2, 6, trailer_offset(1).to_le_bytes())], b"Canon\0");
        for cut in 0..jpeg.len() {
            let _ = parse(&jpeg[..cut]);
        }
    }

    #[test]
    fn the_report_uses_the_fixed_order_and_padded_labels() {
        let _guard = english();
        let dir = std::env::temp_dir().join(format!("krate-exif-{}", crate::csprng::below(1_000_000)));
        std::fs::create_dir_all(&dir).unwrap();
        let file = dir.join("shot.jpg");

        // Two tags that appear in a different order in the file than in the report.
        let offset = trailer_offset(2);
        let mut trailer = Vec::new();
        trailer.extend_from_slice(b"Canon\0");
        let model_offset = offset + trailer.len() as u32;
        trailer.extend_from_slice(b"EOS R\0");
        let jpeg = jpeg_with_exif(
            true,
            &[(0x0110, 2, 6, model_offset.to_le_bytes()), (0x010F, 2, 6, offset.to_le_bytes())],
            &trailer,
        );
        std::fs::write(&file, &jpeg).unwrap();

        let report = read(&file.display().to_string()).unwrap();
        let lines: Vec<&str> = report.lines().collect();
        // Camera comes before Model in ORDER, whatever the file said.
        assert!(lines[0].starts_with("Camera "), "{report}");
        assert!(lines[0].contains("Canon"), "{report}");
        assert!(lines[1].starts_with("Model "), "{report}");
        // Labels are padded to 14 columns.
        assert_eq!(&lines[0][..14], "Camera        ", "{:?}", &lines[0][..14]);
        // The dimension line comes from the SOF0 header, not from EXIF.
        assert!(report.contains("Dimensions"), "{report}");
        // SOF0 stores height before width, so this header is 200 wide by 100 tall.
        assert!(report.contains("200 \u{d7} 100 px"), "{report}");

        std::fs::remove_dir_all(&dir).ok();
    }

    #[test]
    fn a_file_with_nothing_readable_says_so() {
        let _guard = english();
        let dir = std::env::temp_dir().join(format!("krate-exif2-{}", crate::csprng::below(1_000_000)));
        std::fs::create_dir_all(&dir).unwrap();
        let file = dir.join("notes.txt");
        std::fs::write(&file, b"just text").unwrap();
        assert_eq!(read(&file.display().to_string()).unwrap(), i18n::get("Exif_None"));

        assert!(read(&dir.join("missing.jpg").display().to_string()).is_err());
        std::fs::remove_dir_all(&dir).ok();
    }
}
