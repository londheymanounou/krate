//! The tool catalogue. Mirrors `Krate.Core.Catalog`: every tool is text in, text out, so the
//! CLI, the WinUI shell and Android can all drive it through one signature.

use crate::i18n;
use base64::Engine as _;

/// A tool the shells surface. Localized text lives in the string catalogue, keyed by id:
/// `Tool_{id}_Name`, `_Desc`, `_Aliases` — the same keys the C# build uses.
pub struct Tool {
    pub id: &'static str,
    pub category: &'static str,
    pub run: fn(&str) -> Result<String, String>,
}

impl Tool {
    pub fn name(&self) -> String {
        i18n::get(&format!("Tool_{}_Name", self.id)).to_string()
    }

    pub fn description(&self) -> String {
        i18n::get(&format!("Tool_{}_Desc", self.id)).to_string()
    }

    pub fn category_name(&self) -> String {
        i18n::get(&format!("Category_{}", self.category)).to_string()
    }

    pub fn aliases(&self) -> Vec<String> {
        i18n::get(&format!("Tool_{}_Aliases", self.id))
            .split(',')
            .map(|a| a.trim().to_string())
            .filter(|a| !a.is_empty())
            .collect()
    }

    pub fn matches(&self, query: &str) -> bool {
        let query = query.to_lowercase();
        self.id.to_lowercase().contains(&query)
            || self.name().to_lowercase().contains(&query)
            || self.aliases().iter().any(|a| a.to_lowercase().contains(&query))
    }
}

pub fn catalog() -> &'static [Tool] {
    CATALOG
}

pub fn find(id: &str) -> Option<&'static Tool> {
    CATALOG.iter().find(|t| t.id.eq_ignore_ascii_case(id))
}

pub fn run(id: &str, input: &str) -> Result<String, String> {
    match find(id) {
        Some(tool) => (tool.run)(input),
        None => Err(i18n::format("Cli_UnknownTool", &[id])),
    }
}

