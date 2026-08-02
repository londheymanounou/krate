using System.Globalization;
using Krate.Core;
using Krate.Core.Tools;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

/// <summary>Tools that shipped without any test. Digest values are the published vectors for
/// "hello"; the rest assert behaviour that would actually change if the code broke, not just
/// that a call returns something.</summary>
public class TextCoverageTests
{
    public TextCoverageTests() => Strings.Culture = CultureInfo.GetCultureInfo("en");

    [Fact]
    public void UpperAndLower_AreInvariant_NotCultureSensitive()
    {
        Assert.Equal("HELLO", Text.Upper("hello"));
        Assert.Equal("hello", Text.Lower("HELLO"));

        // The Turkish dotted-I is the classic culture trap: under tr-TR, "i".ToUpper() is "İ".
        // These use the invariant casing, so the answer must not move with the UI language.
        Strings.Culture = CultureInfo.GetCultureInfo("tr");
        Assert.Equal("I", Text.Upper("i"));
        Assert.Equal("i", Text.Lower("I"));
        Strings.Culture = CultureInfo.GetCultureInfo("en");
    }

    [Fact]
    public void Deaccent_StripsMarks_AndLeavesEverythingElseAlone()
    {
        Assert.Equal("eeacuoAE", Text.Deaccent("éèàçüôÀÉ"));
        Assert.Equal("Creme brulee", Text.Deaccent("Crème brûlée"));
        Assert.Equal("plain ascii", Text.Deaccent("plain ascii"));
        // Not a combining mark, so it must survive untouched.
        Assert.Equal("straße", Text.Deaccent("straße"));
        Assert.Equal("日本語", Text.Deaccent("日本語"));
    }

    [Fact]
    public void SortLines_OrdersAlphabetically_AndKeepsEveryLine()
    {
        Assert.Equal("apple\nbanana\ncherry", Text.SortLines("cherry\napple\nbanana"));
        // Blank lines are lines too — they must not be silently dropped.
        Assert.Equal(4, Text.SortLines("b\n\na\nc").Split('\n').Length);
    }

    [Fact]
    public void SortByLength_IsShortestFirst_AndTiesBreakOrdinally()
    {
        Assert.Equal("a\nbb\nccc\ndddd", Text.SortByLength("dddd\nbb\na\nccc"));
        Assert.Equal("aa\nbb\ncc", Text.SortByLength("cc\naa\nbb"));
    }

    [Fact]
    public void ReverseLines_FlipsOrderWithoutTouchingContent()
    {
        Assert.Equal("c\nb\na", Text.ReverseLines("a\nb\nc"));
        Assert.Equal("one", Text.ReverseLines("one"));
        // Reversing twice is the identity.
        const string text = "alpha\nbeta\ngamma";
        Assert.Equal(text, Text.ReverseLines(Text.ReverseLines(text)));
    }

    [Fact]
    public void Inspector_ReportsOnlyTheCharactersYouCannotSee()
    {
        Assert.Equal("All basic ASCII characters.", Text.Inspector("hello"));

        var accented = Text.Inspector("café");
        Assert.Contains("U+00E9", accented);          // é
        Assert.DoesNotContain("U+0063", accented);    // plain 'c' is not worth reporting

        // A non-breaking space is exactly the bug this tool exists to find. The literal below
        // really does contain U+00A0, not a space — do not "clean up" the whitespace here.
        Assert.Contains("U+00A0", Text.Inspector("a b"));
        Assert.Contains("U+0009", Text.Inspector("a\tb"));
    }

    [Fact]
    public void CaseConverter_ProducesEveryConvention()
    {
        var result = Text.CaseConverter("hello world");
        Assert.Contains("camelCase:      helloWorld", result);
        Assert.Contains("PascalCase:     HelloWorld", result);
        Assert.Contains("snake_case:     hello_world", result);
        Assert.Contains("kebab-case:     hello-world", result);
        Assert.Contains("SCREAMING_SNAKE:HELLO_WORLD", result);

        // It has to read the existing convention too, not just plain words.
        Assert.Contains("camelCase:      userId", Text.CaseConverter("user_id"));
        Assert.Contains("snake_case:     user_name", Text.CaseConverter("userName"));
    }
}

public class EncodingCoverageTests
{
    public EncodingCoverageTests() => Strings.Culture = CultureInfo.GetCultureInfo("en");

    [Fact]
    public void UrlEncode_EscapesTheCharactersThatBreakAQueryString()
    {
        Assert.Equal("a%20b", Encodings.UrlEncode("a b"));
        Assert.Equal("a%26b%3Dc", Encodings.UrlEncode("a&b=c"));
        Assert.Equal("caf%C3%A9", Encodings.UrlEncode("café"));   // UTF-8 bytes, percent-encoded
    }

    [Fact]
    public void UrlDecode_ReversesEncode_IncludingNonAscii()
    {
        Assert.Equal("a b", Encodings.UrlDecode("a%20b"));
        foreach (var s in new[] { "a b&c=d", "café", "100% sure", "a/b?c#d" })
            Assert.Equal(s, Encodings.UrlDecode(Encodings.UrlEncode(s)));
    }

