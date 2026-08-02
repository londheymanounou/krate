using System.Globalization;
using Krate.Core;
using Xunit;

public class TextMoreTests
{
    [Theory]
    [InlineData("helloWorldAgain")]
    [InlineData("hello_world_again")]
    [InlineData("hello-world-again")]
    [InlineData("Hello World Again")]
    [InlineData("HELLO_WORLD_AGAIN")]
    public void Naming_ReadsEveryConvention_AndEmitsThemAll(string input)
    {
        var result = Text.Naming(input);
        Assert.Contains("camelCase    helloWorldAgain", result);
        Assert.Contains("PascalCase   HelloWorldAgain", result);
        Assert.Contains("snake_case   hello_world_again", result);
        Assert.Contains("kebab-case   hello-world-again", result);
        Assert.Contains("CONSTANT     HELLO_WORLD_AGAIN", result);
    }

    [Fact]
    public void Slug_FlattensAccentsAndPunctuation() =>
        Assert.Equal("dix-idees-pour-l-ete-2026", Text.Slug("  Dix idées pour l'été 2026 !  "));

    [Fact]
    public void Clean_CollapsesSpacesAndBlankRuns() =>
        Assert.Equal("one two\n\nthree", Text.Clean("  one   two  \n\n\n  three  \n\n"));

    [Fact]
    public void Reverse_KeepsGraphemesIntact()
    {
        Assert.Equal("cba", Text.ReverseText("abc"));
        // Reversing by char would tear the accent off the e and split the emoji.
        Assert.Equal("😀été", Text.ReverseText("été😀"));
    }

    [Fact]
    public void Dedupe_KeepsFirstOccurrenceAndOrder() =>
        Assert.Equal("b\na\nc", Text.Dedupe("b\na\nb\nc\na"));

    [Fact]
    public void Morse_RoundTrips()
    {
        Assert.Equal("... --- ...", Text.Morse("SOS"));
        Assert.Equal("hello world", Text.Morse(Text.Morse("Hello World")));
        Assert.Equal(".... . .-.. .-.. --- / .-- --- .-. .-.. -..", Text.Morse("hello world"));
    }

    [Fact]
    public void FrenchTypography_AddsNoBreakSpaces_ButLeavesUrlsAlone()
    {
        var result = Text.FrenchTypography("Bonjour ! Ça va : oui ? https://example.com");
        Assert.Contains("Bonjour !", result);        // narrow no-break space before the !
        Assert.Contains("va :", result);
        Assert.DoesNotContain("Bonjour !", result);       // the ordinary space is gone
        Assert.Contains("https://example.com", result);   // but the URL colon is untouched
    }

    [Fact]
    public void Mask_HidesEmailsAndNumbers()
    {
        var result = Text.Mask("Contact: john.doe@example.com or +33 6 12 34 56 78, ref 123456789.");
        Assert.Contains("[EMAIL]", result);
        Assert.Contains("[PHONE]", result);
        Assert.DoesNotContain("john.doe", result);
        Assert.DoesNotContain("123456789", result);
    }

    [Fact]
    public void Diff_ReportsAddedAndRemovedLines()
    {
        Strings.Culture = CultureInfo.GetCultureInfo("en");
        var result = Text.Diff("one\ntwo\nthree\n---\none\ntwo bis\nthree");
        Assert.Contains("- two", result);
        Assert.Contains("+ two bis", result);
        Assert.Contains("  three", result);
        Assert.Equal("The two texts are identical.", Text.Diff("same\n---\nsame"));
        Assert.Throws<ArgumentException>(() => Text.Diff("no separator here"));
    }

    [Fact]
    public void WordFrequency_RanksByCount() =>
        Assert.StartsWith("     3  the", Text.WordFrequency("the cat the dog the bird"));

    [Fact]
    public void Lorem_ProducesTheRequestedSize()
    {
        Assert.Equal(12, Lorem("12").Split(' ').Length);
        Assert.Equal(3, Text.Lorem("3p").Split("\n\n").Length);
        static string Lorem(string s) => Text.Lorem(s);
    }
}

public class EscapeTests
{
    [Fact]
    public void Json_EscapesAndRoundTrips()
    {
        var escaped = Escapes.Json("line\"one\"\nsecond\\path");
        Assert.StartsWith("\"", escaped);
        Assert.Contains("\\n", escaped);
        Assert.Equal("line\"one\"\nsecond\\path", Escapes.JsonUnescape(escaped));
    }

    [Fact]
    public void Sql_And_Shell_QuoteTheAwkwardCharacter()
    {
        Assert.Equal("'O''Brien'", Escapes.Sql("O'Brien"));
        Assert.Equal("'it'\"'\"'s'", Escapes.Shell("it's"));
    }