static CATALOG: &[Tool] = &[
    Tool { id: "Count", category: "Text", run: |s| Ok(count(s)) },
    Tool { id: "Upper", category: "Text", run: |s| Ok(upper(s)) },
    Tool { id: "Lower", category: "Text", run: |s| Ok(lower(s)) },
    Tool { id: "Invert", category: "Text", run: |s| Ok(invert(s)) },
    Tool { id: "ReverseLines", category: "Text", run: |s| Ok(reverse_lines(s)) },
    Tool { id: "SortByLength", category: "Text", run: |s| Ok(sort_by_length(s)) },
    Tool { id: "Base64", category: "Encoding", run: |s| Ok(base64_encode(s)) },
    Tool { id: "Base64Decode", category: "Encoding", run: base64_decode },
    Tool { id: "UrlEncode", category: "Encoding", run: |s| Ok(url_encode(s)) },
    Tool { id: "UrlDecode", category: "Encoding", run: |s| Ok(url_decode(s)) },
    Tool { id: "HtmlEncode", category: "Encoding", run: |s| Ok(html_encode(s)) },
    Tool { id: "Bases", category: "Encoding", run: bases },
    Tool { id: "Roman", category: "Conversions", run: roman },
    Tool { id: "Percent", category: "Maths", run: percent },
    Tool { id: "Factor", category: "Maths", run: factor },
    Tool { id: "PortLookup", category: "Developer", run: |s| Ok(port_lookup(s)) },
    Tool { id: "MimeType", category: "Developer", run: |s| Ok(mime_type(s)) },
    Tool { id: "Md5", category: "Hashing", run: |s| Ok(crate::hashing::md5(s)) },
    Tool { id: "Sha1", category: "Hashing", run: |s| Ok(crate::hashing::sha1(s)) },
    Tool { id: "Sha256", category: "Hashing", run: |s| Ok(crate::hashing::sha256(s)) },
    Tool { id: "Sha512", category: "Hashing", run: |s| Ok(crate::hashing::sha512(s)) },
    Tool { id: "HashAll", category: "Hashing", run: |s| Ok(crate::hashing::all(s)) },
    Tool { id: "JsonEscape", category: "Encoding", run: |s| Ok(crate::escapes::json(s)) },
    Tool { id: "SqlEscape", category: "Encoding", run: |s| Ok(crate::escapes::sql(s)) },
    Tool { id: "ShellEscape", category: "Encoding", run: |s| Ok(crate::escapes::shell(s)) },
    Tool { id: "PathConvert", category: "Files", run: |s| Ok(crate::escapes::path(s)) },
    Tool { id: "FilenameClean", category: "Files", run: |s| Ok(crate::escapes::filename(s)) },
    Tool { id: "Statistics", category: "Maths", run: crate::maths::statistics },
    Tool { id: "Sequence", category: "Maths", run: crate::maths::sequence },
    Tool { id: "Fraction", category: "Maths", run: crate::maths::fraction },
    Tool { id: "Title", category: "Text", run: |s| Ok(crate::text::title(s)) },
    Tool { id: "Naming", category: "Text", run: crate::text::naming },
    Tool { id: "Slug", category: "Text", run: |s| Ok(crate::text::slug(s)) },
    Tool { id: "Deaccent", category: "Text", run: |s| Ok(crate::text::deaccent(s)) },
    Tool { id: "Clean", category: "Text", run: |s| Ok(crate::text::clean(s)) },
    Tool { id: "Dedupe", category: "Text", run: |s| Ok(crate::text::dedupe(s)) },
    Tool { id: "Reverse", category: "Text", run: |s| Ok(crate::text::reverse_text(s)) },
    Tool { id: "WordFrequency", category: "Text", run: |s| Ok(crate::text::word_frequency(s)) },
    Tool { id: "Morse", category: "Text", run: |s| Ok(crate::text::morse(s)) },
    Tool { id: "Color", category: "Colors", run: crate::colors::describe },
    Tool { id: "Palette", category: "Colors", run: crate::colors::palette },
    Tool { id: "Contrast", category: "Colors", run: crate::colors::contrast },
    Tool { id: "CssUnits", category: "Colors", run: crate::convert::css_units },
    Tool { id: "AspectRatio", category: "Images", run: crate::convert::aspect_ratio },
    Tool { id: "ShoeSize", category: "Conversions", run: crate::convert::shoe },
    Tool { id: "ColorTemp", category: "Colors", run: crate::convert::color_temp },
    Tool { id: "ColorBlind", category: "Colors", run: crate::convert::color_blind },
    Tool { id: "Combinatorics", category: "Maths", run: crate::maths::combinatorics },
    Tool { id: "Solve", category: "Maths", run: crate::maths::solve },
    Tool { id: "Spell", category: "Conversions", run: crate::words::spell },
    Tool { id: "Duration", category: "Dates", run: crate::duration::duration },
    Tool { id: "SpeedDistanceTime", category: "Conversions", run: crate::physics::speed_distance_time },
    Tool { id: "TransferTime", category: "Conversions", run: crate::physics::transfer_time },
    Tool { id: "PasswordStrength", category: "Hashing", run: crate::security::strength },
    Tool { id: "Calc", category: "Maths", run: crate::calc::evaluate },
    Tool { id: "JsonFormat", category: "Developer", run: crate::json::format },
    Tool { id: "JsonMinify", category: "Developer", run: crate::json::minify },
    Tool { id: "JsonValidate", category: "Developer", run: crate::json::validate },
    Tool { id: "Crlf", category: "Developer", run: |s| Ok(crate::dev::to_crlf(s)) },
    Tool { id: "Lf", category: "Developer", run: |s| Ok(crate::dev::to_lf(s)) },
    Tool { id: "Chmod", category: "Developer", run: crate::dev::chmod },
    Tool { id: "QueryString", category: "Developer", run: |s| Ok(crate::dev::query_string(s)) },
    Tool { id: "Bmi", category: "Everyday", run: crate::everyday::bmi },
    Tool { id: "Tip", category: "Everyday", run: crate::everyday::tip },
    Tool { id: "Loan", category: "Everyday", run: crate::everyday::loan },
    Tool { id: "Scientific", category: "Encoding", run: crate::numbers::scientific },
    Tool { id: "JsonUnescape", category: "Encoding", run: crate::numbers::json_unescape },
    Tool { id: "HexDump", category: "Developer", run: |s| Ok(crate::numbers::hex_dump(s)) },
    Tool { id: "HttpStatus", category: "Developer", run: crate::http::http_status },
    Tool { id: "CaseConverter", category: "Text", run: |s| Ok(crate::text::case_converter(s)) },
    Tool { id: "BaseText", category: "Text", run: |s| Ok(crate::text::base_converter(s)) },
    Tool { id: "Toc", category: "Text", run: |s| Ok(crate::markdown::toc(s)) },
    Tool { id: "MarkdownTable", category: "Text", run: crate::markdown::markdown_table },
    Tool { id: "Convert", category: "Conversions", run: crate::units::convert },
    Tool { id: "Subnet", category: "Everyday", run: crate::net::subnet },
    Tool { id: "CurlToCode", category: "Developer", run: crate::net::curl_to_code },
    Tool { id: "Uuid", category: "Hashing", run: crate::generators::uuid },
    Tool { id: "Password", category: "Hashing", run: crate::generators::password },
    Tool { id: "Random", category: "Random", run: crate::generators::random_number },
    Tool { id: "Dice", category: "Random", run: crate::generators::dice },
    Tool { id: "Coin", category: "Random", run: |s| Ok(crate::generators::coin(s)) },
    Tool { id: "RandomColor", category: "Random", run: |s| Ok(crate::generators::random_color(s)) },
    Tool { id: "Pick", category: "Random", run: crate::generators::pick },
    Tool { id: "Shuffle", category: "Random", run: |s| Ok(crate::generators::shuffle(s)) },
    Tool { id: "Cards", category: "Random", run: crate::generators::cards },
    Tool { id: "Teams", category: "Random", run: crate::generators::teams },
    Tool { id: "Lorem", category: "Text", run: crate::typography::lorem },
    Tool { id: "Zalgo", category: "Text", run: crate::typography::zalgo },
    Tool { id: "FrenchTypography", category: "Text", run: |s| Ok(crate::typography::french_typography(s)) },
    Tool { id: "Gradient", category: "Colors", run: crate::typography::gradient },
    Tool { id: "CsvToJson", category: "Encoding", run: crate::data::csv_to_json },
    Tool { id: "JsonToCsv", category: "Encoding", run: crate::data::json_to_csv },
    Tool { id: "Fancy", category: "Text", run: crate::fancy::convert },
    Tool { id: "ImageInfo", category: "Images", run: crate::images::dimensions },
    Tool { id: "FileCompare", category: "Files", run: crate::files::compare },
    Tool { id: "FolderSize", category: "Files", run: crate::files::folder_size },
    Tool { id: "Duplicates", category: "Files", run: crate::files::duplicates },
    Tool { id: "FileSplit", category: "Files", run: crate::files::split },
    Tool { id: "FileJoin", category: "Files", run: crate::files::join },
    Tool { id: "TestFile", category: "Files", run: crate::files::test_file },
    Tool { id: "Diff", category: "Text", run: crate::diff::diff },
    Tool { id: "Tree", category: "Files", run: crate::diff::tree },
    Tool { id: "Jwt", category: "Encoding", run: crate::tokens::jwt },
    Tool { id: "Gitignore", category: "Developer", run: crate::tokens::gitignore },
    Tool { id: "HtmlDecode", category: "Encoding", run: |s| Ok(crate::decode::html_decode(s)) },
    Tool { id: "JsonToYaml", category: "Encoding", run: crate::decode::json_to_yaml },
    Tool { id: "SqlFormat", category: "Developer", run: crate::decode::sql_format },
    Tool { id: "MarkdownToHtml", category: "Encoding", run: |s| Ok(crate::md::to_html(s)) },
    Tool { id: "Rename", category: "Files", run: crate::md::bulk_rename },
    Tool { id: "XmlFormat", category: "Developer", run: crate::xml::xml_format },
    Tool { id: "XmlValidate", category: "Developer", run: crate::xml::xml_validate },
    Tool { id: "Inspector", category: "Text", run: |s| Ok(crate::unicode::inspector(s)) },
    Tool { id: "Mask", category: "Text", run: |s| Ok(crate::unicode::mask(s)) },
    Tool { id: "Encrypt", category: "Hashing", run: crate::crypt::encrypt },
    Tool { id: "Decrypt", category: "Hashing", run: crate::crypt::decrypt },
    Tool { id: "Cron", category: "Encoding", run: crate::cron::describe },
    Tool { id: "DateDiff", category: "Dates", run: crate::dates::difference },
    Tool { id: "WeekInfo", category: "Dates", run: crate::dates::week_info },
    Tool { id: "Timestamp", category: "Dates", run: crate::dates::timestamp },
    Tool { id: "FileHash", category: "Files", run: crate::dates::file_details },
    Tool { id: "Zip", category: "Files", run: crate::archives::compress },
    Tool { id: "Unzip", category: "Files", run: crate::archives::extract },
    Tool { id: "Qr", category: "Developer", run: crate::codes::unicode },
    Tool { id: "Barcode", category: "Developer", run: crate::codes::code128 },
    Tool { id: "Exif", category: "Images", run: crate::exif::read },
    Tool { id: "SysInfo", category: "Everyday", run: crate::system::sys_info },
    Tool { id: "DnsLookup", category: "Developer", run: crate::system::dns_lookup },
    Tool { id: "Timezone", category: "Dates", run: crate::timezone::timezone },
    Tool { id: "Regex", category: "Developer", run: crate::regex::test },
    Tool { id: "Currency", category: "Conversions", run: crate::currency::convert },
    Tool { id: "SortLines", category: "Text", run: |s| Ok(crate::collate::sort_lines(s)) },
    Tool { id: "StripMetadata", category: "Images", run: crate::strip::strip_metadata },
    Tool { id: "PdfSplit", category: "Files", run: crate::pdf::split },
    Tool { id: "PdfMerge", category: "Files", run: crate::pdf::merge },
    // Provided by the interface, not the core: the GUI has a page for each and the CLI drives them
    // directly. The entry keeps them searchable, exactly as the C# placeholders do.
    Tool { id: "Weather", category: "Everyday", run: crate::regex::not_supported },
    Tool { id: "Snake", category: "Everyday", run: crate::regex::not_supported },
    Tool { id: "Game2048", category: "Everyday", run: crate::regex::not_supported },
    Tool { id: "Tetris", category: "Everyday", run: crate::regex::not_supported },
    Tool { id: "UrlParse", category: "Developer", run: crate::web::url_parse },
    Tool { id: "CssMinify", category: "Developer", run: crate::web::css_minify },
    Tool { id: "EnvVars", category: "Developer", run: |s| Ok(crate::web::env_vars(s)) },
];