    [Fact]
    public void HtmlEncode_NeutralisesMarkup()
    {
        Assert.Equal("&lt;script&gt;", Encodings.HtmlEncode("<script>"));
        Assert.Equal("a &amp; b", Encodings.HtmlEncode("a & b"));
        Assert.Equal("&quot;quoted&quot;", Encodings.HtmlEncode("\"quoted\""));
    }

    [Fact]
    public void HtmlDecode_ReversesEncode()
    {
        Assert.Equal("<b>", Encodings.HtmlDecode("&lt;b&gt;"));
        Assert.Equal("a & b", Encodings.HtmlDecode("a &amp; b"));
        foreach (var s in new[] { "<p>hi & bye</p>", "\"x\" > 'y'", "plain" })
            Assert.Equal(s, Encodings.HtmlDecode(Encodings.HtmlEncode(s)));
    }
}

public class HashingCoverageTests
{
    public HashingCoverageTests() => Strings.Culture = CultureInfo.GetCultureInfo("en");

    // Published test vectors — a hash tool that quietly changes output is the worst kind of broken.
    const string HelloMd5 = "5d41402abc4b2a76b9719d911017c592";
    const string HelloSha1 = "aaf4c61ddcc5e8a2dabede0f3b482cd9aea9434d";
    const string HelloSha256 = "2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824";
    const string HelloSha512 =
        "9b71d224bd62f3785d96d46ad3ea3d73319bfbc2890caadae2dff72519673ca7" +
        "2323c3d99ba5c11d7c7acc6e14b8c5da0c4663475c2e5c3adef46f73bcdec043";

    [Fact]
    public void Md5_MatchesTheKnownVector() => Assert.Equal(HelloMd5, Hashing.Md5("hello"));

    [Fact]
    public void Sha1_MatchesTheKnownVector() => Assert.Equal(HelloSha1, Hashing.Sha1("hello"));

    [Fact]
    public void Sha512_MatchesTheKnownVector() => Assert.Equal(HelloSha512, Hashing.Sha512("hello"));

    [Fact]
    public void EmptyInput_StillHashes()
    {
        Assert.Equal("d41d8cd98f00b204e9800998ecf8427e", Hashing.Md5(""));
        Assert.Equal("da39a3ee5e6b4b0d3255bfef95601890afd80709", Hashing.Sha1(""));
    }

    [Fact]
    public void Hashing_IsUtf8Based_NotUtf16()
    {
        // "café" as UTF-8 is 5 bytes; hashing UTF-16 would give a different digest entirely.
        Assert.Equal("07117fe4a1ebd544965dc19573183da2", Hashing.Md5("café"));
    }

    [Fact]
    public void All_ListsEveryDigest_LabelledAndLowercase()
    {
        var lines = Hashing.All("hello").Split('\n');
        Assert.Equal(4, lines.Length);
        Assert.Equal($"MD5      {HelloMd5}", lines[0]);
        Assert.Equal($"SHA-1    {HelloSha1}", lines[1]);
        Assert.Equal($"SHA-256  {HelloSha256}", lines[2]);
        Assert.Equal($"SHA-512  {HelloSha512}", lines[3]);
    }
}

public class MiscCoverageTests
{
    public MiscCoverageTests() => Strings.Culture = CultureInfo.GetCultureInfo("en");

    [Fact]
    public void Color_DescribesOneColourInEveryNotation()
    {
        var red = Colors.Describe(0xFF0000);
        Assert.Contains("HEX  #FF0000", red);
        Assert.Contains("RGB  rgb(255, 0, 0)", red);
        Assert.Contains("HSL  hsl(0, 100%, 50%)", red);

        // Grey has no hue; the code reports 0 rather than NaN.
        Assert.Contains("HSL  hsl(0, 0%, 50%)", Colors.Describe(0x808080));
        Assert.Contains("HEX  #FFFFFF", Colors.Describe(0xFFFFFF));
    }

    [Fact]
    public void Roman_ConvertsBothWays_IncludingSubtractiveForms()
    {
        Assert.Equal("MCMXCIV", Units.Roman("1994"));
        Assert.Equal("IV", Units.Roman("4"));
        Assert.Equal("XL", Units.Roman("40"));
        Assert.Equal("1994", Units.Roman("MCMXCIV"));
        Assert.Equal("4", Units.Roman("iv"));            // case-insensitive
        Assert.Throws<ArgumentException>(() => Units.Roman("   "));
    }

    [Fact]
    public void Coin_OnlyEverLandsHeadsOrTails_AndDoesBoth()
    {
        var seen = new HashSet<string>();
        for (var i = 0; i < 200; i++) seen.Add(Generators.Coin(""));
        Assert.Equal(["Heads", "Tails"], seen.Order());
    }

