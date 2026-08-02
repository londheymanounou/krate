package app.krate

/**
 * An example of valid input per tool, shown as the field's placeholder.
 *
 * The generic screen used to offer a bare "Enter value" for every tool, which tells you nothing
 * about the *shape* it wants — `Subnet` needs CIDR, `Percent` needs a sentence, `Regex` needs two
 * parts separated by a slash. On a CLI that lives in `--help`; on a phone there is nowhere else for
 * it to go, so it goes in the placeholder.
 *
 * Keyed on tool **id**, the stable unlocalized key. Unlisted tools fall back to a generic prompt,
 * so this never has to be exhaustive to be useful.
 */
private val HINTS: Map<String, String> = mapOf(
    // Maths
    "Calc" to "2 * (3 + 4)",
    "Percent" to "15% of 200",
    "Fraction" to "0.75",
    "Factor" to "360",
    "Solve" to "2x + 4 = 10",
    "Statistics" to "4, 8, 15, 16, 23, 42",
    "Sequence" to "2, 4, 8, 16",
    "Combinatorics" to "5 choose 2",
    "Bases" to "255  (or 0xFF, 0b1010)",

    // Conversions
    "Convert" to "10 km mi",
    "Roman" to "42  (or XLII)",
    "Spell" to "1234",
    "ShoeSize" to "42 eu us",
    "SpeedDistanceTime" to "120 km in 90 min",
    "TransferTime" to "700 MB at 20 Mbps",

    // Dates
    "Timestamp" to "1700000000",
    "DateDiff" to "2024-01-15 to 2024-12-25",
    "Duration" to "90 min",
    "WeekInfo" to "2024-06-01",

    // Developer
    "Chmod" to "755  (or rwxr-xr-x)",
    "HttpStatus" to "404",
    "PortLookup" to "443",
    "MimeType" to ".pdf",
    "Regex" to "\\d+ / order 66",
    "UrlParse" to "https://example.com/a?b=c",
    "QueryString" to "a=1&b=2",
    "HexDump" to "Any text to dump",
    "Qr" to "https://example.com",
    "Barcode" to "KRATE-2026",
    "Gitignore" to "node, python",
    "CurlToCode" to "curl https://example.com",

    // Encoding
    "Base64" to "Text to encode",
    "Base64Decode" to "SGVsbG8=",
    "Jwt" to "Paste a JWT (eyJ...)",
    "UrlEncode" to "a b&c",
    "UrlDecode" to "a%20b%26c",
    "Scientific" to "0.00042",
    "CsvToJson" to "name,age\\nada,36",
    "JsonToCsv" to """[{"name":"ada"}]""",
    "JsonToYaml" to """{"a": 1}""",
    "MarkdownToHtml" to "# Title",

    // Colours
    "Color" to "#3366ff",
    "Contrast" to "#ffffff #767676",
    "ColorTemp" to "6500",
    "ColorBlind" to "#e63946",
    "Gradient" to "#ff0000 #0000ff",
    "CssUnits" to "16px",

    // Everyday
    "Bmi" to "70 kg 175 cm",
    "Tip" to "48.50 at 15%",
    "Loan" to "20000 at 4.5% over 5 years",
    "Subnet" to "192.168.1.0/24",

    // Images / text
    "AspectRatio" to "1920x1080",
    "Lorem" to "3",
    "Morse" to "SOS",
    "Mask" to "4111111111111111",
    "Naming" to "my variable name",
    "Slug" to "My Article Title!",
    "Count" to "Text to measure",
    "Diff" to "old text / new text",

    // Multi-input file tools: the picker fills one path, the rest are typed or pasted.
    "FileCompare" to "Two paths, one per line",
    "FileJoin" to "Paths to join, one per line",
    "FileSplit" to "path | 10MB",
    "Duplicates" to "Folder to scan",
    "FolderSize" to "Folder to measure",
    "Tree" to "Folder to list",
    "PathConvert" to "A Windows or Unix path",
    "TestFile" to "path | 10MB",
    "FilenameClean" to "A messy File Name!.txt",
    "EnvVars" to "PATH",
    "DnsLookup" to "example.com",
    "JsonFormat" to """{"a":1}""",
    "XmlFormat" to "<a><b/></a>",
    "SqlFormat" to "select * from t where id=1",
    "CssMinify" to "a { color: red; }",
    "Toc" to "Markdown with # headings",
    "MarkdownTable" to "CSV rows to tabulate",
    "WordFrequency" to "Text to count words in",
    "Inspector" to "Text to inspect",

    // Hashing
    "PasswordStrength" to "correct horse battery staple",
    "Md5" to "Text to hash",
    "Sha256" to "Text to hash",
    "HashAll" to "Text to hash",
)

/** Placeholder for a tool's input field, or a neutral prompt when none is listed. */
fun toolHint(id: String): String = HINTS[id] ?: "Enter value"