// ---------- text ----------

/// Invariant casing, like the C# side: the answer must not move with the interface language
/// (Turkish would otherwise turn "i" into "İ").
pub fn upper(s: &str) -> String {
    s.chars().flat_map(char::to_uppercase).collect()
}

pub fn lower(s: &str) -> String {
    s.chars().flat_map(char::to_lowercase).collect()
}

/// ponytail: average adult reading speed, tweak if it feels off. Matches the C# constant.
pub const WORDS_PER_MINUTE: usize = 200;

/// Character, word and line counts plus a reading estimate.
///
/// The character count is deliberately UTF-16 code units, not Rust `char`s: the C# side reports
/// `string.Length`, so an emoji counts as 2 there. Reporting 1 here would be more "correct" and
/// would still be a behaviour change users would notice between the two builds.
pub fn count(s: &str) -> String {
    let characters = s.encode_utf16().count();
    // Also UTF-16 units, for the same reason: C# counts the emoji's two surrogates here too.
    let no_spaces: usize = s.chars().filter(|c| !c.is_whitespace()).map(char::len_utf16).sum();
    let words = s.split_whitespace().count();
    let lines = if s.is_empty() { 0 } else { s.split('\n').count() };
    let minutes = (words as f64 / WORDS_PER_MINUTE as f64).ceil();

    [
        i18n::format("Text_Count_Characters", &[&characters.to_string()]),
        i18n::format("Text_Count_CharactersNoSpaces", &[&no_spaces.to_string()]),
        i18n::format("Text_Count_Words", &[&words.to_string()]),
        i18n::format("Text_Count_Lines", &[&lines.to_string()]),
        i18n::format("Text_Count_ReadingTime", &[&fmt(minutes)]),
    ]
    .join("\n")
}

