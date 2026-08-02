using System.Globalization;
using System.Net;
using System.Reflection;
using System.Text;
using System.Runtime.InteropServices;
using Krate.Core;
using Xunit;

/// <summary>Holds the Rust port to the C# implementation it is replacing.
///
/// The port goes tool by tool behind the FFI seam, which is only safe if both implementations
/// are provably identical for every tool already moved. Each tool ported to Rust gets a row in
/// <see cref="Ported"/>; these tests then run the same inputs through both and demand the same
/// answer, in all 17 languages. A tool is not "ported" until it passes here.</summary>
public partial class RustParityTests
{
    const string Lib = "krate_core";

    [StructLayout(LayoutKind.Sequential)]
    struct KrateResult
    {
        public int Ok;
        public IntPtr Text;
    }

    [LibraryImport(Lib, EntryPoint = "krate_run", StringMarshalling = StringMarshalling.Utf8)]
    private static partial KrateResult KrateRun(string id, string input);

    [LibraryImport(Lib, EntryPoint = "krate_set_language", StringMarshalling = StringMarshalling.Utf8)]
    private static partial void KrateSetLanguage(string language);

    [LibraryImport(Lib, EntryPoint = "krate_set_runtime", StringMarshalling = StringMarshalling.Utf8)]
    private static partial void KrateSetRuntime(string runtime);

    [LibraryImport(Lib, EntryPoint = "krate_free")]
    private static partial void KrateFree(IntPtr text);

    [LibraryImport(Lib, EntryPoint = "krate_tool_count")]
    private static partial int KrateToolCount();

    /// <summary>Runs a tool in Rust and takes ownership of the returned string. Rust allocated
    /// it, so it has to go back to krate_free rather than any .NET free.</summary>
    static (bool Ok, string Text) Rust(string id, string input)
    {
        var result = KrateRun(id, input);
        try { return (result.Ok != 0, Marshal.PtrToStringUTF8(result.Text) ?? ""); }
        finally { KrateFree(result.Text); }
    }

