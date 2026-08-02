using System.Globalization;
using System.Text;
using System.Xml.Linq;
using Krate.Core;
using Xunit;

public class EncodingTests
{
    [Fact]
    public void Base64_RoundTrips_NonAsciiText()
    {
        const string text = "héllo wörld — ok";
        Assert.Equal(text, Encodings.Base64Decode(Encodings.Base64Encode(text)));
    }

    [Theory]
    [InlineData("255", "0b11111111", "0o377", "255", "0xFF")]
    [InlineData("0xff", "0b11111111", "0o377", "255", "0xFF")]
    [InlineData("0b1010", "0b1010", "0o12", "10", "0xA")]
    [InlineData("-255", "-0b11111111", "-0o377", "-255", "-0xFF")]
    [InlineData("0", "0b0", "0o0", "0", "0x0")]
    public void Bases_ConvertsEveryDirection(string input, string bin, string oct, string dec, string hex)
    {
        var lines = Encodings.Bases(input).Split('\n');
        Assert.Equal($"BIN  {bin}", lines[0]);
        Assert.Equal($"OCT  {oct}", lines[1]);
        Assert.Equal($"DEC  {dec}", lines[2]);
        Assert.Equal($"HEX  {hex}", lines[3]);
    }

    [Fact]
    public void Bases_HandlesLongMinValue_WithoutOverflowing()
    {
        // Math.Abs(long.MinValue) throws; the magnitude must be computed as unsigned.
        Assert.Contains("DEC  -9223372036854775808", Encodings.Bases("-9223372036854775808"));
    }
}

public class UnitTests
{
    [Theory]
    [InlineData("10 km mi", "6.2137119224 mi")]
    [InlineData("1 GiB MiB", "1024 MiB")]
    [InlineData("100 F C", "37.7777777778 C")]
    [InlineData("0 C K", "273.15 K")]
    [InlineData("1 h min", "60 min")]
    public void Convert_ProducesKnownValues(string input, string expected) =>
        Assert.Equal(expected, Units.Convert(input));

    [Fact]
    public void Convert_RefusesAmbiguousCaseAndMismatchedDimensions()
    {
        // "Mb" could be megabit or megabyte — a silent factor-of-8 error is worse than an error message.
        Assert.Throws<ArgumentException>(() => Units.Convert("1 Mb kB"));
        Assert.Throws<ArgumentException>(() => Units.Convert("1 km kg"));
        Assert.Throws<ArgumentException>(() => Units.Convert("1 xyz m"));
        // Unambiguous casing still works.
        Assert.Equal("1000 m", Units.Convert("1 KM m"));
    }

    [Theory]
    [InlineData(1994, "MCMXCIV")]
    [InlineData(4, "IV")]
    [InlineData(3999, "MMMCMXCIX")]
    public void Roman_ConvertsBothWays(int value, string roman)
    {
        Assert.Equal(roman, Units.ToRoman(value));
        Assert.Equal(value, Units.FromRoman(roman));
    }

    [Fact]
    public void Roman_RoundTripsEveryValue_AndRejectsMalformed()
    {
        for (var i = 1; i <= 3999; i++) Assert.Equal(i, Units.FromRoman(Units.ToRoman(i)));
        Assert.Throws<ArgumentException>(() => Units.FromRoman("IIII"));
        Assert.Throws<ArgumentException>(() => Units.FromRoman("IC"));
        Assert.Throws<ArgumentException>(() => Units.ToRoman(4000));
    }
}

