using System.Globalization;
using Krate.Core;
using Xunit;

public class DevMoreTests
{
    public DevMoreTests() => Strings.Culture = CultureInfo.GetCultureInfo("en");

    [Fact]
    public void Gitignore_CombinesNamedTemplates()
    {
        var result = Dev.Gitignore("node, python");
        Assert.Contains("# node", result);
        Assert.Contains("node_modules/", result);
        Assert.Contains("# python", result);
        Assert.Contains("__pycache__/", result);
    }

    [Fact]
    public void Gitignore_ReportsUnknownNames()
    {
        // A partial match still works; all-unknown errors with the list of what's available.
        Assert.Contains("/target/", Dev.Gitignore("rust cobol"));
        Assert.Throws<ArgumentException>(() => Dev.Gitignore("cobol fortran"));
    }

    [Fact]
    public void HexDump_FormatsOffsetHexAndAscii()
    {
        var dump = Dev.HexDump("Hello");
        // 48 65 6c 6c 6f = "Hello"
        Assert.StartsWith("00000000  48 65 6c 6c 6f", dump);
        Assert.Contains("|Hello|", dump);
    }

    [Fact]
    public void HexDump_ReplacesNonPrintableWithDot()
    {
        var dump = Dev.HexDump("A\tB");     // tab is non-printable
        Assert.Contains("41 09 42", dump);
        Assert.Contains("|A.B|", dump);
    }

    [Fact]
    public void HexDump_WrapsAtSixteenBytes()
    {
        var dump = Dev.HexDump(new string('x', 20));
        var lines = dump.Split('\n');
        Assert.Equal(2, lines.Length);
        Assert.StartsWith("00000010", lines[1]);   // second row starts at offset 16
    }
}

public class TocTests
{
    public TocTests() => Strings.Culture = CultureInfo.GetCultureInfo("en");

    [Fact]
    public void Toc_BuildsNestedLinks()
    {
        var toc = Text.Toc("# Title\n## Section A\n## Section B\n### Deep");
        Assert.Contains("- [Title](#title)", toc);
        Assert.Contains("  - [Section A](#section-a)", toc);       // level 2 indented
        Assert.Contains("    - [Deep](#deep)", toc);              // level 3 indented further
    }

    [Fact]
    public void Toc_DisambiguatesDuplicateHeadings()
    {
        var toc = Text.Toc("# Notes\n# Notes");
        Assert.Contains("(#notes)", toc);
        Assert.Contains("(#notes-1)", toc);       // GitHub suffixes the repeat
    }

    [Fact]
    public void Toc_IgnoresHashesInCodeBlocks()
    {
        var toc = Text.Toc("# Real\n```\n# not a heading\n```");
        Assert.Contains("[Real]", toc);
        Assert.DoesNotContain("not a heading", toc);
    }

    [Fact]
    public void Toc_ReportsWhenThereAreNoHeadings() =>
        Assert.Equal("No Markdown headings found.", Text.Toc("just a paragraph"));
}

public class MarkdownTableTests
{
    public MarkdownTableTests() => Strings.Culture = CultureInfo.GetCultureInfo("en");

    [Fact]
    public void Builds_AlignedTableFromCsv()
    {
        var table = Text.MarkdownTable("name,age\nAlice,30\nBob,5");
        var lines = table.Split('\n');
        Assert.Equal("| name  | age |", lines[0]);        // columns padded to the widest cell
        Assert.Equal("| ----- | --- |", lines[1]);        // separator row
        Assert.Equal("| Alice | 30  |", lines[2]);
    }

    [Fact]
    public void Prefers_TabDelimiterWhenPresent()
    {
        // Commas inside values survive when the data is tab-separated (z is padded to the min width 3).
        var table = Text.MarkdownTable("a\tb\nx,y\tz");
        Assert.Contains("| x,y | z", table);
    }
}

public class PhysicsTests
{
    public PhysicsTests() => Strings.Culture = CultureInfo.GetCultureInfo("en");

    [Fact]
    public void Solve_ComputesTheMissingQuantity()
    {
        // distance + time → speed
        var speed = Physics.Solve("100km 2h");
        Assert.Contains("50 km/h", speed);
        Assert.Contains("100 km", speed);

        // speed + time → distance
        Assert.Contains("180 km", Physics.Solve("60km/h 3h"));

        // distance + speed → time
        Assert.Contains("2h", Physics.Solve("100km 50km/h"));
    }

    [Fact]
    public void Solve_MixesUnits()
    {
        // 90 min at 60 km/h = 90 km.
        Assert.Contains("90 km", Physics.Solve("60km/h 90min"));
        // 1 mile in 4 minutes ≈ 24 km/h... check it's in the right ballpark.
        Assert.Contains("km/h", Physics.Solve("1mi 4min"));
    }

    [Fact]
    public void Solve_RejectsBadInput()
    {
        Assert.Throws<ArgumentException>(() => Physics.Solve("100km"));          // only one value
        Assert.Throws<ArgumentException>(() => Physics.Solve("100km 50km"));     // two of the same kind
        Assert.Throws<ArgumentException>(() => Physics.Solve("100km 0km/h"));    // divide by zero
        Assert.Throws<ArgumentException>(() => Physics.Solve("100xyz 2h"));      // unknown unit
    }
}
