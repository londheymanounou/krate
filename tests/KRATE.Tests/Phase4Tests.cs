using System.Globalization;
using Krate.Core;
using Xunit;

public class DataTests
{
    public DataTests() => Strings.Culture = CultureInfo.GetCultureInfo("en");

    [Fact]
    public void Csv_To_Json_TypesNumbersAndBooleans()
    {
        var json = Data.CsvToJson("name,age,active\nAlice,30,true\nBob,25,false");
        Assert.Contains("\"name\": \"Alice\"", json);
        Assert.Contains("\"age\": 30", json);       // number, not "30"
        Assert.Contains("\"active\": true", json);   // boolean, not "true"
    }

    [Fact]
    public void Csv_HonoursQuotedCommasAndNewlines()
    {
        var rows = Data.ParseCsv("a,b\n\"x,y\",\"line1\nline2\"");
        Assert.Equal(["x,y", "line1\nline2"], rows[1]);
        // Zero-padded values stay strings — they are ids, not numbers.
        Assert.Contains("\"zip\": \"007\"", Data.CsvToJson("zip\n007"));
    }

    [Fact]
    public void Json_To_Csv_RoundTrips()
    {
        const string csv = "name,age\nAlice,30\nBob,25";
        var json = Data.CsvToJson(csv);
        var back = Data.JsonToCsv(json);
        Assert.Equal(csv, back);
    }

    [Fact]
    public void Json_To_Csv_QuotesAwkwardFields_AndUnionsKeys()
    {
        var csv = Data.JsonToCsv("""[{"a":"x,y","b":1},{"a":"z","c":2}]""");
        Assert.Equal("a,b,c", csv.Split('\n')[0]);          // union of keys, first-seen order
        Assert.Contains("\"x,y\"", csv);                    // comma forces quoting
        Assert.Throws<ArgumentException>(() => Data.JsonToCsv("""{"not":"array"}"""));
    }
}

public class CronTests
{
    public CronTests() => Strings.Culture = CultureInfo.GetCultureInfo("en");

    [Theory]
    [InlineData("0 9 * * *", "at 09:00")]
    [InlineData("*/15 * * * *", "every 15 minutes")]
    [InlineData("0 9 * * 1-5", "at 09:00, on Monday–Friday")]
    [InlineData("30 8 1 * *", "at 08:30, day of month 1")]
    [InlineData("0 0 1 1 *", "at 00:00, day of month 1, in January")]
    [InlineData("@daily", "at 00:00")]
    public void Describe_ReadsCommonExpressions(string cron, string expected) =>
        Assert.Equal(expected, Cron.Describe(cron));

    [Fact]
    public void Describe_RejectsWrongFieldCount() =>
        Assert.Throws<ArgumentException>(() => Cron.Describe("0 9 *"));

    [Fact]
    public void NextRuns_FindsTheUpcomingFireTimes()
    {
        // From a Wednesday, "0 9 * * 1-5" fires at 09:00 on the next weekdays.
        var from = new DateTime(2026, 7, 22, 10, 0, 0); // Wed 10:00, already past 9am
        var runs = Cron.NextRuns("0 9 * * 1-5", 3, from);
        Assert.Equal(new DateTime(2026, 7, 23, 9, 0, 0), runs[0]); // Thu
        Assert.Equal(new DateTime(2026, 7, 24, 9, 0, 0), runs[1]); // Fri
        Assert.Equal(new DateTime(2026, 7, 27, 9, 0, 0), runs[2]); // Mon (skips the weekend)

        // "*/15 * * * *" fires every 15 minutes.
        var q = Cron.NextRuns("*/15 * * * *", 2, new DateTime(2026, 1, 1, 8, 3, 0));
        Assert.Equal(new DateTime(2026, 1, 1, 8, 15, 0), q[0]);
        Assert.Equal(new DateTime(2026, 1, 1, 8, 30, 0), q[1]);
    }
}

public class MarkdownTests
{
    [Fact]
    public void Converts_HeadingsEmphasisAndCode()
    {
        var html = Markdown.ToHtml("# Title\n\nSome **bold** and *italic* and `code`.");
        Assert.Contains("<h1>Title</h1>", html);
        Assert.Contains("<strong>bold</strong>", html);
        Assert.Contains("<em>italic</em>", html);
        Assert.Contains("<code>code</code>", html);
    }