    [Fact]
    public void Jwt_DecodesHeaderAndPayload_WithoutClaimingToVerify()
    {
        Strings.Culture = CultureInfo.GetCultureInfo("en");
        // Standard jwt.io sample token.
        const string token = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9." +
            "eyJzdWIiOiIxMjM0NTY3ODkwIiwibmFtZSI6IkpvaG4gRG9lIiwiaWF0IjoxNTE2MjM5MDIyfQ." +
            "SflKxwRJSMeKKF2QT4fwpMeJf36POk6yJV_adQssw5c";
        var result = Escapes.Jwt(token);
        Assert.Contains("\"HS256\"", result);
        Assert.Contains("John Doe", result);
        Assert.Contains("iat  2018-01-18", result);
        Assert.Contains("NOT verified", result);
        Assert.Throws<ArgumentException>(() => Escapes.Jwt("not-a-token"));
    }

    [Fact]
    public void Scientific_ShowsBothNotations()
    {
        var result = Escapes.Scientific("0.000123");
        Assert.Contains("SCIENTIFIC  1.23E-4", result);
        Assert.Contains("ENGINEERING 123e-6", result);
    }

    [Fact]
    public void Filename_RemovesForbiddenCharacters_AndReservedNames()
    {
        Assert.Equal("re_port_2026.txt", Escapes.Filename("re:port?2026.txt"));
        Assert.Equal("_CON.txt", Escapes.Filename("CON.txt"));   // CON is a device, not a file
        Assert.Equal("name", Escapes.Filename("name. "));
    }

    [Fact]
    public void Path_SwapsSeparatorsBothWays()
    {
        Assert.Equal("C:/Users/me", Escapes.Path(@"C:\Users\me"));
        Assert.Equal(@"home\me", Escapes.Path("home/me"));
    }

    [Fact]
    public void LineEndings_ConvertWithoutDoubling()
    {
        Assert.Equal("a\r\nb", Escapes.ToCrlf("a\nb"));
        Assert.Equal("a\r\nb", Escapes.ToCrlf("a\r\nb"));
        Assert.Equal("a\nb", Escapes.ToLf("a\r\nb"));
    }
}

public class MathsTests
{
    [Fact]
    public void Percent_AnswersAllThreeQuestions()
    {
        Strings.Culture = CultureInfo.GetCultureInfo("en");
        var result = Maths.Percent("20 150");
        Assert.Contains("20% of 150 = 30", result);
        Assert.Contains("20 is 13.3333333333% of 150", result);
        Assert.Contains("From 20 to 150: 650%", result);
    }

    [Fact]
    public void Fraction_FindsTheSimplestForm()
    {
        Assert.StartsWith("3/8", Maths.Fraction("0.375"));
        Assert.StartsWith("1/3", Maths.Fraction("0.3333333333"));
        Assert.Equal("0.375", Maths.Fraction("3/8"));
        Assert.Throws<ArgumentException>(() => Maths.Fraction("1/0"));
    }

    [Fact]
    public void Factor_DecomposesAndComputesGcdLcm()
    {
        Strings.Culture = CultureInfo.GetCultureInfo("en");
        var result = Maths.Factor("12 18");
        Assert.Contains("12 = 2 × 2 × 3", result);
        Assert.Contains("18 = 2 × 3 × 3", result);
        Assert.Contains("GCD = 6", result);
        Assert.Contains("LCM = 36", result);
        Assert.Contains("(prime)", Maths.Factor("97"));
    }

    [Fact]
    public void Statistics_UsesSampleStandardDeviation()
    {
        var result = Maths.Statistics("2 4 4 4 5 5 7 9");
        Assert.Contains("MEAN    5", result);
        Assert.Contains("MEDIAN  4.5", result);
        Assert.Contains("STDDEV  2.1380899353", result); // n-1, not n (which would give 2)
    }

    [Fact]
    public void Sequence_GeneratesKnownSeries()
    {
        Assert.Equal("0, 1, 1, 2, 3, 5, 8, 13", Maths.Sequence("fib 8"));
        Assert.Equal("2, 3, 5, 7, 11, 13", Maths.Sequence("primes 6"));
        Assert.Equal("2, 5, 8, 11", Maths.Sequence("arith 2 3 4"));
        Assert.Equal("2, 6, 18, 54", Maths.Sequence("geom 2 3 4"));
    }

    [Fact]
    public void Solve_HandlesEveryDiscriminantSign()
    {
        Strings.Culture = CultureInfo.GetCultureInfo("en");
        var two = Maths.Solve("1 -3 2");
        Assert.Contains("x₁ = 1", two);
        Assert.Contains("x₂ = 2", two);
        Assert.Contains("x = -1", Maths.Solve("1 2 1"));           // double root
        Assert.Contains("complex", Maths.Solve("1 0 1"));
        Assert.Contains("x = -2", Maths.Solve("1 2"));             // linear
    }
}

public class WordsTests
{
    [Theory]
    [InlineData(0, "zero")]
    [InlineData(21, "twenty-one")]
    [InlineData(100, "one hundred")]
    [InlineData(1000, "one thousand")]
    [InlineData(1234567, "one million two hundred thirty-four thousand five hundred sixty-seven")]
    [InlineData(-42, "minus forty-two")]
    public void English_SpellsNumbers(long value, string expected) => Assert.Equal(expected, Words.English(value));