public class ColorTests
{
    [Fact]
    public void Parse_AcceptsEveryNotation()
    {
        var expected = (0x33, 0xAA, 0xFF);
        Assert.Equal(expected, Colors.Parse("#3af"));
        Assert.Equal(expected, Colors.Parse("#33AAFF"));
        Assert.Equal(expected, Colors.Parse("33aaff"));
        Assert.Equal(expected, Colors.Parse("rgb(51, 170, 255)"));
        // HSL with whole-degree hue can't land exactly on every RGB triple — near is correct here.
        var fromHsl = Colors.Parse("hsl(204, 100%, 60%)");
        Assert.True(Math.Abs(fromHsl.R - 51) <= 3 && Math.Abs(fromHsl.G - 170) <= 3 && Math.Abs(fromHsl.B - 255) <= 3, $"{fromHsl}");
        Assert.Throws<ArgumentException>(() => Colors.Parse("#gg"));
    }

    [Fact]
    public void RgbHsl_RoundTripsWithinOneStep()
    {
        // HSL is lossy at 8-bit; anything worse than ±1 per channel is a real bug.
        for (var i = 0; i < 512; i++)
        {
            var c = ((i * 7919) % 256, (i * 104729) % 256, (i * 1299709) % 256);
            var (h, s, l) = Colors.ToHsl(c);
            var back = Colors.FromHsl(h, s, l);
            Assert.True(Math.Abs(back.R - c.Item1) <= 1 && Math.Abs(back.G - c.Item2) <= 1 && Math.Abs(back.B - c.Item3) <= 1,
                $"{c} -> hsl({h},{s},{l}) -> {back}");
        }
    }
}

public class DateTests
{
    [Fact]
    public void Timestamp_ConvertsSecondsAndMilliseconds()
    {
        Assert.Contains("ISO    1970-01-01T00:00:00Z", Dates.Timestamp("0"));
        Assert.Contains("ISO    2001-09-09T01:46:40Z", Dates.Timestamp("1000000000"));
        Assert.Contains("ISO    2001-09-09T01:46:40Z", Dates.Timestamp("1000000000000"));
    }

    [Fact]
    public void Difference_CountsCalendarMonths_NotThirtyDayChunks()
    {
        Strings.Culture = CultureInfo.GetCultureInfo("en");
        // 31 Jan → 1 Mar is one month and one day, not "one month minus a day".
        Assert.Contains("0 years, 1 months, 1 days", Dates.Difference("2020-01-31 2020-03-01"));
        Assert.Contains("4 years, 0 months, 0 days", Dates.Difference("2020-02-29 2024-02-29"));
        Assert.Contains("Total days       30", Dates.Difference("2020-01-31 2020-03-01"));
    }

    [Fact]
    public void BusinessDays_ExcludesWeekends()
    {
        // Mon 2024-01-01 to Mon 2024-01-08 = 5 weekdays.
        Assert.Equal(5, Dates.BusinessDays(new DateTime(2024, 1, 1), new DateTime(2024, 1, 8)));
        Assert.Equal(0, Dates.BusinessDays(new DateTime(2024, 1, 6), new DateTime(2024, 1, 8)));
    }
}

public class TextTests
{
    [Fact]
    public void Count_CountsWordsLinesAndCharacters()
    {
        Strings.Culture = CultureInfo.GetCultureInfo("en");
        var result = Text.Count("hello world\nsecond line");
        Assert.Contains("Words             4", result);
        Assert.Contains("Lines             2", result);
        Assert.Contains("Characters        23", result);
    }

    [Fact]
    public void Invert_SwapsCase_AndLeavesOtherCharactersAlone() =>
        Assert.Equal("hELLO, wORLD! 42", Text.Invert("Hello, World! 42"));
}

public class JsonTests
{
    [Fact]
    public void Format_IndentsAndMinifyStrips()
    {
        Assert.Contains("\n", Json.Format("""{"a":[1,2]}"""));
        Assert.Equal("""{"a":[1,2]}""", Json.Minify("{ \"a\": [ 1, 2 ] }"));
    }

    [Fact]
    public void Validate_ReportsWhereTheErrorIs()
    {
        Strings.Culture = CultureInfo.GetCultureInfo("en");
        Assert.Equal("Valid JSON.", Json.Validate("""{"a":1}"""));
        Assert.Contains("line 1", Json.Validate("{\"a\":}"));
    }
}