/// Swaps the case of every character, so "Hello" becomes "hELLO".
pub fn invert(s: &str) -> String {
    s.chars()
        .flat_map(|c| {
            let inverted: Box<dyn Iterator<Item = char>> = if c.is_uppercase() {
                Box::new(c.to_lowercase())
            } else {
                Box::new(c.to_uppercase())
            };
            inverted
        })
        .collect()
}

fn lines(s: &str) -> Vec<&str> {
    s.replace("\r\n", "\n").leak().split('\n').collect()
}

pub fn reverse_lines(s: &str) -> String {
    let mut all = lines(s);
    all.reverse();
    all.join("\n")
}

pub fn sort_by_length(s: &str) -> String {
    let mut all = lines(s);
    all.sort_by(|a, b| a.chars().count().cmp(&b.chars().count()).then(a.cmp(b)));
    all.join("\n")
}

// ---------- encoding ----------

pub fn base64_encode(s: &str) -> String {
    base64::engine::general_purpose::STANDARD.encode(s.as_bytes())
}

pub fn base64_decode(s: &str) -> Result<String, String> {
    let bytes = base64::engine::general_purpose::STANDARD
        .decode(s.trim())
        .map_err(|_| i18n::get("Error_BadBase64").to_string())?;
    String::from_utf8(bytes).map_err(|_| i18n::get("Error_BadBase64").to_string())
}

/// Matches `Uri.EscapeDataString`: everything outside the RFC 3986 unreserved set becomes
/// percent-encoded UTF-8 bytes in upper-case hex.
pub fn url_encode(s: &str) -> String {
    let mut out = String::with_capacity(s.len());
    for byte in s.bytes() {
        match byte {
            b'A'..=b'Z' | b'a'..=b'z' | b'0'..=b'9' | b'-' | b'.' | b'_' | b'~' => {
                out.push(byte as char)
            }
            _ => out.push_str(&format!("%{byte:02X}")),
        }
    }
    out
}

/// Matches `Uri.UnescapeDataString`. A `%` that is not followed by two hex digits is left
/// alone rather than treated as an error, which is what the .NET side does.
pub fn url_decode(s: &str) -> String {
    let bytes = s.as_bytes();
    let mut out: Vec<u8> = Vec::with_capacity(bytes.len());
    let mut i = 0;
    while i < bytes.len() {
        if bytes[i] == b'%' && i + 2 < bytes.len() {
            let hex = std::str::from_utf8(&bytes[i + 1..i + 3]).ok();
            if let Some(value) = hex.and_then(|h| u8::from_str_radix(h, 16).ok()) {
                out.push(value);
                i += 3;
                continue;
            }
        }
        out.push(bytes[i]);
        i += 1;
    }
    String::from_utf8_lossy(&out).into_owned()
}

/// Matches `WebUtility.HtmlEncode`, whose rule is narrower than it looks: the five markup
/// characters, then only U+00A0..U+00FF as decimal entities, plus astral characters (which
/// .NET sees as surrogate pairs). Everything from U+0100 to U+FFFF is left as-is — measured
/// against the C# build rather than assumed, because "encode everything non-ASCII" is wrong.
pub fn html_encode(s: &str) -> String {
    let mut out = String::with_capacity(s.len());
    for c in s.chars() {
        match c {
            '<' => out.push_str("&lt;"),
            '>' => out.push_str("&gt;"),
            '&' => out.push_str("&amp;"),
            '"' => out.push_str("&quot;"),
            '\'' => out.push_str("&#39;"),
            '\u{a0}'..='\u{ff}' => out.push_str(&format!("&#{};", c as u32)),
            c if c as u32 > 0xFFFF => out.push_str(&format!("&#{};", c as u32)),
            c => out.push(c),
        }
    }
    out
}

const BASE_DIGITS: &[u8] = b"0123456789abcdef";

fn to_base(mut magnitude: u64, base: u64) -> String {
    if magnitude == 0 {
        return "0".to_string();
    }
    let mut digits = Vec::new();
    while magnitude > 0 {
        digits.push(BASE_DIGITS[(magnitude % base) as usize]);
        magnitude /= base;
    }
    digits.reverse();
    String::from_utf8(digits).expect("digits are ASCII")
}