    [Fact]
    public void Converts_ListsAndLinks()
    {
        var html = Markdown.ToHtml("- one\n- two\n\n1. first\n2. second\n\n[site](https://x.com)");
        Assert.Contains("<ul>", html);
        Assert.Contains("<li>one</li>", html);
        Assert.Contains("<ol>", html);
        Assert.Contains("<li>first</li>", html);
        Assert.Contains("<a href=\"https://x.com\">site</a>", html);
    }

    [Fact]
    public void Escapes_Html_ButNotInsideCodeReparsed()
    {
        // Raw < and & are escaped so the output is safe to render.
        Assert.Contains("&lt;script&gt;", Markdown.ToHtml("<script>"));
        // ** inside a code span stays literal, not turned into <strong>.
        var html = Markdown.ToHtml("`**not bold**`");
        Assert.Contains("<code>**not bold**</code>", html);
        Assert.DoesNotContain("<strong>", html);
    }

    [Fact]
    public void Converts_CodeBlocks()
    {
        var html = Markdown.ToHtml("```\nline & <tag>\n```");
        Assert.Contains("<pre><code>", html);
        Assert.Contains("line &amp; &lt;tag&gt;", html);
    }
}

public class SecurityTests
{
    public SecurityTests() => Strings.Culture = CultureInfo.GetCultureInfo("en");

    [Fact]
    public void Strength_RatesByEntropy()
    {
        Assert.Contains("very weak", Security.Strength("aaa"));
        Assert.Contains("very strong", Security.Strength("Tr0ub4dour&3xKq!Zp#mW9"));
        Assert.Throws<ArgumentException>(() => Security.Strength(""));
    }

    [Fact]
    public void Strength_PenalisesRepeatsAndSequences()
    {
        // Same length and character pool, but "abcdefgh" is a sequence and should score lower.
        var sequential = Entropy(Security.Strength("abcdefgh"));
        var random = Entropy(Security.Strength("q9mZ2wLx"));
        Assert.True(random > sequential);

        static int Entropy(string report) =>
            int.Parse(new string(report.SkipWhile(c => c != '~').Skip(1).TakeWhile(char.IsDigit).ToArray()));
    }
}

public class FancyTests
{
    public FancyTests() => Strings.Culture = CultureInfo.GetCultureInfo("en");

    static string Line(string text, string label) =>
        Fancy.Convert(text).Split('\n').First(l => l.StartsWith(label)).Split(' ', 2)[1].Trim();

    [Fact]
    public void Bold_MapsUpperAndLowerToTheirOwnCases()
    {
        // The bug this guards: a wrong lowercase base turned 'i' into a bold uppercase I.
        Assert.Equal("\U0001D407\U0001D422", Line("Hi", "Bold"));       // 𝐇𝐢 — bold H, bold i
        Assert.Equal("\U0001D41A\U0001D41B\U0001D41C", Line("abc", "Bold")); // 𝐚𝐛𝐜
    }

    [Fact]
    public void Double_And_Italic_UseRealGlyphsAtTheReservedHoles()
    {
        // Double-struck H/C/N/P/Q/R/Z live in Letterlike Symbols, not the math block.
        Assert.Equal("ℍ", Line("H", "Double"));   // ℍ, not a reserved tofu codepoint
        Assert.Equal("ℕ", Line("N", "Double"));   // ℕ
        // Italic small h is the Planck constant ℎ.
        Assert.Equal("ℎ", Line("h", "Italic"));
    }

    [Fact]
    public void Convert_LeavesUnmappedCharactersAlone()
    {
        Assert.Equal("\U0001D400 \U0001D401!", Line("A B!", "Bold"));   // 𝐀 𝐁! — space and ! untouched
        Assert.Throws<ArgumentException>(() => Fancy.Convert(""));
    }
}

public class WeekTests
{
    public WeekTests() => Strings.Culture = CultureInfo.GetCultureInfo("en");

    [Fact]
    public void WeekInfo_ComputesIsoWeekAndQuarter()
    {
        var result = Dates.WeekInfo("2026-07-22");
        Assert.Contains("2026-W30", result);
        Assert.Contains("Q3", result);
        Assert.Contains("203 / 365", result);   // 22 July is the 203rd day of 2026
    }

    [Fact]
    public void WeekInfo_HandlesIsoYearBoundary()
    {
        // 1 Jan 2021 is a Friday — ISO week 53 of 2020, not week 1 of 2021.
        Assert.Contains("2020-W53", Dates.WeekInfo("2021-01-01"));
    }
}