public class GeneratorTests
{
    [Fact]
    public void Password_HonoursLength_AndExcludesAmbiguousCharacters()
    {
        Assert.Equal(20, Generators.Password("").Length);
        Assert.Equal(64, Generators.Password("64").Length);
        var sample = string.Concat(Enumerable.Range(0, 50).Select(_ => Generators.Password("64")));
        Assert.DoesNotContain(sample, c => c is 'l' or 'I' or 'O' or '0' or '1');
        Assert.Throws<ArgumentException>(() => Generators.Password("0"));
    }

    [Fact]
    public void Password_Options_HonourTheChosenCharacterClasses()
    {
        // Digits only → the result is all digits, at the requested length.
        var digits = Generators.Password(30, upper: false, lower: false, digits: true, symbols: false);
        Assert.Equal(30, digits.Length);
        Assert.All(digits, c => Assert.InRange(c, '2', '9')); // ambiguous 0/1 excluded

        // Lowercase only → no uppercase, digits or symbols appear.
        var lower = string.Concat(Enumerable.Range(0, 40).Select(_ => Generators.Password(30, false, true, false, false)));
        Assert.All(lower, c => Assert.InRange(c, 'a', 'z'));

        // Every class off is refused rather than returning an empty password.
        Assert.Throws<ArgumentException>(() => Generators.Password(20, false, false, false, false));
    }

    [Fact]
    public void Dice_StaysInRange_AndSumsCorrectly()
    {
        for (var i = 0; i < 200; i++)
        {
            var roll = int.Parse(Generators.Dice("d20"));
            Assert.InRange(roll, 1, 20);
        }
        var result = Generators.Dice("3d6");          // "a + b + c = total"
        var parts = result.Split(['+', '='], StringSplitOptions.TrimEntries).Select(int.Parse).ToArray();
        Assert.Equal(parts[^1], parts[..3].Sum());
        Assert.Throws<ArgumentException>(() => Generators.Dice("0d6"));
    }

    [Fact]
    public void Roll_ReturnsCountValuesInRange()
    {
        var rolls = Generators.Roll(5, 8);
        Assert.Equal(5, rolls.Length);
        Assert.All(rolls, r => Assert.InRange(r, 1, 8));
        Assert.Throws<ArgumentException>(() => Generators.Roll(0, 6));  // no dice
        Assert.Throws<ArgumentException>(() => Generators.Roll(1, 1));  // a 1-sided die
    }

    [Fact]
    public void RandomNumber_StaysInRange_Inclusive()
    {
        var seen = new HashSet<int>();
        for (var i = 0; i < 200; i++) seen.Add(int.Parse(Generators.RandomNumber("1 3")));
        Assert.Equal([1, 2, 3], seen.Order());
        Assert.Equal("5", Generators.RandomNumber("5 5"));
    }

    [Fact]
    public void Shuffle_KeepsEveryItem_AndPickReturnsOneOfThem()
    {
        const string list = "a,b,c,d,e";
        Assert.Equal(["a", "b", "c", "d", "e"], Generators.Shuffle(list).Split('\n').Order());
        Assert.Contains(Generators.Pick(list), list.Split(','));
        Assert.Throws<ArgumentException>(() => Generators.Pick("   "));
    }

    [Fact]
    public void Uuid_GeneratesTheRequestedCount_AndTheyAreDistinct()
    {
        var ids = Generators.Uuid("100").Split('\n');
        Assert.Equal(100, ids.Length);
        Assert.Equal(100, ids.Distinct().Count());
        Assert.All(ids, id => Assert.True(Guid.TryParse(id, out _)));
    }
}

public class CatalogCompletenessTests
{
    public static readonly string[] Languages =
        ["en", "fr", "de", "es", "it", "nl", "pl", "pt-BR", "ru", "tr", "hi", "id", "ja", "ko", "vi", "zh-CN", "zh-TW"];

