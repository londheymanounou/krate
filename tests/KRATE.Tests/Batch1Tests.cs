using System.Globalization;
using Krate.Core;
using Xunit;

public class ColorBlindTests
{
    public ColorBlindTests() => Strings.Culture = CultureInfo.GetCultureInfo("en");

    [Fact]
    public void Simulate_TransformsByTheCvdMatrix()
    {
        // Pure red under protanopia: R≈0.567·255, G≈0.558·255, B=0.
        Assert.Equal((145, 142, 0), Colors.SimulateProtanopia((255, 0, 0)));
        // Grey is unchanged by any CVD simulation (matrix rows sum to 1).
        Assert.Equal((128, 128, 128), Colors.SimulateDeuteranopia((128, 128, 128)));
        Assert.Equal((100, 100, 100), Colors.SimulateTritanopia((100, 100, 100)));
    }

    [Fact]
    public void ColorBlind_ReportsEveryTypePlusGreyscale()
    {
        var report = Colors.ColorBlind("#ff0000");
        Assert.Contains("#FF0000", report);   // normal
        Assert.Contains("#918E00", report);   // protanopia of red (145,142,0)
        Assert.Contains("#4C4C4C", report);   // achromatopsia: 0.299·255 = 76 → #4C
    }
}

public class BarcodeTests
{
    public BarcodeTests() => Strings.Culture = CultureInfo.GetCultureInfo("en");

    [Fact]
    public void Checksum_And_Symbols_MatchTheCode128Spec()
    {
        // "A" → value 33; checksum = (104 + 33·1) mod 103 = 34.
        Assert.Equal(34, Barcode.Checksum("A"));
        Assert.Equal([104, 33, 34, 106], Barcode.Symbols("A"));

        // "CODE128": data values 35,47,36,37,17,18,24; checksum = 850 mod 103 = 26.
        Assert.Equal([104, 35, 47, 36, 37, 17, 18, 24, 26, 106], Barcode.Symbols("CODE128"));
    }

    [Fact]
    public void Code128_RendersBarsWithQuietZones_AndRejectsNonAscii()
    {
        var bars = Barcode.Code128("A");
        var rows = bars.Split('\n');
        Assert.Equal(4, rows.Length);                 // has height
        Assert.All(rows, r => Assert.Equal(rows[0], r)); // every row identical
        Assert.StartsWith("          ", rows[0]);      // 10-module quiet zone
        Assert.Contains('█', rows[0]);
        Assert.Throws<ArgumentException>(() => Barcode.Code128("café"));  // é is non-ASCII
        Assert.Throws<ArgumentException>(() => Barcode.Code128(""));
    }
}