    [Theory]
    [InlineData(0, "zéro")]
    [InlineData(21, "vingt et un")]
    [InlineData(71, "soixante et onze")]     // the "et" survives at 71
    [InlineData(80, "quatre-vingts")]        // plural when nothing follows
    [InlineData(81, "quatre-vingt-un")]      // no "et", no s
    [InlineData(91, "quatre-vingt-onze")]
    [InlineData(100, "cent")]
    [InlineData(200, "deux cents")]
    [InlineData(201, "deux cent un")]
    [InlineData(1000, "mille")]              // never "un mille"
    [InlineData(2000, "deux mille")]
    [InlineData(1000000, "un million")]
    [InlineData(2000000, "deux millions")]   // millions are nouns and take the s
    [InlineData(1234567, "un million deux cent trente-quatre mille cinq cent soixante-sept")]
    public void French_SpellsNumbers(long value, string expected) => Assert.Equal(expected, Words.French(value));

    [Fact]
    public void Spell_PicksTheLanguageFromTheInput()
    {
        Assert.Equal("quatre-vingts", Words.Spell("80 fr"));
        Assert.Equal("eighty", Words.Spell("80 en"));
        Assert.Throws<ArgumentException>(() => Words.Spell("80 de"));
    }
}

public class EverydayTests
{
    [Fact]
    public void Bmi_ComputesAndClassifies()
    {
        Strings.Culture = CultureInfo.GetCultureInfo("en");
        var result = Everyday.Bmi("70 175");
        Assert.Contains("BMI  22.86", result);
        Assert.Contains("Normal", result);
        Assert.Contains("BMI  22.86", Everyday.Bmi("70 1.75")); // metres accepted too
        Assert.Throws<ArgumentException>(() => Everyday.Bmi("70"));
    }

    [Fact]
    public void Tip_SplitsTheBill()
    {
        Strings.Culture = CultureInfo.GetCultureInfo("en");
        var result = Everyday.Tip("48.50 15 3");
        Assert.Contains("7.28", result);   // tip
        Assert.Contains("55.78", result);  // total
        Assert.Contains("18.59", result);  // each
    }

    [Fact]
    public void Loan_MatchesTheAmortisationFormula()
    {
        Strings.Culture = CultureInfo.GetCultureInfo("en");
        Assert.Contains("1001.25", Everyday.Loan("200000 3.5 25"));
        Assert.Contains("833.33", Everyday.Loan("100000 0 10")); // zero interest must not divide by zero
    }

    [Fact]
    public void Subnet_ComputesTheUsualFields()
    {
        Strings.Culture = CultureInfo.GetCultureInfo("en");
        var result = Everyday.Subnet("192.168.1.10/24");
        Assert.Contains("NETWORK    192.168.1.0/24", result);
        Assert.Contains("NETMASK    255.255.255.0", result);
        Assert.Contains("BROADCAST  192.168.1.255", result);
        Assert.Contains("HOSTS      192.168.1.1 – 192.168.1.254", result);
        Assert.Contains("USABLE     254", result);
        Assert.Contains("Private", result);
        Assert.Contains("USABLE     1", Everyday.Subnet("8.8.8.8/32"));
        Assert.Contains("Public", Everyday.Subnet("8.8.8.8/32"));
        Assert.Throws<ArgumentException>(() => Everyday.Subnet("192.168.1.10"));
    }
}

public class DevTests
{
    [Fact]
    public void RegexTest_ReportsMatchesAndGroups()
    {
        Strings.Culture = CultureInfo.GetCultureInfo("en");
        var result = Dev.RegexTest("(\\w+)@(\\w+)\\.com\nwrite to bob@example.com or eve@test.com");
        Assert.Contains("2 match(es)", result);
        Assert.Contains("bob@example.com", result);
        Assert.Contains("1 = bob", result);
        Assert.Equal("No match.", Dev.RegexTest("zzz\nnothing here"));
        // A pasted /pattern/flags form keeps working.
        Assert.Contains("1 match(es)", Dev.RegexTest("/HELLO/i\nsay hello"));
    }

    [Fact]
    public void Xml_FormatsAndValidates()
    {
        Strings.Culture = CultureInfo.GetCultureInfo("en");
        Assert.Contains("\n", Dev.XmlFormat("<a><b>1</b></a>"));
        Assert.Equal("Well-formed XML.", Dev.XmlValidate("<a><b>1</b></a>"));
        Assert.Contains("line", Dev.XmlValidate("<a><b>1</a>"));
    }

    [Fact]
    public void QueryString_SplitsParameters()
    {
        var result = Dev.QueryString("https://x.com/p?utm_source=news&q=hello+world");
        Assert.Contains("URL   https://x.com/p", result);
        Assert.Contains("utm_source  =  news", result);
        Assert.Contains("q  =  hello world", result);
    }
}
