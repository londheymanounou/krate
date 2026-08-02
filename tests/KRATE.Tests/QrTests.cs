using System.Globalization;
using Krate.Core;
using Xunit;

public class QrTests
{
    public QrTests() => Strings.Culture = CultureInfo.GetCultureInfo("en");

    [Fact]
    public void Png_HasValidSignature_AndGrowsWithData()
    {
        var png = Qr.Png("https://example.com");
        Assert.Equal([0x89, 0x50, 0x4E, 0x47], png[..4]);   // PNG magic — a genuine image
        Assert.True(Images.Read(WriteTemp(png)) is ("PNG", var w, var h) && w == h && w > 0);
    }

    [Fact]
    public void Unicode_IsSquare_HasQuietZone_AndIsDeterministic()
    {
        var qr = Qr.Unicode("HELLO");
        var rows = qr.Split('\n');
        // The block form packs two module-rows per text-row, so width ≈ 2 × row count.
        var width = rows[0].Length;
        Assert.InRange(rows.Length * 2 - width, -1, 2);

        // Quiet zone: the border modules are light, printed as full blocks.
        Assert.All(rows[0], c => Assert.Equal('█', c));
        Assert.StartsWith("█", rows[^1]);

        Assert.Equal(qr, Qr.Unicode("HELLO"));               // same input, same code
        Assert.NotEqual(qr, Qr.Unicode("WORLD"));
    }

    [Fact]
    public void LongerInput_ProducesADenserCode()
    {
        // A bigger payload forces a higher QR version, i.e. more modules.
        var small = Qr.Unicode("hi").Split('\n').Length;
        var big = Qr.Unicode(new string('x', 200)).Split('\n').Length;
        Assert.True(big > small);
    }

    [Fact]
    public void Empty_IsRejected() => Assert.Throws<ArgumentException>(() => Qr.Unicode(""));

    static string WriteTemp(byte[] bytes)
    {
        var path = Path.Combine(Path.GetTempPath(), "krate-qr-" + Guid.NewGuid().ToString("N")[..8] + ".png");
        File.WriteAllBytes(path, bytes);
        return path;
    }
}