    public static IEnumerable<object[]> AllLanguages() => Languages.Select(l => new object[] { l });

    /// <summary>Strings.Get echoes the key back when a resource is missing, so this catches
    /// any tool shipped without its strings in any of the shipped languages.</summary>
    [Theory]
    [MemberData(nameof(AllLanguages))]
    public void EveryTool_IsFullyLocalised(string language)
    {
        Strings.Culture = CultureInfo.GetCultureInfo(language);
        foreach (var tool in Catalog.Tools)
        {
            Assert.NotEqual($"Tool_{tool.Id}_Name", tool.Name);
            Assert.NotEqual($"Tool_{tool.Id}_Desc", tool.Description);
            Assert.NotEqual($"Category_{tool.Category}", tool.CategoryName);
            Assert.NotEmpty(tool.Aliases);
        }
    }

    [Fact]
    public void ToolIds_AreUnique() =>
        Assert.Equal(Catalog.Tools.Count, Catalog.Tools.Select(t => t.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());

    // cp1252 characters for bytes 0x80-0x9F; 0x00-0x7F and 0xA0-0xFF match Unicode code points.
    const string Cp1252High =
        "€‚ƒ„…†‡ˆ‰Š‹ŒŽ" +
        "‘’“”•–—˜™š›œžŸ";

    /// <summary>True when text is UTF-8 that was decoded as cp1252 ("entrée" arriving as "entrÃ©e").
    /// Every character must map back to a single byte and those bytes must be valid UTF-8 that
    /// differs from the input — real translated text fails one of those and is left alone.</summary>
    static bool IsMojibake(string value)
    {
        var bytes = new byte[value.Length];
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            var high = Cp1252High.IndexOf(c);
            if (c <= 0x7F || c is >= (char)0xA0 and <= (char)0xFF) bytes[i] = (byte)c;
            else if (high >= 0) bytes[i] = (byte)(0x80 + high);
            else return false;                      // a genuine non-Latin character
        }
        try { return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes) != value; }
        catch (ArgumentException) { return false; } // not valid UTF-8, so not double-encoded
    }

    /// <summary>A PowerShell fixup script once rewrote every .resx through the ANSI codepage and
    /// silently double-encoded 5331 strings in all 17 languages. Nothing failed — the apps just
    /// displayed "Ð¤Ð°Ð¹Ð»". Read the files directly: ResourceManager would hide gaps behind fallback.</summary>
    [Fact]
    public void Resources_AreNotDoubleEncoded()
    {
        var dir = Path.Combine(FindRepoRoot(), "src", "KRATE.Core", "Resources");
        var corrupted = new List<string>();
        foreach (var file in Directory.GetFiles(dir, "Strings*.resx"))
            foreach (var data in XDocument.Load(file).Root!.Elements("data"))
            {
                var value = data.Element("value")?.Value ?? "";
                if (IsMojibake(value))
                    corrupted.Add($"{Path.GetFileName(file)}:{data.Attribute("name")?.Value} = {value}");
            }
        Assert.Empty(corrupted);
    }

    /// <summary>MSBuild drops duplicate resource names with only a warning, so the second
    /// translation of a key silently never ships.</summary>
    [Fact]
    public void Resources_HaveNoDuplicateKeys()
    {
        var dir = Path.Combine(FindRepoRoot(), "src", "KRATE.Core", "Resources");
        foreach (var file in Directory.GetFiles(dir, "Strings*.resx"))
        {
            var names = XDocument.Load(file).Root!.Elements("data").Select(d => d.Attribute("name")!.Value).ToList();
            Assert.Equal(names.Count, names.Distinct().Count());
        }
    }

    static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "KRATE.sln")))
            dir = Path.GetDirectoryName(dir);
        return dir ?? throw new DirectoryNotFoundException("KRATE.sln not found above " + AppContext.BaseDirectory);
    }
}