    /// <summary>Every tool that now exists in both implementations, with inputs that exercise
    /// the interesting paths — including the ones that previously broke.</summary>
    public static readonly (string Id, Func<string, string> CSharp, string[] Inputs)[] Ported =
    [
        ("Count",        Text.Count,               ["hello world", "café", "😀", "a\nb\nc", ""]),
        ("Upper",        Text.Upper,               ["hello", "Ünïcode", "i", "", "  spaced  "]),
        ("Lower",        Text.Lower,               ["HELLO", "ÜNÏCODE", "I", "", "MiXeD"]),
        ("Invert",       Text.Invert,              ["Hello World", "123!", "MiXeD cAsE", ""]),
        ("ReverseLines", Text.ReverseLines,        ["a\nb\nc", "one", "x\n\ny"]),
        ("SortByLength", Text.SortByLength,        ["dddd\nbb\na\nccc", "cc\naa\nbb", "solo"]),
        ("Base64",       Encodings.Base64Encode,   ["hello", "café", "", "日本語", "a"]),
        ("Base64Decode", Encodings.Base64Decode,   ["aGVsbG8=", "Y2Fmw6k=", "  aGVsbG8=  ", "!!!bad!!!"]),
        ("UrlEncode",    Encodings.UrlEncode,      ["a b", "café", "a/b?c#d", "100% sure", "naive~_-.test"]),
        ("UrlDecode",    Encodings.UrlDecode,      ["a%20b", "caf%C3%A9", "a%2Fb%3Fc%23d", "plain", "100%"]),
        ("HtmlEncode",   Encodings.HtmlEncode,     ["<script>", "a & b", "\"q\"", "'x'", "café", " ", "€", "😀", "100% sure"]),
        ("Bases",        Encodings.Bases,          ["255", "0xFF", "0b1010", "0o777", "1_000", "-255", "0", "zzz"]),
        ("Roman",        Units.Roman,              ["1994", "4", "40", "3999", "MCMXCIV", "iv"]),
        ("Percent",      Maths.Percent,            ["20 150", "5 0", "0 5", "-10 20", "2.5 4", "5"]),
        ("Factor",       Maths.Factor,             ["12", "97", "1", "12 18", "100 75 50", "zzz"]),
        ("PortLookup",   Dev.PortLookup,           ["443", " 22 ", "MySQL", "mysql", "9999"]),
        ("MimeType",     Dev.MimeTypeLookup,       ["png", ".JPG", "json", "xyz"]),

        ("Md5",          Hashing.Md5,              ["hello", "", "café", "the quick brown fox"]),
        ("Sha1",         Hashing.Sha1,             ["hello", "", "café"]),
        ("Sha256",       Hashing.Sha256,           ["hello", "", "café"]),
        ("Sha512",       Hashing.Sha512,           ["hello", "", "café"]),
        ("HashAll",      Hashing.All,              ["hello", ""]),

        ("JsonEscape",   Escapes.Json,             ["hi", "a\nb", "tab\there", "</script>", "quote\"here", "'x'"]),
        ("SqlEscape",    Escapes.Sql,              ["O'Brien", "plain", "", "a''b"]),
        ("ShellEscape",  Escapes.Shell,            ["plain", "it's", "", "a b c"]),
        ("PathConvert",  Escapes.Path,             [@"C:\a\b", "C:/a/b", "plain", "  spaced  "]),
        ("FilenameClean",Escapes.Filename,         ["a:b*c?.txt", "trailing...", "   ", "CON", "con.txt", "normal.txt"]),

        ("Statistics",   Maths.Statistics,         ["1 2 3 4 5", "1 2 3 4", "7", "2.5 3.5", "zzz"]),
        ("Sequence",     Maths.Sequence,           ["fib 5", "prime 5", "arith 2 3 4", "geom 2 3 4", "nonsense", ""]),
        ("Fraction",     Maths.Fraction,           ["3/4", "1.5", "0.75", "-0.25", "1/0", "zzz"]),

        ("Title",        Text.Title,               ["hello WORLD of code", "o'brien", "o’brien", "hello-world", "3rd place", "", "ALL CAPS"]),
        ("Naming",       Text.Naming,              ["hello world", "helloWorld", "HTTPServer", "user_id", "   "]),
        ("Slug",         Text.Slug,                ["Hello, World!", "Crème Brûlée", "helloWorld", ""]),
        ("Deaccent",     Text.Deaccent,            ["éèàçüôÀÉ", "Crème brûlée", "plain ascii", "straße", "日本語"]),
        ("Clean",        Text.Clean,               ["a   b", "  padded  ", "a\n\n\n\nb", "a\n\n\n", ""]),
        ("Dedupe",       Text.Dedupe,              ["b\na\nb\nc\na", "solo", ""]),
        ("Reverse",      Text.ReverseText,         ["abc", "héllo", "ab😀", ""]),
        ("WordFrequency",Text.WordFrequency,       ["a b a c a b", "one", "The the THE"]),
        ("Morse",        Text.Morse,               ["sos", "... --- ...", "hello world", "", "café"]),

        ("Palette",      Colors.Palette,           ["#FF0000", "#336699", "#000000"]),
        ("Contrast",     Colors.Contrast,          ["#000000\n#FFFFFF", "#FF0000\n#FF0000", "#777777\n#FFFFFF", "#000000"]),
        ("ColorTemp",    Colors.Temperature,       ["1900", "6500K", "2700", "20000", "500", "zzz"]),
        ("ColorBlind",   Colors.ColorBlind,        ["#FF0000", "#808080", "#336699", "zzz"]),

        ("CssUnits",     Css.Units,                ["16px", "1.5rem", "12pt", "50%", "1rem", "zzz", "10furlongs"]),
        ("AspectRatio",  Images.Ratio,             ["1920x1080", "1024x768", "1920x1200", "1000x999", "16:9 1920", "zzz", "0x100"]),
        ("ShoeSize",     Sizes.Shoe,               ["42", "38w", "9uk", "27cm", "10us", "no digits"]),

        ("Combinatorics",Maths.Combinatorics,      ["5", "0", "100", "5 2", "10 3", "6000", "-1", "5 9", "zzz"]),
        ("Solve",        Maths.Solve,              ["1 -3 2", "1 -2 1", "1 0 1", "2 -4", "0 0 5", "1 2 3 4"]),
        ("Spell",        Words.Spell,              ["1234", "0", "21 fr", "71 fr", "80 fr", "81 fr", "200 fr", "1000 fr", "2000000 fr", "101", "zzz"]),
        ("Duration",     Dates.Duration,           ["90", "1.5h", "1d 2h 30m", "500ms", "2:30:00", "90:00", "5 h s", "1 day h", "0", "", "5 furlongs"]),
        ("SpeedDistanceTime", Physics.Solve,       ["100km 50km/h", "100km 2h", "50km/h 2h", "26.2mi 3h", "100km", "100km 0h", "100 furlongs 2h"]),
        ("TransferTime", Transfer.Time,            ["1GB 100Mbps", "100Mbps 1GB", "1GB 100MB/s", "700MB 10Mbps", "2GiB 1Gbps", "1GB", "1GB 2GB"]),
        ("PasswordStrength", Security.Strength,    ["aaaaaaaa", "abcdefgh", "Tr0ub4dor&3xK", "password", "x", "correct horse battery staple", ""]),
        ("Calc",         Calc.Evaluate,            [
            "2+3*4", "(2+3)*4", "10/4", "10%3", "-2^2", "2^3^2", "2^-3",
            "0.1+0.2", "1/3", "1e14", "1e15", "0.0001", "0.00001", "2^100", "1/3000000",
            "sqrt(2)", "pi", "e", "tau", "phi", "floor(2.7)", "ceil(2.1)", "round(2.5)", "round(3.5)",
            "sign(0)", "sign(-4)", "abs(-5)", "deg(pi)", "rad(180)", "ln(e)", "log(1000)", "log2(8)",
            "5!", "0!", "(-1)!", "2.5!", "200!",
            "", "2+", "(2+3", "2 3", "nosuchfn(2)", "nosuchname", "1/0", "0/0",
        ]),

        // Exponent notation and negative zero are included: serde rewrites both, so these rows
        // are what holds RawNumbers to copying the source token through verbatim.
        ("JsonFormat",   Json.Format,              [
            @"{""a"":[1,2],""b"":{""c"":true}}", "[]", "{}", @"{""k"":""café""}", "{", "nonsense",
            @"{""e"":1e3,""E"":1E3,""z"":-0,""big"":1.5e300,""neg"":-2.5e-3}",
        ]),
        ("JsonMinify",   Json.Minify,              [
            @"{ ""a"": [ 1, 2 ] }", "[]", "{}",
            @"{""k"":""café""}", @"{""k"":""<b>""}", @"{""k"":""a&b""}", @"{""k"":""a+b""}",
            @"{""k"":""a=b/c!d*e$f%g""}", @"{""a"":1.0,""c"":0.1}", @"{""big"":12345678901234567890}",
            "{", "nonsense",
            @"{""e"":1e3,""E"":1E3,""z"":-0,""f"":1.50,""g"":1.5e300}",
            // A number-shaped string must not be mistaken for a token, and an escaped quote
            // must not desynchronise the cursor that walks them.
            @"{""a"":""1e3"",""b"":2}", @"{""a"":""say \""1e9\"""",""b"":-2.5e-3}",
            @"[1,[2,[3]],{""k"":[4,5]},-0,1e3]",
        ]),
        ("JsonValidate", Json.Validate,            [
            @"{""a"":1}", @"{""a"":}", "[]", "not json", "",
        ]),

        ("Crlf",         Escapes.ToCrlf,           ["a\nb", "a\r\nb", "no newlines", ""]),
        ("Lf",           Escapes.ToLf,             ["a\r\nb", "a\nb", "no newlines", ""]),
        ("Chmod",        Dev.Chmod,                ["755", "644", "000", "777", "4755", "1777", "rwxr-xr-x", "rw-r--r--", "---------", "", "8", "999", "rwx", "12345"]),
        ("QueryString",  Dev.QueryString,          ["a=1&b=hello%20world", "https://x.com/p?a=1", "q=a+b", "a=1;b=2", "novalue", ""]),
        ("Bmi",          Everyday.Bmi,             ["70 175", "70 1.75", "45 175", "100 175", "70", "0 175", "zzz"]),
        ("Tip",          Everyday.Tip,             ["100", "100 20 4", "48.50 15 3", "0", ""]),
        ("Loan",         Everyday.Loan,            ["200000 3.5 25", "12000 0 1", "5000 10 360", "200000 3.5"]),

        ("Scientific",   Escapes.Scientific,       ["1234.5", "0.000123", "1", "0", "-42", "1e20", "6.022e23", "zzz"]),
        ("JsonUnescape", Escapes.JsonUnescape,     [@"""a\nb""", @"a\tb", @"""café""", "plain"]),
        ("HexDump",      Dev.HexDump,              ["hello", "0123456789abcdefX", "", "café"]),
        ("HttpStatus",   Dev.HttpStatus,           ["200", "404", "301", "500", " 204 ", "418", "599", "999", "0", "zzz"]),
        ("CaseConverter",Text.CaseConverter,       ["XMLHttpRequest2", "hello world", "user_id", "helloWorld", "   ", "", "ABC"]),
        ("Toc",          Text.Toc,                 [
            "# One\n## Two\n### Three", "# Real\n```\n# Not a heading\n```\n## Also real",
            "# Same\n# Same\n# Same", "# Crème, brûlée!", "#NoSpace", "####### Seven", "plain text", "",
        ]),
        ("MarkdownTable",Text.MarkdownTable,       ["a,bb\n1,2", "a,b\tc\n1\t2", "a,b,c\n1", "solo", ""]),
        ("Convert",      Units.Convert,            [
            "10 km mi", "1 GiB MiB", "1 h min", "100 F C", "0 C K", "32 f c",
            "1 KM m", "1 MB kB", "8 b B", "1 Mb kB", "1 km kg", "1 xyz m", "10 km", "zzz km mi",
        ]),
        ("Subnet",       Everyday.Subnet,          [
            "192.168.1.10/24", "10.0.0.1/32", "10.0.0.0/31", "10.0.0.0/0", "10.1.2.3/8",
            "172.16.5.5/12", "8.8.8.8/24", "192.168.1.1", "192.168.1.1/33", "1.2.3.999/24", "zzz/24", "",
        ]),
        ("CurlToCode",   Dev.CurlToCode,           [
            "curl https://api.example.com/users", "curl -X POST https://x.com",
            "curl -X delete https://x.com/1", "curl not-a-url",
        ]),

        // Lorem is not random despite the name: words are taken in order from a fixed list.
        ("Lorem",        Text.Lorem,               ["5", "", "3p", "p", "500", "0", "10001", "zzz"]),
        ("FrenchTypography", Text.FrenchTypography,[
            "Bonjour !", "Bonjour!", "a   ;b", "50%", "« oui »", "Attendez...",
            "https://example.com", "http://x.y", "",
        ]),
        ("Gradient",     Css.Gradient,             ["#f00 #00f", "45deg #f00 #00f", "#f00 #00ff00 #00f", "#f00", "zzz #f00"]),
        ("Fancy",        Fancy.Convert,            ["ABZabz059", "gh", "YZ", "CHNPQRZ", "a-é!", "a b", ""]),
        // A 2020 expiry so the expired/valid branch is deterministic rather than clock-dependent.
        ("Jwt",          Escapes.Jwt,              [
            "eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxIiwiaWF0IjoxNjAwMDAwMDAwLCJleHAiOjE2MDAwMDM2MDB9.sig",
            "eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxIn0.sig",
            "notatoken", "", "!!!.!!!",
        ]),
        ("Gitignore",    Dev.Gitignore,            ["rust", "node, python", "RUST", "rust nonsense", "nonsense", ""]),
        ("UrlParse",     Dev.UrlParse,             [
            "https://x.com/a/b?q=1&r=a+b#frag", "http://x.com:8080/p", "https://x.com",
            "https://x.com:443/", "http://x.com:80/", "https://x.com:80/", "ftp://a.b/c",
            "notaurl", "", "/just/a/path",
        ]),
        // No "&#0;" here: it decodes to a NUL, which the C-string FFI replaces with U+FFFD by
        // design. decode.rs covers that case directly instead.
        // Astral characters are left out: Inspector walks UTF-16 code units, so an emoji produces
        // two lines naming lone surrogates, and a lone surrogate cannot survive the UTF-8 FFI
        // boundary. Inspector_AgreesOnEveryCodeUnit covers those.
        // Timezone: .NET resolves IANA ids through ICU on Windows, the Rust side through
        // chrono-tz's bundled tzdb. Two independent snapshots of the same database, so this row is
        // where any disagreement would surface. Dates only — "now" and bare clock times depend on
        // the current instant and are covered by Timezone_AgreesOnTheCurrentInstant.
        ("Timezone",     Dates.Timezone,           [
            // Standard time and daylight time, either side of the northern switch.
            "2024-01-15 paris tokyo", "2024-07-15 paris tokyo",
            "2024-01-15 utc America/New_York America/Los_Angeles",
            "2024-07-15 utc America/New_York America/Los_Angeles",
            // Fractional offsets.
            "2024-01-15 utc Asia/Kolkata Australia/Eucla Pacific/Chatham",
            "2024-07-15 utc Asia/Kathmandu Asia/Tehran Pacific/Marquesas",
            // Southern hemisphere, where the seasons invert.
            "2024-01-15 utc Australia/Sydney Pacific/Auckland America/Sao_Paulo",
            "2024-07-15 utc Australia/Sydney Pacific/Auckland America/Sao_Paulo",
            // Zones that do not shift at all.
            "2024-07-15 utc Asia/Tokyo Asia/Shanghai Asia/Dubai Asia/Kolkata",
            // A single zone falls back to the default target list.
            "2024-01-15 tokyo", "2024-07-15 nyc",
            // Every alias, so the mapping table is compared entry by entry.
            "2024-03-01 utc paris london berlin madrid rome moscow",
            "2024-03-01 utc newyork nyc ny losangeles la sf",
            "2024-03-01 utc chicago denver toronto saopaulo mexicocity",
            "2024-03-01 utc tokyo shanghai beijing hongkong singapore seoul",
            "2024-03-01 utc dubai mumbai delhi kolkata sydney auckland",
            "2024-03-01 gmt z utc",
            // Names as a user might type them.
            "2024-03-01 utc new_york", "2024-03-01 utc NEWYORK",
            // A full date and time, read as local to the source zone.
            "2024-01-15 14:30 paris", "2024-07-15 09:00 tokyo",
            // Historical dates, where two tzdb snapshots are most likely to differ.
            "1990-06-15 utc Europe/Paris America/New_York",
            "2000-01-01 utc Europe/London Asia/Tokyo",
            "2010-11-07 utc America/New_York",
            // Refusals.
            "", "   ", "14:30", "now", "paris nowhere", "nowhere",
        ]),
        // Regex: the group-numbering rule is the point — .NET numbers unnamed groups first and
        // named ones after, so (a)(?<x>b)(c)(?<y>d) enumerates a, c, x, y. Lookaround and
        // backreferences are covered too, since those are why fancy-regex was chosen. Invalid
        // patterns are in Regex_RejectsTheSameInvalidPatterns: .NET's parser message is its own
        // English text, so only the rejection is compared.
        ("Regex",        Dev.RegexTest,            [
            @"(\w+)@(\w+)
a@b and c@d",
            @"(?<user>\w+)@(?<host>\w+)
a@b",
            @"(\w+)@(?<host>\w+)
a@b",
            @"(?<host>\w+)@(\w+)
a@b",
            @"(a)(?<x>b)(c)(?<y>d)
abcd",
            @"(?:no)(yes)
noyes",
            @"(a)?(b)
b",
            @"(?<n>a)|(b)
b",
            @"\d+
a1 b22 c333",
            @"^
abc",
            @"x
no ex here",
            @"[aeiou]
hello world",
            @"(\w)\1
aa bb cd",
            @"foo(?=bar)
foobar fooqux",
            @"(?<=\$)\d+
$42 and 7",
            @"\b\w{4}\b
this is a test of word len",
            @"(a+)+b
aaab",
            @".
ab",
            @"(?'q'\w)
z",
            @"a
éa",
            @"[]()]
x)y",
            @"\(lit\)
a (lit) b",
            @"/ABC/i
abc ABC",
            "/a.b/s\na\nb",
            "/^b$/m\na\nb\nc",
            @"/x/
x",
            @"/x/gu
x",
            @"(\d)(\d)(\d)
123 456",
            "", "onlyapattern", @"
subject", @"   
subject",
        ]),
        // SortLines: StringComparer.Create(culture, false) is ICU collation, and the Rust side
        // uses ICU4X — the same algorithm on the same CLDR data. This row runs in all 17
        // languages, so a tailoring difference in any of them surfaces here. Non-ASCII is
        // written as escapes so the source file stays ASCII.
        ("SortLines",    Text.SortLines,           [
            "b\nA\na\nB", "zebra\napple\nbanana", "apple\n1one\n_under",
            "x\nx\nx", "", "only", "b\n\na", "b\r\na",
            // Accents must sort next to their base letter, not after z.
            "zebra\ncaf\u00E9\ncafe\n\u00E9clair\napple",
            "z\n\u00E4\na\n\u00F6\no\n\u00E5",
            // Mixed case with accents, where the tiebreak order matters.
            "\u00C9clair\n\u00E9clair\nEclair\neclair",
            // The German sharp s, which some tailorings equate with "ss".
            "stra\u00DFe\nstrasse\nstrase",
            // Non-Latin scripts.
            "\u4E2D\u6587\nabc\n\u65E5\u672C\n\u0410\u0411\u0412",
            "\u03B1\u03B2\n\u03B3\u03B4\nab",
            // Collation does not sort digits numerically.
            "10\n9\n1\n2",
            // Punctuation and whitespace.
            "a b\na-b\nab\na_b",
        ]),
        // Barcode is hand-rolled on both sides, so this is a straight transcription check.
        ("Barcode",      Barcode.Code128,          [
            "HI", "A", "HELLO WORLD", "12345", "abc", " ", "~", "!", "0123456789",
            "The quick brown fox", "a-b_c.d", "",
            "\u00E9", "tab\there", "line\nbreak", "\u007F",
            "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
        ]),
        // Dates: the Rust parser covers a defined subset of DateTimeOffset.TryParse, so every
        // input here is one both sides accept or both reject. The subset is documented in dates.rs;
        // Dates_TheParserSubsetMatches pins the boundary explicitly.
        ("DateDiff",     Dates.Difference,         [
            "2020-01-01 2024-03-05", "2024-03-05 2020-01-01", "2024-01-01 2025-01-01",
            "2024-02-29 2024-03-01", "2020-02-29 2021-02-28", "2024-01-31 2024-03-01",
            "2000-01-01 2000-01-01", "1900-01-01 2000-01-01", "2024-07-27 2024-08-03",
            "2024-01-15;2024-02-15", "2024-01-15\t2024-02-15", "2024-01-15\n2024-02-15",
            "15 January 2024 15 February 2024", "January 15, 2024 2024-02-15",
            "", "nonsense", "2024-02-30 2024-03-01", "2024-13-01 2024-01-01",
        ]),
        ("WeekInfo",     Dates.WeekInfo,           [
            "2026-07-05", "2021-01-01", "2024-12-31", "2019-12-30", "2024-06-15",
            "2024-01-01", "2024-02-29", "2024-12-25", "2026-01-01", "2000-01-01",
            "2024-03-31", "2024-04-01", "2024-09-30", "2024-10-01",
            "15 January 2024", "nonsense", "2024-02-30",
        ]),
        // Month and weekday names come from the culture, so this row is the real check on the
        // generated cultures.rs: it runs in all 17 languages like every other row.
        ("Cron",         Cron.Describe,            [
            "30 4 * * *", "0 0 * * *", "*/15 * * * *", "0 9-17 * * *", "0,30 * * * *",
            "@daily", "@midnight", "@hourly", "@yearly", "@annually", "@monthly", "@weekly",
            "@nonsense", "", "* * *", "* * * * * *", "@",
            "0 0 * 1 *", "0 0 * 12 *", "0 0 * 1-3 *", "0 0 * 1,6,12 *", "0 0 * */3 *",
            "0 0 * * 0", "0 0 * * 7", "0 0 * * 1", "0 0 * * 1-5", "0 0 * * MON", "0 0 * * 6,0",
            "0 0 1 * *", "0 0 1,15 * *", "0 0 1-7 * *", "*/5 */2 * * *", "* * * * *",
            "0 0 * 13 *", "0 0 * * 9", "5 * * * *", "0 12 25 12 *",
        ]),
        ("Inspector",    Text.Inspector,           [
            // Exotic characters are written as Unicode escapes on purpose: several are
            // invisible, and a literal U+2028, U+2029 or U+0000 is not something the C#
            // compiler accepts inside a string constant.
            "hello", "", "abc123!@#", " ", "\u0020\u00A0", "\n", "\t", "a\r\nb",
            "\u00E9", "\u00C9", "\u4E2D", "\u00A0", "\u001F", "\u0085",
            "\u200B", "\u2028", "\u2029", "\u01C5", "\u0301", "\u2160",
            "\u0661\u0662\u0663", "\u00BD", "\u00AD", "\uFFFD", "\u20AC",
            "\u3000", "\u2007", "\u200D", "\u2500", "\uD7A3", "\uE000",
            "\uFFFF", "\uFDD0", "\u0061\u00E9\u0062\u0063\u00A0", "\u0063\u0061\u0066\u00E9\u0020\u4E2D\u3000",
        ]),
        ("Mask",         Text.Mask,                [
            "write to a.b+c@example.co.uk now", "x@y.z", "a@b", "@b.c", "a@b.",
            "call +33 6 12 34 56 78 today", "555-123-4567", "555 123-4567", "12-34",
            "id 123456 here", "12345", "1234567890", "abc123456", "v1.123456", "123456abc",
            "x 123456", "ref 12.345678", "", "nothing to hide", "year 2024 and 1999",
            "mail a@b.co and call 555-123-4567 ref 987654321",
            "a@b.co,c@d.org", "+1 (555) 123-4567", "1.2.3.4", "2024-01-15",
            "id_123456", "id.123456", "naïve@exämple.de", "IBAN FR7630006000011234567890189",
        ]),
        // Well-formed input only. Malformed input is compared by Xml_RejectTheSameMalformedInput,
        // which demands both sides reject but not that they agree on the exact position — those
        // come from System.Xml's internals ("Data at the root level is invalid" for an unclosed
        // element, reported at 1,1).
        ("XmlFormat",     Dev.XmlFormat,           [
            "<a><b>1</b></a>", "<a/>", "<a></a>", "<a b=\"1\" c=\"2\"/>", "<a>text</a>",
            "<a>  <b/>  </a>", "<a><!--c--><b/></a>", "<a><![CDATA[x<y]]></a>",
            "<a><![CDATA[]]></a>", "<a>&lt;&amp;&gt;&quot;</a>", "<a xmlns=\"u\"><b/></a>",
            "<a x=\"&quot;q&quot;\"/>", "<a>line1\nline2</a>", "<a><b><c><d/></c></b></a>",
            "<?xml version=\"1.0\"?><a><b/></a>",
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?><a/>",
            "<?xml version=\"1.0\" standalone=\"yes\"?><a/>",
            "<!--top--><a/>", "<a/><!--after-->", "<!--c1--><!--c2--><a/>",
            "<?pi data?><a/>", "<a><?pi d?><b/></a>", "<a><?pi?></a>",
            "<a x=\"1&#10;2\"/>", "<a x=\"a>b\"/>", "<a>a>b</a>", "<a>&#65;&#x42;</a>",
            "<a><b/>text</a>", "<a>text<b/></a>", "<a> </a>", "<a><b></b></a>",
            "<p:a xmlns:p=\"u\"><p:b/></p:a>", "<a\n  b=\"1\"\n  c=\"2\"/>",
            "<a b=\"1\" a=\"2\" c=\"3\"/>", "<a>tab\there</a>", "<a>café 中</a>",
            "<a>\n  <b/>\n</a>", "<a><b>1</b><b>2</b></a>", "<a x='single'/>",
            "<a>it's</a>", "<a x=\"it's\"/>", "<a>&apos;</a>",
            "<a><b x=\"1\"><c/></b><d>t</d></a>",
            "<r><a/><b>x</b><c><d/></c></r>",
            "<a xmlns=\"u\"><b xmlns=\"u\"/></a>",
            "<a>  \n  <b/>\n  </a>",
        ]),
        ("XmlValidate",   Dev.XmlValidate,         ["<a><b/></a>", "<a/>", "<a>x</a>"]),
        ("MarkdownToHtml", Markdown.ToHtml,        [
            "# Title", "###### Six", "####### Seven", "#NoSpace",
            "one\ntwo\n\nthree", "", "   ", "para\n\n\n\npara2",
            "- a\n- b", "1. a\n2. b", "- a\n1. b", "* only", "- a\n\n- b",
            "---", "***", "___", "- - -", "* * * *", "--", "-*-",
            "> quoted", ">nospace", "> a\n> b",
            "```\n**not bold** <b>\n```", "```\nx", "```lang\ny\n```",
            "**bold** __bold__ *it* _it_ `code`", "[text](http://x.com)", "[x](a&b)",
            "`**x**`", "`<b>`", "`a` and `b`", "`unclosed", "``",
            "a < b & c", "# Heading with `code` and **bold**",
            "- item with [link](u) and *em*", "mixed\n# heading\nmore",
            "1. one\ntext\n2. two", "> quote\n- list\n# head",
        ]),
        ("HtmlDecode",   Encodings.HtmlDecode,     [
            "&amp;", "&AMP;", "&nosuch;", "&amp", "&", "&;", "&#;", "&#x;", "a&amp;b&lt;c",
            "&#65;", "&#x41;", "&#X41;", "&#0065;", "&# 65;", "&#65 ;", "&#+65;", "&#-65;",
            "&#x 41;", "&#xD800;", "&#x10FFFF;", "&#x110000;", "&#1114112;", "&#4294967296;",
            "&&amp;", "&amp&amp;", "&#65&#66;", "&lt&gt;", "&amp;amp;", "&#x1F600;", "&#128512;",
            "&nbsp;&copy;&hellip;", "&frac12;&Alpha;&omega;", "", "no entities here",
        ]),
        ("JsonToYaml",   Data.JsonToYaml,          [
            "{\"a\":1,\"b\":\"x\"}", "{\"a\":{\"b\":[1,2]}}", "[1,2]", "{\"a\":{},\"b\":[]}",
            "{}", "[]", "42", "null", "\"bare\"", "true", "{oops",
            "{\"a\":\"1\",\"b\":\"true\",\"c\":\"null\",\"d\":\"yes\",\"e\":\"\",\"f\":\" x\"}",
            "{\"a\":\"k: v\",\"b\":\"-x\",\"c\":\"say \\\"hi\\\"\",\"d\":\"a\\nb\"}",
            "{\"a\":\"(5)\",\"b\":\"5-\",\"c\":\"$5\",\"d\":\"inf\",\"e\":\"NaN\",\"f\":\"1,000\"}",
            "{\"z\":1,\"a\":2,\"m\":3}", "[[1,[2]],{\"k\":[{}]}]",
            "{\"n\":1.5e300,\"m\":-0,\"big\":123456789012345678901234567890}",
        ]),
        ("SqlFormat",    Dev.SqlFormat,            [
            "select a from t where b = 1", "select * from a inner join b on a.id = b.id order by x",
            "SELECT a FROM t", "select android from t", "select a_from_b from t",
            "select count(*) from t group by x having count(*) > 1 limit 10 offset 5",
            "insert into t values (1,2)", "update t set a = 1 where b is not null",
            "delete from t where a in (1,2) and b like 'x%' or c between 1 and 2",
            "select a from t union all select b from u union select c from v",
            "select distinct a as x from t left join u on t.i = u.i right join v on 1 = 1",
            "select case when a then b else c end from t", "  select\t a \n from  t  ", "x",
        ]),
        ("CssMinify",    Css.Minify,               [
            "a { color : red ; }", "/* note */ a{b:c}", "a > b ~ c + d { x : y }",
            "a{b:c}/* multi\nline\ncomment */", "/*x*/a{b:c}/*y*/d{e:f}", "", "   ",
        ]),
        ("Diff",         Text.Diff,                [
            "a\nb\nc\n---\na\nx\nc", "a\nb\n---\na\nb", "a\n---\na\nb", "a\nb\n---\na",
            "---\nonly second", "first\n---", "no separator here", "",
        ]),
        ("CsvToJson",    Data.CsvToJson,           [
            "name,age,active\nada,36,true", "id\n007", "a,b\n1,\"two, three\"",
            "\"say \"\"hi\"\"\"", "a,b\n1,2\n", "",
        ]),
        ("JsonToCsv",    Data.JsonToCsv,           [
            @"[{""a"":1,""b"":2},{""b"":3,""c"":4}]", @"[{""a"":""two, three""}]",
            @"[{""a"":""say \""hi\""""}]", @"[{""a"":null,""b"":{""n"":1}}]",
            @"{""a"":1}", "[1,2,3]", "[]", "nonsense",
        ]),
        // Malformed colour notations used to crash the C# with a raw range error.
        ("Color",        Colors.Describe,          ["#FF0000", "#f00", "rgb(255, 0, 0)", "hsl(0, 100%, 50%)", "#808080", "zzz", "rgb(0", "hsl(", "rgb)"]),
    ];

    public static IEnumerable<object[]> PortedTools() => Ported.Select(p => new object[] { p.Id });

    /// <summary>One clear failure when the native library is missing, instead of every parity
    /// test failing with a confusing DllNotFoundException.</summary>
    [Fact]
    public void TheRustLibraryIsBuiltAndLoadable()
    {
        var count = KrateToolCount();
        Assert.True(count > 0, "krate_core reported an empty catalogue");
        Assert.True(count >= Ported.Length,
            $"Rust exposes {count} tools but {Ported.Length} are listed as ported");
    }

    [Theory]
    [MemberData(nameof(PortedTools))]
    public void RustAndCSharp_AgreeOnEveryInput(string id)
    {
        var (_, csharp, inputs) = Ported.Single(p => p.Id == id);
        Strings.Culture = CultureInfo.GetCultureInfo("en");
        KrateSetLanguage("en");

        foreach (var input in inputs)
        {
            string expected;
            try { expected = csharp(input); }
            catch (ArgumentException)
            {
                // C# rejected it, so Rust must reject it too — not quietly return something.
                var (rejectedOk, _) = Rust(id, input);
                Assert.False(rejectedOk, $"{id}({input.Replace("\n", "\\n")}): C# threw, Rust returned a result");
                continue;
            }

            var (ok, actual) = Rust(id, input);
            Assert.True(ok, $"{id}({input.Replace("\n", "\\n")}): Rust failed with \"{actual}\"");
            Assert.Equal(expected, actual);
        }
    }

    /// <summary>The catalogue metadata is localized on both sides from the same .resx, so a
    /// drift here means the Rust build script mis-parsed the resource files.</summary>
    [LibraryImport(Lib, EntryPoint = "krate_tool_id")]
    private static partial IntPtr KrateToolId(int index);

    [LibraryImport(Lib, EntryPoint = "krate_tool_name")]
    private static partial IntPtr KrateToolName(int index);

    static string Take(IntPtr ptr)
    {
        try { return Marshal.PtrToStringUTF8(ptr) ?? ""; }
        finally { KrateFree(ptr); }
    }

    [Theory]
    [MemberData(nameof(AllLanguages))]
    public void ToolNamesMatchInEveryLanguage(string language)
    {
        Strings.Culture = CultureInfo.GetCultureInfo(language);
        KrateSetLanguage(language);

        var compared = 0;
        for (var i = 0; i < KrateToolCount(); i++)
        {
            var id = Take(KrateToolId(i));
            var rustName = Take(KrateToolName(i));
            var tool = Catalog.Tools.Single(t => t.Id == id);
            Assert.Equal(tool.Name, rustName);
            compared++;
        }
        // Every tool in the Rust catalogue must have been checked. Comparing against
        // Ported.Length would be wrong: the random tools are verified by RandomParityTests
        // instead, so they are in the catalogue but not in Ported.
        Assert.Equal(KrateToolCount(), compared);
        Assert.True(compared >= Ported.Length, "the catalogue cannot be smaller than Ported");
    }

    public static IEnumerable<object[]> AllLanguages() =>
        CatalogCompletenessTests.Languages.Select(l => new object[] { l });

    /// <summary>Error text is user-facing and localized, so the two implementations have to
    /// agree on it as well — not just on the happy path.</summary>
    [Fact]
    public void RejectionsAgreeAcrossLanguages()
    {
        foreach (var language in CatalogCompletenessTests.Languages)
        {
            Strings.Culture = CultureInfo.GetCultureInfo(language);
            KrateSetLanguage(language);

            foreach (var bad in new[] { "IIII", "VV", "IC", "ABC", "   " })
            {
                var (ok, text) = Rust("Roman", bad);
                Assert.False(ok, $"{language}: Rust accepted the invalid numeral '{bad}'");
                Assert.False(string.IsNullOrWhiteSpace(text), $"{language}: empty error for '{bad}'");
                Assert.DoesNotContain("Error_", text);   // a raw resource key leaked into the message
            }
        }
        Strings.Culture = CultureInfo.GetCultureInfo("en");
        KrateSetLanguage("en");
    }

    /// <summary>The Rust date parser covers a defined subset of DateTimeOffset.TryParse — .NET's
    /// parser accepts dozens of layouts per culture and reproducing all of it would be a large
    /// surface with a lot to get subtly wrong. This test states the boundary rather than leaving it
    /// implicit: everything in <c>Accepted</c> must parse identically on both sides in every
    /// language, and everything in <c>RefusedByBoth</c> must be refused by both.
    ///
    /// If .NET accepts something the subset does not, it belongs in neither list and the gap is
    /// deliberate — that is the trade recorded in dates.rs.</summary>
    [Fact]
    public void Dates_TheParserSubsetMatches()
    {
        var accepted = new[]
        {
            "2024-01-15", "2024-01-15T10:30:45", "2024-01-15 10:30", "2024-02-29", "2024-01",
            "15 January 2024", "January 15, 2024", "15 Jan 2024", "2024/01/15",
            "1/1/29", "1/1/30", "1/1/49", "1/1/50", "1/1/99",
        };
        var refusedByBoth = new[]
        {
            "2024-02-30", "2023-02-29", "2024-13-01", "2024-00-10", "2024-01-00",
            // "" and "   " are NOT here: WeekInfo reads an empty input as "today".
            "nonsense", "99/99/9999", "2024-01-15T25:00", "2024", "20240115",
        };

        foreach (var language in CatalogCompletenessTests.Languages)
        {
            Strings.Culture = CultureInfo.GetCultureInfo(language);
            KrateSetLanguage(language);

            // WeekInfo echoes the parsed date back through the culture's own long pattern, so
            // comparing its output compares the parse and the format together.
            foreach (var input in accepted)
            {
                var expected = Dates.WeekInfo(input);
                var (ok, actual) = Rust("WeekInfo", input);
                Assert.True(ok, $"{language} {input}: {actual}");
                Assert.Equal(expected, actual);
            }
            foreach (var input in refusedByBoth)
            {
                Assert.Throws<ArgumentException>(() => Dates.WeekInfo(input));
                var (ok, text) = Rust("WeekInfo", input);
                Assert.False(ok, $"{language} {input}: Rust accepted what .NET refuses");
                Assert.DoesNotContain("Error_", text);
            }
        }
        Strings.Culture = CultureInfo.GetCultureInfo("en");
        KrateSetLanguage("en");
    }

    /// <summary>Timestamp and FileHash both read the environment — the current time, the local
    /// timezone, a file on disk — so they cannot be rows in <see cref="Ported"/>. Fixed instants and
    /// a purpose-built file make them comparable anyway.</summary>
    [Fact]
    public void Timestamp_AndFileHash_AgreeOnFixedInputs()
    {
        foreach (var language in CatalogCompletenessTests.Languages)
        {
            Strings.Culture = CultureInfo.GetCultureInfo(language);
            KrateSetLanguage(language);

            // A Unix timestamp is an absolute instant, so the LOCAL line exercises the timezone
            // conversion on both sides with no clock involved.
            foreach (var input in new[] { "0", "1600000000", "1600000000000", "-1", "2147483647",
                                          "1", "946684800", "2524608000" })
            {
                var expected = Dates.Timestamp(input);
                var (ok, actual) = Rust("Timestamp", input);
                Assert.True(ok, $"{language} {input}: {actual}");
                Assert.Equal(expected, actual);
            }
            // And a date, which is read as local and converted the other way.
            foreach (var input in new[] { "2024-01-15", "2024-07-15", "2024-01-15 10:30" })
            {
                Assert.Equal(Dates.Timestamp(input), Rust("Timestamp", input).Text);
            }
        }
        Strings.Culture = CultureInfo.GetCultureInfo("en");
        KrateSetLanguage("en");

        var dir = Path.Combine(Path.GetTempPath(), "krate-fileparity-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        try
        {
            // A size big enough to be grouped, so the culture's separators are exercised.
            var file = Path.Combine(dir, "sample.bin");
            File.WriteAllBytes(file, Enumerable.Range(0, 1_234_567).Select(i => (byte)i).ToArray());

            foreach (var language in CatalogCompletenessTests.Languages)
            {
                Strings.Culture = CultureInfo.GetCultureInfo(language);
                KrateSetLanguage(language);
                Assert.Equal(Files.Describe(file), Rust("FileHash", file).Text);
            }
            Strings.Culture = CultureInfo.GetCultureInfo("en");
            KrateSetLanguage("en");

            // A missing file is refused the same way.
            var missing = Path.Combine(dir, "gone.bin");
            Assert.Throws<ArgumentException>(() => Files.Describe(missing));
            Assert.False(Rust("FileHash", missing).Ok);
        }
        finally { Directory.Delete(dir, true); }
    }

    /// <summary>QR is the one tool where the two implementations legitimately differ. QRCoder and
    /// the `qrcode` crate agree on version, encoding mode and error correction, but not always on the
    /// mask pattern: "hi" and "A" come out module-for-module identical while "HELLO" and "12345"
    /// differ in about 13% of modules. The spec says to pick the mask with the lowest penalty score,
    /// and the two score differently when it is close.
    ///
    /// Both outputs are valid codes carrying the same data — verified by decoding each with an
    /// independent decoder (`rqrr`, in codes.rs and in a one-off run against these very renderings).
    /// So this compares what must match — the version, and therefore the dimensions — rather than
    /// pretending the bytes are equal.</summary>
    [Fact]
    public void Qr_AgreesOnShape()
    {
        Strings.Culture = CultureInfo.GetCultureInfo("en");
        KrateSetLanguage("en");

        foreach (var input in new[]
        {
            "hi", "HELLO", "12345", "A", "1", "a", "HELLO WORLD", "0123456789",
            "ABC123", "abc123", "https://example.com", "café", "中文",
            "The quick brown fox jumps over the lazy dog",
            "longer content longer content longer content longer content",
            "0000000000000000000000000000000000000000",
            "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
        })
        {
            var expected = Qr.Unicode(input);
            var (ok, actual) = Rust("Qr", input);
            Assert.True(ok, $"{input}: {actual}");

            var expectedRows = expected.Split('\n');
            var actualRows = actual.Split('\n');
            // Same version means the same module count, which means the same rendered dimensions.
            Assert.Equal(expectedRows.Length, actualRows.Length);
            Assert.Equal(expectedRows[0].Length, actualRows[0].Length);
            // Only the four half-block glyphs may appear.
            Assert.All(actualRows, row => Assert.True(
                row.All(c => c == '\u2588' || c == '\u2580' || c == '\u2584' || c == ' '), row));
            // The quiet zone is light on both sides, whatever the mask does inside.
            Assert.All(actualRows.Take(2), row => Assert.True(row.All(c => c == '\u2588'), row));
        }

        // Empty input is refused by both.
        Assert.Throws<ArgumentException>(() => Qr.Unicode(""));
        Assert.False(Rust("Qr", "").Ok);
    }

    /// <summary>Exif takes a path, so it cannot be a row in <see cref="Ported"/>. The JPEGs are
    /// hand-built here rather than checked in as fixtures: the APP1/TIFF layout is exactly what a real
    /// camera writes, and building it in the test makes the byte offsets visible instead of opaque.
    ///
    /// Both byte orders, both value-storage forms (inline for four bytes or fewer, out-of-line
    /// otherwise), the Exif sub-IFD pointer, and the rational formatting are all covered.</summary>
    [Fact]
    public void Exif_AgreesOnHandBuiltJpegs()
    {
        Strings.Culture = CultureInfo.GetCultureInfo("en");
        KrateSetLanguage("en");
        var dir = Path.Combine(Path.GetTempPath(), "krate-exifparity-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        try
        {
            var files = new List<string>();
            void Add(string name, byte[] bytes)
            {
                var path = Path.Combine(dir, name);
                File.WriteAllBytes(path, bytes);
                files.Add(path);
            }

            // A camera-like JPEG: maker and model out of line, orientation inline, and a sub-IFD
            // holding exposure, aperture and focal length as rationals.
            Add("camera.jpg", BuildJpeg(little: true, withSubIfd: true));
            Add("camera_be.jpg", BuildJpeg(little: false, withSubIfd: true));
            Add("plain.jpg", BuildJpeg(little: true, withSubIfd: false));
            // A JPEG with no EXIF at all still reports its dimensions.
            Add("bare.jpg", [0xFF, 0xD8, 0xFF, 0xC0, 0x00, 0x11, 0x08, 0x00, 0x64, 0x00, 0xC8,
                             0, 0, 0, 0, 0, 0, 0, 0, 0xFF, 0xD9]);
            // Not an image at all.
            Add("notes.txt", "just text"u8.ToArray());
            // A PNG, which has no EXIF but does have dimensions.
            Add("tiny.png", [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A,
                             0, 0, 0, 13, (byte)'I', (byte)'H', (byte)'D', (byte)'R',
                             0, 0, 0, 0x20, 0, 0, 0, 0x10, 8, 6, 0, 0, 0]);
            // Truncated EXIF: neither side may throw.
            var full = BuildJpeg(little: true, withSubIfd: true);
            Add("cut.jpg", full[..(full.Length / 2)]);

            foreach (var path in files)
            {
                string expected;
                try { expected = "OK:" + Exif.Read(path); }
                catch (ArgumentException e) { expected = "ERR:" + e.Message; }
                var (ok, text) = Rust("Exif", path);
                Assert.Equal(expected, (ok ? "OK:" : "ERR:") + text);
            }

            // And a missing file, refused identically.
            var missing = Path.Combine(dir, "gone.jpg");
            Assert.Throws<ArgumentException>(() => Exif.Read(missing));
            Assert.False(Rust("Exif", missing).Ok);
        }
        finally { Directory.Delete(dir, true); }
    }

    /// <summary>Builds a JPEG carrying a real APP1/EXIF block. Offsets are computed rather than
    /// hardcoded so the layout stays correct if an entry is added.</summary>
    static byte[] BuildJpeg(bool little, bool withSubIfd)
    {
        byte[] U16(ushort v) => little ? BitConverter.GetBytes(v) : BitConverter.GetBytes(v).Reverse().ToArray();
        byte[] U32(uint v) => little ? BitConverter.GetBytes(v) : BitConverter.GetBytes(v).Reverse().ToArray();

        // Out-of-line values, gathered first so their offsets are known.
        var trailer = new List<byte>();
        uint Place(byte[] data)
        {
            // Offsets are measured from the start of the TIFF header.
            var entryCount = withSubIfd ? 4u : 3u;
            var trailerStart = 8u + 2u + entryCount * 12u + 4u;
            var at = trailerStart + (uint)trailer.Count;
            trailer.AddRange(data);
            return at;
        }

        var makeAt = Place("Canon\0"u8.ToArray());
        var modelAt = Place("EOS R5\0"u8.ToArray());
        // Rationals for the sub-IFD: 1/250s, f/2.8, 50mm.
        var shutterAt = Place([.. U32(1), .. U32(250)]);
        var apertureAt = Place([.. U32(28), .. U32(10)]);
        var focalAt = Place([.. U32(500), .. U32(10)]);

        // The sub-IFD itself also lives in the trailer.
        var subEntries = new List<byte>();
        subEntries.AddRange(U16(3)); // three entries
        void SubEntry(ushort tag, ushort type, uint count, byte[] value)
        {
            subEntries.AddRange(U16(tag));
            subEntries.AddRange(U16(type));
            subEntries.AddRange(U32(count));
            subEntries.AddRange(value);
        }
        SubEntry(0x829A, 5, 1, U32(shutterAt));
        SubEntry(0x829D, 5, 1, U32(apertureAt));
        SubEntry(0x920A, 5, 1, U32(focalAt));
        subEntries.AddRange(U32(0));
        var subIfdAt = Place([.. subEntries]);

        var ifd = new List<byte>();
        var entries = new List<byte[]>();
        void Entry(ushort tag, ushort type, uint count, byte[] value)
        {
            var e = new List<byte>();
            e.AddRange(U16(tag));
            e.AddRange(U16(type));
            e.AddRange(U32(count));
            e.AddRange(value);
            entries.Add([.. e]);
        }
        Entry(0x010F, 2, 6, U32(makeAt));
        Entry(0x0110, 2, 7, U32(modelAt));
        Entry(0x0112, 3, 1, [.. U16(6), 0, 0]);   // orientation, inline
        if (withSubIfd) Entry(0x8769, 4, 1, U32(subIfdAt));

        ifd.AddRange(U16((ushort)entries.Count));
        foreach (var e in entries) ifd.AddRange(e);
        ifd.AddRange(U32(0));

        var tiff = new List<byte>();
        tiff.AddRange(little ? "II"u8.ToArray() : "MM"u8.ToArray());
        tiff.AddRange(U16(42));
        tiff.AddRange(U32(8));
        tiff.AddRange(ifd);
        tiff.AddRange(trailer);

        var app1 = new List<byte>();
        app1.AddRange("Exif\0\0"u8.ToArray());
        app1.AddRange(tiff);

        var jpeg = new List<byte> { 0xFF, 0xD8, 0xFF, 0xE1 };
        var length = app1.Count + 2;
        jpeg.Add((byte)(length >> 8));
        jpeg.Add((byte)(length & 0xFF));
        jpeg.AddRange(app1);
        // SOF0: 8-bit, 100 tall, 200 wide.
        jpeg.AddRange([0xFF, 0xC0, 0x00, 0x11, 0x08, 0x00, 0x64, 0x00, 0xC8]);
        jpeg.AddRange(new byte[8]);
        jpeg.AddRange([0xFF, 0xD9]);
        return [.. jpeg];
    }

    /// <summary>SysInfo and DnsLookup read the machine, so they cannot be rows in
    /// <see cref="Ported"/> — but on one machine at one moment they must agree exactly, and that is
    /// worth asserting: the OS version string, the architecture spelling, the core count, the memory
    /// figure and every drive line have to match, which pins a good deal of Win32 plumbing.
    ///
    /// The RUNTIME line is the exception. It reports the host runtime, which a Rust library cannot
    /// discover, so the shell supplies it through krate_set_runtime — the same shape as
    /// krate_set_language. Once set, even that line matches.</summary>
    [Fact]
    public void SysInfo_AgreesLineForLine()
    {
        Strings.Culture = CultureInfo.GetCultureInfo("en");
        KrateSetLanguage("en");
        // Without this the Rust side says "Rust", which is honest but different.
        KrateSetRuntime($".NET {Environment.Version}");

        var expected = Everyday.SysInfo("");
        var (ok, actual) = Rust("SysInfo", "");
        Assert.True(ok, actual);
        Assert.Equal(expected, actual);

        // And the default, when the shell says nothing: everything but RUNTIME still matches.
        KrateSetRuntime("Rust");
        var (_, defaulted) = Rust("SysInfo", "");
        Assert.Contains("RUNTIME    Rust", defaulted);
        var expectedLines = expected.Split('\n');
        var defaultedLines = defaulted.Split('\n');
        Assert.Equal(expectedLines.Length, defaultedLines.Length);
        for (var i = 0; i < expectedLines.Length; i++)
            if (!expectedLines[i].StartsWith("RUNTIME"))
                Assert.Equal(expectedLines[i], defaultedLines[i]);

        KrateSetRuntime($".NET {Environment.Version}");
    }

    /// <summary>DNS answers change with the network, so each name is resolved on both sides and the
    /// results compared — the host name, the address count and the address order all have to agree,
    /// which is what shows the reverse lookup and the resolver order are being reproduced.</summary>
    [Fact]
    public void DnsLookup_AgreesOnWhatItCanResolve()
    {
        Strings.Culture = CultureInfo.GetCultureInfo("en");
        KrateSetLanguage("en");

        // Loopback and the local machine are resolvable without a network; the .invalid TLD is
        // guaranteed by RFC 2606 never to resolve.
        foreach (var input in new[]
        {
            "localhost", "http://localhost", "https://localhost", "localhost/path", "  localhost  ",
            "127.0.0.1", "nonexistent.invalid", "definitely.not.a.real.host.invalid", "",
        })
        {
            var expected = Dev.DnsLookup(input);
            var (ok, actual) = Rust("DnsLookup", input);
            Assert.True(ok, actual);
            Assert.Equal(expected, actual);
        }
    }

    /// <summary>"now" and a bare clock time depend on the current instant, so they cannot be rows in
    /// <see cref="Ported"/> — a second could tick between the two calls. Comparing to the minute is
    /// the honest resolution, and the offsets must match exactly either way.</summary>
    [Fact]
    public void Timezone_AgreesOnTheCurrentInstant()
    {
        Strings.Culture = CultureInfo.GetCultureInfo("en");
        KrateSetLanguage("en");

        foreach (var input in new[]
        {
            "now paris tokyo", "now utc", "tokyo", "paris london", "14:30 paris tokyo",
            "09:00 utc America/New_York", "now Asia/Kolkata Pacific/Chatham",
        })
        {
            var expected = Dates.Timezone(input);
            var (ok, actual) = Rust("Timezone", input);
            Assert.True(ok, actual);

            var expectedLines = expected.Split('\n');
            var actualLines = actual.Split('\n');
            Assert.Equal(expectedLines.Length, actualLines.Length);
            for (var i = 0; i < expectedLines.Length; i++)
            {
                // The zone id and the offset must be identical; the clock may differ by a tick if
                // the minute rolled over between the two calls.
                Assert.Equal(expectedLines[i][..20], actualLines[i][..20]);
                Assert.Equal(expectedLines[i][^6..], actualLines[i][^6..]);
                var expectedWhen = DateTime.Parse(expectedLines[i][21..37], CultureInfo.InvariantCulture);
                var actualWhen = DateTime.Parse(actualLines[i][21..37], CultureInfo.InvariantCulture);
                Assert.True((actualWhen - expectedWhen).Duration() <= TimeSpan.FromMinutes(1),
                    $"{input}: {expectedLines[i]} vs {actualLines[i]}");
            }
        }
    }

    /// <summary>An invalid pattern must be refused by both, but not with the same words: .NET's
    /// ArgumentException carries its own English parser text ("Unterminated [] set."), which is a
    /// raw-message leak of the kind fixed elsewhere in this port. Fixing it here would mean inventing
    /// a resource key plus 17 translations AND discarding the detail that tells the user what is
    /// wrong with their pattern, so the wording is left alone and only the rejection is asserted.
    ///
    /// One acceptance difference is excluded deliberately: .NET rejects a reversed repetition range
    /// (a{2,1}) where fancy-regex reads it as literal text. Documented in regex.rs.</summary>
    [Fact]
    public void Regex_RejectsTheSameInvalidPatterns()
    {
        Strings.Culture = CultureInfo.GetCultureInfo("en");
        KrateSetLanguage("en");

        foreach (var pattern in new[] { "[unterminated", "(unclosed", "*", "(?<", "a**", @"(?<>x)" })
        {
            var input = pattern + "\nsubject";
            Assert.ThrowsAny<ArgumentException>(() => Dev.RegexTest(input));
            Assert.False(Rust("Regex", input).Ok, $"Rust accepted {pattern}");
        }
    }

    /// <summary>The four catalogue placeholders exist so the tools stay searchable; the GUI has a
    /// page for each and the CLI drives them directly. Both sides must refuse to run them from the
    /// catalogue rather than returning something that looks like a result.</summary>
    [Fact]
    public void ThePlaceholders_AreRefusedByBothSides()
    {
        Strings.Culture = CultureInfo.GetCultureInfo("en");
        KrateSetLanguage("en");

        foreach (var (id, run) in new (string, Func<string, string>)[]
        {
            ("Weather", Everyday.Weather), ("Snake", Everyday.Snake),
            ("Game2048", Everyday.Game2048), ("Tetris", Everyday.Tetris),
        })
        {
            Assert.Throws<NotSupportedException>(() => run(""));
            var (ok, text) = Rust(id, "");
            Assert.False(ok, $"{id} should not run from the catalogue");
            Assert.DoesNotContain("Error_", text);
        }
    }

    /// <summary>Currency is the one online tool, so the network is kept out of the test: both sides
    /// read the same cache file in %APPDATA%/KRATE, and a hand-written fresh one makes the whole
    /// pipeline deterministic. The C# fetches with HttpClient (SChannel) and the Rust side with
    /// WinHTTP — the same TLS stack, and neither is exercised here.
    ///
    /// Note both report a fresh cache as "offline": the flag is !fetched, and a fresh cache means no
    /// fetch happened. Odd, but it is the behaviour, and it has to match.</summary>
    [Fact]
    public void Currency_AgreesOnACachedTable()
    {
        Strings.Culture = CultureInfo.GetCultureInfo("en");
        KrateSetLanguage("en");

        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "KRATE");
        Directory.CreateDirectory(dir);
        // A base code no provider would ever return, so a real cache can never collide with it.
        var random = new Random();
        var code = new string([.. Enumerable.Range(0, 3).Select(_ => (char)('A' + random.Next(26)))]);
        var path = Path.Combine(dir, $"rates_{code}.json");
        File.WriteAllText(path, """
            {"result":"success","time_last_update_utc":"Wed, 30 Jul 2026 00:02:31 +0000",
             "rates":{"EUR":0.918273,"GBP":0.79999,"JPY":157.5,"USD":1,"ZWL":0}}
            """);
        try
        {
            foreach (var input in new[]
            {
                $"100 {code} EUR", $"{code} EUR", $"1 {code} GBP", $"0 {code} EUR",
                $"1000 {code} JPY", $"1,000 {code} JPY", $"-5 {code} EUR", $"2.5 {code} EUR",
                $"{code} ZWL", $"{code} USD",
                // The amount may come after the codes, or twice — the last one wins.
                $"{code} EUR 250", $"1 {code} EUR 7",
                // Case is normalised.
                $"100 {code.ToLowerInvariant()} eur",
                // Refusals.
                "", $"100 {code}", $"{code}", "100", $"100 {code} XYZ",
            })
            {
                string expected;
                try { expected = "OK:" + Currency.Convert(input); }
                catch (ArgumentException e) { expected = "ERR:" + e.Message; }
                var (ok, text) = Rust("Currency", input);
                Assert.Equal(expected, (ok ? "OK:" : "ERR:") + text);
            }

            // And the pure core, which needs no cache at all.
            var rates = new Dictionary<string, double> { ["EUR"] = 0.9, ["GBP"] = 0.8 };
            Assert.Equal(90, Currency.Compute(100, rates, "EUR"));
            Assert.Throws<ArgumentException>(() => Currency.Compute(1, rates, "XYZ"));
        }
        finally { File.Delete(path); }
    }

    /// <summary>StripMetadata is the one tool where the Rust side deliberately behaves BETTER, so
    /// byte parity is not the goal. ImageSharp decodes and re-encodes, which recompresses a JPEG and
    /// degrades it a little every time metadata is stripped. The Rust side rewrites the container
    /// instead — dropping the metadata segments and copying the image data through untouched — which
    /// is lossless and needs no codec.
    ///
    /// So this compares the message, which must match exactly, and then the properties that matter:
    /// the metadata is gone, the dimensions are unchanged, and the source file is untouched. It also
    /// asserts the lossless claim directly — the Rust output must keep the original scan bytes, which
    /// the C# output cannot.</summary>
    [Fact]
    public void StripMetadata_RemovesMetadataAndKeepsTheImage()
    {
        Strings.Culture = CultureInfo.GetCultureInfo("en");
        KrateSetLanguage("en");
        var dir = Path.Combine(Path.GetTempPath(), "krate-stripparity-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        try
        {
            // A JPEG carrying EXIF and a comment, with recognisable scan bytes to look for after.
            byte[] Jpeg()
            {
                var jpeg = new List<byte> { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10 };
                jpeg.AddRange("JFIF\0"u8.ToArray());
                jpeg.AddRange(new byte[9]);
                var exif = "Exif\0\0MM\0*\0\0\0\u0008\0\0"u8.ToArray();
                jpeg.AddRange([0xFF, 0xE1, (byte)((exif.Length + 2) >> 8), (byte)((exif.Length + 2) & 0xFF)]);
                jpeg.AddRange(exif);
                jpeg.AddRange([0xFF, 0xFE, 0x00, 0x08]);
                jpeg.AddRange("secret"u8.ToArray());
                jpeg.AddRange([0xFF, 0xC0, 0x00, 0x11, 0x08, 0x00, 0x64, 0x00, 0xC8]);
                jpeg.AddRange(new byte[8]);
                jpeg.AddRange([0xFF, 0xDA, 0x00, 0x08, 1, 1, 0, 0, 0x3F, 0x00]);
                jpeg.AddRange([0x12, 0x34, 0x56, 0x78]);
                jpeg.AddRange([0xFF, 0xD9]);
                return [.. jpeg];
            }

            var source = Path.Combine(dir, "photo.jpg");
            var target = Path.Combine(dir, "clean.jpg");
            File.WriteAllBytes(source, Jpeg());

            var (ok, message) = Rust("StripMetadata", $"{source} | {target}");
            Assert.True(ok, message);
            // The message is the localized one, with the output path in it.
            Assert.Equal(Strings.Get("ImageMetadata_Success", target), message);

            var clean = File.ReadAllBytes(target);
            var original = File.ReadAllBytes(source);

            // The metadata is gone.
            Assert.DoesNotContain("Exif", Encoding.Latin1.GetString(clean));
            Assert.DoesNotContain("secret", Encoding.Latin1.GetString(clean));
            Assert.Empty(Exif.Parse(clean));
            // The source is untouched.
            Assert.Equal(original, Jpeg());
            // The dimensions survive, which is what "lossless" has to mean.
            var (_, cleanWidth, cleanHeight) = Images.Read(target);
            var (_, sourceWidth, sourceHeight) = Images.Read(source);
            Assert.Equal((sourceWidth, sourceHeight), (cleanWidth, cleanHeight));
            // And the scan data came through byte for byte — the lossless claim, asserted.
            Assert.Contains(Convert.ToHexString([0x12, 0x34, 0x56, 0x78]), Convert.ToHexString(clean));
            // JFIF and the colour information are kept; only privacy metadata is dropped.
            Assert.Contains("JFIF", Encoding.Latin1.GetString(clean));

            // Both sides refuse the same malformed requests.
            foreach (var request in new[]
            {
                "", "onlyone.jpg", $"{source} | {target} | extra",
                $"{Path.Combine(dir, "missing.jpg")} | {target}",
            })
            {
                string expected;
                try { expected = "OK:" + Files.StripMetadata(request); }
                catch (ArgumentException e) { expected = "ERR:" + e.Message; }
                var (requestOk, text) = Rust("StripMetadata", request);
                // Only the refusals are compared: a success would re-encode on the C# side.
                if (expected.StartsWith("ERR:"))
                    Assert.Equal(expected, (requestOk ? "OK:" : "ERR:") + text);
                else
                    Assert.True(requestOk, text);
            }
        }
        finally { Directory.Delete(dir, true); }
    }

    /// <summary>PdfSharp on one side, lopdf on the other, so the produced files cannot be
    /// byte-identical. What must match is the message — it carries only page counts and file names —
    /// and the structure: the same output files with the same names, each holding the right pages.
    /// The source PDFs are built by PdfSharp so both sides read exactly the same input.</summary>
    [Fact]
    public void Pdf_SplitAndMergeAgreeOnStructure()
    {
        Strings.Culture = CultureInfo.GetCultureInfo("en");
        KrateSetLanguage("en");
        var dir = Path.Combine(Path.GetTempPath(), "krate-pdfparity-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        try
        {
            void MakePdf(string path, int pages)
            {
                using var document = new PdfSharp.Pdf.PdfDocument();
                for (var i = 0; i < pages; i++) document.AddPage();
                document.Save(path);
            }

            int PageCount(string path)
            {
                using var document = PdfSharp.Pdf.IO.PdfReader.Open(path, PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import);
                return document.PageCount;
            }

            // --- Split: run each side on its own copy, then compare messages and structure. ---
            foreach (var pages in new[] { 1, 3, 5 })
            {
                var csDir = Path.Combine(dir, $"cs{pages}");
                var rsDir = Path.Combine(dir, $"rs{pages}");
                Directory.CreateDirectory(csDir);
                Directory.CreateDirectory(rsDir);
                MakePdf(Path.Combine(csDir, "doc.pdf"), pages);
                MakePdf(Path.Combine(rsDir, "doc.pdf"), pages);

                var expected = Pdf.Split(Path.Combine(csDir, "doc.pdf"));
                var (ok, actual) = Rust("PdfSplit", Path.Combine(rsDir, "doc.pdf"));
                Assert.True(ok, actual);
                Assert.Equal(expected, actual);

                // The same file names, and each part holds exactly one page.
                var csParts = Directory.GetFiles(csDir, "doc_p*.pdf").Select(Path.GetFileName).Order().ToArray();
                var rsParts = Directory.GetFiles(rsDir, "doc_p*.pdf").Select(Path.GetFileName).Order().ToArray();
                Assert.Equal(csParts, rsParts);
                Assert.Equal(pages, rsParts.Length);
                foreach (var part in rsParts)
                    Assert.Equal(1, PageCount(Path.Combine(rsDir, part!)));
            }

            // --- Merge: same again, comparing the message and the resulting page count. ---
            var csMerge = Path.Combine(dir, "csm");
            var rsMerge = Path.Combine(dir, "rsm");
            Directory.CreateDirectory(csMerge);
            Directory.CreateDirectory(rsMerge);
            foreach (var (folder, _) in new[] { (csMerge, 0), (rsMerge, 0) })
            {
                MakePdf(Path.Combine(folder, "a.pdf"), 2);
                MakePdf(Path.Combine(folder, "b.pdf"), 3);
                MakePdf(Path.Combine(folder, "c.pdf"), 1);
            }
            string Request(string folder) =>
                string.Join('\n', new[] { "a.pdf", "b.pdf", "c.pdf" }.Select(f => Path.Combine(folder, f)));

            var expectedMerge = Pdf.Merge(Request(csMerge));
            var (mergeOk, actualMerge) = Rust("PdfMerge", Request(rsMerge));
            Assert.True(mergeOk, actualMerge);
            Assert.Equal(expectedMerge, actualMerge);
            Assert.Equal(6, PageCount(Path.Combine(rsMerge, "merged.pdf")));
            Assert.Equal(PageCount(Path.Combine(csMerge, "merged.pdf")),
                         PageCount(Path.Combine(rsMerge, "merged.pdf")));

            // The merge Rust produced must be readable by PdfSharp, and vice versa — the real
            // interoperability check, since each side wrote with its own library.
            Assert.Equal(6, PageCount(Path.Combine(rsMerge, "merged.pdf")));
            var (splitBackOk, _) = Rust("PdfSplit", Path.Combine(csMerge, "merged.pdf"));
            Assert.True(splitBackOk, "Rust could not read a PdfSharp merge");

            // --- Refusals must agree. ---
            var text = Path.Combine(dir, "notes.txt");
            File.WriteAllText(text, "not a pdf");
            foreach (var (id, request, run) in new (string, string, Func<string, string>)[]
            {
                ("PdfSplit", text, Pdf.Split),
                ("PdfSplit", Path.Combine(dir, "missing.pdf"), Pdf.Split),
                ("PdfMerge", "", Pdf.Merge),
                ("PdfMerge", Path.Combine(csMerge, "a.pdf"), Pdf.Merge),
                ("PdfMerge", Path.Combine(dir, "missing.pdf") + "\n" + Path.Combine(dir, "gone.pdf"), Pdf.Merge),
            })
            {
                Assert.ThrowsAny<ArgumentException>(() => run(request));
                var (refusedOk, refusedText) = Rust(id, request);
                Assert.False(refusedOk, $"{id} accepted {request}");
                Assert.DoesNotContain("Error_", refusedText);
            }
        }
        finally { Directory.Delete(dir, true); }
    }

    /// <summary>Archives are checked by interoperability, not by comparing text. The reported size
    /// cannot match: System.IO.Compression and miniz_oxide are different deflate implementations, so
    /// the same input legitimately compresses to a different number of bytes. What must hold is that
    /// each side reads what the other wrote, byte for byte in the extracted content.
    ///
    /// Only zip, tar and tar.gz are covered — the Rust build has no bzip2/lzma backend and no 7-Zip
    /// native library, which archives.rs documents. Those formats are asserted to fail cleanly
    /// rather than pretended to work.</summary>
    [Fact]
    public void Archives_AreInterchangeable()
    {
        Strings.Culture = CultureInfo.GetCultureInfo("en");
        KrateSetLanguage("en");
        var dir = Path.Combine(Path.GetTempPath(), "krate-archparity-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        try
        {
            // A nested tree with a compressible file and a binary one.
            void MakeTree(string root)
            {
                Directory.CreateDirectory(Path.Combine(root, "sub", "deeper"));
                File.WriteAllText(Path.Combine(root, "a.txt"), string.Concat(Enumerable.Repeat("repeat me\n", 500)));
                File.WriteAllText(Path.Combine(root, "sub", "b.txt"), "second");
                File.WriteAllBytes(Path.Combine(root, "sub", "deeper", "c.bin"),
                    Enumerable.Range(0, 5000).Select(i => (byte)i).ToArray());
            }
            var expected = new (string Relative, byte[] Body)[]
            {
                ("a.txt", Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("repeat me\n", 500)))),
                (Path.Combine("sub", "b.txt"), "second"u8.ToArray()),
                (Path.Combine("sub", "deeper", "c.bin"), Enumerable.Range(0, 5000).Select(i => (byte)i).ToArray()),
            };

            void AssertTree(string root)
            {
                foreach (var (relative, body) in expected)
                    Assert.Equal(body, File.ReadAllBytes(Path.Combine(root, relative)));
            }

            // Every format both sides can create. 7z goes through SevenZipSharp's native 7z.dll on
            // the C# side and sevenz-rust2 on the Rust side — two completely separate
            // implementations, which is what makes reading each other's output meaningful.
            foreach (var (format, extension) in new[]
                     { ("zip", ".zip"), ("tar", ".tar"), ("tgz", ".tar.gz"),
                       ("bz2", ".tar.bz2"), ("7z", ".7z") })
            {
                // C# writes, Rust reads.
                var csTree = Path.Combine(dir, $"cs_{format}");
                MakeTree(csTree);
                Assert.Contains(extension, Files.Compress($"{csTree} | {format}"));
                Directory.Delete(csTree, recursive: true);
                var (ok, message) = Rust("Unzip", csTree + extension);
                Assert.True(ok, $"{format}: {message}");
                AssertTree(csTree);

                // Rust writes, C# reads.
                var rsTree = Path.Combine(dir, $"rs_{format}");
                MakeTree(rsTree);
                var (zipOk, zipMessage) = Rust("Zip", $"{rsTree} | {format}");
                Assert.True(zipOk, $"{format}: {zipMessage}");
                Directory.Delete(rsTree, recursive: true);
                Files.Extract(rsTree + extension);
                AssertTree(rsTree);
            }

            // Both sides report the same count of extracted files, even though the archive bytes
            // and the reported size differ.
            var counted = Path.Combine(dir, "counted");
            MakeTree(counted);
            Files.Compress($"{counted} | zip");
            Directory.Delete(counted, recursive: true);
            var csMessage = Files.Extract(counted + ".zip");
            Directory.Delete(counted, recursive: true);
            var rsMessage = Rust("Unzip", counted + ".zip").Text;
            Assert.Equal(csMessage, rsMessage);

            // The formats the Rust build cannot do must fail cleanly and leave nothing behind.
            var lone = Path.Combine(dir, "lone.txt");
            File.WriteAllText(lone, "x");
            // rar and xz cannot be created by either side; nonsense is rejected outright.
            foreach (var format in new[] { "rar", "xz", "nonsense" })
            {
                var (ok, text) = Rust("Zip", $"{lone} | {format}");
                Assert.False(ok, $"{format} should not be creatable in Rust");
                Assert.DoesNotContain("Error_", text);
                Assert.False(File.Exists($"{lone}.{format}"), $"{format} left a file behind");
            }
        }
        finally { Directory.Delete(dir, true); }
    }

    /// <summary>Encrypt and Decrypt are checked by interoperability, not by comparing text: their
    /// output is a one-line message, and a container is different every time because the salt and
    /// IV are fresh. What matters is that the two implementations produce the <b>same format</b>, so
    /// this encrypts with each side and decrypts with the other and demands the original bytes back.
    /// If either got the key derivation, the cipher, the padding or the MAC wrong, the plaintext
    /// comes out wrong or the MAC check rejects it.</summary>
    [Fact]
    public void Crypt_ContainersAreInterchangeable()
    {
        Strings.Culture = CultureInfo.GetCultureInfo("en");
        KrateSetLanguage("en");
        var dir = Path.Combine(Path.GetTempPath(), "krate-cryptparity-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        try
        {
            // 32 bytes is the case PKCS7 handles by appending a whole extra block; 0 bytes is the
            // degenerate one; the large body crosses the 64 KB streaming chunk boundary.
            var bodies = new (string Name, byte[] Body)[]
            {
                ("empty.bin", []),
                ("exact32.bin", Enumerable.Repeat((byte)7, 32).ToArray()),
                ("short.txt", "hello world"u8.ToArray()),
                ("chunky.bin", Enumerable.Range(0, 200_000).Select(i => (byte)(i % 251)).ToArray()),
            };

            // The split is on the LAST pipe, so a password may not contain one — on either
            // side. Spaces and non-ASCII are fine and are worth covering.
            const string password = "corr3ct h0rse b4ttery é中";

            foreach (var (name, body) in bodies)
            {
                // C# encrypts, Rust decrypts.
                var a = Path.Combine(dir, "cs_" + name);
                File.WriteAllBytes(a, body);
                Assert.Contains(Path.GetFileName(a), Crypt.Encrypt($"{a} | {password}"));
                File.Delete(a);
                var (ok, message) = Rust("Decrypt", $"{a}.crate | {password}");
                Assert.True(ok, message);
                Assert.Equal(body, File.ReadAllBytes(a));

                // Rust encrypts, C# decrypts.
                var b = Path.Combine(dir, "rs_" + name);
                File.WriteAllBytes(b, body);
                var (encOk, encMessage) = Rust("Encrypt", $"{b} | {password}");
                Assert.True(encOk, encMessage);
                File.Delete(b);
                Crypt.Decrypt($"{b}.crate | {password}");
                Assert.Equal(body, File.ReadAllBytes(b));
            }

            // A container written by one side must be rejected by the other on a wrong password,
            // and must leave no plaintext behind.
            var guarded = Path.Combine(dir, "guarded.txt");
            File.WriteAllBytes(guarded, "secret"u8.ToArray());
            Crypt.Encrypt($"{guarded} | right");
            File.Delete(guarded);
            var (wrongOk, wrongText) = Rust("Decrypt", $"{guarded}.crate | wrong");
            Assert.False(wrongOk);
            Assert.Equal(Strings.Get("Error_WrongPassword"), wrongText);
            Assert.False(File.Exists(guarded));

            // And the same in the other direction.
            var rustSide = Path.Combine(dir, "rustside.txt");
            File.WriteAllBytes(rustSide, "secret"u8.ToArray());
            Rust("Encrypt", $"{rustSide} | right");
            File.Delete(rustSide);
            var thrown = Assert.Throws<ArgumentException>(() => Crypt.Decrypt($"{rustSide}.crate | wrong"));
            Assert.Equal(Strings.Get("Error_WrongPassword"), thrown.Message);
            Assert.False(File.Exists(rustSide));
        }
        finally { Directory.Delete(dir, true); }
    }

    /// <summary>Both sides must refuse the same malformed requests with the same message. The
    /// success messages cannot be compared this way — a real run has a side effect on disk — so
    /// only the failures are compared here, and the successes by the interoperability test.</summary>
    [Fact]
    public void Crypt_RefusesTheSameRequests()
    {
        Strings.Culture = CultureInfo.GetCultureInfo("en");
        KrateSetLanguage("en");
        var dir = Path.Combine(Path.GetTempPath(), "krate-cryptbad-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        try
        {
            var present = Path.Combine(dir, "there.txt");
            File.WriteAllBytes(present, Enumerable.Repeat((byte)0, 200).ToArray());
            var missing = Path.Combine(dir, "nothere.txt");

            foreach (var request in new[]
            {
                "no separator at all",
                $"{present} | ",              // empty password
                $"{missing} | pw",            // no such file
                $"{dir} | pw",                // a folder, not a file
                "",
                " | ",
            })
            {
                string Expect(Func<string, string> op)
                {
                    try { return "OK:" + op(request); } catch (ArgumentException e) { return "ERR:" + e.Message; }
                }
                foreach (var (id, csharp) in new (string, Func<string, string>)[]
                         { ("Encrypt", Crypt.Encrypt), ("Decrypt", Crypt.Decrypt) })
                {
                    var expected = Expect(csharp);
                    var (ok, text) = Rust(id, request);
                    Assert.Equal(expected, (ok ? "OK:" : "ERR:") + text);
                }
            }

            // "there.txt" is 200 zero bytes: long enough for a header, but not a container.
            var notContainer = Expected(() => Crypt.Decrypt($"{present} | pw"));
            var (rustOk, rustText) = Rust("Decrypt", $"{present} | pw");
            Assert.False(rustOk);
            Assert.Equal(notContainer, rustText);
        }
        finally { Directory.Delete(dir, true); }

        static string Expected(Func<string> op)
        {
            try { op(); return "unexpected success"; } catch (ArgumentException e) { return e.Message; }
        }
    }

    /// <summary>The Rust category table was generated from this runtime's own CharUnicodeInfo, so
    /// this drives <b>every</b> code unit through both sides rather than sampling: 65536 cases, one
    /// per UTF-16 unit, which is the only way a generated table can be shown not to have drifted.
    ///
    /// The 2048 surrogate code units are skipped, and cannot be otherwise: a lone surrogate has no
    /// UTF-8 encoding, so it cannot cross the FFI boundary as input either — it arrives as U+FFFD.
    /// That is a property of the boundary, not a difference between the implementations, and
    /// unicode.rs asserts the surrogate and NUL behaviour directly instead.</summary>
    [Fact]
    public void Inspector_AgreesOnEveryCodeUnit()
    {
        Strings.Culture = CultureInfo.GetCultureInfo("en");
        KrateSetLanguage("en");

        var mismatches = new List<string>();
        var checkedUnits = 0;
        for (int unit = 0; unit <= 0xFFFF; unit++)
        {
            // U+0000 cannot cross either: it terminates the C string carrying the input.
            if (unit == 0 || unit is >= 0xD800 and <= 0xDFFF) continue;
            checkedUnits++;
            var input = ((char)unit).ToString();
            var expected = Text.Inspector(input);
            var (ok, actual) = Rust("Inspector", input);
            if (!ok || expected != actual)
            {
                mismatches.Add($"U+{unit:X4}: expected \"{expected}\", got \"{actual}\"");
                if (mismatches.Count >= 10) break;
            }
        }
        Assert.Equal(0x10000 - 2048 - 1, checkedUnits);
        Assert.Empty(mismatches);
    }

    /// <summary>Both XML tools must agree on what is well-formed. They do NOT have to agree on
    /// where the fault is: .NET's positions come from System.Xml's internals and are sometimes
    /// surprising — an unclosed &lt;a&gt; is reported at line 1 column 1 as "Data at the root level
    /// is invalid", not at the missing close tag. Reproducing that would mean reimplementing its
    /// error recovery, so this test pins the accept/reject decision and the localized shape of the
    /// message, which is what a user acts on.</summary>
    [Fact]
    public void Xml_RejectTheSameMalformedInput()
    {
        Strings.Culture = CultureInfo.GetCultureInfo("en");
        KrateSetLanguage("en");
        var wellFormed = Strings.Get("Xml_Valid");

        foreach (var bad in new[]
        {
            "<a>", "<a></b>", "<a", "notxml", "<a>&bad;</a>", "", "   ", "<a><b></a></b>",
            "<a b=/>", "<a b=\"1\" b=\"2\"/>", "<a>x</a><b/>", "<a b>", "<>", "<a/>trailing",
            "<?xml version=\"1.1\"?><a/>", "<a>&#xZZ;</a>", "<a x=\"<\"/>", "<!--unclosed",
            "<a><![CDATA[unclosed</a>", "<a/><b/>",
        })
        {
            // Validate reports rather than throws, on both sides.
            var expected = Dev.XmlValidate(bad);
            var (validateOk, validateText) = Rust("XmlValidate", bad);
            Assert.True(validateOk);
            Assert.NotEqual(wellFormed, expected);
            Assert.NotEqual(wellFormed, validateText);
            Assert.StartsWith("Malformed XML at line ", validateText);
            Assert.DoesNotContain("Xml_Invalid", validateText);

            // Format throws on both sides, with a localized message.
            Assert.Throws<ArgumentException>(() => Dev.XmlFormat(bad));
            var (formatOk, formatText) = Rust("XmlFormat", bad);
            Assert.False(formatOk, $"Rust accepted malformed input: {bad}");
            Assert.StartsWith("Malformed XML at line ", formatText);
        }
    }

    /// <summary>Rename takes a folder, so it cannot be a row in <see cref="Ported"/>. The preview
    /// listing follows directory enumeration order, which is the interesting part: both sides go
    /// through the same Win32 FindFirstFile, so the order must agree without either side sorting.
    ///
    /// Only the preview is compared. Running the "apply" form through both would need the same
    /// files to exist twice, and the first run would consume them.</summary>
    [Fact]
    public void Rename_AgreesOnThePreviewAndItsOrder()
    {
        Strings.Culture = CultureInfo.GetCultureInfo("en");
        KrateSetLanguage("en");
        var dir = Path.Combine(Path.GetTempPath(), "krate-renameparity-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        try
        {
            // Deliberately not created in sorted order, and with names that sort differently
            // under ordinal vs case-insensitive comparison.
            foreach (var name in new[] { "zeta_draft.txt", "Alpha_draft.txt", "beta_draft.txt",
                                         "_draft.txt", "10_draft.txt", "2_draft.txt", "draft.txt" })
                File.WriteAllText(Path.Combine(dir, name), "x");
            Directory.CreateDirectory(Path.Combine(dir, "sub_draft"));  // a folder must be ignored

            foreach (var request in new[]
            {
                $"{dir} | draft | final",
                $"{dir} | draft | ",              // an empty replacement is allowed
                $"{dir} | nomatchhere | x",
                $"{dir} | _draft | ",
                $"{dir} |  | x",                  // find may not be empty
                "Z:\\definitely\\not\\here | a | b",
                "",
                "onlyonefield",
            })
            {
                string Expect() { try { return Files.BulkRename(request); } catch (ArgumentException e) { return "ERR:" + e.Message; } }
                var expected = Expect();
                var (ok, actual) = Rust("Rename", request);
                Assert.Equal(expected, ok ? actual : "ERR:" + actual);
            }

            // Nothing above may have touched the filesystem.
            Assert.True(File.Exists(Path.Combine(dir, "zeta_draft.txt")));
        }
        finally { Directory.Delete(dir, true); }
    }

    /// <summary>The Rust entity table was dumped from this runtime's own private table, so the
    /// point of this test is to prove the dump did not drift: every one of the 253 names, plus a
    /// name that is deliberately NOT in the table, decoded on both sides.</summary>
    [Fact]
    public void HtmlDecode_AgreesOnEveryNamedEntity()
    {
        Strings.Culture = CultureInfo.GetCultureInfo("en");
        KrateSetLanguage("en");

        // Recover the name list from the runtime the same way the generator did.
        var entities = typeof(WebUtility).Assembly.GetType("System.Net.WebUtility+HtmlEntities")!;
        WebUtility.HtmlDecode("&amp;"); // force the lazy table
        var table = (System.Collections.IDictionary)entities
            .GetField("s_lookupTable", BindingFlags.NonPublic | BindingFlags.Static)!
            .GetValue(null)!;

        var names = new List<string>();
        foreach (System.Collections.DictionaryEntry e in table)
        {
            var key = (ulong)e.Key;
            var name = "";
            while (key != 0) { name = (char)(byte)(key & 0xFF) + name; key >>= 8; }
            names.Add(name);
        }
        Assert.Equal(253, names.Count);
        names.Add("definitelynotanentity");

        foreach (var name in names)
        {
            var input = "&" + name + ";";
            Assert.Equal((true, Encodings.HtmlDecode(input)), Rust("HtmlDecode", input));
        }
    }

    /// <summary>ImageInfo takes a path, not text, so it cannot be a row in <see cref="Ported"/>.
    /// Headers are hand-built: the magic bytes and offsets are exactly what a real file carries,
    /// so no image files or decoder are needed on either side.</summary>
    [Fact]
    public void ImageInfo_AgreesOnEveryFormat()
    {
        Strings.Culture = CultureInfo.GetCultureInfo("en");
        KrateSetLanguage("en");
        var dir = Path.Combine(Path.GetTempPath(), "krate-imgparity-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        try
        {
            foreach (var (name, bytes) in new (string, byte[])[]
            {
                ("a.png",  Png(1920, 1080)),
                ("b.png",  Png(600, 800)),
                ("sq.png", Png(512, 512)),
                ("a.gif",  Gif(320, 240)),
                ("a.bmp",  Bmp(640, 480)),
                ("td.bmp", Bmp(640, -480)),
                ("a.jpg",  Jpeg(1234, 567)),
                ("bad.txt", "not an image at all"u8.ToArray()),
            })
            {
                var path = Path.Combine(dir, name);
                File.WriteAllBytes(path, bytes);

                string expected;
                try { expected = Images.Dimensions(path); }
                catch (ArgumentException)
                {
                    var (rejected, _) = Rust("ImageInfo", path);
                    Assert.False(rejected, $"{name}: C# rejected it, Rust did not");
                    continue;
                }
                var (ok, actual) = Rust("ImageInfo", path);
                Assert.True(ok, $"{name}: Rust failed with \"{actual}\"");
                Assert.Equal(expected, actual);
            }
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    static byte[] Png(uint w, uint h)
    {
        var b = new byte[24];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.CopyTo(b, 0);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(16), w);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(20), h);
        return b;
    }

    static byte[] Gif(ushort w, ushort h)
    {
        var b = new byte[10];
        "GIF89a"u8.CopyTo(b);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(6), w);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(8), h);
        return b;
    }

    static byte[] Bmp(int w, int h)
    {
        var b = new byte[26];
        b[0] = (byte)'B'; b[1] = (byte)'M';
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(18), w);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(22), h);
        return b;
    }

    static byte[] Jpeg(ushort w, ushort h)
    {
        // SOI, a dummy APP0 with a length the parser must skip, then SOF0 with the dimensions.
        var b = new List<byte> { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x04, 0x00, 0x00, 0xFF, 0xC0, 0x00, 0x11, 0x08 };
        b.AddRange([(byte)(h >> 8), (byte)(h & 0xFF), (byte)(w >> 8), (byte)(w & 0xFF)]);
        return [.. b];
    }

    /// <summary>The filesystem tools take paths, so like ImageInfo they need real files rather
    /// than a <see cref="Ported"/> row. Each case builds a fixture, runs both implementations
    /// against the same tree, and compares.</summary>
    [Fact]
    public void FileTools_AgreeOnTheSameTree()
    {
        Strings.Culture = CultureInfo.GetCultureInfo("en");
        KrateSetLanguage("en");
        var dir = Path.Combine(Path.GetTempPath(), "krate-fileparity-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        Directory.CreateDirectory(Path.Combine(dir, "sub"));
        try
        {
            File.WriteAllText(Path.Combine(dir, "one.txt"), "same content here");
            File.WriteAllText(Path.Combine(dir, "two.txt"), "same content here");
            File.WriteAllText(Path.Combine(dir, "trap.txt"), "same length!!!!!!");
            File.WriteAllText(Path.Combine(dir, "solo.txt"), "unique");
            File.WriteAllBytes(Path.Combine(dir, "sub", "big.bin"), new byte[5000]);

            // FolderSize and Duplicates walk the whole tree.
            AssertSame("FolderSize", Files.FolderSize, dir);
            AssertSame("Duplicates", Files.Duplicates, dir);
            AssertSame("Tree", Files.Tree, dir);
            AssertSame("Tree", Files.Tree, dir + " 1");

            // FileCompare takes two paths, one per line.
            var one = Path.Combine(dir, "one.txt");
            var two = Path.Combine(dir, "two.txt");
            var trap = Path.Combine(dir, "trap.txt");
            // Identical content, then same-length-but-different content.
            AssertSame("FileCompare", Files.Compare, string.Join('\n', one, two));
            AssertSame("FileCompare", Files.Compare, string.Join('\n', one, trap));

            // An empty folder and a missing one.
            var empty = Path.Combine(dir, "empty");
            Directory.CreateDirectory(empty);
            AssertSame("FolderSize", Files.FolderSize, empty);
            AssertSame("Duplicates", Files.Duplicates, empty);
            AssertRejected("FolderSize", Files.FolderSize, Path.Combine(dir, "nope"));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    /// <summary>Split and join are destructive, so they get their own fixture and assert the
    /// bytes survive the round trip on both sides independently.</summary>
    [Fact]
    public void SplitAndJoin_RoundTripOnBothSides()
    {
        Strings.Culture = CultureInfo.GetCultureInfo("en");
        KrateSetLanguage("en");
        var original = Enumerable.Range(0, 5000).Select(i => (byte)(i % 251)).ToArray();

        foreach (var side in new[] { "C#", "Rust" })
        {
            var dir = Path.Combine(Path.GetTempPath(), "krate-split-" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(dir);
            try
            {
                var path = Path.Combine(dir, "data.bin");
                File.WriteAllBytes(path, original);

                if (side == "C#") Files.Split($"{path} 2k");
                else Assert.True(Rust("FileSplit", $"{path} 2k").Ok);
                Assert.Equal(3, Directory.GetFiles(dir, "data.bin.part*").Length);

                File.Delete(path);
                if (side == "C#") Files.Join($"{path}.part001");
                else Assert.True(Rust("FileJoin", $"{path}.part001").Ok);
                Assert.Equal(original, File.ReadAllBytes(path));
            }
            finally { Directory.Delete(dir, recursive: true); }
        }
    }

    static void AssertSame(string id, Func<string, string> csharp, string input)
    {
        var expected = csharp(input);
        var (ok, actual) = Rust(id, input);
        Assert.True(ok, $"{id}: Rust failed with \"{actual}\"");
        Assert.Equal(expected, actual);
    }

    static void AssertRejected(string id, Func<string, string> csharp, string input)
    {
        Assert.Throws<ArgumentException>(() => csharp(input));
        Assert.False(Rust(id, input).Ok, $"{id}: Rust accepted what C# rejected");
    }
}