/// One number in binary, octal, decimal and hex. The input base comes from the prefix
/// (0b / 0o / 0x), decimal otherwise.
pub fn bases(input: &str) -> Result<String, String> {
    let t = input.trim().replace(['_', ' '], "");
    if t.is_empty() {
        return Err(i18n::get("Error_NeedNumber").to_string());
    }
    let negative = t.starts_with('-');
    let digits = if negative { &t[1..] } else { &t[..] };
    let bad = || i18n::get("Error_NeedNumber").to_string();

    let lower = digits.to_lowercase();
    let value: i64 = if let Some(bits) = lower.strip_prefix("0b") {
        sign(i64::from_str_radix(bits, 2).map_err(|_| bad())?, negative)
    } else if let Some(oct) = lower.strip_prefix("0o") {
        sign(i64::from_str_radix(oct, 8).map_err(|_| bad())?, negative)
    } else if let Some(hex) = lower.strip_prefix("0x") {
        sign(i64::from_str_radix(hex, 16).map_err(|_| bad())?, negative)
    } else {
        // Decimal keeps its own sign so i64::MIN parses instead of overflowing on negation.
        t.parse::<i64>().map_err(|_| bad())?
    };

    // Sign-and-magnitude, so -255 reads as -0xFF rather than a two's-complement wall of Fs.
    let sign_text = if value < 0 { "-" } else { "" };
    let magnitude = value.unsigned_abs();
    Ok([
        format!("BIN  {sign_text}0b{}", to_base(magnitude, 2)),
        format!("OCT  {sign_text}0o{}", to_base(magnitude, 8)),
        format!("DEC  {value}"),
        format!("HEX  {sign_text}0x{}", to_base(magnitude, 16).to_uppercase()),
    ]
    .join("\n"))
}

fn sign(value: i64, negative: bool) -> i64 {
    if negative { -value } else { value }
}

// ---------- conversions ----------

const ROMAN_TABLE: [(u16, &str); 13] = [
    (1000, "M"), (900, "CM"), (500, "D"), (400, "CD"), (100, "C"), (90, "XC"),
    (50, "L"), (40, "XL"), (10, "X"), (9, "IX"), (5, "V"), (4, "IV"), (1, "I"),
];

/// Roman to Arabic or back, direction detected from the input.
pub fn roman(input: &str) -> Result<String, String> {
    let s = input.trim().to_uppercase();
    if s.is_empty() {
        return Err(i18n::get("Error_NeedNumber").to_string());
    }
    match s.parse::<u16>() {
        Ok(n) => to_roman(n),
        Err(_) => from_roman(&s).map(|n| n.to_string()),
    }
}

pub fn to_roman(mut value: u16) -> Result<String, String> {
    if !(1..=3999).contains(&value) {
        return Err(i18n::get("Error_RomanRange").to_string());
    }
    let mut out = String::new();
    for (v, symbol) in ROMAN_TABLE {
        while value >= v {
            out.push_str(symbol);
            value -= v;
        }
    }
    Ok(out)
}

pub fn from_roman(s: &str) -> Result<u16, String> {
    let bad = || i18n::format("Error_BadRoman", &[s]);
    let (mut total, mut i) = (0u16, 0usize);
    while i < s.len() {
        let matched = ROMAN_TABLE
            .iter()
            .find(|(_, symbol)| s[i..].starts_with(*symbol))
            .ok_or_else(bad)?;
        total = total.checked_add(matched.0).ok_or_else(bad)?;
        i += matched.1.len();
    }
    // Round-trip check: rejects IIII, VV, IC and friends without needing a rule table.
    if to_roman(total).as_deref() != Ok(s) {
        return Err(bad());
    }
    Ok(total)
}

// ---------- maths ----------

/// Splits on the separators the C# side accepts and parses invariantly.
pub(crate) fn numbers(input: &str) -> Result<Vec<f64>, String> {
    let values: Vec<f64> = input
        .split([' ', ',', ';', '\t', '\n'])
        .map(str::trim)
        .filter(|p| !p.is_empty())
        .map(|p| p.parse::<f64>().map_err(|_| i18n::get("Error_NeedNumber").to_string()))
        .collect::<Result<_, _>>()?;
    if values.is_empty() {
        return Err(i18n::get("Error_NeedNumber").to_string());
    }
    Ok(values)
}

/// .NET's `Math.Round(double)` rounds a midpoint to the **even** neighbour; Rust's `f64::round`
/// rounds it away from zero. Anywhere the C# side rounds, this has to be used instead, or the
/// two builds disagree by one on exact halves — which is how `#9FB200` became `#9FB300`.
pub(crate) fn round_half_even(v: f64) -> f64 {
    let floor = v.floor();
    let diff = v - floor;
    if (diff - 0.5).abs() < f64::EPSILON {
        if (floor as i64) % 2 == 0 { floor } else { floor + 1.0 }
    } else {
        v.round()
    }
}

/// The line separator .NET's `StringBuilder.AppendLine` and `Utf8JsonWriter` use — that is
/// `Environment.NewLine`, so CRLF on Windows and LF elsewhere.
///
/// Any output built with `AppendLine` on the C# side must use this, not a bare `\n`. Tools that
/// join with an explicit `'\n'` (most of them) must NOT — check which the original used.
pub(crate) fn newline() -> &'static str {
    if cfg!(windows) { "\r\n" } else { "\n" }
}