    [Fact]
    public void RandomColor_AlwaysProducesAValidColour()
    {
        for (var i = 0; i < 50; i++)
        {
            var hex = Generators.RandomColor("").Split('\n')[0];
            Assert.StartsWith("HEX  #", hex);
            Assert.Equal(6, hex["HEX  #".Length..].Length);
            Assert.True(int.TryParse(hex["HEX  #".Length..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _));
        }
    }
}

public class DevCoverageTests
{
    public DevCoverageTests() => Strings.Culture = CultureInfo.GetCultureInfo("en");

    [Fact]
    public void PortLookup_ResolvesBothDirections()
    {
        Assert.Equal("443 -> HTTPS", Dev.PortLookup("443"));
        Assert.Equal("22 -> SSH", Dev.PortLookup(" 22 "));
        Assert.Equal("MySQL -> Port 3306", Dev.PortLookup("MySQL"));
        Assert.Equal("mysql -> Port 3306", Dev.PortLookup("mysql"));   // case-insensitive
        Assert.Equal("Unknown port or service", Dev.PortLookup("9999"));
    }

    [Fact]
    public void MimeType_MapsExtensions_WithOrWithoutTheDot()
    {
        Assert.Equal("png -> image/png", Dev.MimeTypeLookup("png"));
        Assert.Equal("jpg -> image/jpeg", Dev.MimeTypeLookup(".JPG"));
        Assert.Equal("json -> application/json", Dev.MimeTypeLookup("json"));
        Assert.Equal("Unknown MIME type", Dev.MimeTypeLookup("xyz"));
    }

    /// <summary>Uses the reserved .invalid TLD (RFC 2606), which is guaranteed never to resolve,
    /// so this exercises the failure path without depending on the network being up.</summary>
    [Fact]
    public void DnsLookup_StripsTheScheme_AndReportsFailureInsteadOfThrowing()
    {
        var result = Dev.DnsLookup("https://no-such-host.invalid/some/path");
        Assert.Contains("no-such-host.invalid", result);
        Assert.DoesNotContain("https://", result);
        Assert.DoesNotContain("/some/path", result);
    }

    [Fact]
    public void CurlToCode_ReadsTheMethodAndUrl()
    {
        var get = Dev.CurlToCode("curl https://api.example.com/users");
        Assert.Contains("HttpMethod.GET", get);
        Assert.Contains("https://api.example.com/users", get);

        Assert.Contains("HttpMethod.POST", Dev.CurlToCode("curl -X POST https://api.example.com/users"));
        Assert.Contains("HttpMethod.DELETE", Dev.CurlToCode("curl -X delete https://api.example.com/u/1"));

        Assert.Throws<ArgumentException>(() => Dev.CurlToCode("wget https://example.com"));
        Assert.Equal("Could not parse curl command.", Dev.CurlToCode("curl not-a-url"));
    }

    [Fact]
    public void EnvVars_ListsTheProcessEnvironment_AndExpandsPath()
    {
        Environment.SetEnvironmentVariable("KRATE_TEST_VAR", "sentinel-value");
        try
        {
            var result = Dev.EnvVars("");
            Assert.Contains("KRATE_TEST_VAR: sentinel-value", result);
            // PATH is split one entry per line rather than left as an unreadable single line.
            Assert.Contains("PATH:", result, StringComparison.OrdinalIgnoreCase);
        }
        finally { Environment.SetEnvironmentVariable("KRATE_TEST_VAR", null); }
    }
}

public class StripMetadataTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "krate-meta-" + Guid.NewGuid().ToString("N")[..8]);

    public StripMetadataTests()
    {
        Strings.Culture = CultureInfo.GetCultureInfo("en");
        Directory.CreateDirectory(_dir);
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void StripMetadata_RemovesExif_AndKeepsThePixels()
    {
        var src = Path.Combine(_dir, "in.jpg");
        var dst = Path.Combine(_dir, "out.jpg");

        using (var image = new Image<Rgba32>(8, 4))
        {
            image.Metadata.ExifProfile = new ExifProfile();
            image.Metadata.ExifProfile.SetValue(ExifTag.Copyright, "secret location");
            image.Save(src);
        }
        using (var original = Image.Load(src)) Assert.NotNull(original.Metadata.ExifProfile);

        Files.StripMetadata($"{src}|{dst}");

        using var stripped = Image.Load(dst);
        Assert.Null(stripped.Metadata.ExifProfile);
        Assert.Equal(8, stripped.Width);        // the image itself survives
        Assert.Equal(4, stripped.Height);
    }

    [Fact]
    public void StripMetadata_NeedsBothAnInputAndAnOutput() =>
        Assert.Throws<ArgumentException>(() => Files.StripMetadata(Path.Combine(_dir, "only-one.jpg")));

    /// <summary>An ArgumentException, like every other tool that takes a path. This used to be a
    /// FileNotFoundException with its own resource key — the only place in the codebase that did
    /// either — which meant the shells' ArgumentException handler missed it and the user saw an
    /// unhandled exception instead of a message.</summary>
    [Fact]
    public void StripMetadata_ReportsAMissingFileRatherThanCrashing() =>
        Assert.Throws<ArgumentException>(() =>
            ImageMetadata.StripMetadata(Path.Combine(_dir, "nope.png"), Path.Combine(_dir, "out.png")));
}
