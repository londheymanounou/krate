using System.Globalization;
using System.Text;
using Krate.Core;
using Xunit;

public class ZalgoTests
{
    public ZalgoTests() => Strings.Culture = CultureInfo.GetCultureInfo("en");

    [Fact]
    public void Zalgo_AddsMarks_ButKeepsTheOriginalLetters()
    {
        var z = Text.Zalgo("hello");
        Assert.True(z.Length > "hello".Length);            // marks were added
        // Stripping the combining marks (NFD → drop NonSpacingMark) must give the original text back.
        var stripped = new string(z.Normalize(NormalizationForm.FormD)
            .Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            .ToArray());
        Assert.Equal("hello", stripped);
        Assert.Throws<ArgumentException>(() => Text.Zalgo(""));
    }
}

public class ShoeTests
{
    public ShoeTests() => Strings.Culture = CultureInfo.GetCultureInfo("en");

    [Fact]
    public void Shoe_ConvertsFromEuAcrossAllSystems()
    {
        var r = Sizes.Shoe("42 eu");
        Assert.Contains("Men's", r);
        Assert.Contains("EU  42", r);
        Assert.Contains("US  8.5", r);
        Assert.Contains("CM  26.7", r);
    }

    [Fact]
    public void Shoe_ReadsTheInputSystemAndGender()
    {
        // 26 cm should land on the EU 41 men's row.
        Assert.Contains("EU  41", Sizes.Shoe("26 cm"));
        // Women's flag switches the table.
        Assert.Contains("Women's", Sizes.Shoe("38 eu w"));
        Assert.Throws<ArgumentException>(() => Sizes.Shoe("eu"));
    }
}