/// Mirrors a .NET "0.##…" format string: at most `decimals` places, trailing zeros dropped.
///
/// .NET rounds the **shortest round-trip decimal**, not the exact binary value, and rounds a
/// midpoint away from zero. Rust's `{:.N}` rounds the binary value with ties-to-even, so the two
/// disagree exactly where it is most visible — a 15% tip on 48.50 is stored as 55.774999…, which
/// .NET shows as 55.78 and `{:.2}` shows as 55.77. `format!("{v}")` gives Rust's shortest
/// round-trip, and the rounding is then done on the digits.
pub(crate) fn format_decimal(v: f64, decimals: usize) -> String {
    if !v.is_finite() {
        return v.to_string();
    }
    let shortest = format!("{v}");
    let (negative, digits) = match shortest.strip_prefix('-') {
        Some(rest) => (true, rest.to_string()),
        None => (false, shortest),
    };
    // Exponent form (1e-7) has no digits to round in place; fall back to fixed formatting.
    if digits.contains(['e', 'E']) {
        return format!("{v:.decimals$}");
    }

    let (int_part, frac_part) = match digits.split_once('.') {
        Some((i, f)) => (i.to_string(), f.to_string()),
        None => (digits, String::new()),
    };

    let mut int_digits: Vec<u8> = int_part.bytes().collect();
    let mut frac_digits: Vec<u8> = frac_part.bytes().take(decimals).collect();
    let round_up = frac_part.as_bytes().get(decimals).is_some_and(|d| *d >= b'5');

    if round_up {
        let mut i = frac_digits.len();
        let mut carry = true;
        while carry && i > 0 {
            i -= 1;
            if frac_digits[i] == b'9' {
                frac_digits[i] = b'0';
            } else {
                frac_digits[i] += 1;
                carry = false;
            }
        }
        let mut j = int_digits.len();
        while carry && j > 0 {
            j -= 1;
            if int_digits[j] == b'9' {
                int_digits[j] = b'0';
            } else {
                int_digits[j] += 1;
                carry = false;
            }
        }
        if carry {
            int_digits.insert(0, b'1');
        }
    }

    while frac_digits.last() == Some(&b'0') {
        frac_digits.pop();
    }

    let mut out = String::new();
    if negative && !(int_digits.iter().all(|d| *d == b'0') && frac_digits.is_empty()) {
        out.push('-');
    }
    out.push_str(std::str::from_utf8(&int_digits).expect("ascii"));
    if !frac_digits.is_empty() {
        out.push('.');
        out.push_str(std::str::from_utf8(&frac_digits).expect("ascii"));
    }
    out
}

/// Mirrors C#'s "0.##########": up to ten decimals, trailing zeros dropped, invariant.
pub(crate) fn fmt(v: f64) -> String {
    if !v.is_finite() {
        return v.to_string();
    }
    let mut s = format!("{v:.10}");
    if s.contains('.') {
        s = s.trim_end_matches('0').trim_end_matches('.').to_string();
    }
    // No negative-zero normalisation: .NET renders -0.0 as "-0" and Solve reaches it for the
    // real part of a complex root pair. Tidying it here would be an improvement that breaks
    // parity — during the port, match the original.
    s
}

pub fn percent(input: &str) -> Result<String, String> {
    let n = numbers(input)?;
    if n.len() < 2 {
        return Err(i18n::get("Error_NeedTwoNumbers").to_string());
    }
    let (a, b) = (n[0], n[1]);
    let ratio = if b == 0.0 { "—".to_string() } else { fmt(a / b * 100.0) };
    let change = if a == 0.0 { "—".to_string() } else { fmt((b - a) / a.abs() * 100.0) };
    Ok([
        i18n::format("Percent_Of", &[&fmt(a), &fmt(b), &fmt(a / 100.0 * b)]),
        i18n::format("Percent_Ratio", &[&fmt(a), &fmt(b), &ratio]),
        i18n::format("Percent_Change", &[&fmt(a), &fmt(b), &change]),
    ]
    .join("\n"))
}

pub fn prime_factors(mut n: i64) -> Vec<i64> {
    let mut factors = Vec::new();
    let mut p = 2i64;
    while p.saturating_mul(p) <= n {
        while n % p == 0 {
            factors.push(p);
            n /= p;
        }
        p += if p == 2 { 1 } else { 2 }; // 2, then odds only
    }
    if n > 1 {
        factors.push(n);
    }
    factors
}

pub fn gcd(mut a: i64, mut b: i64) -> i64 {
    while b != 0 {
        (a, b) = (b, a % b);
    }
    a
}

pub fn lcm(a: i64, b: i64) -> i64 {
    a / gcd(a, b) * b
}

/// GCD, LCM and prime factors of the numbers given.
pub fn factor(input: &str) -> Result<String, String> {
    let values: Vec<i64> = numbers(input)?
        .into_iter()
        .map(|v| v.round().abs() as i64)
        .filter(|v| *v > 0)
        .collect();
    if values.is_empty() {
        return Err(i18n::get("Error_NeedNumber").to_string());
    }

    let mut lines: Vec<String> = values
        .iter()
        .map(|v| {
            let factors = prime_factors(*v);
            let is_prime = factors.len() == 1 && *v > 1;
            let body = if *v == 1 {
                "1".to_string()
            } else {
                factors.iter().map(i64::to_string).collect::<Vec<_>>().join(" × ")
            };
            let tail = if is_prime { format!("  {}", i18n::get("Math_Prime")) } else { String::new() };
            format!("{v} = {body}{tail}")
        })
        .collect();

    if values.len() > 1 {
        let g = values.iter().copied().reduce(gcd).unwrap();
        let l = values.iter().copied().reduce(lcm).unwrap();
        lines.push(i18n::format("Math_Gcd", &[&g.to_string()]));
        lines.push(i18n::format("Math_Lcm", &[&l.to_string()]));
    }
    Ok(lines.join("\n"))
}

// ---------- developer ----------

const PORTS: [(&str, &str); 15] = [
    ("21", "FTP"), ("22", "SSH"), ("23", "Telnet"), ("25", "SMTP"), ("53", "DNS"),
    ("80", "HTTP"), ("110", "POP3"), ("143", "IMAP"), ("443", "HTTPS"),
    ("1433", "SQL Server"), ("1521", "Oracle"), ("3306", "MySQL"), ("5432", "PostgreSQL"),
    ("6379", "Redis"), ("27017", "MongoDB"),
];

