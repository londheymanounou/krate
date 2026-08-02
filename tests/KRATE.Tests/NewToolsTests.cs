using System.Globalization;
using Krate.Core;
using Xunit;

// Covers the tools added in the "complete & polish" pass: timezone, cards, teams, CSS minify,
// system info, JSON→YAML.
public class NewToolsTests
{
    public NewToolsTests() => Strings.Culture = CultureInfo.GetCultureInfo("en");

    [Fact]
    public void Timezone_ConvertsAClockTimeAcrossZones()
    {
        // UTC and Tokyo never observe DST relative to each other: 12:00 UTC is always 21:00 (+09:00).
        var r = Dates.Timezone("12:00 utc tokyo");
        Assert.Contains("12:00 +00:00", r);
        Assert.Contains("21:00 +09:00", r);
        Assert.Throws<ArgumentException>(() => Dates.Timezone("12:00 notazone"));
    }

    [Fact]
    public void Cards_DrawsDistinctCards()
    {
        var hand = Generators.Cards("5").Split(' ');
        Assert.Equal(5, hand.Length);
        Assert.Equal(5, hand.Distinct().Count());       // no card drawn twice
        Assert.Single(Generators.Cards("").Split(' '));  // default one card
        Assert.Throws<ArgumentException>(() => Generators.Cards("53")); // only 52 in a deck
    }

    [Fact]
    public void Teams_SplitsEveryoneIntoTheRequestedNumberOfGroups()
    {
        var r = Generators.Teams("3; alice, bob, carol, dave, eve, frank");
        Assert.Equal(3, r.Split('\n').Length);
        foreach (var name in new[] { "alice", "bob", "carol", "dave", "eve", "frank" })
            Assert.Contains(name, r);
        Assert.Throws<ArgumentException>(() => Generators.Teams("3"));   // a count but no names
    }

    [Fact]
    public void CssMinify_StripsCommentsAndWhitespace()
    {
        var min = Css.Minify("a {\n  color: red;\n}\n/* a comment */\nb { margin : 0 ; }");
        Assert.Equal("a{color:red}b{margin:0}", min);
        Assert.Throws<ArgumentException>(() => Css.Minify("   "));
    }

    [Fact]
    public void SysInfo_ReportsTheMachine()
    {
        var r = Everyday.SysInfo("");
        Assert.Contains("OS", r);
        Assert.Contains("CPU CORES", r);
        Assert.Contains("MEMORY", r);
    }

    [Fact]
    public void JsonToYaml_EmitsBlockStyle()
    {
        var y = Data.JsonToYaml("""{"name":"Bob","age":30,"tags":["a","b"],"nested":{"x":1}}""");
        Assert.Contains("name: Bob", y);
        Assert.Contains("age: 30", y);
        Assert.Contains("tags:", y);
        Assert.Contains("  - a", y);
        Assert.Contains("nested:", y);
        Assert.Contains("  x: 1", y);
    }

    [Fact]
    public void Duration_ParsesFlexibleInputAndTotalsEveryUnit()
    {
        // Bare seconds still work (Physics/Transfer rely on the first line staying the compact breakdown).
        var r = Dates.Duration("90000");
        Assert.StartsWith("1d 1h 0m 0s", r);
        Assert.Contains("P1DT1H", r);
        Assert.Matches(@"HOURS\s+25\b", r);
        Assert.Matches(@"MINUTES\s+1500\b", r);

        // Unit tokens sum up; 1d 2h 30m = 95400 s.
        Assert.Equal(95400, Dates.ParseDuration("1d 2h 30m"));
        Assert.Equal(5400, Dates.ParseDuration("1.5h"));
        Assert.Equal(0.5, Dates.ParseDuration("500ms"));
        Assert.Equal(9000, Dates.ParseDuration("2:30:00")); // h:m:s
        Assert.Equal(150, Dates.ParseDuration("2:30"));      // m:s

        // Any-unit-to-any-unit, and a light-year is one year of light-travel time.
        Assert.Equal("2 hours = 7200 seconds", Dates.Duration("2 h s"));
        Assert.Equal(365.25, Dates.ConvertUnits(1, "ly", "day"));

        Assert.Throws<ArgumentException>(() => Dates.ParseDuration("5 parsecs"));
    }

    [Fact]
    public void JsonToYaml_QuotesAmbiguousScalars()
    {
        // A string that looks like a number/bool must be quoted so YAML keeps it a string.
        var y = Data.JsonToYaml("""{"zip":"007","flag":"true"}""");
        Assert.Contains("zip: \"007\"", y);
        Assert.Contains("flag: \"true\"", y);
    }

    [Fact]
    public void UrlParse_ExtractsComponentsAndQueryParams()
    {
        var result = Dev.UrlParse("https://example.com:8080/path/to/page?foo=bar&baz=qux%20123#frag");
        Assert.Contains("Scheme: https", result);
        Assert.Contains("Host:   example.com", result);
        Assert.Contains("Port:   8080", result);
        Assert.Contains("Path:   /path/to/page", result);
        Assert.Contains("Query Parameters:", result);
        Assert.Contains("- foo: bar", result);
        Assert.Contains("- baz: qux 123", result);
        Assert.Contains("Fragment: #frag", result);
    }

    [Fact]
    public void Chmod_ConvertsOctalToSymbolic()
    {
        Assert.Equal("rwxr-xr-x", Dev.Chmod("755"));
        Assert.Equal("rw-r--r--", Dev.Chmod("644"));
        Assert.Equal("suid rwxr-xr-x", Dev.Chmod("4755"));    }

    [Fact]
    public void Chmod_ConvertsSymbolicToOctal()
    {
        Assert.Equal("755", Dev.Chmod("rwxr-xr-x"));
        Assert.Equal("644", Dev.Chmod("rw-r--r--"));
        Assert.Throws<ArgumentException>(() => Dev.Chmod("invalid"));
    }

    [Fact]
    public void HttpStatus_ReturnsCorrectDescription()
    {
        Assert.Equal("200 OK", Dev.HttpStatus("200"));
        Assert.Equal("404 Not Found", Dev.HttpStatus("404"));
        Assert.Equal("500 Internal Server Error", Dev.HttpStatus("500"));
        Assert.Equal("Unknown HTTP Status Code", Dev.HttpStatus("999"));
        Assert.Throws<ArgumentException>(() => Dev.HttpStatus("not a number"));
    }
}