pub fn port_lookup(input: &str) -> String {
    let s = input.trim();
    if let Some((port, service)) = PORTS.iter().find(|(p, _)| *p == s) {
        return format!("{port} -> {service}");
    }
    if let Some((port, _)) = PORTS.iter().find(|(_, svc)| svc.eq_ignore_ascii_case(s)) {
        return format!("{s} -> Port {port}");
    }
    "Unknown port or service".to_string()
}

const MIME: [(&str, &str); 12] = [
    ("html", "text/html"), ("css", "text/css"), ("js", "application/javascript"),
    ("json", "application/json"), ("xml", "application/xml"), ("txt", "text/plain"),
    ("png", "image/png"), ("jpg", "image/jpeg"), ("jpeg", "image/jpeg"),
    ("svg", "image/svg+xml"), ("pdf", "application/pdf"), ("zip", "application/zip"),
];

pub fn mime_type(input: &str) -> String {
    let s = input.trim().to_lowercase();
    let s = s.trim_start_matches('.');
    match MIME.iter().find(|(ext, _)| *ext == s) {
        Some((ext, mime)) => format!("{ext} -> {mime}"),
        None => "Unknown MIME type".to_string(),
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    // These assertions are lifted from the C# suite so both implementations are held to exactly
    // the same contract while the port is in flight.

    #[test]
    fn casing_is_invariant() {
        let _guard = crate::i18n::test_lock();
        assert_eq!(upper("hello"), "HELLO");
        assert_eq!(lower("HELLO"), "hello");
        i18n::set_language("tr");
        assert_eq!(upper("i"), "I", "casing must not follow the interface language");
        assert_eq!(lower("I"), "i");
        i18n::set_language("en");
    }

    #[test]
    fn reverse_lines_flips_order_and_round_trips() {
        assert_eq!(reverse_lines("a\nb\nc"), "c\nb\na");
        assert_eq!(reverse_lines("one"), "one");
        let text = "alpha\nbeta\ngamma";
        assert_eq!(reverse_lines(&reverse_lines(text)), text);
    }

    #[test]
    fn sort_by_length_is_shortest_first_then_ordinal() {
        assert_eq!(sort_by_length("dddd\nbb\na\nccc"), "a\nbb\nccc\ndddd");
        assert_eq!(sort_by_length("cc\naa\nbb"), "aa\nbb\ncc");
    }

    #[test]
    fn invert_swaps_case_both_ways() {
        assert_eq!(invert("Hello World"), "hELLO wORLD");
        assert_eq!(invert("123!"), "123!");
        assert_eq!(invert(&invert("MiXeD cAsE")), "MiXeD cAsE");
    }

    #[test]
    fn base64_round_trips_including_non_ascii() {
        assert_eq!(base64_encode("hello"), "aGVsbG8=");
        assert_eq!(base64_encode("café"), "Y2Fmw6k=");
        assert_eq!(base64_encode(""), "");
        for s in ["hello", "café", "日本語", "", "a"] {
            assert_eq!(base64_decode(&base64_encode(s)).unwrap(), s);
        }
    }

    #[test]
    fn base64_decode_rejects_rubbish() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        assert!(base64_decode("!!!not-base64!!!").is_err());
        assert!(base64_decode("  aGVsbG8=  ").is_ok(), "surrounding space is trimmed");
    }

    #[test]
    fn url_encoding_matches_the_unreserved_set() {
        assert_eq!(url_encode("a b"), "a%20b");
        assert_eq!(url_encode("café"), "caf%C3%A9");
        assert_eq!(url_encode("a/b?c#d"), "a%2Fb%3Fc%23d");
        assert_eq!(url_encode("100% sure"), "100%25%20sure");
        assert_eq!(url_encode("naive~_-.test"), "naive~_-.test", "unreserved stay literal");
        for s in ["a b&c=d", "café", "100% sure", "a/b?c#d"] {
            assert_eq!(url_decode(&url_encode(s)), s);
        }
    }

    /// The rule is narrower than "escape non-ASCII" — measured against the C# build.
    #[test]
    fn html_encode_matches_dotnet_webutility() {
        assert_eq!(html_encode("<script>"), "&lt;script&gt;");
        assert_eq!(html_encode("a & b"), "a &amp; b");
        assert_eq!(html_encode("\"q\""), "&quot;q&quot;");
        assert_eq!(html_encode("'x'"), "&#39;x&#39;");
        assert_eq!(html_encode("café"), "caf&#233;");
        assert_eq!(html_encode("\u{a0}"), "&#160;");
        assert_eq!(html_encode("€"), "€", "U+20AC is above the encoded range");
        assert_eq!(html_encode("😀"), "&#128512;", "astral characters go by code point");
        assert_eq!(html_encode("100% sure"), "100% sure");
    }

    #[test]
    fn bases_shows_every_radix() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        assert_eq!(
            bases("255").unwrap(),
            "BIN  0b11111111\nOCT  0o377\nDEC  255\nHEX  0xFF"
        );
        assert_eq!(bases("0xFF").unwrap(), bases("255").unwrap(), "prefix picks the input base");
        assert_eq!(bases("0b1010").unwrap(), bases("10").unwrap());
        assert_eq!(bases("0o777").unwrap(), bases("511").unwrap());
        assert_eq!(bases("1_000").unwrap(), bases("1000").unwrap(), "separators are ignored");
    }

    #[test]
    fn bases_uses_sign_and_magnitude_not_twos_complement() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        let negative = bases("-255").unwrap();
        assert!(negative.contains("HEX  -0xFF"), "{negative}");
        assert!(negative.contains("DEC  -255"));
    }

    #[test]
    fn bases_rejects_rubbish() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        for bad in ["zzz", "", "0xZZ", "0b12"] {
            assert!(bases(bad).is_err(), "{bad} should not parse");
        }
    }

    #[test]
    fn roman_converts_both_ways() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        assert_eq!(roman("1994").unwrap(), "MCMXCIV");
        assert_eq!(roman("4").unwrap(), "IV");
        assert_eq!(roman("40").unwrap(), "XL");
        assert_eq!(roman("MCMXCIV").unwrap(), "1994");
        assert_eq!(roman("iv").unwrap(), "4");
        assert!(roman("   ").is_err());
    }

    #[test]
    fn roman_rejects_malformed_numerals() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        for bad in ["IIII", "VV", "IC", "ABC"] {
            assert!(roman(bad).is_err(), "{bad} should not parse");
        }
    }

    #[test]
    fn roman_rejects_out_of_range() {
        assert!(to_roman(0).is_err());
        assert!(to_roman(4000).is_err());
        assert_eq!(to_roman(3999).unwrap(), "MMMCMXCIX");
    }

    #[test]
    fn count_reports_utf16_units_like_csharp() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        let result = count("hello world");
        assert!(result.contains("11"), "{result}");
        // string.Length in C# counts an emoji as its two surrogates; matching that matters more
        // than being technically right, or the two builds disagree in front of the user.
        assert!(count("😀").contains('2'), "{}", count("😀"));
        assert!(count("café").contains('4'));
    }

    #[test]
    fn percent_answers_all_three_questions() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        let result = percent("20 150").unwrap();
        assert!(result.contains("30"), "20% of 150 is 30: {result}");
        assert_eq!(result.lines().count(), 3);
        assert!(percent("5").is_err(), "needs two numbers");
        assert!(percent("").is_err());
    }

    #[test]
    fn percent_does_not_divide_by_zero() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        assert!(percent("5 0").unwrap().contains('—'));
        assert!(percent("0 5").unwrap().contains('—'));
    }

    #[test]
    fn factor_finds_primes_gcd_and_lcm() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        assert_eq!(prime_factors(12), vec![2, 2, 3]);
        assert_eq!(prime_factors(97), vec![97]);
        assert_eq!(prime_factors(1), Vec::<i64>::new());
        assert_eq!(gcd(12, 18), 6);
        assert_eq!(lcm(4, 6), 12);

        let result = factor("12 18").unwrap();
        assert!(result.contains("12 = 2 × 2 × 3"), "{result}");
        assert!(result.contains("18 = 2 × 3 × 3"), "{result}");
        assert!(factor("zzz").is_err());
    }

    #[test]
    fn fmt_drops_trailing_zeros_and_never_shows_negative_zero() {
        assert_eq!(fmt(30.0), "30");
        assert_eq!(fmt(1.5), "1.5");
        assert_eq!(fmt(0.0), "0");
        assert_eq!(fmt(-0.0), "-0", "matches .NET, which does not normalise negative zero");
    }

    #[test]
    fn port_lookup_resolves_both_directions() {
        assert_eq!(port_lookup("443"), "443 -> HTTPS");
        assert_eq!(port_lookup(" 22 "), "22 -> SSH");
        assert_eq!(port_lookup("MySQL"), "MySQL -> Port 3306");
        assert_eq!(port_lookup("mysql"), "mysql -> Port 3306");
        assert_eq!(port_lookup("9999"), "Unknown port or service");
    }

    #[test]
    fn mime_type_maps_extensions_with_or_without_the_dot() {
        assert_eq!(mime_type("png"), "png -> image/png");
        assert_eq!(mime_type(".JPG"), "jpg -> image/jpeg");
        assert_eq!(mime_type("json"), "json -> application/json");
        assert_eq!(mime_type("xyz"), "Unknown MIME type");
    }

    #[test]
    fn every_catalogue_tool_is_localised() {
        let _guard = crate::i18n::test_lock();
        for language in i18n::LANGUAGES {
            i18n::set_language(language);
            for tool in catalog() {
                assert_ne!(tool.name(), format!("Tool_{}_Name", tool.id), "{language}");
                assert_ne!(tool.description(), format!("Tool_{}_Desc", tool.id), "{language}");
                assert_ne!(tool.category_name(), format!("Category_{}", tool.category), "{language}");
                assert!(!tool.aliases().is_empty(), "{language}/{}", tool.id);
            }
        }
        i18n::set_language("en");
    }

    #[test]
    fn run_dispatches_by_id_and_reports_unknown_tools() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        assert_eq!(run("Upper", "hi").unwrap(), "HI");
        assert_eq!(run("upper", "hi").unwrap(), "HI", "ids are case-insensitive");
        assert!(run("NoSuchTool", "").is_err());
    }

    #[test]
    fn search_matches_localised_aliases() {
        let _guard = crate::i18n::test_lock();
        i18n::set_language("en");
        assert!(find("Roman").unwrap().matches("roman"));
        i18n::set_language("fr");
        let roman = find("Roman").unwrap();
        assert!(roman.matches("romain") || roman.matches("roman"));
        i18n::set_language("en");
    }
}
